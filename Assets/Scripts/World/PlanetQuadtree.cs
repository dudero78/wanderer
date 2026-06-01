using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Terreno del pianeta a LOD con quadtree (chunked LOD su cubo-sfera). Ogni faccia del cubo è
/// la radice di un albero; un nodo è una patch quadrata del dominio parametrico (tx,ty)∈[0,1]²
/// della faccia. Il nodo si suddivide in 4 figli quando la camera è abbastanza vicina, così la
/// densità di vertici segue la distanza: fitto sotto i piedi, rado all'orizzonte.
///
/// Fondamenti che rendono tutto robusto:
///  - La forma viene SEMPRE da PlanetTerrain.SampleHeight (CPU), la stessa che usa il walker:
///    ogni LOD campiona la stessa funzione → niente divergenze mesh/collisione, e il giocatore
///    (vincolo analitico) non è MAI toccato dal cambio di LOD.
///  - Normali per DIFFERENZA FINITA dai vicini già campionati (niente SampleHeight extra): ~5×
///    meno rumore per nodo, e seamless (i vicini sono condivisi tra nodi adiacenti).
///  - Crepe tra LOD diversi nascoste con SKIRT (bordino abbassato): robusto, niente stitching.
///  - HORIZON CULLING: i nodi oltre l'orizzonte (occlusi dalla curvatura) non si suddividono.
///  - BUILD ASINCRONA: il rumore (matematica pura, thread-safe) gira su thread in background; il
///    main thread fa solo l'upload della mesh (throttlato). Il calcolo non blocca mai il frame:
///    è ciò che tiene fluido anche volando basso e veloce. Il nodo grosso resta visibile finché
///    i 4 figli non sono PRONTI tutti insieme → niente buchi né sovrapposizioni durante la build.
///
/// La floating origin tiene il pianeta vicino all'origine, quindi le posizioni locali dei nodi
/// restano in float preciso anche su pianeti da km.
/// </summary>
public class PlanetQuadtree : MonoBehaviour
{
    PlanetTerrain terrain;
    Material[] faceMaterials;
    Transform cam;
    QuadNode[] roots;

    // --- manopole di LOD ---
    [Tooltip("Vertici per lato di un nodo (32 = 33x33). Nodi più grossi = meno draw call.")]
    public int nodeRes = 32;
    [Tooltip("Un nodo si suddivide se la camera è più vicina di worldSize * questo fattore.")]
    public float lodFactor = 6f;
    [Tooltip("Il merge avviene a splitDist * questo (>1): banda morta → niente flicker.")]
    public float mergeHysteresis = 1.4f;
    [Tooltip("Profondità massima dell'albero.")]
    public int maxDepth = 7;
    [Tooltip("Profondità dello skirt come frazione del nodo: nasconde le crepe.")]
    public float skirtFactor = 0.15f;

    // --- build asincrona ---
    [Tooltip("Massimo di build del rumore in volo su thread in parallelo. Più basso = meno calore "
           + "CPU volando (riempimento un filo più lento, ma è async → non blocca il frame).")]
    public int maxConcurrentBuilds = 6;
    [Tooltip("Massimo di mesh create (upload) per frame sul main thread: evita l'hitch.")]
    public int finalizeBudgetPerFrame = 8;
    [Tooltip("Massima coda di nodi in attesa di build: backpressure contro le esplosioni di split.")]
    public int maxQueued = 96;

    readonly Queue<QuadNode> buildQueue = new Queue<QuadNode>();            // main thread
    public readonly ConcurrentQueue<QuadNode> finalizeQueue = new ConcurrentQueue<QuadNode>();  // riempita dai thread
    public int inFlight;   // build attive su thread (Interlocked)

    public void Init(PlanetTerrain terrain, Material[] faceMaterials, Transform cam)
    {
        this.terrain = terrain;
        this.faceMaterials = faceMaterials;
        this.cam = cam;

        roots = new QuadNode[6];
        for (int f = 0; f < 6; f++)
        {
            var mat = (faceMaterials != null && f < faceMaterials.Length) ? faceMaterials[f] : null;
            roots[f] = new QuadNode(this, f, PlanetMeshBuilder.FaceNormals[f], 0f, 0f, 1f, 0, mat);
            // le radici si costruiscono SUBITO (sincrone, sono solo 6): pianeta grezzo immediato,
            // poi il dettaglio si infittisce in background.
            roots[f].ComputeData();
            roots[f].FinalizeMesh();
            roots[f].SetVisible(true);
        }
    }

    public PlanetTerrain Terrain => terrain;
    public Transform Root => transform;

    public bool CanQueueBuild() => buildQueue.Count < maxQueued;
    public void EnqueueBuild(QuadNode n) { buildQueue.Enqueue(n); }

    void Update()
    {
        if (roots == null) return;
        if (cam == null)
        {
            var c = Camera.main;
            if (c == null) return;
            cam = c.transform;
        }

        Vector3 camPos = cam.position;

        // 1) decisione di LOD: split/merge, visibilità. Gli split accodano i figli alla buildQueue.
        for (int f = 0; f < 6; f++) roots[f].UpdateLod(camPos);

        // 2) avvia le build dei nodi in coda, fino al tetto di concorrenza.
        while (inFlight < maxConcurrentBuilds && buildQueue.Count > 0)
        {
            var n = buildQueue.Dequeue();
            if (n == null || n.Disposed || n.State != QuadNode.BuildState.Queued) continue;
            Interlocked.Increment(ref inFlight);
            n.StartBuildAsync();
        }

        // 3) finalizza (upload mesh) i nodi col calcolo finito, fino al budget per frame.
        int done = 0;
        while (done < finalizeBudgetPerFrame && finalizeQueue.TryDequeue(out var n))
        {
            if (n == null || n.Disposed) { n?.DropData(); continue; }
            n.FinalizeMesh();
            done++;
        }
    }
}

/// <summary>Un nodo del quadtree: una patch [u0,u0+size]×[v0,v0+size] del dominio (tx,ty) di una faccia.</summary>
public class QuadNode
{
    public enum BuildState { Idle, Queued, Building, Ready }

    readonly PlanetQuadtree qt;
    readonly int face;
    readonly Vector3 up, axisA, axisB;
    readonly float u0, v0, size;
    readonly int depth;
    readonly Material material;

    QuadNode[] children;
    GameObject go;
    Mesh mesh;

    Vector3 centerLocal;
    float worldSize;
    bool flip;

    public BuildState State { get; private set; } = BuildState.Idle;
    public bool Disposed { get; private set; }

    // dati mesh calcolati su thread, consumati (upload) sul main thread
    Vector3[] dVerts, dNormals;
    Vector4[] dTangents;
    Vector2[] dUVs;
    int[] dTris;

    public QuadNode(PlanetQuadtree qt, int face, Vector3 up, float u0, float v0, float size, int depth, Material material)
    {
        this.qt = qt;
        this.face = face;
        this.up = up;
        this.u0 = u0; this.v0 = v0; this.size = size;
        this.depth = depth;
        this.material = material;
        PlanetMeshBuilder.FaceAxes(up, out axisA, out axisB);
        ComputeBounds();
    }

    void ComputeBounds()
    {
        var terr = qt.Terrain;
        Vector3 c00 = CornerPos(u0, v0);
        Vector3 c11 = CornerPos(u0 + size, v0 + size);
        worldSize = Vector3.Distance(c00, c11);
        Vector3 dirC = PlanetMeshBuilder.ParamToDir(up, axisA, axisB, u0 + size * 0.5f, v0 + size * 0.5f);
        centerLocal = dirC * terr.SampleHeight(dirC);

        Vector3 c01 = CornerPos(u0, v0 + size);
        Vector3 faceNrm = Vector3.Cross(c01 - c00, c11 - c00);
        flip = Vector3.Dot(faceNrm, c00) < 0f;
    }

    Vector3 CornerPos(float tx, float ty)
    {
        Vector3 dir = PlanetMeshBuilder.ParamToDir(up, axisA, axisB, tx, ty);
        return dir * qt.Terrain.SampleHeight(dir);
    }

    bool AllChildrenReady()
    {
        if (children == null) return false;
        for (int i = 0; i < 4; i++) if (children[i].State != BuildState.Ready) return false;
        return true;
    }

    public void SetVisible(bool v) { if (go != null) go.SetActive(v); }

    /// <summary>Decisione di LOD + visibilità. Il calcolo della mesh avviene altrove (async).</summary>
    public void UpdateLod(Vector3 camPos)
    {
        Vector3 centerWorld = qt.Root.TransformPoint(centerLocal);

        // HORIZON CULLING (gate depth>=2: i nodi-faccia grossi hanno il centro lontano anche se un
        // bordo è vicino → il test sul centro li culerebbe per sbaglio).
        if (depth >= 2 && IsBeyondHorizon(camPos, centerWorld))
        {
            if (children != null) Merge();
            SetVisible(true);
            return;
        }

        float dist = Vector3.Distance(camPos, centerWorld);
        float splitDist = worldSize * qt.lodFactor;

        if (children != null)
        {
            if (dist > splitDist * qt.mergeHysteresis)
            {
                Merge();
                SetVisible(true);
            }
            else if (AllChildrenReady())
            {
                // i 4 figli sono pronti: nascondi questo nodo e mostra/aggiorna i figli.
                SetVisible(false);
                for (int i = 0; i < 4; i++) children[i].UpdateLod(camPos);
            }
            else
            {
                // figli ancora in costruzione: questo nodo COPRE (niente buchi né overlap).
                SetVisible(true);
            }
        }
        else
        {
            SetVisible(true);
            if (State == BuildState.Ready && depth < qt.maxDepth && dist < splitDist && qt.CanQueueBuild())
                Split();
        }
    }

    /// <summary>Il nodo è oltre l'orizzonte (occluso dalla curvatura)?</summary>
    bool IsBeyondHorizon(Vector3 camPos, Vector3 nodeWorld)
    {
        Vector3 center = qt.Root.position;
        float R = qt.Terrain.BaseRadius;
        Vector3 camR = camPos - center;
        float camDist = camR.magnitude;
        if (camDist < R * 1.001f) return false;

        Vector3 camDir = camR / camDist;
        Vector3 nodeDir = (nodeWorld - center).normalized;
        float cosHorizon = R / camDist;
        float margin = 0.12f + worldSize / R;
        return Vector3.Dot(camDir, nodeDir) < cosHorizon - margin;
    }

    void Split()
    {
        float h = size * 0.5f;
        children = new QuadNode[4];
        children[0] = new QuadNode(qt, face, up, u0,     v0,     h, depth + 1, material);
        children[1] = new QuadNode(qt, face, up, u0 + h, v0,     h, depth + 1, material);
        children[2] = new QuadNode(qt, face, up, u0,     v0 + h, h, depth + 1, material);
        children[3] = new QuadNode(qt, face, up, u0 + h, v0 + h, h, depth + 1, material);
        // i figli si costruiranno in background: accodali. Questo nodo resta visibile finché
        // non sono pronti tutti e quattro (la visibilità la gestisce UpdateLod).
        for (int i = 0; i < 4; i++)
        {
            children[i].State = BuildState.Queued;
            qt.EnqueueBuild(children[i]);
        }
    }

    void Merge()
    {
        for (int i = 0; i < 4; i++) { children[i].Dispose(); children[i] = null; }
        children = null;
    }

    public void Dispose()
    {
        Disposed = true;
        if (children != null)
        {
            for (int i = 0; i < 4; i++) { children[i].Dispose(); children[i] = null; }
            children = null;
        }
        if (go != null) Object.Destroy(go);
        if (mesh != null) Object.Destroy(mesh);
        go = null; mesh = null;
        DropData();
    }

    public void DropData() { dVerts = null; dNormals = null; dTangents = null; dUVs = null; dTris = null; }

    /// <summary>Avvia il calcolo della mesh su un thread del pool. Solo matematica thread-safe.</summary>
    public void StartBuildAsync()
    {
        State = BuildState.Building;
        Task.Run(() =>
        {
            try { ComputeData(); }
            catch { /* nodo scartato nel frattempo: ignora */ }
            Interlocked.Decrement(ref qt.inFlight);
            qt.finalizeQueue.Enqueue(this);
        });
    }

    /// <summary>
    /// Calcola vertici/normali/tangenti/uv/triangoli della patch (griglia + skirt). THREAD-SAFE:
    /// usa solo math e letture di campi immutabili (niente API Unity). È il lavoro pesante (rumore).
    /// </summary>
    public void ComputeData()
    {
        var terr = qt.Terrain;
        int R = qt.nodeRes;
        int n = R + 1;
        float step = size / R;

        // griglia ESTESA (bordo di 1 vertice per lato) per le normali da differenza finita.
        int ne = n + 2;
        var P = new Vector3[ne * ne];
        for (int y = 0; y < ne; y++)
            for (int x = 0; x < ne; x++)
            {
                float tx = u0 + (x - 1) * step;
                float ty = v0 + (y - 1) * step;
                Vector3 dir = PlanetMeshBuilder.ParamToDir(up, axisA, axisB, tx, ty);
                P[x + y * ne] = dir * terr.SampleHeight(dir);
            }

        var verts = new List<Vector3>(n * n + 4 * n);
        var normals = new List<Vector3>(n * n + 4 * n);
        var tangents = new List<Vector4>(n * n + 4 * n);
        var uvs = new List<Vector2>(n * n + 4 * n);

        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int ex = x + 1, ey = y + 1;
                Vector3 p = P[ex + ey * ne];
                Vector3 dxv = P[(ex + 1) + ey * ne] - P[(ex - 1) + ey * ne];
                Vector3 dyv = P[ex + (ey + 1) * ne] - P[ex + (ey - 1) * ne];
                Vector3 dir = p.normalized;
                Vector3 nrm = Vector3.Cross(dyv, dxv).normalized;
                if (Vector3.Dot(nrm, dir) < 0f) nrm = -nrm;
                verts.Add(p);
                normals.Add(nrm);
                Vector3 refV = Mathf.Abs(nrm.y) < 0.99f ? Vector3.up : Vector3.right;
                Vector3 tan = Vector3.Normalize(Vector3.Cross(refV, nrm));
                tangents.Add(new Vector4(tan.x, tan.y, tan.z, 1f));
                uvs.Add(new Vector2(u0 + x * step, v0 + y * step));
            }

        var tris = new List<int>(R * R * 6 + R * 4 * 6);
        for (int y = 0; y < R; y++)
            for (int x = 0; x < R; x++)
            {
                int i = x + y * n;
                if (!flip)
                {
                    tris.Add(i); tris.Add(i + n); tris.Add(i + n + 1);
                    tris.Add(i); tris.Add(i + n + 1); tris.Add(i + 1);
                }
                else
                {
                    tris.Add(i); tris.Add(i + n + 1); tris.Add(i + n);
                    tris.Add(i); tris.Add(i + 1); tris.Add(i + n + 1);
                }
            }

        // skirt: anello di bordo abbassato, nasconde le crepe coi vicini a LOD diverso.
        float skirtDrop = Mathf.Max(0.25f, worldSize * qt.skirtFactor);
        var ring = new List<int>(4 * R);
        for (int x = 0; x < R; x++) ring.Add(x);
        for (int y = 0; y < R; y++) ring.Add(y * n + (n - 1));
        for (int x = R; x > 0; x--) ring.Add((n - 1) * n + x);
        for (int y = R; y > 0; y--) ring.Add(y * n);

        int skirtStart = verts.Count;
        for (int k = 0; k < ring.Count; k++)
        {
            int ci = ring[k];
            Vector3 v = verts[ci];
            Vector3 dir = v.normalized;
            verts.Add(v - dir * skirtDrop);
            normals.Add(normals[ci]);
            tangents.Add(tangents[ci]);
            uvs.Add(uvs[ci]);
        }
        int rc = ring.Count;
        for (int k = 0; k < rc; k++)
        {
            int a = ring[k];
            int b = ring[(k + 1) % rc];
            int as_ = skirtStart + k;
            int bs = skirtStart + (k + 1) % rc;
            if (!flip)
            {
                tris.Add(a); tris.Add(as_); tris.Add(bs);
                tris.Add(a); tris.Add(bs); tris.Add(b);
            }
            else
            {
                tris.Add(a); tris.Add(bs); tris.Add(as_);
                tris.Add(a); tris.Add(b); tris.Add(bs);
            }
        }

        dVerts = verts.ToArray();
        dNormals = normals.ToArray();
        dTangents = tangents.ToArray();
        dUVs = uvs.ToArray();
        dTris = tris.ToArray();
    }

    /// <summary>Crea la Mesh e il GameObject dai dati calcolati. SOLO main thread (API Unity).</summary>
    public void FinalizeMesh()
    {
        if (Disposed || dVerts == null) { DropData(); return; }

        mesh = new Mesh { name = $"QNode_f{face}_d{depth}" };
        if (dVerts.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = dVerts;
        mesh.normals = dNormals;
        mesh.tangents = dTangents;
        mesh.uv = dUVs;
        mesh.triangles = dTris;
        mesh.RecalculateBounds();
        DropData();

        go = new GameObject($"QNode_f{face}_d{depth}");
        go.transform.SetParent(qt.Root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        go.SetActive(false);   // la visibilità la decide UpdateLod (il nodo grosso copre finché i 4 non sono pronti)

        State = BuildState.Ready;
    }
}

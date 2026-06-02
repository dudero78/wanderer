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
    [Tooltip("Un nodo si suddivide se la camera è più vicina di worldSize * questo fattore. "
           + "La GEOMETRIA fa solo la FORMA: il dettaglio fine lo porta la texture adattiva dello "
           + "shader (economica). Quindi LOD moderato = meno nodi, meno calore, caricamento più "
           + "veloce, e la superficie resta comunque nitida (microscopio).")]
    public float lodFactor = 6f;
    [Tooltip("Il merge avviene a splitDist * questo (>1): banda morta → niente flicker.")]
    public float mergeHysteresis = 1.4f;
    [Tooltip("Profondità massima dell'albero.")]
    public int maxDepth = 7;
    [Tooltip("Profondità dello skirt come frazione del nodo: nasconde le crepe tra LOD diversi.")]
    public float skirtFactor = 0.7f;

    // --- build asincrona ---
    [Tooltip("Massimo di build del rumore in volo su thread in parallelo. Più basso = meno calore "
           + "CPU volando (riempimento un filo più lento, ma è async → non blocca il frame).")]
    public int maxConcurrentBuilds = 5;
    [Tooltip("Massimo di mesh create (upload) per frame sul main thread: evita l'hitch.")]
    public int finalizeBudgetPerFrame = 10;
    [Tooltip("Massima coda di nodi in attesa di build: backpressure contro le esplosioni di split.")]
    public int maxQueued = 96;

    // --- LOD PREDITTIVO ---
    // Volando, i chunk davanti devono caricare PRIMA di arrivarci. Valutiamo lo split da un punto
    // proiettato avanti lungo la velocità della camera: i nodi verso cui vai si suddividono in
    // anticipo (e col geomorph compaiono lisci, poi crescono mentre arrivi). Fermo/lento = nessun
    // effetto. Il morphing nello shader usa la camera VERA, quindi la resa resta coerente.
    [Tooltip("Secondi di anticipo: valuta il LOD da camPos + velocità * questo.")]
    public float lookAheadSeconds = 1.0f;
    [Tooltip("Anticipo massimo in metri (evita di spalancare il LOD a velocità orbitali).")]
    public float maxLookAhead = 150f;
    Vector3 lastCamPos;
    bool hasLastCam;

    readonly Queue<QuadNode> buildQueue = new Queue<QuadNode>();            // main thread
    public readonly ConcurrentQueue<QuadNode> finalizeQueue = new ConcurrentQueue<QuadNode>();  // riempita dai thread
    public int inFlight;   // build attive su thread (Interlocked)

    // --- CACHE DEI NODI ---
    // Una sola rappresentazione continua (niente imposter separato che "salta"). Quando un nodo
    // viene unito (esce di vista salendo o orbitando), la sua mesh NON si butta: va in cache.
    // Quando quella regione torna in vista (riscendi, o completi l'orbita), si RIUSA all'istante
    // invece di ricostruirla. Conseguenze: orbitare e tornare a terra non ricostruiscono nulla
    // → niente calore da rebuild, niente pop, niente crepe transitorie. È così che un motore
    // serio rende fluida l'orbita e l'atterraggio.
    [Tooltip("Quanti nodi-mesh tenere in cache per il riuso. ~600 copre la fascia di dettaglio "
           + "attorno al pianeta a quota d'orbita. Oltre, si scarta il meno usato di recente (LRU).")]
    public int cacheCapacity = 600;
    readonly Dictionary<long, QuadNode> cache = new Dictionary<long, QuadNode>();
    int cacheClock;

    /// <summary>Estrae dalla cache il nodo per quella regione (se c'è), togliendolo: torna "vivo".</summary>
    public bool TryTakeCached(long key, out QuadNode node)
    {
        if (cache.TryGetValue(key, out node)) { cache.Remove(key); return true; }
        node = null;
        return false;
    }

    /// <summary>Mette una FOGLIA pronta in cache (nascosta) invece di distruggerla. LRU se piena.</summary>
    public void CacheLeaf(QuadNode n)
    {
        n.SetVisible(false);
        n.cacheStamp = ++cacheClock;
        if (cache.TryGetValue(n.Key, out var old) && old != n) old.Dispose();  // mai due nodi per regione
        cache[n.Key] = n;
        if (cache.Count > cacheCapacity) EvictLeastRecent();
    }

    void EvictLeastRecent()
    {
        long evictKey = 0; int min = int.MaxValue; bool found = false;
        foreach (var kv in cache)
            if (kv.Value.cacheStamp < min) { min = kv.Value.cacheStamp; evictKey = kv.Key; found = true; }
        if (found) { var ev = cache[evictKey]; cache.Remove(evictKey); ev.Dispose(); }
    }

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

        // punto di valutazione del LOD: proiettato AVANTI lungo la velocità (carica in anticipo).
        Vector3 evalPos = camPos;
        if (hasLastCam && Time.deltaTime > 1e-5f)
        {
            Vector3 vel = (camPos - lastCamPos) / Time.deltaTime;
            evalPos = camPos + Vector3.ClampMagnitude(vel * lookAheadSeconds, maxLookAhead);
        }
        lastCamPos = camPos;
        hasLastCam = true;

        // 1) decisione di LOD: split/merge, visibilità. Gli split accodano i figli alla buildQueue.
        for (int f = 0; f < 6; f++) roots[f].UpdateLod(evalPos);

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

    // identità STABILE della regione (face, depth, indici di griglia): chiave di cache. Due nodi
    // che coprono la stessa patch hanno la stessa Key → riuso al posto della ricostruzione.
    public readonly long Key;
    public int cacheStamp;   // "orologio" d'uso per l'eviction LRU della cache

    // dati mesh calcolati su thread, consumati (upload) sul main thread
    Vector3[] dVerts, dNormals;
    Vector4[] dTangents;
    Vector2[] dUVs;
    Vector4[] dMorph;   // UV2: xyz = spostamento verso la griglia genitore, w = distanza di split
    int[] dTris;

    public QuadNode(PlanetQuadtree qt, int face, Vector3 up, float u0, float v0, float size, int depth, Material material)
    {
        this.qt = qt;
        this.face = face;
        this.up = up;
        this.u0 = u0; this.v0 = v0; this.size = size;
        this.depth = depth;
        this.material = material;
        Key = MakeKey(face, depth, u0, v0);
        PlanetMeshBuilder.FaceAxes(up, out axisA, out axisB);
        ComputeBounds();
    }

    /// <summary>Chiave stabile della patch: face + profondità + indici di griglia interi. u0,v0 sono
    /// sempre multipli di 1/2^depth, quindi gli indici sono esatti (niente errori di float).</summary>
    public static long MakeKey(int face, int depth, float u0, float v0)
    {
        int n = 1 << depth;
        long ix = Mathf.RoundToInt(u0 * n);
        long iy = Mathf.RoundToInt(v0 * n);
        return (long)face | ((long)depth << 3) | (ix << 6) | (iy << 20);
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

    /// <summary>Il nodo è oltre l'orizzonte (occluso dalla curvatura)? Test in ANGOLI: l'angolo del
    /// nodo dalla verticale-camera supera l'angolo dell'orizzonte (più la mezza estensione del nodo
    /// e un filo di margine). La forma a coseno+offset costante sovrastimava enormemente a bassa
    /// quota (calotta da centinaia di metri) → builder a vuoto e cache intasata.</summary>
    bool IsBeyondHorizon(Vector3 camPos, Vector3 nodeWorld)
    {
        Vector3 center = qt.Root.position;
        float R = qt.Terrain.BaseRadius;
        Vector3 camR = camPos - center;
        float camDist = camR.magnitude;
        if (camDist < R * 1.001f) return false;

        Vector3 camDir = camR / camDist;
        Vector3 nodeDir = (nodeWorld - center).normalized;
        float thetaNode = Mathf.Acos(Mathf.Clamp(Vector3.Dot(camDir, nodeDir), -1f, 1f));
        float thetaHorizon = Mathf.Acos(Mathf.Clamp(R / camDist, 0f, 1f));
        float thetaR = (worldSize * 0.5f) / R;   // mezza estensione angolare del nodo
        const float lead = 0.03f;                // margine: non cullare un nodo appena al bordo
        return thetaNode > thetaHorizon + thetaR + lead;
    }

    void Split()
    {
        float h = size * 0.5f;
        children = new QuadNode[4];
        float[] cu = { u0, u0 + h, u0, u0 + h };
        float[] cv = { v0, v0, v0 + h, v0 + h };
        for (int i = 0; i < 4; i++)
        {
            // se quella regione è in cache (già costruita prima, poi uscita di vista) la RIUSO
            // all'istante: niente ricostruzione → niente pop, niente calore, niente crepe transitorie.
            long k = MakeKey(face, depth + 1, cu[i], cv[i]);
            if (qt.TryTakeCached(k, out var cached))
            {
                children[i] = cached;   // è già Ready, con mesh e GameObject pronti (nascosto)
            }
            else
            {
                var child = new QuadNode(qt, face, up, cu[i], cv[i], h, depth + 1, material);
                child.State = BuildState.Queued;
                children[i] = child;
                qt.EnqueueBuild(child);   // questo nodo resta visibile finché i 4 figli non sono pronti
            }
        }
    }

    void Merge()
    {
        for (int i = 0; i < 4; i++)
        {
            var c = children[i];
            // foglia pronta → in cache per il riuso (non si butta). Altrimenti (in costruzione, o
            // con sottoalbero) si distrugge: cacheiamo solo foglie pronte, semplice e corretto.
            if (c.children == null && c.State == BuildState.Ready && !c.Disposed)
                qt.CacheLeaf(c);
            else
                c.Dispose();
            children[i] = null;
        }
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

    public void DropData() { dVerts = null; dNormals = null; dTangents = null; dUVs = null; dMorph = null; dTris = null; }

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
        // GEOMORPH: per ogni vertice salviamo (in UV2) lo SPOSTAMENTO verso la posizione che avrebbe
        // sulla griglia del GENITORE (metà risoluzione) + la distanza di split del nodo (w). Lo shader
        // lo userà per far "nascere" il nodo con la forma del genitore e trasformarlo nel dettaglio
        // fine avvicinandosi → niente pop, niente anello di LOD. I vertici a indice pari sono già
        // sulla griglia del genitore (delta 0); i dispari si interpolano dai pari adiacenti.
        var morph = new List<Vector4>(n * n + 4 * n);
        float splitDist = worldSize * qt.lodFactor;

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

                // posizione sulla griglia del genitore (decimazione ×2): i pari restano, i dispari
                // diventano la media dei due pari adiacenti (lungo l'asse dispari, o la diagonale).
                Vector3 coarse;
                bool ox = (x & 1) == 1, oy = (y & 1) == 1;
                if (!ox && !oy)      coarse = p;
                else if (ox && !oy)  coarse = 0.5f * (P[(ex - 1) + ey * ne] + P[(ex + 1) + ey * ne]);
                else if (!ox && oy)  coarse = 0.5f * (P[ex + (ey - 1) * ne] + P[ex + (ey + 1) * ne]);
                else                 coarse = 0.5f * (P[(ex - 1) + (ey - 1) * ne] + P[(ex + 1) + (ey + 1) * ne]);
                Vector3 d = coarse - p;
                morph.Add(new Vector4(d.x, d.y, d.z, splitDist));
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

        // skirt: anello di bordo abbassato, nasconde le crepe coi vicini a LOD diverso. La
        // profondità deve coprire il MASSIMO dislivello possibile tra due LOD adiacenti, limitato
        // dall'ampiezza del terreno: quindi scala col nodo ma con pavimento e tetto sull'ampiezza
        // (sotto era troppo corto sui nodi fini → le crepe nere che inquietavano).
        float skirtDrop = Mathf.Clamp(worldSize * qt.skirtFactor, 3f, terr.Amplitude);
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
            // normale RADIALE (verso l'alto), non quella del bordo: così lo skirt si illumina come terreno
            // piatto invece che come parete verticale → niente lametta scura ai confini di LOD.
            normals.Add(dir);
            tangents.Add(tangents[ci]);
            uvs.Add(uvs[ci]);
            // lo skirt MORFA COL bordo (stesso delta del vertice di bordo): resta attaccato alla superficie
            // mentre questa morfa. Con morph 0 il bordo si spostava e si apriva una crepa SOPRA lo skirt.
            morph.Add(morph[ci]);
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
        dMorph = morph.ToArray();
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
        if (dMorph != null) mesh.SetUVs(1, new List<Vector4>(dMorph));   // geomorph (UV2)
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

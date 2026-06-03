using UnityEngine;

/// <summary>
/// Costruisce la mesh di un pianeta come "cube-sphere": sei facce di una griglia,
/// proiettate sulla sfera e spostate in altezza dal PlanetTerrain. Ogni faccia è una
/// mesh figlia del pianeta. Questo è lo standard per pianeti walkable; più avanti
/// diventerà a LOD (quadtree) per coprire scale enormi senza milioni di vertici.
/// </summary>
public static class PlanetMeshBuilder
{
    // Le sei facce del cubo-sfera, in ordine fisso. Pubblico: il quadtree indicizza le radici
    // con lo stesso ordine, così la faccia f del quadtree è la faccia f del bake → UV coerenti.
    public static readonly Vector3[] FaceNormals =
    {
        Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back
    };

    /// <summary>
    /// Assi tangenti di una faccia del cubo. Stessa formula usata ovunque (mesh uniforme, bake,
    /// quadtree): è ciò che garantisce che parametri (tx,ty) → direzione → UV combacino tra i
    /// sistemi. Cambiarla in un solo posto romperebbe l'allineamento col rilievo bakeato.
    /// </summary>
    public static void FaceAxes(Vector3 localUp, out Vector3 axisA, out Vector3 axisB)
    {
        axisA = new Vector3(localUp.y, localUp.z, localUp.x);
        axisB = Vector3.Cross(localUp, axisA);
    }

    /// <summary>Parametri (tx,ty)∈[0,1]² di una faccia → direzione unitaria sulla sfera.</summary>
    public static Vector3 ParamToDir(Vector3 localUp, Vector3 axisA, Vector3 axisB, float tx, float ty)
    {
        Vector3 pointOnCube = localUp + (tx - 0.5f) * 2f * axisA + (ty - 0.5f) * 2f * axisB;
        return pointOnCube.normalized;
    }

    /// <summary>
    /// Inverso di ParamToDir: una direzione unitaria → faccia del cubo + parametri (tx,ty)∈[0,1]².
    /// La faccia è quella verso cui punta la direzione (proiezione massima); poi si riportano gli
    /// assi tangenti in parametri. Usato per trovare il vicino di un nodo attraverso un bordo (anche
    /// fra facce diverse) senza transform manuali: si campiona la direzione appena oltre il bordo e
    /// la si reinterpreta qui. Coerente al 100% con ParamToDir perché usa gli stessi FaceAxes.
    /// </summary>
    public static void DirToFaceParam(Vector3 dir, out int face, out float tx, out float ty)
    {
        face = 0; float best = -2f;
        for (int f = 0; f < 6; f++)
        {
            float d = Vector3.Dot(dir, FaceNormals[f]);
            if (d > best) { best = d; face = f; }
        }
        Vector3 localUp = FaceNormals[face];
        FaceAxes(localUp, out var axisA, out var axisB);
        float dn = Vector3.Dot(dir, localUp);
        if (dn < 1e-6f) dn = 1e-6f;                 // la direzione punta verso la faccia: dn > 0
        float s = 1f / dn;                          // scala la dir sul piano della faccia del cubo
        tx = Mathf.Clamp01(0.5f + 0.5f * s * Vector3.Dot(dir, axisA));
        ty = Mathf.Clamp01(0.5f + 0.5f * s * Vector3.Dot(dir, axisB));
    }

    public static void Build(Transform parent, PlanetTerrain terrain, int resolution, Material mat)
    {
        foreach (var normal in FaceNormals)
        {
            var mesh = BuildFaceMesh(normal, terrain, resolution);
            var go = new GameObject("Face");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }

    public static Mesh BuildFaceMesh(Vector3 localUp, PlanetTerrain terrain, int res)
    {
        ComputeFaceData(localUp, terrain, res, out var verts, out var normals, out var tangents, out var uvs, out var tris);
        return CreateMesh(verts, normals, tangents, uvs, tris);
    }

    /// <summary>
    /// Calcola i dati della mesh-faccia (vertici/normali/tangenti/uv/triangoli). THREAD-SAFE: solo
    /// matematica (SampleHeight/SurfaceNormal sono pure), niente API Unity. Così le 6 facce si
    /// costruiscono in parallelo su thread e il main thread fa solo l'upload (CreateMesh) → niente
    /// freeze di caricamento. La pipeline dei layer va costruita PRIMA sul main thread (RebuildLayers).
    /// </summary>
    public static void ComputeFaceData(Vector3 localUp, PlanetTerrain terrain, int res,
        out Vector3[] verts, out Vector3[] normals, out Vector4[] tangents, out Vector2[] uvs, out int[] tris)
    {
        Vector3 axisA, axisB;
        FaceAxes(localUp, out axisA, out axisB);

        verts = new Vector3[res * res];
        normals = new Vector3[res * res];
        tangents = new Vector4[res * res];
        uvs = new Vector2[res * res];   // (tx,ty) in [0,1]²: indirizza la texture di rilievo bakeata
        tris = new int[(res - 1) * (res - 1) * 6];
        int ti = 0;
        float eps = 2f / (res - 1);   // passo per la differenza centrale (~ una cella di griglia)

        // SKIRT anti-cucitura: le facce dovrebbero condividere i vertici di bordo (stessa dir → stessa
        // SampleHeight), ma micro-differenze di arrotondamento aprono T-junction ai seam → a quota bassa, ad
        // angolo radente, si vedono come fessure nere (lo spazio dietro). Rimedio robusto e standard: estendo il
        // dominio di UNA cella oltre il quadrante e ABBASSO l'anello esterno → quel lembo si infila SOTTO la
        // superficie della faccia vicina e sigilla la fessura, senza toccare la superficie calpestabile (l'anello
        // interno resta a piena quota proprio sul bordo). Nessun triangolo/winding nuovo: solo posizioni.
        float margin = 1f / (res - 1);                         // ~1 cella di sovrapposizione nel vicino
        // profondità del lembo nascosto: proporzionata al RILIEVO totale del corpo (non solo all'ampiezza base),
        // o con scogliere/catene tettoniche da decine di metri il lembo da 2 m non copre e si vedono i lembi
        // strappati ai bordi delle facce. (Solo anteprima/proxy: il gioco usa il quadtree, con skirt suo.)
        float skirt = Mathf.Clamp(terrain.ReliefEstimate() * 0.5f, 2f, terrain.BaseRadius * 0.25f);

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                int i = x + y * res;
                float u = x / (float)(res - 1);
                float v = y / (float)(res - 1);
                float tx = -margin + u * (1f + 2f * margin);   // dominio [-margin, 1+margin]
                float ty = -margin + v * (1f + 2f * margin);
                Vector3 pointOnCube = localUp + (tx - 0.5f) * 2f * axisA + (ty - 0.5f) * 2f * axisB;
                Vector3 dir = pointOnCube.normalized;
                bool flange = x == 0 || x == res - 1 || y == 0 || y == res - 1;   // anello esterno = lembo nascosto
                float h = terrain.SampleHeight(dir) - (flange ? skirt : 0f);
                verts[i] = dir * h;
                Vector3 nrm = terrain.SurfaceNormal(dir, eps);
                normals[i] = nrm;
                // tangente arbitraria perpendicolare alla normale: serve allo shader per
                // applicare le normali di dettaglio (la sua orientazione non conta, è procedurale)
                Vector3 refV = Mathf.Abs(nrm.y) < 0.99f ? Vector3.up : Vector3.right;
                Vector3 tan = Vector3.Normalize(Vector3.Cross(refV, nrm));
                tangents[i] = new Vector4(tan.x, tan.y, tan.z, 1f);
                uvs[i] = new Vector2(Mathf.Clamp01(tx), Mathf.Clamp01(ty));   // [0,1] per il bake (il lembo va appena oltre)

                if (x < res - 1 && y < res - 1)
                {
                    tris[ti++] = i;
                    tris[ti++] = i + res;
                    tris[ti++] = i + res + 1;
                    tris[ti++] = i;
                    tris[ti++] = i + res + 1;
                    tris[ti++] = i + 1;
                }
            }
        }

        // garantisce che le facce guardino verso l'esterno, qualunque sia l'orientamento
        // degli assi: niente facce invertite (buchi) sulla sfera.
        Vector3 c = (verts[tris[0]] + verts[tris[1]] + verts[tris[2]]) / 3f;
        Vector3 faceNrm = Vector3.Cross(verts[tris[1]] - verts[tris[0]], verts[tris[2]] - verts[tris[0]]);
        if (Vector3.Dot(faceNrm, c) < 0f) System.Array.Reverse(tris);
    }

    /// <summary>Crea la Mesh dai dati calcolati. SOLO main thread (API Unity).</summary>
    public static Mesh CreateMesh(Vector3[] verts, Vector3[] normals, Vector4[] tangents, Vector2[] uvs, int[] tris)
    {
        var mesh = new Mesh();
        if (verts.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts;
        mesh.normals = normals;   // normali analitiche, continue tra le facce: niente cuciture
        mesh.tangents = tangents; // per le normali di dettaglio dello shader
        mesh.uv = uvs;            // indirizza la texture di rilievo bakeata, per faccia
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }
}

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
        Vector3 axisA, axisB;
        FaceAxes(localUp, out axisA, out axisB);

        var verts = new Vector3[res * res];
        var normals = new Vector3[res * res];
        var tangents = new Vector4[res * res];
        var uvs = new Vector2[res * res];   // (tx,ty) in [0,1]²: indirizza la texture di rilievo bakeata
        var tris = new int[(res - 1) * (res - 1) * 6];
        int ti = 0;
        float eps = 2f / (res - 1);   // passo per la differenza centrale (~ una cella di griglia)

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                int i = x + y * res;
                float tx = x / (float)(res - 1);
                float ty = y / (float)(res - 1);
                Vector3 pointOnCube = localUp + (tx - 0.5f) * 2f * axisA + (ty - 0.5f) * 2f * axisB;
                Vector3 dir = pointOnCube.normalized;
                verts[i] = dir * terrain.SampleHeight(dir);
                Vector3 nrm = terrain.SurfaceNormal(dir, eps);
                normals[i] = nrm;
                // tangente arbitraria perpendicolare alla normale: serve allo shader per
                // applicare le normali di dettaglio (la sua orientazione non conta, è procedurale)
                Vector3 refV = Mathf.Abs(nrm.y) < 0.99f ? Vector3.up : Vector3.right;
                Vector3 tan = Vector3.Normalize(Vector3.Cross(refV, nrm));
                tangents[i] = new Vector4(tan.x, tan.y, tan.z, 1f);
                uvs[i] = new Vector2(tx, ty);   // la stessa parametrizzazione usata dal bake

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

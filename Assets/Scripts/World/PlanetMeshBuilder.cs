using UnityEngine;

/// <summary>
/// Costruisce la mesh di un pianeta come "cube-sphere": sei facce di una griglia,
/// proiettate sulla sfera e spostate in altezza dal PlanetTerrain. Ogni faccia è una
/// mesh figlia del pianeta. Questo è lo standard per pianeti walkable; più avanti
/// diventerà a LOD (quadtree) per coprire scale enormi senza milioni di vertici.
/// </summary>
public static class PlanetMeshBuilder
{
    static readonly Vector3[] FaceNormals =
    {
        Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back
    };

    public static void Build(Transform parent, PlanetTerrain terrain, int resolution, Material mat)
    {
        foreach (var normal in FaceNormals)
        {
            var mesh = BuildFace(normal, terrain, resolution);
            var go = new GameObject("Face");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            // collider sulla superficie reale: permette di appoggiarci oggetti con un raycast
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }
    }

    static Mesh BuildFace(Vector3 localUp, PlanetTerrain terrain, int res)
    {
        Vector3 axisA = new Vector3(localUp.y, localUp.z, localUp.x);
        Vector3 axisB = Vector3.Cross(localUp, axisA);

        var verts = new Vector3[res * res];
        var normals = new Vector3[res * res];
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
                normals[i] = terrain.SurfaceNormal(dir, eps);

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
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }
}

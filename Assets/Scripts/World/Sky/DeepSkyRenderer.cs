using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Oggetti del cielo profondo (galassie, nebulose, ammassi) come billboard con FOTO VERE (Hubble/ESO/Wikimedia) da un
/// atlante 16×16 (<c>Resources/Sky/dso_atlas</c>). Una mesh di quad (4 vert/oggetto); ogni oggetto del catalogo è
/// associato alla sua immagine tramite l'indice <see cref="SkyData.DsoTile"/> (mappato al bake dall'identificatore
/// Messier/NGC/IC). La dimensione viene dal RAGGIO ANGOLARE → crescono restringendo il campo (binocolo/telescopio):
/// a occhio nudo un fiocco, ingrandendo "si risolve" nell'oggetto vero. Resa additiva (lo sfondo nero della foto non
/// aggiunge nulla) nello shader <c>Wanderer/DeepSkyBillboard</c>. Solo oggetti CON foto → niente blob procedurali.
/// </summary>
public class DeepSkyRenderer : MonoBehaviour
{
    public const float Radius = 98f;

    GameObject go;

    public bool Build(Transform root, int layer)
    {
        if (!SkyData.LoadDso() || SkyData.DsoCount == 0) return false;

        var atlas = Resources.Load<Texture2D>("Sky/dso_atlas");
        if (atlas == null) { Debug.LogWarning("[sky] Resources/Sky/dso_atlas non trovato: niente deep-sky."); return false; }

        var sh = Shader.Find("Wanderer/DeepSkyBillboard");
        if (sh == null) { Debug.LogError("[sky] shader Wanderer/DeepSkyBillboard non trovato (Always Included?)."); return false; }

        int n = SkyData.DsoCount;
        var verts = new Vector3[n * 4];
        var uv0 = new List<Vector4>(n * 4);
        var uv1 = new List<Vector4>(n * 4);
        var tris = new int[n * 6];

        var c0 = new Vector2(-1, -1); var c1 = new Vector2(1, -1);
        var c2 = new Vector2(1, 1);   var c3 = new Vector2(-1, 1);

        for (int i = 0; i < n; i++)
        {
            Vector3 p = SkyData.DsoDir[i] * Radius;
            float rad = SkyData.DsoRadArcmin[i];
            float mag = SkyData.DsoMag[i];
            float tile = SkyData.DsoTile[i];
            int v = i * 4, t = i * 6;

            verts[v] = p; verts[v + 1] = p; verts[v + 2] = p; verts[v + 3] = p;
            uv0.Add(new Vector4(c0.x, c0.y, rad, mag)); uv0.Add(new Vector4(c1.x, c1.y, rad, mag));
            uv0.Add(new Vector4(c2.x, c2.y, rad, mag)); uv0.Add(new Vector4(c3.x, c3.y, rad, mag));
            var u1 = new Vector4(tile, 0, 0, 0);
            uv1.Add(u1); uv1.Add(u1); uv1.Add(u1); uv1.Add(u1);

            tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
            tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
        }

        var mesh = new Mesh { name = "DeepSky", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uv0);
        mesh.SetUVs(1, uv1);
        mesh.SetTriangles(tris, 0, false);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);
        mesh.UploadMeshData(true);

        go = new GameObject("DeepSky");
        go.transform.SetParent(root, false);
        if (layer >= 0) go.layer = layer;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = new Material(sh);
        mr.sharedMaterial.SetTexture("_Atlas", atlas);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return true;
    }
}

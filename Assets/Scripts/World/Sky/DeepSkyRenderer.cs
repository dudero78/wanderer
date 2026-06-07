using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Oggetti del cielo profondo (galassie, nebulose, ammassi) da OpenNGC, come billboard tipizzati. Una mesh di quad
/// (4 vert/oggetto) + un ATLANTE procedurale 2×2 (galassia ellittica · ammasso aperto a granelli · globulare denso ·
/// nebulosa a nuvola). La dimensione viene dal RAGGIO ANGOLARE → crescono restringendo il campo (binocolo/telescopio):
/// una macchia sfocata a occhio nudo, ingrandendo "si risolve". Tinta e luminosità di superficie per tipo. Tutto
/// nello shader <c>Wanderer/DeepSkyBillboard</c>.
/// </summary>
public class DeepSkyRenderer : MonoBehaviour
{
    public const float Radius = 98f;

    static readonly Color[] TypeTint =
    {
        new Color(0.95f, 0.92f, 0.82f),  // 0 galassia (bianco caldo)
        new Color(0.82f, 0.88f, 1.00f),  // 1 ammasso aperto (blu-bianco)
        new Color(1.00f, 0.93f, 0.78f),  // 2 globulare (giallo-bianco)
        new Color(1.00f, 0.55f, 0.55f),  // 3 nebulosa (rosa/rosso emissione)
        new Color(0.55f, 1.00f, 0.85f),  // 4 planetaria (verde-acqua)
    };

    GameObject go;

    public bool Build(Transform root, int layer)
    {
        if (!SkyData.LoadDso() || SkyData.DsoCount == 0) return false;

        var sh = Shader.Find("Wanderer/DeepSkyBillboard");
        if (sh == null) { Debug.LogError("[sky] shader Wanderer/DeepSkyBillboard non trovato (Always Included?)."); return false; }

        int n = SkyData.DsoCount;
        var verts = new Vector3[n * 4];
        var uv0 = new List<Vector4>(n * 4);
        var uv1 = new List<Vector4>(n * 4);
        var cols = new Color32[n * 4];
        var tris = new int[n * 6];

        var c0 = new Vector2(-1, -1); var c1 = new Vector2(1, -1);
        var c2 = new Vector2(1, 1);   var c3 = new Vector2(-1, 1);

        for (int i = 0; i < n; i++)
        {
            Vector3 p = SkyData.DsoDir[i] * Radius;
            float rad = SkyData.DsoRadArcmin[i];
            float mag = SkyData.DsoMag[i];
            int type = SkyData.DsoType[i];
            Color tint = TypeTint[Mathf.Clamp(type, 0, TypeTint.Length - 1)];
            int v = i * 4, t = i * 6;

            verts[v] = p; verts[v + 1] = p; verts[v + 2] = p; verts[v + 3] = p;
            uv0.Add(new Vector4(c0.x, c0.y, rad, mag)); uv0.Add(new Vector4(c1.x, c1.y, rad, mag));
            uv0.Add(new Vector4(c2.x, c2.y, rad, mag)); uv0.Add(new Vector4(c3.x, c3.y, rad, mag));
            var u1 = new Vector4(type, 0, 0, 0);
            uv1.Add(u1); uv1.Add(u1); uv1.Add(u1); uv1.Add(u1);
            Color32 c = tint; cols[v] = c; cols[v + 1] = c; cols[v + 2] = c; cols[v + 3] = c;

            tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
            tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
        }

        var mesh = new Mesh { name = "DeepSky", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uv0);
        mesh.SetUVs(1, uv1);
        mesh.SetColors(cols);
        mesh.SetTriangles(tris, 0, false);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);
        mesh.UploadMeshData(true);

        go = new GameObject("DeepSky");
        go.transform.SetParent(root, false);
        if (layer >= 0) go.layer = layer;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = new Material(sh);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return true;
    }
}

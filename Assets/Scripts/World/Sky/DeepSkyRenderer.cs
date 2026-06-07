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
        mr.sharedMaterial.SetTexture("_Atlas", BuildAtlas(256));
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return true;
    }

    /// <summary>Atlante 2×2 (solo alpha = forma; il colore lo dà la tinta per-vertice). Ogni tile è moltiplicato per
    /// una FINESTRA radiale che porta l'alpha a ZERO sul bordo → cerchio morbido, MAI un quadrato. Tipi: galassia
    /// (ellisse + nucleo) · ammasso aperto (granelli) · globulare (denso, centro brillante) · nebulosa (nuvola).</summary>
    static Texture2D BuildAtlas(int size)
    {
        int tile = size / 2;
        var px = new Color32[size * size];
        var rng = new System.Random(20260607);

        // tile 0: GALASSIA — ellisse soffusa + nucleo brillante
        FillTile(px, size, 0, 0, tile, (x, y, w) =>
            w * Mathf.Clamp01(Mathf.Exp(-(x * x * 2.4f + y * y * 8.5f)) + 0.85f * Mathf.Exp(-(x * x + y * y) * 22f)));

        // tile 1: AMMASSO APERTO — granelli sparsi + velo
        var dots = new Vector3[42];
        for (int k = 0; k < dots.Length; k++)
            dots[k] = new Vector3((float)rng.NextDouble() * 1.7f - 0.85f, (float)rng.NextDouble() * 1.7f - 0.85f, 0.5f + (float)rng.NextDouble() * 0.5f);
        FillTile(px, size, 1, 0, tile, (x, y, w) =>
        {
            float a = 0.05f;
            foreach (var d in dots) a += d.z * Mathf.Exp(-((x - d.x) * (x - d.x) + (y - d.y) * (y - d.y)) * 520f);
            return w * Mathf.Clamp01(a);
        });

        // tile 2: GLOBULARE — alone denso + centro brillante + granuli concentrati
        var gd = new Vector3[140];
        for (int k = 0; k < gd.Length; k++)
        {
            float ang = (float)rng.NextDouble() * 6.2832f, rr = Mathf.Pow((float)rng.NextDouble(), 1.8f) * 0.8f;
            gd[k] = new Vector3(Mathf.Cos(ang) * rr, Mathf.Sin(ang) * rr, 0.35f + (float)rng.NextDouble() * 0.5f);
        }
        FillTile(px, size, 0, 1, tile, (x, y, w) =>
        {
            float a = 0.6f * Mathf.Exp(-Mathf.Sqrt(x * x + y * y) * 3.2f);
            foreach (var d in gd) a += d.z * Mathf.Exp(-((x - d.x) * (x - d.x) + (y - d.y) * (y - d.y)) * 1100f);
            return w * Mathf.Clamp01(a);
        });

        // tile 3: NEBULOSA — nuvola irregolare (somma di lobi)
        var blobs = new Vector4[8];
        for (int k = 0; k < blobs.Length; k++)
            blobs[k] = new Vector4((float)rng.NextDouble() * 1.1f - 0.55f, (float)rng.NextDouble() * 1.1f - 0.55f,
                                   0.45f + (float)rng.NextDouble() * 0.8f, 1.4f + (float)rng.NextDouble() * 3.5f);
        FillTile(px, size, 1, 1, tile, (x, y, w) =>
        {
            float a = 0f;
            foreach (var b in blobs) a += b.z * Mathf.Exp(-((x - b.x) * (x - b.x) + (y - b.y) * (y - b.y)) * b.w);
            return w * Mathf.Clamp01(a * 0.95f);
        });

        var tex = new Texture2D(size, size, TextureFormat.Alpha8, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
        tex.SetPixels32(px);
        tex.Apply(true);
        return tex;
    }

    // riempie il tile [tx,ty] con f(x,y,window): x,y in [-1,1], window = smoothstep che →0 sul bordo (niente quadrato)
    static void FillTile(Color32[] px, int size, int tx, int ty, int tile, System.Func<float, float, float, float> f)
    {
        int ox = tx * tile, oy = ty * tile;
        float c = (tile - 1) * 0.5f;
        for (int y = 0; y < tile; y++)
            for (int x = 0; x < tile; x++)
            {
                float fx = (x - c) / c, fy = (y - c) / c;
                float r = Mathf.Sqrt(fx * fx + fy * fy);
                float w = Mathf.SmoothStep(1.0f, 0.5f, r);       // →0 sul bordo del tile
                byte a = (byte)(Mathf.Clamp01(f(fx, fy, w)) * 255f);
                px[(oy + y) * size + (ox + x)] = new Color32(255, 255, 255, a);
            }
    }
}

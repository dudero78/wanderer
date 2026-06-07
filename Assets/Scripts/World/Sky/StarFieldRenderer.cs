using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Costruisce e disegna il campo stellare come mesh statiche di quad billboard (4 vertici/stella), dal blob di
/// <see cref="SkyData"/>. Due mesh, un draw ciascuna, costruite una volta sola:
///   - CAMPO: tutte le stelle (~119k), shader <c>Wanderer/StarPoint</c> (dimensione/colore/zoom).
///   - ALONI: solo le poche showpiece (flag bit1, mag ≤ ~2.2), shader <c>Wanderer/StarHalo</c> → bagliore che fa
///     spiccare le brillanti (Sirio, Vega, Betelgeuse...).
/// ~119k stelle = ~476k vertici: banale per la GPU. Le stelle stanno a raggio fisso <see cref="Radius"/> attorno al
/// centro della "bolla cielo" (un figlio che segue la camera): non è una distanza vera (sono all'infinito), la
/// dimensione apparente è in PIXEL. Bounds enormi → mai cullata (la camera è sempre al centro).
/// </summary>
public class StarFieldRenderer : MonoBehaviour
{
    public const float Radius = 100f;   // raggio della sfera-cielo (entro near/far della camera; non è una distanza vera)

    const int CellM = 5;   // suddivisione per faccia del cubo → 6·M·M = 150 celle di cielo per il culling del campo profondo

    GameObject fieldGo, haloGo, deepParent;
    Material deepMat;

    /// <summary>Costruisce campo + aloni + (se c'è) il campo PROFONDO, come figli di <paramref name="root"/>.</summary>
    public bool Build(Transform root, int layer)
    {
        if (!SkyData.Load()) return false;

        // CAMPO: tutte le stelle dell'HYG
        var all = new List<int>(SkyData.Count);
        for (int i = 0; i < SkyData.Count; i++) all.Add(i);
        fieldGo = BuildMeshObject(root, layer, "StarField", all, "Wanderer/StarPoint", SkyData.Dir, SkyData.Mag, SkyData.Flags, SkyData.Color);

        // ALONI: solo le showpiece (flag bit1)
        var bright = new List<int>(128);
        for (int i = 0; i < SkyData.Count; i++) if ((SkyData.Flags[i] & 2) != 0) bright.Add(i);
        if (bright.Count > 0) haloGo = BuildMeshObject(root, layer, "StarHalos", bright, "Wanderer/StarHalo", SkyData.Dir, SkyData.Mag, SkyData.Flags, SkyData.Color);

        // CAMPO PROFONDO (ATHYG/Gaia): milioni di stelle deboli che si vedono solo zoomando. INIZIALMENTE SPENTO
        // (SkyController lo accende col binocolo/telescopio → a occhio nudo, mentre voli, non costa nulla) E suddiviso
        // in CELLE di cielo: Unity fa il frustum-culling di ogni cella → al telescopio (campo strettissimo) disegna solo
        // le poche celle inquadrate invece di tutte le stelle → si può andare profondi senza affossare le performance.
        if (SkyData.LoadDeep() && SkyData.DeepCount > 0)
            BuildDeepCells(root, layer);

        return fieldGo != null;
    }

    /// <summary>Accende/spegne il campo profondo (chiamato da SkyController in base allo zoom dello strumento).</summary>
    public void SetDeepEnabled(bool on)
    {
        if (deepParent != null && deepParent.activeSelf != on) deepParent.SetActive(on);
    }

    /// <summary>Cella di cielo (0..6·M·M) di una direzione, via proiezione cube-face (celle ~uniformi, niente poli degeneri).</summary>
    static int CellIndex(Vector3 d)
    {
        float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y), az = Mathf.Abs(d.z);
        int face; float u, v;
        if (ax >= ay && ax >= az) { face = d.x > 0 ? 0 : 1; u = d.y / ax; v = d.z / ax; }
        else if (ay >= az)        { face = d.y > 0 ? 2 : 3; u = d.x / ay; v = d.z / ay; }
        else                      { face = d.z > 0 ? 4 : 5; u = d.x / az; v = d.y / az; }
        int cx = Mathf.Clamp((int)((u * 0.5f + 0.5f) * CellM), 0, CellM - 1);
        int cy = Mathf.Clamp((int)((v * 0.5f + 0.5f) * CellM), 0, CellM - 1);
        return (face * CellM + cy) * CellM + cx;
    }

    /// <summary>Costruisce il campo profondo come UNA mesh per cella di cielo (bounds STRETTI → frustum-culling di Unity),
    /// tutte figlie di un parent che fa da interruttore. Un solo materiale condiviso fra le celle.</summary>
    void BuildDeepCells(Transform root, int layer)
    {
        var sh = Shader.Find("Wanderer/StarPoint");
        if (sh == null) { Debug.LogError("[sky] shader Wanderer/StarPoint non trovato."); return; }

        deepParent = new GameObject("StarFieldDeep");
        deepParent.transform.SetParent(root, false);
        if (layer >= 0) deepParent.layer = layer;
        deepParent.SetActive(false);   // acceso solo zoomando

        int nCells = 6 * CellM * CellM;
        var bins = new List<int>[nCells];
        for (int i = 0; i < SkyData.DeepCount; i++)
        {
            int c = CellIndex(SkyData.DeepDir[i]);
            (bins[c] ?? (bins[c] = new List<int>())).Add(i);
        }

        deepMat = new Material(sh);
        // Costruisco le celle su PIÙ FRAME (~6/frame): 2.4M stelle in un colpo solo darebbero un singhiozzo al
        // caricamento. Il campo profondo è spento finché non zoomi, quindi la costruzione progressiva è invisibile.
        StartCoroutine(BuildCellsRoutine(bins, nCells, layer));
    }

    System.Collections.IEnumerator BuildCellsRoutine(List<int>[] bins, int nCells, int layer)
    {
        int built = 0;
        for (int c = 0; c < nCells; c++)
        {
            if (bins[c] == null || bins[c].Count == 0) continue;
            if (deepParent == null) yield break;   // cielo distrutto nel frattempo
            var mesh = BuildQuadMesh("deepCell" + c, bins[c], SkyData.DeepDir, SkyData.DeepMag, null, SkyData.DeepColor, true);
            var go = new GameObject("cell" + c);
            go.transform.SetParent(deepParent.transform, false);
            if (layer >= 0) go.layer = layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = deepMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            if (++built % 6 == 0) yield return null;
        }
    }

    static GameObject BuildMeshObject(Transform root, int layer, string name, List<int> idx, string shaderName,
                                      Vector3[] dir, float[] mag, byte[] flags, Color32[] color)
    {
        var mesh = BuildQuadMesh(name, idx, dir, mag, flags, color);
        var sh = Shader.Find(shaderName);
        if (sh == null) { Debug.LogError("[sky] shader " + shaderName + " non trovato (Always Included?)."); return null; }

        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        if (layer >= 0) go.layer = layer;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = new Material(sh);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return go;
    }

    /// <summary>Una mesh di quad billboard (4 vert/stella) per le stelle in <paramref name="idx"/>. Per-vertice:
    /// posizione = direzione×Radius, uv.xy = angolo del quad (±1), uv.z = magnitudine, uv.w = tier, colore = B−V.</summary>
    static Mesh BuildQuadMesh(string name, List<int> idx, Vector3[] dir, float[] mag, byte[] flags, Color32[] color, bool tightBounds = false)
    {
        int n = idx.Count;
        var verts = new Vector3[n * 4];
        var uv = new List<Vector4>(n * 4);
        var cols = new Color32[n * 4];
        var tris = new int[n * 6];

        var c0 = new Vector2(-1, -1); var c1 = new Vector2(1, -1);
        var c2 = new Vector2(1, 1);   var c3 = new Vector2(-1, 1);

        for (int k = 0; k < n; k++)
        {
            int i = idx[k];
            Vector3 p = dir[i] * Radius;
            float magV = mag[i];
            float tier = flags != null ? flags[i] : 0f;
            Color32 col = color[i];
            int v = k * 4, t = k * 6;

            verts[v] = p; verts[v + 1] = p; verts[v + 2] = p; verts[v + 3] = p;
            uv.Add(new Vector4(c0.x, c0.y, magV, tier));
            uv.Add(new Vector4(c1.x, c1.y, magV, tier));
            uv.Add(new Vector4(c2.x, c2.y, magV, tier));
            uv.Add(new Vector4(c3.x, c3.y, magV, tier));
            cols[v] = col; cols[v + 1] = col; cols[v + 2] = col; cols[v + 3] = col;

            tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
            tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
        }

        var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uv);
        mesh.SetColors(cols);
        mesh.SetTriangles(tris, 0, false);
        // bounds STRETTI (celle del campo profondo) → frustum-culling per cella; ENORMI (campo/aloni interi) → mai cullati
        // (la camera è dentro la sfera). Le posizioni stanno a Radius attorno al centro della bolla che segue la camera.
        if (tightBounds) mesh.RecalculateBounds();
        else mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);
        mesh.UploadMeshData(true);
        return mesh;
    }
}

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

    GameObject fieldGo, haloGo, deepGo;

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

        // CAMPO PROFONDO (ATHYG): stelle deboli che si vedono solo zoomando. Mesh separato, INIZIALMENTE SPENTO:
        // SkyController lo accende solo col binocolo/telescopio → a occhio nudo (mentre voli) non costa nulla.
        if (SkyData.LoadDeep() && SkyData.DeepCount > 0)
        {
            var deep = new List<int>(SkyData.DeepCount);
            for (int i = 0; i < SkyData.DeepCount; i++) deep.Add(i);
            deepGo = BuildMeshObject(root, layer, "StarFieldDeep", deep, "Wanderer/StarPoint", SkyData.DeepDir, SkyData.DeepMag, null, SkyData.DeepColor);
            if (deepGo != null) deepGo.SetActive(false);
        }

        return fieldGo != null;
    }

    /// <summary>Accende/spegne il campo profondo (chiamato da SkyController in base allo zoom dello strumento).</summary>
    public void SetDeepEnabled(bool on)
    {
        if (deepGo != null && deepGo.activeSelf != on) deepGo.SetActive(on);
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
    static Mesh BuildQuadMesh(string name, List<int> idx, Vector3[] dir, float[] mag, byte[] flags, Color32[] color)
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
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);   // mai cullata
        mesh.UploadMeshData(true);
        return mesh;
    }
}

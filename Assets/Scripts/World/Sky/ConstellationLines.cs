using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Linee delle costellazioni (tutte le 88, da d3-celestial, BSD) + nomi delle stelle famose (dal catalogo HYG). Le
/// linee sono strisce additive a spessore-px costante (shader <c>Wanderer/ConstellationLine</c>) allineate alle stelle
/// (stessa rotazione <see cref="SkyData.StarDirection"/>). Tasto <see cref="ToggleKey"/> CICLA: spente → tutte →
/// zodiacali → emisfero nord → emisfero sud → spente. Etichette in IMGUI con ombra, font scalato alla risoluzione.
/// </summary>
public class ConstellationLines : MonoBehaviour
{
    public const KeyCode ToggleKey = KeyCode.C;
    const float Radius = 100f;

    enum Mode { Off = 0, All = 1, Zodiac = 2 }
    const int ModeCount = 3;
    static readonly string[] ModeName = { "", "TUTTE", "ZODIACALI" };

    Transform skyRoot;
    Camera playerCam;
    Material mat;
    Mesh mesh;
    GameObject go;
    Mode mode = Mode.Off;
    float alpha;
    readonly List<Vector2> placed = new List<Vector2>();
    GUIStyle labelStyle;

    public void Build(Transform root, int layer, Camera cam)
    {
        skyRoot = root; playerCam = cam;
        SkyData.LoadConstellations();
        SkyData.LoadStarNames();

        var sh = Shader.Find("Wanderer/ConstellationLine");
        if (sh == null) { Debug.LogError("[sky] shader Wanderer/ConstellationLine non trovato (Always Included?)."); return; }

        mesh = new Mesh { name = "Constellations", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);

        go = new GameObject("Constellations");
        go.transform.SetParent(root, false);
        if (layer >= 0) go.layer = layer;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mat = new Material(sh); mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false; mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
    }

    bool Active(SkyData.Constellation c) => mode switch
    {
        Mode.All => true,
        Mode.Zodiac => c.Zodiac,
        _ => false,
    };

    void RebuildMesh()
    {
        if (mesh == null || SkyData.Cons == null) return;
        var segs = new List<(Vector3 a, Vector3 b)>();
        if (mode != Mode.Off)
            foreach (var c in SkyData.Cons)
                if (Active(c))
                    for (int s = 0; s < c.A.Length; s++) segs.Add((c.A[s], c.B[s]));

        int n = segs.Count;
        var verts = new List<Vector3>(n * 4);
        var norms = new List<Vector3>(n * 4);
        var uvs = new List<Vector2>(n * 4);
        var tris = new List<int>(n * 6);
        foreach (var (a, b) in segs)
        {
            Vector3 pa = a * Radius, pb = b * Radius, tan = (pb - pa).normalized;
            int bi = verts.Count;
            verts.Add(pa); verts.Add(pa); verts.Add(pb); verts.Add(pb);
            norms.Add(tan); norms.Add(tan); norms.Add(tan); norms.Add(tan);
            uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(1, 0));
            tris.Add(bi); tris.Add(bi + 1); tris.Add(bi + 2); tris.Add(bi); tris.Add(bi + 2); tris.Add(bi + 3);
        }
        mesh.Clear();
        mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0, false);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);
    }

    void Update()
    {
        bool can = !MapMode.IsOpen && playerCam != null && playerCam.isActiveAndEnabled;
        if (can && Input.GetKeyDown(ToggleKey))
        {
            mode = (Mode)(((int)mode + 1) % ModeCount);
            RebuildMesh();
        }
        alpha = Mathf.MoveTowards(alpha, (can && mode != Mode.Off) ? 1f : 0f, Time.unscaledDeltaTime / 0.35f);
        if (mat != null) mat.SetFloat("_Alpha", alpha * 0.9f);
    }

    void OnGUI()
    {
        if (alpha < 0.05f || playerCam == null || !playerCam.isActiveAndEnabled || MapMode.IsOpen) return;
        if (Event.current.type != EventType.Repaint) return;

        int fs = Mathf.Max(13, Mathf.RoundToInt(Screen.height / 58f));   // font scalato (leggibile in build, anche Retina)
        if (labelStyle == null || labelStyle.fontSize != fs) labelStyle = new GUIStyle(GUI.skin.label) { fontSize = fs };
        var prev = GUI.color;
        Vector3 origin = skyRoot != null ? skyRoot.position : playerCam.transform.position;
        float sx = (float)Screen.width / Mathf.Max(1, playerCam.pixelWidth);
        float sy = (float)Screen.height / Mathf.Max(1, playerCam.pixelHeight);
        placed.Clear();

        // nomi delle COSTELLAZIONI (categoria attiva) — azzurro
        if (SkyData.Cons != null)
            foreach (var c in SkyData.Cons)
                if (Active(c))
                    DrawLabel(c.Centroid, c.Name.ToUpperInvariant(), new Color(0.55f, 0.78f, 1f, alpha * 0.8f), origin, sx, sy, fs);

        // nomi delle STELLE famose (le più brillanti) — bianco
        if (SkyData.StarNameStr != null)
            for (int i = 0; i < SkyData.StarNameCount; i++)
                if (SkyData.StarNameMag[i] <= 3.0f)
                    DrawLabel(SkyData.StarNameDir[i], SkyData.StarNameStr[i], new Color(0.92f, 0.95f, 1f, alpha * 0.95f), origin, sx, sy, fs);

        // indicatore di modalità (in basso al centro)
        GUI.color = new Color(0.6f, 0.8f, 1f, alpha * 0.7f);
        var mlab = "COSTELLAZIONI · " + ModeName[(int)mode];
        GUI.Label(new Rect(Screen.width * 0.5f - fs * 8f, Screen.height - fs * 2.4f, fs * 16f, fs * 1.6f), mlab, labelStyle);
        GUI.color = prev;
    }

    void DrawLabel(Vector3 dir, string text, Color col, Vector3 origin, float sx, float sy, int fs)
    {
        Vector3 sp = playerCam.WorldToScreenPoint(origin + dir * Radius);
        if (sp.z <= 0) return;
        float x = sp.x * sx, y = Screen.height - sp.y * sy;
        if (x < 0 || x > Screen.width || y < 0 || y > Screen.height) return;
        var p = new Vector2(x, y);
        float minD = fs * 1.9f;
        for (int k = 0; k < placed.Count; k++) if ((placed[k] - p).sqrMagnitude < minD * minD) return;
        placed.Add(p);

        var rect = new Rect(x + fs * 0.45f, y - fs * 0.65f, fs * 16f, fs * 1.4f);
        // ombra per leggibilità su qualsiasi sfondo
        var sh = new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height);
        GUI.color = new Color(0f, 0f, 0f, col.a * 0.9f); GUI.Label(sh, text, labelStyle);
        GUI.color = col; GUI.Label(rect, text, labelStyle);
    }
}

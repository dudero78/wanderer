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
    public const KeyCode ToggleKey = KeyCode.C;     // costellazioni (cicla spente/tutte/zodiacali)
    public const KeyCode DsoKey = KeyCode.L;        // nomi degli oggetti del profondo cielo (per trovarli)
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
    Material eclipticMat, equatorMat;   // cerchi di riferimento (mostrati con le costellazioni)
    bool showDso;                       // toggle nomi deep-sky (L)
    float dsoAlpha;
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

        SkyData.LoadDso();   // per i nomi deep-sky (toggle L)

        // Cerchi di riferimento (mostrati con le costellazioni): ECLITTICA (piano y=0 nel frame di gioco = piano orbitale,
        // dove corrono Sole/pianeti/zodiaco) in oro; EQUATORE celeste (Dec=0) in azzurro. Linee pulite a spessore-px.
        var ecl = new Vector3[256]; var equ = new Vector3[256];
        for (int i = 0; i < 256; i++)
        {
            float a = i / 256f * 2f * Mathf.PI;
            ecl[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));                 // eclittica = piano orbitale
            equ[i] = SkyData.StarDirection(i / 256f * 360f, 0f);                  // equatore celeste (Dec=0)
        }
        eclipticMat = BuildCircle(root, layer, sh, ecl, new Color(0.62f, 0.50f, 0.18f));   // oro spento
        equatorMat  = BuildCircle(root, layer, sh, equ, new Color(0.30f, 0.45f, 0.62f));   // azzurro spento (diverso dalle costellazioni)
    }

    /// <summary>Un cerchio massimo come anello di strisce additive a spessore-px costante (stesso shader delle costellazioni,
    /// colore proprio). Ritorna il materiale per pilotarne l'_Alpha.</summary>
    Material BuildCircle(Transform root, int layer, Shader sh, Vector3[] dirs, Color col)
    {
        int n = dirs.Length;
        var verts = new List<Vector3>(n * 4); var norms = new List<Vector3>(n * 4);
        var uvs = new List<Vector2>(n * 4); var tris = new List<int>(n * 6);
        for (int i = 0; i < n; i++)
        {
            if ((i & 3) >= 2) continue;   // TRATTEGGIO: due segmenti accesi, due spenti (linea pulita, discreta)
            Vector3 a = dirs[i] * Radius, b = dirs[(i + 1) % n] * Radius, tan = (b - a).normalized;
            int bi = verts.Count;
            verts.Add(a); verts.Add(a); verts.Add(b); verts.Add(b);
            norms.Add(tan); norms.Add(tan); norms.Add(tan); norms.Add(tan);
            uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(1, 0));
            tris.Add(bi); tris.Add(bi + 1); tris.Add(bi + 2); tris.Add(bi); tris.Add(bi + 2); tris.Add(bi + 3);
        }
        var m = new Mesh { name = "SkyCircle" }; m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs);
        m.SetTriangles(tris, 0, false); m.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);
        var cgo = new GameObject("SkyCircle"); cgo.transform.SetParent(root, false);
        if (layer >= 0) cgo.layer = layer;
        cgo.AddComponent<MeshFilter>().sharedMesh = m;
        var cmr = cgo.AddComponent<MeshRenderer>();
        var cmat = new Material(sh); cmat.SetColor("_Color", col); cmat.SetFloat("_Alpha", 0f);
        cmr.sharedMaterial = cmat;
        cmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        cmr.receiveShadows = false; cmr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        return cmat;
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
        if (can && Input.GetKeyDown(DsoKey)) showDso = !showDso;

        alpha = Mathf.MoveTowards(alpha, (can && mode != Mode.Off) ? 1f : 0f, Time.unscaledDeltaTime / 0.35f);
        if (mat != null) mat.SetFloat("_Alpha", alpha * 0.9f);
        // i cerchi di riferimento appaiono SOLO in modalità "tutte" (in zodiacali l'eclittica è già ovvia), tenui
        float circAlpha = mode == Mode.All ? alpha * 0.5f : 0f;
        if (eclipticMat != null) eclipticMat.SetFloat("_Alpha", circAlpha);
        if (equatorMat != null) equatorMat.SetFloat("_Alpha", circAlpha);

        dsoAlpha = Mathf.MoveTowards(dsoAlpha, (can && showDso) ? 1f : 0f, Time.unscaledDeltaTime / 0.3f);
    }

    void OnGUI()
    {
        bool showCon = alpha >= 0.05f, showD = dsoAlpha >= 0.05f;
        if ((!showCon && !showD) || playerCam == null || !playerCam.isActiveAndEnabled || MapMode.IsOpen) return;
        if (Event.current.type != EventType.Repaint) return;

        int fs = Mathf.Max(13, Mathf.RoundToInt(Screen.height / 58f));   // font scalato (leggibile in build, anche Retina)
        if (labelStyle == null || labelStyle.fontSize != fs) labelStyle = new GUIStyle(GUI.skin.label) { fontSize = fs };
        var prev = GUI.color;
        Vector3 origin = skyRoot != null ? skyRoot.position : playerCam.transform.position;
        float sx = (float)Screen.width / Mathf.Max(1, playerCam.pixelWidth);
        float sy = (float)Screen.height / Mathf.Max(1, playerCam.pixelHeight);
        placed.Clear();

        // nomi degli OGGETTI DEL PROFONDO CIELO (toggle L) — verde-acqua, per trovarli. Prima, così hanno priorità nel de-clutter.
        if (showD && SkyData.DsoName != null)
            for (int i = 0; i < SkyData.DsoCount; i++)
                DrawLabel(SkyData.DsoDir[i], SkyData.DsoName[i], new Color(0.55f, 1f, 0.8f, dsoAlpha * 0.92f), origin, sx, sy, fs);

        if (showCon)
        {
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
            var mlab = "COSTELLAZIONI · " + ModeName[(int)mode] + (mode == Mode.All ? "   ·   eclittica (oro) · equatore (azzurro)" : "");
            GUI.Label(new Rect(Screen.width * 0.5f - fs * 12f, Screen.height - fs * 2.4f, fs * 24f, fs * 1.6f), mlab, labelStyle);
            GUI.color = prev;
        }
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

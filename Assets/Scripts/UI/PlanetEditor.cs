using System.IO;
using UnityEngine;

/// <summary>
/// UI dell'EDITOR DI PIANETI (scena dedicata, non in gioco). Pannello a sinistra: si parte da una sfera liscia
/// e si compone la RICETTA (PlanetRecipe) — forma base (con preset), tipo/colore del corpo, N pipeline di
/// crateri (attiva/togli/tara) — con anteprima IMMEDIATA. Salva/carica le ricette su disco (JSON).
///
/// Forma (base/crateri) → ricostruisce la mesh (RebuildSync, bassa res per la reattività). Colore → aggiorna
/// solo i materiali (niente rebuild). È il generatore: la ricetta sarà poi bakeata in texture per il gioco.
/// </summary>
public class PlanetEditor : MonoBehaviour
{
    const int PreviewRes = 110;

    PlanetTerrain terrain;
    SingleMeshPlanet smp;
    PlanetRecipe recipe;
    MeshRenderer[] faceRenderers;

    bool geomDirty, colorDirty;
    int pendingRemove = -1;
    bool pendingAdd;
    Vector2 scroll;
    GUIStyle title, head, val, btn;

    static readonly string[] BasePresets = { "Liscia", "Collinare", "Montagnosa", "Irregolare" };
    static readonly string[] BodyPresets = { "Lunare", "Marziano", "Ghiacciato" };

    string Dir => Path.Combine(Application.persistentDataPath, "planets");

    public void Init(PlanetTerrain t, SingleMeshPlanet s)
    {
        terrain = t; smp = s;
        recipe = t != null ? t.Recipe : PlanetRecipe.SmoothSphere();
        if (s != null) faceRenderers = s.GetComponentsInChildren<MeshRenderer>();
        Directory.CreateDirectory(Dir);
    }

    void Update()
    {
        if (recipe == null) return;
        if (pendingRemove >= 0 && pendingRemove < recipe.craters.Count) { recipe.craters.RemoveAt(pendingRemove); pendingRemove = -1; geomDirty = true; }
        if (pendingAdd) { recipe.craters.Add(new CraterRecipe()); pendingAdd = false; geomDirty = true; }

        if (geomDirty) { terrain.ApplyRecipe(recipe); if (smp != null) smp.RebuildSync(terrain, PreviewRes); geomDirty = false; }
        if (colorDirty) { PushColors(); colorDirty = false; }
    }

    void PushColors()
    {
        if (faceRenderers == null) return;
        foreach (var r in faceRenderers)
        {
            var m = r != null ? r.sharedMaterial : null;
            if (m == null) continue;
            m.SetColor("_SoilMean", recipe.soilMean);
            m.SetColor("_MariaColor", recipe.mariaColor);
            m.SetFloat("_MariaScale", recipe.mariaScale);
            m.SetFloat("_MariaStr", recipe.mariaStrength);
        }
    }

    void OnGUI()
    {
        if (recipe == null) return;
        float ui = Mathf.Max(1f, Screen.height / 1080f);
        EnsureStyles(ui);

        float w = 430f * ui, h = Screen.height - 40f * ui;
        var panel = new Rect(16f * ui, 20f * ui, w, h);
        GUI.Box(panel, GUIContent.none);
        GUILayout.BeginArea(new Rect(panel.x + 14f * ui, panel.y + 12f * ui, w - 28f * ui, h - 24f * ui));

        GUILayout.Label("EDITOR PIANETI", title);
        recipe.name = GUILayout.TextField(recipe.name);
        GUILayout.Space(8f * ui);
        scroll = GUILayout.BeginScrollView(scroll);

        // --- FORMA DI BASE ---
        GUILayout.Label("FORMA DI BASE", head);
        int bp = GUILayout.Toolbar(-1, BasePresets, GUILayout.Height(26f * ui));
        if (bp >= 0) { ApplyBasePreset(bp); geomDirty = true; }
        recipe.amplitude = Slider("Ampiezza (m)", recipe.amplitude, 0f, 150f, ui, ref geomDirty);
        recipe.frequency = Slider("Frequenza", recipe.frequency, 0.5f, 6f, ui, ref geomDirty);
        recipe.octaves = Mathf.RoundToInt(Slider("Ottave", recipe.octaves, 1, 8, ui, ref geomDirty));
        recipe.gain = Slider("Gain", recipe.gain, 0.3f, 0.7f, ui, ref geomDirty);
        if (Button("Nuovo seed", ui)) { recipe.seed = recipe.seed * 1664525 + 1013904223; geomDirty = true; }
        GUILayout.Space(10f * ui);

        // --- TIPO / COLORE ---
        GUILayout.Label("TIPO / COLORE", head);
        int tp = GUILayout.Toolbar(-1, BodyPresets, GUILayout.Height(26f * ui));
        if (tp >= 0) { ApplyBodyPreset(tp); colorDirty = true; }
        recipe.mariaScale = Slider("Mari: scala regioni", recipe.mariaScale, 0.8f, 6f, ui, ref colorDirty);
        recipe.mariaStrength = Slider("Mari: forza", recipe.mariaStrength, 0f, 1f, ui, ref colorDirty);
        GUILayout.Space(10f * ui);

        // --- PIPELINE DI CRATERI ---
        for (int i = 0; i < recipe.craters.Count; i++)
        {
            var c = recipe.craters[i];
            GUILayout.BeginHorizontal();
            GUILayout.Label("CRATERI " + (i + 1), head, GUILayout.ExpandWidth(true));
            if (Button("Togli", ui, 66f)) pendingRemove = i;
            GUILayout.EndHorizontal();
            c.enabled = Toggle("Attiva", c.enabled, ui);
            c.largestRadius = Slider("Raggio max (m)", c.largestRadius, 10f, 400f, ui, ref geomDirty);
            c.density = Slider("Densità", c.density, 0f, 1f, ui, ref geomDirty);
            c.octaves = Mathf.RoundToInt(Slider("Ottave taglia", c.octaves, 1, 7, ui, ref geomDirty));
            c.depthRatio = Slider("Profondità/raggio", c.depthRatio, 0.05f, 0.5f, ui, ref geomDirty);
            c.rimRatio = Slider("Bordo/profondità", c.rimRatio, 0.1f, 0.6f, ui, ref geomDirty);
            c.dominant = Toggle("Dominante", c.dominant, ui);
            if (c.dominant) c.dominantRadius = Slider("  raggio dominante", c.dominantRadius, 50f, 500f, ui, ref geomDirty);
            GUILayout.Space(8f * ui);
        }
        if (Button("+ Aggiungi pipeline crateri", ui)) pendingAdd = true;
        GUILayout.Space(14f * ui);

        // --- FILE ---
        GUILayout.BeginHorizontal();
        if (Button("Salva", ui)) Save();
        if (Button("Carica", ui)) Load();
        if (Button("Nuovo (liscio)", ui)) { recipe = PlanetRecipe.SmoothSphere(); geomDirty = colorDirty = true; }
        GUILayout.EndHorizontal();

        GUILayout.EndScrollView();
        GUILayout.Label("Tasto DESTRO + trascina = ruota · rotella = zoom · anteprima " + PreviewRes, val);
        GUILayout.EndArea();
    }

    void ApplyBasePreset(int p)
    {
        switch (p)
        {
            case 0: recipe.amplitude = 4f; recipe.frequency = 1.6f; recipe.octaves = 4; recipe.gain = 0.5f; break;   // Liscia
            case 1: recipe.amplitude = 30f; recipe.frequency = 2.0f; recipe.octaves = 5; recipe.gain = 0.55f; break; // Collinare
            case 2: recipe.amplitude = 95f; recipe.frequency = 2.5f; recipe.octaves = 6; recipe.gain = 0.6f; break;  // Montagnosa
            case 3: recipe.amplitude = 60f; recipe.frequency = 2.2f; recipe.octaves = 5; recipe.gain = 0.6f; break;  // Irregolare (Phobos)
        }
    }

    void ApplyBodyPreset(int p)
    {
        switch (p)
        {
            case 0: recipe.soilMean = new Color(0.44f, 0.44f, 0.45f); recipe.mariaColor = new Color(0.50f, 0.50f, 0.55f); break; // Lunare
            case 1: recipe.soilMean = new Color(0.52f, 0.33f, 0.22f); recipe.mariaColor = new Color(0.40f, 0.26f, 0.18f); break; // Marziano
            case 2: recipe.soilMean = new Color(0.80f, 0.84f, 0.90f); recipe.mariaColor = new Color(0.62f, 0.72f, 0.86f); break; // Ghiacciato
        }
    }

    // ---- widget ----
    float Slider(string label, float v, float min, float max, float ui, ref bool flag)
    {
        GUILayout.BeginHorizontal(GUILayout.Height(22f * ui));
        GUILayout.Label(label, head, GUILayout.Width(170f * ui));
        float nv = GUILayout.HorizontalSlider(v, min, max, GUILayout.ExpandWidth(true), GUILayout.Height(18f * ui));
        GUILayout.Label(Mathf.Abs(nv) >= 100f ? nv.ToString("F0") : nv.ToString("F2"), val, GUILayout.Width(52f * ui));
        GUILayout.EndHorizontal();
        if (!Mathf.Approximately(nv, v)) flag = true;
        return nv;
    }
    bool Toggle(string label, bool v, float ui)
    {
        bool nv = GUILayout.Toggle(v, "  " + label, GUILayout.Height(20f * ui));
        if (nv != v) geomDirty = true;
        return nv;
    }
    bool Button(string label, float ui, float width = 0f)
        => width > 0f ? GUILayout.Button(label, btn, GUILayout.Width(width * ui), GUILayout.Height(26f * ui))
                      : GUILayout.Button(label, btn, GUILayout.Height(26f * ui));

    void Save()
    {
        var path = Path.Combine(Dir, Sanitize(recipe.name) + ".json");
        File.WriteAllText(path, JsonUtility.ToJson(recipe, true));
        Debug.Log("Ricetta salvata: " + path);
    }
    void Load()
    {
        var path = Path.Combine(Dir, Sanitize(recipe.name) + ".json");
        if (!File.Exists(path)) { Debug.LogWarning("Nessuna ricetta '" + recipe.name + "' in " + Dir); return; }
        JsonUtility.FromJsonOverwrite(File.ReadAllText(path), recipe);
        geomDirty = colorDirty = true;
        Debug.Log("Ricetta caricata: " + path);
    }
    static string Sanitize(string s) { foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return string.IsNullOrEmpty(s) ? "pianeta" : s; }

    void EnsureStyles(float ui)
    {
        if (title == null)
        {
            title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.55f, 0.85f, 1f) } };
            head = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.85f, 0.9f, 1f) } };
            val = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(0.7f, 0.85f, 0.95f) } };
            btn = new GUIStyle(GUI.skin.button);
        }
        title.fontSize = Mathf.RoundToInt(18f * ui);
        head.fontSize = Mathf.RoundToInt(13f * ui);
        val.fontSize = Mathf.RoundToInt(12f * ui);
        btn.fontSize = Mathf.RoundToInt(12f * ui);
    }
}

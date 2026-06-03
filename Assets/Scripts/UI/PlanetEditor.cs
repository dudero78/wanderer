using System.Collections.Generic;
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
    const int PreviewRes = 128;        // mesh durante il drag (reattiva)
    const int FinalRes = 256;          // mesh quando l'edit si assesta (nitida)
    const int BakeMeshRes = 48;        // mesh d'appoggio per il ri-bake della normale-crateri
    const int EditorCraterRt = 512;    // risoluzione RT della normale-crateri nell'editor (rapida da ri-bakeare)

    PlanetTerrain terrain;
    SingleMeshPlanet smp;
    PlanetRecipe recipe;
    MeshRenderer[] faceRenderers;
    RenderTexture[] craterRTs;         // normale-crateri per faccia: liberate e rifatte a ogni assestamento

    bool geomDirty, colorDirty;
    int settleTimer = -1;              // ≥0 = sto contando i frame dall'ultima modifica per rifinire
    int pendingRemove = -1;
    bool pendingAdd;
    Vector2 scroll;
    GUIStyle title, head, val, btn, fold, tip, add;

    // stato dei pannelli collassabili (le sezioni con molti settings si chiudono per fare spazio)
    bool openBase = true, openColor = true, openSea = false;
    readonly List<bool> openCrater = new List<bool>();   // uno per pipeline di crateri

    static readonly GUIContent[] BaseGC =
    {
        new GUIContent("Liscia", "Quasi sfera: rilievo minimo"),
        new GUIContent("Collinare", "Colline dolci, rilievo medio"),
        new GUIContent("Montagnosa", "Rilievo accentuato, picchi alti"),
        new GUIContent("Irregolare", "Forma a patata tipo Phobos"),
    };
    static readonly GUIContent[] BodyGC =
    {
        new GUIContent("Lunare", "Grigio neutro"),
        new GUIContent("Marziano", "Bruno-rossastro"),
        new GUIContent("Ghiacciato", "Chiaro/azzurrino, alto albedo"),
    };
    static readonly GUIContent[] SoilGC =
    {
        new GUIContent("Terra", "Grana di terra bruna"),
        new GUIContent("Rosso", "Grana rossastra"),
        new GUIContent("Roccia", "Grana rocciosa"),
    };
    static readonly string[] SoilTextures = { "soil_dirt", "soil_red", "soil_rock" };

    /// <summary>Aggancio al bake su disco, iniettato dall'assembly Editor (il codice runtime non può
    /// referenziarlo). (terrain, nome cartella) → ok. Null in build → il pulsante avvisa che serve l'editor.</summary>
    public static System.Func<PlanetTerrain, string, bool> BakeToDiskHook;

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
        if (pendingRemove >= 0 && pendingRemove < recipe.craters.Count)
        {
            recipe.craters.RemoveAt(pendingRemove);
            if (pendingRemove < openCrater.Count) openCrater.RemoveAt(pendingRemove);   // stato di collasso allineato
            pendingRemove = -1; geomDirty = true;
        }
        if (pendingAdd) { recipe.craters.Add(new CraterRecipe()); openCrater.Add(true); pendingAdd = false; geomDirty = true; }

        if (geomDirty)
        {
            terrain.ApplyRecipe(recipe);
            if (smp != null) smp.RebuildSync(terrain, PreviewRes);   // anteprima rapida durante il drag
            geomDirty = false;
            settleTimer = 0;                                          // avvia il conto per la rifinitura
        }
        else if (settleTimer >= 0)
        {
            settleTimer++;
            if (settleTimer > 10)                                     // edit assestato (~0.2 s fermo)
            {
                if (smp != null) smp.RebuildSync(terrain, FinalRes);  // mesh ad alta risoluzione
                RebakeCraters();                                      // normale-crateri coerente con la ricetta
                settleTimer = -1;
            }
        }

        if (colorDirty) { PushColors(); colorDirty = false; }
    }

    /// <summary>Ri-bakea la normale dei crateri (bordi nitidi) dalla ricetta corrente, per faccia. Fatto solo
    /// all'assestamento di un edit: durante il drag si vede la sola mesh (basta per orientarsi).</summary>
    void RebakeCraters()
    {
        if (smp == null) return;
        var craterMat = PlanetBaker.CreateCraterMaterial(terrain);
        if (craterMat == null) return;
        if (craterRTs == null) craterRTs = new RenderTexture[6];
        var prev = RenderTexture.active;
        for (int f = 0; f < 6; f++)
        {
            var r = smp.FaceRenderer(f);
            if (r == null || r.sharedMaterial == null) continue;
            var rt = PlanetBaker.BakeCraterNormalRT(terrain, f, EditorCraterRt, craterMat, BakeMeshRes);
            r.sharedMaterial.SetTexture("_CraterNormalMap", rt);
            if (craterRTs[f] != null) craterRTs[f].Release();
            craterRTs[f] = rt;
        }
        RenderTexture.active = prev;
        Destroy(craterMat);
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
            m.SetFloat("_SeaOn", recipe.seaEnabled ? 1f : 0f);
            m.SetFloat("_SeaLevel", recipe.SeaRadius);
            m.SetColor("_SeaColor", recipe.seaColor);
            m.SetFloat("_Saturation", recipe.saturation);
            var st = Resources.Load<Texture2D>("Textures/" + recipe.soilTexture);
            if (st != null) m.SetTexture("_SoilSand", st);
        }
    }

    void OnGUI()
    {
        if (recipe == null) return;
        float ui = Mathf.Max(1f, Screen.height / 1080f);
        EnsureStyles(ui);
        SyncCraterFolds();

        float w = 430f * ui, h = Screen.height - 40f * ui;
        var panel = new Rect(16f * ui, 20f * ui, w, h);
        GUI.Box(panel, GUIContent.none);
        GUILayout.BeginArea(new Rect(panel.x + 14f * ui, panel.y + 12f * ui, w - 28f * ui, h - 24f * ui));

        GUILayout.Label("EDITOR PIANETI", title);
        recipe.name = GUILayout.TextField(recipe.name);
        GUILayout.Space(8f * ui);
        scroll = GUILayout.BeginScrollView(scroll);

        // === FORMA ===
        if (openBase = Foldout("FORMA", openBase, ui))
        {
            int bp = GUILayout.Toolbar(-1, BaseGC, GUILayout.Height(26f * ui));
            if (bp >= 0) { ApplyBasePreset(bp); geomDirty = true; }
            recipe.amplitude = Slider("Ampiezza (m)", "Dislivello massimo del rilievo di base (± dal raggio). Alto = corpo più accidentato.", recipe.amplitude, 0f, 150f, ui, ref geomDirty);
            recipe.frequency = Slider("Frequenza", "Scala delle ondulazioni di base: alta = feature più piccole e fitte.", recipe.frequency, 0.5f, 6f, ui, ref geomDirty);
            recipe.octaves = Mathf.RoundToInt(Slider("Ottave", "Livelli di dettaglio sommati nel rilievo: più ottave = più dettaglio fine.", recipe.octaves, 1, 8, ui, ref geomDirty));
            recipe.gain = Slider("Gain", "Peso delle ottave fini sulle grosse: alto = superficie più ruvida.", recipe.gain, 0.3f, 0.7f, ui, ref geomDirty);
            if (Button("Nuovo seed", "Rigenera casualmente la forma mantenendo i parametri.", ui)) { recipe.seed = recipe.seed * 1664525 + 1013904223; geomDirty = true; }
            GUILayout.Space(8f * ui);
        }

        // === COLORE & SUPERFICIE ===
        if (openColor = Foldout("COLORE & SUPERFICIE", openColor, ui))
        {
            int tp = GUILayout.Toolbar(-1, BodyGC, GUILayout.Height(26f * ui));
            if (tp >= 0) { ApplyBodyPreset(tp); colorDirty = true; }
            int curSoil = System.Array.IndexOf(SoilTextures, recipe.soilTexture);
            int ns = GUILayout.Toolbar(curSoil, SoilGC, GUILayout.Height(24f * ui));
            if (ns >= 0 && ns != curSoil) { recipe.soilTexture = SoilTextures[ns]; colorDirty = true; }
            recipe.saturation = Slider("Saturazione", "Intensità del colore: 0 = grigio, 1 = naturale, oltre = carico.", recipe.saturation, 0f, 2f, ui, ref colorDirty);
            recipe.mariaScale = Slider("Ombra bacini: scala", "Dimensione delle regioni scure nei bacini bassi (effetto di COLORE, non geometria).", recipe.mariaScale, 0.8f, 6f, ui, ref colorDirty);
            recipe.mariaStrength = Slider("Ombra bacini: forza", "Quanto scuriscono i bacini bassi (velatura di colore).", recipe.mariaStrength, 0f, 1f, ui, ref colorDirty);
            GUILayout.Space(8f * ui);
        }

        // === MARI (GEOMETRIA: allagamento) ===
        if (openSea = Foldout("MARI (geometria)", openSea, ui))
        {
            bool seaOn = Toggle("Attiva mare", "Allaga il terreno fino alla quota scelta: copre i crateri bassi. È geometria vera (ci cammini sopra).", recipe.seaEnabled, ui, geometry: true, changed: out bool seaTog);
            if (seaTog) { recipe.seaEnabled = seaOn; geomDirty = true; colorDirty = true; }
            if (recipe.seaEnabled)
            {
                float prevLevel = recipe.seaLevel;
                recipe.seaLevel = Slider("Livello (m)", "Quota del pelo dell'acqua: negativo riempie solo i bacini, positivo sommerge sempre di più.", recipe.seaLevel, -250f, 150f, ui, ref geomDirty);
                if (recipe.seaLevel != prevLevel) colorDirty = true;   // anche lo shader segue il pelo (_SeaLevel)
                recipe.seaColor.r = Slider("Colore R", "Componente rossa del colore dell'acqua.", recipe.seaColor.r, 0f, 1f, ui, ref colorDirty);
                recipe.seaColor.g = Slider("Colore G", "Componente verde del colore dell'acqua.", recipe.seaColor.g, 0f, 1f, ui, ref colorDirty);
                recipe.seaColor.b = Slider("Colore B", "Componente blu del colore dell'acqua.", recipe.seaColor.b, 0f, 1f, ui, ref colorDirty);
            }
            GUILayout.Space(8f * ui);
        }

        // === PIPELINE DI CRATERI (ciascuna collassabile) ===
        for (int i = 0; i < recipe.craters.Count; i++)
        {
            var c = recipe.craters[i];
            GUILayout.BeginHorizontal();
            openCrater[i] = Foldout((c.enabled ? "CRATERI " : "CRATERI (off) ") + (i + 1), openCrater[i], ui, expand: true);
            if (Button("Togli", "Rimuove questa pipeline di crateri.", ui, 66f)) pendingRemove = i;
            GUILayout.EndHorizontal();
            if (!openCrater[i]) continue;
            c.enabled = Toggle("Attiva", "Accende/spegne questo campo di crateri senza rimuoverlo.", c.enabled, ui, geometry: true, changed: out _);
            c.largestRadius = Slider("Raggio max (m)", "Raggio del cratere più grande del campo.", c.largestRadius, 10f, 400f, ui, ref geomDirty);
            c.density = Slider("Densità", "Probabilità che una cella contenga un cratere: alto = più crateri.", c.density, 0f, 1f, ui, ref geomDirty);
            c.octaves = Mathf.RoundToInt(Slider("Ottave taglia", "Bande di dimensione: più ottave = più taglie (tanti piccoli + pochi grandi).", c.octaves, 1, 7, ui, ref geomDirty));
            c.depthRatio = Slider("Profondità/raggio", "Quanto è profonda la conca rispetto al suo raggio.", c.depthRatio, 0.05f, 0.5f, ui, ref geomDirty);
            c.rimRatio = Slider("Bordo/profondità", "Altezza del bordo rialzato rispetto alla profondità.", c.rimRatio, 0.1f, 0.6f, ui, ref geomDirty);
            c.rimSharpness = Slider("Nitidezza bordi", "Forma della parete: 1 = cono dolce, alto = fondo piatto + bordo a cresta netta.", c.rimSharpness, 1f, 4f, ui, ref geomDirty);
            c.dominant = Toggle("Dominante", "Aggiunge un grande impatto piazzato a mano (tipo Stickney su Phobos).", c.dominant, ui, geometry: true, changed: out _);
            if (c.dominant) c.dominantRadius = Slider("  raggio dominante", "Raggio del grande impatto dominante (m).", c.dominantRadius, 50f, 500f, ui, ref geomDirty);
            GUILayout.Space(8f * ui);
        }
        GUILayout.Space(4f * ui);
        if (GUILayout.Button(new GUIContent("➕  Aggiungi pipeline crateri", "Aggiunge un nuovo campo di crateri sovrapposto."),
                             add, GUILayout.Height(36f * ui))) pendingAdd = true;
        GUILayout.Space(12f * ui);

        // === FILE ===
        GUILayout.BeginHorizontal();
        if (Button("Salva", "Salva la ricetta come JSON.", ui)) Save();
        if (Button("Carica", "Carica la ricetta col nome corrente.", ui)) Load();
        if (Button("Nuovo (liscio)", "Riparte da una sfera liscia.", ui)) { recipe = PlanetRecipe.SmoothSphere(); geomDirty = colorDirty = true; }
        GUILayout.EndHorizontal();
        GUILayout.Space(6f * ui);
        if (Button("Bake su disco (fissa il corpo)", "Cuoce le texture del corpo corrente in Resources/BakedPlanet_<nome> (pronte per il gioco).", ui)) BakeToDisk();

        GUILayout.EndScrollView();

        // riquadro spiegazione: mostra la descrizione del setting sotto al mouse (GUI.tooltip).
        GUILayout.Space(4f * ui);
        GUILayout.Box(string.IsNullOrEmpty(GUI.tooltip) ? "Passa il mouse su un comando per la spiegazione." : GUI.tooltip,
                      this.tip, GUILayout.Height(52f * ui));
        GUILayout.Label("Tasto DESTRO trascina = ruota · rotella = zoom", val);
        GUILayout.EndArea();
    }

    /// <summary>Tiene la lista degli stati di collasso allineata al numero di pipeline di crateri.</summary>
    void SyncCraterFolds()
    {
        while (openCrater.Count < recipe.craters.Count) openCrater.Add(true);
        while (openCrater.Count > recipe.craters.Count) openCrater.RemoveAt(openCrater.Count - 1);
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

    // ---- widget (con tooltip: l'etichetta porta la spiegazione mostrata nel riquadro in basso) ----
    float Slider(string label, string tipText, float v, float min, float max, float ui, ref bool flag)
    {
        GUILayout.BeginHorizontal(GUILayout.Height(22f * ui));
        GUILayout.Label(new GUIContent(label, tipText), head, GUILayout.Width(170f * ui));
        float nv = GUILayout.HorizontalSlider(v, min, max, GUILayout.ExpandWidth(true), GUILayout.Height(18f * ui));
        GUILayout.Label(Mathf.Abs(nv) >= 100f ? nv.ToString("F0") : nv.ToString("F2"), val, GUILayout.Width(52f * ui));
        GUILayout.EndHorizontal();
        if (!Mathf.Approximately(nv, v)) flag = true;
        return nv;
    }
    bool Toggle(string label, string tipText, bool v, float ui, bool geometry, out bool changed)
    {
        bool nv = GUILayout.Toggle(v, new GUIContent("  " + label, tipText), GUILayout.Height(20f * ui));
        changed = nv != v;
        if (changed && geometry) geomDirty = true;
        return nv;
    }
    bool Button(string label, string tipText, float ui, float width = 0f)
    {
        var c = new GUIContent(label, tipText);
        return width > 0f ? GUILayout.Button(c, btn, GUILayout.Width(width * ui), GUILayout.Height(26f * ui))
                          : GUILayout.Button(c, btn, GUILayout.Height(26f * ui));
    }

    /// <summary>Intestazione di sezione cliccabile: ▾ aperta / ▸ chiusa. Restituisce il nuovo stato.</summary>
    bool Foldout(string label, bool open, float ui, bool expand = false)
    {
        string s = (open ? "▾  " : "▸  ") + label;
        bool clicked = expand
            ? GUILayout.Button(s, fold, GUILayout.Height(24f * ui), GUILayout.ExpandWidth(true))
            : GUILayout.Button(s, fold, GUILayout.Height(24f * ui));
        return clicked ? !open : open;
    }

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

    void BakeToDisk()
    {
        if (BakeToDiskHook == null) { Debug.LogWarning("Bake su disco: disponibile solo dentro l'editor Unity."); return; }
        if (terrain == null) { Debug.LogWarning("Bake su disco: nessun terreno."); return; }
        terrain.ApplyRecipe(recipe);   // assicura che il terreno rifletta la ricetta corrente prima del bake
        BakeToDiskHook(terrain, "BakedPlanet_" + Sanitize(recipe.name));
    }

    void EnsureStyles(float ui)
    {
        // guardia sull'ULTIMO stile creato: se un hot-reload durante il Play aggiunge un campo nuovo
        // (vecchi stili conservati, nuovo a null), li ricrea tutti invece di lasciarne uno null.
        if (add == null)
        {
            title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.55f, 0.85f, 1f) } };
            head = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.85f, 0.9f, 1f) } };
            val = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(0.7f, 0.85f, 0.95f) } };
            btn = new GUIStyle(GUI.skin.button);
            // intestazione di sezione: button piatto allineato a sinistra, in tinta col titolo
            fold = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            fold.normal.textColor = fold.hover.textColor = fold.active.textColor = new Color(0.6f, 0.82f, 1f);
            // riquadro spiegazione: testo a capo, attenuato
            tip = new GUIStyle(GUI.skin.box) { wordWrap = true, alignment = TextAnchor.UpperLeft, fontStyle = FontStyle.Normal };
            tip.normal.textColor = new Color(0.78f, 0.85f, 0.92f);
            tip.padding = new RectOffset(8, 8, 6, 6);
            // pulsante "aggiungi pipeline": più in vista (grassetto + accento verde) per distinguerlo dal resto
            add = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
            add.normal.textColor = add.hover.textColor = add.active.textColor = new Color(0.55f, 0.95f, 0.6f);
        }
        title.fontSize = Mathf.RoundToInt(18f * ui);
        head.fontSize = Mathf.RoundToInt(13f * ui);
        val.fontSize = Mathf.RoundToInt(12f * ui);
        btn.fontSize = Mathf.RoundToInt(12f * ui);
        fold.fontSize = Mathf.RoundToInt(14f * ui);
        tip.fontSize = Mathf.RoundToInt(12f * ui);
        add.fontSize = Mathf.RoundToInt(14f * ui);
    }
}

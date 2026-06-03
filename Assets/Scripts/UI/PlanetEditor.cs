using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// UI dell'EDITOR DI PIANETI (scena dedicata, non in gioco). Pannello a sinistra: si parte da una sfera liscia
/// e si compone la RICETTA (PlanetRecipe) — forma base (con preset), tipo/colore del corpo, N pipeline di
/// crateri (attiva/togli/tara) — con anteprima IMMEDIATA. Salva/carica le ricette su disco (JSON).
///
/// Forma (base/crateri) → ricostruisce la mesh (rebuild ASYNC su thread, bassa res durante il drag). Colore → aggiorna
/// solo i materiali (niente rebuild). È il generatore: la ricetta sarà poi bakeata in texture per il gioco.
/// </summary>
public class PlanetEditor : MonoBehaviour
{
    const int DragRes = 80;            // mesh DURANTE il drag (bassa res, build su thread → anteprima rapida)
    const int FinalRes = 256;          // mesh quando l'edit si assesta (nitida)
    const int SettleFrames = 10;       // frame di quiete prima di rifinire a full res
    const int BakeMeshRes = 48;        // mesh d'appoggio per il ri-bake della normale-crateri
    const int EditorCraterRt = 512;    // risoluzione RT della normale-crateri nell'editor (rapida da ri-bakeare)

    PlanetTerrain terrain;
    SingleMeshPlanet smp;
    PlanetRecipe recipe;
    MeshRenderer[] faceRenderers;
    RenderTexture[] craterRTs;         // normale-crateri per faccia: liberate e rifatte a ogni assestamento

    bool geomDirty, colorDirty;
    bool needsRebuild;                 // un edit ha cambiato la forma: ricostruisci (async) appena i thread sono liberi
    int settleTimer = -1;              // ≥0 = sto contando i frame dall'ultima modifica per rifinire
    int pendingRemove = -1;            // processo da rimuovere
    int pendingMoveUp = -1;            // processo da spostare SU (scambio con il precedente)
    bool pendingAddFlag;               // c'è una nuova pipeline da aggiungere…
    ProcessType pendingAddType;        // …di questo tipo
    bool chooseType;                   // sto mostrando il selettore di tipo
    Vector2 scroll;
    GUIStyle title, head, val, btn, fold, tip, add;

    // stato dei pannelli collassabili (le sezioni con molti settings si chiudono per fare spazio)
    bool openBase = true, openColor = true;
    readonly List<bool> openProc = new List<bool>();   // uno per processo della pipeline

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

    /// <summary>Rettangolo del pannello (coord. GUI, origine in alto). La camera orbitale lo usa per NON
    /// zoomare quando la rotella gira sopra i settings (lì deve scorrere la lista, non il pianeta).</summary>
    public static Rect PanelRect;
    public static bool PointerOverPanel()
    {
        Vector2 m = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);   // GUI: y verso il basso
        return PanelRect.Contains(m);
    }

    string Dir => Path.Combine(Application.persistentDataPath, "planets");

    public void Init(PlanetTerrain t, SingleMeshPlanet s)
    {
        terrain = t; smp = s;
        recipe = t != null ? t.Recipe : PlanetRecipe.SmoothSphere();
        recipe.Normalize();
        if (s != null) faceRenderers = s.GetComponentsInChildren<MeshRenderer>();
        Directory.CreateDirectory(Dir);
    }

    void Update()
    {
        if (recipe == null) return;
        var P = recipe.processes;
        if (pendingRemove >= 0 && pendingRemove < P.Count)
        {
            P.RemoveAt(pendingRemove);
            if (pendingRemove < openProc.Count) openProc.RemoveAt(pendingRemove);
            pendingRemove = -1; geomDirty = true;
        }
        if (pendingMoveUp > 0 && pendingMoveUp < P.Count)
        {
            int i = pendingMoveUp;
            (P[i - 1], P[i]) = (P[i], P[i - 1]);                         // scambia col precedente: cambia l'ordine
            if (i < openProc.Count) (openProc[i - 1], openProc[i]) = (openProc[i], openProc[i - 1]);
            pendingMoveUp = -1; geomDirty = true;
        }
        if (pendingAddFlag)
        {
            var step = new ProcessStep { type = pendingAddType };   // default per tipo (crateri o mare)
            // un nuovo BOMBARDAMENTO è un evento diverso: seed casuale → crateri in posti diversi dai precedenti
            // (col default fisso finivano tutti nelle stesse celle). Lo slider "Distribuzione" lo ritocca a mano.
            if (step.type == ProcessType.Crateri) step.seed = Random.Range(0, 10000);
            P.Add(step); openProc.Add(true);
            pendingAddFlag = false; geomDirty = true;
        }

        // un edit di forma segna solo "da ricostruire": il rebuild vero è async e SERIALIZZATO (sotto), così il
        // calcolo pesante del rumore non gira sul main thread e lo slider non lagga.
        if (geomDirty) { geomDirty = false; needsRebuild = true; settleTimer = -1; }

        bool building = smp != null && smp.IsBuilding;
        if (needsRebuild && !building)
        {
            terrain.ApplyRecipe(recipe);                  // main thread, leggero (costruisce la lista di layer)
            if (smp != null) smp.RebuildAsync(terrain, DragRes);   // calcolo su thread: anteprima rapida a bassa res
            needsRebuild = false;
            settleTimer = 0;                              // appena fermi, conta per la rifinitura full res
        }
        else if (!needsRebuild && !building && settleTimer >= 0)
        {
            settleTimer++;
            if (settleTimer > SettleFrames)               // edit assestato: rifinisci a full res + ri-bake normale
            {
                terrain.ApplyRecipe(recipe);
                if (smp != null) smp.RebuildAsync(terrain, FinalRes);
                RebakeCraters();
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
        var sea = recipe.LastSea();   // ultimo mare attivo della pipeline
        var st = Resources.Load<Texture2D>("Textures/" + recipe.soilTexture);
        foreach (var r in faceRenderers)
        {
            var m = r != null ? r.sharedMaterial : null;
            if (m == null) continue;
            m.SetColor("_SoilMean", recipe.soilMean);
            m.SetColor("_MariaColor", recipe.mariaColor);
            m.SetFloat("_MariaScale", recipe.mariaScale);
            m.SetFloat("_MariaStr", recipe.mariaStrength);
            m.SetFloat("_SeaOn", sea != null ? 1f : 0f);
            if (sea != null)
            {
                m.SetFloat("_SeaLevel", recipe.baseRadius + sea.seaLevel);
                m.SetColor("_SeaColor", sea.seaColor);
                m.SetFloat("_SeaSat", sea.seaSaturation);
                m.SetFloat("_SeaRough", sea.seaRoughness);
                m.SetFloat("_SeaRoughScale", sea.seaRoughScale);
                m.SetFloat("_SeaForma", sea.seaForma);
                m.SetFloat("_SeaSeed", sea.seed);
            }
            m.SetFloat("_Saturation", recipe.saturation);
            if (st != null) m.SetTexture("_SoilSand", st);
        }
    }

    void OnGUI()
    {
        if (recipe == null) return;
        float ui = Mathf.Max(1f, Screen.height / 1080f);
        EnsureStyles(ui);
        recipe.Normalize();
        SyncProcFolds();

        float w = 430f * ui, h = Screen.height - 40f * ui;
        var panel = new Rect(16f * ui, 20f * ui, w, h);
        PanelRect = panel;   // la camera orbitale evita lo zoom quando la rotella è qui sopra
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

        // === PROCESSI: lista ORDINATA (dall'alto in basso = sequenza geologica). L'ordine cambia l'effetto:
        // un mare allaga ciò che sta sotto; un bombardamento DOPO il mare scava buche asciutte nell'acqua. ===
        GUILayout.Label("PROCESSI  (l'ordine conta)", head);
        var procs = recipe.processes;
        for (int i = 0; i < procs.Count; i++)
        {
            var p = procs[i];
            string kind = p.type == ProcessType.Mare ? "MARE" : "CRATERI";
            if (!p.enabled) kind += " (off)";
            GUILayout.BeginHorizontal();
            openProc[i] = Foldout(kind + "  " + (i + 1), openProc[i], ui, expand: true);
            if (i > 0 && Button("Su", "Sposta prima nella sequenza (sotto i processi precedenti).", ui, 40f)) pendingMoveUp = i;
            if (i < procs.Count - 1 && Button("Giù", "Sposta dopo nella sequenza.", ui, 40f)) pendingMoveUp = i + 1;
            if (Button("X", "Rimuove questo processo.", ui, 30f)) pendingRemove = i;
            GUILayout.EndHorizontal();
            if (!openProc[i]) continue;
            p.enabled = Toggle("Attiva", "Accende/spegne il processo senza rimuoverlo.", p.enabled, ui, geometry: true, changed: out _);
            if (p.type == ProcessType.Crateri)
            {
                GUILayout.BeginHorizontal();
                if (Button("Rimescola", "Pesca un nuovo seme: stessi crateri, posti diversi.", ui)) { p.seed = Random.Range(0, 10000); geomDirty = true; }
                if (Button("Casuale", "Genera una combinazione casuale di TUTTE le impostazioni di questo bombardamento.", ui)) { RandomizeCrater(p); geomDirty = true; }
                GUILayout.EndHorizontal();
                p.largestRadius = Slider("Raggio max (m)", "Raggio del cratere più grande del campo.", p.largestRadius, 10f, 400f, ui, ref geomDirty);
                p.octaves = Mathf.RoundToInt(Slider("Fasce di taglia", "Quante bande di dimensione (dal raggio max, dimezzando): più fasce = gamma di taglie più ampia.", p.octaves, 1, 7, ui, ref geomDirty));
                p.density = Slider("Quantità", "Quanti crateri in totale: probabilità che una cella ne contenga uno. 0 = nessuno, 1 = fittissimi.", p.density, 0f, 1f, ui, ref geomDirty);
                p.wLarge = Slider("  Grandi", "Quota di crateri GRANDI (1 = pieni, 0 = nessuno).", p.wLarge, 0f, 1f, ui, ref geomDirty);
                p.wMedium = Slider("  Medi", "Quota di crateri MEDI.", p.wMedium, 0f, 1f, ui, ref geomDirty);
                p.wSmall = Slider("  Piccoli", "Quota di crateri PICCOLI.", p.wSmall, 0f, 1f, ui, ref geomDirty);
                p.distribution = Slider("Distribuzione", "Scorri per FAR SCORRERE i crateri sul pianeta (ruota il campo): provi disposizioni diverse senza cambiare il pattern.", p.distribution, 0f, 1f, ui, ref geomDirty);
                p.depthRatio = Slider("Profondità/raggio", "Quanto è profonda la conca rispetto al suo raggio.", p.depthRatio, 0.05f, 0.5f, ui, ref geomDirty);
                p.rimRatio = Slider("Bordo/profondità", "Altezza del bordo rialzato rispetto alla profondità.", p.rimRatio, 0.1f, 0.6f, ui, ref geomDirty);
                p.rimSharpness = Slider("Nitidezza bordi", "Forma della parete: 1 = cono dolce, alto = fondo piatto + bordo a cresta netta.", p.rimSharpness, 1f, 4f, ui, ref geomDirty);
                p.dominant = Toggle("Dominante", "Aggiunge un grande impatto piazzato a mano (tipo Stickney su Phobos).", p.dominant, ui, geometry: true, changed: out _);
                if (p.dominant) p.dominantRadius = Slider("  raggio dominante", "Raggio del grande impatto dominante (m).", p.dominantRadius, 50f, 500f, ui, ref geomDirty);
            }
            else // MARE
            {
                // range del livello adattato al corpo (l'ampiezza del terreno): prima era ±250 fissi → con corpi
                // piccoli ogni millimetro cambiava tutto. Ora la corsa è proporzionata → regolazione fine.
                float seaRange = Mathf.Max(recipe.amplitude * 2f, 60f);
                float prevLevel = p.seaLevel;
                p.seaLevel = Slider("Livello (m)", "Quota del pelo dell'acqua: negativo riempie solo i bacini, positivo sommerge sempre di più.", p.seaLevel, -seaRange, seaRange, ui, ref geomDirty);
                if (p.seaLevel != prevLevel) colorDirty = true;   // anche lo shader segue il pelo
                float prevRough = p.seaRoughness;
                p.seaRoughness = Slider("Rugosità (m)", "Quanto è mosso il terreno del mare: 0 = liscio, su = colline / dune / irregolare.", p.seaRoughness, 0f, 40f, ui, ref geomDirty);
                if (p.seaRoughness != prevRough) colorDirty = true;   // lo shader ricostruisce il pelo
                if (p.seaRoughness > 0f)
                {
                    float prevScale = p.seaRoughScale;
                    p.seaRoughScale = Slider("  scala rilievo", "Dimensione delle forme del fondale: bassa = larghe, alta = fitte.", p.seaRoughScale, 2f, 30f, ui, ref geomDirty);
                    if (p.seaRoughScale != prevScale) colorDirty = true;
                    float prevForma = p.seaForma;
                    p.seaForma = Slider("  forma fondale", "Geometria del fondale: −1 = creste/dune, 0 = liscio, +1 = collinette/gobbe (come dune marziane).", p.seaForma, -1f, 1f, ui, ref geomDirty);
                    if (p.seaForma != prevForma) colorDirty = true;
                }
                p.seaSaturation = Slider("Saturazione mare", "Intensità del colore dell'acqua, indipendente dalla saturazione globale.", p.seaSaturation, 0f, 2f, ui, ref colorDirty);
                p.seaColor.r = Slider("Colore R", "Componente rossa dell'acqua.", p.seaColor.r, 0f, 1f, ui, ref colorDirty);
                p.seaColor.g = Slider("Colore G", "Componente verde dell'acqua.", p.seaColor.g, 0f, 1f, ui, ref colorDirty);
                p.seaColor.b = Slider("Colore B", "Componente blu dell'acqua.", p.seaColor.b, 0f, 1f, ui, ref colorDirty);
            }
            GUILayout.Space(8f * ui);
        }

        // nuova pipeline: in fondo, scegli prima il TIPO → va in coda (ordine = effetto)
        GUILayout.Space(4f * ui);
        if (!chooseType)
        {
            if (GUILayout.Button(new GUIContent("+  Nuova pipeline", "Aggiunge un processo in fondo: scegli crateri o mare."), add, GUILayout.Height(36f * ui)))
                chooseType = true;
        }
        else
        {
            GUILayout.Label("Che tipo?", head);
            GUILayout.BeginHorizontal();
            if (Button("Crateri", "Un bombardamento di crateri, sopra ai processi precedenti.", ui)) { pendingAddType = ProcessType.Crateri; pendingAddFlag = true; chooseType = false; }
            if (Button("Mare", "Allaga ciò che sta sotto fino a una quota.", ui)) { pendingAddType = ProcessType.Mare; pendingAddFlag = true; chooseType = false; }
            if (Button("Annulla", "", ui)) chooseType = false;
            GUILayout.EndHorizontal();
        }
        GUILayout.Space(12f * ui);

        // === FILE ===
        GUILayout.BeginHorizontal();
        if (Button("Salva", "Salva la ricetta come JSON.", ui)) Save();
        if (Button("Carica", "Carica la ricetta col nome corrente.", ui)) Load();
        if (Button("Nuovo (liscio)", "Riparte da una sfera liscia.", ui)) { recipe = PlanetRecipe.SmoothSphere(); recipe.Normalize(); openProc.Clear(); geomDirty = colorDirty = true; }
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

    /// <summary>Tiene la lista degli stati di collasso allineata al numero di processi.</summary>
    void SyncProcFolds()
    {
        while (openProc.Count < recipe.processes.Count) openProc.Add(true);
        while (openProc.Count > recipe.processes.Count) openProc.RemoveAt(openProc.Count - 1);
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

    /// <summary>Combinazione CASUALE di tutte le impostazioni di un bombardamento (tranne il dominante, che
    /// resta una scelta dell'utente). Per esplorare in fretta look diversi.</summary>
    void RandomizeCrater(ProcessStep p)
    {
        p.seed = Random.Range(0, 10000);
        p.largestRadius = Random.Range(20f, 300f);
        p.octaves = Random.Range(2, 7);
        p.density = Random.Range(0.2f, 0.9f);
        p.wLarge = Random.Range(0f, 1f);
        p.wMedium = Random.Range(0f, 1f);
        p.wSmall = Random.Range(0f, 1f);
        p.distribution = Random.Range(0f, 1f);
        p.depthRatio = Random.Range(0.1f, 0.45f);
        p.rimRatio = Random.Range(0.15f, 0.55f);
        p.rimSharpness = Random.Range(1f, 4f);
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
        // azzera prima: una ricetta VECCHIA (senza 'processes') non sovrascriverebbe i processi correnti →
        // resterebbero quelli vecchi. Pulendo, la migrazione (Normalize) riparte pulita da craters/mare.
        recipe.processes.Clear(); recipe.craters.Clear(); recipe.seaEnabled = false;
        JsonUtility.FromJsonOverwrite(File.ReadAllText(path), recipe);
        recipe.Normalize();
        openProc.Clear();
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

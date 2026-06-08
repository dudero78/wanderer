using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Schermata impostazioni a TAB, aperta/chiusa con à. È un banco di prova: espone le manopole del gioco
/// (autopilota, volo, camera) come slider che editano i campi LIVE del PlanetWalker → vedi l'effetto subito.
/// Ogni manopola ha una chiave PlayerPrefs e persiste tra le sessioni (le tarature sopravvivono al riavvio,
/// finché non le riporti nei default del codice). Mentre è aperta congela i comandi e libera il cursore.
///
/// Estendere = una riga: aggiungi un F(...) (slider) o B(...) (toggle) alla tab giusta in Build().
/// </summary>
public class SettingsMenu : MonoBehaviour
{
    // Una manopola: slider float (o toggle, isToggle) con getter/setter sul campo vero e chiave di persistenza.
    class Knob
    {
        public string label, key;
        public bool isToggle;
        public string[] choices;      // se != null: scelta a bottoni (get/set = indice)
        public float min, max, def;   // def = valore ORIGINALE (catturato al primo avvio, prima dei PlayerPrefs)
        public System.Func<float> get;
        public System.Action<float> set;
    }
    class Tab { public string name; public List<Knob> knobs = new List<Knob>(); }

    PlanetWalker walker;
    Camera cam;
    readonly List<Tab> tabs = new List<Tab>();
    string[] tabNames;
    int activeTab;
    bool open;
    Vector2 scroll;
    GUIStyle title, head, val, hint, tabStyle, toggleStyle, toggleBtn;
    Texture2D trackTex, thumbTex;

    public void Init(PlanetWalker w, Camera c)
    {
        walker = w;
        cam = c;
        // Build() PRIMA di toccare i PlayerPrefs: i campi del walker hanno ancora i default del codice (quelli
        // decisi insieme) → li catturo come "default originale" di ogni manopola. Il reset ci torna sempre.
        Build();
        // poi applico le tarature salvate ai campi LIVE (sovrascrivono i default; il reset li può ripristinare).
        foreach (var t in tabs)
            foreach (var k in t.knobs)
                if (!k.isToggle && k.key != null && PlayerPrefs.HasKey(k.key)) k.set(PlayerPrefs.GetFloat(k.key));
    }

    // helper di costruzione manopole. F cattura il default LIVE (= valore di codice, dato che Build gira prima
    // dell'applicazione dei PlayerPrefs). B (toggle) prende il default esplicito.
    Knob F(string label, string key, float min, float max, System.Func<float> get, System.Action<float> set)
    {
        var k = new Knob { label = label, key = "wanderer.tune." + key, min = min, max = max, get = get, set = set };
        k.def = k.get();
        return k;
    }
    Knob B(string label, bool def, System.Func<bool> get, System.Action<bool> set)
        => new Knob { label = label, isToggle = true, def = def ? 1f : 0f, get = () => get() ? 1f : 0f, set = v => set(v > 0.5f) };

    // scelta a BOTTONI (es. risoluzioni): get/set lavorano sull'indice
    Knob C(string label, string[] choices, System.Func<int> get, System.Action<int> set)
        => new Knob { label = label, choices = choices, get = () => get(), set = v => set(Mathf.RoundToInt(v)) };

    void Build()
    {
        var w = walker;

        var ap = new Tab { name = "Autopilota" };
        ap.knobs.Add(B("Autopilota stazionario (hover all'arrivo)", false,
            () => GameSettings.AutopilotStationKeeping, v => { GameSettings.AutopilotStationKeeping = v; GameSettings.Save(); }));
        ap.knobs.Add(B("Stop dolce all'interruzione (T)", true,
            () => GameSettings.AutopilotSoftStop, v => { GameSettings.AutopilotSoftStop = v; GameSettings.Save(); }));
        ap.knobs.Add(F("Decelerazione stop dolce", "softStopAccel", 100f, 2000f, () => w.softStopAccel, v => w.softStopAccel = v));
        ap.knobs.Add(F("Accelerazione iniziale", "autoAccel", 20f, 400f, () => w.autoAccel, v => w.autoAccel = v));
        ap.knobs.Add(F("Accelerazione massima", "autoAccelMax", 100f, 3000f, () => w.autoAccelMax, v => w.autoAccelMax = v));
        ap.knobs.Add(F("Fase gentile (s)", "autoAccelGentle", 0f, 12f, () => w.autoAccelGentle, v => w.autoAccelGentle = v));
        ap.knobs.Add(F("Tempo rampa accel. (s)", "autoAccelRampTime", 1f, 20f, () => w.autoAccelRampTime, v => w.autoAccelRampTime = v));
        ap.knobs.Add(F("Decelerazione (freno)", "autoBrakeAccel", 50f, 1000f, () => w.autoBrakeAccel, v => w.autoBrakeAccel = v));
        ap.knobs.Add(F("Soffitto velocità (sicurezza)", "autoMaxSpeed", 5000f, 100000f, () => w.autoMaxSpeed, v => w.autoMaxSpeed = v));
        ap.knobs.Add(F("Dolcezza allineamento (τ)", "autoTurnTau", 0.1f, 3f, () => w.autoTurnTau, v => w.autoTurnTau = v));
        ap.knobs.Add(F("Quota sorvolo (raggi)", "autoHoverRadii", 0f, 5f, () => w.autoHoverRadii, v => w.autoHoverRadii = v));
        ap.knobs.Add(F("Quota sorvolo (g locale)", "autoHoverG", 1f, 30f, () => w.autoHoverG, v => w.autoHoverG = v));
        tabs.Add(ap);

        var fl = new Tab { name = "Volo" };
        fl.knobs.Add(F("Spinta newtoniana", "newtonThrust", 10f, 200f, () => w.newtonThrust, v => w.newtonThrust = v));
        fl.knobs.Add(F("Onset motori (s)", "thrustRampTime", 0.1f, 5f, () => w.thrustRampTime, v => w.thrustRampTime = v));
        fl.knobs.Add(F("Freno X (picco fascia media)", "brakeAccel", 50f, 600f, () => w.brakeAccel, v => w.brakeAccel = v));
        fl.knobs.Add(F("Freno X alta velocità (s)", "brakeTimeConstant", 0.5f, 8f, () => w.brakeTimeConstant, v => w.brakeTimeConstant = v));
        fl.knobs.Add(F("Freno X coda (τ)", "brakeEaseTau", 0.2f, 3f, () => w.brakeEaseTau, v => w.brakeEaseTau = v));
        fl.knobs.Add(F("Velocità rollio (°/s)", "rollSpeed", 10f, 200f, () => w.rollSpeed, v => w.rollSpeed = v));
        fl.knobs.Add(F("Spinta crociera", "cruiseThrust", 30f, 400f, () => w.cruiseThrust, v => w.cruiseThrust = v));
        fl.knobs.Add(F("Smorzamento crociera", "cruiseDamping", 0.1f, 2f, () => w.cruiseDamping, v => w.cruiseDamping = v));
        fl.knobs.Add(F("Rampa boost crociera (s)", "boostRampTime", 0.5f, 8f, () => w.boostRampTime, v => w.boostRampTime = v));
        tabs.Add(fl);

        var camTab = new Tab { name = "Camera" };
        camTab.knobs.Add(F("Sensibilità mouse", "mouseSensitivity", 0.5f, 6f, () => w.mouseSensitivity, v => w.mouseSensitivity = v));
        camTab.knobs.Add(F("Velocità a piedi", "moveSpeed", 2f, 30f, () => w.moveSpeed, v => w.moveSpeed = v));
        if (cam != null)
            camTab.knobs.Add(F("Campo visivo (FOV)", "fov", 35f, 80f, () => cam.fieldOfView, v => cam.fieldOfView = v));
        tabs.Add(camTab);

        // GRAFICA: risoluzione della texture della Via Lattea (chi ha una macchina meno potente può scendere → meno VRAM).
        // Cambio IMMEDIATO: ricarica la variante senza ricostruire la sfera (FindObjectOfType, è un'azione rara).
        var gfx = new Tab { name = "Grafica" };
        gfx.knobs.Add(C("Risoluzione Via Lattea", new[] { "4k", "8k", "16k" },
            () => GameSettings.SkyTextureRes,
            v => { GameSettings.SkyTextureRes = v; GameSettings.Save(); FindObjectOfType<MilkyWayBand>()?.ApplyResolution(); }));
        tabs.Add(gfx);

        // DIAGNOSI: colorazioni di debug del terreno, live. Slider 0-5 (snappa a interi). key=null → non persiste
        // tra le sessioni (è uno strumento, non una taratura): al riavvio riparte da 0 (off).
        var dg = new Tab { name = "Diagnosi" };
        dg.knobs.Add(B("Menu ESC (pausa) attivo", true,
            () => PauseMenu.Enabled, v => PauseMenu.Enabled = v));   // off → ESC libera solo il cursore (per screenshot senza menu)
        dg.knobs.Add(B("Verifiche parità GPU (readback: rallenta il caricamento)", false,
            () => GpuPlanetRenderer.VerifyGpu, v => GpuPlanetRenderer.VerifyGpu = v));   // vale per i corpi costruiti dopo (sistemi svegliati)
        dg.knobs.Add(new Knob {
            label = "Vista terreno  (0 off · 1 radiale · 2 normale · 3 livello LOD · 4 faccia cubo · 5 fetta)",
            key = null, min = 0f, max = 5f, def = 0f,
            get = () => GpuPlanetRenderer.DebugView,
            set = v => GpuPlanetRenderer.DebugView = Mathf.RoundToInt(v) });
        tabs.Add(dg);

        tabNames = new string[tabs.Count];
        for (int i = 0; i < tabs.Count; i++) tabNames[i] = tabs[i].name;
    }

    public static bool AnyOpen;   // true mentre le impostazioni sono aperte → l'HUD si nasconde (no reticoli sopra il menu)
    public bool IsOpen => open;
    public bool OpenedFromPause;  // true se aperte dal menu ESC → con ESC si TORNA al menu; false se da "à" → ESC chiude
    public void Open(bool fromPause = false) { OpenedFromPause = fromPause; SetOpen(true); }
    public void Close() { SetOpen(false); }

    void Update()
    {
        // toggle con à (inputString cattura il carattere a prescindere dal layout fisico). Aperte da à → NON dal menu.
        bool toggleKey = Input.inputString.Contains("à") || Input.inputString.Contains("À");
        if (toggleKey) { if (open) Close(); else Open(false); }
        // Esc chiude SE il menu di pausa è SPENTO (altrimenti l'ESC lo gestisce il PauseMenu → niente doppio handling).
        else if (!PauseMenu.Enabled && open && Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    void SetOpen(bool v)
    {
        if (open == v) return;
        open = v;
        AnyOpen = open;
        if (walker != null) walker.ControlsActive = !open;   // comandi del walker congelati mentre è aperto
        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;
    }

    void OnGUI()
    {
        if (!open) return;
        float ui = Mathf.Max(1f, Screen.height / 1080f);
        EnsureStyles(ui);

        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = prev;

        float w = 880f * ui, h = 560f * ui;   // più largo: i numeri a destra non si tagliano più
        Rect panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        GUI.Box(panel, GUIContent.none);

        float pad = 26f * ui;
        GUILayout.BeginArea(new Rect(panel.x + pad, panel.y + pad, w - 2f * pad, h - 2f * pad));

        GUILayout.Label("IMPOSTAZIONI", title);
        GUILayout.Space(10f * ui);
        activeTab = GUILayout.Toolbar(activeTab, tabNames, tabStyle, GUILayout.Height(34f * ui));
        GUILayout.Space(12f * ui);

        scroll = GUILayout.BeginScrollView(scroll);
        foreach (var k in tabs[activeTab].knobs) DrawKnob(k, ui);
        GUILayout.EndScrollView();

        GUILayout.Space(8f * ui);
        if (GUILayout.Button("Ripristina default (" + tabs[activeTab].name + ")", tabStyle, GUILayout.Height(32f * ui)))
            ResetTab(tabs[activeTab]);
        GUILayout.Space(4f * ui);
        GUILayout.Label("à o Esc per chiudere  ·  le tarature si salvano da sole · il reset torna ai valori decisi", hint);
        GUILayout.EndArea();
    }

    void DrawKnob(Knob k, float ui)
    {
        GUILayout.BeginHorizontal(GUILayout.Height(40f * ui));
        GUILayout.Label(k.label, head, GUILayout.Width(300f * ui));

        if (k.choices != null)
        {
            // scelta a bottoni: quello attivo è verde
            int cur = Mathf.RoundToInt(k.get());
            Color prevBg = GUI.backgroundColor;
            for (int i = 0; i < k.choices.Length; i++)
            {
                GUI.backgroundColor = i == cur ? new Color(0.3f, 0.7f, 0.4f) : new Color(0.35f, 0.35f, 0.4f);
                if (GUILayout.Button(k.choices[i], toggleBtn, GUILayout.Width(90f * ui), GUILayout.Height(34f * ui)))
                    k.set(i);
            }
            GUI.backgroundColor = prevBg;
        }
        else if (k.isToggle)
        {
            // toggle come BOTTONE grande e leggibile (non la casellina minuscola): verde = attivo, grigio = spento.
            bool b = k.get() > 0.5f;
            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = b ? new Color(0.3f, 0.7f, 0.4f) : new Color(0.35f, 0.35f, 0.4f);
            if (GUILayout.Button(b ? "ATTIVO" : "SPENTO", toggleBtn, GUILayout.Width(150f * ui), GUILayout.Height(34f * ui)))
                k.set(b ? 0f : 1f);
            GUI.backgroundColor = prevBg;
        }
        else
        {
            float v = k.get();
            // Traccia e maniglia DISEGNATE A MANO, entrambe centrate sulla stessa linea (cy) → allineate di sicuro.
            // Lo slider Unity sta sotto INVISIBILE (GUIStyle.none) solo per il trascinamento.
            Rect sr = GUILayoutUtility.GetRect(300f * ui, 28f * ui, GUILayout.Width(300f * ui));
            float cy = sr.y + sr.height * 0.5f, th = 6f * ui, tr = 11f * ui;
            GUI.DrawTexture(new Rect(sr.x, cy - th * 0.5f, sr.width, th), trackTex);
            float nv = GUI.HorizontalSlider(sr, v, k.min, k.max, GUIStyle.none, GUIStyle.none);
            float frac = Mathf.InverseLerp(k.min, k.max, nv);
            float tx = sr.x + frac * sr.width;
            GUI.DrawTexture(new Rect(tx - tr, cy - tr, tr * 2f, tr * 2f), thumbTex);   // maniglia centrata su cy
            GUILayout.Space(16f * ui);
            GUILayout.Label(Fmt(nv), val, GUILayout.Width(90f * ui));
            if (!Mathf.Approximately(nv, v))
            {
                k.set(nv);
                if (k.key != null) { PlayerPrefs.SetFloat(k.key, nv); PlayerPrefs.Save(); }
            }
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(10f * ui);
    }

    // Ripristina ogni manopola della scheda al suo default originale (catturato al primo avvio) e cancella la
    // taratura salvata, così non torna al riavvio. Puoi sperimentare senza paura: il reset riporta sempre indietro.
    void ResetTab(Tab t)
    {
        foreach (var k in t.knobs)
        {
            k.set(k.def);
            if (!k.isToggle && k.key != null) PlayerPrefs.DeleteKey(k.key);
        }
        PlayerPrefs.Save();
    }

    static string Fmt(float v) => Mathf.Abs(v) >= 100f ? v.ToString("F0") : v.ToString("F1");

    void EnsureStyles(float ui)
    {
        if (title == null)
        {
            title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.55f, 0.85f, 1f) } };
            head = new GUIStyle(GUI.skin.label) { normal = { textColor = Color.white } };
            val = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(0.7f, 0.95f, 1f) } };
            hint = new GUIStyle(GUI.skin.label) { normal = { textColor = new Color(0.7f, 0.74f, 0.8f) }, wordWrap = true };
            tabStyle = new GUIStyle(GUI.skin.button);
            toggleStyle = new GUIStyle(GUI.skin.toggle) { normal = { textColor = Color.white }, onNormal = { textColor = Color.white } };
            toggleBtn = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            // TRACCIA grigia (1x1) + MANIGLIA a disco (cerchio sfumato): disegnate a mano, sempre allineate.
            trackTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            trackTex.SetPixel(0, 0, new Color(0.5f, 0.55f, 0.62f, 1f)); trackTex.Apply();
            int n = 32; thumbTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;          // 0 centro, 1 bordo
                    byte a = (byte)(Mathf.Clamp01((1f - d) / 0.12f) * 255f); // disco pieno con bordo morbido
                    px[y * n + x] = new Color32(210, 220, 235, a);
                }
            thumbTex.SetPixels32(px); thumbTex.Apply();
        }
        title.fontSize = Mathf.RoundToInt(22f * ui);
        head.fontSize = Mathf.RoundToInt(16f * ui);
        val.fontSize = Mathf.RoundToInt(16f * ui);
        hint.fontSize = Mathf.RoundToInt(13f * ui);
        tabStyle.fontSize = Mathf.RoundToInt(15f * ui);
        toggleStyle.fontSize = Mathf.RoundToInt(14f * ui);
        toggleBtn.fontSize = Mathf.RoundToInt(15f * ui);
    }
}

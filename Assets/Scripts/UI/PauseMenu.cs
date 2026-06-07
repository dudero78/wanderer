using UnityEngine;

/// <summary>
/// Menu di PAUSA (ESC): Riprendi · Opzioni · Comandi · Torna al menu principale (disattivato) · Esci. La schermata
/// "Comandi" elenca tutti i tasti/scorciatoie (così l'HUD ne tiene solo gli essenziali). Mentre è aperto congela i
/// comandi e libera il cursore (come le impostazioni). È l'AUTORITÀ dell'ESC quando attivo: la mappa ha la
/// precedenza (ESC esce dalla mappa), le impostazioni le chiude lui. Disattivabile da debug (<see cref="Enabled"/>):
/// se spento, ESC torna a fare quello di prima (solo liberare il cursore, gestito dal walker).
/// </summary>
public class PauseMenu : MonoBehaviour
{
    public static bool Enabled = true;   // flag di debug (GameBootstrap / Diagnosi): off → ESC non apre il menu
    public static bool Showing;          // true mentre il menu è aperto → l'HUD (reticoli) si nasconde per non sovrapporsi

    PlanetWalker walker;
    SettingsMenu settings;
    MapMode map;
    enum Page { None, Main, Commands }
    Page page = Page.None;
    bool confirmQuit;
    GUIStyle title, btn, btnOff, item, hint, secStyle, keyStyle;

    public bool IsOpen => page != Page.None;

    public void Init(PlanetWalker w, SettingsMenu s, MapMode m) { walker = w; settings = s; map = m; }

    void Update()
    {
        if (!Enabled) { if (Showing) { Showing = false; Freeze(false); } return; }   // disattivato a runtime: chiudi se aperto
        if (!Input.GetKeyDown(KeyCode.Escape)) return;
        if (map != null && map.Active) return;            // in mappa ESC esce dalla mappa (la gestisce MapMode)
        if (settings != null && settings.IsOpen)
        {
            // ESC nelle impostazioni: se ci sei arrivato DAL menu pausa, TORNI al menu; se da "à", chiudi e basta.
            bool back = settings.OpenedFromPause;
            settings.Close();
            if (back) Open();
            return;
        }
        if (page == Page.None) { Open(); return; }
        // DENTRO il menu, ESC torna INDIETRO di un livello (non esce subito).
        if (confirmQuit) { confirmQuit = false; return; }
        if (page == Page.Commands) { page = Page.Main; return; }
        Close();   // dalla schermata principale, ESC chiude il menu
    }

    void Open() { page = Page.Main; confirmQuit = false; Showing = true; Freeze(true); }
    public void Close() { page = Page.None; confirmQuit = false; Showing = false; Freeze(false); }

    void Freeze(bool f)
    {
        if (walker != null) walker.ControlsActive = !f;
        Cursor.lockState = f ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = f;
    }

    void OnGUI()
    {
        if (page == Page.None) return;
        float ui = Mathf.Max(1f, Screen.height / 1080f);
        EnsureStyles(ui);

        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.72f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = prev;

        if (page == Page.Commands) { DrawCommands(ui); return; }

        float w = 360f * ui, bh = 46f * ui, gap = 12f * ui;
        float h = 60f * ui + 5 * (bh + gap);
        float x = (Screen.width - w) * 0.5f, y = (Screen.height - h) * 0.5f;
        GUI.Label(new Rect(x, y, w, 40f * ui), "PAUSA", title);
        y += 56f * ui;
        bool dlg = confirmQuit;   // col dialogo di conferma aperto, i pulsanti sotto sono disabilitati
        GUI.enabled = !dlg;
        if (GUI.Button(new Rect(x, y, w, bh), "Riprendi", btn)) Close(); y += bh + gap;
        if (GUI.Button(new Rect(x, y, w, bh), "Opzioni", btn)) { Close(); settings?.Open(true); } y += bh + gap;
        if (GUI.Button(new Rect(x, y, w, bh), "Comandi", btn)) page = Page.Commands; y += bh + gap;
        GUI.Button(new Rect(x, y, w, bh), "Torna al menu principale", btnOff); y += bh + gap;   // disattivato (niente menu principale ancora)
        if (GUI.Button(new Rect(x, y, w, bh), "Esci", btn)) confirmQuit = true;
        GUI.enabled = true;

        if (confirmQuit) DrawConfirmQuit(ui);
    }

    void DrawConfirmQuit(float ui)
    {
        float w = 420f * ui, h = 180f * ui;
        float x = (Screen.width - w) * 0.5f, y = (Screen.height - h) * 0.5f;
        Color prev = GUI.color; GUI.color = new Color(0f, 0f, 0f, 0.85f);
        GUI.DrawTexture(new Rect(x - 12f * ui, y - 12f * ui, w + 24f * ui, h + 24f * ui), Texture2D.whiteTexture);
        GUI.color = prev;
        GUI.Box(new Rect(x, y, w, h), GUIContent.none);
        GUI.Label(new Rect(x, y + 26f * ui, w, 34f * ui), "Uscire dal gioco?", title);
        float bw = 160f * ui, bh = 44f * ui, gap = 20f * ui;
        float bx = x + (w - 2f * bw - gap) * 0.5f, by = y + h - bh - 24f * ui;
        if (GUI.Button(new Rect(bx, by, bw, bh), "Esci", btn)) Quit();
        if (GUI.Button(new Rect(bx + bw + gap, by, bw, bh), "Annulla", btn)) confirmQuit = false;
    }

    struct Cmd { public string keys, desc; public Cmd(string k, string d) { keys = k; desc = d; } }
    struct Section { public string title; public Cmd[] cmds; public Section(string t, Cmd[] c) { title = t; cmds = c; } }

    // Comandi organizzati in SEZIONI su DUE colonne (stile schermata controlli da gioco): tasto a "chip" + azione.
    static readonly Section[] ColLeft =
    {
        new Section("A TERRA", new[] { new Cmd("WASD", "cammina"), new Cmd("Mouse", "guarda intorno"), new Cmd("Space", "salta") }),
        new Section("VOLO (con la tuta)", new[] {
            new Cmd("WASD", "spinta"), new Cmd("Space / Shift", "sali / scendi"), new Cmd("Q / E", "rollio"),
            new Cmd("N", "cambia volo (Crociera / Newtoniano)"), new Cmd("X", "freno (match velocity)"),
            new Cmd("T", "autopilota (serve destinazione + in volo)") }),
    };
    static readonly Section[] ColRight =
    {
        new Section("STRUMENTI", new[] { new Cmd("F", "torcia"), new Cmd("M", "mappa"), new Cmd("O", "orbite a schermo"),
            new Cmd("B", "binocolo / telescopio (cielo)"), new Cmd("C", "costellazioni") }),
        new Section("SONDA", new[] { new Cmd("P", "lancia"), new Cmd("V", "guarda attraverso"), new Cmd("K", "richiama"), new Cmd("G", "scatta una foto") }),
        new Section("MAPPA", new[] { new Cmd("Trascina sx / WASD", "sposta"), new Cmd("Trascina dx", "ruota"), new Cmd("Rotella", "zoom"), new Cmd("Click", "seleziona") }),
        new Section("INTERFACCIA", new[] { new Cmd("à", "impostazioni"), new Cmd("è", "perf. on/off"), new Cmd("ESC", "questo menu") }),
    };

    void DrawCommands(float ui)
    {
        float w = 900f * ui, h = 620f * ui;
        float x = (Screen.width - w) * 0.5f, y = (Screen.height - h) * 0.5f;
        GUI.Box(new Rect(x, y, w, h), GUIContent.none);
        GUI.Label(new Rect(x, y + 18f * ui, w, 40f * ui), "COMANDI", title);

        float pad = 40f * ui, colW = (w - 3f * pad) * 0.5f, top = y + 78f * ui;
        DrawColumn(new Rect(x + pad, top, colW, h), ColLeft, ui);
        DrawColumn(new Rect(x + 2f * pad + colW, top, colW, h), ColRight, ui);

        if (GUI.Button(new Rect(x + (w - 200f * ui) * 0.5f, y + h - 54f * ui, 200f * ui, 40f * ui), "Indietro", btn)) page = Page.Main;
    }

    void DrawColumn(Rect r, Section[] sections, float ui)
    {
        GUILayout.BeginArea(r);
        foreach (var sec in sections)
        {
            GUILayout.Label(sec.title, secStyle);
            GUILayout.Space(2f * ui);
            foreach (var c in sec.cmds)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(c.keys, keyStyle, GUILayout.Width(150f * ui));
                GUILayout.Label(c.desc, item, GUILayout.Width(r.width - 162f * ui));
                GUILayout.EndHorizontal();
                GUILayout.Space(3f * ui);
            }
            GUILayout.Space(14f * ui);
        }
        GUILayout.EndArea();
    }

    void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void EnsureStyles(float ui)
    {
        if (title == null)
        {
            title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.55f, 0.85f, 1f) } };
            btn = new GUIStyle(GUI.skin.button);
            btnOff = new GUIStyle(GUI.skin.button) { normal = { textColor = new Color(0.5f, 0.5f, 0.55f) } };   // voce disattivata
            item = new GUIStyle(GUI.skin.label) { normal = { textColor = new Color(0.86f, 0.9f, 0.96f) }, wordWrap = true };
            hint = new GUIStyle(GUI.skin.label) { normal = { textColor = new Color(0.7f, 0.74f, 0.8f) } };
            secStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.55f, 0.85f, 1f) } };
            keyStyle = new GUIStyle(GUI.skin.box) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.9f, 0.96f, 1f) } };
        }
        title.fontSize = Mathf.RoundToInt(26f * ui);
        btn.fontSize = btnOff.fontSize = Mathf.RoundToInt(16f * ui);
        item.fontSize = Mathf.RoundToInt(15f * ui);
        hint.fontSize = Mathf.RoundToInt(13f * ui);
        secStyle.fontSize = Mathf.RoundToInt(15f * ui);
        keyStyle.fontSize = Mathf.RoundToInt(13f * ui);
    }
}

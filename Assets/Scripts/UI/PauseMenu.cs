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
    public static bool Enabled = true;   // flag di debug (GameBootstrap): off → ESC non apre il menu (comportamento di prima)

    PlanetWalker walker;
    SettingsMenu settings;
    MapMode map;
    enum Page { None, Main, Commands }
    Page page = Page.None;
    GUIStyle title, btn, btnOff, item, hint;

    public bool IsOpen => page != Page.None;

    public void Init(PlanetWalker w, SettingsMenu s, MapMode m) { walker = w; settings = s; map = m; }

    void Update()
    {
        if (!Enabled) return;
        if (!Input.GetKeyDown(KeyCode.Escape)) return;
        if (map != null && map.Active) return;            // in mappa ESC esce dalla mappa (la gestisce MapMode)
        if (settings != null && settings.IsOpen) { settings.Close(); return; }   // ESC chiude le impostazioni
        if (page == Page.None) Open(); else Close();
    }

    void Open() { page = Page.Main; Freeze(true); }
    public void Close() { page = Page.None; Freeze(false); }

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
        if (GUI.Button(new Rect(x, y, w, bh), "Riprendi", btn)) Close(); y += bh + gap;
        if (GUI.Button(new Rect(x, y, w, bh), "Opzioni", btn)) { Close(); settings?.Open(); } y += bh + gap;
        if (GUI.Button(new Rect(x, y, w, bh), "Comandi", btn)) page = Page.Commands; y += bh + gap;
        GUI.Button(new Rect(x, y, w, bh), "Torna al menu principale", btnOff); y += bh + gap;   // disattivato (niente menu principale ancora)
        if (GUI.Button(new Rect(x, y, w, bh), "Esci", btn)) Quit();
    }

    void DrawCommands(float ui)
    {
        float w = 760f * ui, h = 560f * ui;
        float x = (Screen.width - w) * 0.5f, y = (Screen.height - h) * 0.5f;
        GUI.Label(new Rect(x, y, w, 40f * ui), "COMANDI", title);
        GUILayout.BeginArea(new Rect(x, y + 56f * ui, w, h - 110f * ui));
        foreach (var line in CommandLines) GUILayout.Label(line, item);
        GUILayout.EndArea();
        if (GUI.Button(new Rect(x, y + h - 46f * ui, 200f * ui, 40f * ui), "Indietro", btn)) page = Page.Main;
    }

    // Elenco completo dei comandi (spostato qui dall'HUD). Una riga = "tasto — azione".
    static readonly string[] CommandLines =
    {
        "Movimento a terra",
        "   WASD — cammina · Mouse — guarda · Space — salta",
        "Volo (con la tuta)",
        "   WASD — spinge · Space — sale · Shift — scende · Q/E — rollio",
        "   N — cambia modello di volo (Crociera / Newtoniano) · X — freno (match velocity)",
        "   T — autopilota verso la destinazione (serve una destinazione + essere in volo)",
        "Strumenti",
        "   F — torcia · M — mappa · O — orbite a schermo · P — lancia la sonda",
        "Sonda",
        "   V — guarda attraverso la sonda · K — richiama · G — scatta una foto",
        "Mappa",
        "   Trascina SINISTRO o WASD — sposta · Trascina DESTRO — ruota · Rotella — zoom · Click — seleziona",
        "Interfaccia",
        "   à — impostazioni · è — mostra/nascondi performance · ESC — questo menu",
    };

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
            item = new GUIStyle(GUI.skin.label) { normal = { textColor = Color.white }, wordWrap = true };
            hint = new GUIStyle(GUI.skin.label) { normal = { textColor = new Color(0.7f, 0.74f, 0.8f) } };
        }
        title.fontSize = Mathf.RoundToInt(24f * ui);
        btn.fontSize = btnOff.fontSize = Mathf.RoundToInt(16f * ui);
        item.fontSize = Mathf.RoundToInt(15f * ui);
        hint.fontSize = Mathf.RoundToInt(13f * ui);
    }
}

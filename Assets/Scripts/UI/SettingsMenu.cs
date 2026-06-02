using UnityEngine;

/// <summary>
/// Schermata impostazioni, aperta/chiusa con il tasto à. Mentre è aperta congela i comandi del giocatore e
/// libera il cursore per cliccare. Disegnata in IMGUI (overlay, niente canvas da configurare), scalata con la
/// risoluzione come gli altri HUD. Le opzioni vivono in GameSettings (statiche + PlayerPrefs): qui si toccano e
/// si salvano. È pensata per CRESCERE — aggiungere un'opzione = una riga in OnGUI.
/// </summary>
public class SettingsMenu : MonoBehaviour
{
    PlanetWalker walker;
    bool open;
    GUIStyle title, label, hint;

    public void Init(PlanetWalker w) { walker = w; }

    void Update()
    {
        // toggle con à. inputString cattura il carattere a prescindere dal layout fisico (à è un tasto diretto
        // sulla tastiera italiana). Escape chiude se aperta.
        bool pressedToggle = Input.inputString.Contains("à") || Input.inputString.Contains("À");
        if (pressedToggle || (open && Input.GetKeyDown(KeyCode.Escape)))
            SetOpen(!open);
    }

    void SetOpen(bool v)
    {
        if (open == v) return;
        open = v;
        // mentre il menù è aperto: comandi del walker congelati e cursore libero per cliccare.
        if (walker != null) walker.ControlsActive = !open;
        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;
    }

    void OnGUI()
    {
        if (!open) return;
        float ui = Mathf.Max(1f, Screen.height / 1080f);
        EnsureStyles(ui);

        // fondo scuro a tutto schermo + pannello centrale.
        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = prev;

        float w = 560f * ui, h = 360f * ui;
        Rect panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        GUI.Box(panel, GUIContent.none);

        float pad = 28f * ui;
        float x = panel.x + pad, y = panel.y + pad, rowW = w - 2f * pad;

        GUI.Label(new Rect(x, y, rowW, 34f * ui), "IMPOSTAZIONI", title);
        y += 48f * ui;

        // --- Opzione: autopilota stazionario ---
        bool prevStation = GameSettings.AutopilotStationKeeping;
        bool station = GUI.Toggle(new Rect(x, y, rowW, 30f * ui), prevStation,
            "  Autopilota stazionario (resta in hover all'arrivo)", label);
        if (station != prevStation) { GameSettings.AutopilotStationKeeping = station; GameSettings.Save(); }
        y += 30f * ui;
        GUI.Label(new Rect(x + 24f * ui, y, rowW - 24f * ui, 56f * ui),
            "Se ATTIVO: a fine viaggio l'autopilota tiene la posizione finché non dai un comando.\n" +
            "Se SPENTO (default): arrivi a distanza di sicurezza e poi manovri tu.", hint);
        y += 64f * ui;

        // chiusura
        GUI.Label(new Rect(x, panel.yMax - pad - 22f * ui, rowW, 22f * ui), "à o Esc per chiudere", hint);
    }

    void EnsureStyles(float ui)
    {
        if (title == null)
        {
            title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.55f, 0.85f, 1f) } };
            label = new GUIStyle(GUI.skin.toggle) { normal = { textColor = Color.white }, onNormal = { textColor = Color.white } };
            hint = new GUIStyle(GUI.skin.label) { normal = { textColor = new Color(0.7f, 0.74f, 0.8f) }, wordWrap = true };
        }
        title.fontSize = Mathf.RoundToInt(22f * ui);
        label.fontSize = Mathf.RoundToInt(16f * ui);
        hint.fontSize = Mathf.RoundToInt(13f * ui);
    }
}

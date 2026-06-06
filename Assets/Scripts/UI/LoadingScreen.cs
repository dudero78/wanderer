using UnityEngine;

/// <summary>
/// Schermata di CARICAMENTO: overlay scuro con uno SPINNER che gira e una serie di messaggi BUFFI che scorrono
/// ("stiamo generando/caricando…"). La crea GameBootstrap PRIMA del lavoro pesante e la chiude con <see cref="Done"/>
/// (dissolvenza). Spinner e messaggi animano sui tempi REALI (unscaled), quando il main thread non è bloccato dal build.
/// </summary>
public class LoadingScreen : MonoBehaviour
{
    static readonly string[] Messages =
    {
        "Generando crateri a mano, uno per uno…",
        "Convincendo i pianeti a restare in orbita…",
        "Lucidando le bombole della tuta…",
        "Calibrando la gravità (di nuovo)…",
        "Insegnando alla sonda a non sprofondare…",
        "Allineando i tropici delle sfere…",
        "Accendendo le stelle lontane…",
        "Pre-scaldando i compute shader…",
        "Verificando che il giocatore non fluttui via…",
        "Spolverando la regolite…",
    };

    bool done;
    float alpha = 1f;
    int msgIdx;
    float nextMsg;
    Texture2D spinnerTex, white;
    GUIStyle msgStyle, tipStyle;

    public void Done() => done = true;

    void Update()
    {
        if (done)
        {
            alpha = Mathf.MoveTowards(alpha, 0f, Time.unscaledDeltaTime * 1.8f);
            if (alpha <= 0f) Destroy(gameObject);
        }
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        float ui = Mathf.Max(1f, Screen.height / 1080f);
        EnsureAssets(ui);

        float rt = Time.realtimeSinceStartup;
        if (rt >= nextMsg) { msgIdx = (msgIdx + 1) % Messages.Length; nextMsg = rt + 0.9f; }

        Color prev = GUI.color;
        // fondo scuro (dissolve in uscita)
        GUI.color = new Color(0.02f, 0.02f, 0.04f, alpha);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), white);

        // SPINNER: arco che ruota attorno al centro (sui tempi reali → gira quando il thread è libero)
        float cx = Screen.width * 0.5f, cy = Screen.height * 0.46f, r = 34f * ui;
        GUI.color = new Color(0.55f, 0.9f, 1f, alpha);
        Matrix4x4 m = GUI.matrix;
        GUIUtility.RotateAroundPivot(rt * 220f, new Vector2(cx, cy));
        GUI.DrawTexture(new Rect(cx - r, cy - r, r * 2f, r * 2f), spinnerTex);
        GUI.matrix = m;

        // messaggio buffo + TITOLO (grande, bold, appena trasparente)
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.Label(new Rect(0, cy + r + 22f * ui, Screen.width, 30f * ui), Messages[msgIdx], msgStyle);
        GUI.color = new Color(0.7f, 0.85f, 1f, alpha * 0.8f);
        GUI.Label(new Rect(0, cy - r - 96f * ui, Screen.width, 70f * ui), "WANDERER", tipStyle);
        GUI.color = prev;
    }

    void EnsureAssets(float ui)
    {
        if (white == null) white = Texture2D.whiteTexture;
        if (spinnerTex == null)
        {
            // arco (ring con coda che sfuma): alfa = banda sottile attorno a r≈0.8, modulata sull'angolo (coda)
            int n = 128; spinnerTex = new Texture2D(n, n, TextureFormat.RGBA32, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float rr = 2f * Mathf.Sqrt(dx * dx + dy * dy);
                    float band = 1f - Mathf.Clamp01(Mathf.Abs(rr - 0.8f) / 0.12f);
                    float ang = Mathf.Atan2(dy, dx) / (2f * Mathf.PI) + 0.5f;   // 0..1 attorno
                    float tail = ang;                                          // coda: sfuma da 1 a 0 lungo il giro
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(band * tail) * 255f));
                }
            spinnerTex.SetPixels32(px); spinnerTex.Apply(true);
        }
        if (msgStyle == null)
        {
            msgStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            tipStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.55f, 0.85f, 1f) } };
        }
        msgStyle.fontSize = Mathf.RoundToInt(19f * ui);
        tipStyle.fontSize = Mathf.RoundToInt(60f * ui);   // titolo grande
        tipStyle.fontStyle = FontStyle.Bold;
    }
}

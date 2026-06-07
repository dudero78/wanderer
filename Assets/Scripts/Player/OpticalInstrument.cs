using UnityEngine;

/// <summary>
/// Strumento ottico per osservare il cielo (binocolo → telescopio). Tasto <see cref="ToggleKey"/> cicla:
/// occhio nudo → binocolo (~10×) → telescopio (~50×) → occhio nudo. Restringe il CAMPO VISIVO della camera
/// (animato) → la magnificazione concentra più luce nello stesso schermo, e <see cref="SkyController"/> (che legge
/// la FOV) fa EMERGERE le stelle deboli e i deep-sky → una macchia sfocata "si risolve" salendo d'ingrandimento.
///
/// Per un'inquadratura FERMA (a 50× ogni micro-movimento è enorme) smorza la sensibilità del mouse ∝ 1/√mag.
/// Disegna una MASCHERA a oculare (cerchio nero attorno) finché è agganciato. Tocca solo resa+comandi, niente fisica.
/// </summary>
public class OpticalInstrument : MonoBehaviour
{
    public const KeyCode ToggleKey = KeyCode.B;   // B = binocolo

    // livelli: 1× (occhio nudo) · 8× (binocolo) · 25× (telescopio). Non troppo spinti: a ingrandimenti enormi il
    // campo è così stretto che vedresti pochissime stelle del catalogo (~119k) → vuoto. 25× tiene il cielo popolato.
    static readonly float[] Mag = { 1f, 8f, 25f };

    Camera cam;
    PlanetWalker walker;
    int level;                 // 0 = occhio nudo
    float nakedFov = 52f;      // FOV a riposo (catturata all'aggancio)
    float targetFov = 52f;
    float baseSensitivity;     // sensibilità mouse del giocatore (catturata all'aggancio)
    Texture2D eyepieceTex;     // vignettatura circolare dell'oculare

    public void Init(Camera camera, PlanetWalker w)
    {
        cam = camera; walker = w;
        nakedFov = targetFov = cam != null ? cam.fieldOfView : 52f;
        eyepieceTex = BuildEyepiece(256);
    }

    void Update()
    {
        if (cam == null) return;

        // in mappa (camere diverse, comandi congelati) torna a occhio nudo
        if (MapMode.IsOpen) { if (level != 0) SetLevel(0); }
        else if (walker == null || walker.ControlsActive)
        {
            if (Input.GetKeyDown(ToggleKey)) SetLevel((level + 1) % Mag.Length);
        }

        // FOV animata verso il bersaglio (transizione morbida)
        cam.fieldOfView = Mathf.MoveTowards(cam.fieldOfView, targetFov,
            Mathf.Max(2f, Mathf.Abs(cam.fieldOfView - targetFov)) * 8f * Time.unscaledDeltaTime);
    }

    void SetLevel(int lv)
    {
        if (lv == level) return;

        // entrando dall'occhio nudo: cattura FOV e sensibilità correnti (rispetta le tarature del giocatore)
        if (level == 0 && lv != 0)
        {
            nakedFov = cam.fieldOfView;
            if (walker != null) baseSensitivity = walker.mouseSensitivity;
        }

        level = lv;
        float mag = Mag[level];
        // FOV target dalla magnificazione: mag = tan(fov0/2)/tan(fov/2)
        targetFov = level == 0 ? nakedFov
                  : 2f * Mathf.Atan(Mathf.Tan(nakedFov * 0.5f * Mathf.Deg2Rad) / mag) * Mathf.Rad2Deg;

        // smorza la vista ∝ 1/√mag (a 50× ~7× più lenta) per un'inquadratura ferma; ripristina a occhio nudo
        if (walker != null)
            walker.mouseSensitivity = level == 0 ? baseSensitivity : baseSensitivity / Mathf.Sqrt(mag);
    }

    void OnGUI()
    {
        if (level == 0 || eyepieceTex == null || MapMode.IsOpen) return;
        if (Event.current.type != EventType.Repaint) return;

        int w = Screen.width, h = Screen.height;
        // oculare CIRCOLARE: il cerchio sta in un quadrato alto come lo schermo, centrato; le bande laterali sono nere.
        float side = h;
        float x0 = (w - side) * 0.5f;
        var black = Texture2D.whiteTexture;
        var prev = GUI.color;
        GUI.color = Color.black;
        if (x0 > 0) { GUI.DrawTexture(new Rect(0, 0, x0, h), black); GUI.DrawTexture(new Rect(x0 + side, 0, x0, h), black); }
        GUI.color = prev;
        GUI.DrawTexture(new Rect(x0, 0, side, side), eyepieceTex);   // vignettatura circolare (nero ai bordi, trasparente al centro)

        // etichetta discreta del livello
        GUI.color = new Color(0.8f, 0.85f, 1f, 0.6f);
        GUI.Label(new Rect(w * 0.5f - 60, h - 40, 120, 24), level == 1 ? "BINOCOLO  10×" : "TELESCOPIO  50×");
        GUI.color = prev;
    }

    /// <summary>Texture quadrata della vignettatura: trasparente nel cerchio centrale, nera (opaca) ai bordi, con
    /// bordo morbido. Disegnata sopra la vista → simula l'oculare. Costruita una volta.</summary>
    static Texture2D BuildEyepiece(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);             // 0 al centro, 1 sul bordo inscritto
                float a = Mathf.SmoothStep(0.80f, 0.96f, r);         // trasparente dentro → nero fuori, bordo morbido
                px[y * size + x] = new Color32(0, 0, 0, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false);
        return tex;
    }
}

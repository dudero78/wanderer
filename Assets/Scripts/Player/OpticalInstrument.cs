using UnityEngine;

/// <summary>
/// Strumento ottico per osservare il cielo (binocolo → telescopio). Tasto <see cref="ToggleKey"/> cicla:
/// occhio nudo → binocolo (8×) → telescopio (25×) → occhio nudo. Restringe il CAMPO VISIVO della camera
/// (animato) → la magnificazione concentra più luce nello stesso schermo, e <see cref="SkyController"/> (che legge
/// la FOV) fa EMERGERE le stelle deboli e i deep-sky → una macchia sfocata "si risolve" salendo d'ingrandimento.
///
/// Per un'inquadratura FERMA (a 50× ogni micro-movimento è enorme) smorza la sensibilità del mouse ∝ 1/√mag.
/// Disegna una MASCHERA a oculare (cerchio nero attorno) finché è agganciato. Tocca solo resa+comandi, niente fisica.
/// </summary>
public class OpticalInstrument : MonoBehaviour
{
    public const KeyCode ToggleKey = KeyCode.B;   // B = binocolo
    public const KeyCode PhotoKey = KeyCode.G;    // G = scatta foto (quando sei all'oculare)
    float photoFlash; bool flashQueued;

    // B cicla 3 stati: 1× (occhio nudo) · 7× (binocolo) · 20× (telescopio). Sul telescopio la ROTELLA alza ancora
    // l'ingrandimento (fino a ×WheelMax → 80×). Così con 2 click di B entri/esci, niente 4 livelli da ciclare.
    static readonly float[] Mag = { 1f, 7f, 20f };
    const float WheelMax = 4f;   // moltiplicatore max della rotella sul telescopio (20× → 80×)

    Camera cam;
    PlanetWalker walker;
    int level;                 // 0 = occhio nudo
    float wheelZoom = 1f;      // moltiplicatore extra della rotella, attivo solo sul telescopio (livello 2)
    bool animating;            // true mentre la FOV sta transitando (solo allora lo strumento la guida)
    float nakedFov = 52f;      // FOV a riposo (catturata all'aggancio)
    float targetFov = 52f;
    float baseSensitivity;     // sensibilità mouse del giocatore (catturata all'aggancio)
    GUIStyle hudStyle;
    Texture2D eyepieceTex;     // vignettatura circolare dell'oculare

    float EffectiveMag => Mag[level] * (level == Mag.Length - 1 ? wheelZoom : 1f);

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
            // foto col telescopio (G): solo quando sei all'oculare (la sonda usa G solo in vista-sonda → niente conflitto)
            if (level > 0 && Input.GetKeyDown(PhotoKey)) TakePhoto();
            // ROTELLA sul telescopio (ultimo livello): alza/abbassa l'ingrandimento oltre il fisso
            if (level == Mag.Length - 1)
            {
                float w = Input.mouseScrollDelta.y;
                if (Mathf.Abs(w) > 0.01f)
                {
                    wheelZoom = Mathf.Clamp(wheelZoom + w * 0.35f, 1f, WheelMax);
                    RecomputeTarget();
                }
            }
        }
        if (flashQueued) { flashQueued = false; photoFlash = 0.8f; }   // il flash parte il frame DOPO lo scatto (non finisce nella foto)
        if (photoFlash > 0f) photoFlash -= Time.unscaledDeltaTime;

        // La FOV la guida lo strumento SOLO durante l'animazione di zoom. A occhio nudo e fermo, NON la tocca: così lo
        // slider FOV delle opzioni funziona (prima la riportava indietro ogni frame). Seguo il valore scelto dal giocatore.
        if (animating)
        {
            cam.fieldOfView = Mathf.MoveTowards(cam.fieldOfView, targetFov,
                Mathf.Max(2f, Mathf.Abs(cam.fieldOfView - targetFov)) * 8f * Time.unscaledDeltaTime);
            if (Mathf.Abs(cam.fieldOfView - targetFov) < 0.01f) { cam.fieldOfView = targetFov; animating = false; }
        }
        else if (level == 0)
        {
            nakedFov = cam.fieldOfView;   // a occhio nudo segui lo slider FOV, non combatterlo
        }
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
        wheelZoom = 1f;   // azzera la rotella a ogni cambio di livello
        RecomputeTarget();
    }

    void RecomputeTarget()
    {
        animating = true;
        float mag = EffectiveMag;
        // FOV target dalla magnificazione: mag = tan(fov0/2)/tan(fov/2)
        targetFov = level == 0 ? nakedFov
                  : 2f * Mathf.Atan(Mathf.Tan(nakedFov * 0.5f * Mathf.Deg2Rad) / mag) * Mathf.Rad2Deg;

        // smorza la vista ∝ 1/√mag per un'inquadratura ferma; ripristina a occhio nudo
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

        // etichetta del livello — ingrandimento EFFETTIVO (con la rotella), font scalato alla risoluzione
        int fs = Mathf.Max(14, Mathf.RoundToInt(h / 52f));
        if (hudStyle == null || hudStyle.fontSize != fs)
            hudStyle = new GUIStyle(GUI.skin.label) { fontSize = fs, alignment = TextAnchor.MiddleCenter };
        GUI.color = new Color(0.82f, 0.88f, 1f, 0.85f);
        string nome = level == 1 ? "BINOCOLO" : "TELESCOPIO";
        string extra = level == Mag.Length - 1 ? "   ROTELLA = zoom" : "";
        GUI.Label(new Rect(w * 0.5f - fs * 12f, h - fs * 2.2f, fs * 24f, fs * 1.5f),
                  $"{nome}  {EffectiveMag:0}×     G = foto{extra}", hudStyle);
        GUI.color = prev;

        // flash dello scatto (parte il frame DOPO la foto → non finisce nell'immagine)
        if (photoFlash > 0f)
        {
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(photoFlash) * 0.55f);
            GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }

    void TakePhoto()
    {
        string path = System.IO.Path.Combine(PhotoDir(), $"cielo_{Time.frameCount}.png");
        ScreenCapture.CaptureScreenshot(path);
        Debug.Log($"[telescopio] foto salvata: {path}");
        flashQueued = true;   // il flash parte il frame DOPO → non finisce nella foto
    }

    /// <summary>Cartella foto = Documenti/Wanderer/Foto (come la sonda).</summary>
    static string PhotoDir()
    {
        string docs = (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor)
            ? System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments)
            : System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Documents");
        string dir = System.IO.Path.Combine(docs, "Wanderer", "Foto");
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    // smoothstep "vera" (alla GLSL): 0 per x≤e0, 1 per x≥e1, ramp morbida in mezzo. NON Mathf.SmoothStep (che è un
    // lerp fra e0 ed e1 e NON arriva a 0/1 → era il bug che rendeva la maschera un velo grigio uniforme, niente cerchio).
    static float Smooth01(float e0, float e1, float x)
    {
        float t = Mathf.Clamp01((x - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }

    /// <summary>Texture quadrata dell'oculare: ALPHA 0 nel cerchio centrale (vedi il cielo) → ALPHA 1 fuori (nero
    /// opaco), con bordo morbido. Disegnata sopra la vista, quadrato alto come lo schermo + bande nere ai lati → un
    /// vero CERCHIO. Una leggera vignettatura interna scurisce verso il bordo per il "feel" ottico. Costruita una volta.</summary>
    static Texture2D BuildEyepiece(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);             // 0 al centro, 1 sul bordo inscritto, ~1.41 agli angoli
                float a = Smooth01(0.78f, 0.94f, r);                 // 0 dentro (trasparente) → 1 fuori (nero), bordo morbido
                px[y * size + x] = new Color32(0, 0, 0, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false);
        return tex;
    }
}

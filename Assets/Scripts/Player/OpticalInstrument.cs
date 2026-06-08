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
    // l'ingrandimento (fino a ×WheelMax → 160×). Così con 2 click di B entri/esci, niente 4 livelli da ciclare.
    static readonly float[] Mag = { 1f, 7f, 20f };
    const float WheelMax = 8f;   // moltiplicatore max della rotella sul telescopio (20× → 160×, per oggetti piccoli/deboli)

    Camera playerCam;
    PlanetWalker walker;
    ProbeController probe;      // l'altro osservatore: in vista-sonda il telescopio zooma la SUA camera
    Camera boundCam;           // la camera attualmente agganciata (giocatore o sonda)
    bool boundIsProbe;         // l'aggancio corrente è la sonda? (decide quale sensibilità mouse smorzare)
    int level;                 // 0 = occhio nudo
    float wheelZoom = 1f;      // moltiplicatore extra della rotella, attivo solo sul telescopio (livello 2)
    bool animating;            // true mentre la FOV sta transitando (solo allora lo strumento la guida)
    float nakedFov = 52f;      // FOV a riposo (catturata all'aggancio)
    float targetFov = 52f;
    float baseSensitivity;     // sensibilità mouse dell'osservatore agganciato (catturata all'aggancio)
    GUIStyle hudStyle;
    Texture2D eyepieceTex;     // vignettatura circolare dell'oculare

    float EffectiveMag => Mag[level] * (level == Mag.Length - 1 ? wheelZoom : 1f);

    // OSSERVATORE attivo: in vista-sonda è la camera della sonda, altrimenti quella del giocatore. Deciso qui (non da
    // SkyController) così non dipende dall'ordine di update: appena entri/esci dalla sonda lo strumento si ri-aggancia.
    bool ProbeView => probe != null && probe.Viewing && probe.Probe != null && probe.Probe.Cam != null;
    Camera ActiveCam => ProbeView ? probe.Probe.Cam : playerCam;

    public void Init(Camera camera, PlanetWalker w, ProbeController p)
    {
        playerCam = camera; walker = w; probe = p;
        nakedFov = targetFov = camera != null ? camera.fieldOfView : 52f;
        eyepieceTex = BuildEyepiece(512);
    }

    void Update()
    {
        Camera ac = ActiveCam;
        // la camera attiva è cambiata (giocatore ⇄ sonda)? RIPRISTINA la vecchia (FOV+sensibilità) e ri-aggancia a
        // occhio nudo sulla nuova → nessuna camera resta "incastrata" zoomata, e la sonda riparte dal suo grandangolo.
        if (ac != boundCam) BindTo(ac);
        if (boundCam == null) return;

        // in mappa torna a occhio nudo; altrimenti l'osservatore è "interattivo" se i comandi del giocatore sono attivi
        // OPPURE se stai guardando attraverso la sonda (lì i comandi del giocatore sono congelati apposta).
        if (MapMode.IsOpen) { if (level != 0) SetLevel(0); }
        else if (ProbeView || walker == null || walker.ControlsActive)
        {
            if (Input.GetKeyDown(ToggleKey)) SetLevel((level + 1) % Mag.Length);
            // foto col telescopio (G): solo dal GIOCATORE. In vista-sonda G è già la foto della sonda (ProbeController) →
            // non lo gestisco qui per non scattare due volte.
            if (!ProbeView && level > 0 && Input.GetKeyDown(PhotoKey)) TakePhoto();
            // ROTELLA sul telescopio (ultimo livello): alza/abbassa l'ingrandimento oltre il fisso
            if (level == Mag.Length - 1)
            {
                float w = Input.mouseScrollDelta.y;
                if (Mathf.Abs(w) > 0.01f)
                {
                    // clampo il delta per-evento: su Mac la rotella manda "momentum" con valori grandi e irregolari →
                    // limitarlo rende i passi più uniformi (e lo smorzamento li anima dolcemente).
                    wheelZoom = Mathf.Clamp(wheelZoom + Mathf.Clamp(w, -2f, 2f) * 0.3f, 1f, WheelMax);
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
            // smorzamento ESPONENZIALE (frame-rate independent): la FOV ease verso il target con costante ~0.08s. Anche i
            // passi piccoli della rotella ad alto ingrandimento si ANIMANO (prima un "pavimento" di velocità li snappava
            // → sembravano scatti). La FOV insegue il target anche mentre scrolli di continuo → zoom fluido.
            float k = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
            boundCam.fieldOfView = Mathf.Lerp(boundCam.fieldOfView, targetFov, k);
            if (Mathf.Abs(boundCam.fieldOfView - targetFov) < 0.0008f * targetFov) { boundCam.fieldOfView = targetFov; animating = false; }
        }
        else if (level == 0)
        {
            nakedFov = boundCam.fieldOfView;   // a occhio nudo segui lo slider FOV / il grandangolo della sonda, non combatterlo
        }
    }

    /// <summary>Aggancia lo strumento a una camera (giocatore o sonda): RIPRISTINA prima quella vecchia (riporta FOV e
    /// sensibilità ai valori a riposo), poi cattura il nuovo osservatore a occhio nudo. Robusto: nessuno stato zoomato
    /// resta appeso quando passi da una vista all'altra.</summary>
    void BindTo(Camera c)
    {
        if (boundCam != null) { boundCam.fieldOfView = nakedFov; SetSensitivity(baseSensitivity); }
        boundCam = c;
        boundIsProbe = ProbeView;
        level = 0; wheelZoom = 1f; animating = false;
        nakedFov = targetFov = c != null ? c.fieldOfView : 52f;
        baseSensitivity = GetSensitivity();
    }

    float GetSensitivity() => boundIsProbe ? (probe != null ? probe.lookSensitivity : 1f)
                                           : (walker != null ? walker.mouseSensitivity : 1f);
    void SetSensitivity(float v)
    {
        if (boundIsProbe) { if (probe != null) probe.lookSensitivity = v; }
        else if (walker != null) walker.mouseSensitivity = v;
    }

    void SetLevel(int lv)
    {
        if (lv == level) return;

        // entrando dall'occhio nudo: cattura FOV e sensibilità correnti (rispetta le tarature dell'osservatore)
        if (level == 0 && lv != 0)
        {
            if (boundCam != null) nakedFov = boundCam.fieldOfView;
            baseSensitivity = GetSensitivity();
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
        SetSensitivity(level == 0 ? baseSensitivity : baseSensitivity / Mathf.Sqrt(mag));
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

        // etichetta del livello — ingrandimento EFFETTIVO (con la rotella), font scalato alla risoluzione. Nascosta
        // nelle foto pulite (l'oculare resta: è l'inquadratura del cannocchiale, non "HUD").
        if (!GameSettings.HudHiddenForPhoto)
        {
            int fs = Mathf.Max(14, Mathf.RoundToInt(h / 52f));
            if (hudStyle == null || hudStyle.fontSize != fs)
                hudStyle = new GUIStyle(GUI.skin.label) { fontSize = fs, alignment = TextAnchor.MiddleCenter };
            GUI.color = new Color(0.82f, 0.88f, 1f, 0.85f);
            string nome = level == 1 ? "BINOCOLO" : "TELESCOPIO";
            string extra = level == Mag.Length - 1 ? "   ROTELLA = zoom" : "";
            GUI.Label(new Rect(w * 0.5f - fs * 12f, h - fs * 2.2f, fs * 24f, fs * 1.5f),
                      $"{nome}  {EffectiveMag:0}×     G = foto{extra}", hudStyle);
            GUI.color = prev;
        }

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
        GameSettings.SuppressHudForPhoto();   // foto pulite: nasconde l'etichetta dello strumento su questo frame
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
                float a = Smooth01(0.962f, 0.972f, r);               // 0 dentro (trasparente) → 1 fuori (nero): bordo NETTO (field stop), appena anti-aliased
                px[y * size + x] = new Color32(0, 0, 0, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false);
        return tex;
    }
}

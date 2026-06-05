using UnityEngine;

/// <summary>
/// Governa il frame rate di RENDERING. Bersaglio: 60 fps quando c'è movimento (mouse, tasti, o il
/// giocatore che sfreccia), dove la fluidità si sente; scende quando la scena è ferma, dove a occhio
/// non cambia nulla ma si risparmiano giri di rendering.
///
/// La fisica resta a 60 Hz (Time.fixedDeltaTime, dal bootstrap) a prescindere dal frame rate di
/// rendering: precisione delle orbite e reattività dei comandi non cambiano mai. Il governo tocca
/// SOLO quante volte ridisegniamo.
///
/// Manopole pubbliche: per disattivare il governo metti idleFps = activeFps.
/// </summary>
public class PerformanceGovernor : MonoBehaviour
{
    public int activeFps = 60;      // quando ti muovi o guardi intorno
    public int idleFps = 30;        // quando la scena è ferma: a occhio quasi identica, meno lavoro
    public float idleDelay = 0.3f;  // secondi di immobilità prima di scendere
    public float moveThreshold = 2f; // m/s sopra cui il giocatore è "in movimento" (volo balistico)

    // Stato letto da chi misura il carico dal frame-time (es. RenderScaler): un frame lento MENTRE capiamo a
    // idleFps è "avvelenato" dal cap (non GPU satura) → va ignorato come segnale di affanno.
    public static int TargetFps = 60;
    public static bool IdleCapped;

    PlanetWalker walker;
    float lastActiveTime;

    void Start()
    {
        QualitySettings.vSyncCount = 0;   // il cap fps non funziona con vSync attivo
        // il walker non esiste ancora quando il bootstrap aggiunge questo componente:
        // lo cerchiamo qui, a scena costruita. Se manca, restiamo semplicemente a activeFps.
        walker = FindAnyObjectByType<PlanetWalker>();
        lastActiveTime = Time.unscaledTime;
        Application.targetFrameRate = activeFps;
        TargetFps = activeFps;
        IdleCapped = false;
    }

    void Update()
    {
        if (IsActive()) lastActiveTime = Time.unscaledTime;

        bool idle = Time.unscaledTime - lastActiveTime > idleDelay;
        int target = idle ? idleFps : activeFps;
        TargetFps = target;
        IdleCapped = idle && idleFps < activeFps;   // frame lento ora = cap, non GPU satura

        // riassegna solo al cambio: evitare di scrivere targetFrameRate ogni frame
        if (Application.targetFrameRate != target)
            Application.targetFrameRate = target;
    }

    bool IsActive()
    {
        // mouse-look: anche un piccolo movimento del mouse conta come "stai guardando"
        if (Mathf.Abs(Input.GetAxisRaw("Mouse X")) > 0.0001f) return true;
        if (Mathf.Abs(Input.GetAxisRaw("Mouse Y")) > 0.0001f) return true;
        // qualsiasi tasto premuto (WASD, Space, Shift, F, N, X, ...) = stai agendo
        if (Input.anyKey) return true;
        // volo balistico: nessun tasto premuto ma stai sfrecciando (newtoniano in coast).
        // Senza questo la scena scenderebbe a 30 mentre il mondo ti scorre accanto → scattoso.
        if (walker != null && walker.Speed > moveThreshold) return true;
        return false;
    }
}

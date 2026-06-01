using UnityEngine;

/// <summary>
/// Governa il frame rate per contenere il calore senza toccare la resa.
///
/// Il costo pesante della scena è lo shader procedurale del pianeta, che gira per
/// ogni pixel a ogni fotogramma. Ridisegnare un'immagine FERMA 60 volte al secondo
/// è lavoro sprecato: a occhio è identica a 30 fps. Quindi:
///   - 60 fps quando c'è movimento (mouse, tasti, o il giocatore che si muove davvero),
///     dove la fluidità si sente;
///   - 30 fps quando la scena è immobile, dove non si vede la differenza ma la GPU
///     lavora la metà → molto meno calore.
///
/// La fisica resta a 60 Hz (Time.fixedDeltaTime, impostato nel bootstrap) a
/// prescindere dal frame rate di rendering: precisione delle orbite e reattività dei
/// comandi non cambiano mai. Abbassare gli fps tocca SOLO quante volte ridisegniamo.
///
/// Tutte le soglie sono manopole pubbliche: per girare sempre a 30 basta activeFps=30,
/// per disattivare il governo idleFps=activeFps.
/// </summary>
public class PerformanceGovernor : MonoBehaviour
{
    // I profili (Stats) dicono che la GPU finisce il frame in ~1 ms: il collo di bottiglia e il
    // CALORE sono il MAIN THREAD CPU, che a 60 fps rifà il loop completo 60 volte/s per niente
    // (la GPU avrebbe finito comunque). Quindi il cap fps qui è la leva DIRETTA sul calore: meno
    // fps = meno giri di CPU al secondo = meno watt. 30 attivi è il registro di Outer Wilds e con
    // questa GPU si disegna senza sforzo. Per più fluidità rimetti 60 (più caldo, è il prezzo).
    public int activeFps = 30;      // quando ti muovi o guardi intorno
    public int idleFps = 15;        // quando la scena è ferma: a occhio identica, metà CPU
    public float idleDelay = 0.3f;  // secondi di immobilità prima di scendere
    public float moveThreshold = 2f; // m/s sopra cui il giocatore è "in movimento" (volo balistico)

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
    }

    void Update()
    {
        if (IsActive()) lastActiveTime = Time.unscaledTime;

        bool idle = Time.unscaledTime - lastActiveTime > idleDelay;
        int target = idle ? idleFps : activeFps;

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

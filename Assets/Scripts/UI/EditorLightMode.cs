using UnityEngine;

/// <summary>
/// Modo luce dell'EDITOR (toggle col tasto L). Due modi:
///
/// - ANCORATA (default al caricamento): il sole è fisso nel mondo e il pianeta non gira, quindi orbitando con
///   la camera la STESSA faccia resta illuminata. Si valuta il look reale, il terminatore, il rilievo radente.
/// - LIBERA: il sole resta fisso RISPETTO ALLA VISTA (viene sempre da destra, radente). Orbitando è come
///   RUOTARE IL PIANETA sotto quella luce → qualunque faccia porti davanti si illumina. Serve a ispezionare
///   ogni lato del pianeta senza zone perennemente in ombra.
///
/// Non tocca i controlli (il tasto destro resta "orbita"): cambia solo a cosa è agganciata la direzione del
/// sole — al mondo (ancorata) o al frame della camera (libera). Aggiorna sia la mesh CPU (ruotando la luce
/// direzionale, che illumina lo shader standard/baked) sia l'anteprima GPU (uniform _SunDir via RefreshLighting).
/// </summary>
public class EditorLightMode : MonoBehaviour
{
    public Light sun;
    public Transform cam;
    public GpuPlanetSurface gpu;

    /// <summary>true = luce libera (agganciata alla vista). Letto dall'HUD dell'editor. Default = ancorata.</summary>
    public static bool Free = false;

    Quaternion anchored;   // rotazione di default del sole (la "luce ancorata" da destra)

    void Start()
    {
        Free = false;                                   // ogni avvio parte ancorata, come da progetto
        if (sun != null) anchored = sun.transform.rotation;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.L))
        {
            Free = !Free;
            if (!Free && sun != null) sun.transform.rotation = anchored;   // torna alla luce fissa di default
            if (gpu != null) gpu.RefreshLighting();
        }

        if (Free && sun != null && cam != null)
        {
            // Sole agganciato al frame della camera: prevalentemente FRONTALE e DALL'ALTO (~35° fuori asse),
            // un filo da destra → la maggior parte del disco è illuminata, resta solo una falce d'ombra (~1/8)
            // in basso-sinistra; il rilievo resta leggibile e ogni faccia che ruoti davanti si illumina.
            // Direzione VERSO il sole; la luce direzionale "guarda" nel verso opposto.
            Vector3 toSun = (cam.right * 0.35f + cam.up * 0.45f - cam.forward * 0.82f).normalized;
            sun.transform.rotation = Quaternion.LookRotation(-toSun);
            if (gpu != null) gpu.RefreshLighting();
        }
    }
}

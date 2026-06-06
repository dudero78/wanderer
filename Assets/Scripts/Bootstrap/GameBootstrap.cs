using UnityEngine;

/// <summary>
/// Costruisce l'intera scena da codice: stella, pianeta, giocatore, camera e
/// luce. È la filosofia del progetto resa concreta — nessun setup manuale
/// nell'editor, tutto autorabile e modificabile dal codice. Basta un
/// GameObject con questo componente nella scena (lo crea la voce di menu
/// "Wanderer/Crea scena demo").
/// </summary>
public class GameBootstrap : MonoBehaviour
{
    [Header("Rendering dei corpi rocciosi")]
    [Tooltip("ON = resa GPU (percorso B1): geometria calcolata sulla GPU, 1 draw indirect, colore procedurale, "
           + "niente bake. Tappa 1 = risoluzione fissa, ancora niente LOD. Ha la precedenza su useQuadtree. "
           + "Se la GPU non supporta i compute, ripiega sul quadtree.")]
    public bool useGpuSurface = true;
    [Tooltip("Solo se useGpuSurface=ON: vertici interni per lato di ogni faccia (fisso, Tappa 1).")]
    public int gpuSurfaceRes = 256;
    [Tooltip("ON = quadtree CDLOD (geometria view-dependent, crateri nitidi calpestabili, look Elite/SC). "
           + "OFF = mesh singola a risoluzione fissa (fallback, niente LOD).")]
    public bool useQuadtree = true;
    [Tooltip("Solo se useQuadtree=OFF: risoluzione della mesh singola per faccia (build su thread).")]
    public int singleMeshRes = 320;
    [Tooltip("Riempi le fette del LOD in UN dispatch invece che uno per nodo (meno chiamate API → meno churn CPU). "
           + "Si attiva solo se la VERIFICA di parità batch↔per-nodo all'avvio è verde (log [batch-fill]); altrimenti "
           + "ripiega da solo sul path per-nodo. Parità confermata (max diff 0) → ON di default.")]
    public bool useBatchFill = true;

    [Tooltip("GEOMORPH: transizioni LOD lisce nel vertex shader. SPEGNILO per A/B se vedi artefatti di geometria "
           + "(blob 'sciolti' / spuntoni) sui crateri densi a bassa quota.")]
    public bool useGeomorph = true;

    [Tooltip("OVERDRAW: interno Cull Back + skirt Cull Off in 2 draw (due materiali) → dimezza l'ombreggiatura "
           + "per-pixel del terreno. Se l'INTERNO del pianeta SPARISCE accendendolo, metti interiorCull=1 (Front).")]
    public bool useCullSplit = true;
    public int interiorCull = 1;   // 1=Front (verificato: l'interno è Front-facing; con 2/Back le geometrie si ribaltano)

    [Tooltip("DEBUG/test: nasci su questo corpo invece che sul pianeta-casa (es. \"terra-test3\"). Vuoto = pianeta-casa.")]
    public string spawnOnBody = "Valentina2";

    [Header("Diagnosi superficie (anche live dal menu à in gioco)")]
    [Tooltip("Colorazione di debug del terreno: 0=off · 1=posizione radiale (geometria pura) · 2=normale di mondo "
           + "(shading) · 3=livello di LOD · 4=faccia del cubo · 5=fetta (ogni slab un colore). Serve a capire se un "
           + "difetto è geometria/ricetta (dentro un colore-fetta) o struttura del LOD (sui bordi/livelli).")]
    public int debugView = 0;

    [Tooltip("DIAGNOSI ricetta (build-time): salta interi TIPI di pipeline per scoprire chi genera un difetto. "
           + "Bitmask: 1=Crateri, 2=Mare, 4=Tettonica (somma per più di uno; es. 5 = niente crateri+tettonica). "
           + "0 = tutte attive. Salta sia su GPU che sul walker → la parità resta verde. Cambialo e ri-premi Play.")]
    public int debugDisablePipelines = 0;

    void Start()
    {
        // impostazioni di gioco (facilitazioni opzionali, regolabili dal menù à) lette da PlayerPrefs.
        GameSettings.Load();

        // Frame rate governato in modo dinamico: 60 fps quando ti muovi o guardi intorno,
        // 30 quando la scena è ferma. Un fotogramma immobile a 30 è a occhio identico a 60,
        // ma la GPU (che spende quasi tutto sullo shader procedurale del pianeta, per-pixel
        // a ogni frame) lavora la metà → molto meno calore, senza perdere resa. Dettagli e
        // manopole in PerformanceGovernor.
        QualitySettings.vSyncCount = 0;
        gameObject.AddComponent<PerformanceGovernor>();
        // fisica fissa a 60 Hz, disaccoppiata dal rendering: orbite precise e yaw reattivo
        // quanto il pitch, anche quando il rendering scende a 30.
        Time.fixedDeltaTime = 1f / 60f;

        var solar = gameObject.AddComponent<SolarSystem>();
        // 1 = ritmo di gioco. La velocità orbitale del pianeta (~628 m/s) è già quella che il freno X
        // deve domare per sincronizzarti con un corpo: accelerare il tempo la gonfia e rende il match
        // velocity ingiocabile. Alza questo SOLO per osservare le orbite veloci (debug), non in volo.
        solar.TimeScale = 1.0;

        // --- COMPOSIZIONE della scena, ogni pezzo ISOLATO nel suo file (niente "minestrone" qui): sistema solare →
        //     spawn del giocatore → illuminazione → interfaccia. Aggiungere/cambiare un pezzo = nel suo Setup, non qui. ---
        GpuPlanetRenderer.UseBatchFill = useBatchFill;   // PRIMA della Build: la verifica gira nel Setup di ogni corpo
        GpuPlanetRenderer.UseGeomorph = useGeomorph;     // A/B per isolare gli artefatti di geometria
        GpuPlanetRenderer.CullSplit = useCullSplit;      // overdraw: interno Cull Back + skirt Cull Off (2 materiali)
        GpuPlanetRenderer.InteriorCull = interiorCull;
        GpuPlanetRenderer.DebugView = debugView;          // diagnosi superficie (poi pilotabile live dal menu à)
        PlanetRecipe.DebugDisableTypes = debugDisablePipelines;   // diagnosi: salta tipi di pipeline (build-time, GPU+CPU)
        var sys = SolarSystemSetup.Build(solar, useQuadtree, singleMeshRes, useGpuSurface, gpuSurfaceRes, spawnOnBody);
        var rig = PlayerSpawn.Spawn(solar, sys.HomePlanetGo, sys.HomeTerrain, sys.StarTransform);
        LightingSetup.Setup(gameObject, solar, sys.StarTransform, sys.HomePlanetGo.transform);
        UiSetup.Setup(gameObject, solar, rig, sys);
    }
}

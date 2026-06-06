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

    [Tooltip("PBR per PENDENZA (GPU-4): roccia esposta sui versanti ripidi (bordi/pareti dei crateri) + speculare "
           + "GGX leggero del suolo. Look SC/ED. Spegnilo per A/B o se preferisci il suolo puramente Lambert.")]
    public bool usePbrTerrain = true;

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

    [Tooltip("Menu di PAUSA con ESC (Riprendi/Opzioni/Comandi/Esci). Se OFF, ESC non apre nulla (come prima: "
           + "libera solo il cursore).")]
    public bool enablePauseMenu = true;

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

        // SCHERMATA DI CARICAMENTO: appare subito; la costruzione gira in COROUTINE, così la schermata si vede prima
        // del lavoro pesante e resta un minimo (i messaggi buffi scorrono, lo spinner gira).
        var loading = new GameObject("LoadingScreen").AddComponent<LoadingScreen>();
        StartCoroutine(Boot(loading));
    }

    System.Collections.IEnumerator Boot(LoadingScreen loading)
    {
        float bootT0 = Time.realtimeSinceStartup;
        yield return null; yield return null;   // 2 frame: la schermata appare e gira PRIMA del build pesante (bloccante)

        var solar = gameObject.AddComponent<SolarSystem>();
        // 1 = ritmo di gioco. La velocità orbitale del pianeta (~628 m/s) è già quella che il freno X
        // deve domare per sincronizzarti con un corpo: accelerare il tempo la gonfia e rende il match
        // velocity ingiocabile. Alza questo SOLO per osservare le orbite veloci (debug), non in volo.
        solar.TimeScale = 1.0;

        // --- COMPOSIZIONE della scena, ogni pezzo ISOLATO nel suo file (niente "minestrone" qui): sistema solare →
        //     spawn del giocatore → illuminazione → interfaccia. Aggiungere/cambiare un pezzo = nel suo Setup, non qui. ---
        GpuPlanetRenderer.UseBatchFill = useBatchFill;   // PRIMA della Build: la verifica gira nel Setup di ogni corpo
        GpuPlanetRenderer.UseGeomorph = useGeomorph;     // A/B per isolare gli artefatti di geometria
        GpuPlanetRenderer.UsePbrTerrain = usePbrTerrain; // PBR per pendenza + GGX (GPU-4), A/B
        GpuPlanetRenderer.CullSplit = useCullSplit;      // overdraw: interno Cull Back + skirt Cull Off (2 materiali)
        GpuPlanetRenderer.InteriorCull = interiorCull;
        GpuPlanetRenderer.DebugView = debugView;          // diagnosi superficie (poi pilotabile live dal menu à)
        GpuPlanetRenderer.SuppressDraw = false;           // la mappa lo mette true: con domain-reload OFF sopravvivrebbe fra le sessioni Play → pianeta GPU muto. Azzera all'avvio scena
        PlanetRecipe.DebugDisableTypes = debugDisablePipelines;   // diagnosi: salta tipi di pipeline (build-time, GPU+CPU)
        PauseMenu.Enabled = enablePauseMenu;                      // menu ESC (debug: off = ESC come prima)
        var sys = SolarSystemSetup.Build(solar, useQuadtree, singleMeshRes, useGpuSurface, gpuSurfaceRes, spawnOnBody);
        yield return null;
        var rig = PlayerSpawn.Spawn(solar, sys.HomePlanetGo, sys.HomeTerrain, sys.StarTransform);
        var eclipse = LightingSetup.Setup(gameObject, solar, sys.StarTransform, sys.HomePlanetGo.transform);
        UiSetup.Setup(gameObject, solar, rig, sys);

        // TAPPA 4 multi-sistema: cabla sveglia/sonno dei sistemi DISTANTI (SolarSystem decide il QUANDO per
        // prossimità; qui il COSA: costruisci/distruggi i corpi + ri-punta la luce alla stella giusta + ricostruisci
        // le eclissi sui nuovi corpi). Il sistema-casa resta residente. Identico a prima finché resti nel sistema-casa.
        Transform homeStar = sys.StarTransform; Transform homePlanet = sys.HomePlanetGo.transform;
        solar.WakeSystem = s =>
        {
            bool ok = SolarSystemSetup.BuildSystem(solar, s, useQuadtree, useGpuSurface, gpuSurfaceRes);
            if (ok)
            {
                if (SunLight.Instance != null && s.StarTransform != null && s.Bodies != null && s.Bodies.Count > 0)
                    SunLight.Instance.Retarget(s.StarTransform, s.Bodies[0].transform);
                eclipse?.Rebuild();
            }
            return ok;
        };
        solar.SleepSystem = s =>
        {
            SolarSystemSetup.DestroySystem(solar, s);
            if (SunLight.Instance != null) SunLight.Instance.Retarget(homeStar, homePlanet);   // la luce torna al sistema-casa
            eclipse?.Rebuild();
        };

        yield return null;
        // tieni la schermata un minimo (i messaggi scorrono, niente "flash"), poi dissolvi.
        while (Time.realtimeSinceStartup - bootT0 < 2.5f) yield return null;
        loading.Done();
    }
}

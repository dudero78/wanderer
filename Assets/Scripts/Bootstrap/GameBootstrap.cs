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
    [Tooltip("ON = quadtree CDLOD (geometria view-dependent, crateri nitidi calpestabili, look Elite/SC). "
           + "OFF = mesh singola a risoluzione fissa (fallback, niente LOD).")]
    public bool useQuadtree = true;
    [Tooltip("Solo se useQuadtree=OFF: risoluzione della mesh singola per faccia (build su thread).")]
    public int singleMeshRes = 320;

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

        // --- SISTEMA SOLARE: stella + pianeta-casa + corpi in orbita (composizione isolata in SolarSystemSetup).
        //     Registra tutto, ancora l'origine alla casa e posiziona i corpi al tempo 0. Restituisce i riferimenti
        //     che servono qui sotto per piazzare giocatore/luce/mappa. Aggiungere un corpo = una voce là, non qui. ---
        var sys = SolarSystemSetup.Build(solar, useQuadtree, singleMeshRes);
        var starGo = sys.StarTransform.gameObject;
        var star = sys.Star;
        var planetGo = sys.HomePlanetGo;
        var planet = sys.HomePlanet;
        var terrain = sys.HomeTerrain;

        // --- Giocatore: nasce a terra all'ALBA sull'EQUATORE, rivolto verso il sole (sole
        // all'orizzonte davanti). Direzione del sole = dal pianeta verso la stella. Il polo è
        // Vector3.up → l'equatore è il piano y=0. Il terminatore (alba/tramonto) è il cerchio
        // perpendicolare al sole. Il loro incrocio = cross(sole, polo): equatore E terminatore. ---
        Vector3 sunDir = (starGo.transform.position - planetGo.transform.position).normalized;
        Vector3 pole = Vector3.up;
        Vector3 spawnDir = Vector3.Cross(sunDir, pole);
        if (spawnDir.sqrMagnitude < 1e-4f) spawnDir = Vector3.Cross(sunDir, Vector3.right);   // sole sul polo: ripiego
        spawnDir.Normalize();
        // il sole è già tangente al suolo qui (perpendicolare a spawnDir) → forward = verso il sole
        Quaternion spawnRot = Quaternion.LookRotation(sunDir, spawnDir);   // forward=sole, up=radiale

        Vector3 playerSpawnPos = planetGo.transform.position + spawnDir * (terrain.SampleHeight(spawnDir) + 1f);
        var playerGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        playerGo.name = "Player";
        var prb = playerGo.AddComponent<Rigidbody>();
        var walker = playerGo.AddComponent<PlanetWalker>();
        playerGo.transform.SetPositionAndRotation(playerSpawnPos, spawnRot);
        prb.position = playerSpawnPos;   // allinea subito lo stato fisico: niente teletrasporto a (0,0,0) al frame 0
        prb.rotation = spawnRot;         // guarda verso il sole all'orizzonte (il walker preserva questo orientamento)
        solar.PlayerBody = prb;          // da ora l'origine ancora al corpo PIÙ VICINO al giocatore (viaggi tra corpi)
        // il giocatore sta a terra col vincolo analitico: il collider fisico non serve
        var playerCol = playerGo.GetComponent<Collider>();
        if (playerCol) playerCol.enabled = false;
        SetColor(playerGo, new Color(0.85f, 0.35f, 0.3f));

        // --- Tuta-jetpack: faro-pilastro. Posizione calcolata QUI, da dati noti e stabili
        // (spawn del giocatore + altezza del terreno): ~8 m DAVANTI al giocatore, sul terreno. Il
        // giocatore guarda verso il sole, quindi "davanti" = verso il sole (già tangente al suolo). ---
        Vector3 forwardTangent = sunDir;
        Vector3 suitDir = (playerSpawnPos + forwardTangent * 8f - planetGo.transform.position).normalized;
        Vector3 suitGround = planetGo.transform.position + suitDir * terrain.SampleHeight(suitDir);

        var suitGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        suitGo.name = "Tuta";
        suitGo.transform.localScale = new Vector3(0.8f, 1.1f, 0.8f);   // capsula a misura di "tuta"
        var suitCol = suitGo.GetComponent<Collider>();
        if (suitCol) Destroy(suitCol);
        SetColor(suitGo, new Color(0.2f, 0.9f, 1f), emissive: true);

        var glowGo = new GameObject("Glow");
        glowGo.transform.SetParent(suitGo.transform, false);
        var glow = glowGo.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(0.3f, 0.95f, 1f);
        glow.range = 5f;         // alone stretto sulla tuta, non un'inondazione di ciano sul terreno
        glow.intensity = 1.2f;

        var pickup = suitGo.AddComponent<SuitPickup>();
        pickup.surfaceClearance = 1.2f;   // metà altezza della capsula: la base tocca il suolo
        pickup.pickupRadius = 3.5f;
        pickup.Init(playerGo.transform, walker, suitGround, suitDir);
        solar.Loose.Add(suitGo.transform);   // oggetto sciolto: va traslato allo switch di corpo

        // --- Camera ---
        var camGo = new GameObject("PlayerCamera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 300000f;
        // FOV contenuto (default Unity 60 → 52): a campo largo le sfere ai BORDI si deformano in ellissi
        // (distorsione prospettica rettilineare); 52° la riduce molto. Regolabile dal menù à (scheda Camera).
        cam.fieldOfView = 52f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.01f, 0.01f, 0.03f);
        // RenderScaler: rende a una frazione della risoluzione e riscala. Il profilo dice che la
        // GPU finisce il frame in ~1 ms (scarica): NON è lei a scaldare, è la CPU/main-thread, che
        // governiamo col cap fps. Quindi qui teniamo 1.0 = piena risoluzione, immagine nitida: la
        // GPU se lo può permettere. Se un domani caricheremo la GPU di effetti, è la prima leva da
        // riabbassare (0.85–0.9) per recuperare margine senza toccare la resa del terreno.
        camGo.AddComponent<RenderScaler>().scale = 1.0f;
        camGo.transform.SetParent(playerGo.transform, false);
        camGo.transform.localPosition = new Vector3(0f, 0.6f, 0f);
        walker.cameraPivot = camGo.transform;

        // --- Torcia (inclusa nella tuta): spotlight che segue lo sguardo, toggle con F ---
        var flashGo = new GameObject("Flashlight");
        flashGo.transform.SetParent(camGo.transform, false);   // figlia della camera: punta dove guardi
        // appena sotto l'occhio (lampada al mento): illumina bene il terreno davanti dando
        // un filo di angolo dal basso, senza sbilanciare il fascio di lato (niente ovale storto).
        flashGo.transform.localPosition = new Vector3(0f, -0.15f, 0f);
        var lamp = flashGo.AddComponent<Light>();
        lamp.type = LightType.Spot;
        lamp.range = 110f;
        lamp.spotAngle = 68f;
        lamp.color = new Color(1f, 0.95f, 0.85f);
        // niente ombre proiettate dalla torcia: a luce radente su questa mesh danno
        // "crepe" (shadow acne). Il rilievo emerge comunque dall'illuminazione diffusa
        // angolata (la torcia è spostata di lato), in modo pulito.
        lamp.shadows = LightShadows.None;
        // niente cookie: lo spot di default è già un cono morbido e rotondo, ed è più luminoso.
        // (Il rettangolo ciano era la TUTA, non la torcia: risolto nascondendola alla raccolta.)
        // sempre enabled: la torcia si commuta via intensità.
        lamp.enabled = true;
        lamp.intensity = 0f;
        var flashlight = flashGo.AddComponent<Flashlight>();
        flashlight.walker = walker;
        flashlight.lamp = lamp;
        flashlight.onIntensity = 2.2f;   // base contenuta: niente bruciato da vicino
        flashlight.baseRange = 110f;

        // --- Luce stellare ---
        var lightGo = new GameObject("SunLight");
        var dl = lightGo.AddComponent<Light>();
        dl.type = LightType.Directional;
        dl.intensity = 2.0f;
        dl.color = new Color(1f, 0.96f, 0.9f);
        // niente ombre proiettate dal sole: causavano lo "schiarimento" brusco del terreno
        // mentre ti allontani (le auto-ombre svaniscono oltre la shadow distance). Il rilievo
        // resta ben visibile grazie alle normali analitiche. Bonus: meno calore.
        dl.shadows = LightShadows.None;
        var sun = lightGo.AddComponent<SunLight>();
        sun.star = starGo.transform;
        sun.planet = planetGo.transform;

        // Ombre di ECLISSI (analitiche, nello shader): un corpo fra il sole e un altro lo oscura. Niente
        // shadow map → nessuna acne sul terreno (per quello le ombre proiettate restano spente).
        var eclipse = gameObject.AddComponent<EclipseDriver>();
        eclipse.Init(solar, lightGo.transform);

        // notte quasi nera: il terminatore (linea giorno/notte) diventa netto, look lunare.
        // Con l'atmosfera, più avanti, sarà lo scattering a rialzare la luce sul lato in ombra.
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.05f, 0.054f, 0.065f);

        // --- Modalità mappa (M): zoom-out sul sistema, orbite disegnate, click per selezionare un corpo ---
        var map = gameObject.AddComponent<MapMode>();
        map.Init(cam, walker, solar);

        // --- Indicatore di rotta: reticolo stile Outer Wilds sul corpo selezionato (bussola del viaggio) ---
        var route = gameObject.AddComponent<RouteIndicator>();
        route.Init(cam, walker, solar, map);

        // --- Orbite a schermo (O): linee delle orbite del sistema, anche in volo ---
        var orbitDisplay = gameObject.AddComponent<OrbitDisplay>();
        orbitDisplay.Init(solar);

        var hud = gameObject.AddComponent<DebugHud>();
        hud.Init(playerGo.transform, planet, star, solar, walker, flashlight, suitGo.transform, camGo.transform);

        // --- Schermata impostazioni (à): facilitazioni opzionali (es. autopilota stazionario) ---
        var settings = gameObject.AddComponent<SettingsMenu>();
        settings.Init(walker, cam);
    }

    static void SetColor(GameObject go, Color c, bool emissive = false)
    {
        var r = go.GetComponent<Renderer>();
        if (!r) return;

        if (emissive)
        {
            // oggetti "che brillano" (stella, tuta-beacon): disco pieno e luminoso, NON ombreggiato dalla
            // luce — un sole non va messo in ombra. Unlit/Color evita anche lo stripping della variante
            // _EMISSION dello Standard in build (la attiveremmo a runtime → la build la toglie → sfera scura).
            var us = Shader.Find("Unlit/Color");
            if (us != null) { r.material = new Material(us) { color = c }; return; }
        }

        // oggetti normali (player): Standard, illuminato dal sole.
        var sh = Shader.Find("Standard");
        if (sh == null)
        {
            // shader assente nella build (stripping): tieni il materiale di default del primitivo invece di
            // crashare (new Material(null) lancerebbe e aborterebbe la costruzione della scena → nero totale).
            Debug.LogError("Shader 'Standard' non trovato nella build: aggiungilo agli Always Included Shaders.");
            return;
        }
        r.material = new Material(sh) { color = c };
    }
}

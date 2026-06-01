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
    void Start()
    {
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
        solar.TimeScale = 3.0;

        // --- Stella (corpo centrale, fisso all'origine dell'universo) ---
        var starGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        starGo.name = "Stella";
        var starCol = starGo.GetComponent<Collider>();
        if (starCol) Destroy(starCol);
        var star = starGo.AddComponent<CelestialBody>();
        star.Radius = 2000;
        star.SurfaceGravity = 100;
        star.UniversePosition = Vector3d.Zero;
        starGo.transform.localScale = Vector3.one * (float)(star.Radius * 2);
        SetColor(starGo, new Color(1f, 0.88f, 0.55f), emissive: true);
        solar.Register(star);

        // --- Pianeta (orbita la stella) ---
        var planetGo = new GameObject("Pianeta");
        var planet = planetGo.AddComponent<CelestialBody>();
        planet.Radius = 500;
        planet.SurfaceGravity = 9.81;
        planet.Parent = star;
        planet.Orbit = new KeplerOrbit
        {
            SemiMajorAxis = 60000,
            Eccentricity = 0.1,
            Period = 600,
            Inclination = 0.15
        };

        // terreno procedurale: il noise definisce la forma, la mesh la mostra,
        // il PlanetWalker ci cammina sopra. Una sola fonte di verità.
        var terrain = planetGo.AddComponent<PlanetTerrain>();
        terrain.BaseRadius = (float)planet.Radius;
        terrain.Amplitude = 55f;    // colline più marcate: silhouette meno "palla liscia"
        terrain.Frequency = 5.5f;
        // 6 ottave: oltre alle colline larghe, l'ottava 6 dà rilievo REALE a ~9 m. È geometria
        // vera, non bump: a luce radente fa ombra e silhouette → la fascia media non collassa più
        // in una macchia piatta (il bump da solo, senza ombre, lì sparisce). Non oltre 6: a mesh
        // 300 (~2.6 m tra vertici) un'ottava più fine cadrebbe sotto Nyquist e aliaserebbe.
        terrain.Octaves = 6;
        terrain.Seed = 1337;

        // materiale procedurale: resta come FALLBACK (e fonte dei parametri _BaseFreq ecc.).
        // Se il bake riesce, le facce passano alla versione "fredda" che legge la texture.
        var planetMat = new Material(Shader.Find("Wanderer/Planet"));
        planetMat.SetFloat("_BaseRadius", terrain.BaseRadius);
        planetMat.SetFloat("_Amplitude", terrain.Amplitude);
        // Mesh densa (300 per faccia ≈ 2.6 m tra i vertici): serve perché le ottave fini del
        // terreno diventino geometria risolta, non vertici troppo radi che le lisciano via.
        PlanetMeshBuilder.Build(planetGo.transform, terrain, 300, planetMat);

        // Bake del rilievo in texture, UNA volta: da "6 ottave di Perlin per pixel per frame"
        // a "una lettura di texture per pixel". Toglie il calore e, grazie a mipmap+aniso, la
        // sfuocatura a distanza. Il rilievo viene ORA solo da qui (niente ottave procedurali),
        // quindi 2048² per faccia: ~0.4 m/texel sul pianeta da 500 m, il dettaglio medio resta;
        // il sub-texel a contatto lo dà la grana. Fallback ai materiali procedurali se il bake fallisce.
        bool baked = PlanetBaker.Bake(planetGo.transform, terrain.BaseRadius, terrain.Amplitude, 0.25f, 2048);
        Debug.Log(baked ? "Pianeta: rilievo bakeato (percorso freddo)." : "Pianeta: bake non riuscito, resto sul procedurale.");

        solar.Register(planet);

        // origine ancorata al pianeta: resta a ~(0,0,0), il resto dell'universo si muove
        solar.Anchor = planet;
        planet.UpdatePosition(0);
        solar.Step();

        // --- Giocatore: nasce a terra usando l'altezza del terreno (la stessa che usa il
        // vincolo analitico), così è già al livello del suolo: niente caduta iniziale. ---
        Vector3 spawnDir = Vector3.up;
        Vector3 playerSpawnPos = planetGo.transform.position + spawnDir * (terrain.SampleHeight(spawnDir) + 1f);
        var playerGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        playerGo.name = "Player";
        var prb = playerGo.AddComponent<Rigidbody>();
        var walker = playerGo.AddComponent<PlanetWalker>();
        playerGo.transform.position = playerSpawnPos;
        prb.position = playerSpawnPos;   // allinea subito lo stato fisico: niente teletrasporto a (0,0,0) al frame 0
        // il giocatore sta a terra col vincolo analitico: il collider fisico non serve
        var playerCol = playerGo.GetComponent<Collider>();
        if (playerCol) playerCol.enabled = false;
        SetColor(playerGo, new Color(0.85f, 0.35f, 0.3f));

        // --- Tuta-jetpack: faro-pilastro. Posizione calcolata QUI, da dati noti e stabili
        // (spawn del giocatore + altezza del terreno): ~8 m davanti, sul terreno. ---
        Vector3 forwardTangent = Vector3.ProjectOnPlane(Vector3.forward, spawnDir).normalized;   // sguardo iniziale, sul piano del suolo
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

        // --- Camera ---
        var camGo = new GameObject("PlayerCamera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 300000f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.01f, 0.01f, 0.03f);
        // NIENTE RenderScaler: il bake ha reso lo shader economico, quindi la camera renderizza
        // a risoluzione nativa (Retina piena) → massima nitidezza. Il RenderScaler resta come
        // componente disponibile (Core/RenderScaler.cs): se il calore tornasse aggiungendo
        // contenuto, basta agganciarlo qui con scale < 1.
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

        // notte quasi nera: il terminatore (linea giorno/notte) diventa netto, look lunare.
        // Con l'atmosfera, più avanti, sarà lo scattering a rialzare la luce sul lato in ombra.
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.05f, 0.054f, 0.065f);

        var hud = gameObject.AddComponent<DebugHud>();
        hud.Init(playerGo.transform, planet, star, solar, walker, flashlight, suitGo.transform, camGo.transform);
    }

    static void SetColor(GameObject go, Color c, bool emissive = false)
    {
        var r = go.GetComponent<Renderer>();
        if (!r) return;
        var m = new Material(Shader.Find("Standard")) { color = c };
        if (emissive)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * 1.5f);
        }
        r.material = m;
    }
}

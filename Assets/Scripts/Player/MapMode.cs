using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Modalità mappa stile Outer Wilds. Premendo M la camera fa uno zoom-out veloce DALLA POSIZIONE ESATTA del
/// giocatore fino a inquadrare l'intero sistema in prospettiva, con le ORBITE dei corpi. Si clicca un corpo per
/// SELEZIONARLO (navigazione "vai verso"); un sistema distante per fissarlo come WAYPOINT galattico. M/Esc per tornare.
///
/// ── SPAZIO-MAPPA LOCALE (il cuore della riscrittura) ──────────────────────────────────────────────────────────
/// La mappa NON disegna più i corpi alle loro posizioni-scena del gioco (ancorate a FloatingOrigin.SceneOrigin). Un
/// sistema distante è a milioni di metri da lì → il float della camera-mappa trema. Invece: ogni cosa vive in
/// coordinate-UNIVERSO (Vector3d) e si proietta con ToMap(uni) = (uni − mapOrigin).ToVector3(), dove mapOrigin è
/// l'origine del SISTEMA più vicino al pivot. Vicino al fuoco le coordinate sono sempre piccole → precisione perfetta
/// a qualunque distanza. PROPRIETÀ CHIAVE: siccome la CAMERA e gli OGGETTI usano lo STESSO ToMap, l'immagine è
/// invariante al cambio di mapOrigin (nessun salto quando passi da un sistema all'altro) e — non meno importante — il
/// primo frame dell'animazione d'entrata coincide al pixel con la vista reale del giocatore (l'effetto "zoom-out dalla
/// mia posizione" è corretto per costruzione, a ogni distanza).
///
/// ── CAMERA TRACKBALL (orbita libera, niente snap) ─────────────────────────────────────────────────────────────
/// Posizione (camPosU) e orientamento (camRot) della camera sono INDIPENDENTI dal pivot. Il destro fa un'orbita
/// RIGIDA attorno al pivot (ruota posizione E orientamento insieme → resti puntato sul pivot). Il clic imposta il
/// pivot sul punto cliccato SENZA muovere la camera → nessuno snap della vista. Pan (sinistro trascinato / WASD) e
/// zoom-verso-cursore (rotella) restano. È il comportamento Blender-like richiesto.
///
/// Usa una camera dedicata: in mappa la camera del giocatore viene spenta. I comandi del walker sono congelati
/// (ControlsActive=false) ma gravità e suolo restano attivi: il giocatore aspetta a terra.
/// </summary>
public class MapMode : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.M;
    public KeyCode galaxyKey = KeyCode.G;   // vista galattica (inquadra tutti i sistemi)
    public KeyCode namesKey = KeyCode.N;     // mostra/nascondi i nomi dei corpi
    bool showBodyNames = true;               // DEV: nomi dei corpi (anche dei sistemi distanti) visibili da subito
    [Tooltip("Durata dello zoom-out/in in secondi.")]
    public float transitionTime = 1.0f;
    [Tooltip("Dimensione dei marker come frazione della distanza camera (≈ costante a schermo).")]
    public float markerScreenSize = 0.02f;

    Camera playerCam;
    Transform playerCamT;
    PlanetWalker walker;
    SolarSystem solar;
    RenderScaler playerScaler;   // la camera giocatore renderizza in una RT presentata a parte: va spenta in mappa

    Camera mapCam;
    int mapLayer;                // layer "MapView": la camera-mappa renderizza e raycasta SOLO questo (niente scena reale)
    enum State { Off, Entering, On, Exiting }
    State state = State.Off;
    float t;
    bool animSkipFrame;   // salta il PRIMO frame dell'animazione (il deltaTime di EnterMap/ExitMap è gonfio = vedi sotto)
    float fadeT;          // tempo dall'apertura: un breve fade da nero maschera lo swap mondo-reale → proxy della mappa
    Texture2D fadeTex;    // 1×1 bianco per il velo nero del fade
    const float FadeTime = 0.18f;   // durata del fade in secondi (assoluta: indipendente da transitionTime)

    // ── Camera trackball, in coordinate-UNIVERSO ──
    Vector3d camPosU;            // posizione della camera (universo)
    Quaternion camRot = Quaternion.identity;   // orientamento della camera (indipendente dal pivot)
    Vector3d pivotU;             // centro d'orbita (universo): NON è il bersaglio di sguardo, solo il perno del destro
    Vector3d mapOrigin;          // punto-universo che corrisponde all'origine di render della mappa (sistema più vicino al pivot)
    Vector3d fromU; Quaternion fromRot;   // capisaldi dell'animazione d'entrata/uscita

    const float MapPitchDefault = 33.5f, MapRotSpeed = 0.25f, MapPanRate = 0.7f;

    GUIStyle mapStyle, hereStyle, nameStyle, probeStyle;
    GameObject playerMarker;   // "tu sei qui": dove sta il giocatore (su un corpo o in volo), in universo
    GameObject probeMarker;    // la SONDA, quando dispiegata (Probe.Instance attiva): pallino ambra + etichetta

    // Scia: traiettoria percorsa. Registrata SEMPRE (anche fuori mappa) in coordinate-UNIVERSO (Vector3d) e proiettata
    // con ToMap ogni frame → coerente con stella e orbite a qualunque distanza.
    LineRenderer trail;
    const int MaxTrailPoints = 1024;   // ring buffer: oltre, scarta il punto più vecchio (scia "recente")
    readonly Vector3d[] trailRing = new Vector3d[MaxTrailPoints];
    int trailHead, trailCount;
    Vector3d TrailAt(int i) => trailRing[(trailHead + i) % MaxTrailPoints];
    float trailStep = 30f;             // distanza minima fra due punti registrati (scalata sul sistema in Init)
    float trailMaxJump = 1e9f;         // salto max plausibile fra due frame: oltre = ri-ancoraggio, si scarta

    const int MapProxyRes = 40;   // risoluzione mesh del proxy "corpo reale" in mappa (basta: è piccolo)

    // ── Visuali dei CORPI VIVI (in solar.Bodies): marker cliccabile + proxy craterizzato + anello d'orbita ──
    readonly List<GameObject> markers = new List<GameObject>();
    readonly Dictionary<GameObject, CelestialBody> markerBody = new Dictionary<GameObject, CelestialBody>();
    readonly Dictionary<CelestialBody, Transform> proxies = new Dictionary<CelestialBody, Transform>();
    readonly List<LineRenderer> orbits = new List<LineRenderer>();
    readonly List<CelestialBody> orbitBody = new List<CelestialBody>();
    readonly List<Color> orbitCol = new List<Color>();   // colore base per orbita (per il fade-in/out entrando/uscendo dalla mappa)
    CelestialBody selected;

    // ── Visuali dei SISTEMI DISTANTI (statiche, dai SystemRecipe): billboard della stella + dischi-pianeti + anelli ──
    // Esistono dai DATI senza caricare il sistema nel mondo. Quando un sistema si SVEGLIA (i suoi corpi veri entrano in
    // solar.Bodies) si NASCONDONO e lasciano il posto ai corpi reali; quando dorme tornano visibili.
    sealed class SystemVisual
    {
        public StarSystem sys;
        public GameObject star;                          // billboard cliccabile (waypoint)
        public readonly List<GameObject> discs = new List<GameObject>();   // dischi-pianeti cliccabili (waypoint)
        public readonly List<LineRenderer> rings = new List<LineRenderer>();
        public readonly List<KeplerOrbit> bodyOrbits = new List<KeplerOrbit>();   // parallelo a discs/rings: per posizionarli
        public readonly List<float> bodyRadii = new List<float>();
        public readonly List<string> bodyNames = new List<string>();
    }
    readonly List<SystemVisual> systemVisuals = new List<SystemVisual>();
    readonly Dictionary<GameObject, StarSystem> waypointOf = new Dictionary<GameObject, StarSystem>();   // clic billboard → sistema (waypoint alla stella)
    readonly Dictionary<GameObject, float> discRadius = new Dictionary<GameObject, float>();              // disco/billboard → raggio (zoom-su-corpo)
    readonly Dictionary<GameObject, SolarSystem.DormantTarget> discTarget = new Dictionary<GameObject, SolarSystem.DormantTarget>();   // clic disco pianeta dormiente → bersaglio del PIANETA

    // statico: "la mappa è aperta?" → StarRenderClamp lo guarda per NON clampare la stella sulla camera-mappa (clamp
    // tarato sulla camera del giocatore; in mappa corromperebbe il transform della stella, e il reticolo di rotta che
    // legge transform.position finirebbe su un fantasma fuori posto).
    public static bool IsOpen { get; private set; }

    public CelestialBody Selected => selected;
    public bool Active => state != State.Off;
    public Camera ViewCamera => mapCam;   // la camera della mappa (per il reticolo di rotta in modalità mappa)

    // ── Conversioni spazio-mappa ──
    Vector3 ToMap(Vector3d uni) => (uni - mapOrigin).ToVector3();
    Vector3d ToUniverse(Vector3 mapPos) => mapOrigin + new Vector3d(mapPos);

    /// <summary>Converte una posizione dallo spazio-scena del GIOCATORE (ancorato a SceneOrigin) allo spazio-mappa
    /// (ancorato a mapOrigin). Serve al RouteIndicator: in mappa proietta il bersaglio sulla camera-mappa, che vive in
    /// un frame diverso da quello del gioco. universo = SceneOrigin + scenePos → mappa = scenePos + (SceneOrigin − mapOrigin).</summary>
    public Vector3 ToViewSpace(Vector3 playerScenePos) =>
        playerScenePos + (FloatingOrigin.SceneOrigin - mapOrigin).ToVector3();

    public void Init(Camera playerCamera, PlanetWalker w, SolarSystem s)
    {
        playerCam = playerCamera;
        playerCamT = playerCamera.transform;
        walker = w;
        solar = s;
        playerScaler = playerCamera.GetComponent<RenderScaler>();

        var go = new GameObject("MapCamera");
        go.tag = "MainCamera";
        mapCam = go.AddComponent<Camera>();
        mapCam.nearClipPlane = 1f;
        mapCam.farClipPlane = 500000f;
        mapCam.clearFlags = CameraClearFlags.SolidColor;
        mapCam.backgroundColor = new Color(0.01f, 0.01f, 0.03f);
        mapCam.fieldOfView = 55f;
        mapCam.enabled = false;

        // LAYER DEDICATO: la camera-mappa renderizza SOLO le visuali della mappa (proxy/marker/orbite), MAI gli oggetti
        // reali della scena. Senza, renderizzava anche le stelle vere — per giunta spostate fuori posto da StarRenderClamp,
        // che usa Camera.main (= la camera-mappa in mappa) → il "sole finto" vagante. La camera giocatore esclude il layer.
        mapLayer = LayerMask.NameToLayer("MapView");
        if (mapLayer < 0) mapLayer = 9;
        mapCam.cullingMask = 1 << mapLayer;
        playerCam.cullingMask &= ~(1 << mapLayer);

        float homeR = SystemRadius(HomeSystem());
        trailStep = homeR * 0.0015f;   // scia fine ma con punti contenuti
        trailMaxJump = homeR * 0.25f;  // un frame reale non copre mai tanto: oltre = ri-ancoraggio
        BuildVisuals();
        ShowVisuals(false);
    }

    // ───────────────────────────────────────── Costruzione visuali ─────────────────────────────────────────

    void BuildVisuals()
    {
        var markerShader = Shader.Find("Unlit/Color");
        var lineShader = Shader.Find("Sprites/Default");

        BuildBodyVisuals(markerShader, lineShader);
        BuildSystemVisuals(markerShader, lineShader);

        // marker "TU SEI QUI": verde acceso. Niente collider → non intercetta i click e non copre i corpi dietro.
        playerMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        playerMarker.name = "Marker_Player";
        var col2 = playerMarker.GetComponent<Collider>();
        if (col2 != null) Destroy(col2);
        if (markerShader != null)
        {
            var m = new Material(markerShader);
            m.SetColor("_Color", new Color(0.35f, 1f, 0.45f));
            playerMarker.GetComponent<MeshRenderer>().sharedMaterial = m;
        }
        playerMarker.transform.SetParent(transform, false);
        playerMarker.layer = mapLayer;

        // marker della SONDA: ambra, niente collider (non selezionabile, non copre i corpi). Visibile solo da dispiegata.
        probeMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        probeMarker.name = "Marker_Probe";
        var col3 = probeMarker.GetComponent<Collider>();
        if (col3 != null) Destroy(col3);
        if (markerShader != null)
        {
            var m = new Material(markerShader);
            m.SetColor("_Color", new Color(1f, 0.75f, 0.2f));
            probeMarker.GetComponent<MeshRenderer>().sharedMaterial = m;
        }
        probeMarker.transform.SetParent(transform, false);
        probeMarker.layer = mapLayer;
        probeMarker.SetActive(false);

        // scia del giocatore: filo verde, brilla al capo recente e sfuma sul vecchio (coda a cometa)
        if (lineShader != null)
        {
            var tgo = new GameObject("PlayerTrail");
            tgo.transform.SetParent(transform, false);
            tgo.layer = mapLayer;
            trail = tgo.AddComponent<LineRenderer>();
            trail.material = new Material(lineShader);
            trail.useWorldSpace = true;
            trail.numCapVertices = 2;
            trail.positionCount = 0;
            trail.startColor = new Color(0.4f, 1f, 0.5f, 0.12f);   // capo VECCHIO (indice 0): quasi spento
            trail.endColor = new Color(0.45f, 1f, 0.55f, 0.9f);    // capo RECENTE: acceso
        }
    }

    readonly HashSet<CelestialBody> visualized = new HashSet<CelestialBody>();   // corpi con visuali costruite (per l'update incrementale)

    void BuildBodyVisuals(Shader markerShader, Shader lineShader)
    {
        for (int i = 0; i < solar.Bodies.Count; i++)
            if (solar.Bodies[i] != null) AddBodyVisual(solar.Bodies[i], markerShader, lineShader);
    }

    // Costruisce le visuali di UN corpo (marker cliccabile + proxy craterizzato + anello d'orbita).
    void AddBodyVisual(CelestialBody b, Shader markerShader, Shader lineShader)
    {
        if (!visualized.Add(b)) return;
        bool isStar = b.Orbit == null;
        Color col = isStar ? (b.System != null ? b.System.StarColor : new Color(1f, 0.85f, 0.45f))
                           : new Color(0.7f, 0.78f, 0.9f);

        if (!b.Massless)   // il baricentro di un binario non è un bersaglio (niente marker/proxy), ma l'orbita sì
        {
            var mk = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mk.name = "Marker_" + b.gameObject.name;
            if (markerShader != null)
            {
                var m = new Material(markerShader);
                m.SetColor("_Color", col);
                mk.GetComponent<MeshRenderer>().sharedMaterial = m;
            }
            mk.transform.SetParent(transform, false);
            mk.layer = mapLayer;
            markers.Add(mk);
            markerBody[mk] = b;

            var terr = b.GetComponent<PlanetTerrain>();
            if (terr != null && terr.Recipe != null)
            {
                mk.GetComponent<MeshRenderer>().enabled = false;   // il marker resta bersaglio di click INVISIBILE
                var pgo = new GameObject("Proxy_" + b.gameObject.name);
                pgo.transform.SetParent(transform, false);
                var proxy = pgo.AddComponent<SingleMeshPlanet>();
                // full-res (MapProxyRes) su THREAD; proxy immediato a res MINIMA (4) → niente freeze sul main thread
                // all'apertura (prima il proxy immediato era a MapProxyRes = 9600 vert·noise per corpo, ×N corpi = blocco).
                proxy.Build(terr, terr.FaceMaterials, MapProxyRes, 4);
                SetMapLayer(pgo);   // il proxy ha mesh-figlie: ricorsivo
                proxies[b] = pgo.transform;
            }
        }

        if (b.Orbit != null && b.Parent != null && lineShader != null)
        {
            var oc = new Color(col.r, col.g, col.b, 0.5f);
            orbits.Add(NewOrbitRing(lineShader, oc, 96));
            orbitBody.Add(b);
            orbitCol.Add(oc);
        }
    }

    // INCREMENTALE (era il lag all'apertura): aggiunge le visuali dei corpi NUOVI (sistema svegliato) e rimuove quelle
    // dei corpi spariti (sistema addormentato), senza ricostruire i proxy esistenti (mesh costose) ogni volta.
    void SyncBodyVisuals()
    {
        var present = new HashSet<CelestialBody>();
        for (int i = 0; i < solar.Bodies.Count; i++) if (solar.Bodies[i] != null) present.Add(solar.Bodies[i]);

        for (int i = markers.Count - 1; i >= 0; i--)
        {
            var b = markerBody[markers[i]];
            if (present.Contains(b)) continue;
            Destroy(markers[i]); markerBody.Remove(markers[i]); markers.RemoveAt(i);
            if (b != null && proxies.TryGetValue(b, out var px)) { if (px) Destroy(px.gameObject); proxies.Remove(b); }
            if (b != null) visualized.Remove(b);
        }
        for (int i = orbits.Count - 1; i >= 0; i--)
        {
            var ob = orbitBody[i];
            if (present.Contains(ob)) continue;
            if (orbits[i]) Destroy(orbits[i].gameObject);
            orbits.RemoveAt(i); orbitBody.RemoveAt(i); orbitCol.RemoveAt(i);
            if (ob != null) visualized.Remove(ob);   // copre i corpi massless (baricentro: solo orbita, niente marker)
        }

        var ms = Shader.Find("Unlit/Color"); var ls = Shader.Find("Sprites/Default");
        for (int i = 0; i < solar.Bodies.Count; i++)
            if (solar.Bodies[i] != null) AddBodyVisual(solar.Bodies[i], ms, ls);
    }

    // Visuali STATICHE dei sistemi distanti, dai SystemRecipe: si costruiscono UNA volta (sono dato immutabile). Il
    // sistema-casa (SystemOrigin = Zero) è sempre vivo → niente proxy statico (lo coprono i corpi veri).
    void BuildSystemVisuals(Shader markerShader, Shader lineShader)
    {
        if (solar.Systems == null) return;
        foreach (var s in solar.Systems)
        {
            if (s == null || s.SystemOrigin.sqrMagnitude < 1.0) continue;   // salta la casa (origine Zero)
            var sv = new SystemVisual { sys = s };

            // billboard della stella: disco unlit del colore del sistema, cliccabile (waypoint galattico).
            var smk = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            smk.name = "System_" + s.Name;
            if (markerShader != null)
            {
                var m = new Material(markerShader); m.SetColor("_Color", s.StarColor);
                smk.GetComponent<MeshRenderer>().sharedMaterial = m;
            }
            smk.transform.SetParent(transform, false);
            smk.layer = mapLayer;
            sv.star = smk;
            waypointOf[smk] = s;
            discRadius[smk] = s.StarRadius;   // bersaglio di zoom: puoi zoomare dritto sulla stella distante

            // dischi-pianeti + anelli d'orbita, dai corpi della ricetta (posizione = SystemOrigin + orbita(SimTime)).
            var rec = s.Recipe;
            if (rec != null && rec.Bodies != null)
                foreach (var def in rec.Bodies)
                {
                    if (def.Orbit == null) continue;
                    var disc = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    disc.name = "Body_" + s.Name + "_" + def.Name;
                    if (markerShader != null)
                    {
                        var m = new Material(markerShader); m.SetColor("_Color", new Color(0.7f, 0.78f, 0.9f));
                        disc.GetComponent<MeshRenderer>().sharedMaterial = m;
                    }
                    disc.transform.SetParent(transform, false);
                    disc.layer = mapLayer;
                    discRadius[disc] = def.Radius;
                    discTarget[disc] = new SolarSystem.DormantTarget {   // cliccandolo punti il PIANETA (non solo il sistema)
                        name = def.Name, system = s, orbit = def.Orbit, radius = def.Radius, gravity = def.Gravity };
                    sv.discs.Add(disc);
                    sv.bodyOrbits.Add(def.Orbit);
                    sv.bodyRadii.Add(def.Radius);
                    sv.bodyNames.Add(def.Name);

                    if (lineShader != null)
                        sv.rings.Add(NewOrbitRing(lineShader, new Color(0.7f, 0.78f, 0.9f, 0.4f), 96));
                }

            systemVisuals.Add(sv);
        }
    }

    // Assegna ricorsivamente l'oggetto (e figli: i proxy hanno mesh-figlie) al layer della mappa → la camera-mappa li vede.
    void SetMapLayer(GameObject go)
    {
        go.layer = mapLayer;
        foreach (Transform c in go.transform) SetMapLayer(c.gameObject);
    }

    LineRenderer NewOrbitRing(Shader lineShader, Color col, int segments)
    {
        var lgo = new GameObject("OrbitRing");
        lgo.transform.SetParent(transform, false);
        lgo.layer = mapLayer;
        var lr = lgo.AddComponent<LineRenderer>();
        lr.material = new Material(lineShader);
        lr.useWorldSpace = true;
        lr.loop = true;
        lr.numCapVertices = 2;
        lr.positionCount = segments;
        lr.startColor = col; lr.endColor = col;
        return lr;
    }

    // ───────────────────────────────────── Geometria dei sistemi ─────────────────────────────────────

    StarSystem HomeSystem()
    {
        if (solar.Systems != null)
            foreach (var s in solar.Systems)
                if (s != null && s.SystemOrigin.sqrMagnitude < 1.0) return s;
        return solar.Active;
    }

    /// <summary>Il sistema più vicino al pivot (= quello "in vista"). Definisce centro, raggio e mapOrigin.</summary>
    StarSystem SystemInView()
    {
        StarSystem best = null; double bd = double.MaxValue;
        if (solar.Systems != null)
            foreach (var s in solar.Systems)
            {
                if (s == null) continue;
                double d = Vector3d.Distance(s.SystemOrigin, pivotU);
                if (d < bd) { bd = d; best = s; }
            }
        return best ?? solar.Active;
    }

    Vector3d SystemCenterU(StarSystem s) => s != null ? s.SystemOrigin : Vector3d.Zero;

    /// <summary>Il sistema su cui inquadrare aprendo la mappa: se sei DENTRO/vicino a un sistema mostra QUELLO (vedi
    /// dove sei e selezioni i corpi vicini, es. il sole stando a casa); SOLO in crociera lontana da tutti mostra la
    /// DESTINAZIONE scelta (così, a metà viaggio per Vega, centra su Vega e non su casa lontana).</summary>
    StarSystem FocusSystem(Vector3d playerU)
    {
        StarSystem nearest = null; double bd = double.MaxValue;
        if (solar.Systems != null)
            foreach (var s in solar.Systems)
            {
                if (s == null) continue;
                double d = Vector3d.Distance(s.SystemOrigin, playerU);
                if (d < bd) { bd = d; nearest = s; }
            }
        if (nearest != null && bd < SystemRadius(nearest) * 3.0) return nearest;   // sei in un sistema → mostra quello
        if (solar.DestinationDormant != null) return solar.DestinationDormant.system;
        if (solar.DestinationSystem != null) return solar.DestinationSystem;
        if (solar.Destination != null && solar.Destination.System != null) return solar.Destination.System;
        return nearest ?? solar.Active;
    }

    /// <summary>Raggio del sistema = l'orbita più esterna (apoapsis). Dai DATI della ricetta se presente (vale anche da
    /// dormiente); altrimenti dai corpi VIVI filtrati su questo sistema (il sistema-casa, Recipe.Bodies==null) — così non
    /// si conta per sbaglio un sistema distante svegliato, i cui corpi convivono in solar.Bodies.</summary>
    float SystemRadius(StarSystem s)
    {
        float r = 3000f;
        if (s == null) return r;
        if (s.Recipe != null && s.Recipe.Bodies != null)
        {
            foreach (var def in s.Recipe.Bodies)
                if (def.Orbit != null)
                    r = Mathf.Max(r, (float)(def.Orbit.SemiMajorAxis * (1.0 + def.Orbit.Eccentricity)) + def.Radius);
        }
        else
        {
            for (int i = 0; i < solar.Bodies.Count; i++)
            {
                var b = solar.Bodies[i];
                if (b != null && b.System == s && b.Orbit != null)
                    r = Mathf.Max(r, (float)((b.UniversePosition - s.SystemOrigin).magnitude + b.Radius));
            }
        }
        return r;
    }

    /// <summary>Estensione galattica: distanza dal centro in vista al sistema più lontano (0 se uno solo). Dà il tetto
    /// di zoom-out e di far-clip così tutte le stelle entrano in campo senza perdersi nel vuoto.</summary>
    float GalaxyExtent()
    {
        float r = 0f;
        Vector3d c = SystemCenterU(SystemInView());
        if (solar.Systems != null)
            foreach (var s in solar.Systems)
                if (s != null) r = Mathf.Max(r, (float)Vector3d.Distance(s.SystemOrigin, c));
        return r;
    }

    double CamDist() => Vector3d.Distance(camPosU, pivotU);

    /// <summary>Profondità (distanza dalla camera) del punto sotto un pixel, sull'eclittica del sistema in vista (dove
    /// stanno i corpi). Serve a far corrispondere il pan al MOVIMENTO REALE del contenuto sotto il cursore, indipendente
    /// da dove sia il pivot: usare CamDist (camera→pivot) sbagliava quando il pivot era lontano dal contenuto (es.
    /// zoomato su un pianeta col pivot al centro-sistema, o viste lontane). Fallback: CamDist.</summary>
    float DepthAt(Vector2 screenPx)
    {
        var ray = mapCam.ScreenPointToRay(screenPx);
        float planeY = ToMap(SystemCenterU(SystemInView())).y;
        var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
        if (plane.Raycast(ray, out float ent) && ent > 0f) return ent;
        return (float)CamDist();
    }

    /// <summary>Quanti metri-mondo equivalgono a 1 pixel a una data profondità (per pan ~1:1 col contenuto).</summary>
    float WorldPerPixel(float depth) => depth * 2f * Mathf.Tan(mapCam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(Screen.height, 1);

    // ───────────────────────────────────────── Update ─────────────────────────────────────────

    void Update()
    {
        RecordTrail();   // sempre: la traiettoria si accumula anche mentre voli, non solo in mappa

        if (state == State.Off)
        {
            if (walker != null && Input.GetKeyDown(toggleKey)) EnterMap();
            return;
        }

        // uscita: da On E da Entering (così non resti "intrappolato" se premi M durante l'animazione d'entrata)
        if ((state == State.On || state == State.Entering) && (Input.GetKeyDown(toggleKey) || Input.GetKeyDown(KeyCode.Escape)))
            ExitMap();

        mapOrigin = SystemCenterU(SystemInView());   // origine di render = sistema in vista → coords piccole vicino al fuoco

        if (state == State.Entering || state == State.Exiting)
        {
            // Clock dell'animazione ROBUSTO. L'animazione "scattava e si ribaltava" perché l'intera rotazione di 0.7s
            // (che include la picchiata a 90°) veniva compressa in 1-2 frame → sembrava un teletrasporto capovolto.
            // Causa: il frame di EnterMap/ExitMap è PESANTE (1ª volta costruisce i proxy; ogni volta riaccende la
            // camera-mappa al primo render e fa il toggle SuppressDraw della GPU) → tutto quel tempo entra nel
            // deltaTime del frame DOPO e fa schizzare t oltre 1. Cura: (1) SALTA il primo frame, lasciando sfogare
            // l'hitch (la camera resta sulla tua vista esatta); (2) CAPPA il dt, così nessuno spike futuro può
            // comprimere l'animazione. unscaled: la mappa non deve dipendere dal TimeScale del gioco.
            if (animSkipFrame) animSkipFrame = false;
            else t += Mathf.Min(Time.unscaledDeltaTime, 0.05f) / Mathf.Max(0.01f, transitionTime);
            if (state == State.Entering) fadeT += Time.unscaledDeltaTime;   // fade da nero in apertura (tempo assoluto)
            float c = Mathf.Clamp01(t);
            float s = c * c * c * (c * (6f * c - 15f) + 10f);   // smootherstep (C2): velocità E accelerazione nulle ai capi → moto fluido

            if (state == State.Entering)
            {
                Overview(out var toU, out var toRot);
                Vector3d playerU = FloatingOrigin.SceneOrigin + new Vector3d(walker != null ? walker.transform.position : playerCamT.position);
                bool playerNear = (float)Vector3d.Distance(playerU, SystemCenterU(SystemInView())) < SystemRadius(SystemInView()) * 3f;   // sei NEL sistema in vista?

                if (playerNear)
                {
                    // ENTRATA — UN SOLO movimento fluido (niente fasi). POSIZIONE: arco morbido (Bézier quadratica) che si STACCA
                    // dalla superficie verso l'ESTERNO — il punto di controllo è sollevato lungo il "su" locale (radiale dal
                    // pianeta) → la tangente iniziale punta via dal suolo, mai un tuffo dentro il pianeta — e arca fino
                    // all'overview. ROTAZIONE: un solo slerp dalla tua vista a quella d'arrivo (cammino più breve → fluido,
                    // niente giravolte). Tutto guidato da 's' (smootherstep): velocità nulla ai capi, costante e morbida in mezzo.
                    var gb = walker != null ? walker.GravityBody : null;
                    Vector3 localUp = gb != null ? (playerU - gb.UniversePosition).ToVector3().normalized : Vector3.up;
                    if (localUp.sqrMagnitude < 0.5f) localUp = Vector3.up;
                    float clearance = (gb != null ? (float)gb.Radius : 500f) * 4f;        // stacco dal suolo prima di arcare via
                    Vector3d ctrl = fromU + new Vector3d(localUp * clearance);

                    double om = 1.0 - s;
                    camPosU = fromU * (om * om) + ctrl * (2.0 * om * s) + toU * (s * s);
                    camRot = Quaternion.Slerp(fromRot, toRot, s);
                }
                else
                {
                    // LONTANO (in crociera, nessun pianeta da scavalcare): retta diretta dalla tua vista all'overview.
                    camPosU = fromU + (toU - fromU) * s;
                    camRot = Quaternion.Slerp(fromRot, toRot, s);
                }
            }
            else   // uscita: ritorno DIRETTO alla vista del giocatore (le orbite sfumano da sole)
            {
                Vector3d toU = FloatingOrigin.SceneOrigin + new Vector3d(playerCamT.position);
                camPosU = fromU + (toU - fromU) * s;
                camRot = Quaternion.Slerp(fromRot, playerCamT.rotation, s);
            }
            ApplyCameraTransform();
            if (t >= 1f)
            {
                if (state == State.Entering) state = State.On;
                else { FinishExit(); return; }
            }
        }
        else if (state == State.On)
        {
            ApplyCameraTransform();   // transform valido PRIMA dell'input (ScreenPointToRay/WorldToScreenPoint corretti)
            HandleMapInput();         // orbita + pan + zoom + selezione (muta camPosU/camRot/pivotU)
            ApplyCameraTransform();   // riflette l'input (stesso mapOrigin del frame)
        }

        // PIANI DI CLIP dinamici: in zoom-out i corpi/stelle (fino a GalaxyExtent dal fuoco) superavano il far fisso →
        // sparivano. Qui il far segue lo zoom; il near sale con la distanza (precisione di profondità).
        float sysR = SystemRadius(SystemInView());
        float galaxy = GalaxyExtent();
        mapCam.farClipPlane = (float)CamDist() + Mathf.Max(sysR * 4f, galaxy * 1.4f) + sysR;
        mapCam.nearClipPlane = Mathf.Clamp((float)CamDist() * 0.001f, 0.5f, sysR);
        UpdateVisuals();
    }

    void ApplyCameraTransform() => mapCam.transform.SetPositionAndRotation(ToMap(camPosU), camRot);

    /// <summary>Vista d'insieme del sistema in vista: stessa angolazione dall'alto/dietro del vecchio overview, così
    /// l'animazione d'entrata resta fluida. Restituisce posizione (universo) e orientamento.</summary>
    void Overview(out Vector3d posU, out Quaternion rot)
    {
        var sys = SystemInView();
        Vector3d cU = SystemCenterU(sys);
        float R = SystemRadius(sys);
        float d = R / Mathf.Tan(mapCam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.25f;
        Vector3 dir = Quaternion.Euler(MapPitchDefault, 0f, 0f) * Vector3.back;   // dal centro verso la camera: (0,0.55,-0.83)
        posU = cU + new Vector3d(dir * d);
        rot = Quaternion.LookRotation(-dir, Vector3.up);
    }

    // ───────────────────────────────────────── Input camera ─────────────────────────────────────────

    void HandleMapInput()
    {
        // ── PAN col WASD: muove camera E pivot nel piano-schermo (resta puntata sul pivot). ∝ distanza → coerente a ogni zoom.
        float px = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
        float py = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
        if (px != 0f || py != 0f)
        {
            // pan ∝ profondità del contenuto al centro schermo (agnostico dal pivot/sistema), non ∝ CamDist
            float wpp = WorldPerPixel(DepthAt(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)));
            Vector3 move = (mapCam.transform.right * px + mapCam.transform.up * py) * (wpp * Screen.height * MapPanRate * Time.deltaTime);
            Vector3d dU = new Vector3d(move);
            camPosU += dU; pivotU += dU;
        }

        // ── ORBITA RIGIDA col DESTRO trascinato: ruota camera E orientamento attorno al pivot → resti puntato sul pivot.
        // Yaw attorno al mondo-su (orizzonte stabile), pitch attorno al destro della camera (con clamp anti-ribaltamento).
        // Al PREMERE: il pivot va sul punto sotto il cursore → l'universo ruota attorno a ciò che hai sotto il mouse, che
        // resta FERMO lì (proprietà della rotazione rigida: il pivot conserva la sua posizione a schermo).
        if (Input.GetMouseButtonDown(1)) { lastMousePx = Input.mousePosition; pivotU = PivotFromCursor(); }
        if (Input.GetMouseButton(1))
        {
            Vector2 dpx = (Vector2)Input.mousePosition - (Vector2)lastMousePx; lastMousePx = Input.mousePosition;
            Vector3 right = camRot * Vector3.right;
            Quaternion qYaw = Quaternion.AngleAxis(dpx.x * MapRotSpeed, Vector3.up);
            Quaternion qPit = Quaternion.AngleAxis(-dpx.y * MapRotSpeed, right);
            // clamp pitch: non far avvicinare la vista a ±88° dalla verticale (evita ribaltamenti ai poli)
            float elev = Vector3.Angle((qPit * camRot) * Vector3.forward, Vector3.up);
            Quaternion q = (elev > 4f && elev < 176f) ? qPit * qYaw : qYaw;
            camRot = q * camRot;
            Vector3 off = (camPosU - pivotU).ToVector3();
            camPosU = pivotU + new Vector3d(q * off);
        }

        // ── TASTO SINISTRO: trascinare = pan; click senza trascinamento (< 6 px) = selezione + sposta il pivot sul punto.
        if (Input.GetMouseButtonDown(0)) { leftDownPx = Input.mousePosition; lastLeftPx = leftDownPx; leftDragged = false; }
        if (Input.GetMouseButton(0))
        {
            Vector2 cur = Input.mousePosition;
            if ((cur - leftDownPx).sqrMagnitude > 36f) leftDragged = true;
            if (leftDragged)
            {
                Vector2 dd = cur - lastLeftPx;
                // pan 1:1 col punto sotto il cursore (profondità sull'eclittica) → Vega si muove "come ti aspetti",
                // indipendente da dove sia il pivot (prima usava CamDist → drift se il pivot era nel sistema-casa).
                float wpp = WorldPerPixel(DepthAt(cur));
                Vector3 move = (-mapCam.transform.right * dd.x - mapCam.transform.up * dd.y) * wpp;
                Vector3d dU = new Vector3d(move);
                camPosU += dU; pivotU += dU;
            }
            lastLeftPx = cur;
        }
        if (Input.GetMouseButtonUp(0) && !leftDragged) SelectAtCursor();

        // ── ZOOM-VERSO-CURSORE con la rotella. Il FOCUS è il corpo sotto il mouse (anche a qualche px dalla superficie:
        // tolleranza) → zoomi SU di lui e ti fermi alla sua superficie (niente overshoot, niente stop prematuro). Se non
        // punti nulla, il focus è il punto sotto il cursore sull'eclittica. Il clamp è sulla distanza DAL FOCUS, non dal pivot.
        float sc = Mathf.Clamp(Input.mouseScrollDelta.y, -3f, 3f);
        if (Mathf.Abs(sc) > 0.001f)
        {
            float k = Mathf.Pow(0.82f, sc);   // <1 zoom-in, >1 zoom-out
            Vector3 camMap = mapCam.transform.position;
            var ray = mapCam.ScreenPointToRay(Input.mousePosition);

            Vector3 focusMap; float minD;
            if (ZoomFocusBody(out Vector3 bodyMap, out float bodyRadius))
            {
                focusMap = bodyMap;                  // punti un corpo → zoomi su di lui
                minD = bodyRadius * 1.3f;            // ti fermi appena fuori dalla superficie
            }
            else
            {
                // niente corpo: punto sotto il cursore sull'eclittica (o, se la vista è radente, davanti alla camera)
                float planeY = ToMap(SystemCenterU(SystemInView())).y;
                var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
                focusMap = (plane.Raycast(ray, out float ent) && ent > 0f && ent < (float)CamDist() * 8f)
                    ? ray.GetPoint(ent) : camMap + mapCam.transform.forward * (float)CamDist();
                minD = SystemRadius(SystemInView()) * 0.04f;
            }

            Vector3 newCamMap = focusMap + (camMap - focusMap) * k;
            float maxD = Mathf.Max(SystemRadius(SystemInView()) * 3f, GalaxyExtent() * 1.4f);
            Vector3 toFocus = newCamMap - focusMap;
            float dF = toFocus.magnitude;
            float clD = Mathf.Clamp(dF, minD, maxD);
            if (dF > 1e-4f) newCamMap = focusMap + toFocus * (clD / dF);
            camPosU = ToUniverse(newCamMap);
        }

        // ── G: VISTA GALATTICA. Inquadra l'intera galassia (tutti i sistemi); se sei già a scala galattica, torna al
        // sistema in vista. Decide dallo zoom ATTUALE (niente flag che diventa stantio se navighi a mano).
        if (Input.GetKeyDown(galaxyKey))
        {
            if ((float)CamDist() > SystemRadius(SystemInView()) * 20f) FrameSystem();
            else FrameGalaxy();
        }

        // ── N: mostra/nascondi i nomi dei corpi (anche dei pianeti dei sistemi distanti).
        if (Input.GetKeyDown(namesKey)) showBodyNames = !showBodyNames;
    }

    /// <summary>Il corpo sotto il cursore (marker vivo o disco dormiente), per lo zoom. Prima un raycast diretto; poi una
    /// TOLLERANZA a schermo (~4% altezza) → "punto vicino alla superficie" conta come "voglio zoomare su di lui". Ritorna
    /// la sua posizione in spazio-mappa e il raggio (per fermare lo zoom alla superficie).</summary>
    bool ZoomFocusBody(out Vector3 mapPos, out float radius)
    {
        mapPos = default; radius = 0f;
        var go = PickTarget(0.04f);
        if (go != null && TryBodyRadius(go, out radius)) { mapPos = go.transform.position; return true; }
        return false;
    }

    /// <summary>L'oggetto selezionabile (marker vivo · disco/stella di un sistema dormiente) sotto o VICINO al cursore.
    /// Prima un raycast esatto; poi una TOLLERANZA a schermo (tolFrac dell'altezza) → la selezione/lo zoom non chiedono
    /// di centrare il pixel esatto del disco (richiesta di Dario: selezione più generosa). Null se niente è abbastanza vicino.</summary>
    GameObject PickTarget(float tolFrac)
    {
        var ray = mapCam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out var hit, 1e9f, 1 << mapLayer)) return hit.collider.gameObject;
        float tol = Screen.height * tolFrac;
        float best = tol * tol; GameObject bestGo = null;
        Vector2 m = Input.mousePosition;
        foreach (var mk in markers)
            if (mk != null && mk.activeInHierarchy) ConsiderTol(mk, m, ref best, ref bestGo);
        foreach (var sv in systemVisuals)
        {
            if (sv.star != null && sv.star.activeInHierarchy) ConsiderTol(sv.star, m, ref best, ref bestGo);
            foreach (var d in sv.discs)
                if (d != null && d.activeInHierarchy) ConsiderTol(d, m, ref best, ref bestGo);
        }
        return bestGo;
    }

    void ConsiderTol(GameObject go, Vector2 mouse, ref float best, ref GameObject bestGo)
    {
        Vector3 sp = mapCam.WorldToScreenPoint(go.transform.position);
        if (sp.z <= 0f) return;
        float d2 = ((Vector2)sp - mouse).sqrMagnitude;
        if (d2 < best) { best = d2; bestGo = go; }
    }

    bool TryBodyRadius(GameObject go, out float radius)
    {
        if (markerBody.TryGetValue(go, out var b) && b != null) { radius = (float)b.Radius; return true; }
        if (discRadius.TryGetValue(go, out radius)) return true;
        radius = 0f; return false;
    }

    /// <summary>Inquadra l'intera galassia: pivot sul baricentro dei sistemi, distanza per contenerli tutti.</summary>
    void FrameGalaxy()
    {
        Vector3d c = Vector3d.Zero; int n = 0;
        if (solar.Systems != null)
            foreach (var s in solar.Systems) if (s != null) { c += s.SystemOrigin; n++; }
        if (n > 0) c = c / n;
        float R = 3000f;
        if (solar.Systems != null)
            foreach (var s in solar.Systems) if (s != null) R = Mathf.Max(R, (float)Vector3d.Distance(s.SystemOrigin, c) + SystemRadius(s));
        pivotU = c;
        float d = R / Mathf.Tan(mapCam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.25f;
        Vector3 dir = Quaternion.Euler(MapPitchDefault, 0f, 0f) * Vector3.back;
        mapOrigin = SystemCenterU(SystemInView());
        camPosU = c + new Vector3d(dir * d);
        camRot = Quaternion.LookRotation(-dir, Vector3.up);
    }

    /// <summary>Inquadra il sistema in vista (ritorno dalla vista galattica).</summary>
    void FrameSystem()
    {
        Overview(out camPosU, out var rot);
        camRot = rot;
        pivotU = SystemCenterU(SystemInView());
    }
    Vector3 lastMousePx;
    Vector2 leftDownPx, lastLeftPx;
    bool leftDragged;

    // ───────────────────────────────────────── Entrata / uscita ─────────────────────────────────────────

    void EnterMap()
    {
        // INCREMENTALE: aggiunge solo le visuali dei corpi nuovi (sistema svegliato) e toglie quelle dei corpi spariti
        // (sistema addormentato) → niente più lag all'apertura della mappa vicino a un altro sistema.
        SyncBodyVisuals();
        lastBodyCount = solar.Bodies.Count;

        // l'animazione PARTE dalla vista reale del giocatore (in universo) → "zoom-out dalla posizione esatta".
        fromU = FloatingOrigin.SceneOrigin + new Vector3d(playerCamT.position);
        fromRot = playerCamT.rotation;

        // pivot = centro del sistema su cui inquadrare: la DESTINAZIONE se ne hai una (anche un pianeta di un sistema
        // distante selezionato a metà viaggio), altrimenti il sistema in cui ti trovi. L'overview lo inquadra; mapOrigin
        // si ricalcola da quel pivot a ogni frame in Update.
        pivotU = SystemCenterU(FocusSystem(fromU));
        mapOrigin = pivotU;

        camPosU = fromU; camRot = fromRot;
        ApplyCameraTransform();

        // spegni il RenderScaler (la camera giocatore renderizza in una RT che una camera presentatrice stende a
        // schermo: senza spegnerlo coprirebbe la camera-mappa).
        if (playerScaler != null) playerScaler.enabled = false;
        playerCam.enabled = false;
        mapCam.enabled = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (walker != null) walker.ControlsActive = false;
        GpuPlanetRenderer.SuppressDraw = true;   // la superficie GPU entrerebbe nella camera-mappa: in mappa solo i proxy
        IsOpen = true;
        ShowVisuals(true);
        state = State.Entering; t = 0f; animSkipFrame = true; fadeT = 0f;
    }

    void ExitMap()
    {
        fromU = camPosU; fromRot = camRot;
        state = State.Exiting; t = 0f; animSkipFrame = true;
    }

    void FinishExit()
    {
        mapCam.enabled = false;
        playerCam.enabled = true;
        if (playerScaler != null) playerScaler.enabled = true;   // ripristina il render scalato
        GpuPlanetRenderer.SuppressDraw = false;   // torna a disegnare la superficie GPU nella camera del giocatore
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        if (walker != null) walker.ControlsActive = true;
        IsOpen = false;
        ShowVisuals(false);
        state = State.Off;
    }

    /// <summary>Pivot d'orbita dal punto sotto il cursore. Priorità: (1) un corpo/disco colpito (orbiti attorno a LUI);
    /// (2) l'intersezione con l'ECLITTICA — il piano y dei sistemi/orbite, tutti a y≈0 a casa come fra le stelle: clic
    /// nel vuoto = punto sull'eclittica sotto il mouse (l'intuizione di Dario); (3) fallback: un punto davanti alla camera
    /// alla distanza attuale del pivot (raggio d'orbita invariato), se la vista è quasi radente al piano.</summary>
    Vector3d PivotFromCursor()
    {
        var ray = mapCam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out var hit, 1e9f, 1 << mapLayer)) return ToUniverse(hit.point);

        float planeY = ToMap(SystemCenterU(SystemInView())).y;   // eclittica del sistema in vista (y≈0 in universo)
        var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
        if (plane.Raycast(ray, out float ent) && ent > 0f && ent < (float)CamDist() * 8f)
            return ToUniverse(ray.GetPoint(ent));

        return ToUniverse(ray.GetPoint((float)CamDist()));
    }

    void SelectAtCursor()
    {
        var go = PickTarget(0.05f);   // tolleranza generosa: basta cliccare VICINO al disco, non sul pixel esatto
        if (go == null) return;
        if (markerBody.TryGetValue(go, out var b))
        {
            selected = b;
            solar.Destination = b;          // corpo VIVO: in volo l'origine si ancora a lui, il freno X sincronizza
            solar.DestinationSystem = null; solar.DestinationDormant = null;
        }
        else if (discTarget.TryGetValue(go, out var dt))
        {
            // PIANETA di un sistema dormiente: bersaglio diretto → l'autopilota ci vola; avvicinandoti il sistema si
            // sveglia e il pianeta diventa un corpo vero (promozione automatica). Così imposti la rotta a QUEL mondo.
            solar.DestinationDormant = dt;
            solar.Destination = null; solar.DestinationSystem = null; selected = null;
        }
        else if (waypointOf.TryGetValue(go, out var sys))
        {
            // STELLA di un sistema distante → waypoint al SISTEMA (ci voli verso, arrivando si sveglia).
            solar.DestinationSystem = sys;
            solar.Destination = null; solar.DestinationDormant = null; selected = null;
        }
    }

    // ───────────────────────────────────────── Scia ─────────────────────────────────────────

    // Registra la posizione-universo del giocatore quando si è spostato abbastanza dall'ultimo punto.
    void RecordTrail()
    {
        if (walker == null) return;
        Vector3d uni = FloatingOrigin.SceneOrigin + new Vector3d(walker.transform.position);
        if (trailCount > 0)
        {
            double d = Vector3d.Distance(uni, TrailAt(trailCount - 1));
            if (d > trailMaxJump) return;   // salto da ri-ancoraggio (floating origin): NON è moto reale, scarta
            if (d <= trailStep) return;     // troppo vicino all'ultimo punto: non registrare
        }
        if (trailCount < MaxTrailPoints)
            trailRing[(trailHead + trailCount++) % MaxTrailPoints] = uni;   // riempi
        else
        {
            trailRing[trailHead] = uni;                                     // pieno: sovrascrivi il più vecchio, avanza la testa
            trailHead = (trailHead + 1) % MaxTrailPoints;
        }
    }

    // ───────────────────────────────────────── Aggiornamento visuali ─────────────────────────────────────────

    // Dimensione di un corpo in mappa: IN SCALA tra corpi (∝ raggio reale), RIMPICCIOLISCE zoomando out (∝ d^0.7,
    // sotto-lineare → cala RISPETTO alle orbite), con un PAVIMENTO a dimensione-schermo (sempre visibile/selezionabile)
    // e mai sotto la taglia reale da vicino.
    float MapBodySize(float radius, Vector3 mapPos, Vector3 camPos)
    {
        float d = Vector3.Distance(camPos, mapPos);
        float R = 0.001f * radius * Mathf.Pow(Mathf.Max(d, 1f), 0.7f);
        R = Mathf.Max(R, radius);
        return Mathf.Max(R, d * 0.004f);
    }

    /// <summary>Spessore di linea ~COSTANTE a schermo (px) per un oggetto a 'distToCam' dalla camera: width_world =
    /// px · dist · 2·tan(fov/2)/altezza. Usare la distanza dal CENTRO dell'orbita (non dal pivot) evita che le orbite
    /// si gonfino in nastri quando zoomi/sposti il pivot altrove.</summary>
    float ScreenWidth(float distToCam, float px) =>
        Mathf.Max(0.05f, px * distToCam * 2f * Mathf.Tan(mapCam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(Screen.height, 1));

    /// <summary>Il "dettaglio" del sistema (orbite + dischi-pianeti) è visibile quando la camera è abbastanza VICINA al suo
    /// centro; alla scala galattica si nasconde (restano solo le stelle) → niente orbite gonfie/sovrapposte a zoom largo.</summary>
    bool DetailVisible(StarSystem s) =>
        s != null && (float)Vector3d.Distance(camPosU, s.SystemOrigin) < SystemRadius(s) * 14f;

    /// <summary>Solo le ORBITE fanno fade-in entrando in mappa e fade-out uscendo (il resto resta visibile) → l'orbita
    /// che a fine animazione passa "in faccia" è già sfumata via, senza il brutto effetto-bug.</summary>
    float orbitFade = 1f;
    float OrbitFade() =>
        state == State.Entering ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t))
      : state == State.Exiting ? Mathf.SmoothStep(0f, 1f, 1f - Mathf.Clamp01(t))
      : 1f;

    int lastBodyCount;   // se cambia mentre la mappa è APERTA (sistema svegliato), aggiorna le visuali → niente "sparizione"

    void UpdateVisuals()
    {
        Vector3 camPos = mapCam.transform.position;
        orbitFade = OrbitFade();
        if (solar.Bodies.Count != lastBodyCount) { SyncBodyVisuals(); lastBodyCount = solar.Bodies.Count; }

        // "tu sei qui": il giocatore in universo
        if (playerMarker != null && walker != null)
        {
            Vector3d playerU = FloatingOrigin.SceneOrigin + new Vector3d(walker.transform.position);
            Vector3 pm = ToMap(playerU);
            playerMarker.transform.position = pm;
            playerMarker.transform.localScale = Vector3.one * (Vector3.Distance(camPos, pm) * markerScreenSize * 0.6f);
        }

        // SONDA (se dispiegata): la sua posizione-scena + SceneOrigin = universo → spazio-mappa.
        var probe = Probe.Instance;
        bool probeOn = probe != null && probe.gameObject.activeSelf;
        if (probeMarker != null)
        {
            probeMarker.SetActive(probeOn);
            if (probeOn)
            {
                Vector3 pp = ToMap(FloatingOrigin.SceneOrigin + new Vector3d(probe.transform.position));
                probeMarker.transform.position = pp;
                probeMarker.transform.localScale = Vector3.one * (Vector3.Distance(camPos, pp) * markerScreenSize * 0.6f);
            }
        }

        // scia
        if (trail != null)
        {
            int n = trailCount;
            trail.positionCount = n;
            for (int i = 0; i < n; i++) trail.SetPosition(i, ToMap(TrailAt(i)));
            if (n >= 2)
                trail.widthMultiplier = ScreenWidth(Vector3.Distance(camPos, ToMap(TrailAt(n - 1))), 2f);   // costante a schermo
            trail.enabled = n >= 2;
        }

        // CORPI VIVI: marker (click target) + proxy craterizzato, posizionati in universo
        for (int i = 0; i < markers.Count; i++)
        {
            var mk = markers[i];
            var b = markerBody[mk];
            if (b == null) { mk.SetActive(false); continue; }
            Vector3 bp = ToMap(b.UniversePosition);
            mk.transform.position = bp;
            if (proxies.TryGetValue(b, out var px))
            {
                float R = MapBodySize((float)b.Radius, bp, camPos);
                if (b == selected) R *= 1.2f;
                px.position = bp;
                px.localScale = Vector3.one * (R / Mathf.Max(1f, (float)b.Radius));   // mesh a raggio reale → scala a R
                mk.transform.localScale = Vector3.one * (R * 2f);   // sfera-collider (raggio 0.5) → copre il proxy
                // Se la camera è DENTRO il raggio visuale del proxy (= sei sulla superficie del corpo aprendo la mappa),
                // nasconderlo: altrimenti vedi l'INTERNO del guscio per un istante prima di sollevarti. Riappare appena sali.
                bool inside = Vector3.Distance(camPos, bp) < R * 1.05f;
                if (px.gameObject.activeSelf == inside) px.gameObject.SetActive(!inside);
            }
            else
            {
                float sz = MapBodySize((float)b.Radius, bp, camPos);   // la stella: disco stilizzato, stessa scala dei pianeti
                if (b == selected) sz *= 1.4f;
                mk.transform.localScale = Vector3.one * sz;
            }
        }

        // ORBITE dei corpi vivi: relative al genitore (universo)
        for (int i = 0; i < orbits.Count; i++)
        {
            var b = orbitBody[i];
            var lr = orbits[i];
            if (b == null || b.Orbit == null || b.Parent == null || !DetailVisible(b.System)) { lr.enabled = false; continue; }
            lr.enabled = true;
            var oc = orbitCol[i]; oc.a *= orbitFade; lr.startColor = lr.endColor = oc;   // fade entrando/uscendo
            Vector3 centerMap = ToMap(b.Parent.UniversePosition);
            lr.widthMultiplier = ScreenWidth(Vector3.Distance(camPos, centerMap), 2.5f);   // costante a schermo
            int n = lr.positionCount;
            for (int k = 0; k < n; k++)
            {
                double tt = b.Orbit.Period * (k / (double)n);
                Vector3d p = b.Parent.UniversePosition + b.Orbit.GetRelativePosition(tt);
                lr.SetPosition(k, ToMap(p));
            }
        }

        UpdateSystemVisuals(camPos);
    }

    // Sistemi distanti: billboard stella sempre visibile (la "galassia"); dischi-pianeti + anelli visibili quando sei
    // VICINO al sistema (zoomato/spostato lì). Tutto NASCOSTO quando il sistema è SVEGLIO (i corpi veri lo coprono).
    void UpdateSystemVisuals(Vector3 camPos)
    {
        foreach (var sv in systemVisuals)
        {
            var s = sv.sys;
            bool dormant = s != null && !s.Active && !s.Waking;   // in risveglio la stella/i corpi VERI stanno arrivando → cedi il posto

            // billboard della stella, a dimensione-schermo (con pavimento al raggio stella)
            if (sv.star != null)
            {
                Vector3 sp = ToMap(s.SystemOrigin);
                sv.star.transform.position = sp;
                bool selectedSys = s == solar.DestinationSystem;
                float sz = Mathf.Max(Vector3.Distance(camPos, sp) * markerScreenSize * 1.6f, s.StarRadius);
                if (selectedSys) sz *= 2.2f;
                sv.star.transform.localScale = Vector3.one * sz;
                sv.star.SetActive(dormant);   // svegliato → la stella vera (in solar.Bodies) prende il posto
            }

            // dischi-pianeti + anelli: da dormiente, quando la camera è vicina al sistema OPPURE quando il sistema è
            // SELEZIONATO (così, scelta Vega, vedi i suoi pianeti/nomi anche prima di averla visitata — richiesta di Dario).
            bool selectedSys2 = s == solar.DestinationSystem || (solar.DestinationDormant != null && solar.DestinationDormant.system == s);
            bool showDetail = dormant && (selectedSys2
                                          || (float)Vector3d.Distance(camPosU, s.SystemOrigin) < SystemRadius(s) * 12f);
            for (int i = 0; i < sv.discs.Count; i++)
            {
                var disc = sv.discs[i];
                Vector3d bp = s.SystemOrigin + sv.bodyOrbits[i].GetRelativePosition(solar.SimTime);
                Vector3 bm = ToMap(bp);
                disc.transform.position = bm;
                disc.transform.localScale = Vector3.one * MapBodySize(sv.bodyRadii[i], bm, camPos);
                disc.SetActive(showDetail);

                if (i < sv.rings.Count)
                {
                    var lr = sv.rings[i];
                    lr.enabled = showDetail;
                    if (showDetail)
                    {
                        var orb = sv.bodyOrbits[i];
                        lr.startColor = lr.endColor = new Color(0.7f, 0.78f, 0.9f, 0.4f * orbitFade);   // fade entrando/uscendo
                        lr.widthMultiplier = ScreenWidth(Vector3.Distance(camPos, ToMap(s.SystemOrigin)), 2.5f);
                        int n = lr.positionCount;
                        for (int k = 0; k < n; k++)
                        {
                            double tt = orb.Period * (k / (double)n);
                            lr.SetPosition(k, ToMap(s.SystemOrigin + orb.GetRelativePosition(tt)));
                        }
                    }
                }
            }
        }
    }

    void ShowVisuals(bool on)
    {
        for (int i = 0; i < markers.Count; i++) markers[i].SetActive(on);
        foreach (var px in proxies.Values) if (px != null) px.gameObject.SetActive(on);
        for (int i = 0; i < orbits.Count; i++) orbits[i].enabled = on;
        foreach (var sv in systemVisuals)
        {
            if (sv.star != null) sv.star.SetActive(false);   // riattivati selettivamente in UpdateSystemVisuals
            foreach (var d in sv.discs) if (d != null) d.SetActive(false);
            foreach (var r in sv.rings) if (r != null) r.enabled = false;
        }
        if (playerMarker != null) playerMarker.SetActive(on);
        if (probeMarker != null) probeMarker.SetActive(false);   // riattivato da UpdateVisuals se la sonda è dispiegata
        if (trail != null) trail.enabled = on && trailCount >= 2;
    }

    // ───────────────────────────────────────── HUD (etichette) ─────────────────────────────────────────

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (state == State.Off) return;
        float ui = Mathf.Max(1f, Screen.height / 1080f);   // scala col display (Retina/4K)
        if (mapStyle == null) mapStyle = new GUIStyle(GUI.skin.label) { normal = { textColor = Color.white } };
        mapStyle.fontSize = Mathf.RoundToInt(15f * ui);
        GUI.Label(new Rect(20f * ui, Screen.height - 60f * ui, 1400f * ui, 24f * ui),
            (selected != null ? "Selezionato: " + selected.gameObject.name :
             solar.DestinationDormant != null ? "Destinazione: " + solar.DestinationDormant.name + " (" + solar.DestinationDormant.system.Name + ")" :
             solar.DestinationSystem != null ? "Waypoint: ★ " + solar.DestinationSystem.Name : "Clic = seleziona")
            + "   ·   Destro = ruota   ·   WASD = sposta   ·   Rotella = zoom   ·   G = galassia   ·   N = nomi"
            + (showBodyNames ? " (ON)" : " (OFF)") + "   ·   M / Esc", mapStyle);

        // ── ETICHETTE (TU SEI QUI · nomi sistemi · nomi corpi): RACCOLTE e poi DE-CONFLITTATE in verticale prima di
        // disegnarle. A grande distanza io/target/pianeti proiettano quasi nello stesso punto → senza questo si
        // sovrappongono (illeggibili). Greedy: ordina per y, spingi giù chi si sovrappone a una già piazzata.
        if (hereStyle == null) hereStyle = new GUIStyle(GUI.skin.label)
            { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.45f, 1f, 0.55f) } };
        if (nameStyle == null) nameStyle = new GUIStyle(GUI.skin.label)
            { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.85f, 0.9f, 1f) } };
        if (probeStyle == null) probeStyle = new GUIStyle(GUI.skin.label)
            { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.78f, 0.3f) } };
        hereStyle.fontSize = Mathf.RoundToInt(13f * ui);
        nameStyle.fontSize = Mathf.RoundToInt(12f * ui);
        probeStyle.fontSize = Mathf.RoundToInt(13f * ui);

        labelList.Clear();
        if (playerMarker != null)
        {
            float clear = playerMarker.transform.localScale.x;
            var gb = walker != null ? walker.GravityBody : null;
            if (gb != null && proxies.TryGetValue(gb, out var gpx)) clear = gpx.localScale.x * (float)gb.Radius;
            AddLabel(playerMarker.transform.position + mapCam.transform.up * (clear * 1.4f), "TU SEI QUI", hereStyle);
        }
        // SONDA: sempre etichettata quando dispiegata (è un oggetto che vuoi sempre poter localizzare, come "TU SEI QUI").
        if (probeMarker != null && probeMarker.activeSelf)
            AddLabel(probeMarker.transform.position + mapCam.transform.up * (probeMarker.transform.localScale.x * 1.4f), "SONDA", probeStyle);

        foreach (var sv in systemVisuals)
            if (sv.star != null && sv.star.activeSelf && sv.sys != null)
                AddLabel(sv.star.transform.position + mapCam.transform.up * (sv.star.transform.localScale.x * 0.9f), sv.sys.Name, hereStyle);

        // NOMI DEI CORPI (toggle N): corpi vivi solo quando sei ZOOMATO nel loro sistema (a vista d'insieme/galattica
        // affollerebbero — a quella distanza non li si vuole); dischi dei sistemi dormienti quando il loro dettaglio è
        // mostrato (vicino o sistema selezionato → vedi i nomi di Vega anche da lontano, se l'hai scelta).
        if (showBodyNames)
        {
            for (int i = 0; i < markers.Count; i++)
            {
                var mk = markers[i];
                if (mk == null || !mk.activeInHierarchy) continue;
                var b = markerBody[mk];
                if (b == null || b == walker?.GravityBody) continue;   // sotto il giocatore c'è già "TU SEI QUI"
                if (b.System != null && (float)Vector3d.Distance(camPosU, b.System.SystemOrigin) > SystemRadius(b.System) * 2f) continue;   // troppo lontano: niente nome
                AddLabel(mk.transform.position + mapCam.transform.up * (mk.transform.localScale.x * 0.9f), b.gameObject.name, nameStyle);
            }
            foreach (var sv in systemVisuals)
                for (int i = 0; i < sv.discs.Count; i++)
                    if (sv.discs[i] != null && sv.discs[i].activeInHierarchy)
                        AddLabel(sv.discs[i].transform.position + mapCam.transform.up * (sv.discs[i].transform.localScale.x * 0.9f), sv.bodyNames[i], nameStyle);
        }
        DrawLabels();

        // FADE da nero in apertura: maschera lo swap mondo-reale → proxy della mappa (il primo frame non può combaciare).
        // Disegnato per ULTIMO → copre anche le etichette, dissolvenza uniforme.
        float fade = state == State.Entering ? Mathf.Clamp01(1f - fadeT / FadeTime) : 0f;
        if (fade > 0.001f)
        {
            if (fadeTex == null) { fadeTex = new Texture2D(1, 1); fadeTex.SetPixel(0, 0, Color.white); fadeTex.Apply(); }
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, fade);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), fadeTex);
            GUI.color = prev;
        }
    }

    struct LabelItem { public Rect rect; public string text; public GUIStyle style; }
    readonly List<LabelItem> labelList = new List<LabelItem>();

    void AddLabel(Vector3 worldAnchor, string text, GUIStyle style)
    {
        Vector3 sp = mapCam.WorldToScreenPoint(worldAnchor);
        if (sp.z <= 0f) return;
        Vector2 size = style.CalcSize(new GUIContent(text));
        labelList.Add(new LabelItem {
            rect = new Rect(sp.x - size.x * 0.5f, Screen.height - sp.y - size.y * 0.5f, size.x, size.y),
            text = text, style = style });
    }

    void DrawLabels()
    {
        labelList.Sort((a, b) => a.rect.y.CompareTo(b.rect.y));
        for (int i = 0; i < labelList.Count; i++)
        {
            var li = labelList[i];
            bool moved = true; int guard = 0;
            while (moved && guard++ < 30)   // spingi giù finché non si sovrappone più a una già piazzata (j<i)
            {
                moved = false;
                for (int j = 0; j < i; j++)
                    if (li.rect.Overlaps(labelList[j].rect)) { li.rect.y = labelList[j].rect.yMax + 1f; moved = true; }
            }
            labelList[i] = li;
            GUI.Label(li.rect, li.text, li.style);
        }
    }
}

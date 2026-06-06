using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Modalità mappa stile Outer Wilds. Premendo M la camera fa uno zoom-out veloce dal personaggio
/// fino a inquadrare l'intero sistema in prospettiva, con le ORBITE dei corpi disegnate. Si clicca
/// un corpo per SELEZIONARLO (base per la futura navigazione "vai verso"). Premi M (o Esc) per tornare.
///
/// Usa una camera dedicata: in mappa la camera del giocatore viene spenta e accesa quella della mappa
/// (entrambe taggate MainCamera, una sola attiva → Camera.main resta valido). I comandi del walker
/// sono congelati (ControlsActive=false) ma gravità e suolo restano attivi: il giocatore aspetta a terra.
/// </summary>
public class MapMode : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.M;
    [Tooltip("Durata dello zoom-out/in in secondi.")]
    public float transitionTime = 0.7f;
    [Tooltip("Dimensione dei marker come frazione della distanza camera (≈ costante a schermo).")]
    public float markerScreenSize = 0.02f;

    Camera playerCam;
    Transform playerCamT;
    PlanetWalker walker;
    SolarSystem solar;
    RenderScaler playerScaler;   // la camera giocatore renderizza in una RT presentata a parte: va spento in mappa

    Camera mapCam;
    enum State { Off, Entering, On, Exiting }
    State state = State.Off;
    float t;
    Vector3 fromPos; Quaternion fromRot;

    GUIStyle mapStyle, hereStyle;
    GameObject playerMarker;   // "tu sei qui": dove sta il giocatore in scena (su un corpo o in volo)

    // Scia: traiettoria percorsa. Registrata SEMPRE (anche fuori mappa) in coordinate-UNIVERSO (la scena
    // trasla con la floating origin) e riconvertita a scena ogni frame → resta coerente con stella e orbite.
    LineRenderer trail;
    const int MaxTrailPoints = 1024;   // ring buffer: oltre, scarta il punto più vecchio (scia "recente")
    // scia = RING BUFFER vero (O(1) inserimento): niente List.RemoveAt(0) O(n) a ogni punto durante il volo veloce.
    // trailHead = indice del più VECCHIO; i punti in ordine cronologico sono trailRing[(trailHead+i) % cap].
    readonly Vector3d[] trailRing = new Vector3d[MaxTrailPoints];
    int trailHead, trailCount;
    Vector3d TrailAt(int i) => trailRing[(trailHead + i) % MaxTrailPoints];
    float trailStep = 30f;             // distanza minima fra due punti registrati (scalata sul sistema in Init)
    float trailMaxJump = 1e9f;         // salto max plausibile fra due frame: oltre = ri-ancoraggio, si scarta

    const int MapProxyRes = 40;   // risoluzione mesh del proxy "corpo reale" in mappa (basta: è piccolo)
    readonly List<GameObject> markers = new List<GameObject>();
    readonly Dictionary<GameObject, CelestialBody> markerBody = new Dictionary<GameObject, CelestialBody>();
    // proxy del CORPO REALE (mesh craterizzata + materiali bakeati) per i corpi rocciosi: sostituisce il
    // disco piatto. Il marker-sfera resta come bersaglio di click invisibile.
    readonly Dictionary<CelestialBody, Transform> proxies = new Dictionary<CelestialBody, Transform>();
    readonly List<LineRenderer> orbits = new List<LineRenderer>();
    readonly List<CelestialBody> orbitBody = new List<CelestialBody>();
    CelestialBody selected;

    // TAPPA 5 — MAPPA GALATTICA: billboard delle stelle dei sistemi DISTANTI (alla loro SystemOrigin). Compaiono
    // quando zoomi oltre il sistema-casa; danno la "galassia da navigare". Niente corpi/fette: solo un disco unlit.
    readonly List<GameObject> systemMarkers = new List<GameObject>();
    readonly List<StarSystem> systemMarkerSys = new List<StarSystem>();
    readonly Dictionary<GameObject, StarSystem> systemOf = new Dictionary<GameObject, StarSystem>();
    int builtBodyCount;   // n. corpi per cui sono costruite le visuali → se cambia (sistema svegliato) si ricostruisce

    // Camera ORBITALE della mappa: yaw/pitch (trascina col DESTRO), distanza (rotella), attorno a un FOCUS.
    // Il focus insegue il corpo SELEZIONATO (focusFollows, con smoothing) finché non PANni con WASD → si sgancia
    // e roami libero; la selezione successiva lo ri-aggancia. Click sinistro = seleziona.
    float mapYaw, mapPitch, mapDist;
    Vector3 focusPos, lastMousePx;
    Vector2 leftDownPx, lastLeftPx;   // pan col TRASCINAMENTO SINISTRO (distingue click=selezione da drag=pan)
    bool leftDragged;
    bool focusFollows;
    const float MapPitchDefault = 33.5f, MapRotSpeed = 0.25f, MapPanRate = 0.7f;

    public CelestialBody Selected => selected;
    public bool Active => state != State.Off;
    public Camera ViewCamera => mapCam;   // la camera della mappa (per il reticolo di rotta in modalità mappa)

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

        trailStep = SystemRadius() * 0.0015f;   // ~42 m su un sistema da 28 km → scia fine ma punti contenuti
        trailMaxJump = SystemRadius() * 0.25f;  // un frame reale non copre mai tanto: oltre = ri-ancoraggio
        BuildVisuals();
        ShowVisuals(false);
    }

    void BuildVisuals()
    {
        var markerShader = Shader.Find("Unlit/Color");
        var lineShader = Shader.Find("Sprites/Default");
        float orbitWidth = SystemRadius() * 0.004f;

        BuildBodyVisuals(markerShader, lineShader, orbitWidth);

        // TAPPA 5 — billboard delle stelle dei sistemi DISTANTI (SystemOrigin != Zero): disco unlit del colore della
        // stella. CLICCABILI (collider) → selezionano il sistema come WAYPOINT galattico.
        if (solar.Systems != null)
            foreach (var s in solar.Systems)
            {
                if (s == null || s.SystemOrigin.sqrMagnitude < 1.0) continue;   // salta il sistema-casa (origine Zero)
                var smk = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                smk.name = "System_" + s.Name;
                systemOf[smk] = s;   // bersaglio di click → seleziona il sistema
                if (markerShader != null)
                {
                    var m = new Material(markerShader); m.SetColor("_Color", s.StarColor);
                    smk.GetComponent<MeshRenderer>().sharedMaterial = m;
                }
                smk.transform.SetParent(transform, false);
                systemMarkers.Add(smk); systemMarkerSys.Add(s);
            }

        // marker "TU SEI QUI": verde acceso, distinto dai corpi. Niente collider → non intercetta i
        // click (non è selezionabile) e non copre i corpi dietro di sé.
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

        // scia del giocatore: filo verde, brilla al capo recente e sfuma sul vecchio (coda a cometa)
        if (lineShader != null)
        {
            var tgo = new GameObject("PlayerTrail");
            tgo.transform.SetParent(transform, false);
            trail = tgo.AddComponent<LineRenderer>();
            trail.material = new Material(lineShader);
            trail.useWorldSpace = true;
            trail.widthMultiplier = orbitWidth * 0.8f;
            trail.numCapVertices = 2;
            trail.positionCount = 0;
            trail.startColor = new Color(0.4f, 1f, 0.5f, 0.12f);   // capo VECCHIO (indice 0): quasi spento
            trail.endColor = new Color(0.45f, 1f, 0.55f, 0.9f);    // capo RECENTE: acceso
        }
    }

    // Visuali dei CORPI (marker cliccabili + proxy + orbite). Estratte così si possono RICOSTRUIRE quando un sistema
    // distante si SVEGLIA (i suoi corpi entrano in solar.Bodies dopo l'avvio) → i suoi pianeti diventano selezionabili
    // e mostrano l'orbita, come quelli di casa. I billboard dei sistemi / "tu sei qui" / scia NON cambiano (una volta).
    void BuildBodyVisuals(Shader markerShader, Shader lineShader, float orbitWidth)
    {
        for (int i = 0; i < solar.Bodies.Count; i++)
        {
            var b = solar.Bodies[i];
            if (b == null) continue;
            bool isStar = b.Orbit == null;
            // la stella usa il COLORE del suo sistema (Vega = blu, ecc.), non un giallo fisso (prima la stella
            // svegliata diventava gialla anche se il suo billboard dormiente era blu).
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
                markers.Add(mk);
                markerBody[mk] = b;

                var terr = b.GetComponent<PlanetTerrain>();
                if (terr != null && terr.Recipe != null)
                {
                    mk.GetComponent<MeshRenderer>().enabled = false;
                    var pgo = new GameObject("Proxy_" + b.gameObject.name);
                    pgo.transform.SetParent(transform, false);
                    var proxy = pgo.AddComponent<SingleMeshPlanet>();
                    proxy.Build(terr, terr.FaceMaterials, MapProxyRes, MapProxyRes);
                    proxies[b] = pgo.transform;
                }
            }

            if (b.Orbit != null && b.Parent != null && lineShader != null)
            {
                var lgo = new GameObject("Orbit_" + b.gameObject.name);
                lgo.transform.SetParent(transform, false);
                var lr = lgo.AddComponent<LineRenderer>();
                lr.material = new Material(lineShader);
                lr.useWorldSpace = true;
                lr.loop = true;
                lr.widthMultiplier = orbitWidth;
                lr.numCapVertices = 2;
                lr.positionCount = 96;
                var faint = new Color(col.r, col.g, col.b, 0.5f);
                lr.startColor = faint; lr.endColor = faint;
                orbits.Add(lr);
                orbitBody.Add(b);
            }
        }
        builtBodyCount = solar.Bodies.Count;
    }

    // Ricostruisce SOLO le visuali dei corpi (quando l'insieme dei corpi cambia: un sistema svegliato/addormentato).
    void RebuildBodyVisuals()
    {
        foreach (var mk in markers) if (mk) Destroy(mk);
        markers.Clear(); markerBody.Clear();
        foreach (var kv in proxies) if (kv.Value) Destroy(kv.Value.gameObject);
        proxies.Clear();
        foreach (var lr in orbits) if (lr) Destroy(lr.gameObject);
        orbits.Clear(); orbitBody.Clear();
        BuildBodyVisuals(Shader.Find("Unlit/Color"), Shader.Find("Sprites/Default"), SystemRadius() * 0.004f);
    }

    // Centro del sistema = la stella (corpo senza orbita); fallback: media dei corpi.
    Vector3 SystemCenter()
    {
        for (int i = 0; i < solar.Bodies.Count; i++)
            if (solar.Bodies[i] != null && solar.Bodies[i].Orbit == null)
                return solar.Bodies[i].transform.position;
        Vector3 sum = Vector3.zero; int c = 0;
        for (int i = 0; i < solar.Bodies.Count; i++)
            if (solar.Bodies[i] != null) { sum += solar.Bodies[i].transform.position; c++; }
        return c > 0 ? sum / c : Vector3.zero;
    }

    // Raggio del sistema = l'orbita più esterna (apoapsis); così l'inquadratura contiene tutto.
    float SystemRadius()
    {
        float r = 3000f;
        for (int i = 0; i < solar.Bodies.Count; i++)
        {
            var b = solar.Bodies[i];
            if (b != null && b.Orbit != null)
                r = Mathf.Max(r, (float)(b.Orbit.SemiMajorAxis * (1.0 + b.Orbit.Eccentricity)));
        }
        return r;
    }

    /// <summary>Distanza di inquadratura iniziale: l'intero sistema entra in campo (come il vecchio overview).</summary>
    float DefaultDist() => SystemRadius() / Mathf.Tan(mapCam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.25f;

    /// <summary>TAPPA 5 — raggio GALATTICO: distanza-scena al sistema distante più lontano (0 se non ce ne sono).
    /// Limita lo zoom-out al livello galattico così le stelle distanti entrano in campo senza perdersi nel vuoto.</summary>
    float GalaxyRadius()
    {
        float r = 0f;
        if (solar.Systems != null)
            foreach (var s in solar.Systems)
                if (s != null) r = Mathf.Max(r, (s.SystemOrigin - FloatingOrigin.SceneOrigin).ToVector3().magnitude);
        return r;
    }

    /// <summary>Punto attorno a cui orbita la camera: il corpo selezionato (così zoomi/ruoti su di lui) o il
    /// centro del sistema se niente è selezionato.</summary>
    Vector3 FocusTarget()
    {
        if (selected != null) return selected.transform.position;
        if (solar.DestinationSystem != null) return (solar.DestinationSystem.SystemOrigin - FloatingOrigin.SceneOrigin).ToVector3();
        return SystemCenter();
    }

    /// <summary>Tiene il focus entro un limite (raggio del sistema, o GALATTICO se ci sono sistemi distanti) → non ci
    /// si perde nel vuoto, ma si può arrivare fino alle stelle lontane.</summary>
    void ClampFocus()
    {
        Vector3 c = SystemCenter();
        float lim = Mathf.Max(SystemRadius() * 1.5f, GalaxyRadius() * 1.3f);
        focusPos = c + Vector3.ClampMagnitude(focusPos - c, lim);
    }

    /// <summary>Vista della camera dai parametri orbitali (yaw/pitch/distanza attorno a focusPos). Default
    /// (yaw 0, pitch 33.5°) = lo stesso angolo dall'alto/dietro del vecchio overview → l'entrata resta fluida.</summary>
    void ComputeOverview(out Vector3 pos, out Quaternion rot)
    {
        Vector3 c = focusPos;
        Vector3 dir = Quaternion.Euler(mapPitch, mapYaw, 0f) * Vector3.back;   // pitch=33.5,yaw=0 → (0,0.55,-0.83)
        pos = c + dir * mapDist;
        rot = Quaternion.LookRotation(c - pos, Vector3.up);
    }

    /// <summary>Pan (WASD) + rotazione (trascina col DESTRO, asse verticale invertito) + zoom (rotella) +
    /// selezione (click sinistro).</summary>
    void HandleMapInput()
    {
        // PAN col WASD: muove il focus nel PIANO dello schermo (destra/su della camera) → si sgancia dal corpo.
        // Velocità ∝ distanza → pan coerente a ogni zoom. Senza WASD, il focus insegue il corpo selezionato.
        float px = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
        float py = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
        if (px != 0f || py != 0f)
        {
            focusFollows = false;
            Vector3 move = mapCam.transform.right * px + mapCam.transform.up * py;
            focusPos += move * (mapDist * MapPanRate * Time.deltaTime);
            ClampFocus();
        }
        else if (focusFollows)
            focusPos = Vector3.Lerp(focusPos, FocusTarget(), 1f - Mathf.Exp(-Time.deltaTime * 6f));

        // ROTAZIONE col tasto DESTRO trascinato. Asse verticale INVERTITO (trascini su → guardi più di lato).
        // All'inizio del trascinamento il PIVOT diventa il punto sotto il cursore → ruoti attorno a dove clicchi.
        // Ruota attorno al CENTRO della vista (il focus): nessuno snap. NB: con una camera che punta sempre il focus,
        // spostare il pivot sul punto cliccato la farebbe RI-MIRARE = salto → si tiene il centro vista come pivot.
        if (Input.GetMouseButtonDown(1)) lastMousePx = Input.mousePosition;
        if (Input.GetMouseButton(1))
        {
            Vector2 d = (Vector2)Input.mousePosition - (Vector2)lastMousePx; lastMousePx = Input.mousePosition;
            mapYaw += d.x * MapRotSpeed;
            mapPitch = Mathf.Clamp(mapPitch - d.y * MapRotSpeed, 8f, 88f);   // invertito + niente ribaltamenti ai poli
        }

        // TASTO SINISTRO: TRASCINARE = pan della mappa (trascini il contenuto, il focus va all'opposto); un CLICK
        // senza trascinamento (movimento < 6 px) = SELEZIONE. Così pan e selezione convivono sullo stesso tasto.
        if (Input.GetMouseButtonDown(0)) { leftDownPx = Input.mousePosition; lastLeftPx = leftDownPx; leftDragged = false; }
        if (Input.GetMouseButton(0))
        {
            Vector2 cur = Input.mousePosition;
            if ((cur - leftDownPx).sqrMagnitude > 36f) leftDragged = true;
            if (leftDragged)
            {
                focusFollows = false;
                Vector2 dd = cur - lastLeftPx;
                float panK = mapDist / Mathf.Max(Screen.height, 1) * 1.5f;
                focusPos += (-mapCam.transform.right * dd.x - mapCam.transform.up * dd.y) * panK;
                ClampFocus();
            }
            lastLeftPx = cur;
        }
        if (Input.GetMouseButtonUp(0) && !leftDragged) SelectAtCursor();

        // ZOOM con la rotella, verso/da il focus. Limiti: vicino al raggio del corpo (non ci entri dentro) … molto largo.
        float sc = Mathf.Clamp(Input.mouseScrollDelta.y, -3f, 3f);
        if (Mathf.Abs(sc) > 0.001f)
        {
            // min: vicino al corpo selezionato (per ispezionarlo da presso) o una frazione del sistema; max: cappato
            // (oltre il sistema sparisce nel vuoto). Il far clip è dinamico → niente corpi che scompaiono in zoom-out.
            float minD = selected != null ? (float)selected.Radius * 1.6f : SystemRadius() * 0.04f;
            // TAPPA 5: lo zoom-out arriva al livello GALATTICO (le stelle distanti in campo), se la galassia ha più
            // di un sistema; altrimenti resta al sistema-casa come prima.
            float maxD = Mathf.Max(SystemRadius() * 3f, GalaxyRadius() * 1.4f);
            float oldD = mapDist;
            mapDist = Mathf.Clamp(mapDist * Mathf.Pow(0.82f, sc), minD, maxD);

            // ZOOM VERSO IL CURSORE: punto sotto il mouse sul piano per il focus, sposto il focus verso di lui in
            // proporzione al cambio di zoom. GUARDIA: col cursore ai bordi il raggio è quasi PARALLELO al piano →
            // ent enorme → il focus schizzava lontano e poi la mappa "rimbalzava" (il glitch). Applico solo se ent è sano.
            var ray = mapCam.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(-mapCam.transform.forward, focusPos);
            if (plane.Raycast(ray, out float ent) && ent > 0f && ent < mapDist * 3f)
            {
                Vector3 hit = ray.GetPoint(ent);
                focusPos = hit + (focusPos - hit) * (mapDist / Mathf.Max(oldD, 1e-4f));
                focusFollows = false;
                ClampFocus();
            }
        }
    }

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

        if (state == State.Entering || state == State.Exiting)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, transitionTime);
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            ComputeOverview(out var ovPos, out var ovRot);
            Vector3 tgtPos = state == State.Entering ? ovPos : playerCamT.position;
            Quaternion tgtRot = state == State.Entering ? ovRot : playerCamT.rotation;
            mapCam.transform.SetPositionAndRotation(Vector3.Lerp(fromPos, tgtPos, s), Quaternion.Slerp(fromRot, tgtRot, s));
            if (t >= 1f)
            {
                if (state == State.Entering) state = State.On;
                else FinishExit();
            }
        }
        else if (state == State.On)
        {
            HandleMapInput();   // zoom + rotazione + selezione (aggiorna anche focusPos)
            ComputeOverview(out var ovPos, out var ovRot);
            mapCam.transform.SetPositionAndRotation(ovPos, ovRot);
        }

        if (state != State.Off)
        {
            // PIANI DI CLIP dinamici: in zoom-out i corpi (fino a ~SystemRadius dal focus) superavano il far clip
            // fisso (500k) → sparivano. Qui il far segue lo zoom; il near sale con la distanza (precisione di profondità).
            float sysR = SystemRadius();
            mapCam.farClipPlane = mapDist + Mathf.Max(sysR * 4f, GalaxyRadius() * 1.2f);   // raggiunge le stelle distanti (Tappa 5)
            mapCam.nearClipPlane = Mathf.Clamp(mapDist * 0.001f, 0.5f, sysR);
            UpdateVisuals();
        }
    }

    void EnterMap()
    {
        // se l'insieme dei corpi è cambiato (un sistema distante si è svegliato/addormentato), ricostruisci le
        // visuali dei corpi → i pianeti del nuovo sistema diventano selezionabili e mostrano l'orbita.
        if (solar.Bodies.Count != builtBodyCount) RebuildBodyVisuals();

        fromPos = playerCamT.position; fromRot = playerCamT.rotation;
        mapCam.transform.SetPositionAndRotation(fromPos, fromRot);
        // spegni il RenderScaler: renderizza la camera giocatore in una RT che una camera
        // presentatrice stende a schermo ogni frame → senza spegnerlo coprirebbe la camera-mappa.
        if (playerScaler != null) playerScaler.enabled = false;
        playerCam.enabled = false;
        mapCam.enabled = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (walker != null) walker.ControlsActive = false;
        GpuPlanetRenderer.SuppressDraw = true;   // la superficie GPU entrerebbe nella camera-mappa: in mappa solo i proxy
        ShowVisuals(true);
        // parametri orbitali iniziali = lo stesso overview di prima (intero sistema, dall'alto/dietro)
        mapYaw = 0f; mapPitch = MapPitchDefault; mapDist = DefaultDist();
        focusPos = SystemCenter(); focusFollows = true;
        state = State.Entering; t = 0f;
    }

    void ExitMap()
    {
        fromPos = mapCam.transform.position; fromRot = mapCam.transform.rotation;
        state = State.Exiting; t = 0f;
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
        ShowVisuals(false);
        state = State.Off;
    }

    void SelectAtCursor()
    {
        var ray = mapCam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out var hit, 1e9f)) return;
        if (markerBody.TryGetValue(hit.collider.gameObject, out var b))
        {
            selected = b;
            solar.Destination = b;          // in volo l'origine si ancora a lei → ferma e centrata, il freno X la sincronizza
            solar.DestinationSystem = null; // un corpo e un sistema distante sono mutuamente esclusivi
            focusFollows = true;            // ri-aggancia la camera al corpo scelto (dopo un eventuale pan libero)
        }
        else if (systemOf.TryGetValue(hit.collider.gameObject, out var sys))
        {
            // WAYPOINT GALATTICO: selezioni un sistema distante → ci voli verso (il reticolo di rotta lo indica);
            // arrivando, si sveglia (Tappa 4) e puoi selezionarne i corpi.
            solar.DestinationSystem = sys;
            solar.Destination = null;
            selected = null;
            focusFollows = true;   // centra la mappa sul sistema selezionato (come per i corpi)
        }
    }

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

    // Dimensione di un corpo in mappa: IN SCALA tra corpi (∝ raggio reale) e che RIMPICCIOLISCE zoomando out
    // (∝ d^0.7, sotto-lineare → i corpi calano RISPETTO alle orbite e non si fondono), con un PAVIMENTO a
    // dimensione-schermo (sempre selezionabile/visibile, anche il sole da lontanissimo) e mai sotto la taglia reale da vicino.
    float MapBodySize(CelestialBody b, Vector3 camPos)
    {
        float d = Vector3.Distance(camPos, b.transform.position);
        float R = 0.001f * (float)b.Radius * Mathf.Pow(Mathf.Max(d, 1f), 0.7f);
        R = Mathf.Max(R, (float)b.Radius);
        return Mathf.Max(R, d * 0.004f);
    }

    void UpdateVisuals()
    {
        Vector3 camPos = mapCam.transform.position;
        if (playerMarker != null && walker != null)
        {
            playerMarker.transform.position = walker.transform.position;
            playerMarker.transform.localScale = Vector3.one *
                (Vector3.Distance(camPos, walker.transform.position) * markerScreenSize * 0.6f);
        }
        if (trail != null)
        {
            trail.widthMultiplier = Mathf.Max(0.2f, mapDist * 0.0016f);   // ∝ zoom, come le orbite (era fissa = nastro verde enorme)
            int n = trailCount;
            trail.positionCount = n;
            for (int i = 0; i < n; i++) trail.SetPosition(i, (TrailAt(i) - FloatingOrigin.SceneOrigin).ToVector3());
            trail.enabled = n >= 2;   // serve almeno un segmento
        }
        for (int i = 0; i < markers.Count; i++)
        {
            var mk = markers[i];
            var b = markerBody[mk];
            if (b == null) { mk.SetActive(false); continue; }
            mk.transform.position = b.transform.position;
            if (proxies.TryGetValue(b, out var px))
            {
                float R = MapBodySize(b, camPos);
                if (b == selected) R *= 1.2f;
                px.position = b.transform.position;
                px.localScale = Vector3.one * (R / Mathf.Max(1f, (float)b.Radius));   // mesh a raggio reale → scala a R
                mk.transform.localScale = Vector3.one * (R * 2f);   // sfera-collider (raggio 0.5) → copre il proxy
            }
            else
            {
                // la stella: disco stilizzato, STESSA scala dei pianeti (in scala tra loro).
                float sz = MapBodySize(b, camPos);
                if (b == selected) sz *= 1.4f;
                mk.transform.localScale = Vector3.one * sz;
            }
        }

        // TAPPA 5 — stelle dei sistemi distanti: posizione = SystemOrigin - SceneOrigin (scena), scala screen-costante.
        // Visibili solo a zoom GALATTICO (mapDist oltre ~2× il sistema-casa) per non affollare la vista locale.
        bool galacticZoom = mapDist > SystemRadius() * 2f;
        for (int i = 0; i < systemMarkers.Count; i++)
        {
            var smk = systemMarkers[i]; var s = systemMarkerSys[i];
            if (smk == null || s == null) continue;
            Vector3 sp = (s.SystemOrigin - FloatingOrigin.SceneOrigin).ToVector3();
            smk.transform.position = sp;
            bool selectedSys = s == solar.DestinationSystem;
            float sz = Mathf.Max(Vector3.Distance(camPos, sp) * markerScreenSize * 1.6f, s.StarRadius);
            if (selectedSys) sz *= 2.2f;   // selezione BEN visibile (più grande)
            smk.transform.localScale = Vector3.one * sz;
            // visibile a zoom galattico OPPURE se è quello che stai guardando (vicino al focus) → zoomando su Vega
            // NON sparisce più (prima si spegneva uscendo dallo zoom galattico → schermo nero).
            bool nearFocus = (focusPos - sp).magnitude < mapDist * 2.5f;
            smk.SetActive(galacticZoom || nearFocus);
        }

        for (int i = 0; i < orbits.Count; i++)
        {
            var b = orbitBody[i];
            var lr = orbits[i];
            if (b == null || b.Orbit == null || b.Parent == null) { lr.enabled = false; continue; }
            lr.enabled = true;
            Vector3 parentScene = b.Parent.transform.position;
            // SPESSORE ∝ ZOOM (mapDist) → larghezza ~costante a schermo a ogni zoom. Usare la distanza dal GENITORE
            // sbagliava: l'orbita del baricentro (genitore = stella lontana) restava un nastro enorme anche zoomata.
            // (Il LineRenderer non sa lo screen-space per-vertice di Wanderer/OrbitLine: mapDist basta per la mappa.)
            lr.widthMultiplier = Mathf.Max(0.3f, mapDist * 0.0022f);
            int n = lr.positionCount;
            for (int k = 0; k < n; k++)
            {
                double tt = b.Orbit.Period * (k / (double)n);
                lr.SetPosition(k, parentScene + b.Orbit.GetRelativePosition(tt).ToVector3());
            }
        }
    }

    void ShowVisuals(bool on)
    {
        for (int i = 0; i < markers.Count; i++) markers[i].SetActive(on);
        for (int i = 0; i < systemMarkers.Count; i++) if (systemMarkers[i] != null) systemMarkers[i].SetActive(false);   // accesi solo a zoom galattico (UpdateVisuals)
        foreach (var px in proxies.Values) if (px != null) px.gameObject.SetActive(on);
        for (int i = 0; i < orbits.Count; i++) orbits[i].enabled = on;
        if (playerMarker != null) playerMarker.SetActive(on);
        if (trail != null) trail.enabled = on && trailCount >= 2;
    }

    void OnGUI()
    {
        if (state == State.Off) return;
        if (Event.current.type != EventType.Repaint) return;
        float ui = Mathf.Max(1f, Screen.height / 1080f);   // scala col display (Retina/4K)
        if (mapStyle == null) mapStyle = new GUIStyle(GUI.skin.label) { normal = { textColor = Color.white } };
        mapStyle.fontSize = Mathf.RoundToInt(15f * ui);
        GUI.Label(new Rect(20f * ui, Screen.height - 60f * ui, 1100f * ui, 24f * ui),
            (selected != null ? "Selezionato: " + selected.gameObject.name : "Clic = seleziona")
            + "   ·   Destro = ruota   ·   WASD = sposta   ·   Rotella = zoom   ·   M / Esc", mapStyle);

        // etichetta "TU SEI QUI" ancorata al marker del giocatore (solo se davanti alla camera)
        if (playerMarker != null && mapCam != null)
        {
            if (hereStyle == null) hereStyle = new GUIStyle(GUI.skin.label)
                { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.45f, 1f, 0.55f) } };
            hereStyle.fontSize = Mathf.RoundToInt(13f * ui);
            // l'etichetta fluttua SOPRA l'eventuale pianeta su cui ti trovi (offset lungo l'alto-schermo pari al
            // suo raggio apparente) → non si sovrappone alla superficie. In spazio profondo l'offset è minimo.
            float clear = playerMarker.transform.localScale.x;
            var gb = walker != null ? walker.GravityBody : null;
            if (gb != null && proxies.TryGetValue(gb, out var gpx)) clear = gpx.localScale.x * (float)gb.Radius;
            Vector3 anchor = playerMarker.transform.position + mapCam.transform.up * (clear * 1.4f);
            Vector3 sp = mapCam.WorldToScreenPoint(anchor);
            if (sp.z > 0f)
                GUI.Label(new Rect(sp.x - 100f * ui, Screen.height - sp.y - 10f * ui, 200f * ui, 20f * ui), "TU SEI QUI", hereStyle);
        }

        // TAPPA 5 — etichette dei sistemi distanti (solo a zoom galattico, quando i loro billboard sono accesi)
        if (hereStyle != null)
            for (int i = 0; i < systemMarkers.Count; i++)
            {
                var smk = systemMarkers[i]; var s = systemMarkerSys[i];
                if (smk == null || !smk.activeSelf || s == null) continue;
                Vector3 ssp = mapCam.WorldToScreenPoint(smk.transform.position + mapCam.transform.up * (smk.transform.localScale.x * 0.9f));
                if (ssp.z > 0f)
                    GUI.Label(new Rect(ssp.x - 100f * ui, Screen.height - ssp.y - 10f * ui, 200f * ui, 20f * ui), s.Name, hereStyle);
            }
    }
}

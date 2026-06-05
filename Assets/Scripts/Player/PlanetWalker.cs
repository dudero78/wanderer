using UnityEngine;

/// <summary>
/// Controller di camminata su un corpo sferico. La gravità punta sempre verso
/// il centro del pianeta più vicino, quindi "giù" cambia a seconda di dove sei
/// sulla superficie. Usa un Rigidbody con gravità custom: la gravità di Unity
/// è sempre verso il basso del mondo e qui non servirebbe a nulla.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlanetWalker : MonoBehaviour
{
    public float moveSpeed = 8f;
    public bool wallStop = true;        // collisione laterale: ferma la marcia in salita ripida (niente attraversare pareti/dirupi)
    public float wallSlope = 1.2f;      // pendenza oltre cui sei bloccato in salita (tan: 1.0≈45°, 1.2≈50°). wallStop=false = off
    public float wallProbe = 1.5f;      // distanza (m) avanti del campione di pendenza
    public float jumpSpeed = 7f;
    public float mouseSensitivity = 2.2f;
    public Transform cameraPivot;

    [Header("Jetpack")]
    public float jetThrust = 30f;       // spinta dei motori a velocità di manovra (uguale su tutti gli assi)
    public float jetDamping = 1.2f;     // smorzamento di manovra: con jetThrust dà la terminale ~25 m/s
    public float maxFlySpeed = 30f;     // limite verticale a piedi (archi di salto)
    public float maxFallSpeed = 55f;    // velocità terminale di caduta a piedi

    // Due modelli di volo, commutabili con N. Crociera: la potenza dei motori cresce con la
    // quota e con quanto tieni la spinta, così resti maneggevole vicino al suolo (atterraggio)
    // e veloce in alto. Newtoniano: niente attrito, la velocità si accumula (delta-v reale,
    // alla Outer Wilds) — sarà il default dell'astronave.
    public enum FlightModel { Cruise, Newtonian }

    [Header("Crociera (potenza a quota + rampa)")]
    public float cruiseThrust = 110f;     // spinta a boost pieno (terminale ~ cruiseThrust/cruiseDamping)
    public float cruiseDamping = 0.5f;    // smorzamento a boost pieno: 110/0.5 ≈ 220 m/s di crociera
    public float boostRampTime = 3.5f;    // secondi per salire a piena crociera tenendo la spinta
    public float boostFalloffTime = 1.2f; // secondi per tornare a manovra (più rapido: serve per atterrare)
    public float cruiseAltLow = 12f;      // sotto questa quota i motori restano alla potenza di ora
    public float cruiseAltHigh = 120f;    // sopra questa quota la crociera è pienamente sbloccata

    [Header("Newtoniano")]
    public float newtonThrust = 120f;     // accelerazione a pieno regime, NESSUN limite di velocità (delta-v reale): spinta DECISA da astronave (OW). Scala con la gravità locale (max(.,1.6·g)) → vicino alla stella resta una lotta vera
    public float thrustRampTime = 1.0f;   // secondi perché i motori salgano a piena spinta: onset BREVE → senti la gravità un istante, poi i motori "prendono" e ti scagliano
    public float brakeAccel = 320f;       // freno di assetto: picco di decelerazione nella fascia media (doma centinaia di m/s)
    public float brakeTimeConstant = 2.2f;// ad ALTA velocità la decel cresce con la velocità (frena entro ~questi secondi): da migliaia di m/s frena molto più forte del picco costante
    public float brakeRampTime = 0.3f;    // secondi per salire a piena potenza tenendo X (anti-tap accidentale)
    public float brakeKnee = 40f;         // sotto questa velocità il freno entra nella coda (inizia prima)
    public float brakeEaseTau = 0.35f;    // costante di tempo dell'avvicinamento finale a 0 (più basso = più rapido): gli ultimi numeri scorrono SVELTI fino allo 0
    public float brakeFloor = 20f;        // decel minima nella coda: governa gli ULTIMI numeri (3,2,1) → li chiude svelti, niente strisciamento vicino a 0
    public float softStopAccel = 700f;    // decelerazione dello STOP DOLCE (autopilota interrotto): più RAPIDA del freno X (brakeAccel)
    public float softStopEndSpeed = 0.6f; // sotto questa velocità relativa lo stop dolce molla e ridà il controllo
    public KeyCode brakeKey = KeyCode.X;  // tienilo premuto per annullare l'orbita e poter atterrare
    public float rollSpeed = 75f;         // gradi/s di rollio con Q/E in volo libero

    [Header("Autopilota (T): aggancia il corpo selezionato, allinea, accelera, frena a quota di sorvolo")]
    public KeyCode autopilotKey = KeyCode.T;  // toggle: T inserisce/disinserisce; vola hands-off verso la destinazione
    public float autoAccel = 140f;         // accelerazione INIZIALE (partenza gentile: hai tempo di cambiare idea se passa un corpo interessante)
    public float autoAccelMax = 1000f;     // accelerazione a regime: ci sale se resti sullo stesso bersaglio → i viaggi lunghi (es. fino al sole) prendono velocità in fretta
    public float autoAccelGentle = 3f;     // secondi di partenza gentile prima che la rampa di accelerazione cominci a salire
    public float autoAccelRampTime = 7f;   // secondi per passare (dopo la fase gentile) da autoAccel a autoAccelMax tenendo lo stesso target
    public float autoBrakeAccel = 350f;    // decelerazione per FRENARE / annullare la deriva; detta la distanza di frenata e quindi la velocità di picco sulla tratta (più alta = viaggi più veloci)
    public float autoMaxSpeed = 50000f;    // soffitto di sicurezza alla velocità dell'autopilota: ALTO, di norma non lo tocca (il vero limite è il "frena in tempo"); protegge se il sistema diventa enorme
    public float autoTurnTau = 0.7f;       // costante di tempo dell'allineamento del muso (più alto = più dolce/lento, ease-out)
    public float autoHoverRadii = 1f;      // quota d'arrivo (minima) = questo × raggio del corpo SOPRA la superficie
    public float autoHoverG = 6f;          // ...ma mai più dentro di dove la gravità LOCALE scende a questo (m/s²): su corpi pesanti ti fermi più in alto, dove g è dolce → tempo per manovrare
    public float autoArriveSync = 1f;      // |velocità relativa| sotto cui, arrivati a quota, sei "arrivato"

    [System.NonSerialized] public bool HasJetpack;
    [System.NonSerialized] public bool ControlsActive = true;   // false = comandi congelati (es. modalità mappa)
    [System.NonSerialized] public float EquipTime = -999f;
    [System.NonSerialized] public float Altitude;   // metri sopra la superficie del corpo di gravità
    [System.NonSerialized] public CelestialBody GravityBody;   // corpo la cui gravità domina (il più vicino): riferimento dell'altitudine

    public FlightModel Model { get; private set; } = FlightModel.Cruise;
    public bool IsNewtonian => Model == FlightModel.Newtonian;
    public float Speed => rb != null ? rb.linearVelocity.magnitude : 0f;
    public Vector3 Velocity => rb != null ? rb.linearVelocity : Vector3.zero;   // velocità relativa al corpo ancorato
    public float Boost01 => boost01;   // 0 = manovra, 1 = crociera piena (per l'HUD)
    public float ThrustSpool01 => thrustSpool01;   // regime motori newtoniani 0..1 (per l'HUD)
    public bool Braking { get; private set; }         // freno di assetto attivo (per l'HUD)
    public bool Autopilot { get; private set; }        // autopilota inserito (toggle T): vola da solo verso la destinazione
    public bool AutoHolding { get; private set; }       // arrivato: tiene la stazione (hover) finché non dai un comando
    public bool SoftStopping { get; private set; }      // hai interrotto l'autopilota: auto-freno fino a v≈0, poi molla

    Rigidbody rb;
    float pitch;
    float yawDelta;   // yaw del mouse accumulato in Update, applicato in FixedUpdate
    float rollDelta;  // rollio Q/E accumulato in Update, applicato in FixedUpdate (solo volo libero)
    float boost01;    // rampa di potenza della crociera, 0..1
    float thrustSpool01;   // regime dei motori newtoniani, 0..1: prendono gradualmente
    float brakeSpool01;    // rampa di potenza del freno X, 0..1: parte dolce → sale rapidissimo (anti-tap)
    float autoTransitTime;          // secondi di volo autopilota sullo STESSO bersaglio: alza l'accelerazione sui viaggi lunghi
    CelestialBody autoLastTarget;   // bersaglio dell'autopilota al frame scorso: se cambia, azzera la rampa di accelerazione
    bool autoAligned;               // l'autopilota ha completato l'allineamento iniziale del muso → camera LIBERA (la rotta non dipende dalla vista)

    public void EquipJetpack()
    {
        HasJetpack = true;
        EquipTime = Time.time;
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.freezeRotation = true;
        // NIENTE interpolazione: con la fisica a 60Hz e il rendering a 30 il moto resta fluido (la fisica
        // gira a doppio), e i TELETRASPORTI del ri-ancoraggio dell'origine restano sempre puliti — la camera
        // (figlia) non resta mai un frame indietro sul salto (era il "frame nero"/snap allontanandosi).
        rb.interpolation = RigidbodyInterpolation.None;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        // rete di sicurezza: limita l'impulso di espulsione da una penetrazione,
        // così un contatto profondo non catapulta mai il giocatore nello spazio.
        rb.maxDepenetrationVelocity = 10f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (!ControlsActive) return;   // congelato (es. modalità mappa): niente sguardo né tasti

        // T (toggle) inserisce/disinserisce l'autopilota: vola hands-off verso il corpo SELEZIONATO. Si inserisce
        // solo con la tuta E con una destinazione scelta sulla mappa (niente da agganciare, altrimenti). Quando si
        // inserisce passa a Newtoniano, così alla disinserzione resti in volo libero (no scatto di assetto).
        if (HasJetpack && Input.GetKeyDown(autopilotKey))
        {
            var dest = SolarSystem.Instance != null ? SolarSystem.Instance.Destination : null;
            bool wasAuto = Autopilot;
            Autopilot = !Autopilot && dest != null;
            AutoHolding = false;
            if (Autopilot) { Model = FlightModel.Newtonian; autoTransitTime = 0f; autoLastTarget = null; autoAligned = false; SoftStopping = false; }
            else if (wasAuto) SoftStopping = GameSettings.AutopilotSoftStop;   // interrotto a mano → frena fino a v≈0 (se l'opzione è ON)
        }

        // ARRIVATO → l'autopilota TIENE LA STAZIONE (hover) finché non dai un comando: a quel punto molla e
        // riprendi il controllo. Così non ti scarica mai in caduta libera. Vale solo da fermo in stazione
        // (durante il viaggio i comandi restano congelati: hands-off). Qualunque spinta/freno/movimento libera.
        if (AutoHolding && (Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.01f || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.01f
            || Input.GetButton("Jump") || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(brakeKey)))
        {
            Autopilot = false;
            AutoHolding = false;
        }

        // Lo stop dolce (dopo aver interrotto l'autopilota) si annulla appena prendi il controllo: qualunque
        // spinta manuale → smetti di frenare e voli tu. X NON annulla (sta già frenando, lascialo continuare).
        if (SoftStopping && (Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.01f || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.01f
            || Input.GetButton("Jump") || Input.GetKey(KeyCode.LeftShift)))
            SoftStopping = false;

        // sguardo: yaw sul corpo, pitch sulla camera. CONGELATO solo durante l'allineamento iniziale
        // dell'autopilota (il computer punta il muso al target); appena allineato la camera torna LIBERA —
        // guardarti intorno non tocca la rotta (l'autopilota spinge verso il target, non lungo la vista).
        if (!Autopilot || autoAligned)
        {
            float mx = Input.GetAxis("Mouse X") * mouseSensitivity;
            float my = Input.GetAxis("Mouse Y") * mouseSensitivity;
            yawDelta += mx;   // accumulato: applicato in FixedUpdate (interpolato) -> niente stutter
            pitch = Mathf.Clamp(pitch - my, -85f, 85f);
            if (cameraPivot) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        if (Input.GetKeyDown(KeyCode.Escape)) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        if (Input.GetMouseButtonDown(0)) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }

        // N commuta il modello di volo (solo con la tuta: senza non si vola). Disinserisce l'autopilota:
        // se metti mano al modello vuoi il controllo.
        if (HasJetpack && Input.GetKeyDown(KeyCode.N))
        {
            Model = Model == FlightModel.Cruise ? FlightModel.Newtonian : FlightModel.Cruise;
            Autopilot = false;
            AutoHolding = false;
            SoftStopping = false;
        }

        // rollio in volo libero (Q/E): accumulato qui, applicato e azzerato in FixedUpdate.
        float roll = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
        rollDelta += roll * rollSpeed * Time.deltaTime;
    }

    void FixedUpdate()
    {
        var planet = Nearest();
        if (planet == null) return;
        GravityBody = planet;

        Vector3 center = planet.transform.position;
        Vector3 toCenter = center - rb.position;
        float r = toCenter.magnitude;
        // direzione "su" sempre valida: anche se il giocatore finisse al centro,
        // restiamo in grado di rimetterlo in superficie (nessuna trappola).
        Vector3 up = r > 0.001f ? -toCenter / r : transform.up;

        // altezza del suolo nella direzione attuale: segue le colline del terreno. Si campiona SOLO vicino
        // alla superficie: alto in volo i bump (decine di m) sono irrilevanti a km di quota, e la pipeline
        // crateri (hash 3D + fBm) girerebbe a 60Hz per niente. Sopra soglia → raggio nominale.
        float surface = (float)planet.Radius;
        if (r < planet.Radius + 600.0 && planet.TryGetComponent<PlanetTerrain>(out var terr))
        {
            float s = terr.SampleHeight(up);
            if (!float.IsNaN(s) && !float.IsInfinity(s)) surface = s;
        }
        float restHeight = surface + 1f;   // distanza centro-capsula a riposo
        Altitude = r - surface;             // quota sopra il suolo (per la torcia che scala in volo)

        // gravità verso il centro, limitata al valore di superficie: r non scende
        // mai sotto il raggio nel calcolo, quindi niente picco 1/r^2 vicino al centro.
        float rEff = Mathf.Max(r, (float)planet.Radius);
        float g = (float)(planet.Mu / ((double)rEff * rEff));   // magnitudo del corpo DOMINANTE (riferimento per freno/autopilota)
        // GRAVITÀ = somma vettoriale dei corpi (1/r²), non solo il più vicino. In un BINARIO ravvicinato (i gemelli)
        // questo toglie il SALTO della direzione "giù" a metà strada: la somma è continua e dà il punto di Lagrange
        // naturale fra i due. I corpi lontani pesano ~0 (1/r²) → altrove non cambia nulla. Il 'planet' più vicino
        // resta il riferimento di orientamento/superficie/altitudine; solo la FORZA applicata è la somma.
        Vector3 gravAccel = -up * g;
        var sysG = SolarSystem.Instance;
        if (sysG != null)
            for (int i = 0; i < sysG.Bodies.Count; i++)
            {
                var b = sysG.Bodies[i];
                if (b == null || b == planet || b.Massless) continue;
                Vector3 toB = b.transform.position - rb.position;
                float rB = toB.magnitude;
                if (rB < 1e-3f) continue;
                float rEffB = Mathf.Max(rB, (float)b.Radius);
                gravAccel += (toB / rB) * (float)(b.Mu / ((double)rEffB * rEffB));
            }
        rb.AddForce(gravAccel, ForceMode.Acceleration);

        // ORIENTAMENTO — due regimi.
        // VOLO LIBERO (Newtoniano, staccato dal suolo): NIENTE aggancio alla gravità. L'orientamento
        // non si riallinea più al pianeta: era PROPRIO quel riallineamento a ruotarti la vista mentre il
        // pianeta orbita (la direzione "via dal centro" cambia di continuo), facendo "scivolare" il
        // bersaglio fuori schermo anche da fermo. Qui ruoti solo col mouse: yaw attorno al TUO su, che
        // resta fisso → la mira resta dove la metti.
        // A TERRA / CROCIERA: i piedi restano agganciati alla gravità (su = via dal centro), come serve
        // per camminare e per gli assi tangenti della crociera.
        bool airborne = r > restHeight + 0.05f;
        bool freeFlight = HasJetpack && Model == FlightModel.Newtonian && airborne;

        // AUTOPILOTA attivo solo in volo e con una destinazione selezionata. A terra non c'è nulla da agganciare:
        // se atterri (o perdi la destinazione) si disinserisce da solo.
        var dest = SolarSystem.Instance != null ? SolarSystem.Instance.Destination : null;
        if (Autopilot && (!airborne || dest == null)) { Autopilot = false; AutoHolding = false; }
        bool autoActive = Autopilot && HasJetpack && airborne && dest != null;

        Quaternion look;
        if (autoActive && !autoAligned)
        {
            // ALLINEAMENTO INIZIALE: orienta il muso verso il bersaglio con ease-out esponenziale (slerp di una
            // frazione per frame): rallenta avvicinandosi all'assetto → niente scatto, feel da astronave. Raddrizza
            // anche la camera (pitch → 0) → il corpo finisce dolcemente al centro. Appena allineato (muso ~sul
            // target) marca autoAligned: da lì la camera torna LIBERA al giocatore e l'orientamento non è più
            // guidato. La ROTTA dell'autopilota non dipende dalla camera (spinge lungo la direzione-mondo verso il
            // target), quindi guardarti intorno non la cambia. Spegni/riaccendi o cambi meta → si ri-allinea.
            float kTurn = 1f - Mathf.Exp(-Time.fixedDeltaTime / Mathf.Max(autoTurnTau, 0.01f));
            Vector3 toDest = dest.transform.position - rb.position;
            if (toDest.sqrMagnitude > 1e-4f)
            {
                Quaternion want = Quaternion.LookRotation(toDest.normalized, up);
                look = Quaternion.Slerp(transform.rotation, want, kTurn);
                Vector3 viewFwd = (look * Quaternion.Euler(pitch, 0f, 0f)) * Vector3.forward;
                if (Vector3.Angle(viewFwd, toDest) < 3f) autoAligned = true;   // muso sul target → camera libera
            }
            else look = transform.rotation;
            pitch = Mathf.Lerp(pitch, 0f, kTurn);
            if (cameraPivot) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
        else if (freeFlight)
        {
            look = Quaternion.AngleAxis(yawDelta, transform.up) * transform.rotation;
            // rollio attorno all'asse di SGUARDO (forward con il pitch): inclina anche il "su" del
            // giocatore, così i successivi yaw/pitch ruotano con te → feel da astronave (6DOF parziale).
            if (Mathf.Abs(rollDelta) > 0.0001f)
            {
                Vector3 viewFwd = (look * Quaternion.Euler(pitch, 0f, 0f)) * Vector3.forward;
                look = Quaternion.AngleAxis(rollDelta, viewFwd) * look;
            }
        }
        else
        {
            Quaternion aligned = Quaternion.FromToRotation(transform.up, up) * transform.rotation;
            look = Quaternion.AngleAxis(yawDelta, up) * aligned;
        }
        yawDelta = 0f;
        rollDelta = 0f;   // consumato (a terra/crociera il rollio si scarta: l'aggancio gravità raddrizza)
        rb.MoveRotation(look);

        // assi locali sul piano del terreno, dalla rotazione appena calcolata
        Vector3 fwd = Vector3.ProjectOnPlane(look * Vector3.forward, up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(look * Vector3.right, up).normalized;

        // input di movimento (azzerati se i comandi sono congelati: la gravità e il vincolo di
        // suolo qui sotto restano attivi, quindi in mappa il giocatore resta fermo a terra).
        float h = ControlsActive ? Input.GetAxisRaw("Horizontal") : 0f;
        float v = ControlsActive ? Input.GetAxisRaw("Vertical") : 0f;
        bool thrustUp = ControlsActive && Input.GetButton("Jump");           // Space: sale
        bool thrustDown = ControlsActive && Input.GetKey(KeyCode.LeftShift); // Shift: scende

        bool grounded = r <= restHeight + 0.05f;
        bool flying = HasJetpack && (!grounded || thrustUp);
        Braking = false;
        // lo stop dolce vale solo in volo libero newtoniano: a terra, in crociera o con l'autopilota
        // ri-inserito si annulla (niente freno latente che riparte da solo al prossimo decollo).
        if (SoftStopping && (!flying || Model != FlightModel.Newtonian || autoActive)) SoftStopping = false;

        if (flying)
        {
            // ===== VOLO COL JETPACK: motori nei 3 assi =====
            Vector3 thrust = fwd * v + right * h + up * ((thrustUp ? 1f : 0f) - (thrustDown ? 1f : 0f));
            if (thrust.sqrMagnitude > 1f) thrust = thrust.normalized;   // niente diagonale più veloce
            bool thrusting = thrust.sqrMagnitude > 0.0001f;

            if (autoActive)
            {
                AutopilotControl(dest, g);
                boost01 = 0f;
                thrustSpool01 = 0f;
            }
            else if (Model == FlightModel.Newtonian)
            {
                // Newtoniano puro: nessun attrito, la spinta si somma → la velocità cresce
                // davvero (delta-v reale). Per fermarti ti giri e controspingi. La gravità
                // radiale qui sopra agisce comunque: senza spinta cadi, come nello spazio.
                //
                // Comandi RELATIVI ALLO SGUARDO (non agli assi tangenti del pianeta): nello
                // spazio aperto vuoi "puntare e andare" — guardi il pianeta e W ti ci porta.
                // Gli assi tangenti scivolerebbero con la posizione radiale, rendendo
                // impossibile tornare indietro da lontano.
                Quaternion camRot = look * Quaternion.Euler(pitch, 0f, 0f);
                Vector3 nThrust = (camRot * Vector3.forward) * v
                                + (camRot * Vector3.right) * h
                                + (camRot * Vector3.up) * ((thrustUp ? 1f : 0f) - (thrustDown ? 1f : 0f));
                if (nThrust.sqrMagnitude > 1f) nThrust = nThrust.normalized;

                // Spool dei motori: la spinta non scatta a pieno regime al primo tasto.
                // Sale verso 1 in thrustRampTime mentre tieni i comandi e ricade più in
                // fretta quando li rilasci → i motori "prendono" gradualmente, dando inerzia
                // alla manovra (e un filo di coda quando smetti di spingere).
                float spoolRate = thrusting ? thrustRampTime : thrustRampTime * 0.4f;
                thrustSpool01 = Mathf.MoveTowards(thrustSpool01, thrusting ? 1f : 0f,
                                                  Time.fixedDeltaTime / Mathf.Max(spoolRate, 0.01f));

                // Spinta SCALATA alla gravità locale: garantisce che da QUALUNQUE corpo su cui sei
                // atterrato puoi ripartire (invariante "ciò su cui atterri, lo puoi lasciare"). Su un
                // corpo leggero (pianeta, g≈9.8) resta newtonThrust; su uno pesante (la stella, g=100)
                // sale a 1.6·g → ~0.6·g di spinta netta verso l'alto, decollo sempre possibile. In
                // spazio profondo g≈0 quindi resta newtonThrust: nessun effetto dove non serve.
                float liftThrust = Mathf.Max(newtonThrust, g * 1.6f);
                rb.AddForce(nThrust * liftThrust * thrustSpool01, ForceMode.Acceleration);
                boost01 = 0f;   // azzerato: tornando a Crociera si riparte da manovra

                // MATCH VELOCITY (X): TIENI la velocità relativa al corpo ANCORATO a ZERO. L'origine è ancorata
                // al corpo sotto i piedi (a terra) o alla DESTINAZIONE in volo, quindi rb.linearVelocity È già la
                // velocità relativa a quel corpo. Tenuto premuto: annulla lo slancio E contrasta la gravità →
                // resti FERMO rispetto al corpo (hover vicino a un pianeta, sincronizzato con la destinazione in
                // viaggio). NON è "frena e cadi": per scendere/atterrare RILASCI X (la gravità ti riprende) o Shift.
                // X premuto, OPPURE stop dolce in corso (autopilota interrotto): stesso freno graduale.
                Braking = Input.GetKey(brakeKey) || SoftStopping;
                // spool del freno: sale a 1 in brakeRampTime tenendo X, ricade quasi subito al rilascio. Così
                // un tap accidentale frena/tiene pochissimo (parte dolce) ma tenuto premuto sale RAPIDISSIMO.
                // lo stop dolce sale a regime più in fretta (onset più corto) del freno X tenuto a mano.
                float rampTime = SoftStopping ? brakeRampTime * 0.4f : brakeRampTime;
                brakeSpool01 = Mathf.MoveTowards(brakeSpool01, Braking ? 1f : 0f,
                                                 Time.fixedDeltaTime / (Braking ? Mathf.Max(rampTime, 0.01f) : 0.05f));
                if (Braking)
                {
                    Vector3 vel = rb.linearVelocity;
                    float sp = vel.magnitude;
                    if (sp > 0.001f)
                    {
                        // Tre fasce: ALTA velocità → decel PROPORZIONALE alla velocità (sp/brakeTimeConstant):
                        // da migliaia di m/s frena molto più forte del picco costante. FASCIA MEDIA → picco
                        // costante (peakAccel). CODA (sotto il ginocchio) → di nuovo proporzionale ma con τ più
                        // piccolo (brakeEaseTau): gli ultimi numeri scorrono SVELTI fino allo 0. Floor minimo
                        // per chiudere l'ultimo tratto in fretta, in tempo finito. Lo stop dolce usa softStopAccel
                        // (più alto di brakeAccel) come picco → frenata più rapida della X.
                        float peakAccel = SoftStopping ? softStopAccel : brakeAccel;
                        float core = sp > brakeKnee
                            ? Mathf.Max(peakAccel, sp / Mathf.Max(brakeTimeConstant, 0.01f))
                            : sp / Mathf.Max(brakeEaseTau, 0.01f);
                        float decel = Mathf.Max(core, brakeFloor) * brakeSpool01;
                        float newSp = Mathf.Max(0f, sp - decel * Time.fixedDeltaTime);
                        rb.linearVelocity = vel * (newSp / sp);
                    }
                    // ...e contrasta la gravità (annullata in proporzione allo spool, applicata in cima a g pieno):
                    // a freno pieno la gravità netta è 0 → NON affondi più, tieni la quota. Prima la frenata aveva
                    // poca autorità vicino allo zero e la gravità vinceva → si scendeva a ~5 m/s. In spazio g≈0 → nullo.
                    rb.AddForce(up * g * brakeSpool01, ForceMode.Acceleration);
                }

                // fine dello stop dolce: quasi fermo rispetto al corpo ancorato → molla il freno automatico e
                // ridà il controllo (resti fermo, in volo libero). rb.linearVelocity è già relativa all'ancora.
                if (SoftStopping && rb.linearVelocity.magnitude < softStopEndSpeed) SoftStopping = false;
            }
            else
            {
                // Crociera: la potenza dei motori sale verso la piena crociera mentre tieni
                // la spinta IN QUOTA, e ricade quando rilasci o scendi → maneggevole vicino al
                // suolo (atterraggio come ora), veloce in alto.
                float altUnlock = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cruiseAltLow, cruiseAltHigh, Altitude));
                float target = thrusting ? altUnlock : 0f;
                float rate = (target > boost01 ? boostRampTime : boostFalloffTime);
                boost01 = Mathf.MoveTowards(boost01, target, Time.fixedDeltaTime / Mathf.Max(rate, 0.01f));
                thrustSpool01 = 0f;   // azzerato: tornando a Newtoniano i motori ripartono da fermi

                float effThrust = Mathf.Lerp(jetThrust, cruiseThrust, boost01);
                float effDamping = Mathf.Lerp(jetDamping, cruiseDamping, boost01);
                rb.AddForce(thrust * effThrust, ForceMode.Acceleration);

                // Smorzamento ANISOTROPO: frena il moto controllato (orizzontale + salita)
                // così ti fermi quando rilasci, ma NON frena la caduta — altrimenti la
                // gravità non si sente e galleggeresti giù a velocità costante invece di
                // precipitare accelerando. Conseguenza voluta: il jetpack non ti tiene su
                // da solo, per mantenere quota dai un filo di Space (come un jetpack vero).
                float damp = Mathf.Clamp01(1f - effDamping * Time.fixedDeltaTime);
                Vector3 vel = rb.linearVelocity;
                float vRad = Vector3.Dot(vel, up);
                Vector3 vTan = (vel - up * vRad) * damp;   // orizzontale: sempre smorzata
                if (vRad > 0f) vRad *= damp;               // salita smorzata; caduta libera di accelerare
                rb.linearVelocity = vTan + up * vRad;
            }

            // se tocchi il suolo, non sprofondare (rete di sicurezza valida per entrambi i modelli)
            if (r < restHeight)
            {
                rb.position = center + up * restHeight;
                float into = Vector3.Dot(rb.linearVelocity, up);
                if (into < 0f) rb.linearVelocity -= up * into;
            }
        }
        else
        {
            // ===== CAMMINATA A PIEDI =====
            Vector3 wish = fwd * v + right * h;
            if (wish.sqrMagnitude > 1f) wish = wish.normalized;

            // WALL-STOP: il vincolo radiale ferma lo SPROFONDAMENTO ma non il moto LATERALE → senza questo si
            // attraversano pareti/dirupi che salgono di fianco (proprio le feature calpestabili dei crateri).
            // Campiono la PENDENZA orizzontale del terreno (gradiente di SampleHeight sui due assi tangenti) e tolgo
            // da 'wish' la sola componente IN SALITA quando supera la soglia → non ti arrampichi sul muro, ci scivoli
            // lungo. 4 campioni extra solo camminando vicino al suolo; il walker resta analitico (NaN → niente blocco).
            if (wallStop && wish.sqrMagnitude > 1e-4f && planet.TryGetComponent<PlanetTerrain>(out var terrW))
            {
                Vector3 tA = Vector3.Cross(up, Mathf.Abs(up.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
                Vector3 tB = Vector3.Cross(up, tA);
                float e = wallProbe;
                float sPa = terrW.SampleHeight((up * surface + tA * e).normalized);
                float sMa = terrW.SampleHeight((up * surface - tA * e).normalized);
                float sPb = terrW.SampleHeight((up * surface + tB * e).normalized);
                float sMb = terrW.SampleHeight((up * surface - tB * e).normalized);
                Vector3 uphill = tA * ((sPa - sMa) / (2f * e)) + tB * ((sPb - sMb) / (2f * e));   // pendenza orizzontale
                float slope = uphill.magnitude;
                if (slope > wallSlope)
                {
                    Vector3 uh = uphill / slope;
                    float into = Vector3.Dot(wish, uh);
                    if (into > 0f) wish -= uh * into;   // scivola lungo il muro: via la sola componente che sale
                }
            }

            Vector3 vUp = Vector3.Project(rb.linearVelocity, up);

            // vincolo analitico del suolo: non si scende mai sotto la superficie
            if (r < restHeight)
            {
                rb.position = center + up * restHeight;
                if (Vector3.Dot(vUp, up) < 0f) vUp = Vector3.zero;
            }

            // senza jetpack, Space fa saltare
            if (!HasJetpack && grounded && thrustUp) vUp = up * jumpSpeed;

            float vs = Mathf.Clamp(Vector3.Dot(vUp, up), -maxFallSpeed, maxFlySpeed);
            rb.linearVelocity = wish * moveSpeed + up * vs;
        }
    }

    // ===== AUTOPILOTA =====
    // Vola hands-off verso il corpo selezionato e si ferma SINCRONIZZATO a quota di sorvolo (autoHoverRadii ×
    // raggio sopra la superficie). Logica: profilo di velocità "frena in tempo" — la velocità di avvicinamento
    // desiderata è la massima da cui posso ancora azzerare entro il punto d'arrivo (v = √(2·a·d)), capata a una
    // crociera. Pilota l'INTERO vettore velocità relativa verso quel target (componente verso il bersaglio =
    // v desiderata, componente laterale = 0), così annulla anche la deriva. Il Δv si applica a rb.linearVelocity:
    // un cambio di velocità è identico in qualunque riferimento inerziale, quindi non importa a chi è ancorata
    // l'origine. La gravità (AddForce sopra) tira ogni frame; l'autopilota la ricorregge al frame dopo (residuo
    // = g·dt, trascurabile) → tiene il sorvolo stabile anche contro la stella (cap accel ≥ 1.6·g).
    void AutopilotControl(CelestialBody target, float g)
    {
        Vector3 toDest = target.transform.position - rb.position;
        float dist = toDest.magnitude;
        if (dist < 0.001f) { Autopilot = false; AutoHolding = false; return; }
        Vector3 toT = toDest / dist;

        // RAMPA DI ACCELERAZIONE: gentile i primi autoAccelGentle secondi (tempo di cambiare idea se sfreccia
        // vicino un corpo interessante), poi sale da autoAccel a autoAccelMax in autoAccelRampTime → finché
        // resti sullo STESSO bersaglio l'autopilota "capisce" che è un viaggio lungo e spinge sempre più forte.
        // Cambiare destinazione (o disinserire) azzera la rampa: la prossima tratta riparte di nuovo gentile.
        if (target != autoLastTarget) { autoTransitTime = 0f; autoLastTarget = target; autoAligned = false; }
        autoTransitTime += Time.fixedDeltaTime;
        float accelRamp = Mathf.Clamp01((autoTransitTime - autoAccelGentle) / Mathf.Max(autoAccelRampTime, 0.01f));
        float effAccel = Mathf.Lerp(autoAccel, autoAccelMax, accelRamp);

        // PUNTO DI SORVOLO: il PIÙ ESTERNO tra una quota proporzionale al raggio (minima, per i corpi leggeri)
        // e la distanza a cui la gravità LOCALE scende a autoHoverG (√(μ/autoHoverG)). Su un corpo pesante (la
        // stella) ti fermi molto più in alto, dove g è dolce → quando l'autopilota molla hai TEMPO di manovrare
        // prima di cadere, invece di trovarti incollato a una superficie ad alta gravità.
        float standoffByRadius = (float)target.Radius * (1f + Mathf.Max(0f, autoHoverRadii));
        float standoffByGravity = autoHoverG > 0.01f ? Mathf.Sqrt((float)target.Mu / autoHoverG) : 0f;
        float standoff = Mathf.Max(standoffByRadius, standoffByGravity);
        float dtg = dist - standoff;   // distanza dal punto di sorvolo: >0 fuori (avvicìnati), <0 dentro (allontànati)

        Vector3 relVel = RelativeVelocityTo(target);
        float closing = Vector3.Dot(relVel, toT);   // + = ti avvicini

        // Velocità RADIALE desiderata BIDIREZIONALE: profilo "frena in tempo" √(2·a·|dtg|), col SEGNO di dtg.
        //  - fuori dal sorvolo (dtg>0): avvicìnati (+); dentro (dtg<0): allontànati (−) per RISALIRE → il sorvolo
        //    è un EQUILIBRIO STABILE. Il vero limite è il profilo √(2·a·d) — per costruzione la velocità massima
        //    da cui l'autopilota riesce ancora a fermarsi entro l'arrivo (sulle tratte lunghe va più veloce senza
        //    sfondare). Sopra c'è solo un SOFFITTO DI SICUREZZA alto (autoMaxSpeed), che di norma non si tocca.
        // La decel del PROFILO è CONSERVATIVA: freno MENO la gravità di superficie (il caso peggiore lungo la
        // discesa). Tuffandoti verso un corpo pesante la gravità erode la frenata reale (decel netta = freno − g):
        // se il profilo usasse il freno pieno freneresti troppo tardi e SFONDERESTI sulla superficie (era il bug
        // sul sole). Con aProfile = freno − g_superficie la frenata è sempre realizzabile (la decel reale ≥ aProfile).
        float gSurf = (float)target.SurfaceGravity;
        float aProfile = Mathf.Max(autoBrakeAccel - gSurf, autoBrakeAccel * 0.3f);
        float mag = Mathf.Min(autoMaxSpeed, Mathf.Sqrt(2f * aProfile * Mathf.Abs(dtg)));
        float vWant = (dtg >= 0f ? 1f : -1f) * mag;
        Vector3 desiredRel = toT * vWant;   // SOLO radiale verso/dal bersaglio; componente laterale desiderata = 0

        // quanto possiamo cambiare la velocità in questo frame: morbido per PRENDERE velocità, forte per
        // FRENARE/raddrizzare. Autorità ≥ 1.6·g in ENTRAMBE le fasi → al netto della gravità resta ≥ aProfile,
        // quindi l'autopilota può davvero seguire il profilo e risalire/tenere il sorvolo anche vicino alla stella.
        float aAuthority = Mathf.Max(autoBrakeAccel, g * 1.6f);
        float accelCap = (closing < vWant ? Mathf.Max(effAccel, g * 1.6f) : aAuthority);
        Vector3 newRel = Vector3.MoveTowards(relVel, desiredRel, accelCap * Time.fixedDeltaTime);
        rb.linearVelocity += newRel - relVel;   // Δv: identico in ogni riferimento inerziale

        // ARRIVATO: a quota di sorvolo e quasi fermo. Cosa succede dipende dall'impostazione:
        //  - station-keeping ON  → entra in STAZIONE (latch): tiene l'hover finché non dai un comando (Update).
        //  - station-keeping OFF (default) → DISINSERISCE: ti molla a distanza di sicurezza, manovri tu.
        // Isteresi: tornando nettamente in transito (nuova meta dalla mappa) esci dalla stazione → resta hands-off.
        float arriveBand = Mathf.Max(standoff * 0.08f, 5f);
        bool atStation = Mathf.Abs(dtg) < arriveBand && relVel.magnitude < autoArriveSync * 2f;
        if (atStation)
        {
            if (GameSettings.AutopilotStationKeeping) AutoHolding = true;
            else { Autopilot = false; AutoHolding = false; Braking = false; return; }
        }
        else if (Mathf.Abs(dtg) > arriveBand * 3f) AutoHolding = false;
        Braking = !AutoHolding && closing > vWant + 1f;   // per l'HUD: in avvicinamento sta decelerando
    }

    // Velocità del giocatore RELATIVA a un corpo (stessa contabilità del RouteIndicator): rb.linearVelocity è
    // relativa al corpo ANCORATO; al bersaglio si toglie la velocità-scena del bersaglio = (target − ancora) in
    // velocità-universo × TimeScale. Se il bersaglio È l'ancora (in viaggio) resta la velocità del giocatore.
    Vector3 RelativeVelocityTo(CelestialBody target)
    {
        Vector3 pv = rb.linearVelocity;
        var s = SolarSystem.Instance;
        var refb = s != null ? s.Reference : null;
        if (refb == null || target == null) return pv;
        Vector3 tvs = (target.UniverseVelocityAt(s.SimTime) - refb.UniverseVelocityAt(s.SimTime)).ToVector3() * (float)s.TimeScale;
        return pv - tvs;
    }

    CelestialBody Nearest()
    {
        var s = SolarSystem.Instance;
        if (s == null) return null;
        CelestialBody best = null;
        float bd = float.MaxValue;
        for (int i = 0; i < s.Bodies.Count; i++)
        {
            var b = s.Bodies[i];
            if (b == null || b.Massless) continue;   // il baricentro non dà gravità né riferimento d'altitudine
            float d = (b.transform.position - rb.position).sqrMagnitude;
            if (d < bd) { bd = d; best = b; }
        }
        return best;
    }
}

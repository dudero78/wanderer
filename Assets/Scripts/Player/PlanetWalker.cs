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
    public float newtonThrust = 30f;      // accelerazione a pieno regime, nessun limite di velocità
    public float thrustRampTime = 1.2f;   // secondi perché i motori salgano a piena spinta (inerzia)
    public float brakeAccel = 25f;        // freno di assetto: decelerazione verso velocità-pianeta zero
    public KeyCode brakeKey = KeyCode.X;  // tienilo premuto per annullare l'orbita e poter atterrare

    [System.NonSerialized] public bool HasJetpack;
    [System.NonSerialized] public bool ControlsActive = true;   // false = comandi congelati (es. modalità mappa)
    [System.NonSerialized] public float EquipTime = -999f;
    [System.NonSerialized] public float Altitude;   // metri sopra la superficie (per la torcia)

    public FlightModel Model { get; private set; } = FlightModel.Cruise;
    public bool IsNewtonian => Model == FlightModel.Newtonian;
    public float Speed => rb != null ? rb.linearVelocity.magnitude : 0f;
    public float Boost01 => boost01;   // 0 = manovra, 1 = crociera piena (per l'HUD)
    public float ThrustSpool01 => thrustSpool01;   // regime motori newtoniani 0..1 (per l'HUD)
    public float RadialSpeed { get; private set; }   // >0 ti allontani dal pianeta, <0 ti avvicini
    public bool Braking { get; private set; }         // freno di assetto attivo (per l'HUD)

    Rigidbody rb;
    float pitch;
    float yawDelta;   // yaw del mouse accumulato in Update, applicato in FixedUpdate
    float boost01;    // rampa di potenza della crociera, 0..1
    float thrustSpool01;   // regime dei motori newtoniani, 0..1: prendono gradualmente

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
        rb.interpolation = RigidbodyInterpolation.Interpolate;
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

        // sguardo: yaw sul corpo, pitch sulla camera
        float mx = Input.GetAxis("Mouse X") * mouseSensitivity;
        float my = Input.GetAxis("Mouse Y") * mouseSensitivity;
        yawDelta += mx;   // accumulato: applicato in FixedUpdate (interpolato) -> niente stutter
        pitch = Mathf.Clamp(pitch - my, -85f, 85f);
        if (cameraPivot) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        if (Input.GetKeyDown(KeyCode.Escape)) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        if (Input.GetMouseButtonDown(0)) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }

        // N commuta il modello di volo (solo con la tuta: senza non si vola)
        if (HasJetpack && Input.GetKeyDown(KeyCode.N))
            Model = Model == FlightModel.Cruise ? FlightModel.Newtonian : FlightModel.Cruise;
    }

    void FixedUpdate()
    {
        var planet = Nearest();
        if (planet == null) return;

        Vector3 center = planet.transform.position;
        Vector3 toCenter = center - rb.position;
        float r = toCenter.magnitude;
        // direzione "su" sempre valida: anche se il giocatore finisse al centro,
        // restiamo in grado di rimetterlo in superficie (nessuna trappola).
        Vector3 up = r > 0.001f ? -toCenter / r : transform.up;

        // altezza del suolo nella direzione attuale: segue le colline del terreno
        float surface = (float)planet.Radius;
        if (planet.TryGetComponent<PlanetTerrain>(out var terr))
        {
            float s = terr.SampleHeight(up);
            if (!float.IsNaN(s) && !float.IsInfinity(s)) surface = s;
        }
        float restHeight = surface + 1f;   // distanza centro-capsula a riposo
        Altitude = r - surface;             // quota sopra il suolo (per la torcia che scala in volo)
        RadialSpeed = Vector3.Dot(rb.linearVelocity, up);   // segno dell'avvicinamento, per l'HUD

        // gravità verso il centro, limitata al valore di superficie: r non scende
        // mai sotto il raggio nel calcolo, quindi niente picco 1/r^2 vicino al centro.
        float rEff = Mathf.Max(r, (float)planet.Radius);
        float g = (float)(planet.Mu / ((double)rEff * rEff));
        rb.AddForce(-up * g, ForceMode.Acceleration);

        // allineamento dei piedi (up radiale) + yaw del mouse, tutto in un'unica MoveRotation:
        // così la rotazione è interpolata dalla fisica e non c'è stutter orizzontale.
        Quaternion aligned = Quaternion.FromToRotation(transform.up, up) * transform.rotation;
        Quaternion look = Quaternion.AngleAxis(yawDelta, up) * aligned;
        yawDelta = 0f;
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

        if (flying)
        {
            // ===== VOLO COL JETPACK: motori nei 3 assi =====
            Vector3 thrust = fwd * v + right * h + up * ((thrustUp ? 1f : 0f) - (thrustDown ? 1f : 0f));
            if (thrust.sqrMagnitude > 1f) thrust = thrust.normalized;   // niente diagonale più veloce
            bool thrusting = thrust.sqrMagnitude > 0.0001f;

            if (Model == FlightModel.Newtonian)
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

                rb.AddForce(nThrust * newtonThrust * thrustSpool01, ForceMode.Acceleration);
                boost01 = 0f;   // azzerato: tornando a Crociera si riparte da manovra

                // Freno di assetto (MATCH VELOCITY): porta a zero la velocità rispetto al corpo
                // ANCORATO. L'origine è ancorata al corpo sotto i piedi (a terra) o alla DESTINAZIONE
                // selezionata (in volo), quindi rb.linearVelocity È già la velocità relativa a quel
                // corpo. Tienilo premuto per "sincronizzarti" con la destinazione (resta centrata) o
                // per uscire dall'orbita di un pianeta (annulli la tangenziale e la gravità ti fa scendere).
                Braking = Input.GetKey(brakeKey);
                if (Braking)
                {
                    Vector3 vel = rb.linearVelocity;
                    float sp = vel.magnitude;
                    if (sp > 0.001f)
                    {
                        float newSp = Mathf.Max(0f, sp - brakeAccel * Time.fixedDeltaTime);
                        rb.linearVelocity = vel * (newSp / sp);
                    }
                }
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

    CelestialBody Nearest()
    {
        var s = SolarSystem.Instance;
        if (s == null) return null;
        CelestialBody best = null;
        float bd = float.MaxValue;
        for (int i = 0; i < s.Bodies.Count; i++)
        {
            var b = s.Bodies[i];
            if (b == null) continue;
            float d = (b.transform.position - rb.position).sqrMagnitude;
            if (d < bd) { bd = d; best = b; }
        }
        return best;
    }
}

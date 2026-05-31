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
    public float jetThrust = 30f;       // spinta dei motori (uguale su tutti gli assi)
    public float jetDamping = 1.2f;     // smorzamento in volo: lo rende controllabile
    public float maxFlySpeed = 30f;     // limite verticale a piedi (archi di salto)
    public float maxFallSpeed = 55f;    // velocità terminale di caduta a piedi

    [System.NonSerialized] public bool HasJetpack;
    [System.NonSerialized] public float EquipTime = -999f;
    [System.NonSerialized] public float Altitude;   // metri sopra la superficie (per la torcia)

    Rigidbody rb;
    float pitch;
    float yawDelta;   // yaw del mouse accumulato in Update, applicato in FixedUpdate

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
        // sguardo: yaw sul corpo, pitch sulla camera
        float mx = Input.GetAxis("Mouse X") * mouseSensitivity;
        float my = Input.GetAxis("Mouse Y") * mouseSensitivity;
        yawDelta += mx;   // accumulato: applicato in FixedUpdate (interpolato) -> niente stutter
        pitch = Mathf.Clamp(pitch - my, -85f, 85f);
        if (cameraPivot) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        if (Input.GetKeyDown(KeyCode.Escape)) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        if (Input.GetMouseButtonDown(0)) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
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

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        bool thrustUp = Input.GetButton("Jump");           // Space: sale
        bool thrustDown = Input.GetKey(KeyCode.LeftShift); // Shift: scende

        bool grounded = r <= restHeight + 0.05f;
        bool flying = HasJetpack && (!grounded || thrustUp);

        if (flying)
        {
            // ===== VOLO COL JETPACK: motori nei 3 assi (newtoniano + smorzamento) =====
            Vector3 thrust = fwd * v + right * h + up * ((thrustUp ? 1f : 0f) - (thrustDown ? 1f : 0f));
            if (thrust.sqrMagnitude > 1f) thrust = thrust.normalized;   // niente diagonale più veloce
            rb.AddForce(thrust * jetThrust, ForceMode.Acceleration);

            // smorzamento: rende il volo controllabile e fa fermare dolcemente al rilascio
            rb.linearVelocity *= Mathf.Clamp01(1f - jetDamping * Time.fixedDeltaTime);

            // se tocchi il suolo, non sprofondare
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

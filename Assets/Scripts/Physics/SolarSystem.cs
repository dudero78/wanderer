using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Orchestratore del sistema solare. Avanza il tempo di simulazione, aggiorna
/// le posizioni-universo di tutti i corpi, riposiziona l'origine fluttuante e
/// infine proietta tutto nello spazio di rendering — in quest'ordine preciso,
/// così non c'è mai uno scarto di un frame tra fisica e grafica.
/// </summary>
public class SolarSystem : MonoBehaviour
{
    public static SolarSystem Instance;

    public double TimeScale = 3.0;          // accelera la simulazione per vedere l'orbita
    public double SimTime = 0;
    public CelestialBody Anchor;            // ancora iniziale (prima che esista il giocatore)
    public Rigidbody PlayerBody;            // se settato: l'origine ancora al corpo PIÙ VICINO al giocatore
    public List<Transform> Loose = new List<Transform>();   // oggetti sciolti da traslare allo switch di corpo

    public CelestialBody Destination;       // corpo selezionato sulla mappa: in volo l'origine ancora a lui
    public List<CelestialBody> Bodies = new List<CelestialBody>();

    // Corpo di RIFERIMENTO: quello a cui è ancorata l'origine in questo istante. È FERMO in scena,
    // quindi velocità (rb.linearVelocity) e quota del giocatore sono relative a LUI. La HUD lo legge:
    // così i numeri parlano sempre del corpo con cui stai interagendo (sotto i piedi o in viaggio).
    public CelestialBody Reference { get; private set; }
    CelestialBody currentAnchor;
    bool traveling;                         // isteresi zona-locale/viaggio: evita il flip-flop sul bordo
    int restoreInterpFrame = -1;            // frame in cui ripristinare l'interpolazione dopo un teletrasporto

    void ShiftLoose(Vector3 s)
    {
        if (PlayerBody != null)
        {
            PlayerBody.position += s;
            // Il ri-ancoraggio è un TELETRASPORTO grande del giocatore. I corpi spostano il transform
            // subito (SyncTransform), ma con l'interpolazione attiva la camera (figlia, interpolata) resta
            // indietro di un frame → "scatto" mentre guardi la destinazione. Spegniamo l'interpolazione su
            // questo frame (transform = posizione fisica, allineato ai corpi) e la riaccendiamo al prossimo.
            PlayerBody.interpolation = RigidbodyInterpolation.None;
            Physics.SyncTransforms();
            // riaccendi qualche frame DOPO: serve che il buffer di interpolazione si rinfreschi con pose
            // POST-teletrasporto, altrimenti al ripristino interpola dal vecchio punto (origine) al nuovo
            // (decine di km) → smear a tutto schermo = "frame nero".
            restoreInterpFrame = Time.frameCount + 3;
        }
        for (int i = 0; i < Loose.Count; i++)
            if (Loose[i] != null) Loose[i].position += s;
    }

    void Awake()
    {
        Instance = this;
        FloatingOrigin.Reset();
    }

    public void Register(CelestialBody b)
    {
        if (b != null && !Bodies.Contains(b)) Bodies.Add(b);
    }

    void Update()
    {
        SimTime += Time.deltaTime * TimeScale;
        Step();
    }

    public void Step()
    {
        // riaccendi l'interpolazione il frame DOPO un teletrasporto di ri-ancoraggio (vedi ShiftLoose).
        if (restoreInterpFrame >= 0 && Time.frameCount >= restoreInterpFrame)
        {
            if (PlayerBody != null) PlayerBody.interpolation = RigidbodyInterpolation.Interpolate;
            restoreInterpFrame = -1;
        }

        // 1. aggiorna le posizioni-universo (double, esatte)
        for (int i = 0; i < Bodies.Count; i++)
        {
            var b = Bodies[i];
            if (b != null && b.Orbit != null) b.UpdatePosition(SimTime);
        }

        // 2. ancora l'origine al corpo PIÙ VICINO al giocatore: così il corpo verso cui voli è FERMO
        //    e raggiungibile, mentre quello che lasci orbita via. Quando il corpo più vicino CAMBIA,
        //    trasli giocatore + oggetti sciolti per restare nello stesso punto-universo: è un cambio di
        //    sistema di riferimento, senza salti a schermo (tutto si sposta insieme). Prima che esista
        //    il giocatore si usa Anchor (il pianeta).
        if (PlayerBody != null)
        {
            Vector3 pp = PlayerBody.position;
            var nearest = NearestBody(pp);
            float alt = nearest != null ? (nearest.transform.position - pp).magnitude - (float)nearest.Radius : 1e12f;

            // ZONA LOCALE / VIAGGIO con ISTERESI. La soglia di "decollo" cresce col raggio del corpo
            // (floor per gli asteroidi). Esci dalla zona locale (→ viaggio) sopra 'takeoff', ci rientri
            // solo sotto 'takeoff*0.6': la banda morta evita il flip-flop di riferimento sul bordo —
            // critico, perché ogni switch corregge la velocità e oscillare la farebbe sobbalzare.
            float takeoff = nearest != null ? Mathf.Max(250f, (float)nearest.Radius * 0.5f) : 0f;
            if (!traveling && alt > takeoff) traveling = true;
            else if (traveling && alt < takeoff * 0.6f) traveling = false;

            // ANCORAGGIO. In viaggio con una DESTINAZIONE → ancori a lei: è FERMA in scena (non sfugge
            // mentre orbita) e raggiungibile. Altrimenti (zona locale, o nessuna destinazione) → al più
            // vicino. Allo SWITCH di corpo (cambio di sistema di riferimento) fai due cose:
            CelestialBody target = (traveling && Destination != null) ? Destination : nearest;
            if (target != null)
            {
                if (currentAnchor != target)
                {
                    // (1) trasli giocatore+oggetti per restare nello stesso punto-universo → niente
                    //     salto a schermo. (2) correggi la velocità del giocatore della differenza di
                    //     velocità tra vecchio e nuovo corpo (× TimeScale: i corpi si muovono in scena
                    //     a quel ritmo) → la sua velocità-UNIVERSO non cambia. Conseguenza voluta:
                    //     appena decolli NON ti fermi rispetto alla destinazione, mantieni lo slancio
                    //     orbitale e lei "scorre"; è il freno X (match velocity) a sincronizzarti e a
                    //     fermarla. Cambiare ancora non altera mai il tuo moto reale, solo i numeri.
                    Vector3 shift = (FloatingOrigin.SceneOrigin - target.UniversePosition).ToVector3();
                    if (currentAnchor != null && PlayerBody != null)
                    {
                        Vector3d dv = currentAnchor.UniverseVelocityAt(SimTime) - target.UniverseVelocityAt(SimTime);
                        PlayerBody.linearVelocity += dv.ToVector3() * (float)TimeScale;
                    }
                    ShiftLoose(shift);
                    currentAnchor = target;
                }
                FloatingOrigin.SceneOrigin = target.UniversePosition;
                Reference = target;
            }
        }
        else if (Anchor != null) { FloatingOrigin.SceneOrigin = Anchor.UniversePosition; Reference = Anchor; }

        // 3. proietta tutto nello spazio float di Unity
        for (int i = 0; i < Bodies.Count; i++)
            if (Bodies[i] != null) Bodies[i].SyncTransform();
    }

    CelestialBody NearestBody(Vector3 scenePos)
    {
        CelestialBody best = null; float bd = float.MaxValue;
        for (int i = 0; i < Bodies.Count; i++)
        {
            var b = Bodies[i];
            if (b == null) continue;
            float d = (b.transform.position - scenePos).sqrMagnitude;
            if (d < bd) { bd = d; best = b; }
        }
        return best;
    }
}

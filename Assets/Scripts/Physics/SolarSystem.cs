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

    public double TimeScale = 1.0;          // 1 = valore di GIOCO (unica fonte). Solo un debug fast-forward esplicito lo alza: con 3 le velocità orbitali triplicano e il match-velocity è ingiocabile
    public double SimTime = 0;
    public CelestialBody Anchor;            // ancora iniziale (prima che esista il giocatore)
    public Rigidbody PlayerBody;            // se settato: l'origine ancora al corpo PIÙ VICINO al giocatore
    public List<Transform> Loose = new List<Transform>();   // oggetti sciolti da traslare allo switch di corpo

    public CelestialBody Destination;       // corpo selezionato sulla mappa: in volo l'origine ancora a lui
    public List<CelestialBody> Bodies = new List<CelestialBody>();

    // Tappa 1 multi-sistema (#16, vedi STARSYSTEM_DESIGN.md): i sistemi stellari + quello ATTIVO. A N=1
    // Active.Bodies è la STESSA istanza di Bodies (riferimento condiviso) → comportamento identico a prima.
    // Reference/Anchor/Destination/currentAnchor restano QUI: sono stato del GIOCATORE (l'ancora vive FRA i sistemi),
    // spostarli nel contenitore romperebbe il futuro viaggio interstellare.
    public List<StarSystem> Systems = new List<StarSystem>();
    public StarSystem Active;

    // Corpo di RIFERIMENTO: quello a cui è ancorata l'origine in questo istante. È FERMO in scena,
    // quindi velocità (rb.linearVelocity) e quota del giocatore sono relative a LUI. La HUD lo legge:
    // così i numeri parlano sempre del corpo con cui stai interagendo (sotto i piedi o in viaggio).
    public CelestialBody Reference { get; private set; }
    CelestialBody currentAnchor;
    CelestialBody lastNearest;              // isteresi sul corpo più vicino (vedi NearestBody)
    bool traveling;                         // isteresi zona-locale/viaggio: evita il flip-flop sul bordo

    // Ri-ancoraggio = teletrasporto del giocatore + traslazione degli oggetti sciolti. È pulito perché il
    // Rigidbody NON è interpolato (vedi PlanetWalker.Awake): transform = posizione fisica, niente lag camera.
    void ShiftLoose(Vector3 s)
    {
        if (PlayerBody != null) PlayerBody.position += s;
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
        // 1. aggiorna le posizioni-universo (double, esatte)
        for (int i = 0; i < Bodies.Count; i++)
        {
            var b = Bodies[i];
            if (b != null && b.Orbit != null) b.UpdatePosition(SimTime);
        }

        // 1b. CACHE della velocità-universo: la calcoliamo UNA volta per Step (ognuna = 2 solve Kepler + ricorsione)
        //     e i consumatori (walker, indicatore di rotta, lo switch d'ancora qui sotto) la LEGGONO invece di
        //     ricalcolarla ogni frame per ciascuno. Tutti usano SimTime → un solo valore valido per il frame.
        for (int i = 0; i < Bodies.Count; i++)
        {
            var b = Bodies[i];
            if (b != null) b.UniverseVelocity = b.UniverseVelocityAt(SimTime);
        }

        // 2. ancora l'origine al corpo PIÙ VICINO al giocatore: così il corpo verso cui voli è FERMO
        //    e raggiungibile, mentre quello che lasci orbita via. Quando il corpo più vicino CAMBIA,
        //    trasli giocatore + oggetti sciolti per restare nello stesso punto-universo: è un cambio di
        //    sistema di riferimento, senza salti a schermo (tutto si sposta insieme). Prima che esista
        //    il giocatore si usa Anchor (il pianeta).
        if (PlayerBody != null)
        {
            Vector3 pp = PlayerBody.position;
            var nearest = NearestBody(pp, lastNearest);
            lastNearest = nearest;
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
                        Vector3d dv = currentAnchor.UniverseVelocity - target.UniverseVelocity;
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

    // Corpo più vicino al giocatore, CON ISTERESI. Senza isteresi, a metà strada fra due corpi (es. Pianeta
    // e luna) il vincitore dell'argmin oscilla ogni frame; siccome l'origine della scena si ancora a lui,
    // SceneOrigin salterebbe tra due posizioni-universo → la vista sobbalza fra due inquadrature (nausea).
    // Si passa a un nuovo corpo solo se è più vicino di almeno il 10% rispetto a quello attuale (sticky).
    CelestialBody NearestBody(Vector3 scenePos, CelestialBody sticky)
    {
        CelestialBody best = null; float bd = float.MaxValue;
        for (int i = 0; i < Bodies.Count; i++)
        {
            var b = Bodies[i];
            if (b == null || b.Massless) continue;   // il baricentro non ancora l'origine
            float d = (b.transform.position - scenePos).sqrMagnitude;
            if (d < bd) { bd = d; best = b; }
        }
        if (sticky != null && best != sticky)
        {
            float ds = (sticky.transform.position - scenePos).sqrMagnitude;
            if (bd > ds * 0.81f) best = sticky;   // 0.9² → banda morta del 10% sulla distanza
        }
        return best;
    }
}

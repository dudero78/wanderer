using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Orchestratore del sistema solare. Avanza il tempo di simulazione, aggiorna
/// le posizioni-universo di tutti i corpi, riposiziona l'origine fluttuante e
/// infine proietta tutto nello spazio di rendering — in quest'ordine preciso,
/// così non c'è mai uno scarto di un frame tra fisica e grafica.
/// </summary>
[DefaultExecutionOrder(-100)]   // lo Step (tempo + posizioni + ri-ancoraggio) gira PRIMA della fisica del walker
public class SolarSystem : MonoBehaviour
{
    public static SolarSystem Instance;

    public double TimeScale = 1.0;          // 1 = valore di GIOCO (unica fonte). Solo un debug fast-forward esplicito lo alza: con 3 le velocità orbitali triplicano e il match-velocity è ingiocabile
    public double SimTime = 0;
    public CelestialBody Anchor;            // ancora iniziale (prima che esista il giocatore)
    public Rigidbody PlayerBody;            // se settato: l'origine ancora al corpo PIÙ VICINO al giocatore
    public List<Transform> Loose = new List<Transform>();   // oggetti sciolti da traslare allo switch di corpo

    public CelestialBody Destination;       // corpo selezionato sulla mappa: in volo l'origine ancora a lui
    public StarSystem DestinationSystem;    // SISTEMA stellare distante selezionato in mappa (waypoint galattico): ci voli verso, arrivando si sveglia (Tappa 4). Mutuamente esclusivo con Destination

    /// <summary>Un PIANETA di un sistema DORMIENTE scelto come bersaglio dalla mappa: non esiste ancora come corpo vivo,
    /// la sua posizione si calcola dalla ricetta (orbita relativa al SystemOrigin). L'autopilota ci vola; avvicinandoti il
    /// sistema si sveglia e il pianeta diventa un corpo vero → allora lo "promuovo" a Destination (collisione/atterraggio
    /// reali). Mutuamente esclusivo con Destination/DestinationSystem.</summary>
    public class DormantTarget
    {
        public string name;
        public StarSystem system;
        public KeplerOrbit orbit;     // relativa al SystemOrigin (null = la stella stessa)
        public double radius, gravity;
        public Vector3d UniversePos(double time) => system.SystemOrigin + (orbit != null ? orbit.GetRelativePosition(time) : Vector3d.Zero);
    }
    public DormantTarget DestinationDormant;

    /// <summary>BERSAGLIO unificato del targeting: un CORPO o un SISTEMA distante, con TUTTO ciò che serve a chi lo usa
    /// (autopilota, reticolo di rotta, HUD). Fonte UNICA → qualunque cosa selezionabile eredita automaticamente ogni
    /// funzione di targeting (rotta, distanza, velocità di avvicinamento, autopilota), senza ricablare i consumatori.</summary>
    public struct TargetInfo
    {
        public string name;
        public Vector3 scenePos;       // posizione in scena
        public float radius;
        public float mu, surfaceGravity;
        public Vector3 sceneVelocity;  // velocità del bersaglio nel frame di scena (per la velocità relativa)
        public bool interstellar;      // sistema distante (spazio vuoto)
        public CelestialBody body;     // null se sistema
        public StarSystem system;      // null se corpo
        public object Id => (object)body ?? system;   // identità per resettare la rampa dell'autopilota al cambio
    }

    /// <summary>Il bersaglio corrente (corpo selezionato, o sistema distante). false se non c'è nulla selezionato.</summary>
    public bool TryGetTarget(out TargetInfo t)
    {
        t = default;
        float ts = (float)TimeScale;
        if (Destination != null)
        {
            var b = Destination;
            Vector3 sv = Reference != null ? (b.UniverseVelocity - Reference.UniverseVelocity).ToVector3() * ts : Vector3.zero;
            t = new TargetInfo { name = b.gameObject.name, scenePos = b.transform.position, radius = (float)b.Radius, mu = (float)b.Mu, surfaceGravity = (float)b.SurfaceGravity, sceneVelocity = sv, body = b, system = b.System };
            return true;
        }
        if (DestinationDormant != null)
        {
            var dt = DestinationDormant;
            Vector3 pos = (dt.UniversePos(SimTime) - FloatingOrigin.SceneOrigin).ToVector3();
            // punto FISSO nell'universo (come il sistema): in scena si muove come −(velocità del riferimento)·TimeScale
            Vector3 sv = Reference != null ? (Vector3d.Zero - Reference.UniverseVelocity).ToVector3() * ts : Vector3.zero;
            float r = (float)dt.radius;
            t = new TargetInfo { name = dt.name, scenePos = pos, radius = r, mu = (float)(dt.gravity * dt.radius * dt.radius),
                                 surfaceGravity = (float)dt.gravity, sceneVelocity = sv, system = dt.system };
            return true;
        }
        if (DestinationSystem != null)
        {
            var sys = DestinationSystem;
            Vector3 pos = (sys.SystemOrigin - FloatingOrigin.SceneOrigin).ToVector3();
            // punto FISSO nell'universo (UniverseVelocity=0): in scena si muove come −(velocità del riferimento)·TimeScale
            Vector3 sv = Reference != null ? (Vector3d.Zero - Reference.UniverseVelocity).ToVector3() * ts : Vector3.zero;
            t = new TargetInfo { name = "★ " + sys.Name, scenePos = pos, radius = sys.StarRadius > 0f ? sys.StarRadius : 2000f, sceneVelocity = sv, interstellar = true, system = sys };
            return true;
        }
        return false;
    }

    public List<CelestialBody> Bodies = new List<CelestialBody>();

    // Tappa 1 multi-sistema (#16, vedi STARSYSTEM_DESIGN.md): i sistemi stellari + quello ATTIVO. A N=1
    // Active.Bodies è la STESSA istanza di Bodies (riferimento condiviso) → comportamento identico a prima.
    // Reference/Anchor/Destination/currentAnchor restano QUI: sono stato del GIOCATORE (l'ancora vive FRA i sistemi),
    // spostarli nel contenitore romperebbe il futuro viaggio interstellare.
    public List<StarSystem> Systems = new List<StarSystem>();
    public StarSystem Active;

    // Tappa 4 multi-sistema (interest L1): SVEGLIA/ADDORMENTA i sistemi DISTANTI per prossimità (con isteresi). I
    // callback (costruzione/distruzione corpi + retarget luce + rebuild eclissi) li imposta GameBootstrap. NULL =
    // nessun multi-sistema (identico a prima). Il sistema-casa (Recipe.Bodies==null) resta SEMPRE residente: il
    // limite di corpi vivi non c'è più (region-stamp uint), quindi casa + un sistema distante svegliato coesistono.
    // WakeSystem: avvia la costruzione del sistema (ora GRADUALE su più frame → niente freeze). Fire-and-forget: imposta
    // s.Waking, costruisce in coroutine, a fine build mette s.Active e fa luce/eclissi/promozione del bersaglio.
    public System.Action<StarSystem> WakeSystem;
    public System.Action<StarSystem> SleepSystem;

    // Corpo di RIFERIMENTO: quello a cui è ancorata l'origine in questo istante. È FERMO in scena,
    // quindi velocità (rb.linearVelocity) e quota del giocatore sono relative a LUI. La HUD lo legge:
    // così i numeri parlano sempre del corpo con cui stai interagendo (sotto i piedi o in viaggio).
    public CelestialBody Reference { get; private set; }
    CelestialBody currentAnchor;
    bool deepMode;                          // crociera interstellare: ancorati a un PUNTO fisso ri-centrato sul giocatore (floating origin vera)
    Vector3d deepAnchor;                    // il punto-origine attuale dello spazio profondo (ri-centrato oltre la soglia)
    const float RebaseThresholdSq = 50000f * 50000f;   // ri-centra se |pos-scena| supera ~50 km → coords sempre piccole (precisione)
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

    /// <summary>Cambio d'ancora (corpo o punto-sistema): trasla giocatore+oggetti per restare nello stesso punto-universo
    /// (niente salto a schermo) e — se c'era già un'ancora (had) — corregge la velocità della differenza (vecchia−nuova)
    /// × TimeScale, così la velocità-UNIVERSO si conserva. I Loose DINAMICI (sonda in volo) ricevono la stessa correzione
    /// (senza, schizzerebbero via); i kinematici (tuta, sonda posata) sono saltati.</summary>
    void SwitchAnchor(Vector3d newPos, Vector3d oldVel, Vector3d newVel, bool had)
    {
        Vector3 shift = (FloatingOrigin.SceneOrigin - newPos).ToVector3();
        if (had)
        {
            Vector3 dvScene = (oldVel - newVel).ToVector3() * (float)TimeScale;
            if (PlayerBody != null) PlayerBody.linearVelocity += dvScene;
            for (int li = 0; li < Loose.Count; li++)
            {
                if (Loose[li] == null) continue;
                var lrb = Loose[li].GetComponent<Rigidbody>();
                if (lrb != null && !lrb.isKinematic) lrb.linearVelocity += dvScene;
            }
        }
        ShiftLoose(shift);
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

    /// <summary>Toglie un corpo dalla lista (per addormentare un sistema distante, Tappa 4). Sgancia i riferimenti
    /// di stato del giocatore che lo puntano, così non resta un'ancora/destinazione su un corpo distrutto.</summary>
    public void Unregister(CelestialBody b)
    {
        if (b == null) return;
        Bodies.Remove(b);
        if (lastNearest == b) lastNearest = null;
        if (currentAnchor == b) currentAnchor = null;
        if (Destination == b) Destination = null;
    }

    // #8 DETERMINISMO: tempo + posizioni + ri-ancoraggio nel TICK FISSO, sullo stesso ritmo della fisica del walker
    // (input in Update, fisica in FixedUpdate). SimTime avanza di un passo COSTANTE (fixedDeltaTime) → deterministico,
    // niente straddle dello shift d'ancora in un frame lento. DefaultExecutionOrder(-100) garantisce che giri PRIMA
    // del walker, che legge le posizioni-corpo aggiornate. NB: i corpi non-ancora si muovono ora a ritmo fisso (50 Hz):
    // sono lontani/piccoli, l'eventuale micro-stutter è trascurabile; se desse fastidio si aggiunge l'interpolazione.
    long tickCount;   // #PHYS-2: contatore intero di tick fissi → SimTime DETERMINISTICO (niente deriva da accumulo float)

    void FixedUpdate()
    {
        // SimTime da TICK INTERO (non accumulatore di deltaTime): due run identici danno lo STESSO SimTime al tick N
        // → orbite (analitiche, funzione di SimTime) riproducibili bit-per-bit. Sblocca replay/lockstep netcode.
        // Il passo è costante (Time.fixedDeltaTime fissato a 1/60 in GameBootstrap), quindi è anche privo di deriva.
        tickCount++;
        SimTime = tickCount * (double)Time.fixedDeltaTime * TimeScale;
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
            // mentre orbita) e raggiungibile. Altrimenti (zona locale, o nessuna destinazione) → al più vicino.
            CelestialBody bodyTarget = (traveling && Destination != null) ? Destination : nearest;

            // CROCIERA INTERSTELLARE (verso un SISTEMA distante): in mezzo allo spazio NON c'è un corpo vicino a cui
            // agganciarsi, e ancorare a casa o alla destinazione fa esplodere la pos-scena a metà strada (a milioni di
            // metri il float TREMA → jitter, errori di proiezione). FLOATING ORIGIN VERA: ancora a un PUNTO fisso che
            // RI-CENTRO sul giocatore appena si allontana oltre una soglia → la pos-scena resta sempre piccola (≤~50 km).
            bool wantDeep = traveling && (DestinationSystem != null || DestinationDormant != null);
            if (wantDeep)
            {
                if (!deepMode)
                {
                    Vector3d playerU = FloatingOrigin.SceneOrigin + new Vector3d(pp);
                    SwitchAnchor(playerU, currentAnchor != null ? currentAnchor.UniverseVelocity : Vector3d.Zero, Vector3d.Zero, currentAnchor != null);
                    deepAnchor = playerU; deepMode = true; currentAnchor = null;
                }
                else if (pp.sqrMagnitude > RebaseThresholdSq)   // troppo lontano dall'origine → RI-CENTRA sul giocatore
                {
                    Vector3d newDeep = FloatingOrigin.SceneOrigin + new Vector3d(pp);
                    SwitchAnchor(newDeep, Vector3d.Zero, Vector3d.Zero, false);   // punto→punto (vel 0): solo traslazione, niente correzione
                    deepAnchor = newDeep;
                }
                FloatingOrigin.SceneOrigin = deepAnchor;
                Reference = null;   // spazio profondo: niente corpo di riferimento (HUD usa il reticolo del bersaglio per la velocità)
            }
            else if (bodyTarget != null)
            {
                // Allo SWITCH di ancora correggi la velocità della differenza vecchia−nuova (× TimeScale) → la velocità
                // UNIVERSO non cambia. Uscendo dallo spazio profondo l'ancora vecchia è un punto fermo (vel 0). I Loose
                // dinamici (sonda) ricevono la stessa correzione.
                if (deepMode || currentAnchor != bodyTarget)
                    SwitchAnchor(bodyTarget.UniversePosition, deepMode || currentAnchor == null ? Vector3d.Zero : currentAnchor.UniverseVelocity,
                                 bodyTarget.UniverseVelocity, deepMode || currentAnchor != null);
                deepMode = false; currentAnchor = bodyTarget;
                FloatingOrigin.SceneOrigin = bodyTarget.UniversePosition;
                Reference = bodyTarget;
            }
        }
        else if (Anchor != null) { FloatingOrigin.SceneOrigin = Anchor.UniversePosition; Reference = Anchor; }

        // 2b. INTEREST INTERSTELLARE (Tappa 4): sveglia/addormenta i sistemi distanti per prossimità (dopo
        //     l'ancoraggio, così la pos-universo del giocatore è quella del frame). No-op a multi-sistema spento.
        UpdateInterstellar();

        // 3. proietta tutto nello spazio float di Unity
        for (int i = 0; i < Bodies.Count; i++)
            if (Bodies[i] != null) Bodies[i].SyncTransform();
    }

    /// <summary>Sveglia un sistema DISTANTE quando il giocatore entra nel suo raggio (isteresi ×1.4 per dormire) →
    /// transizione interstellare costruita sui meccanismi già testati (ri-streaming dei corpi, switch d'ancora). Il
    /// sistema-casa (Recipe.Bodies==null) non viene mai toccato (resta residente). Niente flip-flop: banda morta come
    /// il takeoff. La pos-universo del giocatore = SceneOrigin (double) + posizione di scena.</summary>
    void UpdateInterstellar()
    {
        if (PlayerBody == null || WakeSystem == null || Systems.Count <= 1) return;
        Vector3d playerU = FloatingOrigin.SceneOrigin + new Vector3d(PlayerBody.position);
        // sistema di DESTINAZIONE — include il sistema del CORPO selezionato vivo (Destination.System): dopo che un
        // pianeta dormiente è stato promosso a corpo vero, la destinazione è Destination, non più DestinationDormant →
        // senza questo Vega non risultava più "destinazione" e veniva ADDORMENTATA mentre ancora ci viaggiavi (→ il
        // corpo distrutto azzerava Destination = target perso, e il sistema spariva). Lo si costruisce PRESTO (in
        // viaggio) e resta RESIDENTE finché è la destinazione.
        StarSystem destSys = DestinationDormant != null ? DestinationDormant.system
                           : DestinationSystem != null ? DestinationSystem
                           : Destination != null ? Destination.System : null;
        for (int i = 0; i < Systems.Count; i++)
        {
            var s = Systems[i];
            if (s == null || s.Recipe == null || s.Recipe.Bodies == null) continue;   // casa (Bodies==null): sempre residente
            double d = Vector3d.Distance(s.SystemOrigin, playerU);
            bool isDest = s == destSys;
            // sveglia GRADUALE (coroutine): vicino, OPPURE è la destinazione e sei già in viaggio. Waking evita di
            // ri-triggerare ogni frame; la promozione del bersaglio dormiente la fa il builder a fine costruzione.
            if (!s.Active && !s.Waking && (d < s.WakeRadius || (isDest && traveling))) { s.Waking = true; WakeSystem(s); }
            // dormi quando lontano — MA non la destinazione (resta residente mentre ci viaggi) e mai a metà risveglio.
            else if (s.Active && !s.Waking && !isDest && d > s.WakeRadius * 1.4) SleepSystem?.Invoke(s);
        }
    }

    /// <summary>Quando il sistema del bersaglio dormiente si SVEGLIA, sostituisci il bersaglio (punto da ricetta) col
    /// CORPO VERO appena creato (stesso nome) → collisione/atterraggio reali e l'autopilota continua su di lui.
    /// Pubblico: lo chiama il builder asincrono a fine costruzione.</summary>
    public void PromoteDormantTarget(StarSystem s)
    {
        if (DestinationDormant == null || DestinationDormant.system != s) return;
        for (int i = 0; i < Bodies.Count; i++)
            if (Bodies[i] != null && Bodies[i].gameObject.name == DestinationDormant.name)
            {
                Destination = Bodies[i]; DestinationDormant = null; return;
            }
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

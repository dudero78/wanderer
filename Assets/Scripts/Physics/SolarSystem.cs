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
    CelestialBody currentAnchor;
    const float SwitchAltitude = 5000f;     // sotto: ancora al corpo sotto i piedi; sopra: sei "in volo"

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
            bool grounded = nearest != null && alt < SwitchAltitude;

            // A TERRA / vicino a un corpo: ancora al corpo sotto i piedi (fermo in scena → camminata
            // e atterraggio stabili). IN VOLO con una DESTINAZIONE selezionata: ancora alla destinazione
            // → è FERMA in scena e centrata, e il freno X (match velocity) azzera la velocità RISPETTO A
            // LEI (sincronizzi, come l'aggancio di Outer Wilds). In volo senza destinazione: al più vicino.
            // Allo switch di corpo si trasla giocatore+oggetti per restare nello stesso punto-universo.
            CelestialBody target = grounded ? nearest : (Destination != null ? Destination : nearest);
            if (target != null)
            {
                if (currentAnchor != target)
                {
                    Vector3 shift = (FloatingOrigin.SceneOrigin - target.UniversePosition).ToVector3();
                    ShiftLoose(shift);
                    currentAnchor = target;
                }
                FloatingOrigin.SceneOrigin = target.UniversePosition;
            }
        }
        else if (Anchor != null) FloatingOrigin.SceneOrigin = Anchor.UniversePosition;

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

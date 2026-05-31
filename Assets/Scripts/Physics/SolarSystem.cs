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
    public CelestialBody Anchor;            // l'origine segue questo corpo (il pianeta su cui stai)

    public List<CelestialBody> Bodies = new List<CelestialBody>();

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

        // 2. ancora l'origine al corpo di riferimento: resta a ~(0,0,0)
        if (Anchor != null) FloatingOrigin.SceneOrigin = Anchor.UniversePosition;

        // 3. proietta tutto nello spazio float di Unity
        for (int i = 0; i < Bodies.Count; i++)
            if (Bodies[i] != null) Bodies[i].SyncTransform();
    }
}

using UnityEngine;

/// <summary>
/// Un corpo celeste (stella, pianeta, luna). La sua posizione autorevole vive
/// in coordinate-universo (double). Se ha un'orbita, la posizione si ricalcola
/// analiticamente a ogni frame; altrimenti resta fissa (la stella centrale).
///
/// La gravità è definita tramite la gravità di superficie e il raggio — più
/// intuitivo di una massa con G. Da lì ricaviamo μ = g·r², così a livello del
/// suolo l'accelerazione è esattamente SurfaceGravity.
/// </summary>
public class CelestialBody : MonoBehaviour
{
    public double Radius = 500;
    public double SurfaceGravity = 9.81;
    public KeplerOrbit Orbit;        // null -> corpo fisso (la stella)
    public CelestialBody Parent;     // corpo centrale dell'orbita
    // Punto SENZA MASSA (es. il baricentro di un binario): orbita ed È genitore di altri corpi, ma non attrae,
    // non ancora l'origine, non compare in mappa come bersaglio. La sua ORBITA sì viene disegnata ("O"/mappa).
    public bool Massless;

    public Vector3d UniversePosition;
    public Vector3d UniverseVelocity;   // cache: aggiornata UNA volta per Step da SolarSystem; i consumatori la leggono (no ricalcolo per-frame)
    public StarSystem System;           // sistema stellare di appartenenza (Tappa 1 multi-sistema). A N=1 c'è un solo sistema

    /// <summary>Parametro gravitazionale standard μ = g·r².</summary>
    public double Mu => SurfaceGravity * Radius * Radius;

    public void UpdatePosition(double time)
    {
        if (Orbit != null && Parent != null)
        {
            // aggiorna PRIMA il genitore (ricorsivo) → la posizione del figlio non dipende più dall'ordine
            // dell'array Bodies: riordinarlo o aggiungere una luna-di-luna prima del suo genitore non introduce
            // più un lag di un frame. Idempotente (stesso 'time'), si ferma alla stella (Orbit==null), niente
            // cicli (la gerarchia è un albero). Costo trascurabile (pochi corpi, Kepler analitico).
            Parent.UpdatePosition(time);
            UniversePosition = Parent.UniversePosition + Orbit.GetRelativePosition(time);
        }
    }

    /// <summary>
    /// Velocità-universo del corpo (m per secondo di SimTime), per differenza finita centrata
    /// sull'orbita analitica. Ricorre al genitore: una luna somma la velocità del suo pianeta.
    /// Un corpo fisso (la stella) ha velocità zero. Serve al cambio di sistema di riferimento:
    /// per preservare il moto reale del giocatore quando l'origine passa da un corpo all'altro.
    /// </summary>
    public Vector3d UniverseVelocityAt(double time)
    {
        if (Orbit == null || Parent == null) return Vector3d.Zero;
        const double dt = 0.01;
        Vector3d rel = (Orbit.GetRelativePosition(time + dt) - Orbit.GetRelativePosition(time - dt)) / (2.0 * dt);
        return Parent.UniverseVelocityAt(time) + rel;
    }

    /// <summary>Proietta la posizione-universo nello spazio di rendering di Unity.</summary>
    public void SyncTransform()
    {
        transform.position = (UniversePosition - FloatingOrigin.SceneOrigin).ToVector3();
    }
}

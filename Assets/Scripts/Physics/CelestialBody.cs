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

    public Vector3d UniversePosition;

    /// <summary>Parametro gravitazionale standard μ = g·r².</summary>
    public double Mu => SurfaceGravity * Radius * Radius;

    /// <summary>Accelerazione gravitazionale a distanza r dal centro.</summary>
    public float GravityAt(float r)
    {
        double rr = r;
        return (float)(Mu / (rr * rr));
    }

    public void UpdatePosition(double time)
    {
        if (Orbit != null && Parent != null)
            UniversePosition = Parent.UniversePosition + Orbit.GetRelativePosition(time);
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

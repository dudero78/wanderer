using UnityEngine;

/// <summary>
/// Vettore a doppia precisione. È il cuore dell'architettura: ogni posizione
/// "vera" nell'universo vive qui, in double, lontano dai limiti del float.
/// Unity (rendering e fisica) lavora sempre in float vicino all'origine;
/// la conversione avviene in un solo punto (CelestialBody.SyncTransform).
/// Questo isola la precisione dal resto del gioco: qualunque cambio di
/// direzione futuro non tocca questa fondazione.
/// </summary>
public struct Vector3d
{
    public double x, y, z;

    public Vector3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
    public Vector3d(Vector3 v) { x = v.x; y = v.y; z = v.z; }

    public static readonly Vector3d Zero = new Vector3d(0, 0, 0);

    public double magnitude => System.Math.Sqrt(x * x + y * y + z * z);
    public double sqrMagnitude => x * x + y * y + z * z;

    public Vector3d normalized
    {
        get { double m = magnitude; return m > 1e-12 ? new Vector3d(x / m, y / m, z / m) : Zero; }
    }

    /// <summary>Conversione verso lo spazio float di Unity. L'unico ponte double -> float.</summary>
    public Vector3 ToVector3() => new Vector3((float)x, (float)y, (float)z);

    public static Vector3d operator +(Vector3d a, Vector3d b) => new Vector3d(a.x + b.x, a.y + b.y, a.z + b.z);
    public static Vector3d operator -(Vector3d a, Vector3d b) => new Vector3d(a.x - b.x, a.y - b.y, a.z - b.z);
    public static Vector3d operator -(Vector3d a) => new Vector3d(-a.x, -a.y, -a.z);
    public static Vector3d operator *(Vector3d a, double s) => new Vector3d(a.x * s, a.y * s, a.z * s);
    public static Vector3d operator *(double s, Vector3d a) => a * s;
    public static Vector3d operator /(Vector3d a, double s) => new Vector3d(a.x / s, a.y / s, a.z / s);

    public static double Distance(Vector3d a, Vector3d b) => (a - b).magnitude;
    public static double Dot(Vector3d a, Vector3d b) => a.x * b.x + a.y * b.y + a.z * b.z;

    public override string ToString() => $"({x:F1}, {y:F1}, {z:F1})";
}

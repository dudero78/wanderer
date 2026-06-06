using static System.Math;

/// <summary>
/// Orbita Kepleriana analitica. Niente integrazione passo-passo: data l'epoca,
/// la posizione a qualunque istante si calcola in forma chiusa. È deterministica
/// e non accumula mai drift numerico — l'orbita è stabile per sempre.
/// Logica C# pura, senza dipendenze da Unity: facile da testare in isolamento.
/// </summary>
[System.Serializable]
public class KeplerOrbit
{
    public double SemiMajorAxis = 60000;        // a   — semiasse maggiore
    public double Eccentricity = 0.0;           // e   — 0 = cerchio, ->1 = ellisse stretta
    public double Period = 600;                 // T   — secondi per un'orbita completa
    public double Inclination = 0;              // i   — inclinazione del piano (radianti)
    public double LongitudeAscendingNode = 0;   // Ω
    public double ArgumentOfPeriapsis = 0;      // ω
    public double MeanAnomalyAtEpoch = 0;       // M0  — fase a t = 0

    /// <summary>Posizione relativa al corpo centrale, all'istante <paramref name="time"/>.</summary>
    public Vector3d GetRelativePosition(double time)
    {
        double n = 2.0 * PI / Period;                 // moto medio
        double M = MeanAnomalyAtEpoch + n * time;     // anomalia media
        double E = SolveKepler(M, Eccentricity);      // anomalia eccentrica

        double a = SemiMajorAxis, e = Eccentricity;
        // posizione nel piano orbitale (frame perifocale)
        double xp = a * (Cos(E) - e);
        double yp = a * Sqrt(Max(0.0, 1 - e * e)) * Sin(E);

        // rotazione perifocale -> inerziale: R_z(Ω) · R_x(i) · R_z(ω)
        double cw = Cos(ArgumentOfPeriapsis), sw = Sin(ArgumentOfPeriapsis);
        double cO = Cos(LongitudeAscendingNode), sO = Sin(LongitudeAscendingNode);
        double ci = Cos(Inclination), si = Sin(Inclination);

        double x = (cO * cw - sO * sw * ci) * xp + (-cO * sw - sO * cw * ci) * yp;
        double y = (sO * cw + cO * sw * ci) * xp + (-sO * sw + cO * cw * ci) * yp;
        double z = (sw * si) * xp + (cw * si) * yp;

        // piano orbitale (x,y) -> piano orizzontale di Unity (x,z); fuori-piano -> su (y)
        return new Vector3d(x, z, y);
    }

    /// <summary>Velocità relativa al corpo centrale (m per secondo di SimTime), in forma CHIUSA: la derivata
    /// analitica esatta di <see cref="GetRelativePosition"/>. Sostituisce la differenza finita (dt=0.01) che era
    /// solo un'approssimazione O(dt²) e costava 2 solve di Kepler. dE/dt = n/(1−e·cosE) da M = E − e·sinE.</summary>
    public Vector3d GetRelativeVelocity(double time)
    {
        double n = 2.0 * PI / Period;
        double M = MeanAnomalyAtEpoch + n * time;
        double E = SolveKepler(M, Eccentricity);

        double a = SemiMajorAxis, e = Eccentricity;
        double Edot = n / (1 - e * Cos(E));           // derivata dell'anomalia eccentrica
        double dxp = -a * Sin(E) * Edot;              // velocità nel frame perifocale
        double dyp = a * Sqrt(Max(0.0, 1 - e * e)) * Cos(E) * Edot;

        // stessa rotazione perifocale -> inerziale di GetRelativePosition (lineare → vale anche sulla velocità)
        double cw = Cos(ArgumentOfPeriapsis), sw = Sin(ArgumentOfPeriapsis);
        double cO = Cos(LongitudeAscendingNode), sO = Sin(LongitudeAscendingNode);
        double ci = Cos(Inclination), si = Sin(Inclination);

        double vx = (cO * cw - sO * sw * ci) * dxp + (-cO * sw - sO * cw * ci) * dyp;
        double vy = (sO * cw + cO * sw * ci) * dxp + (-sO * sw + cO * cw * ci) * dyp;
        double vz = (sw * si) * dxp + (cw * si) * dyp;

        return new Vector3d(vx, vz, vy);              // stesso rimappaggio assi (x,y)->(x,z) di GetRelativePosition
    }

    /// <summary>Risolve M = E - e·sin(E) con Newton-Raphson. Converge in pochi passi.</summary>
    static double SolveKepler(double M, double e)
    {
        M %= 2 * PI;
        if (M < -PI) M += 2 * PI; else if (M > PI) M -= 2 * PI;

        double E = e < 0.8 ? M : PI;
        for (int k = 0; k < 24; k++)
        {
            double f = E - e * Sin(E) - M;
            double fp = 1 - e * Cos(E);
            double d = f / fp;
            E -= d;
            if (Abs(d) < 1e-13) break;
        }
        return E;
    }
}

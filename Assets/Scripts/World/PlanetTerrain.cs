using UnityEngine;

/// <summary>
/// Definisce la forma del terreno di un pianeta. È la verità condivisa: lo stesso
/// SampleHeight è usato sia per generare la mesh che dal PlanetWalker per tenere il
/// giocatore sulla superficie. Mesh e collisione non possono mai divergere.
/// </summary>
public class PlanetTerrain : MonoBehaviour
{
    public float BaseRadius = 500f;
    public float Amplitude = 30f;     // dislivello massimo (± rispetto al raggio)
    public float Frequency = 2.5f;    // scala delle feature sulla sfera unitaria
    public int Octaves = 6;
    public float Lacunarity = 2f;
    public float Gain = 0.5f;
    public int Seed = 1337;

    /// <summary>Distanza dal centro della superficie nella direzione (unitaria) data.</summary>
    public float SampleHeight(Vector3 unitDir)
    {
        float n = Noise3D.Fbm(unitDir * Frequency, Octaves, Lacunarity, Gain, Seed); // [0,1]
        return BaseRadius + (n - 0.5f) * 2f * Amplitude;
    }

    /// <summary>
    /// Normale della superficie nella direzione data, calcolata dalla pendenza del terreno
    /// (differenze centrali sui due assi tangenti). Dipende solo da direzione e altezza,
    /// quindi è continua su tutta la sfera: niente cuciture tra le facce.
    /// </summary>
    public Vector3 SurfaceNormal(Vector3 dir, float eps)
    {
        dir = dir.normalized;
        Vector3 refV = Mathf.Abs(dir.y) < 0.99f ? Vector3.up : Vector3.right;
        Vector3 tA = Vector3.Normalize(Vector3.Cross(dir, refV));
        Vector3 tB = Vector3.Cross(dir, tA);

        Vector3 dAp = (dir + tA * eps).normalized;
        Vector3 dAm = (dir - tA * eps).normalized;
        Vector3 dBp = (dir + tB * eps).normalized;
        Vector3 dBm = (dir - tB * eps).normalized;

        Vector3 pAp = dAp * SampleHeight(dAp);
        Vector3 pAm = dAm * SampleHeight(dAm);
        Vector3 pBp = dBp * SampleHeight(dBp);
        Vector3 pBm = dBm * SampleHeight(dBm);

        Vector3 n = Vector3.Cross(pBp - pBm, pAp - pAm).normalized;
        if (Vector3.Dot(n, dir) < 0f) n = -n;   // sempre verso l'esterno
        return n;
    }
}

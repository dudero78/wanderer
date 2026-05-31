using UnityEngine;

/// <summary>
/// Value noise 3D + fBm (somma di ottave). Deterministico: stesso seed, stesso
/// risultato, sempre. Lavora su posizioni 3D, quindi è continuo su tutta la sfera
/// senza giunzioni — perfetto per spostare l'altezza del terreno di un pianeta.
/// </summary>
public static class Noise3D
{
    static float Hash(int x, int y, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)seed;
            h ^= (uint)(x * 374761393);
            h ^= (uint)(y * 668265263);
            h ^= (uint)(z * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFFu) / (float)0x1000000;   // [0,1)
        }
    }

    static float Smooth(float t) => t * t * (3f - 2f * t);

    /// <summary>Value noise interpolato sui vertici del cubo unitario. [0,1)</summary>
    public static float Value(Vector3 p, int seed)
    {
        int xi = Mathf.FloorToInt(p.x), yi = Mathf.FloorToInt(p.y), zi = Mathf.FloorToInt(p.z);
        float u = Smooth(p.x - xi), v = Smooth(p.y - yi), w = Smooth(p.z - zi);

        float c000 = Hash(xi, yi, zi, seed),       c100 = Hash(xi + 1, yi, zi, seed);
        float c010 = Hash(xi, yi + 1, zi, seed),   c110 = Hash(xi + 1, yi + 1, zi, seed);
        float c001 = Hash(xi, yi, zi + 1, seed),   c101 = Hash(xi + 1, yi, zi + 1, seed);
        float c011 = Hash(xi, yi + 1, zi + 1, seed), c111 = Hash(xi + 1, yi + 1, zi + 1, seed);

        float x00 = Mathf.Lerp(c000, c100, u);
        float x10 = Mathf.Lerp(c010, c110, u);
        float x01 = Mathf.Lerp(c001, c101, u);
        float x11 = Mathf.Lerp(c011, c111, u);
        return Mathf.Lerp(Mathf.Lerp(x00, x10, v), Mathf.Lerp(x01, x11, v), w);
    }

    /// <summary>Fractal Brownian motion: somma di ottave a frequenza crescente. [0,1]</summary>
    public static float Fbm(Vector3 p, int octaves, float lacunarity, float gain, int seed)
    {
        float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * Value(p * freq, seed + i * 1013);
            norm += amp;
            freq *= lacunarity;
            amp *= gain;
        }
        return sum / norm;
    }
}

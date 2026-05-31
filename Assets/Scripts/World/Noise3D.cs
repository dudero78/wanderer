using UnityEngine;

/// <summary>
/// Gradient noise 3D (Perlin) + fBm. Deterministico: stesso seed, stesso risultato.
/// Lavora su posizioni 3D, quindi è continuo su tutta la sfera senza giunzioni.
/// A differenza del value noise, vale ZERO sui punti del reticolo con gradienti casuali:
/// niente struttura "a celle" visibile nelle normali sotto luce radente (i "glifi").
/// </summary>
public static class Noise3D
{
    static uint UHash(int x, int y, int z, int seed)
    {
        unchecked
        {
            // mixing sequenziale: avalanche pieno, niente correlazioni assiali
            uint h = (uint)seed;
            h = (h + (uint)x) * 0x9E3779B1u; h ^= h >> 16;
            h = (h + (uint)y) * 0x85EBCA77u; h ^= h >> 13;
            h = (h + (uint)z) * 0xC2B2AE3Du; h ^= h >> 16;
            h *= 0x27D4EB2Fu; h ^= h >> 15;
            return h;
        }
    }

    /// <summary>Gradiente pseudo-casuale in [-1,1]^3 sul punto di reticolo dato.</summary>
    static Vector3 Grad(int x, int y, int z, int seed)
    {
        uint h = UHash(x, y, z, seed);
        return new Vector3(
            (h & 0x3FFu) / 511.5f - 1f,
            ((h >> 10) & 0x3FFu) / 511.5f - 1f,
            ((h >> 20) & 0x3FFu) / 511.5f - 1f);
    }

    // interpolazione quintica (C2): derivata seconda continua -> normali lisce ai bordi cella
    static float Quintic(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    /// <summary>Gradient noise (Perlin) sui vertici del cubo unitario. ~[-1,1]</summary>
    public static float Value(Vector3 p, int seed)
    {
        int xi = Mathf.FloorToInt(p.x), yi = Mathf.FloorToInt(p.y), zi = Mathf.FloorToInt(p.z);
        float fx = p.x - xi, fy = p.y - yi, fz = p.z - zi;
        float u = Quintic(fx), v = Quintic(fy), w = Quintic(fz);

        float n000 = Vector3.Dot(Grad(xi,     yi,     zi,     seed), new Vector3(fx,      fy,      fz));
        float n100 = Vector3.Dot(Grad(xi + 1, yi,     zi,     seed), new Vector3(fx - 1f, fy,      fz));
        float n010 = Vector3.Dot(Grad(xi,     yi + 1, zi,     seed), new Vector3(fx,      fy - 1f, fz));
        float n110 = Vector3.Dot(Grad(xi + 1, yi + 1, zi,     seed), new Vector3(fx - 1f, fy - 1f, fz));
        float n001 = Vector3.Dot(Grad(xi,     yi,     zi + 1, seed), new Vector3(fx,      fy,      fz - 1f));
        float n101 = Vector3.Dot(Grad(xi + 1, yi,     zi + 1, seed), new Vector3(fx - 1f, fy,      fz - 1f));
        float n011 = Vector3.Dot(Grad(xi,     yi + 1, zi + 1, seed), new Vector3(fx,      fy - 1f, fz - 1f));
        float n111 = Vector3.Dot(Grad(xi + 1, yi + 1, zi + 1, seed), new Vector3(fx - 1f, fy - 1f, fz - 1f));

        float x00 = Mathf.Lerp(n000, n100, u);
        float x10 = Mathf.Lerp(n010, n110, u);
        float x01 = Mathf.Lerp(n001, n101, u);
        float x11 = Mathf.Lerp(n011, n111, u);
        return Mathf.Lerp(Mathf.Lerp(x00, x10, v), Mathf.Lerp(x01, x11, v), w);
    }

    // ruota il dominio tra le ottave: decorrela le orientazioni, terreno più naturale
    static Vector3 Rotate(Vector3 v) => new Vector3(
         0.00f * v.x + 0.80f * v.y + 0.60f * v.z,
        -0.80f * v.x + 0.36f * v.y - 0.48f * v.z,
        -0.60f * v.x - 0.48f * v.y + 0.64f * v.z);

    /// <summary>Fractal Brownian motion: somma di ottave a frequenza crescente. [0,1]</summary>
    public static float Fbm(Vector3 p, int octaves, float lacunarity, float gain, int seed)
    {
        float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
        Vector3 q = p;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * Value(q * freq, seed + i * 1013);
            norm += amp;
            freq *= lacunarity;
            amp *= gain;
            q = Rotate(q);
        }
        return Mathf.Clamp01(sum / norm * 0.5f + 0.5f);   // Perlin ~[-1,1] -> [0,1]
    }
}

using UnityEngine;

/// <summary>
/// Forma di base del corpo: fBm gradient-noise sulla sfera. È sempre il PRIMO layer
/// della pipeline e ignora l'altezza in ingresso — definisce la fondazione su cui gli
/// altri processi scolpiscono.
///
/// Il rapporto ampiezza/raggio è ciò che distingue le taglie di corpo: un corpo piccolo
/// e irregolare (tipo Phobos) ha ampiezza ALTA rispetto al raggio (la gravità non lo
/// liscia); un corpo grande (tipo Luna) la avrebbe BASSA — quasi una sfera — e il
/// rilievo verrebbe quasi tutto dai crateri, non da qui.
/// </summary>
public class BaseTerrainLayer : TerrainLayer
{
    readonly float baseRadius, amplitude, frequency, lacunarity, gain;
    readonly int octaves, seed;

    public BaseTerrainLayer(float baseRadius, float amplitude, float frequency,
                            int octaves, float lacunarity, float gain, int seed)
    {
        this.baseRadius = baseRadius;
        this.amplitude = amplitude;
        this.frequency = frequency;
        this.octaves = octaves;
        this.lacunarity = lacunarity;
        this.gain = gain;
        this.seed = seed;
    }

    public override float Apply(Vector3 unitDir, float height)
    {
        float n = Noise3D.Fbm(unitDir * frequency, octaves, lacunarity, gain, seed); // [0,1]
        return baseRadius + (n - 0.5f) * 2f * amplitude;
    }
}

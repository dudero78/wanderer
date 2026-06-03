using UnityEngine;

/// <summary>
/// Processo "mare": allaga il terreno fino a una quota. Tutto ciò che sta SOTTO il pelo dell'acqua viene
/// portato al livello del mare → una superficie piatta che copre bacini e crateri bassi; le terre emerse
/// restano quelle che superano la quota. Alzando il livello sommergi più crateri, abbassandolo riemergono.
///
/// È GEOMETRIA vera (non un colore): `h = max(h, seaRadius)`. Va in fondo alla pipeline (DOPO i crateri: i
/// crateri scavano, poi il mare riempie). Essendo nella pipeline, il walker ci cammina sopra e la mesh
/// combaciano da soli (stessa SampleHeight) — il mare è solido. L'aspetto (colore acqua) lo dà lo shader,
/// che tinge i punti a quota ≤ seaRadius.
/// </summary>
public class SeaTerrainLayer : TerrainLayer
{
    readonly float seaRadius;   // raggio assoluto del pelo (a riposo, senza increspatura)
    readonly float roughness;   // ampiezza dell'increspatura (m): 0 = piatto
    readonly float roughScale;  // frequenza dell'increspatura (colline larghe ↔ dune fitte)
    readonly int seed;

    public SeaTerrainLayer(float baseRadius, float seaLevel, float roughness = 0f, float roughScale = 3f, int seed = 4242)
    {
        seaRadius = baseRadius + seaLevel;
        this.roughness = Mathf.Max(0f, roughness);
        this.roughScale = Mathf.Max(0.1f, roughScale);
        this.seed = seed;
    }

    public override float Apply(Vector3 unitDir, float height)
    {
        // il pelo dell'acqua può essere increspato (colline/dune): fBm centrato su 0, scalato dall'ampiezza.
        float surface = seaRadius;
        if (roughness > 0f)
            surface += (Noise3D.Fbm(unitDir * roughScale, 4, 2f, 0.5f, seed) - 0.5f) * 2f * roughness;
        return Mathf.Max(height, surface);
    }
}

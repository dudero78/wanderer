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
    readonly float seaRadius;   // raggio assoluto del pelo (a riposo, senza rilievo)
    readonly float roughness;   // ampiezza del rilievo del fondale (m): 0 = piatto
    readonly float roughScale;  // frequenza del rilievo (forme larghe ↔ fitte)
    readonly float forma;       // −1 creste/dune, 0 liscio, +1 collinette/gobbe
    readonly int seed;

    public SeaTerrainLayer(float baseRadius, float seaLevel, float roughness = 0f, float roughScale = 3f, float forma = 0f, int seed = 4242)
    {
        seaRadius = baseRadius + seaLevel;
        this.roughness = Mathf.Max(0f, roughness);
        this.roughScale = Mathf.Max(0.1f, roughScale);
        this.forma = Mathf.Clamp(forma, -1f, 1f);
        this.seed = seed;
    }

    /// <summary>Quota del PELO del mare nella direzione data (raggio + rilievo del fondale). Lo shader la
    /// ricostruisce IDENTICA (n3_fbm + SeaShape) per tingere il mare seguendo la geometria, qualunque forma abbia.</summary>
    public float Surface(Vector3 unitDir)
    {
        if (roughness <= 0f) return seaRadius;
        float c = (Noise3D.Fbm(unitDir * roughScale, 4, 2f, 0.5f, seed) - 0.5f) * 2f;   // rumore centrato [-1,1]
        return seaRadius + Shape(c, forma) * roughness;
    }

    public override float Apply(Vector3 unitDir, float height) => Mathf.Max(height, Surface(unitDir));

    /// <summary>Modella il rumore centrato 'c' secondo 'forma': −1 = creste (dune), 0 = liscio, +1 = gobbe
    /// (collinette). DEVE combaciare con SeaShape() nello shader (PlanetSurfaceBaked) per la tinta.</summary>
    public static float Shape(float c, float forma)
    {
        float ridged = 1f - 2f * Mathf.Abs(c);   // creste affilate (dune)
        float billow = 2f * Mathf.Abs(c) - 1f;   // gobbe rotonde (collinette)
        return forma < 0f ? Mathf.Lerp(ridged, c, forma + 1f) : Mathf.Lerp(c, billow, forma);
    }
}

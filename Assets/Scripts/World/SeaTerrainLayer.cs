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
    readonly float seaRadius;   // raggio assoluto del pelo dell'acqua

    public SeaTerrainLayer(float baseRadius, float seaLevel)
    {
        seaRadius = baseRadius + seaLevel;
    }

    public override float Apply(Vector3 unitDir, float height) => Mathf.Max(height, seaRadius);
}

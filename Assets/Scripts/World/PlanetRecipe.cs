using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RICETTA di un pianeta: la definizione completa e SALVABILE di un corpo (forma base + pipeline di processi
/// + colore). È la fonte di verità unica condivisa da:
///   - l'EDITOR (pannelli/slider che la modificano dal vivo),
///   - il BAKE (la cuoce in texture: albedo/normale/height per l'orbita),
///   - il RENDERING (il quadtree calcola l'altezza per-vertice da questa ricetta → crateri netti calpestabili).
///
/// Serializable + JSON (JsonUtility) → si salva/carica anche in build (l'editor gira dal menu del gioco), non
/// solo nell'editor Unity. I `TerrainLayer` (BaseTerrainLayer, CraterTerrainLayer, …) sono l'ESECUZIONE di
/// questa ricetta: PlanetTerrain.ApplyRecipe costruisce lo stack dai campi qui sotto.
/// </summary>
[System.Serializable]
public class PlanetRecipe
{
    public string name = "Nuovo corpo";
    public float baseRadius = 500f;
    public float surfaceGravity = 9.81f;

    [Header("Forma di base (fBm)")]
    public float amplitude = 45f;
    public float frequency = 2.0f;
    public int octaves = 5;
    public float lacunarity = 2f;
    public float gain = 0.55f;
    public int seed = 1337;

    [Header("Pipeline di crateri (0..N: aggiungibili/rimovibili dall'editor)")]
    public List<CraterRecipe> craters = new List<CraterRecipe>();

    [Header("Colore / mari")]
    public Color soilMean = new Color(0.44f, 0.44f, 0.45f);
    public Color mariaColor = new Color(0.52f, 0.52f, 0.56f);
    public float mariaScale = 2.2f;
    public float mariaStrength = 0.7f;

    /// <summary>Ricetta del "pianeta demo" attuale (tipo Phobos). Punto di partenza dell'editor.</summary>
    public static PlanetRecipe Demo()
    {
        var r = new PlanetRecipe { name = "Demo (Phobos-like)", baseRadius = 500f, surfaceGravity = 9.81f };
        r.craters.Add(new CraterRecipe
        {
            enabled = true, seed = 7777, octaves = 5, largestRadius = 110f, density = 0.9f,
            depthRatio = 0.20f, rimRatio = 0.30f,
            dominant = true, dominantDir = new Vector3(0.3f, 1f, 0.2f), dominantRadius = 230f
        });
        return r;
    }
}

/// <summary>Una "pipeline di bombardamento": un campo di crateri con la sua distribuzione e morfologia.
/// Più pipeline = più popolazioni di crateri (es. antiche grandi + recenti piccole). L'editor le aggiunge/toglie.</summary>
[System.Serializable]
public class CraterRecipe
{
    public bool enabled = true;
    public int seed = 7777;
    public int octaves = 5;            // bande di taglia (raggio dimezzato per ottava)
    public float largestRadius = 110f; // m
    public float density = 0.55f;      // prob. cratere per cella [0..1]
    public float depthRatio = 0.20f;   // profondità/raggio
    public float rimRatio = 0.30f;     // bordo/profondità
    [Space]
    public bool dominant = false;      // un grande impatto piazzato a mano (tipo Stickney)
    public Vector3 dominantDir = new Vector3(0.3f, 1f, 0.2f);
    public float dominantRadius = 230f;
}

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

    [Header("Mari (GEOMETRIA: allagamento)")]
    public bool seaEnabled = false;
    public float seaLevel = 0f;        // quota del pelo dell'acqua, METRI relativi al baseRadius (− sotto, + sopra)
    public Color seaColor = new Color(0.13f, 0.33f, 0.52f);

    [Header("Superficie")]
    public string soilTexture = "soil_dirt";   // texture del suolo in Resources/Textures (grana + macro)
    public float saturation = 1f;              // saturazione del colore finale (0 = grigio, 1 = naturale, >1 = carico)

    /// <summary>Raggio assoluto del pelo dell'acqua (per shader e SeaTerrainLayer).</summary>
    public float SeaRadius => baseRadius + seaLevel;

    /// <summary>Carica una ricetta salvata come asset del progetto (Assets/Resources/Planets/&lt;name&gt;.json, importata
    /// come TextAsset) → finisce nella build. null se non c'è. Le ricette dell'editor (persistentDataPath) si
    /// copiano qui per renderle parte del gioco.</summary>
    public static PlanetRecipe LoadResource(string name)
    {
        var ta = Resources.Load<TextAsset>("Planets/" + name);
        if (ta == null) { Debug.LogWarning("Ricetta '" + name + "' non trovata in Resources/Planets."); return null; }
        return JsonUtility.FromJson<PlanetRecipe>(ta.text);
    }

    /// <summary>Copia della ricetta scalata a un nuovo raggio: le misure ASSOLUTE (ampiezza, raggi dei crateri) si
    /// scalano col raggio, le grandezze adimensionali (frequenze, densità, rapporti, colori) restano invariate →
    /// STESSO aspetto autorato, corpo più piccolo o più grande. Il baseRadius risultante DEVE combaciare col
    /// Radius del CelestialBody (mesh e gravità sulla stessa scala).</summary>
    public PlanetRecipe ScaledTo(float targetRadius)
    {
        var c = JsonUtility.FromJson<PlanetRecipe>(JsonUtility.ToJson(this));   // copia profonda
        float k = baseRadius > 1e-3f ? targetRadius / baseRadius : 1f;
        c.baseRadius = targetRadius;
        c.amplitude *= k;
        c.seaLevel *= k;   // misura assoluta: scala col raggio (il pelo dell'acqua resta alla stessa quota relativa)
        foreach (var cr in c.craters)
        {
            if (cr == null) continue;
            cr.largestRadius *= k;
            cr.dominantRadius *= k;
        }
        return c;
    }

    /// <summary>Sfera quasi liscia, nessun processo: il punto di PARTENZA dell'editor (poi aggiungi tutto).</summary>
    public static PlanetRecipe SmoothSphere()
    {
        return new PlanetRecipe
        {
            name = "Nuovo corpo", baseRadius = 500f, surfaceGravity = 9.81f,
            amplitude = 4f, frequency = 1.6f, octaves = 4, gain = 0.5f, seed = 1
        };
    }

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
    public float rimSharpness = 2f;    // ripidità della parete verso il bordo: 1 = cono, >1 = fondo piatto + bordo a cresta netta
    [Space]
    public bool dominant = false;      // un grande impatto piazzato a mano (tipo Stickney)
    public Vector3 dominantDir = new Vector3(0.3f, 1f, 0.2f);
    public float dominantRadius = 230f;
}

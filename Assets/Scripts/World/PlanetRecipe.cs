using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RICETTA di un pianeta: la definizione completa e SALVABILE di un corpo (forma base + processi + colore).
/// È la fonte di verità unica condivisa da editor, bake e rendering.
///
/// I PROCESSI sono una lista ORDINATA (<see cref="processes"/>): ogni voce è un "bombardamento" di crateri o
/// un "allagamento" (mare). L'ordine è la sequenza geologica e CAMBIA il risultato — un cratere DOPO un mare
/// scava una buca asciutta nell'acqua; PRIMA del mare viene sommerso. Lo stack di <see cref="TerrainLayer"/>
/// (in PlanetTerrain) esegue i processi in quest'ordine sopra la forma di base.
///
/// Serializable + JSON (JsonUtility). Le ricette VECCHIE (campi <see cref="craters"/> + mare singolo) si
/// MIGRANO da sole in <see cref="processes"/> al primo uso (vedi <see cref="Normalize"/>): niente rotture.
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

    [Header("Processi (lista ORDINATA: crateri / mari; l'ordine conta)")]
    public List<ProcessStep> processes = new List<ProcessStep>();

    [Header("Colore")]
    public Color soilMean = new Color(0.44f, 0.44f, 0.45f);
    public Color mariaColor = new Color(0.52f, 0.52f, 0.56f);
    public float mariaScale = 2.2f;
    public float mariaStrength = 0.7f;

    [Header("Superficie")]
    public string soilTexture = "soil_dirt";
    public float saturation = 1f;

    // --- LEGACY (solo per deserializzare ricette vecchie e migrarle; svuotati da Normalize) ---
    public List<CraterRecipe> craters = new List<CraterRecipe>();
    public bool seaEnabled = false;
    public float seaLevel = 0f;
    public Color seaColor = new Color(0.13f, 0.33f, 0.52f);

    /// <summary>Migra il modello vecchio (craters + mare singolo) nella lista ordinata di processi, una
    /// volta. Idempotente: se i processi ci sono già, non fa nulla. I crateri vanno prima, poi il mare —
    /// l'ordine storico. Da chiamare prima di leggere <see cref="processes"/>.</summary>
    public void Normalize()
    {
        if (processes == null) processes = new List<ProcessStep>();
        if (processes.Count > 0) return;
        if (craters != null)
            foreach (var c in craters)
                if (c != null) processes.Add(ProcessStep.FromCrater(c));
        if (seaEnabled) processes.Add(ProcessStep.NewSea(seaLevel, seaColor));
        craters = new List<CraterRecipe>();   // la verità ora è 'processes'
        seaEnabled = false;
    }

    /// <summary>L'ultimo mare ATTIVO della pipeline (= la superficie d'acqua finale), per la tinta dello
    /// shader; null se nessuno.</summary>
    public ProcessStep LastSea()
    {
        ProcessStep sea = null;
        if (processes != null)
            foreach (var p in processes)
                if (p != null && p.enabled && p.type == ProcessType.Mare) sea = p;
        return sea;
    }

    /// <summary>Il primo bombardamento ATTIVO, per il bake della normale-crateri. null se nessuno.</summary>
    public ProcessStep PrimaryCrater()
    {
        if (processes != null)
            foreach (var p in processes)
                if (p != null && p.enabled && p.type == ProcessType.Crateri) return p;
        return null;
    }

    public static PlanetRecipe LoadResource(string name)
    {
        var ta = Resources.Load<TextAsset>("Planets/" + name);
        if (ta == null) { Debug.LogWarning("Ricetta '" + name + "' non trovata in Resources/Planets."); return null; }
        var r = JsonUtility.FromJson<PlanetRecipe>(ta.text);
        r.Normalize();
        return r;
    }

    /// <summary>Copia scalata a un nuovo raggio: le misure ASSOLUTE (ampiezza, raggi crateri, livello mare)
    /// scalano col raggio, le adimensionali (frequenze, densità, rapporti, colori) restano → stesso aspetto.</summary>
    public PlanetRecipe ScaledTo(float targetRadius)
    {
        var c = JsonUtility.FromJson<PlanetRecipe>(JsonUtility.ToJson(this));   // copia profonda
        c.Normalize();
        float k = baseRadius > 1e-3f ? targetRadius / baseRadius : 1f;
        c.baseRadius = targetRadius;
        c.amplitude *= k;
        foreach (var p in c.processes)
        {
            if (p == null) continue;
            p.largestRadius *= k;
            p.dominantRadius *= k;
            p.seaLevel *= k;
            p.seaRoughness *= k;
            p.seaClarity *= k;
            p.elevationContrast *= k;
            p.boundaryUplift *= k;
            p.continentalRelief *= k;
        }
        return c;
    }

    /// <summary>Sfera quasi liscia, nessun processo: il punto di PARTENZA dell'editor.</summary>
    public static PlanetRecipe SmoothSphere()
    {
        return new PlanetRecipe
        {
            name = "Nuovo corpo", baseRadius = 500f, surfaceGravity = 9.81f,
            amplitude = 4f, frequency = 1.6f, octaves = 4, gain = 0.5f, seed = 1
        };
    }

    /// <summary>Ricetta del "pianeta demo" (tipo Phobos): un bombardamento.</summary>
    public static PlanetRecipe Demo()
    {
        var r = new PlanetRecipe { name = "Demo (Phobos-like)", baseRadius = 500f, surfaceGravity = 9.81f };
        r.processes.Add(new ProcessStep
        {
            type = ProcessType.Crateri,
            seed = 7777, octaves = 5, largestRadius = 110f, density = 0.9f,
            depthRatio = 0.20f, rimRatio = 0.30f,
            dominant = true, dominantDir = new Vector3(0.3f, 1f, 0.2f), dominantRadius = 230f
        });
        return r;
    }
}

/// <summary>Tipo di processo nella pipeline ordinata.</summary>
public enum ProcessType { Crateri, Mare, Tettonica }

/// <summary>Un passo della pipeline: un bombardamento di crateri OPPURE un allagamento (mare). Tiene i
/// parametri di ENTRAMBI; <see cref="type"/> decide quali si usano. Un'unica classe (niente polimorfismo)
/// per restare serializzabile da JsonUtility.</summary>
[System.Serializable]
public class ProcessStep
{
    public ProcessType type = ProcessType.Crateri;
    public bool enabled = true;

    // --- parametri CRATERI ---
    public int seed = 7777;
    public int octaves = 5;
    public float largestRadius = 110f;
    public float density = 0.55f;
    public float depthRatio = 0.20f;
    public float rimRatio = 0.30f;
    public float rimSharpness = 2f;
    // quota relativa di crateri per FASCIA di taglia (moltiplica la densità): 1 = piena, 0 = nessuno.
    // Le ottave vanno dalla più grande (grandi) alla più piccola (piccoli); 'medi' è la fascia centrale.
    public float wLarge = 1f, wMedium = 1f, wSmall = 1f;
    public float distribution = 0f;             // fase 0..1: ruota il campo di crateri → li "fa scorrere" sul pianeta
    public bool bigCraters = false;             // OFF (default) = placement classico (abbondante/organico); ON = owned-cell (crateri grandi affidabili a ogni raggio, niente crepe, ma più regolare — utile come pipeline combinabile)
    public bool dominant = false;
    public Vector3 dominantDir = new Vector3(0.3f, 1f, 0.2f);
    public float dominantRadius = 230f;
    // profilo PROPRIO del dominante (indipendente dai crateri di campo) + irregolarità
    public float domDepthRatio = 0.20f;         // profondità/raggio del dominante
    public float domRimRatio = 0.30f;           // altezza bordo / profondità
    public float domRimSharp = 2f;              // nitidezza bordo (1=cono … alto=cresta netta)
    public float domIrregular = 0f;             // 0 = circolare liscio; su = rim frastagliato e forma asimmetrica (impatto antico/battuto)
    public float domIrregScale = 6f;            // scala dell'irregolarità: bassa = lobi larghi (forma deformata), alta = rim ruvido fine
    [System.NonSerialized] public bool domUiOpen = true;   // stato UI: sezione dominante espansa/collassata (non salvato)

    // --- parametri TETTONICA ---
    public int plateCount = 8;                  // numero di placche (poche = continenti grandi, tante = guscio incrinato)
    public float continentalFraction = 0.4f;    // frazione di placche continentali (alte)
    public float elevationContrast = 60f;       // dislivello continente↔oceano (m)
    public float boundaryUplift = 40f;          // sollevamento/rift max ai confini (m)
    public float boundaryWidth = 0.08f;         // ampiezza fascia di confine
    public float tectonicWarp = 0.45f;          // irregolarità delle coste (domain warp); 0 = archi geometrici finti
    public float coastSlope = 0.5f;             // 0 = coste a scogliera, 1 = piattaforme continentali dolci
    public float continentalRelief = 18f;       // ampiezza (m) del rilievo INTERNO dei continenti (colline); oceani lisci
    public float riftBalance = 0.55f;            // carattere confini: 0 = solo catene in rilievo; 1 = i divergenti scavano canyon profondi come le catene

    // --- parametri MARE ---
    public float seaLevel = 0f;                 // quota del pelo, metri relativi al baseRadius
    public Color seaColor = new Color(0.13f, 0.33f, 0.52f);
    public float seaSaturation = 1f;            // saturazione del colore del mare (indipendente dal globale)
    public float seaRoughness = 0f;             // ampiezza del rilievo del fondale (m): 0 = piatto, su = mosso
    public float seaRoughScale = 3f;            // frequenza del rilievo: bassa = forme larghe, alta = fitte
    public float seaForma = 0f;                 // forma del fondale: −1 = creste/dune, 0 = liscio, +1 = collinette/gobbe
    public bool liquid = false;                 // true = resa come ACQUA (riflessi/lucentezza/fresnel); false = superficie opaca tinta. Solo visivo: la geometria resta il pelo piatto (il nuoto sarà gameplay)
    public bool seaClear = false;               // true = acqua TRASPARENTE: si vede il fondale sommerso, che sbiadisce verso il colore profondo con la profondità. Richiede liquid. Solo path GPU
    public float seaClarity = 8f;               // profondità (m) a cui l'acqua diventa ~opaca: bassa = torbida (vedi solo le secche), alta = cristallina (vedi anche il fondo profondo)

    public static ProcessStep FromCrater(CraterRecipe c) => new ProcessStep
    {
        type = ProcessType.Crateri, enabled = c.enabled,
        seed = c.seed, octaves = c.octaves, largestRadius = c.largestRadius, density = c.density,
        depthRatio = c.depthRatio, rimRatio = c.rimRatio, rimSharpness = c.rimSharpness,
        dominant = c.dominant, dominantDir = c.dominantDir, dominantRadius = c.dominantRadius
    };

    public static ProcessStep NewSea(float level, Color color) => new ProcessStep
    {
        type = ProcessType.Mare, enabled = true, seaLevel = level, seaColor = color
    };
}

/// <summary>LEGACY: una pipeline di crateri del modello vecchio. Resta solo per deserializzare/migrare le
/// ricette salvate prima della lista di processi (vedi PlanetRecipe.Normalize).</summary>
[System.Serializable]
public class CraterRecipe
{
    public bool enabled = true;
    public int seed = 7777;
    public int octaves = 5;
    public float largestRadius = 110f;
    public float density = 0.55f;
    public float depthRatio = 0.20f;
    public float rimRatio = 0.30f;
    public float rimSharpness = 2f;
    public bool dominant = false;
    public Vector3 dominantDir = new Vector3(0.3f, 1f, 0.2f);
    public float dominantRadius = 230f;
}

using UnityEngine;

/// <summary>
/// Preset del terreno dei corpi: un solo posto dove vivono i parametri (forma + crateri), così la scena
/// (GameBootstrap) e il bake offline su disco (editor) costruiscono lo STESSO pianeta. Se i parametri
/// stessero solo in GameBootstrap, il bake-tool dovrebbe duplicarli e divergerebbero.
/// </summary>
public static class PlanetPresets
{
    /// <summary>Configura il PlanetTerrain del "pianeta demo" (tipo Phobos: dolce + crateri).</summary>
    public static void ConfigureDemoPlanet(PlanetTerrain terrain)
    {
        terrain.BaseRadius = 500f;
        terrain.Amplitude = 45f;    // colline morbide: silhouette dolce, non "palla liscia"
        terrain.Frequency = 2.0f;
        // 5 ottave: terreno DOLCE, "luna prima di crateri/mari/rocce". Le feature più piccole sono ~100 m →
        // colline larghe e morbide. La definizione a media/corta distanza arriva dalle FEATURE (crateri, rocce).
        terrain.Octaves = 5;
        terrain.Gain = 0.55f;
        terrain.Seed = 1337;

        // Bombardamento: crateri come geometria vera nella forma (ci cammini dentro). Tarati per un corpo
        // piccolo tipo Phobos: tanti piccoli + un dominante (tipo Stickney).
        terrain.CratersEnabled = true;
        terrain.CraterOctaves = 5;            // ~110, 55, 27, 13, 7 m
        terrain.CraterLargestRadius = 110f;
        // densità alta per compensare la spaziatura più larga: celle più grandi → meno crateri per cella,
        // quindi ne mettiamo in (quasi) ogni cella per tenere il campo fitto.
        terrain.CraterDensity = 0.9f;
        terrain.CraterDepthRatio = 0.20f;
        terrain.CraterRimRatio = 0.30f;
        terrain.DominantCrater = true;
        terrain.DominantCraterDir = new Vector3(0.3f, 1f, 0.2f);  // verso il polo; lo spawn è all'equatore → separati
        terrain.DominantCraterRadius = 230f;
    }
}

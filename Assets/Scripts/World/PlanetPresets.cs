using UnityEngine;

/// <summary>
/// Preset del terreno dei corpi: un solo posto dove vivono i parametri (forma + crateri), così la scena
/// (GameBootstrap) e il bake offline su disco (editor) costruiscono lo STESSO pianeta. Se i parametri
/// stessero solo in GameBootstrap, il bake-tool dovrebbe duplicarli e divergerebbero.
/// </summary>
public static class PlanetPresets
{
    /// <summary>Configura il PlanetTerrain del "pianeta demo" via RICETTA (la stessa fonte di verità che userà
    /// l'editor). Comportamento identico ai vecchi campi: la ricetta Demo ha gli stessi valori.</summary>
    public static void ConfigureDemoPlanet(PlanetTerrain terrain)
    {
        terrain.ApplyRecipe(PlanetRecipe.Demo());
    }
}

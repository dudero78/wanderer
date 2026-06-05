using UnityEngine;

/// <summary>
/// Applica gli uniform di COLORE / MARE di una <see cref="PlanetRecipe"/> a un Material.
///
/// Perché esiste: lo STESSO blocco di SetColor/SetFloat era copiato a mano in quattro punti (PlanetBaker,
/// GpuPlanetRenderer, GpuPlanetSurface, PlanetEditor). È la fonte numero uno di bug "fantasma" — aggiungi un
/// colore alla ricetta, ne dimentichi una copia, e un corpo esce diverso SOLO in un percorso (in gioco sì, in
/// mappa no, o viceversa), difficilissimo da diagnosticare. Qui la verità del colore è in UN posto solo.
///
/// I percorsi differiscono SOLO negli extra del mare (sono trade-off VOLUTI, non incoerenze da unificare):
///  - bake CPU (shader PlanetBaked): niente liquido né trasparenza (è opaco, non ha la profondità
///    per-vertice) → liquid=false, transparency=false;
///  - anteprima CPU dell'editor (PushColors, shader PlanetBaked): liquido sì, trasparenza no → liquid=true,
///    transparency=false;
///  - resa GPU (gioco + anteprima GPU): liquido + clear + clarity → liquid=true, transparency=true.
/// I due flag riproducono ESATTAMENTE il comportamento storico di ogni sito: a parità di chiamata, nulla cambia.
/// </summary>
public static class PlanetRecipeUniforms
{
    /// <summary>Suolo + maria + saturazione: il blocco identico su OGNI percorso.</summary>
    public static void ApplyColor(Material mat, PlanetRecipe rec)
    {
        if (mat == null || rec == null) return;
        mat.SetColor("_SoilMean", rec.soilMean);
        mat.SetColor("_MariaColor", rec.mariaColor);
        mat.SetFloat("_MariaScale", rec.mariaScale);
        mat.SetFloat("_MariaStr", rec.mariaStrength);
        mat.SetFloat("_Saturation", rec.saturation);
    }

    /// <summary>Pelo del mare (l'ultimo mare attivo = <see cref="PlanetRecipe.LastSea"/>), oppure _SeaOn=0 se
    /// il corpo è asciutto. I flag liquid/transparency aggiungono gli uniform che solo certi percorsi settavano
    /// (vedi nota di classe): chi passa false lascia quegli uniform al loro valore, come faceva prima.</summary>
    public static void ApplySea(Material mat, PlanetRecipe rec, ProcessStep sea, bool liquid, bool transparency)
    {
        if (mat == null || rec == null) return;
        if (sea == null) { mat.SetFloat("_SeaOn", 0f); return; }
        mat.SetFloat("_SeaOn", 1f);
        mat.SetFloat("_SeaLevel", rec.baseRadius + sea.seaLevel);
        mat.SetColor("_SeaColor", sea.seaColor);
        mat.SetFloat("_SeaSat", sea.seaSaturation);
        mat.SetFloat("_SeaRough", sea.seaRoughness);
        mat.SetFloat("_SeaRoughScale", sea.seaRoughScale);
        mat.SetFloat("_SeaForma", sea.seaForma);
        mat.SetFloat("_SeaSeed", sea.seed);
        if (liquid) mat.SetFloat("_SeaLiquid", sea.liquid ? 1f : 0f);
        if (transparency)
        {
            mat.SetFloat("_SeaClear", sea.seaClear ? 1f : 0f);
            mat.SetFloat("_SeaClarity", sea.seaClarity);
        }
    }
}

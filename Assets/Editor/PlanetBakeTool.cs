using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Comando editor "Wanderer/Bake planet assets": bakea UNA volta, OFFLINE e a piena risoluzione delle mesh
/// d'appoggio (mask 256, crateri 200 — qui non incidono su load né performance), le texture di superficie del
/// pianeta e le salva come asset in Assets/Resources/BakedPlanet. A runtime PlanetBaker.TryLoadBakedMaterials
/// le carica → avvio quasi istantaneo (niente ~1.9s di bake GPU), qualità massima.
///
/// È OPT-IN e sicuro: finché non lanci questo comando la cartella non esiste e il gioco usa il bake runtime
/// (comportamento attuale). Per tornare indietro: cancella la cartella Assets/Resources/BakedPlanet.
/// Ri-lancialo se cambi i parametri del terreno in PlanetPresets (gli asset diventano altrimenti obsoleti).
/// </summary>
public static class PlanetBakeTool
{
    const int MaskMeshRes = 256;     // mesh d'appoggio ALTA per il bake offline (qualità massima)
    const int CraterMeshRes = 200;

    [MenuItem("Wanderer/Bake planet assets")]
    public static void BakePlanetAssets()
    {
        if (Shader.Find("Wanderer/PlanetBaked") == null || Shader.Find("Wanderer/CraterNormalBake") == null)
        {
            Debug.LogError("Bake planet assets: shader Wanderer/* non trovati. Annullo.");
            return;
        }

        // pianeta temporaneo configurato come in gioco (preset condiviso): stesso terreno → bake coerente.
        var go = new GameObject("__planetBakeTemp");
        try
        {
            var terrain = go.AddComponent<PlanetTerrain>();
            PlanetPresets.ConfigureDemoPlanet(terrain);
            terrain.RebuildLayers();

            // cartella pulita
            string dir = "Assets/Resources/" + PlanetBaker.BakedDir;
            if (AssetDatabase.IsValidFolder(dir)) AssetDatabase.DeleteAsset(dir);
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            AssetDatabase.CreateFolder("Assets/Resources", PlanetBaker.BakedDir);

            var maskMat = PlanetBaker.CreateMaskMaterial();
            var craterMat = PlanetBaker.CreateCraterMaterial(terrain);

            // detail normal (condivisa, tileable)
            var detailRT = PlanetBaker.BakeDetailNormalRT(PlanetBaker.DetailRtSize);
            SaveRtAsAsset(detailRT, dir + "/Detail.asset", TextureWrapMode.Repeat, FilterMode.Trilinear, 4);

            for (int f = 0; f < 6; f++)
            {
                EditorUtility.DisplayProgressBar("Bake planet assets", "Faccia " + (f + 1) + "/6", f / 6f);
                var maskRT = PlanetBaker.BakeMaskRT(terrain, f, MaskMeshRes, maskMat);
                SaveRtAsAsset(maskRT, dir + "/Mask" + f + ".asset", TextureWrapMode.Clamp, FilterMode.Bilinear, 1);
                var craterRT = PlanetBaker.BakeCraterNormalRT(terrain, f, PlanetBaker.CraterRtSize, craterMat, CraterMeshRes);
                SaveRtAsAsset(craterRT, dir + "/Crater" + f + ".asset", TextureWrapMode.Clamp, FilterMode.Trilinear, 4);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Bake planet assets: FATTO → " + dir + " (13 texture). A runtime verranno caricate da disco.");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Object.DestroyImmediate(go);
        }
    }

    /// <summary>Legge una RenderTexture in una Texture2D (lineare, con mipmap) e la salva come asset.</summary>
    static void SaveRtAsAsset(RenderTexture rt, string path, TextureWrapMode wrap, FilterMode filter, int aniso)
    {
        if (rt == null) return;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, true, true);   // mipChain + linear (dati/normali)
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.wrapMode = wrap;
        tex.filterMode = filter;
        tex.anisoLevel = aniso;
        tex.Apply(true);     // genera i mipmap
        RenderTexture.active = prev;
        rt.Release();
        AssetDatabase.CreateAsset(tex, path);
    }
}

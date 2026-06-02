using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Comando editor "Wanderer/Bake planet assets": bakea UNA volta, OFFLINE, le texture di superficie del
/// pianeta e le salva come asset in Assets/Resources/BakedPlanet. A runtime PlanetBaker.TryLoadBakedMaterials
/// le carica → avvio quasi istantaneo (niente ~1.9s di bake GPU).
///   - mask + normale crateri + detail: come il bake runtime (mesh d'appoggio 64/48 — qualità identica).
///   - HEIGHTMAP (Stage 1 della rifondazione terreno): displacement float per faccia, MIP-MAPPATA, calcolata
///     in CPU dalla VERA PlanetTerrain.SampleHeight (niente duplicazione in HLSL → niente divergenza). È il
///     DATO macro su cui poggeranno CDLOD (geometria view-dependent) e la normale derivata. Vedi memoria
///     wanderer-terreno-strategia. Di per sé non cambia ancora il rendering (backbone).
///
/// È OPT-IN e sicuro: finché non lanci questo comando la cartella non esiste e il gioco usa il bake runtime.
/// Per tornare indietro: cancella la cartella. Ri-lancialo se cambi i parametri in PlanetPresets.
/// </summary>
public static class PlanetBakeTool
{
    const int MaskMeshRes = 64;     // = bake runtime (qualità identica; alzarle rovina i crateri, vedi storia)
    const int CraterMeshRes = 48;
    const int HeightRes = 1024;     // heightmap per faccia (~0.77 m/texel su 500 m): backbone per CDLOD

    [MenuItem("Wanderer/Bake planet assets")]
    public static void BakePlanetAssets()
    {
        if (Shader.Find("Wanderer/PlanetBaked") == null || Shader.Find("Wanderer/CraterNormalBake") == null)
        {
            Debug.LogError("Bake planet assets: shader Wanderer/* non trovati. Annullo.");
            return;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");

        // Un corpo per cartella. Il configuratore prepara il terreno (ricetta → pipeline) come in gioco:
        // stessa fonte di verità → bake coerente con ciò che il gioco renderizza a runtime.
        BakeBody(PlanetBaker.BakedDir, PlanetPresets.ConfigureDemoPlanet);
        BakeBody(GameBootstrap.CetraBakedDir, t => GameBootstrap.ApplyCetraRecipe(t));

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Bake planet assets: FATTO (pianeta + Cetra). Caricate da disco a runtime.");
    }

    /// <summary>Bakea tutte le texture di UN corpo (mask + crater normal + detail + heightmap) nella cartella data.
    /// 'configure' prepara il terreno (ricetta + RebuildLayers) come in gioco.</summary>
    static void BakeBody(string bakedDirName, System.Action<PlanetTerrain> configure)
    {
        var go = new GameObject("__planetBakeTemp");
        try
        {
            var terrain = go.AddComponent<PlanetTerrain>();
            configure(terrain);

            // cartella pulita
            string dir = "Assets/Resources/" + bakedDirName;
            if (AssetDatabase.IsValidFolder(dir)) AssetDatabase.DeleteAsset(dir);
            AssetDatabase.CreateFolder("Assets/Resources", bakedDirName);

            var maskMat = PlanetBaker.CreateMaskMaterial();
            var craterMat = PlanetBaker.CreateCraterMaterial(terrain);

            // detail normal (condivisa, tileable)
            var detailRT = PlanetBaker.BakeDetailNormalRT(PlanetBaker.DetailRtSize);
            SaveRtAsAsset(detailRT, dir + "/Detail.asset", TextureWrapMode.Repeat, FilterMode.Trilinear, 4);

            for (int f = 0; f < 6; f++)
            {
                EditorUtility.DisplayProgressBar("Bake " + bakedDirName, "Faccia " + (f + 1) + "/6", f / 6f);
                var maskRT = PlanetBaker.BakeMaskRT(terrain, f, MaskMeshRes, maskMat);
                SaveRtAsAsset(maskRT, dir + "/Mask" + f + ".asset", TextureWrapMode.Clamp, FilterMode.Bilinear, 1);
                var craterRT = PlanetBaker.BakeCraterNormalRT(terrain, f, PlanetBaker.CraterRtSize, craterMat, CraterMeshRes);
                SaveRtAsAsset(craterRT, dir + "/Crater" + f + ".asset", TextureWrapMode.Clamp, FilterMode.Trilinear, 4);
            }

            // HEIGHTMAP per faccia (Stage 1): displacement float mippato, dalla VERA SampleHeight (CPU, offline).
            for (int f = 0; f < 6; f++)
            {
                EditorUtility.DisplayProgressBar("Bake " + bakedDirName, "Heightmap faccia " + (f + 1) + "/6", 0.5f + f / 12f);
                SaveHeightmap(terrain, f, dir + "/Height" + f + ".asset");
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Object.DestroyImmediate(go);
        }
    }

    /// <summary>
    /// Bakea la HEIGHTMAP di una faccia: per ogni texel, displacement = SampleHeight(dir) − BaseRadius in metri,
    /// in una Texture2D RFloat MIP-MAPPATA. Calcolo in CPU dalla VERA pipeline del terreno (una sola verità,
    /// niente shader da tenere in sync). Parallel.For sulle righe: SampleHeight è thread-safe dopo RebuildLayers.
    /// I mipmap mediano l'altezza → LOD pronto (la normale e la geometria CDLOD la campioneranno alla scala giusta).
    /// </summary>
    static void SaveHeightmap(PlanetTerrain terrain, int face, string path)
    {
        int res = HeightRes;
        Vector3 up = PlanetMeshBuilder.FaceNormals[face];
        PlanetMeshBuilder.FaceAxes(up, out var axisA, out var axisB);
        float baseR = terrain.BaseRadius;
        var data = new float[res * res];
        Parallel.For(0, res, y =>
        {
            for (int x = 0; x < res; x++)
            {
                float tx = x / (float)(res - 1);
                float ty = y / (float)(res - 1);
                Vector3 dir = PlanetMeshBuilder.ParamToDir(up, axisA, axisB, tx, ty);
                data[y * res + x] = terrain.SampleHeight(dir) - baseR;   // displacement in metri
            }
        });
        var tex = new Texture2D(res, res, TextureFormat.RFloat, true, true)
        { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        tex.SetPixelData(data, 0);
        tex.Apply(true);   // genera i mipmap (media le altezze = LOD)
        AssetDatabase.CreateAsset(tex, path);
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

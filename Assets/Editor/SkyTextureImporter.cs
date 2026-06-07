using UnityEditor;
using UnityEngine;

/// <summary>
/// La texture della Via Lattea (Resources/Sky/MilkyWay) è 16384×8192 (layer "milkyway" NASA = solo velo diffuso, senza
/// stelle): senza questo, l'import di default di Unity la ridimensiona a 2048 → sgranata col telescopio. Qui forziamo i
/// 16384, i mipmap (niente shimmer da lontano), e il wrap in Ascensione Retta (Repeat su U).
/// </summary>
public class SkyTextureImporter : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        string p = assetPath.Replace('\\', '/');

        if (p.Contains("Resources/Sky/MilkyWay"))
        {
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.maxTextureSize = 16384;           // 16k (layer "milkyway" NASA): dust-lane nitidissime al telescopio
            ti.mipmapEnabled = true;
            ti.wrapModeU = TextureWrapMode.Repeat;
            ti.wrapModeV = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Trilinear;
            ti.sRGBTexture = true;
            // compressione di alta qualità (la banda è diffusa → artefatti trascurabili, VRAM molto più bassa di RGBA32)
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
        }
        else if (p.Contains("Resources/Sky/dso_atlas"))
        {
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.maxTextureSize = 8192;            // atlante 16×16 a 512px/tile = 8192²: piena risoluzione
            ti.mipmapEnabled = true;             // i DSO piccoli a schermo → mip contro lo shimmer (la vignetta nera fa da bordo anti-bleed)
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Trilinear;
            ti.sRGBTexture = true;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
        }
    }
}

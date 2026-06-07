using UnityEditor;
using UnityEngine;

/// <summary>
/// La texture della Via Lattea (Resources/Sky/MilkyWay) è 8192×4096: senza questo, l'import di default di Unity la
/// ridimensiona a 2048 (maxTextureSize) → si perderebbe la risoluzione (sgranata col telescopio). Qui forziamo gli
/// 8192, i mipmap (niente shimmer da lontano/piccola), e il wrap in Ascensione Retta (Repeat su U).
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
            ti.maxTextureSize = 8192;            // tieni la piena risoluzione (default sarebbe 2048)
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
            ti.maxTextureSize = 4096;            // atlante 16×16 a piena risoluzione
            ti.mipmapEnabled = true;             // i DSO piccoli a schermo → mip contro lo shimmer (la vignetta nera fa da bordo anti-bleed)
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Trilinear;
            ti.sRGBTexture = true;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
        }
    }
}

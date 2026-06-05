using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Prepara i materiali "freddi" delle facce del pianeta. Bakea UNA volta, per faccia, una maschera di
/// regioni minerali (Wanderer/PlanetBake) e la normale dei crateri (Wanderer/CraterNormalBake), più una
/// detail-normal map tileable condivisa. Lo shader di superficie (Wanderer/PlanetBaked) legge queste texture
/// invece di ricalcolare rumore procedurale a ogni frame.
///
/// Due strade:
///  - RUNTIME (BakeFaceMaterials): bakea su RenderTexture all'avvio (~1.9s GPU). Fallback sempre disponibile.
///  - DA DISCO (TryLoadBakedMaterials): se esistono gli asset bakeati offline in Resources/BakedPlanet (li
///    genera il comando editor "Wanderer/Bake planet assets"), li carica e basta → avvio quasi istantaneo,
///    e il bake offline gira a piena risoluzione delle mesh d'appoggio (qualità massima, non incide sul load).
///
/// Metodo di bake: TEXTURE-SPACE BAKING. La mesh della faccia viene disegnata usando le sue UV come
/// posizione (vertex shader di PlanetBake): la RenderTexture si riempie nei punti della mesh vera. Le UV
/// coprono [0,1]² piene su ogni faccia → niente buchi.
/// </summary>
public static class PlanetBaker
{
    public const string BakedDir = "BakedPlanet";   // sotto Resources/ : asset bakeati offline
    public const int MaskRtSize = 512;
    public const int CraterRtSize = 1024;
    public const int DetailRtSize = 1024;

    // ---- ENTRY POINTS -------------------------------------------------------------------------------

    /// <summary>Bake RUNTIME procedurale (fallback). maskMeshRes/craterMeshRes = risoluzione delle mesh
    /// d'appoggio del bake (basse a runtime per il load; alte nel bake offline). Ritorna null se gli shader
    /// mancano (→ il chiamante resta sul procedurale uniforme).</summary>
    public static Material[] BakeFaceMaterials(PlanetTerrain terrain, int maskMeshRes, int craterMeshRes = 48)
    {
        var sampleShader = Shader.Find("Wanderer/PlanetBaked");
        var maskMat = CreateMaskMaterial();
        if (sampleShader == null || maskMat == null)
        {
            Debug.LogWarning("PlanetBaker: shader di bake non trovati, niente superficie bakeata.");
            return null;
        }
        var craterMat = CreateCraterMaterial(terrain);
        var detailRT = BakeDetailNormalRT(DetailRtSize);
        var soil = Resources.Load<Texture2D>("Textures/soil_dirt");

        var mats = new Material[6];
        var prev = RenderTexture.active;
        for (int f = 0; f < 6; f++)
        {
            var maskRT = BakeMaskRT(terrain, f, maskMeshRes, maskMat);
            RenderTexture craterRT = craterMat != null ? BakeCraterNormalRT(terrain, f, CraterRtSize, craterMat, craterMeshRes) : null;
            mats[f] = BuildMaterial(terrain, maskRT, craterRT, detailRT, soil);
        }
        RenderTexture.active = prev;
        return mats;
    }

    /// <summary>Carica i materiali dagli asset bakeati offline (Resources/BakedPlanet). Ritorna null se il
    /// set è incompleto/assente → il chiamante usa il bake runtime. Così la feature è OPT-IN e non rompe nulla.</summary>
    public static Material[] TryLoadBakedMaterials(PlanetTerrain terrain, string bakedDir = BakedDir)
    {
        var sampleShader = Shader.Find("Wanderer/PlanetBaked");
        if (sampleShader == null) return null;
        var detail = Resources.Load<Texture2D>(bakedDir + "/Detail");
        var soil = Resources.Load<Texture2D>("Textures/soil_dirt");

        var mats = new Material[6];
        for (int f = 0; f < 6; f++)
        {
            var mask = Resources.Load<Texture2D>(bakedDir + "/Mask" + f);
            var crater = Resources.Load<Texture2D>(bakedDir + "/Crater" + f);
            if (mask == null || crater == null) return null;   // set incompleto → fallback al bake runtime
            mats[f] = BuildMaterial(terrain, mask, crater, detail, soil);
        }
        Debug.Log("[load] superficie da asset bakeati (Resources/" + bakedDir + ")");
        return mats;
    }

    // ---- MATERIALE ----------------------------------------------------------------------------------

    /// <summary>Materiale di superficie di una faccia, con le texture (RT a runtime o Texture2D da disco).</summary>
    static Material BuildMaterial(PlanetTerrain terrain, Texture maskTex, Texture craterTex, Texture detailTex, Texture soil)
    {
        var mat = new Material(Shader.Find("Wanderer/PlanetBaked"));
        mat.SetFloat("_BaseRadius", terrain.BaseRadius);
        mat.SetFloat("_Amplitude", terrain.Amplitude);
        // COLORE dalla ricetta (suolo + mari): senza questo il materiale resta sul grigio lunare di default dello
        // shader → un pianeta marziano uscirebbe grigio. L'editor li spingeva a mano (PushColors); qui è la stessa cosa
        // per il gioco. Vale sia per il bake runtime che per quello da disco (entrambi passano da qui).
        var rec = terrain.Recipe;
        if (rec != null)
        {
            // COLORE/MARE dalla ricetta (fonte unica: PlanetRecipeUniforms). Bake CPU = shader PlanetBaked,
            // opaco e senza profondità per-vertice → niente liquido né trasparenza (il pelo è solo tinta piatta).
            PlanetRecipeUniforms.ApplyColor(mat, rec);
            PlanetRecipeUniforms.ApplySea(mat, rec, rec.LastSea(), liquid: false, transparency: false);
        }
        if (maskTex != null) mat.SetTexture("_MaskMap", maskTex);
        if (detailTex != null) mat.SetTexture("_DetailNormal", detailTex);
        // texture del suolo: dalla ricetta se indicata (soil_dirt/red/rock), altrimenti quella passata.
        var soilTex = soil;
        if (rec != null && !string.IsNullOrEmpty(rec.soilTexture))
        {
            var t = Resources.Load<Texture2D>("Textures/" + rec.soilTexture);
            if (t != null) soilTex = t;
        }
        if (soilTex != null) mat.SetTexture("_SoilSand", soilTex);
        if (craterTex != null) mat.SetTexture("_CraterNormalMap", craterTex);
        // L'albedo equirect (_AlbedoMap/_AlbedoMapStr) resta una feature dello shader per i corpi con DATI REALI
        // o mappe autorate; di default OFF → si usa l'albedo procedurale (mari + variazione) dello shader.
        return mat;
    }

    // ---- MATERIALI DI BAKE (riusati da runtime e dal tool editor) ----------------------------------

    public static Material CreateMaskMaterial()
    {
        var sh = Shader.Find("Wanderer/PlanetBake");
        if (sh == null) return null;
        var m = new Material(sh);
        // frequenze delle maschere: DEVONO coincidere coi default dello shader di superficie o le regioni
        // non combaciano col resto. (_RoughFreq è bakeato ma per ora lo shader usa solo R.)
        m.SetFloat("_MineralFreq", 1.8f);
        m.SetFloat("_RoughFreq", 0.8f);
        return m;
    }

    public static Material CreateCraterMaterial(PlanetTerrain terrain)
    {
        var sh = Shader.Find("Wanderer/CraterNormalBake");
        if (sh == null) return null;
        // Parametri IDENTICI al campo geometrico → i bordi nitidi cadono esattamente sulle conche della mesh.
        // Le ottave sono LE STESSE della geometria (niente +2): le più fini sarebbero sub-texel e il gradiente
        // analitico per-pixel aliaserebbe in un pettine regolare (aliasing di campionamento, nessun clamp lo toglie).
        var m = new Material(sh);
        m.SetFloat("_BaseRadius", terrain.BaseRadius);

        // Se c'è una RICETTA (editor), il bake segue la sua pipeline di crateri PRIMARIA (la prima attiva) →
        // la normale è coerente con ciò che hai composto. Nessuna pipeline attiva → densità 0 (normale piatta:
        // niente crateri fantasma sulla sfera liscia di partenza). Senza ricetta usa i campi legacy della scena.
        if (terrain != null && terrain.Recipe != null)
        {
            terrain.Recipe.Normalize();
            var c = terrain.Recipe.PrimaryCrater();
            m.SetFloat("_CraterSeed", c != null ? c.seed : 0);
            m.SetFloat("_CraterOctaves", c != null ? c.octaves : 1);
            m.SetFloat("_CraterLargest", c != null ? c.largestRadius : 100f);
            m.SetFloat("_CraterDensity", c != null ? c.density : 0f);
            m.SetFloat("_CraterDepthRatio", c != null ? c.depthRatio : 0.2f);
            m.SetFloat("_CraterRimRatio", c != null ? c.rimRatio : 0.3f);
            m.SetFloat("_CraterRimSharp", c != null ? c.rimSharpness : 2f);
        }
        else
        {
            m.SetFloat("_CraterSeed", terrain.CraterSeed);
            m.SetFloat("_CraterOctaves", terrain.CraterOctaves);
            m.SetFloat("_CraterLargest", terrain.CraterLargestRadius);
            m.SetFloat("_CraterDensity", terrain.CraterDensity);
            m.SetFloat("_CraterDepthRatio", terrain.CraterDepthRatio);
            m.SetFloat("_CraterRimRatio", terrain.CraterRimRatio);
            m.SetFloat("_CraterRimSharp", terrain.CraterRimSharpness);
        }
        // forza normale crateri: troppo alta (≥~0.9) e i piccoli sotto luce radente sembrano "cromati". 0.7 = bordi
        // leggibili senza effetto metallico.
        m.SetFloat("_CraterNormalStr", 0.7f);
        return m;
    }

    // ---- BAKE DELLE RENDERTEXTURE (per faccia) -----------------------------------------------------

    /// <summary>Maschera regioni minerali (R), bassa frequenza. La mesh d'appoggio (meshRes) copre le UV.</summary>
    public static RenderTexture BakeMaskRT(PlanetTerrain terrain, int face, int meshRes, Material maskMat)
    {
        var bakeMesh = PlanetMeshBuilder.BuildFaceMesh(PlanetMeshBuilder.FaceNormals[face], terrain, meshRes);
        var rt = new RenderTexture(MaskRtSize, MaskRtSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
        {
            name = "MaskBake_" + face,
            useMipMap = true,
            autoGenerateMips = false,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        rt.Create();

        var cb = new CommandBuffer { name = "PlanetBake_" + face };
        cb.SetRenderTarget(rt);
        cb.ClearRenderTarget(true, true, Color.clear);
        cb.DrawMesh(bakeMesh, Matrix4x4.identity, maskMat, 0, 0);
        Graphics.ExecuteCommandBuffer(cb);
        cb.Release();
        rt.GenerateMips();
        Object.DestroyImmediate(bakeMesh);
        return rt;
    }

    /// <summary>NORMALE dei crateri di UNA faccia (alta freq world-fixed), mippata. La mesh d'appoggio
    /// (meshRes) dà solo il frame tangente liscio; il fragment calcola la normale per texel a piena RT.</summary>
    public static RenderTexture BakeCraterNormalRT(PlanetTerrain terrain, int face, int size, Material craterMat, int meshRes)
    {
        var mesh = PlanetMeshBuilder.BuildFaceMesh(PlanetMeshBuilder.FaceNormals[face], terrain, meshRes);
        var rt = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
        {
            name = "CraterNormalRT_" + face,
            useMipMap = true,
            autoGenerateMips = false,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4
        };
        rt.Create();

        var cb = new CommandBuffer { name = "CraterNormalBake_" + face };
        cb.SetRenderTarget(rt);
        cb.ClearRenderTarget(true, true, new Color(0.5f, 0.5f, 1f, 1f));   // normale piatta di default
        cb.DrawMesh(mesh, Matrix4x4.identity, craterMat, 0, 0);
        Graphics.ExecuteCommandBuffer(cb);
        cb.Release();
        rt.GenerateMips();
        Object.DestroyImmediate(mesh);
        return rt;
    }

    /// <summary>Detail normal map tileable della grana del suolo (wrap Repeat, mippata). null se manca lo shader.</summary>
    public static RenderTexture BakeDetailNormalRT(int size)
    {
        var shader = Shader.Find("Wanderer/DetailNormalBake");
        if (shader == null) { Debug.LogWarning("PlanetBaker: shader detail-normal non trovato, suolo senza grana."); return null; }
        // foto della terra bruna strutturata: lo shader ne deriva il RILIEVO (non il colore: le ombre cotte
        // nella foto illuminerebbero il lato in ombra). Strutturata, non rumore uniforme → niente sparkle/moiré.
        var source = Resources.Load<Texture2D>("Textures/soil_dirt");
        if (source == null) { Debug.LogWarning("PlanetBaker: Textures/soil_dirt non trovata, suolo senza grana."); return null; }
        var mat = new Material(shader);
        mat.SetTexture("_Source", source);
        var rt = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32)
        {
            name = "DetailNormalRT",
            useMipMap = true,
            autoGenerateMips = false,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4
        };
        rt.Create();

        var cb = new CommandBuffer { name = "DetailNormalBake" };
        cb.SetRenderTarget(rt);
        cb.ClearRenderTarget(true, true, Color.clear);
        cb.Blit(null, rt, mat);
        Graphics.ExecuteCommandBuffer(cb);
        cb.Release();
        rt.GenerateMips();
        return rt;
    }
}

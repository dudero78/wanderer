using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Prepara i materiali "freddi" delle facce del pianeta per il quadtree. Bakea UNA volta, per
/// faccia, una maschera di regioni minerali (Wanderer/PlanetBake) e, condivisa fra tutte le facce,
/// una detail-normal map tileable della grana del suolo. Lo shader di superficie (Wanderer/PlanetBaked)
/// legge queste texture invece di ricalcolare rumore procedurale a ogni frame.
///
/// Metodo: TEXTURE-SPACE BAKING. Disegniamo la mesh della faccia usando le sue UV come posizione
/// (lo fa il vertex shader di PlanetBake): la RenderTexture si riempie col valore calcolato
/// esattamente nei punti della mesh vera. Le UV coprono [0,1]² piene su ogni faccia → niente buchi.
/// È lo stesso schema d'indirizzamento del virtual texturing: per pianeti enormi basterà bakeare
/// tessere attorno alla camera invece di facce intere, senza cambiare l'approccio.
/// </summary>
public static class PlanetBaker
{
    /// <summary>
    /// Costruisce 6 mesh-faccia temporanee solo per bakeare la maschera minerale in texture, e
    /// RITORNA i 6 materiali (uno per faccia, ordine di PlanetMeshBuilder.FaceNormals). Le mesh
    /// temporanee vengono distrutte: le texture vivono nelle RenderTexture. Il quadtree renderizza
    /// con questi materiali. Ritorna null su qualsiasi problema (→ il chiamante resta sul procedurale).
    /// </summary>
    public static Material[] BakeFaceMaterials(PlanetTerrain terrain, int bakeMeshRes)
    {
        var bakeShader = Shader.Find("Wanderer/PlanetBake");
        var sampleShader = Shader.Find("Wanderer/PlanetBaked");
        if (bakeShader == null || sampleShader == null)
        {
            Debug.LogWarning("PlanetBaker: shader di bake non trovati, niente quadtree bakeato.");
            return null;
        }

        var bakeMat = new Material(bakeShader);
        // frequenze delle maschere: DEVONO coincidere coi default dello shader di superficie o le
        // regioni non combaciano col resto. (_RoughFreq è bakeato ma per ora lo shader usa solo R.)
        bakeMat.SetFloat("_MineralFreq", 1.8f);
        bakeMat.SetFloat("_RoughFreq", 0.8f);

        // detail normal map tileable della grana del suolo: UNA, condivisa da tutte le facce. Con
        // mipmap+aniso non aliasa (in lontananza si media a piatto); il wrap Repeat la rende ripetibile.
        RenderTexture detailRT = BakeDetailNormal(1024);

        // materiale per il bake della NORMALE dei crateri (alta freq world-fixed). Parametri
        // IDENTICI al campo geometrico → i bordi nitidi cadono esattamente sulle conche della mesh.
        // Le ottave sono LE STESSE della geometria (niente +2): le ottave più fini sarebbero
        // sub-texel sulla RT (rim < ~Nyquist) e il gradiente analitico per-pixel aliaserebbe in un
        // pettine regolare — aliasing di campionamento, che nessun clamp toglie. Inoltre non esistono
        // nemmeno nella geometria, quindi disegnerebbero rilievo fantasma.
        var craterShader = Shader.Find("Wanderer/CraterNormalBake");
        Material craterMat = null;
        if (craterShader != null)
        {
            craterMat = new Material(craterShader);
            craterMat.SetFloat("_BaseRadius", terrain.BaseRadius);
            craterMat.SetFloat("_CraterSeed", terrain.CraterSeed);
            craterMat.SetFloat("_CraterOctaves", terrain.CraterOctaves);
            craterMat.SetFloat("_CraterLargest", terrain.CraterLargestRadius);
            craterMat.SetFloat("_CraterDensity", terrain.CraterDensity);
            craterMat.SetFloat("_CraterDepthRatio", terrain.CraterDepthRatio);
            craterMat.SetFloat("_CraterRimRatio", terrain.CraterRimRatio);
            // forza della normale dei crateri: troppo alta (≥~0.9) e i crateri PICCOLI sotto luce
            // radente sembrano "cromati"/metallici (normali ripide che ribaltano la luce). 0.7 è il
            // compromesso: bordi leggibili senza effetto metallico.
            craterMat.SetFloat("_CraterNormalStr", 0.7f);
        }

        // foto del suolo STRUTTURATA (terra bruna coi sassi): da' la grana fine e la variazione macro.
        // Strutturata apposta: una grana uniforme (asfalto) letta da vicino legge come "neve TV".
        var soil = Resources.Load<Texture2D>("Textures/soil_dirt");

        var mats = new Material[6];
        var prev = RenderTexture.active;
        for (int f = 0; f < 6; f++)
        {
            // mesh-faccia temporanea, solo per disegnare la maschera in spazio texture
            var bakeMesh = PlanetMeshBuilder.BuildFaceMesh(PlanetMeshBuilder.FaceNormals[f], terrain, bakeMeshRes);

            // maschera regioni minerali (R): bassa frequenza → RT piccola ARGB32 lineare. Una lettura
            // al posto di rumore procedurale per-pixel: meno lavoro per pixel.
            var maskRT = new RenderTexture(512, 512, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "MaskBake_" + f,
                useMipMap = true,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            maskRT.Create();

            var cb = new CommandBuffer { name = "PlanetBake_" + f };
            cb.SetRenderTarget(maskRT);
            cb.ClearRenderTarget(true, true, Color.clear);
            cb.DrawMesh(bakeMesh, Matrix4x4.identity, bakeMat, 0, 0);   // pass 0: maschere
            Graphics.ExecuteCommandBuffer(cb);
            cb.Release();
            maskRT.GenerateMips();
            Object.Destroy(bakeMesh);   // la texture è fatta: la mesh non serve più

            var mat = new Material(sampleShader);
            mat.SetFloat("_BaseRadius", terrain.BaseRadius);
            mat.SetFloat("_Amplitude", terrain.Amplitude);
            mat.SetTexture("_MaskMap", maskRT);
            if (detailRT != null) mat.SetTexture("_DetailNormal", detailRT);
            if (soil != null) mat.SetTexture("_SoilSand", soil);
            if (craterMat != null)
            {
                var craterRT = BakeCraterNormal(terrain, f, 1024, craterMat);
                mat.SetTexture("_CraterNormalMap", craterRT);
            }
            mats[f] = mat;
        }
        RenderTexture.active = prev;
        return mats;
    }

    /// <summary>
    /// Bakea la NORMALE dei crateri di UNA faccia in una RenderTexture mippata (alta freq world-fixed).
    /// La mesh-faccia (bassa risoluzione: serve solo il frame tangente liscio) viene disegnata in
    /// spazio texture; il fragment calcola la normale del cratere per texel a piena risoluzione della
    /// RT. Il MIPMAP poi la media in lontananza → niente sparkle. Clamp + trilinear + aniso.
    /// </summary>
    static RenderTexture BakeCraterNormal(PlanetTerrain terrain, int face, int size, Material craterMat)
    {
        // res bassa: la mesh dà solo il FRAME TANGENTE liscio interpolato per-pixel; la normale del cratere
        // la calcola il fragment a piena risoluzione → non serve campionare il terreno su tanti vertici.
        var mesh = PlanetMeshBuilder.BuildFaceMesh(PlanetMeshBuilder.FaceNormals[face], terrain, 48);
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
        Object.Destroy(mesh);
        return rt;
    }

    /// <summary>
    /// Genera la detail normal map tileable della grana del suolo in una RenderTexture con mipmap +
    /// wrap Repeat. Ritorna null se lo shader manca (le facce restano senza grana).
    /// </summary>
    static RenderTexture BakeDetailNormal(int size)
    {
        var shader = Shader.Find("Wanderer/DetailNormalBake");
        if (shader == null)
        {
            Debug.LogWarning("PlanetBaker: shader detail-normal non trovato, suolo senza grana.");
            return null;
        }
        // foto della terra bruna strutturata: da questa lo shader deriva il RILIEVO del micro-suolo
        // (non il colore: le ombre cotte nella foto illuminerebbero il lato in ombra). Strutturata,
        // non rumore uniforme → niente sparkle/moiré sotto luce forte.
        var source = Resources.Load<Texture2D>("Textures/soil_dirt");
        if (source == null)
        {
            Debug.LogWarning("PlanetBaker: Textures/soil_dirt non trovata, suolo senza grana.");
            return null;
        }
        var mat = new Material(shader);
        mat.SetTexture("_Source", source);
        var rt = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32)
        {
            name = "DetailNormalRT",
            useMipMap = true,
            autoGenerateMips = false,
            wrapMode = TextureWrapMode.Repeat,   // tileable: si ripete sul terreno senza cuciture
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4
        };
        rt.Create();

        var cb = new CommandBuffer { name = "DetailNormalBake" };
        cb.SetRenderTarget(rt);
        cb.ClearRenderTarget(true, true, Color.clear);
        cb.Blit(null, rt, mat);   // full-screen pass: il frag genera la grana tileable
        Graphics.ExecuteCommandBuffer(cb);
        cb.Release();
        rt.GenerateMips();
        return rt;
    }
}

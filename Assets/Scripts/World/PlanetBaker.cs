using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Bakea il rilievo procedurale del pianeta in una texture per faccia, UNA volta, e assegna a
/// ogni faccia un materiale "freddo" (Wanderer/PlanetBaked) che la legge invece di ricalcolare
/// il rumore a ogni frame. È il passo che toglie il calore: da "6 ottave di Perlin per pixel
/// per frame" a "una lettura di texture per pixel".
///
/// Metodo: TEXTURE-SPACE BAKING. Disegniamo la mesh della faccia usando le sue UV come
/// posizione (lo fa il vertex shader di PlanetBake): la RenderTexture si riempie col rilievo
/// calcolato esattamente nei punti della mesh vera. Coerenza totale con la forma del pianeta,
/// senza ricostruire coordinate (che sarebbe fragile). Le UV coprono [0,1]² piene su ogni
/// faccia, quindi la texture si riempie tutta: niente buchi.
///
/// Indirizzamento per-faccia-UV = lo stesso schema del virtual texturing/quadtree: quando
/// serviranno pianeti enormi, basterà bakeare tessere attorno alla camera invece di facce
/// intere — l'indirizzamento non cambia. Questa è la fondazione, non un vicolo cieco.
/// </summary>
public static class PlanetBaker
{
    /// <summary>
    /// Bakea tutte le facce figlie di 'planet'. Ritorna true se ha bakeato almeno una faccia.
    /// Su qualsiasi problema (shader mancante, formato non supportato) ritorna false senza
    /// toccare i materiali esistenti: il chiamante resta col percorso procedurale come fallback.
    /// </summary>
    public static bool Bake(Transform planet, float baseRadius, float amplitude, float baseFreq, int resolution)
    {
        var bakeShader = Shader.Find("Wanderer/PlanetBake");
        var sampleShader = Shader.Find("Wanderer/PlanetBaked");
        if (bakeShader == null || sampleShader == null)
        {
            Debug.LogWarning("PlanetBaker: shader di bake non trovati, resto sul procedurale.");
            return false;
        }
        // RGBAHalf serve per memorizzare valori con segno (valore + gradiente del rilievo).
        if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
        {
            Debug.LogWarning("PlanetBaker: RGBAHalf non supportato, resto sul procedurale.");
            return false;
        }

        var bakeMat = new Material(bakeShader);
        bakeMat.SetFloat("_BaseFreq", baseFreq);
        // tutte le 6 ottave del rilievo in texture: lo shader di superficie non calcola più
        // ottave procedurali (davano striature a luce radente). Il dettaglio sub-texel a contatto
        // lo fa la detail-normal della grana, non il rilievo.
        bakeMat.SetFloat("_BakeOct", 6.0f);

        // detail normal map tileable della grana del suolo: UNA, condivisa da tutte le facce.
        // Con mipmap+aniso non aliasa (in lontananza si media a piatto); il wrap Repeat la rende
        // ripetibile. Se manca lo shader, le facce restano senza grana ma il bake procede.
        RenderTexture detailRT = BakeDetailNormal(1024);

        // due foto di suolo STRUTTURATE per i biomi di colore (terra bruna coi sassi = base; terra
        // rossastra = zone calde minerali). Strutturate apposta: una foto a grana uniforme (asfalto)
        // letta da vicino legge come "neve TV", queste no. Caricate UNA volta, condivise. Se una
        // manca, il materiale resta col default "gray" dello shader (zona neutra): nessun crash.
        var soilBase = Resources.Load<Texture2D>("Textures/soil_dirt");   // base: terra bruna coi sassi
        var soilWarm = Resources.Load<Texture2D>("Textures/soil_red");    // zone calde: terra rossastra (Marte)

        int baked = 0;
        var prev = RenderTexture.active;
        for (int f = 0; f < planet.childCount; f++)
        {
            var face = planet.GetChild(f);
            var mf = face.GetComponent<MeshFilter>();
            var mr = face.GetComponent<MeshRenderer>();
            if (mf == null || mr == null || mf.sharedMesh == null) continue;

            var rt = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBHalf)
            {
                name = "ReliefBake_" + f,
                useMipMap = true,
                autoGenerateMips = false,   // generati a mano dopo il disegno
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8              // bordi netti a luce radente, a distanza: zero costo per-frame
            };
            rt.Create();

            // disegno immediato della mesh in spazio texture, indipendente dalla camera
            var cb = new CommandBuffer { name = "PlanetBake_" + f };
            cb.SetRenderTarget(rt);
            cb.ClearRenderTarget(true, true, Color.clear);
            cb.DrawMesh(mf.sharedMesh, Matrix4x4.identity, bakeMat, 0, 0);
            Graphics.ExecuteCommandBuffer(cb);
            cb.Release();
            rt.GenerateMips();

            var mat = new Material(sampleShader);
            mat.SetFloat("_BaseRadius", baseRadius);
            mat.SetFloat("_Amplitude", amplitude);
            mat.SetFloat("_BaseFreq", baseFreq);
            mat.SetTexture("_ReliefMap", rt);
            if (detailRT != null) mat.SetTexture("_DetailNormal", detailRT);
            if (soilBase != null) mat.SetTexture("_SoilSand", soilBase);   // slot "base"
            if (soilWarm != null) mat.SetTexture("_SoilDirt", soilWarm);   // slot "zone calde"
            mr.sharedMaterial = mat;
            baked++;
        }
        RenderTexture.active = prev;

        return baked > 0;
    }

    /// <summary>
    /// Come Bake, ma NON tocca la scena: costruisce 6 mesh-faccia temporanee solo per bakeare il
    /// rilievo in texture, e RITORNA i 6 materiali (uno per faccia, nello stesso ordine di
    /// PlanetMeshBuilder.FaceNormals). Le mesh temporanee vengono distrutte: la texture vive nella
    /// RenderTexture, indipendente dalla mesh. È la via per il quadtree, che renderizza con questi
    /// materiali invece di una mesh uniforme. Ritorna null su qualsiasi problema (→ fallback).
    /// </summary>
    public static Material[] BakeFaceMaterials(PlanetTerrain terrain, float baseFreq, int resolution, int bakeMeshRes)
    {
        var bakeShader = Shader.Find("Wanderer/PlanetBake");
        var sampleShader = Shader.Find("Wanderer/PlanetBaked");
        if (bakeShader == null || sampleShader == null)
        {
            Debug.LogWarning("PlanetBaker: shader di bake non trovati, niente quadtree bakeato.");
            return null;
        }
        if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
        {
            Debug.LogWarning("PlanetBaker: RGBAHalf non supportato, niente quadtree bakeato.");
            return null;
        }

        var bakeMat = new Material(bakeShader);
        bakeMat.SetFloat("_BaseFreq", baseFreq);
        bakeMat.SetFloat("_BakeOct", 6.0f);
        // frequenze delle maschere: DEVONO coincidere coi default dello shader di superficie
        // (_MineralFreq, _RoughFreq) o le zone non combaciano col resto.
        bakeMat.SetFloat("_MineralFreq", 1.8f);
        bakeMat.SetFloat("_RoughFreq", 0.8f);

        RenderTexture detailRT = BakeDetailNormal(1024);
        var soilBase = Resources.Load<Texture2D>("Textures/soil_dirt");   // base: terra bruna coi sassi
        var soilWarm = Resources.Load<Texture2D>("Textures/soil_red");    // zone calde: terra rossastra (Marte)

        var mats = new Material[6];
        var prev = RenderTexture.active;
        for (int f = 0; f < 6; f++)
        {
            // mesh-faccia temporanea, solo per disegnare il rilievo in spazio texture
            var bakeMesh = PlanetMeshBuilder.BuildFaceMesh(PlanetMeshBuilder.FaceNormals[f], terrain, bakeMeshRes);

            var rt = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBHalf)
            {
                name = "ReliefBake_" + f,
                useMipMap = true,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8
            };
            rt.Create();

            // maschera (minerali + rugosità): bassa frequenza → RT piccola ARGB32, sostituisce i
            // 2 rumori procedurali per-pixel dello shader di superficie (meno calore).
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
            cb.SetRenderTarget(rt);
            cb.ClearRenderTarget(true, true, Color.clear);
            cb.DrawMesh(bakeMesh, Matrix4x4.identity, bakeMat, 0, 0);   // pass 0: rilievo
            cb.SetRenderTarget(maskRT);
            cb.ClearRenderTarget(true, true, Color.clear);
            cb.DrawMesh(bakeMesh, Matrix4x4.identity, bakeMat, 0, 1);   // pass 1: maschere
            Graphics.ExecuteCommandBuffer(cb);
            cb.Release();
            rt.GenerateMips();
            maskRT.GenerateMips();
            Object.Destroy(bakeMesh);   // le texture sono fatte: la mesh non serve più

            var mat = new Material(sampleShader);
            mat.SetFloat("_BaseRadius", terrain.BaseRadius);
            mat.SetFloat("_Amplitude", terrain.Amplitude);
            mat.SetFloat("_BaseFreq", baseFreq);
            mat.SetTexture("_ReliefMap", rt);
            mat.SetTexture("_MaskMap", maskRT);
            if (detailRT != null) mat.SetTexture("_DetailNormal", detailRT);
            if (soilBase != null) mat.SetTexture("_SoilSand", soilBase);   // slot "base"
            if (soilWarm != null) mat.SetTexture("_SoilDirt", soilWarm);   // slot "zone calde"
            mats[f] = mat;
        }
        RenderTexture.active = prev;
        return mats;
    }

    /// <summary>
    /// Genera la detail normal map tileable della grana del suolo in una RenderTexture con
    /// mipmap + wrap Repeat. Ritorna null se lo shader manca (le facce restano senza grana).
    /// </summary>
    static RenderTexture BakeDetailNormal(int size)
    {
        var shader = Shader.Find("Wanderer/DetailNormalBake");
        if (shader == null)
        {
            Debug.LogWarning("PlanetBaker: shader detail-normal non trovato, suolo senza grana.");
            return null;
        }
        // foto della terra bruna strutturata (Resources/Textures/soil_dirt). Da questa lo shader
        // deriva il RILIEVO del micro-suolo (non il colore: le ombre cotte nella foto
        // illuminerebbero il lato in ombra). Strutturata, non rumore uniforme → niente sparkle/moiré
        // sotto luce forte (la grana d'asfalto generava normali ad alta frequenza che scintillavano).
        // Se manca, niente grana ma il resto procede.
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

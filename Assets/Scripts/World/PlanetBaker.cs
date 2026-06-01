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

        // tre foto di suolo per i biomi di colore (sabbia grigia base + fango oliva + terra
        // bruna): lo shader le mescola per zone larghe. Caricate UNA volta, condivise. Se una
        // manca, il materiale resta col default "gray" dello shader (zona neutra): nessun crash.
        var soilSand = Resources.Load<Texture2D>("Textures/soil_grain");
        var soilMud  = Resources.Load<Texture2D>("Textures/soil_mud");
        var soilDirt = Resources.Load<Texture2D>("Textures/soil_dirt");

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
            if (soilSand != null) mat.SetTexture("_SoilSand", soilSand);
            if (soilMud  != null) mat.SetTexture("_SoilMud",  soilMud);
            if (soilDirt != null) mat.SetTexture("_SoilDirt", soilDirt);
            mr.sharedMaterial = mat;
            baked++;
        }
        RenderTexture.active = prev;

        return baked > 0;
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
        // foto PBR di sabbia grigia (Resources/Textures/soil_grain). Da questa lo shader deriva
        // il RILIEVO della grana (non il colore: le ombre cotte nella foto illuminerebbero il
        // lato in ombra). Se manca, niente grana ma il resto procede.
        var source = Resources.Load<Texture2D>("Textures/soil_grain");
        if (source == null)
        {
            Debug.LogWarning("PlanetBaker: Textures/soil_grain non trovata, suolo senza grana.");
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
            anisoLevel = 8
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

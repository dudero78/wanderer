using UnityEngine;

/// <summary>
/// Anteprima GPU del pianeta dell'editor (percorso "GPU per l'editor", Tappe 1-3).
///
/// Genera la GEOMETRIA della superficie interamente sulla GPU (PlanetHeight.compute, gli stessi
/// SampleHeight/ParamToDir provati al millimetro dal walker) + le NORMALI ANALITICHE, e la DISEGNA
/// direttamente dai buffer con Graphics.RenderPrimitivesIndexed — SENZA mai leggere indietro su CPU (era
/// il blocco che "trascinava"). Lo shader Wanderer/PlanetProcedural indicizza i buffer con SV_VertexID e
/// calcola il COLORE nel fragment dalla ricetta (niente texture bakate). Niente mesh CPU di mezzo.
///
/// Convive con l'anteprima CPU (SingleMeshPlanet): l'editor commuta fra le due (tasto G) per il
/// confronto A/B. La parità della FORMA con la CPU è garantita da GpuShapeBuffers (unica fonte dei
/// parametri GPU). Le 6 facce si sovrappongono di una cella per coprire le cuciture agli spigoli del cubo.
///
/// Robustezza: senza supporto compute (o shader mancante) Ready resta false e l'editor tiene la CPU.
/// </summary>
public class GpuPlanetSurface : MonoBehaviour
{
    int res;                    // vertici interni per lato di ogni faccia
    int gp;                     // lato della griglia padded (res+2): un bordo per le normali
    int vertsPerFace;           // gp*gp
    int indexCount;

    ComputeShader cs;
    int kFace, kNorm, kIdx;
    GraphicsBuffer posBuf, nrmBuf, bedNrmBuf, depthBuf, surfBuf, idxBuf;   // posizioni/normali pelo (3 float/v) + normale fondo (3 float/v) + profondità acqua (1 float/v) + pelo mare (1 float/v) + index buffer
    // index buffer in CACHE per lato-griglia (gp): la topologia dipende solo dalla risoluzione e l'array a
    // 2048 è enorme (~600 MB) → costruirlo UNA volta per livello, poi riusarlo (niente scatti ripetuti sullo zoom).
    readonly System.Collections.Generic.Dictionary<int, GraphicsBuffer> idxCache = new System.Collections.Generic.Dictionary<int, GraphicsBuffer>();
    readonly System.Collections.Generic.Dictionary<int, int> idxCountCache = new System.Collections.Generic.Dictionary<int, int>();
    GpuShapeBuffers shape;                    // base + pipeline ordinata (crateri/mare/tettonica)
    Material mat;
    Bounds bounds;

    public bool Ready { get; private set; }
    /// <summary>Quando true, Update disegna la superficie GPU. L'editor lo accende/spegne col toggle.</summary>
    public bool Active { get; set; }

    /// <summary>Vertici interni per lato di ogni faccia (la "risoluzione" dell'anteprima). Lo slider "Dettaglio
    /// anteprima" dell'editor la cambia a runtime con <see cref="SetResolution"/>.</summary>
    public int Resolution => res;

    /// <summary>Alloca i buffer, costruisce l'index buffer (una volta) e fa il primo dispatch. res = vertici
    /// interni per lato di ogni faccia (la GPU ha margine: si può tenere alta).</summary>
    public void Setup(PlanetTerrain terrain, int res)
    {
        this.res = Mathf.Max(2, res);
        gp = this.res + 2;
        vertsPerFace = gp * gp;

        if (!SystemInfo.supportsComputeShaders) return;
        cs = Resources.Load<ComputeShader>("Shaders/PlanetHeightEditor");   // kernel editor (CSFaceGrid/Normals/Indices); core condiviso
        var sh = Shader.Find("Wanderer/PlanetProcedural");
        if (cs == null || sh == null)
        {
            Debug.LogWarning("GpuPlanetSurface: compute o shader mancante → anteprima GPU non disponibile.");
            return;
        }
        kFace = cs.FindKernel("CSFaceGrid");
        kNorm = cs.FindKernel("CSFaceNormals");
        kIdx = cs.FindKernel("CSIndices");

        int totalVerts = vertsPerFace * 6;
        posBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts * 3, 4);
        nrmBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts * 3, 4);
        bedNrmBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts * 3, 4);
        depthBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts, 4);
        surfBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts, 4);
        cs.SetBuffer(kFace, "_VPos", posBuf);
        cs.SetBuffer(kFace, "_VDepth", depthBuf);
        cs.SetBuffer(kFace, "_VSurf", surfBuf);
        cs.SetBuffer(kNorm, "_VPos", posBuf);
        cs.SetBuffer(kNorm, "_VNrm", nrmBuf);
        cs.SetBuffer(kNorm, "_VBedNrm", bedNrmBuf);

        UseIndexBuffer();

        mat = new Material(sh);
        mat.SetBuffer("_VPos", posBuf);
        mat.SetBuffer("_VNrm", nrmBuf);
        mat.SetBuffer("_VBedNrm", bedNrmBuf);
        mat.SetBuffer("_VDepth", depthBuf);
        mat.SetBuffer("_VSurf", surfBuf);

        Rebuild(terrain);
        Ready = true;
    }

    /// <summary>Ricostruisce posizioni+normali sulla GPU dalla ricetta corrente (chiamato quando un edit si
    /// assesta). Cheap: è tutto sulla GPU, nessun readback.</summary>
    public void Rebuild(PlanetTerrain terrain)
    {
        if (cs == null) return;

        shape?.Dispose();
        // entrambi i kernel chiamano SampleHeight (CSFaceGrid = posizioni, CSFaceNormals = normali analitiche)
        shape = GpuShapeBuffers.Build(cs, terrain, new[] { kFace, kNorm });

        cs.SetInt("_R", res);
        cs.SetInt("_HasSea", terrain.Recipe != null && terrain.Recipe.LastSea() != null ? 1 : 0);
        int gFace = (gp + 7) / 8;
        int gNrm = (gp + 7) / 8;   // normali su TUTTA la griglia padded (incluso il bordo di sovrapposizione)
        for (int f = 0; f < 6; f++)
        {
            Vector3 up = PlanetMeshBuilder.FaceNormals[f];
            PlanetMeshBuilder.FaceAxes(up, out var axisA, out var axisB);
            cs.SetVector("_FaceUp", up);
            cs.SetVector("_AxisA", axisA);
            cs.SetVector("_AxisB", axisB);
            cs.SetInt("_FaceOffset", f * vertsPerFace);
            cs.Dispatch(kFace, gFace, gFace, 1);   // posizioni (incl. bordo di sovrapposizione)
            cs.Dispatch(kNorm, gNrm, gNrm, 1);     // normali analitiche (tutta la griglia padded)
        }

        float radius = terrain.Recipe != null ? terrain.Recipe.baseRadius : terrain.BaseRadius;
        bounds = new Bounds(Vector3.zero, Vector3.one * (radius * 2.5f));
        ApplyColor(terrain);
    }

    /// <summary>Aggiorna SOLO gli uniform di colore (edit di colore: niente rigenerazione della geometria).</summary>
    public void RefreshColor(PlanetTerrain terrain) { if (Ready) ApplyColor(terrain); }

    /// <summary>Cambia la risoluzione dell'anteprima a runtime (slider "Dettaglio anteprima"): rialloca i buffer
    /// e ricostruisce l'index buffer e la geometria. Da vicino una res più alta toglie la sfaccettatura; sulla GPU
    /// (largamente scarica) costa poco. Niente readback, resta tutto sulla GPU.</summary>
    public void SetResolution(int newRes, PlanetTerrain terrain)
    {
        if (cs == null || !Ready) return;
        newRes = Mathf.Clamp(newRes, 2, 4096);
        if (newRes == res) return;

        posBuf?.Release(); nrmBuf?.Release(); bedNrmBuf?.Release(); depthBuf?.Release(); surfBuf?.Release();   // l'index buffer NON si rilascia: è in cache (riusato per livello)
        res = newRes; gp = res + 2; vertsPerFace = gp * gp;

        int totalVerts = vertsPerFace * 6;
        posBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts * 3, 4);
        nrmBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts * 3, 4);
        bedNrmBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts * 3, 4);
        depthBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts, 4);
        surfBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts, 4);
        cs.SetBuffer(kFace, "_VPos", posBuf);
        cs.SetBuffer(kFace, "_VDepth", depthBuf);
        cs.SetBuffer(kFace, "_VSurf", surfBuf);
        cs.SetBuffer(kNorm, "_VPos", posBuf);
        cs.SetBuffer(kNorm, "_VNrm", nrmBuf);
        cs.SetBuffer(kNorm, "_VBedNrm", bedNrmBuf);
        UseIndexBuffer();
        mat.SetBuffer("_VPos", posBuf);
        mat.SetBuffer("_VNrm", nrmBuf);
        mat.SetBuffer("_VBedNrm", bedNrmBuf);
        mat.SetBuffer("_VDepth", depthBuf);
        mat.SetBuffer("_VSurf", surfBuf);

        Rebuild(terrain);
    }

    /// <summary>Aggiorna SOLO gli uniform di luce (direzione/colore del sole + ambiente) dalla scena. Chiamato
    /// ogni frame in modalità "luce libera" (il sole è agganciato alla camera): è leggero, niente colore/geometria.</summary>
    public void RefreshLighting()
    {
        if (mat == null) return;
        var sun = FindAnyObjectByType<Light>();
        Vector3 dir = sun != null ? -sun.transform.forward : Vector3.up;
        Color sc = sun != null ? sun.color * sun.intensity : Color.white;
        mat.SetVector("_SunDir", dir.normalized);
        mat.SetVector("_SunColor", new Vector4(sc.r, sc.g, sc.b, 1f));
        Color amb = RenderSettings.ambientLight;
        mat.SetVector("_Ambient", new Vector4(amb.r, amb.g, amb.b, 1f));
    }

    /// <summary>Passa allo shader la luce della scena + gli uniform di colore dalla RICETTA (stessa mappa di
    /// PlanetBaker.BuildMaterial: suolo/bacini/mare/saturazione). Gli altri (minerali/vette/macro/tinta) usano i
    /// default dello shader, come fa la CPU. Il colore è ricalcolato nel fragment, niente texture bakate.</summary>
    void ApplyColor(PlanetTerrain terrain)
    {
        if (mat == null) return;

        RefreshLighting();

        var rec = terrain.Recipe;
        mat.SetFloat("_BaseRadius", rec != null ? rec.baseRadius : terrain.BaseRadius);
        mat.SetFloat("_Amplitude", rec != null ? rec.amplitude : terrain.Amplitude);
        // forma base (per ricostruire la quota di base nel fragment: maria/vette la seguono, non i crateri)
        mat.SetFloat("_Frequency", rec != null ? rec.frequency : terrain.Frequency);
        mat.SetInt("_Octaves", rec != null ? rec.octaves : terrain.Octaves);
        mat.SetFloat("_Lacunarity", rec != null ? rec.lacunarity : terrain.Lacunarity);
        mat.SetFloat("_Gain", rec != null ? rec.gain : terrain.Gain);
        mat.SetInt("_Seed", rec != null ? rec.seed : terrain.Seed);
        if (rec != null)
        {
            mat.SetColor("_SoilMean", rec.soilMean);
            mat.SetColor("_MariaColor", rec.mariaColor);
            mat.SetFloat("_MariaScale", rec.mariaScale);
            mat.SetFloat("_MariaStr", rec.mariaStrength);
            mat.SetFloat("_Saturation", rec.saturation);
            var sea = rec.LastSea();   // ultimo mare attivo = il pelo finale (come PlanetBaker)
            if (sea != null)
            {
                mat.SetFloat("_SeaOn", 1f);
                mat.SetFloat("_SeaLevel", rec.baseRadius + sea.seaLevel);
                mat.SetColor("_SeaColor", sea.seaColor);
                mat.SetFloat("_SeaSat", sea.seaSaturation);
                mat.SetFloat("_SeaRough", sea.seaRoughness);
                mat.SetFloat("_SeaRoughScale", sea.seaRoughScale);
                mat.SetFloat("_SeaForma", sea.seaForma);
                mat.SetFloat("_SeaSeed", sea.seed);
                mat.SetFloat("_SeaLiquid", sea.liquid ? 1f : 0f);
                mat.SetFloat("_SeaClear", sea.seaClear ? 1f : 0f);
                mat.SetFloat("_SeaClarity", sea.seaClarity);
            }
            else mat.SetFloat("_SeaOn", 0f);
        }
    }

    /// <summary>Triangoli di TUTTA la griglia padded (gp×gp) di ogni faccia: i vertici di bordo estendono la
    /// faccia di una cella oltre [0,1] → le 6 facce si SOVRAPPONGONO di una cella, coprendo le micro-fessure agli
    /// spigoli del cubo (le zone sovrapposte sono geometria identica → coincidono, niente z-fighting). Costruito
    /// una volta PER LIVELLO di risoluzione (cache su gp): la topologia non cambia, solo le posizioni (sulla
    /// GPU) variano con la ricetta. Riusato quando lo zoom torna a un livello già costruito → niente scatti.</summary>
    void UseIndexBuffer()
    {
        if (idxCache.TryGetValue(gp, out var cached))
        {
            idxBuf = cached; indexCount = idxCountCache[gp];
            return;
        }
        int quadsPerFace = (gp - 1) * (gp - 1);
        int quadsTotal = quadsPerFace * 6;
        int count = quadsTotal * 6;
        // buffer Index + Structured: lo RIEMPIE il compute (kernel CSIndices, RWStructuredBuffer<int>), poi viene
        // usato come index buffer dal RenderPrimitivesIndexed. Niente array gestito né upload → niente scatto a 2048.
        var buf = new GraphicsBuffer(GraphicsBuffer.Target.Index | GraphicsBuffer.Target.Structured, count, 4);
        // dispatch 2D: a 2048 i gruppi su un solo asse supererebbero 65535 → si spalmano su (x,y)
        int totalGroups = (quadsTotal + 63) / 64;
        int gx = Mathf.Min(totalGroups, 32768);
        int gy = (totalGroups + gx - 1) / gx;
        cs.SetBuffer(kIdx, "_Indices", buf);
        cs.SetInt("_IdxGp", gp);
        cs.SetInt("_IdxVertsPerFace", vertsPerFace);
        cs.SetInt("_IdxQuadsPerFace", quadsPerFace);
        cs.SetInt("_IdxQuadsTotal", quadsTotal);
        cs.SetInt("_IdxThreadW", gx * 64);
        cs.Dispatch(kIdx, gx, gy, 1);
        idxCache[gp] = buf; idxCountCache[gp] = count;
        idxBuf = buf; indexCount = count;
    }

    void Update()
    {
        if (!Active || !Ready) return;
        // Dopo un DOMAIN RELOAD in Play (ricompilazione di script/shader durante una sessione attiva) i
        // GraphicsBuffer — non serializzabili — tornano NULL, mentre Ready/Active vengono ripristinati dal
        // backup: disegnare qui lancerebbe ArgumentNullException A OGNI FRAME (stallo + crash). Non disegnare
        // finché i buffer non sono validi (al prossimo Rebuild/Setup tornano buoni).
        if (mat == null || idxBuf == null || !idxBuf.IsValid() || posBuf == null || !posBuf.IsValid()) return;
        var rp = new RenderParams(mat) { worldBounds = bounds };
        Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Triangles, idxBuf, indexCount);
    }

    void OnDestroy()
    {
        posBuf?.Release(); nrmBuf?.Release(); bedNrmBuf?.Release(); depthBuf?.Release(); surfBuf?.Release();
        foreach (var b in idxCache.Values) b?.Release();   // idxBuf punta a una di queste: non rilasciarlo a parte
        idxCache.Clear(); idxCountCache.Clear();
        shape?.Dispose();
        if (mat != null) Destroy(mat);
    }
}

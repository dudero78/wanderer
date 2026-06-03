using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Resa della superficie di un pianeta IN GIOCO calcolata e disegnata SULLA GPU (percorso B1, Tappa 1).
///
/// La geometria (posizioni + normali analitiche + profondità acqua) è calcolata da PlanetHeight.compute —
/// gli stessi SampleHeight provati al millimetro dal walker — in un POOL di "fette": ogni fetta è la griglia
/// padded di un nodo. Si disegna con UN SOLO draw INDIRECT istanziato (Graphics.RenderPrimitivesIndexedIndirect):
/// niente Mesh Unity, niente upload sul main thread, niente readback, niente draw call per-nodo. La CPU per
/// frame fa solo: aggiornare la matrice oggetto→mondo (floating origin) + la luce, e lanciare 1 draw.
///
/// Tappa 1: NIENTE LOD ancora — il pool sono le 6 facce a risoluzione fissa (6 fette). Serve a provare la
/// pipeline end-to-end in gioco (pool → index di una fetta → indirezione istanza→fetta → args → 1 draw
/// indirect → shader Wanderer/PlanetSurfaceGPU col colore procedurale). La Tappa 2 aggancia il cervello del
/// quadtree: split → alloca/riempi una fetta, merge → libera, e cresce instanceCount. La struttura indirect
/// è già quella definitiva, così la Tappa 2 non riscrive il percorso di disegno.
///
/// Il walker NON dipende da questo (legge SampleHeight in modo analitico) → la collisione è intatta.
/// Robustezza: senza compute o senza shader, Ready resta false e SolarSystemSetup ripiega sul quadtree.
/// </summary>
public class GpuPlanetRenderer : MonoBehaviour
{
    int res;            // vertici interni per lato di ogni faccia (la "risoluzione" fissa, Tappa 1)
    int gp;             // lato della griglia padded (res+2): 1 bordo per normali + sovrapposizione cuciture
    int vertsPerSlab;   // gp*gp
    int slabCount;      // Tappa 1: 6 (le facce del cubo)
    int indexCountPerSlab;
    float radius;

    ComputeShader cs;
    int kFace, kNorm, kIdx;
    GraphicsBuffer posBuf, nrmBuf, bedNrmBuf, depthBuf;   // POOL: slabCount*vertsPerSlab vertici
    GraphicsBuffer idxBuf;                                // topologia di UNA fetta (condivisa da tutte le istanze)
    GraphicsBuffer slabOfInstance;                        // istanza → indice di fetta
    GraphicsBuffer argsBuf;                               // 1 comando indexed-indirect
    GpuShapeBuffers shape;
    Material mat;
    Transform planetTf;
    PlanetTerrain terrain;
    Bounds localBounds;

    public bool Ready { get; private set; }

    /// <summary>DIAGNOSI: 0 = resa normale · 1 = posizione radiale (geometria, OK) · 2 = normale di mondo (OK).
    /// Geometria e normali confermate → resa normale.</summary>
    public int debugMode = 0;

    /// <summary>Alloca i buffer, costruisce la geometria sulla GPU e prepara il draw indirect. res = vertici
    /// interni per lato di faccia (fisso in Tappa 1: la GPU ha margine, si tiene alto).</summary>
    public void Setup(PlanetTerrain terrain, int res)
    {
        this.terrain = terrain;
        this.planetTf = terrain.transform;
        this.res = Mathf.Max(2, res);
        gp = this.res + 2;
        vertsPerSlab = gp * gp;
        slabCount = 6;   // 6 facce del cubo (Tappa 1, niente LOD)

        if (!SystemInfo.supportsComputeShaders) return;
        cs = Resources.Load<ComputeShader>("Shaders/PlanetHeight");
        var sh = Shader.Find("Wanderer/PlanetSurfaceGPU");
        if (cs == null || sh == null)
        {
            Debug.LogWarning("GpuPlanetRenderer: compute o shader 'Wanderer/PlanetSurfaceGPU' mancante → superficie GPU non disponibile.");
            return;
        }
        kFace = cs.FindKernel("CSFaceGrid");
        kNorm = cs.FindKernel("CSFaceNormals");
        kIdx = cs.FindKernel("CSIndices");

        int totalVerts = vertsPerSlab * slabCount;
        posBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts * 3, 4);
        nrmBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts * 3, 4);
        bedNrmBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts * 3, 4);
        depthBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts, 4);
        cs.SetBuffer(kFace, "_VPos", posBuf);
        cs.SetBuffer(kFace, "_VDepth", depthBuf);
        cs.SetBuffer(kNorm, "_VPos", posBuf);
        cs.SetBuffer(kNorm, "_VNrm", nrmBuf);
        cs.SetBuffer(kNorm, "_VBedNrm", bedNrmBuf);

        BuildSlabIndexBuffer();
        BuildInstanceAndArgs();

        mat = new Material(sh);
        mat.SetBuffer("_VPos", posBuf);
        mat.SetBuffer("_VNrm", nrmBuf);
        mat.SetBuffer("_VBedNrm", bedNrmBuf);
        mat.SetBuffer("_VDepth", depthBuf);
        mat.SetBuffer("_SlabOfInstance", slabOfInstance);
        mat.SetInt("_VertsPerSlab", vertsPerSlab);

        Rebuild();
        Ready = true;
    }

    /// <summary>Index buffer della topologia di UNA fetta (gp×gp → quad → 6 indici), in [0, vertsPerSlab).
    /// L'offset di fetta lo aggiunge il vertex shader via SV_InstanceID → un solo index buffer per tutte le
    /// istanze. Riusa il kernel CSIndices trattando "1 faccia = 1 fetta" (baseV=0).</summary>
    void BuildSlabIndexBuffer()
    {
        int quadsPerSlab = (gp - 1) * (gp - 1);
        indexCountPerSlab = quadsPerSlab * 6;
        idxBuf = new GraphicsBuffer(GraphicsBuffer.Target.Index | GraphicsBuffer.Target.Structured, indexCountPerSlab, 4);
        int totalGroups = (quadsPerSlab + 63) / 64;
        int gx = Mathf.Min(totalGroups, 32768);
        int gy = (totalGroups + gx - 1) / gx;
        cs.SetBuffer(kIdx, "_Indices", idxBuf);
        cs.SetInt("_IdxGp", gp);
        cs.SetInt("_IdxVertsPerFace", 0);          // baseV = 0: indici di una sola fetta
        cs.SetInt("_IdxQuadsPerFace", quadsPerSlab);
        cs.SetInt("_IdxQuadsTotal", quadsPerSlab);
        cs.SetInt("_IdxThreadW", gx * 64);
        cs.Dispatch(kIdx, gx, gy, 1);
    }

    /// <summary>Buffer di indirezione (istanza → fetta) e comando indirect. Tappa 1: identità [0..5], tutte
    /// e 6 le fette visibili. Tappa 2: la lista visibile la riempirà il cervello del quadtree.</summary>
    void BuildInstanceAndArgs()
    {
        var slabs = new uint[slabCount];
        for (uint i = 0; i < slabCount; i++) slabs[i] = i;
        slabOfInstance = new GraphicsBuffer(GraphicsBuffer.Target.Structured, slabCount, 4);
        slabOfInstance.SetData(slabs);

        argsBuf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
        var cmd = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
        cmd[0].indexCountPerInstance = (uint)indexCountPerSlab;
        cmd[0].instanceCount = (uint)slabCount;
        cmd[0].startIndex = 0;
        cmd[0].baseVertexIndex = 0;
        cmd[0].startInstance = 0;
        argsBuf.SetData(cmd);
    }

    /// <summary>Calcola le 6 facce nel pool sulla GPU dalla ricetta corrente + imposta gli uniform di colore.</summary>
    void Rebuild()
    {
        if (cs == null) return;
        shape?.Dispose();
        shape = GpuShapeBuffers.Build(cs, terrain, new[] { kFace, kNorm });   // base + pipeline ordinata, parità col walker

        cs.SetInt("_R", res);
        cs.SetInt("_HasSea", terrain.Recipe != null && terrain.Recipe.LastSea() != null ? 1 : 0);
        int groups = (gp + 7) / 8;
        for (int f = 0; f < slabCount; f++)
        {
            Vector3 up = PlanetMeshBuilder.FaceNormals[f];
            PlanetMeshBuilder.FaceAxes(up, out var axisA, out var axisB);
            cs.SetVector("_FaceUp", up);
            cs.SetVector("_AxisA", axisA);
            cs.SetVector("_AxisB", axisB);
            cs.SetInt("_FaceOffset", f * vertsPerSlab);
            cs.Dispatch(kFace, groups, groups, 1);   // posizioni + profondità acqua
            cs.Dispatch(kNorm, groups, groups, 1);   // normali analitiche (pelo + fondo)
        }

        radius = terrain.Recipe != null ? terrain.Recipe.baseRadius : terrain.BaseRadius;
        localBounds = new Bounds(Vector3.zero, Vector3.one * (radius * 2.5f));
        ApplyColor();
    }

    /// <summary>Uniform di colore dalla RICETTA (stessa mappa di PlanetBaker/anteprima editor) + luce di scena.</summary>
    void ApplyColor()
    {
        if (mat == null) return;
        RefreshLighting();

        var rec = terrain.Recipe;
        mat.SetFloat("_BaseRadius", rec != null ? rec.baseRadius : terrain.BaseRadius);
        mat.SetFloat("_Amplitude", rec != null ? rec.amplitude : terrain.Amplitude);
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

    Light sun;          // cache: il SOLE (non esiste ancora quando Build crea i corpi → risolto pigro in Update)
    Light torch;        // cache: la torcia (spot del giocatore), risolta pigra

    /// <summary>Direzione/colore del sole + ambiente dalla scena. Leggero, chiamabile ogni frame.</summary>
    void RefreshLighting()
    {
        if (mat == null) return;
        // In gioco ci sono PIÙ luci (stella puntiforme, torcia spot, sole direzionale): prendere "una a caso"
        // (FindAnyObjectByType<Light>) becca spesso la torcia SPENTA → _SunColor≈0 → pianeta nero. Il sole è la
        // direzionale che porta il componente SunLight; fallback alla prima luce direzionale.
        if (sun == null)
        {
            if (SunLight.Instance != null) sun = SunLight.Instance.GetComponent<Light>();   // deterministico
            else { var sl = FindAnyObjectByType<SunLight>(); if (sl != null) sun = sl.GetComponent<Light>(); }
        }
        Vector3 dir = sun != null ? -sun.transform.forward : Vector3.up;
        Color sc = sun != null ? sun.color * sun.intensity : Color.white;
        mat.SetVector("_SunDir", dir.normalized);
        mat.SetVector("_SunColor", new Vector4(sc.r, sc.g, sc.b, 1f));
        Color amb = RenderSettings.ambientLight;
        mat.SetVector("_Ambient", new Vector4(amb.r, amb.g, amb.b, 1f));
    }

    /// <summary>Passa allo shader lo spot della TORCIA (luce manuale: lo shader GPU non riceve le luci di Unity).
    /// Quando spenta l'intensità è 0 → _TorchColor=0 → termine nullo. Pos/dir in mondo (figlia della camera).</summary>
    void RefreshTorch()
    {
        if (mat == null) return;
        if (torch == null)
        {
            var fl = FindAnyObjectByType<Flashlight>();
            if (fl != null) torch = fl.lamp;
        }
        if (torch == null) { mat.SetVector("_TorchColor", Vector4.zero); return; }

        Color tc = torch.color * torch.intensity;   // intensità 0 da spenta → contributo nullo
        mat.SetVector("_TorchPos", torch.transform.position);
        mat.SetVector("_TorchDir", torch.transform.forward);
        mat.SetVector("_TorchColor", new Vector4(tc.r, tc.g, tc.b, 1f));
        mat.SetFloat("_TorchRange", torch.range);
        float half = torch.spotAngle * 0.5f * Mathf.Deg2Rad;
        mat.SetFloat("_TorchCosOuter", Mathf.Cos(half));
        mat.SetFloat("_TorchCosInner", Mathf.Cos(half * 0.85f));
    }

    void Update()
    {
        if (!Ready) return;
        // Dopo un domain reload in Play i GraphicsBuffer (non serializzabili) tornano null mentre Ready viene
        // ripristinato: non disegnare finché non sono validi (al prossimo Setup tornano buoni).
        if (mat == null || posBuf == null || !posBuf.IsValid() || idxBuf == null || !idxBuf.IsValid() || argsBuf == null) return;

        // floating origin: il pianeta si muove ogni frame, le posizioni nel pool sono LOCALI → trasformo qui.
        mat.SetMatrix("_ObjectToWorld", planetTf.localToWorldMatrix);
        mat.SetFloat("_DebugView", debugMode);
        RefreshLighting();
        RefreshTorch();

        var worldBounds = new Bounds(planetTf.position, Vector3.one * (radius * 5f));
        var rp = new RenderParams(mat)
        {
            worldBounds = worldBounds,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false
        };
        Graphics.RenderPrimitivesIndexedIndirect(rp, MeshTopology.Triangles, idxBuf, argsBuf, 1);
    }

    void OnDestroy()
    {
        posBuf?.Release(); nrmBuf?.Release(); bedNrmBuf?.Release(); depthBuf?.Release();
        idxBuf?.Release(); slabOfInstance?.Release(); argsBuf?.Release();
        shape?.Dispose();
        if (mat != null) Destroy(mat);
    }
}

using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Resa della superficie di un pianeta IN GIOCO sulla GPU con LOD (percorso B1). ORCHESTRATORE: possiede il
/// ComputeShader e il materiale, pilota il quadtree (<see cref="PlanetLodTree"/>) ogni frame, riempie le fette del
/// pool (<see cref="SlabPool"/>) e fa UN <c>Graphics.RenderPrimitivesIndexedIndirect</c> con le foglie visibili —
/// niente Mesh Unity, niente upload, niente readback, niente draw call per-nodo.
///
/// Spaccato in tre (#18): <see cref="SlabPool"/> = memoria VRAM condivisa + bookkeeping degli slot ·
/// <see cref="PlanetLodTree"/> = topologia del quadtree + selezione LOD (CDLOD col morph continuo, crack-free) ·
/// questo = compute (implementa <see cref="ISlabFiller"/>) + materiale/draw + luce + gate di parità.
///
/// Il walker NON dipende da questo (legge SampleHeight analitico in 1 punto) → collisione intatta. La luce è
/// MANUALE (lo shader dai-buffer non riceve le luci di Unity): sole + torcia passati a mano. Robustezza: senza
/// compute o shader, Ready resta false e SolarSystemSetup ripiega sul quadtree.
/// </summary>
public class GpuPlanetRenderer : MonoBehaviour, ISlabFiller
{
    // --- manopole LOD (passate al PlanetLodTree al Setup) ---
    public float lodFactor = 3f; // suddivide se la camera è più vicina di worldSize·questo (più basso = meno nodi/fill;
                                 // NON tocca il dettaglio sotto i piedi, solo quanto lontano si estende)
    public float mergeHysteresis = 1f;   // CDLOD: confini di LOD NETTI (split e merge alla stessa soglia). Una banda morta
                                         // (>1) farebbe morfare i due lati di un confine a misure diverse → crepe.
    public int maxDepth = 6;
    public int maxSlabs = 1024;  // fette nel pool CONDIVISO fra i corpi (free + cache + visibili). Pre-allocate (~459 MB).
    public int splitBudget = 8;  // quante tessere nuove al MASSIMO preparare per fotogramma (×4 fill): spalmando il
                                 // lavoro su più fotogrammi si evita l'ondata che fa scattare. Il LOD predittivo copre il "ritardo"
    public float lookaheadTime = 0.7f;   // LOD PREDITTIVO: valuta lo split dalla posizione DOVE SARAI fra ~tot secondi
    public static bool UseGeomorph = true;   // GEOMORPH CDLOD nel vertex shader: transizioni LOD lisce. Statico (da GameBootstrap), toggle A/B
    public float morphRange = 0.5f;      // ampiezza della banda di morph (frazione di splitDist): 0.1 stretta, 0.9 larga
    // OVERDRAW: un solo draw col _Cull del materiale (deterministico). STATICI (impostati da GameBootstrap).
    public static bool CullSplit = true;
    public static int InteriorCull = 1;   // 1=Front: il verso dell'interno è Front-facing (Cull Back ribaltava tutto). Verificato in gioco
    // PBR per pendenza + GGX leggero (GPU-4): keyword _PBR_TERRAIN sul materiale. Statico (da GameBootstrap), A/B.
    public static bool UsePbrTerrain = true;

    // DIAGNOSI superficie (statico, pilotabile da GameBootstrap e dal menu in-game à):
    //   0 = off · 1 = posizione radiale (geometria pura) · 2 = normale di mondo (shading) ·
    //   3 = livello di LOD · 4 = faccia del cubo · 5 = fetta (ogni slab un colore).
    public static int DebugView = 0;
    int lastDebugView = -1;   // per accendere/spegnere la keyword PLANET_DEBUG_VIEW solo al cambio

    int nodeRes = 32;  // quad per lato di un nodo (33×33 vertici interni)
    int n;             // nodeRes+1 (vertici per lato)
    int vertsPerSlab;  // n*n (la sola griglia interna; niente skirt)
    int indexCountPerSlab;   // indici della griglia interna (nodeRes²·6) — è tutto, non ci sono più skirt
    float radius;

    SlabPool pool;        // memoria VRAM condivisa (#18)
    PlanetLodTree tree;   // quadtree + selezione LOD (#18)

    ComputeShader cs;
    int kSlab, kSlabBatch;
    // ID di proprietà cachati: SetX per NOME ri-hasha la stringa a ogni chiamata (×~600/frame nei fill); con
    // ID interi l'overhead per-chiamata crolla. _NN è costante → settato una volta nel Setup.
    static readonly int ID_FaceUp = Shader.PropertyToID("_FaceUp");
    static readonly int ID_AxisA = Shader.PropertyToID("_AxisA");
    static readonly int ID_AxisB = Shader.PropertyToID("_AxisB");
    static readonly int ID_U0 = Shader.PropertyToID("_U0");
    static readonly int ID_V0 = Shader.PropertyToID("_V0");
    static readonly int ID_Step = Shader.PropertyToID("_Step");
    static readonly int ID_NSlabOff = Shader.PropertyToID("_NSlabOff");

    GraphicsBuffer idxBuf;                 // topologia di UNA fetta (griglia interna), condivisa
    GraphicsBuffer slabOfInstance;         // istanza visibile → indice di fetta
    GraphicsBuffer splitDistOfInstance;    // geomorph: splitDist per istanza, parallelo a slabOfInstance
    GraphicsBuffer dirOfInstance;          // ANTI-SPUNTONE: direzione-centro del nodo per istanza (float4, w inutilizzato)
    GraphicsBuffer regionOfInstance;       // REGION-STAMP: id regione UINT atteso per istanza (confronto esatto col marchio della fetta → limite corpi via)
    GraphicsBuffer argsBuf;
    GraphicsBuffer.IndirectDrawIndexedArgs[] argsData = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
    GpuShapeBuffers shape;
    Material mat;        // superficie: _Cull = Front (CullSplit) o Off → dimezza l'overdraw del fragment in un draw solo
    Transform planetTf;
    PlanetTerrain terrain;

    Transform cam;
    Light sun, torch;
    bool torchSearched;   // FindAnyObjectByType<Flashlight> è caro: cercala UNA volta (lo spawn è sincrono in GameBootstrap.Start, prima del primo Update)
    bool uploadedOnce;    // i buffer per-istanza si ri-caricano solo quando la selezione cambia (vedi tree.SelectionChanged) → a camera ferma niente SetData
    int fillsThisFrame;   // diagnosi: quante fette riempite (dispatch) in questo frame
    long fillTicks;       // diagnosi: tempo CPU speso nelle chiamate dei fill (SetX+Dispatch), per separarlo dalla traversata
    // scomposizione CPU del frame (ms), SOMMATA su tutti i corpi, esposta all'HUD: collo del churn a colpo d'occhio
    public static float Trav, Fill, Send;
    static int sBreakdownFrame = -1;
    // PERF-2: la strumentazione PER-FILL (2 GetTimestamp per ogni fetta riempita, fino a splitBudget·4 al churn) gira
    // SOLO con Profile=true. Default false → fuori dal path più caldo in ship; accendila per diagnosticare il churn.
    public static bool Profile;

    // --- BATCH FILL (opt-in): accumula i fill del frame e li manda in UN dispatch invece di uno per nodo (taglia le
    // chiamate API). Si attiva solo se la VERIFICA di parità (batch↔per-nodo, vedi VerifyBatchFill) è verde; altrimenti
    // resta il path per-nodo (sicuro). R1: il batch corruppe la geometria una volta → ora è dietro al banco di verifica.
    public static bool UseBatchFill;   // acceso da GameBootstrap PRIMA del Setup (la verifica gira nel Setup)
    bool batchReady;             // true = verifica passata → uso il path batch
    GraphicsBuffer jobsBuf;      // parametri per-nodo del frame
    NodeJob[] jobScratch;        // CPU side, riempito durante la traversata, caricato una volta al flush
    int jobCount;
    struct NodeJob { public Vector4 faceUp, axisA, axisB, uv, misc; }   // = NodeJobGPU (uv: u0,v0,step,slabOff; misc.z: indice fetta, w: id regione)

    public bool Ready { get; private set; }

    public void Setup(PlanetTerrain terrain, int res)
    {
        this.terrain = terrain;
        this.planetTf = terrain.transform;
        // TETTO di nodeRes (#2 sez. B): la VRAM del pool cresce col QUADRATO di nodeRes. PARI obbligatorio: il geomorph
        // assume bordi pari; dispari → letture fuori-griglia. È un DIAL: 96=dettaglio pieno/più VRAM, 64=~4× meno.
        nodeRes = Mathf.Clamp(res, 4, 96) & ~1;
        n = nodeRes + 1;
        vertsPerSlab = n * n;   // CDLOD: niente skirt → solo la griglia interna (pool più piccolo, fill più leggero)

        if (!SystemInfo.supportsComputeShaders) return;
        // ISTANZA PROPRIA del compute: in scena ci sono più corpi, e il ComputeShader condiviso avrebbe binding di
        // buffer/uniform GLOBALI → i corpi si clobbererebbero a vicenda. Una copia per corpo = stato indipendente.
        var baseCs = Resources.Load<ComputeShader>("Shaders/PlanetHeight");
        cs = baseCs != null ? Instantiate(baseCs) : null;
        var sh = Shader.Find("Wanderer/PlanetSurfaceGPU");
        if (cs == null || sh == null)
        {
            Debug.LogWarning("GpuPlanetRenderer: compute o shader 'Wanderer/PlanetSurfaceGPU' mancante → superficie GPU non disponibile.");
            return;
        }
        kSlab = cs.FindKernel("CSNodeSlab");
        kSlabBatch = cs.FindKernel("CSNodeSlabBatch");

        pool = new SlabPool(maxSlabs, vertsPerSlab);   // alloca (o riusa) il pool CONDIVISO + bodyId
        if (pool.Mismatched) { pool.Release(); pool = null; return; }   // ARCH-1/R8: nodeRes divergente → Ready resta false → SolarSystemSetup ripiega sul quadtree
        jobsBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxSlabs, 5 * 16); // NodeJob = 5×float4 (per-corpo, ~82 KB)
        jobScratch = new NodeJob[maxSlabs];

        // i 2 kernel (per-nodo + batch) scrivono lo STESSO pool; il batch legge anche _Jobs
        foreach (int k in new[] { kSlab, kSlabBatch })
        {
            cs.SetBuffer(k, "_VPos", pool.Pos);
            cs.SetBuffer(k, "_VNrm", pool.Nrm);
            cs.SetBuffer(k, "_VBedNrm", pool.BedNrm);
            cs.SetBuffer(k, "_VDepth", pool.Depth);
            cs.SetBuffer(k, "_VField", pool.Field);
            cs.SetBuffer(k, "_VSurf", pool.Surf);
            cs.SetBuffer(k, "_VColor", pool.Color);   // colore per-vertice (GPU-1)
        }
        cs.SetBuffer(kSlabBatch, "_Jobs", jobsBuf);
        cs.SetBuffer(kSlab, "_SlabRegion", pool.Region);          // marchio di regione: lo scrive il fill dell'interno
        cs.SetBuffer(kSlabBatch, "_SlabRegion", pool.Region);
        shape = GpuShapeBuffers.Build(cs, terrain, new[] { kSlab, kSlabBatch });   // base+ricetta a entrambi
        cs.SetInt("_NN", n);                 // costante per i kernel dei fill (non più per-fill)

        BuildIndexBuffer();

        slabOfInstance = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxSlabs, 4);
        splitDistOfInstance = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxSlabs, 4);
        dirOfInstance = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxSlabs, 16);   // float4 (16B): evita la trappola Metal float3
        regionOfInstance = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxSlabs, 4);  // uint per istanza (region-stamp)
        argsBuf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);

        mat = new Material(sh);
        mat.SetBuffer("_VPos", pool.Pos);
        mat.SetBuffer("_VNrm", pool.Nrm);
        mat.SetBuffer("_VBedNrm", pool.BedNrm);
        mat.SetBuffer("_VDepth", pool.Depth);
        mat.SetBuffer("_VField", pool.Field);
        mat.SetBuffer("_VSurf", pool.Surf);
        mat.SetBuffer("_VColor", pool.Color);   // colore per-vertice (GPU-1): 3 fbm value-noise interpolati, niente più 6 vnoise/pixel
        mat.SetBuffer("_SlabOfInstance", slabOfInstance);
        mat.SetBuffer("_SplitDistOfInstance", splitDistOfInstance);
        mat.SetBuffer("_DirOfInstance", dirOfInstance);
        mat.SetBuffer("_RegionOfInstance", regionOfInstance);
        mat.SetBuffer("_SlabRegion", pool.Region);
        mat.SetInt("_VertsPerSlab", vertsPerSlab);
        mat.SetFloat("_PerVertexFields", 1f);   // in gioco: usa baseN per-vertice (fragment più economico)
        mat.SetFloat("_PerVertexColor", 1f);    // in gioco: usa i 3 fbm colore per-vertice (GPU-1) → 6 vnoise/pixel in meno
        mat.SetInt("_NN", n);                                  // geomorph: vertici per lato → (i,j) dal vid + lettura vicini
        mat.SetFloat("_MorphRange", morphRange);
        mat.SetFloat("_UseGeomorph", UseGeomorph ? 1f : 0f);

        radius = terrain.Recipe != null ? terrain.Recipe.baseRadius : terrain.BaseRadius;
        cs.SetInt("_HasSea", terrain.Recipe != null && terrain.Recipe.LastSea() != null ? 1 : 0);

        // SCALE dei 3 campi colore per-vertice (GPU-1) sul compute, PRIMA dei fill (tree.Build). Devono combaciare
        // ESATTAMENTE con quelle del fragment: _MacroScale/_MineralScale sono costanti (default Properties = 5 / 1.8,
        // anche sul materiale), _MariaScale viene dalla ricetta (= ApplyColor → mat._MariaScale) o dal default 2.2.
        cs.SetFloat("_MacroScale", 5f);
        cs.SetFloat("_MineralScale", 1.8f);
        cs.SetFloat("_MariaScale", terrain.Recipe != null ? terrain.Recipe.mariaScale : 2.2f);

        // il quadtree: pilota split/merge e raccoglie le foglie visibili; mi richiama (ISlabFiller) per riempire le fette
        tree = new PlanetLodTree(terrain, radius, maxSlabs, pool, this,
                                 lodFactor, mergeHysteresis, maxDepth, splitBudget, lookaheadTime);
        tree.Build();   // 6 facce-radice, ciascuna con la sua fetta riempita (per-nodo: batchReady ancora false)

        // BATCH FILL: lo accendo solo se la verifica di parità (batch↔per-nodo, sui 6 root) è verde. Altrimenti
        // resto sul per-nodo (sicuro). Vedi R1: il batch corruppe la geometria una volta → niente fede cieca.
        if (UseBatchFill)
        {
            batchReady = VerifyBatchFill();
            if (!batchReady) Debug.LogWarning($"{terrain.name}: batch fill NON attivato (parità fallita) → resto sul per-nodo.");
        }

        VerifyParityRuntime();   // #9: gate non bloccante CPU↔GPU (un readback dei root, una volta sola)
        ApplyColor();

        Ready = true;
    }

    /// <summary>GATE DI PARITÀ a runtime (#9): all'avvio del corpo confronta la geometria che il renderer ha
    /// DAVVERO prodotto sulla GPU (i 6 root, già riempiti in Pos) con SampleHeight della CPU sulle stesse direzioni.
    /// È la verifica più diretta della rete CPU↔GPU: la mesh la fa la GPU (PlanetHeightCore.hlsl), la collisione del
    /// walker la fa la CPU (PlanetTerrain.SampleHeight). Non bloccante: solo un LogError, un readback per corpo.</summary>
    void VerifyParityRuntime()
    {
        try
        {
            if (pool == null || pool.Pos == null || !pool.Pos.IsValid() || terrain == null || tree == null || tree.Roots == null) return;
            int interior = n * n;
            var data = new float[interior * 3];
            int step = Mathf.Max(1, nodeRes / 8);   // ~8×8 campioni per faccia: copertura ampia, costo trascurabile
            float invRes = 1f / nodeRes;
            float maxDiff = 0f; int worstFace = -1; long samples = 0; int nanCount = 0;
            foreach (var r in tree.Roots)
            {
                if (r == null || r.slab < 0) continue;
                pool.Pos.GetData(data, 0, r.slab * vertsPerSlab * 3, interior * 3);   // solo l'interno (niente skirt)
                for (int j = 0; j <= nodeRes; j += step)
                    for (int i = 0; i <= nodeRes; i += step)
                    {
                        int vi = i + j * n;
                        float gx = data[vi * 3], gy = data[vi * 3 + 1], gz = data[vi * 3 + 2];
                        float gpuH = Mathf.Sqrt(gx * gx + gy * gy + gz * gz);     // |pos| = altezza radiale (GPU)
                        Vector3 dir = PlanetMeshBuilder.ParamToDir(r.up, r.axisA, r.axisB, i * invRes, j * invRes);
                        float cpuH = terrain.SampleHeight(dir);                   // stessa direzione, altezza CPU
                        samples++;
                        // NaN/Inf passerebbe INOSSERVATO: 'd > maxDiff' con d=NaN è sempre falso → il gate loggherebbe OK
                        // su geometria corrotta. Va pescato esplicitamente (buco nella rete #1, altezza duplicata a mano).
                        if (float.IsNaN(gpuH) || float.IsInfinity(gpuH) || float.IsNaN(cpuH)) { nanCount++; worstFace = r.face; continue; }
                        float d = Mathf.Abs(gpuH - cpuH);
                        if (d > maxDiff) { maxDiff = d; worstFace = r.face; }
                    }
            }
            if (nanCount > 0)
                Debug.LogError($"[parità GPU↔CPU] {terrain.name}: {nanCount} campioni con altezza NaN/Inf sulla GPU (faccia {worstFace}) → " +
                               "geometria corrotta che il gate avrebbe mascherato (NaN non aggiorna maxDiff). Controlla PlanetHeightCore.hlsl.");
            else if (maxDiff > 0.5f)
                Debug.LogError($"[parità GPU↔CPU] {terrain.name}: DIVERGE di {maxDiff:F3} m (faccia {worstFace}, {samples} campioni) → " +
                               "la mesh GPU e la collisione CPU (SampleHeight) non combaciano: il giocatore fluttuerà o sprofonderà. " +
                               "Controlla PlanetHeightCore.hlsl rispetto ai TerrainLayer C#.");
            else
                Debug.Log($"[parità GPU↔CPU] {terrain.name}: OK (max diff {maxDiff:F4} m su {samples} campioni).");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[parità GPU↔CPU] {(terrain != null ? terrain.name : "?")}: verifica saltata ({e.GetType().Name}).");
        }
    }

    // ===== ISlabFiller: trasforma la regione di un nodo in geometria nella sua fetta (un dispatch del compute) =====

    /// <summary>Riempie la fetta del nodo. In batch: ACCODA il job (un solo dispatch al FlushFills di fine frame).
    /// Altrimenti: dispatch per-nodo immediato (path sicuro, default).</summary>
    public void FillSlab(PlanetLodTree.Node nd)
    {
        if (nd.slab < 0) return;
        fillsThisFrame++;
        if (batchReady && jobCount < jobScratch.Length) { jobScratch[jobCount++] = MakeJob(nd); return; }
        FillSlabImmediate(nd);
    }

    NodeJob MakeJob(PlanetLodTree.Node nd) => new NodeJob
    {
        faceUp = nd.up, axisA = nd.axisA, axisB = nd.axisB,
        uv = new Vector4(nd.u0, nd.v0, nd.size / nodeRes, nd.slab * vertsPerSlab),
        // misc.z = indice fetta; misc.w = id regione UINT REINTERPRETATO nei bit del float (asuint nel compute lo
        // recupera esatto): così l'id passa nel buffer float4 dei job senza la perdita di precisione del vecchio cast.
        misc = new Vector4(0f, 0f, nd.slab, System.BitConverter.Int32BitsToSingle(unchecked((int)tree.RegionId(nd)))),
    };

    /// <summary>Fill PER-NODO immediato (1 dispatch, uniform per-nodo). Path classico, sicuro.</summary>
    void FillSlabImmediate(PlanetLodTree.Node nd)
    {
        if (nd.slab < 0) return;
        long t0 = Profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;   // diagnosi (PERF-2): solo con Profile
        cs.SetVector(ID_FaceUp, nd.up);
        cs.SetVector(ID_AxisA, nd.axisA);
        cs.SetVector(ID_AxisB, nd.axisB);
        cs.SetFloat(ID_U0, nd.u0);
        cs.SetFloat(ID_V0, nd.v0);
        cs.SetFloat(ID_Step, nd.size / nodeRes);
        cs.SetInt(ID_NSlabOff, nd.slab * vertsPerSlab);
        cs.SetInt("_SlabIndex", nd.slab);             // region-stamp (path per-nodo): il fill marchia questa fetta
        cs.SetInt("_SlabRegionId", unchecked((int)tree.RegionId(nd)));   // id regione UINT (i bit passano via SetInt; il compute lo legge come uint)
        int g = (n * n + 63) / 64;   // dispatch 1D (occupancy): n·n thread, 64/gruppo
        cs.Dispatch(kSlab, g, 1, 1);
        if (Profile) fillTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }

    /// <summary>Manda in UN dispatch tutti i job-fetta accumulati nel frame. Il nodo è sull'asse z del dispatch →
    /// ogni gruppo legge i suoi parametri da _Jobs[nodo]. Niente per-nodo SetX/Dispatch.</summary>
    void FlushFills()
    {
        if (!batchReady || jobCount == 0) return;
        long t0 = Profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        jobsBuf.SetData(jobScratch, 0, 0, jobCount);
        int g = (n * n + 63) / 64;   // dispatch 1D (occupancy): vertice lineare su x, NODO su y
        cs.Dispatch(kSlabBatch, g, jobCount, 1);
        if (Profile) fillTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        jobCount = 0;
    }

    /// <summary>BANCO DI VERIFICA (R1): riempie i 6 root col path PER-NODO e col path BATCH e confronta i vertici
    /// (readback). Se combaciano sub-cm → il batch è corretto e si può usare. Inchioda il bug d'indicizzazione che
    /// l'altra volta corruppe la geometria. Costa qualche readback sincrono, ma è UNA volta sola all'avvio.</summary>
    bool VerifyBatchFill()
    {
        int g = (n * n + 63) / 64;   // dispatch 1D (occupancy): vertice lineare su x, NODO su y
        // confronta TUTTI i buffer scritti dai fill, non solo le posizioni: un bug nei VALORI di normali/profondità/
        // pelo passerebbe inosservato e si vedrebbe come luce/acqua sbagliata. pos/nrm/bedNrm = 3 float/v, gli altri 1.
        var bufs = new[] { pool.Pos, pool.Nrm, pool.BedNrm, pool.Depth, pool.Field, pool.Surf, pool.Color };
        var pers = new[] { 3, 3, 3, 1, 1, 1, 3 };
        var names = new[] { "pos", "nrm", "bedNrm", "depth", "field", "surf", "color" };

        // A = PER-NODO per ogni root, salvo tutti i buffer (è il noto-corretto)
        var roots2 = new System.Collections.Generic.List<PlanetLodTree.Node>();
        foreach (var r in tree.Roots) if (r.slab >= 0) { FillSlabImmediate(r); roots2.Add(r); }
        var aData = new float[roots2.Count][][];
        for (int ri = 0; ri < roots2.Count; ri++)
        {
            aData[ri] = new float[bufs.Length][];
            for (int bi = 0; bi < bufs.Length; bi++)
            {
                int cnt = vertsPerSlab * pers[bi];
                var d = new float[cnt];
                bufs[bi].GetData(d, 0, roots2[ri].slab * cnt, cnt);
                aData[ri][bi] = d;
            }
        }

        // B = BATCH di TUTTI i root in UN SOLO dispatch — come in gioco (MULTI-JOB). Un bug che spunta solo con
        // più nodi nello stesso dispatch (hazard/indicizzazione sull'asse z/y) si vede QUI, non col job singolo.
        jobCount = 0;
        foreach (var r in roots2) jobScratch[jobCount++] = MakeJob(r);
        jobsBuf.SetData(jobScratch, 0, 0, jobCount);
        cs.Dispatch(kSlabBatch, g, jobCount, 1);

        float maxDiff = 0f; long compared = 0; string worst = "";
        for (int ri = 0; ri < roots2.Count; ri++)
            for (int bi = 0; bi < bufs.Length; bi++)
            {
                int cnt = vertsPerSlab * pers[bi];
                var bd = new float[cnt];
                bufs[bi].GetData(bd, 0, roots2[ri].slab * cnt, cnt);
                var ad = aData[ri][bi];
                for (int i = 0; i < cnt; i++) { float d = Mathf.Abs(ad[i] - bd[i]); if (d > maxDiff) { maxDiff = d; worst = names[bi]; } compared++; }
            }

        foreach (var r in roots2) FillSlabImmediate(r);   // ripristina il per-nodo
        jobCount = 0;
        bool ok = maxDiff < 0.01f;                           // sub-cm/sub-1% = identici (a meno del round-off)
        Debug.Log($"[batch-fill] verifica {terrain.name} (multi-job, {roots2.Count} fette × {bufs.Length} buffer in 1 dispatch): " +
                  $"{(ok ? "PARITÀ OK" : "DIVERGE!")} — {compared} float, max diff {maxDiff:F5} (buffer peggiore: {worst})");
        return ok;
    }

    /// <summary>Sospende il disegno di TUTTE le superfici GPU (lo accende MapMode in mappa). RenderPrimitivesIndexed‑
    /// Indirect entra in OGNI camera attiva → senza questo la superficie reale del pianeta comparirebbe in mappa,
    /// sopra/accanto ai proxy a dimensione-costante. In mappa la camera del giocatore è spenta comunque.</summary>
    public static bool SuppressDraw;

    /// <summary>OSSERVATORI EXTRA oltre al giocatore (es. una SONDA lanciata su un altro corpo). Chi possiede un
    /// punto di vista che deve vedere bene il terreno lontano si registra qui (e si toglie quando sparisce). VUOTA di
    /// default → comportamento identico (solo la camera del giocatore). Per ogni corpo, il renderer usa il viewpoint
    /// PIÙ VICINO per il dettaglio LOD e il morph, e NON culla un corpo se QUALCUNO lo vede da vicino.</summary>
    public static readonly System.Collections.Generic.List<Transform> ExtraViewpoints = new System.Collections.Generic.List<Transform>();

    void Update()
    {
        if (!Ready) return;
        if (SuppressDraw) return;   // mappa aperta: niente disegno della superficie GPU
        // Dopo un domain reload in Play (es. una mia modifica ricompilata MENTRE sei in Play) gli stati statici del pool
        // si azzerano e i binding del compute si invalidano, ma Ready resta dal backup → senza guardia i fill
        // dispatcherebbero con buffer non legati. Skip finché non si rifà il Setup. DEV-ONLY: in build niente domain reload.
        if (!SlabPool.IsAllocated || pool == null || mat == null || pool.Pos == null || !pool.Pos.IsValid()
            || idxBuf == null || !idxBuf.IsValid() || argsBuf == null) return;
        if (cam == null) { var c = Camera.main; if (c == null) return; cam = c.transform; }

        Matrix4x4 m = planetTf.localToWorldMatrix;
        // EARLY-OUT sub-pixel: un corpo così lontano da occupare meno di ~1 pixel NON fa nulla (niente refresh uniform,
        // niente traversata, niente draw, niente SetData). Converte il costo per-frame da O(corpi) a O(corpi VICINI).
        Vector3 bodyCenter = m.MultiplyPoint3x4(Vector3.zero);
        // Due distanze: bestSqr = viewpoint PIÙ VICINO in assoluto (giocatore o sonda) → governa SOLO il culling
        // sub-pixel (un corpo che QUALCUNO vede da vicino non si culla). viewPos = chi guida il LOD/morph, e qui il
        // GIOCATORE ha la PRIORITÀ: un osservatore extra (sonda) prende il timone SOLO se è MOLTO più vicino (≥2×,
        // sqr<0.25). Così lanciare la sonda accanto a te NON ruba dettaglio al suolo sotto i piedi (niente
        // "perde dettaglio e ricarica" a ogni lancio); la sonda guida il LOD solo dei corpi a cui è chiaramente più vicina.
        Vector3 viewPos = cam.position;
        float playerSqr = (cam.position - bodyCenter).sqrMagnitude;
        float bestSqr = playerSqr;     // più vicino in assoluto (per il culling)
        float lodSqr = playerSqr;      // viewpoint del LOD (priorità al giocatore)
        for (int i = 0; i < ExtraViewpoints.Count; i++)
        {
            var vp = ExtraViewpoints[i]; if (vp == null) continue;
            float d = (vp.position - bodyCenter).sqrMagnitude;
            if (d < bestSqr) bestSqr = d;
            if (d < lodSqr * 0.25f) { lodSqr = d; viewPos = vp.position; }   // l'extra prende il LOD solo se ≥2× più vicino
        }
        if (radius < Mathf.Sqrt(bestSqr) * 0.0006f) return;

        // per-frame sul materiale unico: porta il _Cull (Front se cullSplit, altrimenti Off = niente culling).
        // VISTE DEBUG: accendi la variante (keyword) solo al CAMBIO. In gioco (DebugView=0) la keyword è spenta → costo zero.
        if (DebugView != lastDebugView)
        {
            lastDebugView = DebugView;
            if (DebugView > 0) mat.EnableKeyword("PLANET_DEBUG_VIEW");
            else               mat.DisableKeyword("PLANET_DEBUG_VIEW");
        }
        mat.SetMatrix("_ObjectToWorld", m);
        mat.SetVector("_CamPosWorld", viewPos);   // geomorph: distanza dal viewpoint attivo (giocatore o sonda più vicina)
        mat.SetFloat("_DebugView", DebugView);
        mat.SetInt("_Cull", CullSplit ? InteriorCull : 0);
        RefreshLighting(mat);
        RefreshTorch(mat);

        // LOD: il quadtree decide split/merge e raccoglie le foglie visibili (mi richiama in FillSlab per le fette nuove)
        fillsThisFrame = 0;
        fillTicks = 0;
        long swStart = System.Diagnostics.Stopwatch.GetTimestamp();   // diagnosi: costo CPU del mio lavoro per frame (GetTimestamp = niente alloc heap, gira per ogni corpo vicino ogni frame)
        tree.Update(m, viewPos, Time.deltaTime);   // LOD guidato dal viewpoint più vicino (la guardia sui salti copre il cambio osservatore)
        FlushFills();   // batch: i fill accumulati nella traversata partono ora, in un solo dispatch (no-op se per-nodo)
        double lodMs = (System.Diagnostics.Stopwatch.GetTimestamp() - swStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;   // traversata + dispatch dei fill
        if (tree.VisibleCount == 0) return;

        // UPLOAD solo quando la selezione cambia (split/merge/orizzonte) o al primo frame: a camera ferma i buffer
        // tengono già i valori giusti → niente SetData (3 buffer + args) per frame. Il morph è un uniform separato.
        if (tree.SelectionChanged || !uploadedOnce)
        {
            slabOfInstance.SetData(tree.VisibleSlabs, 0, 0, tree.VisibleCount);
            splitDistOfInstance.SetData(tree.VisibleSplitDist, 0, 0, tree.VisibleCount);   // geomorph: parallelo a slabOfInstance
            dirOfInstance.SetData(tree.VisibleDirs, 0, 0, tree.VisibleCount);              // anti-spuntone: parallelo a slabOfInstance
            regionOfInstance.SetData(tree.VisibleRegions, 0, 0, tree.VisibleCount);        // region-stamp uint: parallelo a slabOfInstance
            argsData[0].indexCountPerInstance = (uint)indexCountPerSlab;
            argsData[0].instanceCount = (uint)tree.VisibleCount;
            argsData[0].startIndex = 0;
            argsData[0].baseVertexIndex = 0;   // GPU-6: espliciti (default 0) per blindare il draw indirect su DX12/Vulkan
            argsData[0].startInstance = 0;     // (su Metal sono già 0; i validation layer di DX12/Vulkan li vogliono espliciti)
            argsBuf.SetData(argsData);
            uploadedOnce = true;
        }

        var worldBounds = new Bounds(bodyCenter, Vector3.one * (radius * 5f));
        // UN draw indiretto: la sola griglia interna (niente skirt). Il _Cull del materiale dimezza l'overdraw.
        var rp = new RenderParams(mat) { worldBounds = worldBounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
        Graphics.RenderPrimitivesIndexedIndirect(rp, MeshTopology.Triangles, idxBuf, argsBuf, 1);

        double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - swStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        double fillMs = fillTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        double travMs = lodMs - fillMs;                 // logica di traversata pura (split/merge/orizzonte)
        double sendMs = ms - lodMs;                      // SetData + invio del draw (qui appare l'attesa-GPU)
        if (Time.frameCount != sBreakdownFrame) { sBreakdownFrame = Time.frameCount; Trav = Fill = Send = 0f; }
        Trav += (float)travMs; Fill += (float)fillMs; Send += (float)sendMs;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // logga SOLO i frame "pesanti" (≥4 ms). FUORI dalle build di rilascio: in ship il Debug.Log con string-interp
        // scatterebbe PROPRIO sui frame di stutter, peggiorando il picco che vuole misurare.
        if (ms >= 4.0) Debug.Log($"[GpuPlanet {name}] CPU {ms:F1} ms (trav {travMs:F1} · fill {fillMs:F1} · invio {sendMs:F1}) · fills={fillsThisFrame} · visibili={tree.VisibleCount}");
#endif
    }

    /// <summary>Index buffer della topologia di UNA fetta: la griglia interna n×n. L'offset di fetta lo aggiunge
    /// il vertex shader via SV_InstanceID → un solo index buffer per tutte le istanze.</summary>
    void BuildIndexBuffer()
    {
        var idx = new System.Collections.Generic.List<int>(nodeRes * nodeRes * 6);
        for (int j = 0; j < nodeRes; j++)
            for (int i = 0; i < nodeRes; i++)
            {
                int i00 = i + j * n, i10 = (i + 1) + j * n, i01 = i + (j + 1) * n, i11 = (i + 1) + (j + 1) * n;
                idx.Add(i00); idx.Add(i01); idx.Add(i11);
                idx.Add(i00); idx.Add(i11); idx.Add(i10);
            }
        indexCountPerSlab = idx.Count;
        idxBuf = new GraphicsBuffer(GraphicsBuffer.Target.Index, indexCountPerSlab, 4);
        idxBuf.SetData(idx);
    }

    void ApplyColor()
    {
        if (mat == null) return;
        RefreshLighting(mat);
        var rec = terrain.Recipe;
        mat.SetFloat("_BaseRadius", rec != null ? rec.baseRadius : terrain.BaseRadius);
        mat.SetFloat("_Amplitude", rec != null ? rec.amplitude : terrain.Amplitude);
        mat.SetFloat("_Frequency", rec != null ? rec.frequency : terrain.Frequency);
        mat.SetInt("_Octaves", rec != null ? rec.octaves : terrain.Octaves);
        mat.SetFloat("_Lacunarity", rec != null ? rec.lacunarity : terrain.Lacunarity);
        mat.SetFloat("_Gain", rec != null ? rec.gain : terrain.Gain);
        mat.SetInt("_Seed", rec != null ? rec.seed : terrain.Seed);
        // _HAS_SEA (GPU-2): accendi la variante col mare SOLO se la ricetta ha davvero un mare → i corpi asciutti
        // (Cetra/Luna6) compilano il fragment SENZA il blocco acqua. La keyword vale per-materiale (questo corpo).
        bool hasSea = rec != null && rec.LastSea() != null;
        if (hasSea) mat.EnableKeyword("_HAS_SEA"); else mat.DisableKeyword("_HAS_SEA");
        // PBR per pendenza + GGX (GPU-4): keyword sul materiale (A/B da GameBootstrap).
        if (UsePbrTerrain) mat.EnableKeyword("_PBR_TERRAIN"); else mat.DisableKeyword("_PBR_TERRAIN");
        if (rec != null)
        {
            // COLORE/MARE dalla ricetta (fonte unica: PlanetRecipeUniforms). Resa GPU in gioco = liquido + trasparenza.
            PlanetRecipeUniforms.ApplyColor(mat, rec);
            PlanetRecipeUniforms.ApplySea(mat, rec, rec.LastSea(), liquid: true, transparency: true);
        }
    }

    void RefreshLighting(Material m)
    {
        if (m == null) return;
        if (sun == null && SunLight.Instance != null) sun = SunLight.Instance.GetComponent<Light>();
        Vector3 dir = sun != null ? -sun.transform.forward : Vector3.up;
        Color sc = sun != null ? sun.color * sun.intensity : Color.white;
        m.SetVector("_SunDir", dir.normalized);
        m.SetVector("_SunColor", new Vector4(sc.r, sc.g, sc.b, 1f));
        Color amb = RenderSettings.ambientLight;
        m.SetVector("_Ambient", new Vector4(amb.r, amb.g, amb.b, 1f));
    }

    void RefreshTorch(Material m)
    {
        if (m == null) return;
        if (torch == null && !torchSearched) { torchSearched = true; var fl = FindAnyObjectByType<Flashlight>(); if (fl != null) torch = fl.lamp; }
        if (torch == null) { m.SetVector("_TorchColor", Vector4.zero); return; }
        Color tc = torch.color * torch.intensity;   // intensità 0 da spenta → contributo nullo
        m.SetVector("_TorchPos", torch.transform.position);
        m.SetVector("_TorchDir", torch.transform.forward);
        m.SetVector("_TorchColor", new Vector4(tc.r, tc.g, tc.b, 1f));
        m.SetFloat("_TorchRange", torch.range);
        float half = torch.spotAngle * 0.5f * Mathf.Deg2Rad;
        m.SetFloat("_TorchCosOuter", Mathf.Cos(half));
        m.SetFloat("_TorchCosInner", Mathf.Cos(half * 0.85f));
    }

    /// <summary>Imposta gli uniform di ECLISSI sul materiale GPU (li calcola EclipseDriver per frame, come per i
    /// materiali bakeati). Raggio 0 = nessuna eclissi. Così l'ombra di eclissi appare anche sul renderer autoritativo,
    /// non solo sul fallback PlanetBaked.</summary>
    public void SetEclipse(Vector3 occPos, float occRad, Vector3 sunDir, float sunAng)
    {
        if (mat == null) return;
        mat.SetVector("_EclipseOccluderPos", occPos);
        mat.SetFloat("_EclipseOccluderRadius", occRad);
        mat.SetVector("_EclipseSunDir", sunDir);
        mat.SetFloat("_EclipseSunAngular", sunAng);
    }

    void OnDestroy()
    {
        tree?.ReturnSlabs();   // streaming-safe: rendi le fette di questo corpo PRIMA di mollare il riferimento al pool
        pool?.Release();       // i buffer geometria sono CONDIVISI e refcountati: liberati solo quando muore l'ultimo corpo
        jobsBuf?.Release();
        idxBuf?.Release(); slabOfInstance?.Release(); splitDistOfInstance?.Release(); dirOfInstance?.Release(); regionOfInstance?.Release(); argsBuf?.Release();
        shape?.Dispose();
        if (mat != null) Destroy(mat);
        if (cs != null) Destroy(cs);   // l'istanza propria del compute
    }
}

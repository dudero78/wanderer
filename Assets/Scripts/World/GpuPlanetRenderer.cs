using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Resa della superficie di un pianeta IN GIOCO sulla GPU con LOD (percorso B1, Tappa 2).
///
/// QUADTREE GPU su cubo-sfera: 6 facce-radice, ogni nodo è una patch [u0,u0+size]×[v0,v0+size] del dominio
/// parametrico della faccia. Si suddivide quando la camera è vicina → densità che segue la distanza: fitta
/// sotto i piedi (crateri nitidi), rada all'orizzonte, CULLATA dietro la curvatura. Ogni nodo-foglia possiede
/// una FETTA del pool GPU (griglia interna + skirt), riempita da PlanetHeight.compute (CSNodeSlab/CSNodeSkirt).
/// La lista delle foglie visibili va a UN solo Graphics.RenderPrimitivesIndexedIndirect — niente Mesh Unity,
/// niente upload, niente readback, niente draw call per-nodo.
///
/// Perché sulla GPU è PIÙ semplice del vecchio quadtree CPU: il "build" di un nodo è un dispatch, e il
/// risultato si disegna direttamente dal buffer (nessun thread, nessuna lettura asincrona, nessuna coda).
///
/// Il walker NON dipende da questo (legge SampleHeight analitico in 1 punto) → collisione intatta. La luce è
/// MANUALE (lo shader dai-buffer non riceve le luci di Unity): sole + torcia passati a mano.
///
/// Tappa 2 = LOD + skirt (niente fessure ai confini). Tappa 2b: geomorph (transizioni LOD lisce, niente pop).
/// Robustezza: senza compute o shader, Ready resta false e SolarSystemSetup ripiega sul quadtree.
/// </summary>
public class GpuPlanetRenderer : MonoBehaviour
{
    // --- manopole LOD ---
    int nodeRes = 32;            // quad per lato di un nodo (33×33 vertici interni)
    public float lodFactor = 3f; // suddivide se la camera è più vicina di worldSize·questo (più basso = meno nodi/fill;
                                 // NON tocca il dettaglio sotto i piedi, solo quanto lontano si estende)
    public float mergeHysteresis = 1f;   // CDLOD: confini di LOD NETTI (split e merge alla stessa soglia). Una banda morta
                                         // (>1) farebbe morfare i due lati di un confine a misure diverse → crepe. Il flip
                                         // alla soglia è invisibile (mf=1 = forma del genitore su entrambi i lati) e in cache.
    public int maxDepth = 6;
    public float skirtFactor = 0.5f;   // profondità skirt = worldSize·questo. Tenerlo BASSO: lo skirt è un muretto al
                                       // confine di LOD, e più è profondo più si vede come lamella scura. Il fix vero
                                       // delle cuciture è il GEOMORPH (Tappa 2b), non skirt più profondi.
    public int maxSlabs = 1024;  // fette nel pool CONDIVISO fra i corpi (free + cache + visibili). Pre-allocate.
                                 // (~459 MB). Il "nero all'orizzonte" NON era esaurimento del pool ma l'horizon
                                 // culling che mangiava i picchi → risolto in BeyondHorizon, qui resta il valore VRAM-ottimo.
    public int splitBudget = 8;  // quante tessere nuove al MASSIMO preparare per fotogramma (×4 fill): spalmando il
                                 // lavoro su più fotogrammi si evita l'ondata che fa scattare. Il LOD predittivo copre il "ritardo"
    public float lookaheadTime = 0.7f;   // LOD PREDITTIVO: valuta lo split dalla posizione DOVE SARAI fra ~tot secondi
                                         // (verso cui voli) → il dettaglio è pronto PRIMA di arrivarci (niente "carica tardi")
    public static bool UseGeomorph = true;   // GEOMORPH CDLOD nel vertex shader: transizioni LOD lisce. Statico (da GameBootstrap), toggle A/B
    // DIAGNOSI spuntoni: false = NON disegna gli SKIRT (i muretti ai confini di LOD). Se gli spuntoni nei crateri
    // spariscono spegnendoli (lasciando magari fessure), sono gli skirt → cura = quadtree 2:1. Toggle A/B temporaneo.
    public static bool DrawSkirts = false;
    public float morphRange = 0.5f;      // ampiezza della banda di morph (frazione di splitDist): 0.1 stretta, 0.9 larga
    // OVERDRAW: interno con Cull Back + skirt con Cull Off, in 2 draw (il _Cull lo guida il MATERIALE, deterministico).
    // STATICI (impostati da GameBootstrap). InteriorCull 2=Back; se l'INTERNO sparisce accendendolo, il verso è
    // invertito → mettilo a 1 (Front). Dimezza l'ombreggiatura per-pixel del terreno (niente retro-facce).
    public static bool CullSplit = true;
    public static int InteriorCull = 1;   // 1=Front: il verso dell'interno è Front-facing (Cull Back ribaltava tutto). Verificato in gioco

    /// <summary>DIAGNOSI: 0 = resa normale · 1 = posizione radiale (fragment banale) · 2 = normale di mondo.</summary>
    // DIAGNOSI superficie (statico, pilotabile da GameBootstrap e dal menu in-game à):
    //   0 = off · 1 = posizione radiale (geometria pura) · 2 = normale di mondo (shading) ·
    //   3 = livello di LOD · 4 = faccia del cubo · 5 = fetta (ogni slab un colore).
    public static int DebugView = 0;
    int lastDebugView = -1;   // per accendere/spegnere la keyword PLANET_DEBUG_VIEW solo al cambio

    int n;             // nodeRes+1 (vertici per lato)
    int vertsPerSlab;  // n*n + 4*nodeRes (interno + skirt)
    int skirtStart;    // n*n
    int indexCountPerSlab;
    int interiorIndexCount;   // indici dei tris INTERNI (primi nodeRes²·6): il resto dell'index buffer è skirt
    float radius;

    ComputeShader cs;
    int kSlab, kSkirt;
    // ID di proprietà cachati: SetX per NOME ri-hasha la stringa a ogni chiamata (×~600/frame nei fill); con
    // ID interi l'overhead per-chiamata crolla. _NN/_NSkirtStart sono costanti → settati una volta nel Setup.
    static readonly int ID_FaceUp = Shader.PropertyToID("_FaceUp");
    static readonly int ID_AxisA = Shader.PropertyToID("_AxisA");
    static readonly int ID_AxisB = Shader.PropertyToID("_AxisB");
    static readonly int ID_U0 = Shader.PropertyToID("_U0");
    static readonly int ID_V0 = Shader.PropertyToID("_V0");
    static readonly int ID_Step = Shader.PropertyToID("_Step");
    static readonly int ID_NSlabOff = Shader.PropertyToID("_NSlabOff");
    static readonly int ID_NSkirtDrop = Shader.PropertyToID("_NSkirtDrop");
    // POOL geometria: ALIAS dei buffer CONDIVISI statici (sPos…), assegnati in AcquirePool. Un solo pool serve
    // tutti i corpi (#2 sez. A): un corpo solo è "attivo" per volta, gli altri stanno alle radici → 1×~843 MB, non 6×.
    GraphicsBuffer posBuf, nrmBuf, bedNrmBuf, depthBuf, fieldBuf, surfBuf;
    GraphicsBuffer slabRegion;   // alias del marchio di regione CONDIVISO (parallelo al pool: un marchio per fetta fisica)
    GraphicsBuffer idxBuf;                                // topologia di UNA fetta (interno + skirt), condivisa
    GraphicsBuffer slabOfInstance;                        // istanza visibile → indice di fetta
    GraphicsBuffer splitDistOfInstance;                   // geomorph: splitDist (worldSize·lodFactor) per istanza, parallelo a slabOfInstance
    GraphicsBuffer dirOfInstance;                         // ANTI-SPUNTONE: direzione-centro del nodo per istanza (float4, w inusato). Il vertex collassa chi devia troppo in direzione = fetta di regione sbagliata
    Vector4[] dirScratch;
    GraphicsBuffer argsBuf;
    GraphicsBuffer.IndirectDrawIndexedArgs[] argsData = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
    GraphicsBuffer argsSkirtBuf;   // secondo draw (solo skirt, Cull Off) quando cullSplit è ON
    GraphicsBuffer.IndirectDrawIndexedArgs[] argsSkirtData = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
    GpuShapeBuffers shape;
    Material mat;        // INTERNO: Cull Back quando cullSplit è ON
    Material matSkirt;   // SKIRT: Cull Off (doppia faccia). Stessi buffer/uniform di mat, solo il _Cull diverso
    Transform planetTf;
    PlanetTerrain terrain;

    // free-list + CACHE LRU delle fette: CONDIVISE fra i corpi (alias degli statici, assegnati in AcquirePool).
    // Una regione (face,depth,u0,v0) che esce di vista NON si butta: la sua fetta resta in cache e si riusa al
    // ritorno → niente ricalcolo. Con pool condiviso un corpo attivo può "prendere in prestito" le fette in cache
    // dei corpi lontani (eviction LRU): per loro è solo un refill al ritorno.
    Stack<int> freeSlabs;
    Dictionary<long, int> cacheSlab;
    // LRU O(1): la cache si sfratta dal FRONTE (regione meno recente) in tempo costante, invece di scansionare
    // tutto il dizionario (era O(n), proprio nel churn a cambio-quota). lru = ordine d'inserimento (release),
    // lruNode = handle per la rimozione O(1) quando una regione torna viva.
    LinkedList<long> lru;
    Dictionary<long, LinkedListNode<long>> lruNode;
    readonly Stack<Node[]> childPool = new Stack<Node[]>();   // riusa gli array figli (no new Node[4] a ogni split → GC giù nel churn)
    int bodyId;   // identità del corpo nel pool condiviso: entra nella chiave di cache → due corpi non collidono

    // ---- POOL CONDIVISO (statico, refcountato): i 6 buffer geometria + free-list + cache, UNO per tutti i corpi.
    // Risparmio: 1×~843 MB invece di 6× ≈ 5 GB allocati alla costruzione scena (#2 sez. A, NOTES_pool_vram.md).
    // Tutti i corpi devono avere lo STESSO vertsPerSlab (= stesso nodeRes), o le fette non sarebbero allineate.
    static GraphicsBuffer sPos, sNrm, sBedNrm, sDepth, sField, sSurf;
    static GraphicsBuffer sSlabRegion;   // marchio di regione CONDIVISO (parallelo al pool): un float per fetta fisica
    static readonly Stack<int> sFreeSlabs = new Stack<int>();
    static readonly Dictionary<long, int> sCacheSlab = new Dictionary<long, int>();
    static readonly LinkedList<long> sLru = new LinkedList<long>();
    static readonly Dictionary<long, LinkedListNode<long>> sLruNode = new Dictionary<long, LinkedListNode<long>>();
    static int sRefCount;
    static int sPoolVerts, sPoolSlabs;   // dimensioni con cui il pool è stato allocato (tutti i corpi le condividono)
    static int sNextBodyId;
    // pool degli oggetti Node: split/merge in movimento NON allocano più (niente GC → niente stallo periodico).
    readonly Stack<Node> nodePool = new Stack<Node>();
    Node[] roots;
    uint[] visibleScratch;
    float[] splitScratch;     // geomorph: splitDist per istanza visibile (riempito in AddVisible, caricato con visibleScratch)
    int visibleCount;

    Transform cam;
    Light sun, torch;
    Vector3 prevCamRel;       // posizione camera rispetto al CENTRO del pianeta nel frame scorso (per il LOD predittivo)
    bool hasPrevCamRel;
    // CONTESTO LOD del frame: settato UNA volta in Update, letto dalla traversata. Così UpdateLod NON si passa
    // più matrice+vettori PER COPIA a ogni nodo (in Mono/editor copiare ~112 byte migliaia di volte = il costo
    // misurato), e le costanti dell'orizzonte (direzione/angolo camera) si calcolano una volta, non per ogni nodo.
    Matrix4x4 lodM;
    Vector3 lodCamLook, lodCenter, lodCamDir;
    float lodThetaHorizon;
    float lodPeakAngle;       // height-aware: angolo con cui i PICCHI del terreno spuntano oltre l'orizzonte geometrico
    bool lodHorizonValid;     // false se la camera è troppo bassa (sotto il raggio) → niente culling all'orizzonte
    int splitsThisFrame;
    int fillsThisFrame;   // diagnosi: quante fette riempite (dispatch) in questo frame
    long fillTicks;       // diagnosi: tempo CPU speso nelle chiamate dei fill (SetX+Dispatch), per separarlo dalla traversata
    // scomposizione CPU del frame (ms), SOMMATA su tutti i corpi, esposta all'HUD: collo del churn a colpo d'occhio
    public static float Trav, Fill, Send;
    static int sBreakdownFrame = -1;

    // --- BATCH FILL (opt-in): accumula i fill del frame e li manda in UN dispatch invece di uno per nodo (taglia le
    // chiamate API). Si attiva solo se la VERIFICA di parità (batch↔per-nodo, vedi VerifyBatchFill) è verde; altrimenti
    // resta il path per-nodo (sicuro). R1: il batch corruppe la geometria una volta → ora è dietro al banco di verifica.
    public static bool UseBatchFill;   // acceso da GameBootstrap PRIMA del Setup (la verifica gira nel Setup)
    bool batchReady;             // true = verifica passata → uso il path batch
    int kSlabBatch, kSkirtBatch;
    GraphicsBuffer jobsBuf;      // parametri per-nodo del frame
    NodeJob[] jobScratch;        // CPU side, riempito durante la traversata, caricato una volta al flush
    int jobCount;
    struct NodeJob { public Vector4 faceUp, axisA, axisB, uv, misc; }   // = NodeJobGPU (uv: u0,v0,step,slabOff; misc.x: skirtDrop)

    public bool Ready { get; private set; }

    /// <summary>Un nodo del quadtree: leggero (niente GameObject/Mesh). slab ≥ 0 solo sulle FOGLIE.</summary>
    class Node
    {
        public int face, depth;
        public float u0, v0, size;
        public Vector3 up, axisA, axisB;
        public Vector3 centerLocal;
        public float worldSize;
        public Node[] children;   // null = foglia
        public int slab = -1;
        public bool horizonHidden;   // stato isteresi del culling all'orizzonte (evita il flip cancella/ricrea)
    }

    public void Setup(PlanetTerrain terrain, int res)
    {
        this.terrain = terrain;
        this.planetTf = terrain.transform;
        // TETTO di nodeRes (#2 sez. B): la VRAM del pool cresce col QUADRATO di nodeRes. 96 invece di 128 taglia
        // ~1.8× la memoria per fetta con un calo di dettaglio modesto (il quadtree si suddivide comunque per
        // distanza). È un DIAL: 128 = dettaglio pieno/più VRAM, 64 = ~4× meno VRAM/più grossolano. Cap, non default,
        // così vale qualunque gpuSurfaceRes serializzato in scena. (Editor preview = altra classe, non toccata.)
        nodeRes = Mathf.Clamp(res, 4, 96) & ~1;   // PARI obbligatorio: il geomorph/skirt assumono bordi pari; dispari → letture fuori-griglia = spuntoni
        n = nodeRes + 1;
        skirtStart = n * n;
        vertsPerSlab = n * n + 4 * nodeRes;

        if (!SystemInfo.supportsComputeShaders) return;
        // ISTANZA PROPRIA del compute: in scena ci sono più corpi, e il ComputeShader condiviso (Resources.Load
        // è cache-ato) avrebbe binding di buffer/uniform GLOBALI → i corpi si clobbererebbero a vicenda (uno split
        // userebbe la ricetta di un altro). Una copia per corpo = stato indipendente.
        var baseCs = Resources.Load<ComputeShader>("Shaders/PlanetHeight");
        cs = baseCs != null ? Instantiate(baseCs) : null;
        var sh = Shader.Find("Wanderer/PlanetSurfaceGPU");
        if (cs == null || sh == null)
        {
            Debug.LogWarning("GpuPlanetRenderer: compute o shader 'Wanderer/PlanetSurfaceGPU' mancante → superficie GPU non disponibile.");
            return;
        }
        kSlab = cs.FindKernel("CSNodeSlab");
        kSkirt = cs.FindKernel("CSNodeSkirt");
        kSlabBatch = cs.FindKernel("CSNodeSlabBatch");
        kSkirtBatch = cs.FindKernel("CSNodeSkirtBatch");

        AcquirePool();   // alloca (o riusa) il pool CONDIVISO → alias posBuf…surfBuf + free-list/cache + bodyId
        jobsBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxSlabs, 5 * 16); // NodeJob = 5×float4 (per-corpo, ~82 KB)
        jobScratch = new NodeJob[maxSlabs];

        // i 4 kernel (per-nodo + batch) scrivono lo STESSO pool; i batch leggono anche _Jobs
        foreach (int k in new[] { kSlab, kSkirt, kSlabBatch, kSkirtBatch })
        {
            cs.SetBuffer(k, "_VPos", posBuf);
            cs.SetBuffer(k, "_VNrm", nrmBuf);
            cs.SetBuffer(k, "_VBedNrm", bedNrmBuf);
            cs.SetBuffer(k, "_VDepth", depthBuf);
            cs.SetBuffer(k, "_VField", fieldBuf);
            cs.SetBuffer(k, "_VSurf", surfBuf);
        }
        cs.SetBuffer(kSlabBatch, "_Jobs", jobsBuf);
        cs.SetBuffer(kSkirtBatch, "_Jobs", jobsBuf);
        cs.SetBuffer(kSlab, "_SlabRegion", slabRegion);          // marchio di regione: lo scrive il fill dell'interno
        cs.SetBuffer(kSlabBatch, "_SlabRegion", slabRegion);
        shape = GpuShapeBuffers.Build(cs, terrain, new[] { kSlab, kSkirt, kSlabBatch, kSkirtBatch });   // base+ricetta a tutti
        cs.SetInt("_NN", n);                 // costanti per i kernel dei fill (non più per-fill)
        cs.SetInt("_NSkirtStart", skirtStart);

        BuildIndexBuffer();

        visibleScratch = new uint[maxSlabs];   // la free-list è inizializzata UNA volta in AcquirePool (pool condiviso)
        splitScratch = new float[maxSlabs];
        dirScratch = new Vector4[maxSlabs];

        slabOfInstance = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxSlabs, 4);
        splitDistOfInstance = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxSlabs, 4);
        dirOfInstance = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxSlabs, 16);   // float4 (16B): evita la trappola Metal float3
        // REGION-STAMP (anti fetta-fantasma): il marchio per fetta (slabRegion, CONDIVISO via AcquirePool) è scritto dal
        // kernel di fill INSIEME alla geometria → se la fetta tiene una regione vecchia (churn), il marchio lo svela.
        argsBuf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
        argsSkirtBuf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);

        mat = new Material(sh);
        mat.SetBuffer("_VPos", posBuf);
        mat.SetBuffer("_VNrm", nrmBuf);
        mat.SetBuffer("_VBedNrm", bedNrmBuf);
        mat.SetBuffer("_VDepth", depthBuf);
        mat.SetBuffer("_VField", fieldBuf);
        mat.SetBuffer("_VSurf", surfBuf);
        mat.SetBuffer("_SlabOfInstance", slabOfInstance);
        mat.SetBuffer("_SplitDistOfInstance", splitDistOfInstance);
        mat.SetBuffer("_DirOfInstance", dirOfInstance);
        mat.SetBuffer("_SlabRegion", slabRegion);
        mat.SetInt("_VertsPerSlab", vertsPerSlab);
        mat.SetFloat("_PerVertexFields", 1f);   // in gioco: usa baseN per-vertice (fragment più economico)
        mat.SetInt("_NN", n);                                  // geomorph: vertici per lato → (i,j) dal vid + lettura vicini
        mat.SetFloat("_MorphRange", morphRange);
        mat.SetFloat("_UseGeomorph", UseGeomorph ? 1f : 0f);

        radius = terrain.Recipe != null ? terrain.Recipe.baseRadius : terrain.BaseRadius;
        cs.SetInt("_HasSea", terrain.Recipe != null && terrain.Recipe.LastSea() != null ? 1 : 0);

        // 6 facce-radice, ciascuna con la sua fetta riempita (per-nodo: batchReady è ancora false → fill noto-corretto)
        roots = new Node[6];
        for (int f = 0; f < 6; f++)
        {
            roots[f] = MakeNode(f, 0f, 0f, 1f, 0);
            AcquireSlab(roots[f]);   // cache vuota all'avvio → alloca e riempie (per-nodo)
        }

        // BATCH FILL: lo accendo solo se la verifica di parità (batch↔per-nodo, sui 6 root) è verde. Altrimenti
        // resto sul per-nodo (sicuro). Vedi R1: il batch corruppe la geometria una volta → niente fede cieca.
        if (UseBatchFill)
        {
            batchReady = VerifyBatchFill();
            if (!batchReady) Debug.LogWarning($"{terrain.name}: batch fill NON attivato (parità fallita) → resto sul per-nodo.");
        }

        VerifyParityRuntime();   // #9: gate non bloccante CPU↔GPU (un readback dei root, una volta sola)
        ApplyColor();

        // SKIRT: secondo materiale = copia di mat ma con Cull Off. new Material(mat) copia gli uniform; i BUFFER li
        // ri-lego esplicitamente (la copia non sempre li porta con sé) → robusto. Il Cull lo guida il MATERIALE.
        matSkirt = new Material(mat);
        BindSurfaceBuffers(matSkirt);
        matSkirt.SetInt("_Cull", 0);   // skirt SEMPRE Cull Off (doppia faccia, tappano la fessura da ogni angolo)

        Ready = true;
    }

    /// <summary>Lega i 6 buffer del pool + i due per-istanza a un materiale (mat e matSkirt condividono gli stessi).</summary>
    void BindSurfaceBuffers(Material m)
    {
        m.SetBuffer("_VPos", posBuf); m.SetBuffer("_VNrm", nrmBuf); m.SetBuffer("_VBedNrm", bedNrmBuf);
        m.SetBuffer("_VDepth", depthBuf); m.SetBuffer("_VField", fieldBuf); m.SetBuffer("_VSurf", surfBuf);
        m.SetBuffer("_SlabOfInstance", slabOfInstance); m.SetBuffer("_SplitDistOfInstance", splitDistOfInstance);
        m.SetBuffer("_DirOfInstance", dirOfInstance); m.SetBuffer("_SlabRegion", slabRegion);
    }

    /// <summary>GATE DI PARITÀ a runtime (#9): all'avvio del corpo confronta la geometria che il renderer ha
    /// DAVVERO prodotto sulla GPU (i 6 root, già riempiti in posBuf) con SampleHeight della CPU sulle stesse
    /// direzioni. È la verifica più diretta possibile della rete CPU↔GPU: la mesh la fa la GPU
    /// (PlanetHeightCore.hlsl), la collisione del walker la fa la CPU (PlanetTerrain.SampleHeight). Se un domani
    /// una divergenza HLSL↔C# si insinua, il giocatore "fluttuerebbe/sprofonderebbe"; il test di parità del menu
    /// è OFFLINE e manuale, questo invece scatta a OGNI avvio. Non bloccante: solo un LogError, un readback per corpo.</summary>
    void VerifyParityRuntime()
    {
        try
        {
            if (posBuf == null || !posBuf.IsValid() || terrain == null || roots == null) return;
            int interior = n * n;
            var data = new float[interior * 3];
            int step = Mathf.Max(1, nodeRes / 8);   // ~8×8 campioni per faccia: copertura ampia, costo trascurabile
            float invRes = 1f / nodeRes;
            float maxDiff = 0f; int worstFace = -1; long samples = 0;
            foreach (var r in roots)
            {
                if (r == null || r.slab < 0) continue;
                posBuf.GetData(data, 0, r.slab * vertsPerSlab * 3, interior * 3);   // solo l'interno (niente skirt)
                for (int j = 0; j <= nodeRes; j += step)
                    for (int i = 0; i <= nodeRes; i += step)
                    {
                        int vi = i + j * n;
                        float gx = data[vi * 3], gy = data[vi * 3 + 1], gz = data[vi * 3 + 2];
                        float gpuH = Mathf.Sqrt(gx * gx + gy * gy + gz * gz);     // |pos| = altezza radiale (GPU)
                        Vector3 dir = PlanetMeshBuilder.ParamToDir(r.up, r.axisA, r.axisB, i * invRes, j * invRes);
                        float cpuH = terrain.SampleHeight(dir);                   // stessa direzione, altezza CPU
                        float d = Mathf.Abs(gpuH - cpuH);
                        if (d > maxDiff) { maxDiff = d; worstFace = r.face; }
                        samples++;
                    }
            }
            if (maxDiff > 0.5f)
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

    Node MakeNode(int face, float u0, float v0, float size, int depth)
    {
        var nd = nodePool.Count > 0 ? nodePool.Pop() : new Node();   // dal pool → zero alloc nel churn del LOD
        nd.face = face; nd.u0 = u0; nd.v0 = v0; nd.size = size; nd.depth = depth;
        nd.children = null; nd.slab = -1; nd.horizonHidden = false;
        nd.up = PlanetMeshBuilder.FaceNormals[face];
        PlanetMeshBuilder.FaceAxes(nd.up, out nd.axisA, out nd.axisB);
        ComputeBounds(nd);
        return nd;
    }

    /// <summary>Centro (per la distanza di LOD) + dimensione mondo (per split/skirt), sulla SFERA BASE.
    /// NON chiama più SampleHeight: era il walker CPU (pesante: rumore+crateri+tettonica), 3 volte per nodo → 12
    /// per split → era il PICCO misurato durante la costruzione del LOD. Lo spostamento d'altezza (decine di m) è
    /// trascurabile per la DISTANZA di LOD e l'orizzonte (centinaia di m) → niente cambio visibile. La collisione
    /// del walker usa il suo SampleHeight analitico, indipendente da questo.</summary>
    void ComputeBounds(Node nd)
    {
        Vector3 c00 = CornerDir(nd, nd.u0, nd.v0);
        Vector3 c11 = CornerDir(nd, nd.u0 + nd.size, nd.v0 + nd.size);
        nd.worldSize = Vector3.Distance(c00, c11) * radius;
        Vector3 dirC = PlanetMeshBuilder.ParamToDir(nd.up, nd.axisA, nd.axisB, nd.u0 + nd.size * 0.5f, nd.v0 + nd.size * 0.5f);
        nd.centerLocal = dirC * radius;
    }

    Vector3 CornerDir(Node nd, float tx, float ty)
        => PlanetMeshBuilder.ParamToDir(nd.up, nd.axisA, nd.axisB, tx, ty);   // direzione sulla sfera (niente altezza)

    /// <summary>Chiave STABILE della regione (face, profondità, indici di griglia interi): u0,v0 sono multipli
    /// di 1/2^depth → indici esatti. Stessa idea del vecchio quadtree.</summary>
    long Key(Node nd)
    {
        int N = 1 << nd.depth;
        long ix = Mathf.RoundToInt(nd.u0 * N);
        long iy = Mathf.RoundToInt(nd.v0 * N);
        // bodyId nei bit alti: nel pool CONDIVISO due corpi con la stessa regione non devono collidere in cache.
        // iy ≤ 2^depth ≤ 64 < 2^7 → occupa i bit 32-38, quindi bodyId da bit 40 in su è libero.
        return (long)nd.face | ((long)nd.depth << 3) | (ix << 8) | (iy << 32) | ((long)bodyId << 40);
    }

    // id REGIONE compatto e collision-free, ≤ 2^23 (= esatto in FLOAT, così il vertex shader lo confronta senza errore):
    // bodyId | face | depth | ix | iy. Lo scrive il fill nella fetta (slabRegion) e lo porta l'istanza (dir.w); se non
    // combaciano, la fetta tiene la geometria di una regione VECCHIA (churn) → il vertice si collassa. ≤7 corpi in 2^23.
    float RegionId(Node nd)
    {
        int N = 1 << nd.depth;
        int ix = Mathf.RoundToInt(nd.u0 * N);
        int iy = Mathf.RoundToInt(nd.v0 * N);
        return bodyId * 1048576 + ((nd.face * 8 + nd.depth) * 128 + ix) * 128 + iy;
    }

    /// <summary>Alloca il pool CONDIVISO la prima volta (refcount), o lo riusa, e ne fa alias i campi per-corpo
    /// (posBuf…surfBuf, freeSlabs/cacheSlab/lru). Assegna un bodyId univoco per la chiave di cache.</summary>
    void AcquirePool()
    {
        if (sRefCount > 0 && vertsPerSlab != sPoolVerts)
            Debug.LogError($"GpuPlanetRenderer: pool condiviso con vertsPerSlab diverso ({vertsPerSlab} vs {sPoolVerts}) — tutti i corpi devono avere lo stesso nodeRes, le fette non sarebbero allineate.");
        if (sRefCount == 0)
        {
            int totalVerts = maxSlabs * vertsPerSlab;
            sPos = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts * 3, 4);
            sNrm = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts * 3, 4);
            sBedNrm = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts * 3, 4);
            sDepth = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts, 4);
            sField = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts, 4);
            sSurf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts, 4);
            sSlabRegion = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxSlabs, 4);   // marchio di regione per fetta
            sFreeSlabs.Clear();
            for (int i = maxSlabs - 1; i >= 0; i--) sFreeSlabs.Push(i);
            sCacheSlab.Clear(); sLru.Clear(); sLruNode.Clear();
            sPoolVerts = vertsPerSlab; sPoolSlabs = maxSlabs;
            long poolBytes = (long)maxSlabs * vertsPerSlab * 48L;   // pos+nrm+bedNrm (3 float) + depth+field+surf (1) = 48 byte/vertice
            Debug.Log($"[GpuPlanet] pool VRAM CONDIVISO ~{poolBytes / (1024 * 1024)} MB (nodeRes={nodeRes}, maxSlabs={maxSlabs}, vertsPerSlab={vertsPerSlab}) — UNO per tutti i corpi");
        }
        sRefCount++;
        posBuf = sPos; nrmBuf = sNrm; bedNrmBuf = sBedNrm; depthBuf = sDepth; fieldBuf = sField; surfBuf = sSurf;
        slabRegion = sSlabRegion;
        freeSlabs = sFreeSlabs; cacheSlab = sCacheSlab; lru = sLru; lruNode = sLruNode;
        bodyId = sNextBodyId++;
    }

    /// <summary>Rilascia il pool condiviso quando l'ULTIMO corpo sparisce (refcount → 0). I corpi del gioco
    /// nascono e muoiono tutti insieme (lifecycle di scena) → al refcount 0 il pool si libera per intero.</summary>
    void ReleasePool()
    {
        if (sRefCount == 0) return;
        sRefCount--;
        if (sRefCount == 0)
        {
            sPos?.Release(); sNrm?.Release(); sBedNrm?.Release(); sDepth?.Release(); sField?.Release(); sSurf?.Release(); sSlabRegion?.Release();
            sPos = sNrm = sBedNrm = sDepth = sField = sSurf = null;
            sFreeSlabs.Clear(); sCacheSlab.Clear(); sLru.Clear(); sLruNode.Clear(); sNextBodyId = 0;
        }
    }

    int AllocSlab()
    {
        if (freeSlabs.Count > 0) return freeSlabs.Pop();
        // pool pieno: sfratta in O(1) la regione meno recente = il FRONTE della lista LRU (niente più scansione del dizionario)
        if (lru.Count > 0)
        {
            long evKey = lru.First.Value;
            lru.RemoveFirst(); lruNode.Remove(evKey);
            int s = cacheSlab[evKey]; cacheSlab.Remove(evKey);
            return s;
        }
        return -1;
    }

    /// <summary>Dà al nodo una fetta pronta: dalla CACHE se la regione c'è (già riempita, niente dispatch),
    /// altrimenti una libera e la riempie.</summary>
    void AcquireSlab(Node nd)
    {
        long key = Key(nd);
        if (cacheSlab.TryGetValue(key, out int s)) { cacheSlab.Remove(key); lru.Remove(lruNode[key]); lruNode.Remove(key); nd.slab = s; return; }
        nd.slab = AllocSlab();
        FillSlab(nd);
    }

    /// <summary>La fetta del nodo esce di vista: la METTE IN CACHE (riuso futuro) invece di buttarla.</summary>
    void ReleaseSlab(Node nd)
    {
        if (nd.slab < 0) return;
        long key = Key(nd);
        if (cacheSlab.ContainsKey(key)) freeSlabs.Push(nd.slab);   // duplicato improbabile: libera
        else { cacheSlab[key] = nd.slab; lruNode[key] = lru.AddLast(key); }   // in coda = più recente
        nd.slab = -1;
    }

    /// <summary>Riempie la fetta del nodo. In batch: ACCODA il job (un solo dispatch al FlushFills di fine frame).
    /// Altrimenti: dispatch per-nodo immediato (path sicuro, default).</summary>
    void FillSlab(Node nd)
    {
        if (nd.slab < 0) return;
        fillsThisFrame++;
        if (batchReady && jobCount < jobScratch.Length) { jobScratch[jobCount++] = MakeJob(nd); return; }
        FillSlabImmediate(nd);
    }

    // profondità dello skirt. UNA sola formula condivisa da per-nodo e batch → restano IDENTICI (parità). Cap a
    // worldSize·3 (non più worldSize, che annullava skirtFactor>1): su pareti ripide serve uno skirt più profondo
    // del nodo per coprire il salto di LOD.
    float SkirtDrop(Node nd) => Mathf.Clamp(nd.worldSize * skirtFactor, 1f, nd.worldSize);

    NodeJob MakeJob(Node nd) => new NodeJob
    {
        faceUp = nd.up, axisA = nd.axisA, axisB = nd.axisB,
        uv = new Vector4(nd.u0, nd.v0, nd.size / nodeRes, nd.slab * vertsPerSlab),
        misc = new Vector4(SkirtDrop(nd), 0f, nd.slab, RegionId(nd)),   // misc.z = indice fetta, misc.w = id regione (region-stamp)
    };

    /// <summary>Fill PER-NODO immediato (2 dispatch, uniform per-nodo). Path classico, sicuro.</summary>
    void FillSlabImmediate(Node nd)
    {
        if (nd.slab < 0) return;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();   // diagnosi: misura il costo CPU delle chiamate del fill
        cs.SetVector(ID_FaceUp, nd.up);
        cs.SetVector(ID_AxisA, nd.axisA);
        cs.SetVector(ID_AxisB, nd.axisB);
        cs.SetFloat(ID_U0, nd.u0);
        cs.SetFloat(ID_V0, nd.v0);
        cs.SetFloat(ID_Step, nd.size / nodeRes);
        cs.SetInt(ID_NSlabOff, nd.slab * vertsPerSlab);
        cs.SetFloat(ID_NSkirtDrop, SkirtDrop(nd));
        cs.SetInt("_SlabIndex", nd.slab);             // region-stamp (path per-nodo): il fill marchia questa fetta
        cs.SetFloat("_SlabRegionId", RegionId(nd));
        int g = (n + 7) / 8;
        cs.Dispatch(kSlab, g, g, 1);
        cs.Dispatch(kSkirt, (4 * nodeRes + 63) / 64, 1, 1);
        fillTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }

    /// <summary>Manda in UN dispatch (slab + skirt) tutti i job accumulati nel frame. Il nodo è sull'asse z (slab) /
    /// y (skirt) del dispatch → ogni gruppo legge i suoi parametri da _Jobs[nodo]. Niente per-nodo SetX/Dispatch.</summary>
    void FlushFills()
    {
        if (!batchReady || jobCount == 0) return;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        jobsBuf.SetData(jobScratch, 0, 0, jobCount);
        int g = (n + 7) / 8;
        cs.Dispatch(kSlabBatch, g, g, jobCount);
        cs.Dispatch(kSkirtBatch, (4 * nodeRes + 63) / 64, jobCount, 1);
        fillTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        jobCount = 0;
    }

    /// <summary>BANCO DI VERIFICA (R1): riempie i 6 root col path PER-NODO e col path BATCH e confronta i vertici
    /// (readback). Se combaciano sub-cm → il batch è corretto e si può usare. Inchioda il bug d'indicizzazione che
    /// l'altra volta corruppe la geometria. Costa qualche readback sincrono, ma è UNA volta sola all'avvio.</summary>
    bool VerifyBatchFill()
    {
        int g = (n + 7) / 8;
        // confronta TUTTI i buffer scritti dai fill, non solo le posizioni: un bug d'indicizzazione che sposta
        // _VPos lo prende anche pos, ma un errore nei VALORI di normali/profondità/pelo passerebbe inosservato e
        // si vedrebbe come luce/acqua sbagliata (più subdolo di uno spuntone). pos/nrm/bedNrm = 3 float/v, gli altri 1.
        var bufs = new[] { posBuf, nrmBuf, bedNrmBuf, depthBuf, fieldBuf, surfBuf };
        var pers = new[] { 3, 3, 3, 1, 1, 1 };
        var names = new[] { "pos", "nrm", "bedNrm", "depth", "field", "surf" };

        // A = PER-NODO per ogni root, salvo tutti i buffer (è il noto-corretto)
        var roots2 = new List<Node>();
        foreach (var r in roots) if (r.slab >= 0) { FillSlabImmediate(r); roots2.Add(r); }
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
        cs.Dispatch(kSlabBatch, g, g, jobCount);
        cs.Dispatch(kSkirtBatch, (4 * nodeRes + 63) / 64, jobCount, 1);

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

    bool Split(Node nd)
    {
        if (freeSlabs.Count + cacheSlab.Count < 4) return false;   // niente fette disponibili → resta foglia
        ReleaseSlab(nd);                          // il nodo diventa interno: la sua fetta va in cache
        float h = nd.size * 0.5f;
        nd.children = childPool.Count > 0 ? childPool.Pop() : new Node[4];   // dal pool → niente GC del Node[4] a ogni split
        for (int i = 0; i < 4; i++)               // niente array temporanei (GC): u/v dei 4 quadranti a mano
        {
            float cuX = (i == 1 || i == 3) ? nd.u0 + h : nd.u0;
            float cvY = (i >= 2) ? nd.v0 + h : nd.v0;
            var c = MakeNode(nd.face, cuX, cvY, h, nd.depth + 1);
            AcquireSlab(c);                       // dalla cache se c'è, altrimenti riempi
            nd.children[i] = c;
        }
        return true;
    }

    void DisposeSubtree(Node nd)
    {
        if (nd.children != null)
        {
            for (int i = 0; i < 4; i++) DisposeSubtree(nd.children[i]);
            childPool.Push(nd.children);   // riusa l'array figli
            nd.children = null;
        }
        ReleaseSlab(nd);     // in cache (riuso), non buttata
        nodePool.Push(nd);   // l'oggetto Node torna al pool (le radici non passano mai di qui) → zero GC
    }

    void Merge(Node nd)
    {
        for (int i = 0; i < 4; i++) DisposeSubtree(nd.children[i]);
        childPool.Push(nd.children);   // riusa l'array figli
        nd.children = null;
        AcquireSlab(nd);   // riusa la fetta della regione se è in cache, altrimenti riempi
    }

    // SELEZIONE LOD = CDLOD puro (una passata, per distanza + horizon culling). Crack-free SENZA toppe: lo garantisce il
    // MORPH CONTINUO nel vertex shader (mf è funzione continua della distanza, uguale per ogni foglia dello stesso
    // livello → due vicine alla stessa distanza combaciano) + confini di LOD NETTI (mergeHysteresis=1: split e merge alla
    // STESSA soglia, niente banda morta che farebbe morfare i due lati a misure diverse). Niente skirt, bilanciamento,
    // stitch: il morph fa tutto. Il flip split/merge alla soglia è VISIVAMENTE invisibile (lì mf=1 = forma del genitore su
    // entrambi i lati) e a costo ~zero (la fetta è in cache LRU → ri-acquisirla non rigenera la geometria).
    void UpdateLod(Node nd)
    {
        Vector3 centerWorld = lodM.MultiplyPoint3x4(nd.centerLocal);

        // HORIZON CULLING dalla posizione REALE della camera: è visibilità, non scelta di dettaglio (gate depth>=2:
        // le radici hanno il centro lontano anche se un bordo è vicino).
        if (nd.depth >= 2 && BeyondHorizon(nd, centerWorld))
        {
            if (nd.children != null) Merge(nd);   // collassa il lato occluso → libera fette per il lato visibile
            return;                                // non disegnato (dietro la curvatura)
        }

        float dist = Vector3.Distance(lodCamLook, centerWorld);   // dettaglio = dove SARAI (predittivo), non dove sei
        float splitDist = nd.worldSize * lodFactor;

        if (nd.children != null)
        {
            if (dist > splitDist * mergeHysteresis) { Merge(nd); AddVisible(nd); }
            else for (int i = 0; i < 4; i++) UpdateLod(nd.children[i]);
        }
        else if (nd.depth < maxDepth && dist < splitDist && splitsThisFrame < splitBudget && Split(nd))
        {
            splitsThisFrame++;
            for (int i = 0; i < 4; i++) UpdateLod(nd.children[i]);
        }
        else AddVisible(nd);
    }

    void AddVisible(Node nd)
    {
        if (nd.slab >= 0 && visibleCount < visibleScratch.Length)
        {
            splitScratch[visibleCount] = nd.worldSize * lodFactor;   // morph: distanza di split del nodo (per istanza)
            Vector3 cd = nd.centerLocal.normalized;                  // anti-spuntone: direzione-centro del nodo (spazio oggetto)
            dirScratch[visibleCount] = new Vector4(cd.x, cd.y, cd.z, RegionId(nd));   // .w = id regione attesa (region-stamp)
            visibleScratch[visibleCount] = (uint)nd.slab;
            visibleCount++;
        }
    }

    /// <summary>Il nodo è oltre l'orizzonte (occluso dalla curvatura)? Test in ANGOLI: l'angolo del nodo dalla
    /// verticale-camera supera l'angolo dell'orizzonte + mezza estensione + margine. **ISTERESI** (stato per-nodo):
    /// serve di più per NASCONDERE che per RI-MOSTRARE → niente flip cancella/ricrea ai micro-cambi di quota (a
    /// quota bassa l'orizzonte è sensibilissimo). Le creste che bucano l'orizzonte sono gestite con precisione da
    /// **lodPeakAngle** (acos(R/(R+maxH)) sommato alla soglia): senza, la curvatura tagliava i picchi → tessere NERE.</summary>
    bool BeyondHorizon(Node nd, Vector3 nodeWorld)
    {
        if (!lodHorizonValid) { nd.horizonHidden = false; return false; }   // camera troppo bassa: niente culling
        Vector3 nodeDir = (nodeWorld - lodCenter).normalized;
        float thetaNode = Mathf.Acos(Mathf.Clamp(Vector3.Dot(lodCamDir, nodeDir), -1f, 1f));
        float thetaR = (nd.worldSize * 0.5f) / radius;   // mezza estensione angolare del nodo
        float baseT = lodThetaHorizon + thetaR + lodPeakAngle;   // orizzonte + estensione nodo + spunto dei picchi (height-aware)
        // isteresi anti-flicker: ora che i PICCHI sono gestiti da lodPeakAngle, qui basta una banda morta PICCOLA
        // (se già nascosto rientra a 0.02, se visibile si nasconde a 0.05) → niente oscillazione ai micro-cambi di
        // quota, senza il vecchio margine grosso che sovra-disegnava (e gonfiava il set visibile).
        float margin = nd.horizonHidden ? 0.02f : 0.05f;
        nd.horizonHidden = thetaNode > baseT + margin;
        return nd.horizonHidden;
    }

    /// <summary>Sospende il disegno di TUTTE le superfici GPU (lo accende MapMode in mappa). RenderPrimitivesIndexed‑
    /// Indirect entra in OGNI camera attiva → senza questo la superficie reale del pianeta comparirebbe in mappa,
    /// sopra/accanto ai proxy a dimensione-costante (sembravano di taglie diverse). In mappa la camera del giocatore
    /// è spenta, quindi non c'è nulla da disegnare comunque.</summary>
    public static bool SuppressDraw;

    void Update()
    {
        if (!Ready) return;
        if (SuppressDraw) return;   // mappa aperta: niente disegno della superficie GPU
        // Dopo un domain reload in Play (es. una mia modifica ricompilata MENTRE sei in Play) gli stati statici si
        // azzerano (sRefCount→0, pool perso) e i binding del compute si invalidano, ma Ready resta dal backup →
        // senza guardia i fill dispatcherebbero con buffer non legati ("_Craters non impostato at kernel 2/3"). Skip
        // finché non si rifà il Setup (ri-Play / ricrea scena). DEV-ONLY: in una build non c'è domain reload.
        if (sRefCount == 0 || mat == null || posBuf == null || !posBuf.IsValid() || idxBuf == null || !idxBuf.IsValid() || argsBuf == null) return;
        if (cam == null) { var c = Camera.main; if (c == null) return; cam = c.transform; }

        Matrix4x4 m = planetTf.localToWorldMatrix;
        // EARLY-OUT sub-pixel: un corpo così lontano da occupare meno di ~1 pixel NON fa nulla (niente refresh uniform
        // su 2 materiali, niente traversata, niente draw, niente SetData). Converte il costo per-frame da O(corpi) a
        // O(corpi VICINI) — il termine che cresce con tanti corpi / più sistemi solari. In mappa lo mostra il proxy.
        // Raggio angolare ≈ radius/dist; sotto ~0.0006 rad è sotto al pixel (a FOV/risoluzione tipici). Niente isteresi:
        // alla soglia il corpo è già invisibile, un eventuale flip è impercettibile.
        Vector3 bodyCenter = m.MultiplyPoint3x4(Vector3.zero);
        if (radius < Vector3.Distance(cam.position, bodyCenter) * 0.0006f) return;
        // per-frame su ENTRAMBI i materiali (interno e skirt sono ombreggiati uguale). mat porta il _Cull dell'interno
        // (Back se cullSplit, altrimenti Off = comportamento a draw singolo). matSkirt resta Cull Off (impostato nel Setup).
        // VISTE DEBUG: accendi la variante (keyword) solo quando serve, e solo al CAMBIO (toggle keyword ogni frame =
        // spreco). In gioco (DebugView=0) la keyword è spenta → la variante senza codice di diagnosi = costo zero.
        if (DebugView != lastDebugView)
        {
            lastDebugView = DebugView;
            if (DebugView > 0) { mat.EnableKeyword("PLANET_DEBUG_VIEW"); if (matSkirt != null) matSkirt.EnableKeyword("PLANET_DEBUG_VIEW"); }
            else               { mat.DisableKeyword("PLANET_DEBUG_VIEW"); if (matSkirt != null) matSkirt.DisableKeyword("PLANET_DEBUG_VIEW"); }
        }
        mat.SetMatrix("_ObjectToWorld", m);
        mat.SetVector("_CamPosWorld", cam.position);   // geomorph: distanza camera per il fattore di morph (shader dai-buffer)
        mat.SetFloat("_DebugView", DebugView);
        mat.SetInt("_Cull", CullSplit ? InteriorCull : 0);
        RefreshLighting(mat);
        RefreshTorch(mat);
        if (CullSplit && matSkirt != null)
        {
            matSkirt.SetMatrix("_ObjectToWorld", m);
            matSkirt.SetVector("_CamPosWorld", cam.position);
            matSkirt.SetFloat("_DebugView", DebugView);
            RefreshLighting(matSkirt);
            RefreshTorch(matSkirt);
        }

        // LOD: decidi split/merge e raccogli le foglie visibili
        var sw = System.Diagnostics.Stopwatch.StartNew();   // diagnosi: costo CPU del mio lavoro per frame
        Vector3 camWorld = cam.position;
        Vector3 planetCenter = m.MultiplyPoint3x4(Vector3.zero);

        // LOD PREDITTIVO: punto "dove sarai fra lookaheadTime". La velocità si misura sulla posizione RELATIVA al
        // centro del pianeta (camRel): stabile con la floating origin (origine e centro shiftano insieme → camRel no)
        // e col pianeta che orbita (= moto del giocatore rispetto al pianeta). Guardia sui salti (cambio ancora/teleport).
        Vector3 camRel = camWorld - planetCenter;
        Vector3 camLook = camWorld;
        if (hasPrevCamRel && Time.deltaTime > 1e-4f)
        {
            Vector3 disp = (camRel - prevCamRel) * (lookaheadTime / Time.deltaTime);
            if (disp.magnitude > radius) disp = disp.normalized * radius;   // niente lookahead assurdo su un salto
            camLook = camWorld + disp;
        }
        prevCamRel = camRel;
        hasPrevCamRel = true;

        // contesto LOD del frame (calcolato UNA volta, non per nodo): la traversata legge questi campi.
        lodM = m;
        lodCamLook = camLook;
        lodCenter = planetCenter;
        float camDist = camRel.magnitude;
        lodHorizonValid = camDist >= radius * 1.001f;
        if (lodHorizonValid)
        {
            lodCamDir = camRel / camDist;
            lodThetaHorizon = Mathf.Acos(Mathf.Clamp(radius / camDist, 0f, 1f));
            // HEIGHT-AWARE: i picchi (altezza maxH sopra il raggio) restano visibili oltre l'orizzonte SFERICO di
            // acos(R/(R+maxH)). Su corpi piccoli è grande (R=500,maxH=25 → ~18°): senza, la curvatura "mangia" le
            // creste ancora visibili a quota → tessere NERE all'orizzonte in viaggio. maxH stimato generoso (base +
            // crateri/tettonica), limitato a [3%,10%] del raggio per non sovra-disegnare troppo. La GPU ha margine.
            float maxH = Mathf.Clamp(terrain.Amplitude * 3f, radius * 0.04f, radius * 0.12f);
            lodPeakAngle = Mathf.Acos(Mathf.Clamp(radius / (radius + maxH), 0f, 1f));
        }

        visibleCount = 0;
        splitsThisFrame = 0;
        fillsThisFrame = 0;
        fillTicks = 0;
        for (int f = 0; f < 6; f++) UpdateLod(roots[f]);   // CDLOD: una passata, seleziona+raccoglie (il morph continuo fa il crack-free)
        FlushFills();   // batch: i fill accumulati nella traversata partono ora, in un solo dispatch (no-op se per-nodo)
        double lodMs = sw.Elapsed.TotalMilliseconds;   // traversata + dispatch dei fill (logica LOD sulla CPU)
        if (visibleCount == 0) return;

        slabOfInstance.SetData(visibleScratch, 0, 0, visibleCount);
        splitDistOfInstance.SetData(splitScratch, 0, 0, visibleCount);   // geomorph: parallelo a slabOfInstance
        dirOfInstance.SetData(dirScratch, 0, 0, visibleCount);           // anti-spuntone: parallelo a slabOfInstance

        var worldBounds = new Bounds(planetCenter, Vector3.one * (radius * 5f));
        if (CullSplit && matSkirt != null)
        {
            // DUE materiali: INTERNO (mat, Cull Back → niente retro-facce ombreggiate = metà overdraw del fragment)
            // sui primi interiorIndexCount indici, + SKIRT (matSkirt, Cull Off) sul resto. Il Cull lo guida il
            // MATERIALE (deterministico, a differenza del MaterialPropertyBlock). Stessi buffer/uniform su entrambi.
            argsData[0].indexCountPerInstance = (uint)interiorIndexCount;
            argsData[0].instanceCount = (uint)visibleCount;
            argsData[0].startIndex = 0;
            argsBuf.SetData(argsData);
            argsSkirtData[0].indexCountPerInstance = (uint)(indexCountPerSlab - interiorIndexCount);
            argsSkirtData[0].instanceCount = (uint)visibleCount;
            argsSkirtData[0].startIndex = (uint)interiorIndexCount;
            argsSkirtBuf.SetData(argsSkirtData);
            var rpI = new RenderParams(mat) { worldBounds = worldBounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            Graphics.RenderPrimitivesIndexedIndirect(rpI, MeshTopology.Triangles, idxBuf, argsBuf, 1);
            if (DrawSkirts)   // DIAGNOSI: salta il draw degli skirt quando spento
            {
                var rpS = new RenderParams(matSkirt) { worldBounds = worldBounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
                Graphics.RenderPrimitivesIndexedIndirect(rpS, MeshTopology.Triangles, idxBuf, argsSkirtBuf, 1);
            }
        }
        else
        {
            // UN draw, tutto l'index buffer, Cull Off (mat._Cull=0). DIAGNOSI: solo l'interno se gli skirt sono spenti.
            argsData[0].indexCountPerInstance = (uint)(DrawSkirts ? indexCountPerSlab : interiorIndexCount);
            argsData[0].instanceCount = (uint)visibleCount;
            argsData[0].startIndex = 0;
            argsBuf.SetData(argsData);
            var rp = new RenderParams(mat) { worldBounds = worldBounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            Graphics.RenderPrimitivesIndexedIndirect(rp, MeshTopology.Triangles, idxBuf, argsBuf, 1);
        }

        // diagnosi: logga SOLO i frame "pesanti" (il mio lavoro CPU ≥ 4 ms) con quante patch ha generato.
        // Se vedi spesso fills alti o ms alti = è il LOD (CPU). Se i frame scattano ma qui i ms sono bassi = è la GPU.
        sw.Stop();
        double ms = sw.Elapsed.TotalMilliseconds;
        double fillMs = fillTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        double travMs = lodMs - fillMs;                 // logica di traversata pura (split/merge/orizzonte)
        double sendMs = ms - lodMs;                      // SetData + invio del draw (qui appare l'attesa-GPU)
        // accumula la scomposizione del frame (sommata sui corpi; reset al cambio di frame) per l'HUD
        if (Time.frameCount != sBreakdownFrame) { sBreakdownFrame = Time.frameCount; Trav = Fill = Send = 0f; }
        Trav += (float)travMs; Fill += (float)fillMs; Send += (float)sendMs;
        if (ms >= 4.0) Debug.Log($"[GpuPlanet {name}] CPU {ms:F1} ms (trav {travMs:F1} · fill {fillMs:F1} · invio {sendMs:F1}) · fills={fillsThisFrame} · visibili={visibleCount}");
    }

    /// <summary>Index buffer della topologia di UNA fetta: griglia interna n×n + anello di skirt. L'offset di
    /// fetta lo aggiunge il vertex shader via SV_InstanceID → un solo index buffer per tutte le istanze.</summary>
    void BuildIndexBuffer()
    {
        int per = 4 * nodeRes;
        var idx = new List<int>(nodeRes * nodeRes * 6 + per * 6);
        // interno (Cull Off → il verso non conta)
        for (int j = 0; j < nodeRes; j++)
            for (int i = 0; i < nodeRes; i++)
            {
                int i00 = i + j * n, i10 = (i + 1) + j * n, i01 = i + (j + 1) * n, i11 = (i + 1) + (j + 1) * n;
                idx.Add(i00); idx.Add(i01); idx.Add(i11);
                idx.Add(i00); idx.Add(i11); idx.Add(i10);
            }
        interiorIndexCount = idx.Count;   // tutto ciò che precede le skirt = i tris INTERNI (draw separato con Cull Back)
        // skirt: collega ogni vertice di bordo al suo vertice di skirt abbassato
        for (int k = 0; k < per; k++)
        {
            int a = PerimInterior(k);
            int b = PerimInterior((k + 1) % per);
            int as_ = skirtStart + k;
            int bs = skirtStart + (k + 1) % per;
            idx.Add(a); idx.Add(as_); idx.Add(bs);
            idx.Add(a); idx.Add(bs); idx.Add(b);
        }
        indexCountPerSlab = idx.Count;
        idxBuf = new GraphicsBuffer(GraphicsBuffer.Target.Index, indexCountPerSlab, 4);
        idxBuf.SetData(idx);
    }

    /// <summary>Indice (nella griglia interna n×n) del vertice di perimetro k. STESSA mappa di CSNodeSkirt.</summary>
    int PerimInterior(int k)
    {
        int res = nodeRes;
        int e = k / res, t = k % res, i, j;
        if (e == 0)      { i = t;       j = 0; }
        else if (e == 1) { i = res;     j = t; }
        else if (e == 2) { i = res - t; j = res; }
        else             { i = 0;       j = res - t; }
        return i + j * n;
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
        if (torch == null) { var fl = FindAnyObjectByType<Flashlight>(); if (fl != null) torch = fl.lamp; }
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

    /// <summary>STREAMING-SAFE: rende le fette di QUESTO corpo (vive nell'albero + in cache) alla free-list
    /// condivisa, così distruggere un SINGOLO corpo a metà sessione (teletrasporto/streaming futuro) non lascia
    /// fette "prese" nel pool condiviso finché non muoiono tutti. Se è l'ultimo, ReleasePool azzera comunque.</summary>
    void ReturnMySlabs()
    {
        if (cacheSlab == null) return;   // pool mai acquisito (Setup fallito): niente da rendere
        if (roots != null) for (int f = 0; f < 6; f++) FreeSubtreeSlabs(roots[f]);
        // voci di cache di QUESTO corpo: il bodyId sta nei bit alti della chiave (>> 40)
        if (cacheSlab.Count > 0)
        {
            var mine = new List<long>();
            foreach (var kv in cacheSlab) if ((kv.Key >> 40) == bodyId) mine.Add(kv.Key);
            foreach (var k in mine)
            {
                sFreeSlabs.Push(cacheSlab[k]); cacheSlab.Remove(k);
                if (lruNode.TryGetValue(k, out var node)) { lru.Remove(node); lruNode.Remove(k); }
            }
        }
    }

    void FreeSubtreeSlabs(Node nd)
    {
        if (nd == null) return;
        if (nd.children != null) for (int i = 0; i < 4; i++) FreeSubtreeSlabs(nd.children[i]);
        if (nd.slab >= 0) { sFreeSlabs.Push(nd.slab); nd.slab = -1; }
    }

    void OnDestroy()
    {
        ReturnMySlabs();   // streaming-safe: rendi le fette di questo corpo PRIMA di mollare il riferimento al pool
        ReleasePool();   // i 6 buffer geometria sono CONDIVISI e refcountati: liberati solo quando muore l'ultimo corpo
        jobsBuf?.Release();
        idxBuf?.Release(); slabOfInstance?.Release(); splitDistOfInstance?.Release(); dirOfInstance?.Release(); argsBuf?.Release(); argsSkirtBuf?.Release();
        shape?.Dispose();
        if (mat != null) Destroy(mat);
        if (matSkirt != null) Destroy(matSkirt);
        if (cs != null) Destroy(cs);   // l'istanza propria del compute
    }
}

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
    public float mergeHysteresis = 2f;   // banda morta larga: meno flip split/merge (meno churn)
    public int maxDepth = 6;
    public float skirtFactor = 0.5f;
    public int maxSlabs = 1024;  // fette nel pool (free + cache + visibili). Pre-allocate
    public int splitBudget = 8;  // quante tessere nuove al MASSIMO preparare per fotogramma (×4 fill): spalmando il
                                 // lavoro su più fotogrammi si evita l'ondata che fa scattare. Il LOD predittivo copre il "ritardo"
    public float lookaheadTime = 0.7f;   // LOD PREDITTIVO: valuta lo split dalla posizione DOVE SARAI fra ~tot secondi
                                         // (verso cui voli) → il dettaglio è pronto PRIMA di arrivarci (niente "carica tardi")

    /// <summary>DIAGNOSI: 0 = resa normale · 1 = posizione radiale (fragment banale) · 2 = normale di mondo.</summary>
    public int debugMode = 0;

    int n;             // nodeRes+1 (vertici per lato)
    int vertsPerSlab;  // n*n + 4*nodeRes (interno + skirt)
    int skirtStart;    // n*n
    int indexCountPerSlab;
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
    GraphicsBuffer posBuf, nrmBuf, bedNrmBuf, depthBuf, fieldBuf;   // POOL: maxSlabs * vertsPerSlab
    GraphicsBuffer idxBuf;                                // topologia di UNA fetta (interno + skirt), condivisa
    GraphicsBuffer slabOfInstance;                        // istanza visibile → indice di fetta
    GraphicsBuffer argsBuf;
    GraphicsBuffer.IndirectDrawIndexedArgs[] argsData = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
    GpuShapeBuffers shape;
    Material mat;
    Transform planetTf;
    PlanetTerrain terrain;

    readonly Stack<int> freeSlabs = new Stack<int>();
    // CACHE LRU: una regione (face,depth,u0,v0) che esce di vista NON si butta — la sua fetta (già riempita, la
    // geometria è statica) resta in cache e si RIUSA al ritorno → niente ricalcolo (era il "continua a ridisegnarsi").
    readonly Dictionary<long, int> cacheSlab = new Dictionary<long, int>();
    readonly Dictionary<long, int> cacheClock = new Dictionary<long, int>();
    int clock;
    // pool degli oggetti Node: split/merge in movimento NON allocano più (niente GC → niente stallo periodico).
    readonly Stack<Node> nodePool = new Stack<Node>();
    Node[] roots;
    uint[] visibleScratch;
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
    bool lodHorizonValid;     // false se la camera è troppo bassa (sotto il raggio) → niente culling all'orizzonte
    int splitsThisFrame;
    int fillsThisFrame;   // diagnosi: quante fette riempite (dispatch) in questo frame
    long fillTicks;       // diagnosi: tempo CPU speso nelle chiamate dei fill (SetX+Dispatch), per separarlo dalla traversata

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
        nodeRes = Mathf.Clamp(res, 4, 128);
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

        int totalVerts = maxSlabs * vertsPerSlab;
        posBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts * 3, 4);
        nrmBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts * 3, 4);
        bedNrmBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts * 3, 4);
        depthBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts, 4);
        fieldBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts, 4);   // baseN per-vertice
        foreach (int k in new[] { kSlab, kSkirt })
        {
            cs.SetBuffer(k, "_VPos", posBuf);
            cs.SetBuffer(k, "_VNrm", nrmBuf);
            cs.SetBuffer(k, "_VBedNrm", bedNrmBuf);
            cs.SetBuffer(k, "_VDepth", depthBuf);
            cs.SetBuffer(k, "_VField", fieldBuf);
        }
        shape = GpuShapeBuffers.Build(cs, terrain, new[] { kSlab, kSkirt });   // base+ricetta, parità col walker
        cs.SetInt("_NN", n);                 // costanti per i kernel dei fill (non più per-fill)
        cs.SetInt("_NSkirtStart", skirtStart);

        BuildIndexBuffer();

        // free-list: tutte le fette libere all'inizio
        freeSlabs.Clear();
        for (int i = maxSlabs - 1; i >= 0; i--) freeSlabs.Push(i);
        visibleScratch = new uint[maxSlabs];

        slabOfInstance = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxSlabs, 4);
        argsBuf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);

        mat = new Material(sh);
        mat.SetBuffer("_VPos", posBuf);
        mat.SetBuffer("_VNrm", nrmBuf);
        mat.SetBuffer("_VBedNrm", bedNrmBuf);
        mat.SetBuffer("_VDepth", depthBuf);
        mat.SetBuffer("_VField", fieldBuf);
        mat.SetBuffer("_SlabOfInstance", slabOfInstance);
        mat.SetInt("_VertsPerSlab", vertsPerSlab);
        mat.SetFloat("_PerVertexFields", 1f);   // in gioco: usa baseN per-vertice (fragment più economico)

        radius = terrain.Recipe != null ? terrain.Recipe.baseRadius : terrain.BaseRadius;
        cs.SetInt("_HasSea", terrain.Recipe != null && terrain.Recipe.LastSea() != null ? 1 : 0);

        // 6 facce-radice, ciascuna con la sua fetta riempita
        roots = new Node[6];
        for (int f = 0; f < 6; f++)
        {
            roots[f] = MakeNode(f, 0f, 0f, 1f, 0);
            AcquireSlab(roots[f]);   // cache vuota all'avvio → alloca e riempie
        }

        ApplyColor();
        Ready = true;
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
    static long Key(Node nd)
    {
        int N = 1 << nd.depth;
        long ix = Mathf.RoundToInt(nd.u0 * N);
        long iy = Mathf.RoundToInt(nd.v0 * N);
        return (long)nd.face | ((long)nd.depth << 3) | (ix << 8) | (iy << 32);
    }

    int AllocSlab()
    {
        if (freeSlabs.Count > 0) return freeSlabs.Pop();
        // pool pieno: sfratta la regione MENO usata di recente dalla cache
        if (cacheSlab.Count > 0)
        {
            long evKey = 0; int min = int.MaxValue;
            foreach (var kv in cacheClock) if (kv.Value < min) { min = kv.Value; evKey = kv.Key; }
            int s = cacheSlab[evKey]; cacheSlab.Remove(evKey); cacheClock.Remove(evKey);
            return s;
        }
        return -1;
    }

    /// <summary>Dà al nodo una fetta pronta: dalla CACHE se la regione c'è (già riempita, niente dispatch),
    /// altrimenti una libera e la riempie.</summary>
    void AcquireSlab(Node nd)
    {
        long key = Key(nd);
        if (cacheSlab.TryGetValue(key, out int s)) { cacheSlab.Remove(key); cacheClock.Remove(key); nd.slab = s; return; }
        nd.slab = AllocSlab();
        FillSlab(nd);
    }

    /// <summary>La fetta del nodo esce di vista: la METTE IN CACHE (riuso futuro) invece di buttarla.</summary>
    void ReleaseSlab(Node nd)
    {
        if (nd.slab < 0) return;
        long key = Key(nd);
        if (cacheSlab.ContainsKey(key)) freeSlabs.Push(nd.slab);   // duplicato improbabile: libera
        else { cacheSlab[key] = nd.slab; cacheClock[key] = ++clock; }
        nd.slab = -1;
    }

    /// <summary>Riempie la fetta del nodo sulla GPU (griglia interna + skirt), un dispatch per nodo. Niente readback.</summary>
    void FillSlab(Node nd)
    {
        if (nd.slab < 0) return;
        fillsThisFrame++;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();   // diagnosi: misura il costo CPU delle chiamate del fill
        cs.SetVector(ID_FaceUp, nd.up);
        cs.SetVector(ID_AxisA, nd.axisA);
        cs.SetVector(ID_AxisB, nd.axisB);
        cs.SetFloat(ID_U0, nd.u0);
        cs.SetFloat(ID_V0, nd.v0);
        cs.SetFloat(ID_Step, nd.size / nodeRes);
        cs.SetInt(ID_NSlabOff, nd.slab * vertsPerSlab);
        cs.SetFloat(ID_NSkirtDrop, Mathf.Clamp(nd.worldSize * skirtFactor, 1f, nd.worldSize));
        int g = (n + 7) / 8;
        cs.Dispatch(kSlab, g, g, 1);
        cs.Dispatch(kSkirt, (4 * nodeRes + 63) / 64, 1, 1);
        fillTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }

    bool Split(Node nd)
    {
        if (freeSlabs.Count + cacheSlab.Count < 4) return false;   // niente fette disponibili → resta foglia
        ReleaseSlab(nd);                          // il nodo diventa interno: la sua fetta va in cache
        float h = nd.size * 0.5f;
        nd.children = new Node[4];
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
            nd.children = null;
        }
        ReleaseSlab(nd);     // in cache (riuso), non buttata
        nodePool.Push(nd);   // l'oggetto Node torna al pool (le radici non passano mai di qui) → zero GC
    }

    void Merge(Node nd)
    {
        for (int i = 0; i < 4; i++) DisposeSubtree(nd.children[i]);
        nd.children = null;
        AcquireSlab(nd);   // riusa la fetta della regione se è in cache, altrimenti riempi
    }

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
            if (dist > splitDist * mergeHysteresis)
            {
                Merge(nd);
                AddVisible(nd);
            }
            else
            {
                for (int i = 0; i < 4; i++) UpdateLod(nd.children[i]);
            }
        }
        else
        {
            if (nd.depth < maxDepth && dist < splitDist && splitsThisFrame < splitBudget && Split(nd))
            {
                splitsThisFrame++;
                for (int i = 0; i < 4; i++) UpdateLod(nd.children[i]);
            }
            else AddVisible(nd);
        }
    }

    void AddVisible(Node nd)
    {
        if (nd.slab >= 0 && visibleCount < visibleScratch.Length)
            visibleScratch[visibleCount++] = (uint)nd.slab;
    }

    /// <summary>Il nodo è oltre l'orizzonte (occluso dalla curvatura)? Test in ANGOLI: l'angolo del nodo dalla
    /// verticale-camera supera l'angolo dell'orizzonte + mezza estensione + margine. **ISTERESI** (stato per-nodo):
    /// serve di più per NASCONDERE che per RI-MOSTRARE → niente flip cancella/ricrea ai micro-cambi di quota (a
    /// quota bassa l'orizzonte è sensibilissimo). Il margine alto fa anche da grossolano "le creste bucano
    /// l'orizzonte" (un nodo appena oltre ha ancora picchi visibili) finché non arriva il fix height-aware vero.</summary>
    bool BeyondHorizon(Node nd, Vector3 nodeWorld)
    {
        if (!lodHorizonValid) { nd.horizonHidden = false; return false; }   // camera troppo bassa: niente culling
        Vector3 nodeDir = (nodeWorld - lodCenter).normalized;
        float thetaNode = Mathf.Acos(Mathf.Clamp(Vector3.Dot(lodCamDir, nodeDir), -1f, 1f));
        float thetaR = (nd.worldSize * 0.5f) / radius;   // mezza estensione angolare del nodo
        float baseT = lodThetaHorizon + thetaR;          // angolo orizzonte calcolato UNA volta per frame (non per nodo)
        // isteresi: se già nascosto resta nascosto finché non rientra ben dentro (marginLow); se visibile si
        // nasconde solo quando è ben oltre (marginHigh). Banda morta = niente oscillazione.
        float margin = nd.horizonHidden ? 0.04f : 0.14f;
        nd.horizonHidden = thetaNode > baseT + margin;
        return nd.horizonHidden;
    }

    void Update()
    {
        if (!Ready) return;
        // Dopo un domain reload in Play i GraphicsBuffer (non serializzabili) tornano null mentre Ready resta:
        // non disegnare finché non sono validi.
        if (mat == null || posBuf == null || !posBuf.IsValid() || idxBuf == null || !idxBuf.IsValid() || argsBuf == null) return;
        if (cam == null) { var c = Camera.main; if (c == null) return; cam = c.transform; }

        Matrix4x4 m = planetTf.localToWorldMatrix;
        mat.SetMatrix("_ObjectToWorld", m);
        mat.SetFloat("_DebugView", debugMode);
        RefreshLighting();
        RefreshTorch();

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
        }

        visibleCount = 0;
        splitsThisFrame = 0;
        fillsThisFrame = 0;
        fillTicks = 0;
        for (int f = 0; f < 6; f++) UpdateLod(roots[f]);
        double lodMs = sw.Elapsed.TotalMilliseconds;   // traversata + dispatch dei fill (logica LOD sulla CPU)
        if (visibleCount == 0) return;

        slabOfInstance.SetData(visibleScratch, 0, 0, visibleCount);
        argsData[0].indexCountPerInstance = (uint)indexCountPerSlab;
        argsData[0].instanceCount = (uint)visibleCount;
        argsBuf.SetData(argsData);

        var worldBounds = new Bounds(planetCenter, Vector3.one * (radius * 5f));
        var rp = new RenderParams(mat)
        {
            worldBounds = worldBounds,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false
        };
        Graphics.RenderPrimitivesIndexedIndirect(rp, MeshTopology.Triangles, idxBuf, argsBuf, 1);

        // diagnosi: logga SOLO i frame "pesanti" (il mio lavoro CPU ≥ 4 ms) con quante patch ha generato.
        // Se vedi spesso fills alti o ms alti = è il LOD (CPU). Se i frame scattano ma qui i ms sono bassi = è la GPU.
        sw.Stop();
        double ms = sw.Elapsed.TotalMilliseconds;
        double fillMs = fillTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        double travMs = lodMs - fillMs;                 // logica di traversata pura (split/merge/orizzonte)
        double sendMs = ms - lodMs;                      // SetData + invio del draw (qui appare l'attesa-GPU)
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
            var sea = rec.LastSea();
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

    void RefreshLighting()
    {
        if (mat == null) return;
        if (sun == null && SunLight.Instance != null) sun = SunLight.Instance.GetComponent<Light>();
        Vector3 dir = sun != null ? -sun.transform.forward : Vector3.up;
        Color sc = sun != null ? sun.color * sun.intensity : Color.white;
        mat.SetVector("_SunDir", dir.normalized);
        mat.SetVector("_SunColor", new Vector4(sc.r, sc.g, sc.b, 1f));
        Color amb = RenderSettings.ambientLight;
        mat.SetVector("_Ambient", new Vector4(amb.r, amb.g, amb.b, 1f));
    }

    void RefreshTorch()
    {
        if (mat == null) return;
        if (torch == null) { var fl = FindAnyObjectByType<Flashlight>(); if (fl != null) torch = fl.lamp; }
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

    void OnDestroy()
    {
        posBuf?.Release(); nrmBuf?.Release(); bedNrmBuf?.Release(); depthBuf?.Release(); fieldBuf?.Release();
        idxBuf?.Release(); slabOfInstance?.Release(); argsBuf?.Release();
        shape?.Dispose();
        if (mat != null) Destroy(mat);
        if (cs != null) Destroy(cs);   // l'istanza propria del compute
    }
}

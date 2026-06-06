using UnityEngine;

/// <summary>Chi sa trasformare la regione di un nodo-foglia in geometria nella sua fetta del pool (un dispatch del
/// compute). Lo implementa GpuPlanetRenderer (possiede il ComputeShader). Il quadtree NON conosce la GPU: quando una
/// foglia nuova ha bisogno di geometria, chiama FillSlab e basta. Così #18 (god-object) resta spaccato in modo netto:
/// SlabPool = memoria, PlanetLodTree = topologia/LOD, GpuPlanetRenderer = compute + draw.</summary>
public interface ISlabFiller
{
    void FillSlab(PlanetLodTree.Node nd);
}

/// <summary>
/// QUADTREE GPU su cubo-sfera (estratto da GpuPlanetRenderer, #18): 6 facce-radice, ogni nodo è una patch
/// [u0,u0+size]×[v0,v0+size] del dominio parametrico della faccia. Si suddivide quando la camera è vicina → densità
/// che segue la distanza: fitta sotto i piedi (crateri nitidi), rada all'orizzonte, CULLATA dietro la curvatura.
/// Ogni nodo-foglia possiede una FETTA del pool (la griglia interna). La lista delle foglie visibili (VisibleSlabs…)
/// la consuma il renderer per un solo draw indiretto.
///
/// SELEZIONE LOD = CDLOD puro (una passata, per distanza + horizon culling). Crack-free SENZA toppe: lo garantisce il
/// MORPH CONTINUO nel vertex shader (mf è funzione continua della distanza, uguale per ogni foglia dello stesso
/// livello → due vicine alla stessa distanza combaciano) + confini di LOD NETTI (mergeHysteresis=1).
///
/// Niente GameObject/Mesh per nodo: split/merge in movimento NON allocano (pool di Node + array figli → niente GC).
/// </summary>
public class PlanetLodTree
{
    /// <summary>Un nodo del quadtree: leggero (niente GameObject/Mesh). slab ≥ 0 solo sulle FOGLIE.</summary>
    public class Node
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

    readonly PlanetTerrain terrain;
    readonly float radius;
    readonly SlabPool pool;
    readonly ISlabFiller filler;

    // manopole LOD (copiate dal renderer al Setup; in pratica costanti per la sessione)
    readonly float lodFactor;
    readonly float mergeHysteresis;
    readonly int maxDepth;
    readonly int splitBudget;
    readonly float lookaheadTime;

    // pool degli oggetti Node: split/merge in movimento NON allocano più (niente GC → niente stallo periodico).
    readonly System.Collections.Generic.Stack<Node> nodePool = new System.Collections.Generic.Stack<Node>();
    readonly System.Collections.Generic.Stack<Node[]> childPool = new System.Collections.Generic.Stack<Node[]>();   // riusa gli array figli
    Node[] roots;
    public Node[] Roots => roots;

    // foglie visibili del frame (riempite in AddVisible, le carica il renderer su GraphicsBuffer)
    public uint[] VisibleSlabs { get; private set; }
    public float[] VisibleSplitDist { get; private set; }   // geomorph: splitDist (worldSize·lodFactor) per istanza
    public Vector4[] VisibleDirs { get; private set; }       // anti-spuntone: direzione-centro del nodo (.w = id regione)
    public int VisibleCount { get; private set; }
    public int SplitsThisFrame { get; private set; }

    // predittivo + contesto LOD del frame: settato UNA volta in Update, letto dalla traversata. Così UpdateLod NON si
    // passa più matrice+vettori PER COPIA a ogni nodo (in Mono/editor copiare ~112 byte migliaia di volte = il costo
    // misurato), e le costanti dell'orizzonte (direzione/angolo camera) si calcolano una volta, non per ogni nodo.
    Vector3 prevCamRel;       // posizione camera rispetto al CENTRO del pianeta nel frame scorso (per il LOD predittivo)
    bool hasPrevCamRel;
    Matrix4x4 lodM;
    Vector3 lodCamLook, lodCenter, lodCamDir;
    float lodThetaHorizon;
    float lodPeakAngle;       // height-aware: angolo con cui i PICCHI del terreno spuntano oltre l'orizzonte geometrico
    bool lodHorizonValid;     // false se la camera è troppo bassa (sotto il raggio) → niente culling all'orizzonte

    public PlanetLodTree(PlanetTerrain terrain, float radius, int maxSlabs, SlabPool pool, ISlabFiller filler,
                         float lodFactor, float mergeHysteresis, int maxDepth, int splitBudget, float lookaheadTime)
    {
        this.terrain = terrain;
        this.radius = radius;
        this.pool = pool;
        this.filler = filler;
        this.lodFactor = lodFactor;
        this.mergeHysteresis = mergeHysteresis;
        this.maxDepth = maxDepth;
        this.splitBudget = splitBudget;
        this.lookaheadTime = lookaheadTime;
        VisibleSlabs = new uint[maxSlabs];
        VisibleSplitDist = new float[maxSlabs];
        VisibleDirs = new Vector4[maxSlabs];
    }

    /// <summary>6 facce-radice, ciascuna con la sua fetta riempita (per-nodo: il batch non è ancora attivo).</summary>
    public void Build()
    {
        roots = new Node[6];
        for (int f = 0; f < 6; f++)
        {
            roots[f] = MakeNode(f, 0f, 0f, 1f, 0);
            AcquireSlab(roots[f]);   // cache vuota all'avvio → alloca e riempie (per-nodo)
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

    /// <summary>Centro (per la distanza di LOD) + dimensione mondo (per split), sulla SFERA BASE.
    /// NON chiama SampleHeight: era il walker CPU (pesante), 3 volte per nodo → 12 per split → era il PICCO misurato
    /// durante la costruzione del LOD. Lo spostamento d'altezza (decine di m) è trascurabile per la DISTANZA di LOD e
    /// l'orizzonte (centinaia di m). La collisione del walker usa il suo SampleHeight analitico, indipendente da questo.</summary>
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
    /// di 1/2^depth → indici esatti. bodyId nei bit alti: due corpi con la stessa regione non collidono in cache.</summary>
    long Key(Node nd)
    {
        int N = 1 << nd.depth;
        long ix = Mathf.RoundToInt(nd.u0 * N);
        long iy = Mathf.RoundToInt(nd.v0 * N);
        // iy ≤ 2^depth ≤ 64 < 2^7 → occupa i bit 32-38, quindi bodyId da bit 40 in su è libero.
        return (long)nd.face | ((long)nd.depth << 3) | (ix << 8) | (iy << 32) | ((long)pool.BodyId << 40);
    }

    // id REGIONE compatto e collision-free, ≤ 2^23 (= esatto in FLOAT, così il vertex shader lo confronta senza errore):
    // bodyId | face | depth | ix | iy. Lo scrive il fill nella fetta (slabRegion) e lo porta l'istanza (dir.w); se non
    // combaciano, la fetta tiene la geometria di una regione VECCHIA (churn) → il vertice si collassa. ≤7 corpi in 2^23.
    public float RegionId(Node nd)
    {
        int N = 1 << nd.depth;
        int ix = Mathf.RoundToInt(nd.u0 * N);
        int iy = Mathf.RoundToInt(nd.v0 * N);
        return pool.BodyId * 1048576 + ((nd.face * 8 + nd.depth) * 128 + ix) * 128 + iy;
    }

    /// <summary>Dà al nodo una fetta pronta: dalla CACHE se la regione c'è (già riempita, niente dispatch),
    /// altrimenti una libera e la riempie (via il filler = compute del renderer).</summary>
    void AcquireSlab(Node nd)
    {
        long key = Key(nd);
        if (pool.TryTakeCached(key, out int s)) { nd.slab = s; return; }
        nd.slab = pool.Alloc();
        filler.FillSlab(nd);
    }

    /// <summary>La fetta del nodo esce di vista: la METTE IN CACHE (riuso futuro) invece di buttarla.</summary>
    void ReleaseSlab(Node nd)
    {
        if (nd.slab < 0) return;
        pool.PutCached(Key(nd), nd.slab);
        nd.slab = -1;
    }

    bool Split(Node nd)
    {
        if (pool.FreeCount + pool.CacheCount < 4) return false;   // niente fette disponibili → resta foglia
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

    /// <summary>Per-frame: LOD predittivo + contesto del frame + traversata (split/merge/orizzonte) + raccolta delle
    /// foglie visibili. NON fa il flush dei fill (lo fa il renderer dopo, in un solo dispatch). Lascia pronti
    /// VisibleSlabs/VisibleSplitDist/VisibleDirs/VisibleCount.</summary>
    public void Update(Matrix4x4 m, Vector3 camWorld, float dt)
    {
        Vector3 planetCenter = m.MultiplyPoint3x4(Vector3.zero);

        // LOD PREDITTIVO: punto "dove sarai fra lookaheadTime". La velocità si misura sulla posizione RELATIVA al
        // centro del pianeta (camRel): stabile con la floating origin (origine e centro shiftano insieme → camRel no)
        // e col pianeta che orbita (= moto del giocatore rispetto al pianeta). Guardia sui salti (cambio ancora/teleport).
        Vector3 camRel = camWorld - planetCenter;
        Vector3 camLook = camWorld;
        if (hasPrevCamRel && dt > 1e-4f)
        {
            Vector3 disp = (camRel - prevCamRel) * (lookaheadTime / dt);
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
            // crateri/tettonica), limitato a [4%,12%] del raggio per non sovra-disegnare troppo. La GPU ha margine.
            float maxH = Mathf.Clamp(terrain.Amplitude * 3f, radius * 0.04f, radius * 0.12f);
            lodPeakAngle = Mathf.Acos(Mathf.Clamp(radius / (radius + maxH), 0f, 1f));
        }

        VisibleCount = 0;
        SplitsThisFrame = 0;
        for (int f = 0; f < 6; f++) UpdateLod(roots[f]);   // CDLOD: una passata, seleziona+raccoglie (il morph continuo fa il crack-free)
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
            if (dist > splitDist * mergeHysteresis) { Merge(nd); AddVisible(nd); }
            else for (int i = 0; i < 4; i++) UpdateLod(nd.children[i]);
        }
        else if (nd.depth < maxDepth && dist < splitDist && SplitsThisFrame < splitBudget && Split(nd))
        {
            SplitsThisFrame++;
            for (int i = 0; i < 4; i++) UpdateLod(nd.children[i]);
        }
        else AddVisible(nd);
    }

    void AddVisible(Node nd)
    {
        if (nd.slab >= 0 && VisibleCount < VisibleSlabs.Length)
        {
            VisibleSplitDist[VisibleCount] = nd.worldSize * lodFactor;   // morph: distanza di split del nodo (per istanza)
            Vector3 cd = nd.centerLocal.normalized;                      // anti-spuntone: direzione-centro del nodo (spazio oggetto)
            VisibleDirs[VisibleCount] = new Vector4(cd.x, cd.y, cd.z, RegionId(nd));   // .w = id regione attesa (region-stamp)
            VisibleSlabs[VisibleCount] = (uint)nd.slab;
            VisibleCount++;
        }
    }

    /// <summary>Il nodo è oltre l'orizzonte (occluso dalla curvatura)? Test in ANGOLI: l'angolo del nodo dalla
    /// verticale-camera supera l'angolo dell'orizzonte + mezza estensione + margine. **ISTERESI** (stato per-nodo):
    /// serve di più per NASCONDERE che per RI-MOSTRARE → niente flip cancella/ricrea ai micro-cambi di quota. Le creste
    /// che bucano l'orizzonte sono gestite con precisione da **lodPeakAngle** (acos(R/(R+maxH)) sommato alla soglia).</summary>
    bool BeyondHorizon(Node nd, Vector3 nodeWorld)
    {
        if (!lodHorizonValid) { nd.horizonHidden = false; return false; }   // camera troppo bassa: niente culling
        Vector3 nodeDir = (nodeWorld - lodCenter).normalized;
        float thetaNode = Mathf.Acos(Mathf.Clamp(Vector3.Dot(lodCamDir, nodeDir), -1f, 1f));
        float thetaR = (nd.worldSize * 0.5f) / radius;   // mezza estensione angolare del nodo
        float baseT = lodThetaHorizon + thetaR + lodPeakAngle;   // orizzonte + estensione nodo + spunto dei picchi (height-aware)
        float margin = nd.horizonHidden ? 0.02f : 0.05f;
        nd.horizonHidden = thetaNode > baseT + margin;
        return nd.horizonHidden;
    }

    /// <summary>STREAMING-SAFE: rende le fette di QUESTO corpo (vive nell'albero + in cache) alla free-list condivisa,
    /// così distruggere un SINGOLO corpo a metà sessione non lascia fette "prese" nel pool condiviso.</summary>
    public void ReturnSlabs()
    {
        if (roots != null) for (int f = 0; f < 6; f++) FreeSubtreeSlabs(roots[f]);
        // voci di cache di QUESTO corpo: il bodyId sta nei bit alti della chiave (>> 40)
        pool.PurgeCache(k => (k >> 40) == pool.BodyId);
    }

    void FreeSubtreeSlabs(Node nd)
    {
        if (nd == null) return;
        if (nd.children != null) for (int i = 0; i < 4; i++) FreeSubtreeSlabs(nd.children[i]);
        if (nd.slab >= 0) { pool.FreeRaw(nd.slab); nd.slab = -1; }
    }
}

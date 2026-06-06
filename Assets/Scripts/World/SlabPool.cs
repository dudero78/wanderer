using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// POOL VRAM CONDIVISO delle "fette" (slab) di geometria, estratto da GpuPlanetRenderer (#18 — spaccare il god-object).
///
/// Una FETTA è una griglia n×n di vertici (la patch di un nodo-foglia del quadtree). I 6 buffer geometria
/// (pos/nrm/bedNrm/depth/field/surf) + il marchio di regione sono **STATICI e refcountati**: UNO per TUTTI i corpi
/// in scena. Un corpo solo è "attivo" per volta (gli altri stanno alle radici), quindi serve 1×~843 MB e non 6×≈5 GB
/// (#2 sez. A, NOTES_pool_vram.md). Ogni `SlabPool` è un HANDLE per-corpo che fa alias dei buffer condivisi e tiene
/// un `BodyId` univoco (entra nella chiave di cache → due corpi con la stessa regione non collidono).
///
/// Slot bookkeeping: free-list (fette mai usate) + CACHE LRU per regione (una regione che esce di vista NON si butta:
/// la sua fetta resta in cache e si riusa al ritorno, niente ricalcolo) + sfratto O(1) dal fronte LRU quando il pool è
/// pieno. La GEOMETRIA dentro la fetta la scrive chi possiede il compute (GpuPlanetRenderer, via ISlabFiller): il pool
/// gestisce SOLO l'allocazione degli slot, non il loro contenuto.
/// </summary>
public class SlabPool
{
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

    /// <summary>Il pool condiviso è allocato? (guardia per Update dopo un domain reload in Play.)</summary>
    public static bool IsAllocated => sRefCount > 0;

    // ---- POOL geometria: ALIAS dei buffer CONDIVISI statici (sPos…), assegnati nel costruttore. Un solo pool serve
    // tutti i corpi (#2 sez. A): un corpo solo è "attivo" per volta, gli altri stanno alle radici → 1×~843 MB, non 6×.
    public GraphicsBuffer Pos { get; private set; }
    public GraphicsBuffer Nrm { get; private set; }
    public GraphicsBuffer BedNrm { get; private set; }
    public GraphicsBuffer Depth { get; private set; }
    public GraphicsBuffer Field { get; private set; }
    public GraphicsBuffer Surf { get; private set; }
    public GraphicsBuffer Region { get; private set; }   // alias del marchio di regione CONDIVISO (un marchio per fetta fisica)
    public int BodyId { get; private set; }              // identità del corpo nel pool condiviso: entra nella chiave di cache

    readonly int maxSlabs;
    readonly int vertsPerSlab;

    // free-list + CACHE LRU delle fette: CONDIVISE fra i corpi (alias degli statici, assegnati nel costruttore).
    // Una regione (face,depth,u0,v0) che esce di vista NON si butta: la sua fetta resta in cache e si riusa al
    // ritorno → niente ricalcolo. Con pool condiviso un corpo attivo può "prendere in prestito" le fette in cache
    // dei corpi lontani (eviction LRU): per loro è solo un refill al ritorno.
    // LRU O(1): la cache si sfratta dal FRONTE (regione meno recente) in tempo costante, invece di scansionare
    // tutto il dizionario (era O(n), proprio nel churn a cambio-quota). lru = ordine d'inserimento (release),
    // lruNode = handle per la rimozione O(1) quando una regione torna viva.
    Stack<int> freeSlabs;
    Dictionary<long, int> cacheSlab;
    LinkedList<long> lru;
    Dictionary<long, LinkedListNode<long>> lruNode;

    public int FreeCount => freeSlabs.Count;
    public int CacheCount => cacheSlab.Count;
    public int VertsPerSlab => vertsPerSlab;

    /// <summary>Alloca il pool CONDIVISO la prima volta (refcount), o lo riusa, e ne fa alias i campi per-corpo
    /// (Pos…Surf, freeSlabs/cacheSlab/lru). Assegna un BodyId univoco per la chiave di cache.</summary>
    public SlabPool(int maxSlabs, int vertsPerSlab)
    {
        this.maxSlabs = maxSlabs;
        this.vertsPerSlab = vertsPerSlab;

        if (sRefCount > 0 && vertsPerSlab != sPoolVerts)
            Debug.LogError($"SlabPool: pool condiviso con vertsPerSlab diverso ({vertsPerSlab} vs {sPoolVerts}) — tutti i corpi devono avere lo stesso nodeRes, le fette non sarebbero allineate.");
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
            Debug.Log($"[GpuPlanet] pool VRAM CONDIVISO ~{poolBytes / (1024 * 1024)} MB (maxSlabs={maxSlabs}, vertsPerSlab={vertsPerSlab}) — UNO per tutti i corpi");
        }
        sRefCount++;
        Pos = sPos; Nrm = sNrm; BedNrm = sBedNrm; Depth = sDepth; Field = sField; Surf = sSurf;
        Region = sSlabRegion;
        freeSlabs = sFreeSlabs; cacheSlab = sCacheSlab; lru = sLru; lruNode = sLruNode;
        BodyId = sNextBodyId++;
    }

    /// <summary>Slot libero: dalla free-list, o sfrattando in O(1) la regione meno recente (fronte LRU). -1 se vuoto.</summary>
    public int Alloc()
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

    /// <summary>La regione è in cache (già riempita)? Se sì la rimuove dalla cache e ritorna la sua fetta (niente dispatch).</summary>
    public bool TryTakeCached(long key, out int slab)
    {
        if (cacheSlab.TryGetValue(key, out slab)) { cacheSlab.Remove(key); lru.Remove(lruNode[key]); lruNode.Remove(key); return true; }
        slab = -1;
        return false;
    }

    /// <summary>Mette la fetta in CACHE per la sua regione (riuso futuro) invece di buttarla. In coda = più recente.</summary>
    public void PutCached(long key, int slab)
    {
        if (cacheSlab.ContainsKey(key)) freeSlabs.Push(slab);   // duplicato improbabile: libera
        else { cacheSlab[key] = slab; lruNode[key] = lru.AddLast(key); }
    }

    /// <summary>Rende una fetta DIRETTAMENTE alla free-list (streaming-safe: la fetta viva di un corpo che muore).</summary>
    public void FreeRaw(int slab) => freeSlabs.Push(slab);

    /// <summary>Sfratta dalla cache tutte le regioni che soddisfano `belongs` (es. quelle di QUESTO corpo) → free-list.</summary>
    public void PurgeCache(Func<long, bool> belongs)
    {
        if (cacheSlab.Count == 0) return;
        var mine = new List<long>();
        foreach (var kv in cacheSlab) if (belongs(kv.Key)) mine.Add(kv.Key);
        foreach (var k in mine)
        {
            freeSlabs.Push(cacheSlab[k]); cacheSlab.Remove(k);
            if (lruNode.TryGetValue(k, out var node)) { lru.Remove(node); lruNode.Remove(k); }
        }
    }

    /// <summary>Rilascia il pool condiviso quando l'ULTIMO corpo sparisce (refcount → 0). I corpi del gioco
    /// nascono e muoiono tutti insieme (lifecycle di scena) → al refcount 0 il pool si libera per intero.</summary>
    public void Release()
    {
        if (sRefCount == 0) return;
        sRefCount--;
        Pos = Nrm = BedNrm = Depth = Field = Surf = Region = null;
        freeSlabs = null; cacheSlab = null; lru = null; lruNode = null;
        if (sRefCount == 0)
        {
            sPos?.Release(); sNrm?.Release(); sBedNrm?.Release(); sDepth?.Release(); sField?.Release(); sSurf?.Release(); sSlabRegion?.Release();
            sPos = sNrm = sBedNrm = sDepth = sField = sSurf = sSlabRegion = null;
            sFreeSlabs.Clear(); sCacheSlab.Clear(); sLru.Clear(); sLruNode.Clear(); sNextBodyId = 0;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Calcola sulla GPU la griglia di altezze di un nodo del quadtree (la stessa funzione del walker,
/// PlanetTerrain.SampleHeight, ma sul compute shader PlanetHeight.compute). È ciò che toglie il
/// rumore per-vertice dai thread CPU: la GPU — scarica al 95% — calcola la forma in parallelo, la
/// CPU fa solo l'assemblaggio leggero (normali, morph, skirt) della mesh.
///
/// I parametri della forma (base + tutte le pipeline di crateri della ricetta) sono ricavati UNA
/// volta dal PlanetTerrain — esattamente come PlanetTerrain.RebuildLayers — e restano fissi: ogni
/// nodo varia solo faccia/u0/v0/step. La lettura del risultato è ASINCRONA (AsyncGPUReadback): non
/// blocca mai il frame, si integra col modello di build già esistente del quadtree.
///
/// Robustezza: se la piattaforma non supporta i compute shader (o il file manca), Supported è false
/// e il quadtree resta sul percorso CPU. Niente regressioni.
/// </summary>
public class GpuHeightBaker : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct CraterGPU
    {
        public float seed, octaves, largestRadius, density, depthRatio, rimRatio, rimSharpness, hasDominant;
        public Vector3 dominantDir;
        public float dominantRadius;
        public float wLarge, wMedium, wSmall, distribution;   // pesi per fascia di taglia + fase di distribuzione
    }
    const int CraterStride = 16 * 4;   // 8 float + Vector3(3) + float + 4 float = 16 float = 64 byte

    readonly ComputeShader cs;
    readonly int kNode, kParity;
    ComputeBuffer craterBuf;
    readonly int ne;                   // lato della griglia estesa di un nodo (nodeRes+3): costante
    readonly Stack<ComputeBuffer> pool = new Stack<ComputeBuffer>();
    int outstanding;                   // readback in volo (per non far esplodere il pool)

    public bool Supported { get; private set; }

    /// <summary>Costruisce il baker per un terreno dato. nodeRes = la stessa manopola del quadtree
    /// (la griglia estesa è nodeRes+3). Carica il compute da Resources/Shaders/PlanetHeight.</summary>
    public GpuHeightBaker(PlanetTerrain terrain, int nodeRes)
    {
        ne = nodeRes + 3;
        if (!SystemInfo.supportsComputeShaders) return;
        cs = Resources.Load<ComputeShader>("Shaders/PlanetHeight");
        if (cs == null) { Debug.LogWarning("GpuHeightBaker: PlanetHeight.compute non trovato → quadtree su CPU."); return; }

        kNode = cs.FindKernel("CSNodeGrid");
        kParity = cs.FindKernel("CSParity");
        craterBuf = BindShapeParams(cs, terrain, new[] { kNode, kParity });
        Supported = true;
    }

    /// <summary>Imposta sul compute gli uniform della forma (base + crateri) ricavati dal terreno ESATTAMENTE
    /// come PlanetTerrain.RebuildLayers, e crea+lega il buffer dei crateri ai kernel dati. È l'UNICA fonte di
    /// questi parametri lato GPU: la usano sia il baker (quadtree) sia l'anteprima GPU dell'editor → non possono
    /// divergere. Restituisce il ComputeBuffer dei crateri (il chiamante lo rilascia).</summary>
    public static ComputeBuffer BindShapeParams(ComputeShader cs, PlanetTerrain terrain, int[] kernels)
    {
        var rec = terrain.Recipe;
        var craters = new List<CraterGPU>();

        if (rec != null)
        {
            cs.SetFloat("_BaseRadius", rec.baseRadius);
            cs.SetFloat("_Amplitude", rec.amplitude);
            cs.SetFloat("_Frequency", rec.frequency);
            cs.SetInt("_Octaves", rec.octaves);
            cs.SetFloat("_Lacunarity", rec.lacunarity);
            cs.SetFloat("_Gain", rec.gain);
            cs.SetInt("_Seed", rec.seed);

            // Crateri: parità COMPLETA con CraterTerrainLayer (pesi per taglia + distribuzione inclusi).
            // Resta fuori solo il MARE e la TETTONICA (Tappa 2): il compute calcola base + crateri.
            rec.Normalize();
            foreach (var p in rec.processes)
            {
                if (p == null || !p.enabled || p.type != ProcessType.Crateri) continue;
                craters.Add(new CraterGPU
                {
                    seed = p.seed, octaves = p.octaves, largestRadius = p.largestRadius,
                    density = p.density, depthRatio = p.depthRatio, rimRatio = p.rimRatio,
                    rimSharpness = p.rimSharpness,
                    hasDominant = p.dominant ? 1f : 0f,
                    dominantDir = p.dominantDir.normalized, dominantRadius = p.dominantRadius,
                    wLarge = p.wLarge, wMedium = p.wMedium, wSmall = p.wSmall, distribution = p.distribution
                });
            }
        }
        else
        {
            cs.SetFloat("_BaseRadius", terrain.BaseRadius);
            cs.SetFloat("_Amplitude", terrain.Amplitude);
            cs.SetFloat("_Frequency", terrain.Frequency);
            cs.SetInt("_Octaves", terrain.Octaves);
            cs.SetFloat("_Lacunarity", terrain.Lacunarity);
            cs.SetFloat("_Gain", terrain.Gain);
            cs.SetInt("_Seed", terrain.Seed);

            if (terrain.CratersEnabled)
                craters.Add(new CraterGPU
                {
                    seed = terrain.CraterSeed, octaves = terrain.CraterOctaves, largestRadius = terrain.CraterLargestRadius,
                    density = terrain.CraterDensity, depthRatio = terrain.CraterDepthRatio, rimRatio = terrain.CraterRimRatio,
                    rimSharpness = terrain.CraterRimSharpness,
                    hasDominant = terrain.DominantCrater ? 1f : 0f,
                    dominantDir = terrain.DominantCraterDir.normalized, dominantRadius = terrain.DominantCraterRadius,
                    wLarge = 1f, wMedium = 1f, wSmall = 1f, distribution = 0f   // legacy: densità uniforme, niente rotazione
                });
        }

        // StructuredBuffer dev'essere SEMPRE legato (anche a 0 crateri): tieni almeno 1 elemento fittizio.
        int count = craters.Count;
        var buf = new ComputeBuffer(Mathf.Max(1, count), CraterStride);
        if (count > 0) buf.SetData(craters);
        cs.SetInt("_CraterCount", count);
        foreach (int k in kernels) cs.SetBuffer(k, "_Craters", buf);
        return buf;
    }

    // ---- griglia di un nodo (uso del quadtree) -------------------------------------------------

    /// <summary>Dispatcha il calcolo della griglia estesa di un nodo e chiama onDone (sul MAIN thread)
    /// con le posizioni locali (dir*altezza), oppure null in caso di errore di readback. Va invocato
    /// dal main thread (API grafiche).</summary>
    public void RequestNodeGrid(Vector3 faceUp, Vector3 axisA, Vector3 axisB,
                                float u0, float v0, float step, Action<Vector3[]> onDone)
    {
        var buf = Acquire();
        cs.SetVector("_FaceUp", faceUp);
        cs.SetVector("_AxisA", axisA);
        cs.SetVector("_AxisB", axisB);
        cs.SetFloat("_U0", u0);
        cs.SetFloat("_V0", v0);
        cs.SetFloat("_Step", step);
        cs.SetInt("_NE", ne);
        cs.SetBuffer(kNode, "_Pout", buf);

        int groups = (ne + 7) / 8;
        cs.Dispatch(kNode, groups, groups, 1);

        outstanding++;
        AsyncGPUReadback.Request(buf, req =>
        {
            outstanding--;
            if (req.hasError) { Release(buf); onDone(null); return; }
            var P = Unpack(req.GetData<float>());
            Release(buf);
            onDone(P);
        });
    }

    /// <summary>float piatti (x,y,z) → Vector3[ne*ne].</summary>
    Vector3[] Unpack(Unity.Collections.NativeArray<float> flat)
    {
        var P = new Vector3[ne * ne];
        for (int i = 0; i < P.Length; i++)
            P[i] = new Vector3(flat[i * 3], flat[i * 3 + 1], flat[i * 3 + 2]);
        return P;
    }

    /// <summary>Calcola la griglia di UN nodo in modo BLOCCANTE (solo per il test di parità del nodo).</summary>
    public Vector3[] NodeGridBlocking(Vector3 faceUp, Vector3 axisA, Vector3 axisB, float u0, float v0, float step)
    {
        var buf = Acquire();
        cs.SetVector("_FaceUp", faceUp);
        cs.SetVector("_AxisA", axisA);
        cs.SetVector("_AxisB", axisB);
        cs.SetFloat("_U0", u0);
        cs.SetFloat("_V0", v0);
        cs.SetFloat("_Step", step);
        cs.SetInt("_NE", ne);
        cs.SetBuffer(kNode, "_Pout", buf);
        int groups = (ne + 7) / 8;
        cs.Dispatch(kNode, groups, groups, 1);
        var req = AsyncGPUReadback.Request(buf);
        req.WaitForCompletion();
        var P = req.hasError ? new Vector3[ne * ne] : Unpack(req.GetData<float>());
        Release(buf);
        return P;
    }

    public int Ne => ne;

    public int Outstanding => outstanding;

    ComputeBuffer Acquire()
    {
        if (pool.Count > 0) return pool.Pop();
        return new ComputeBuffer(ne * ne * 3, 4);   // float piatti: 3 per punto, niente padding float3
    }

    void Release(ComputeBuffer b)
    {
        if (b == null) return;
        // il pool deve coprire le build concorrenti in volo (vedi maxConcurrentBuilds GPU nel quadtree):
        // con molte letture asincrone insieme servono altrettanti buffer vivi, non se ne ricicla 16.
        if (pool.Count < 64) pool.Push(b);
        else b.Release();
    }

    // ---- parità (test offline) -----------------------------------------------------------------

    /// <summary>Calcola le altezze GPU per le direzioni date (BLOCCANTE: solo per il test di parità).</summary>
    public float[] SampleHeightsBlocking(Vector3[] dirs)
    {
        int n = dirs.Length;
        var dirBuf = new ComputeBuffer(n, 3 * 4);
        var hBuf = new ComputeBuffer(n, 4);
        dirBuf.SetData(dirs);
        cs.SetBuffer(kParity, "_Dirs", dirBuf);
        cs.SetBuffer(kParity, "_Heights", hBuf);
        cs.SetInt("_DirCount", n);
        cs.Dispatch(kParity, (n + 63) / 64, 1, 1);

        var req = AsyncGPUReadback.Request(hBuf);
        req.WaitForCompletion();
        var outv = new float[n];
        if (!req.hasError) req.GetData<float>().CopyTo(outv);
        dirBuf.Release();
        hBuf.Release();
        return outv;
    }

    public void Dispose()
    {
        craterBuf?.Release(); craterBuf = null;
        while (pool.Count > 0) pool.Pop().Release();
    }
}

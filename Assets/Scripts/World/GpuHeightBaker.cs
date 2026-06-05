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
    readonly ComputeShader cs;
    readonly int kNode, kParity;
    GpuShapeBuffers shape;             // base + pipeline ordinata (crateri/mare/tettonica)
    readonly int ne;                   // lato della griglia estesa di un nodo (nodeRes+3): costante
    readonly Stack<ComputeBuffer> pool = new Stack<ComputeBuffer>();
    int outstanding;                   // readback in volo (per non far esplodere il pool)
    bool disposed;                     // se Dispose è già passato, un buffer che rientra (readback in volo) va RILASCIATO, non rimesso nel pool morto

    public bool Supported { get; private set; }

    /// <summary>Costruisce il baker per un terreno dato. nodeRes = la stessa manopola del quadtree
    /// (la griglia estesa è nodeRes+3). Carica il compute da Resources/Shaders/PlanetHeight.</summary>
    public GpuHeightBaker(PlanetTerrain terrain, int nodeRes)
    {
        ne = nodeRes + 3;
        if (!SystemInfo.supportsComputeShaders) return;
        cs = Resources.Load<ComputeShader>("Shaders/PlanetHeightEditor");   // kernel CSNodeGrid/CSParity; core condiviso
        if (cs == null) { Debug.LogWarning("GpuHeightBaker: PlanetHeight.compute non trovato → quadtree su CPU."); return; }

        kNode = cs.FindKernel("CSNodeGrid");
        kParity = cs.FindKernel("CSParity");
        shape = GpuShapeBuffers.Build(cs, terrain, new[] { kNode, kParity });
        Supported = true;
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
        if (disposed) { b.Release(); return; }   // baker già disposto: una lettura in volo che rientra va liberata, non poolata (no leak)
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
        disposed = true;   // i buffer ancora in volo li rilascerà il loro callback (Release vede 'disposed')
        shape?.Dispose(); shape = null;
        while (pool.Count > 0) pool.Pop().Release();
    }
}

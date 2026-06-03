using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Costruisce e tiene i buffer GPU che descrivono la PIPELINE ORDINATA di un terreno per PlanetHeight.compute:
/// forma base (uniform) + una lista ORDINATA di processi (crateri / mare / tettonica) che rimandano ai buffer
/// per-tipo. L'ordine conta (un cratere dopo un mare scava all'asciutto), quindi il compute applica i processi
/// nell'ordine esatto della ricetta — come PlanetTerrain.SampleHeight sulla CPU.
///
/// È l'UNICA fonte dei parametri GPU: la usano sia il baker (quadtree) sia l'anteprima GPU dell'editor → CPU e
/// GPU non possono divergere. Le placche della tettonica sono generate UNA volta in C# (TectonicTerrainLayer) e
/// caricate così come sono: niente RNG da replicare in HLSL, parità garantita.
/// </summary>
public class GpuShapeBuffers : System.IDisposable
{
    // type code del processo (DEVE combaciare con PlanetHeight.compute). Coincide con ProcessType:
    // Crateri=0, Mare=1, Tettonica=2.
    [StructLayout(LayoutKind.Sequential)] struct ProcessGPU { public float type, index; }

    [StructLayout(LayoutKind.Sequential)]
    struct CraterGPU
    {
        public float seed, octaves, largestRadius, density, depthRatio, rimRatio, rimSharpness, hasDominant;
        public Vector3 dominantDir;
        public float dominantRadius;
        public float wLarge, wMedium, wSmall, distribution;
        public float domDepthRatio, domRimRatio, domRimSharp, domIrregular, domIrregScale;   // profilo+irregolarità PROPRI del dominante
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SeaGPU { public float seaRadius, roughness, roughScale, forma, seed; }

    [StructLayout(LayoutKind.Sequential)]
    struct TectonicGPU { public float seed, contrast, uplift, boundaryWidth, warp, coastSlope, plateOffset, plateCount, continentalRelief, riftBalance; }

    // float4 (non float3) per evitare il disallineamento dei float3 negli StructuredBuffer su Metal.
    // seedDir.w = continentale (0/1); motion.w = elevJitter.
    [StructLayout(LayoutKind.Sequential)]
    struct PlateGPU { public Vector4 seedDir, motion; }

    const int ProcStride = 2 * 4, CraterStride = 21 * 4, SeaStride = 5 * 4, TectStride = 10 * 4, PlateStride = 8 * 4;

    ComputeBuffer procBuf, craterBuf, seaBuf, tectBuf, plateBuf;

    /// <summary>Ricava base + pipeline dal terreno (ricetta se c'è, altrimenti campi legacy), crea i buffer e li
    /// lega ai kernel dati. Restituisce il contenitore (il chiamante lo Dispose).</summary>
    public static GpuShapeBuffers Build(ComputeShader cs, PlanetTerrain terrain, int[] kernels)
    {
        var procs = new List<ProcessGPU>();
        var craters = new List<CraterGPU>();
        var seas = new List<SeaGPU>();
        var tects = new List<TectonicGPU>();
        var plates = new List<PlateGPU>();

        var rec = terrain.Recipe;
        if (rec != null)
        {
            rec.Normalize();
            SetBase(cs, rec.baseRadius, rec.amplitude, rec.frequency, rec.octaves, rec.lacunarity, rec.gain, rec.seed);
            float baseRadius = rec.baseRadius;

            foreach (var p in rec.processes)
            {
                if (p == null || !p.enabled) continue;
                if (p.type == ProcessType.Crateri)
                {
                    procs.Add(new ProcessGPU { type = 0, index = craters.Count });
                    craters.Add(MakeCrater(p));
                }
                else if (p.type == ProcessType.Mare)
                {
                    procs.Add(new ProcessGPU { type = 1, index = seas.Count });
                    seas.Add(new SeaGPU
                    {
                        seaRadius = baseRadius + p.seaLevel,
                        roughness = Mathf.Max(0f, p.seaRoughness),
                        roughScale = Mathf.Max(0.1f, p.seaRoughScale),
                        forma = Mathf.Clamp(p.seaForma, -1f, 1f),
                        seed = p.seed
                    });
                }
                else // Tettonica
                {
                    procs.Add(new ProcessGPU { type = 2, index = tects.Count });
                    // genera le placche con lo STESSO codice della CPU, poi le carica
                    var tl = new TectonicTerrainLayer(baseRadius, p.seed, p.plateCount, p.continentalFraction,
                        p.elevationContrast, p.boundaryUplift, p.boundaryWidth, p.tectonicWarp, p.coastSlope, p.continentalRelief, p.riftBalance);
                    int off = plates.Count;
                    for (int i = 0; i < tl.PlateCount; i++)
                    {
                        Vector3 sd = tl.PlateSeedDir(i), mo = tl.PlateMotion(i);
                        plates.Add(new PlateGPU
                        {
                            seedDir = new Vector4(sd.x, sd.y, sd.z, tl.PlateContinental(i) ? 1f : 0f),
                            motion = new Vector4(mo.x, mo.y, mo.z, tl.PlateElevJitter(i))
                        });
                    }
                    tects.Add(new TectonicGPU
                    {
                        seed = tl.Seed, contrast = tl.Contrast, uplift = tl.Uplift, boundaryWidth = tl.BoundaryWidth,
                        warp = tl.Warp, coastSlope = tl.CoastSlope, plateOffset = off, plateCount = tl.PlateCount,
                        continentalRelief = tl.ContinentalRelief, riftBalance = tl.RiftBalance
                    });
                }
            }
        }
        else
        {
            SetBase(cs, terrain.BaseRadius, terrain.Amplitude, terrain.Frequency, terrain.Octaves,
                    terrain.Lacunarity, terrain.Gain, terrain.Seed);
            if (terrain.CratersEnabled)
            {
                procs.Add(new ProcessGPU { type = 0, index = 0 });
                craters.Add(new CraterGPU
                {
                    seed = terrain.CraterSeed, octaves = terrain.CraterOctaves, largestRadius = terrain.CraterLargestRadius,
                    density = Mathf.Clamp01(terrain.CraterDensity), depthRatio = terrain.CraterDepthRatio,
                    rimRatio = terrain.CraterRimRatio, rimSharpness = terrain.CraterRimSharpness,
                    hasDominant = terrain.DominantCrater ? 1f : 0f,
                    dominantDir = terrain.DominantCraterDir.normalized, dominantRadius = terrain.DominantCraterRadius,
                    wLarge = 1f, wMedium = 1f, wSmall = 1f, distribution = 0f,
                    domDepthRatio = terrain.CraterDepthRatio, domRimRatio = terrain.CraterRimRatio,
                    domRimSharp = terrain.CraterRimSharpness, domIrregular = 0f, domIrregScale = 6f   // legacy: profilo del campo, niente irregolarità
                });
            }
        }

        var b = new GpuShapeBuffers();
        b.procBuf = Make(procs, ProcStride);
        b.craterBuf = Make(craters, CraterStride);
        b.seaBuf = Make(seas, SeaStride);
        b.tectBuf = Make(tects, TectStride);
        b.plateBuf = Make(plates, PlateStride);

        cs.SetInt("_ProcessCount", procs.Count);
        foreach (int k in kernels)
        {
            cs.SetBuffer(k, "_Process", b.procBuf);
            cs.SetBuffer(k, "_Craters", b.craterBuf);
            cs.SetBuffer(k, "_Seas", b.seaBuf);
            cs.SetBuffer(k, "_Tectonics", b.tectBuf);
            cs.SetBuffer(k, "_Plates", b.plateBuf);
        }
        return b;
    }

    static CraterGPU MakeCrater(ProcessStep p) => new CraterGPU
    {
        seed = p.seed, octaves = p.octaves, largestRadius = p.largestRadius,
        density = Mathf.Clamp01(p.density), depthRatio = p.depthRatio, rimRatio = p.rimRatio,
        rimSharpness = p.rimSharpness,
        hasDominant = p.dominant ? 1f : 0f,
        dominantDir = p.dominantDir.normalized, dominantRadius = p.dominantRadius,
        wLarge = Mathf.Clamp01(p.wLarge), wMedium = Mathf.Clamp01(p.wMedium),
        wSmall = Mathf.Clamp01(p.wSmall), distribution = p.distribution,
        domDepthRatio = p.domDepthRatio, domRimRatio = p.domRimRatio,
        domRimSharp = Mathf.Max(1f, p.domRimSharp), domIrregular = Mathf.Max(0f, p.domIrregular),
        domIrregScale = Mathf.Max(0.5f, p.domIrregScale)
    };

    static void SetBase(ComputeShader cs, float baseRadius, float amplitude, float frequency, int octaves,
                        float lacunarity, float gain, int seed)
    {
        cs.SetFloat("_BaseRadius", baseRadius);
        cs.SetFloat("_Amplitude", amplitude);
        cs.SetFloat("_Frequency", frequency);
        cs.SetInt("_Octaves", octaves);
        cs.SetFloat("_Lacunarity", lacunarity);
        cs.SetFloat("_Gain", gain);
        cs.SetInt("_Seed", seed);
    }

    // StructuredBuffer dev'essere SEMPRE legato (anche vuoto): tieni almeno 1 elemento fittizio.
    static ComputeBuffer Make<T>(List<T> data, int stride) where T : struct
    {
        var buf = new ComputeBuffer(Mathf.Max(1, data.Count), stride);
        if (data.Count > 0) buf.SetData(data);
        return buf;
    }

    public void Dispose()
    {
        procBuf?.Release(); craterBuf?.Release(); seaBuf?.Release(); tectBuf?.Release(); plateBuf?.Release();
        procBuf = craterBuf = seaBuf = tectBuf = plateBuf = null;
    }
}

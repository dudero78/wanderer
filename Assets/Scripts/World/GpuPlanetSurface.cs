using UnityEngine;

/// <summary>
/// Anteprima GPU del pianeta dell'editor — TAPPA 1 del percorso "GPU per l'editor".
///
/// Genera la GEOMETRIA della superficie interamente sulla GPU (PlanetHeight.compute, gli stessi
/// SampleHeight/ParamToDir provati al millimetro dal walker) e la DISEGNA direttamente dai buffer
/// con Graphics.RenderPrimitivesIndexed — SENZA mai leggere indietro su CPU (era il blocco che
/// "trascinava"). Dimostra lo schema no-readback: il compute riempie i buffer di posizioni+normali,
/// lo shader Wanderer/PlanetProcedural li indicizza con SV_VertexID, niente mesh CPU di mezzo.
///
/// Convive con l'anteprima CPU (SingleMeshPlanet): l'editor commuta fra le due (tasto G) per il
/// confronto A/B. La parità della FORMA con la CPU è garantita da GpuShapeBuffers
/// (unica fonte dei parametri GPU). La colorazione ricca (PlanetBaked) è una tappa successiva: qui
/// è un Lambert grigio, basta a provare il render-dai-buffer.
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
    int kFace, kNorm;
    GraphicsBuffer posBuf, nrmBuf, idxBuf;   // posizioni/normali (3 float per vertice) + index buffer
    GpuShapeBuffers shape;                    // base + pipeline ordinata (crateri/mare/tettonica)
    Material mat;
    Bounds bounds;

    public bool Ready { get; private set; }
    /// <summary>Quando true, Update disegna la superficie GPU. L'editor lo accende/spegne col toggle.</summary>
    public bool Active { get; set; }

    /// <summary>Alloca i buffer, costruisce l'index buffer (una volta) e fa il primo dispatch. res = vertici
    /// interni per lato di ogni faccia (la GPU ha margine: si può tenere alta).</summary>
    public void Setup(PlanetTerrain terrain, int res)
    {
        this.res = Mathf.Max(2, res);
        gp = this.res + 2;
        vertsPerFace = gp * gp;

        if (!SystemInfo.supportsComputeShaders) return;
        cs = Resources.Load<ComputeShader>("Shaders/PlanetHeight");
        var sh = Shader.Find("Wanderer/PlanetProcedural");
        if (cs == null || sh == null)
        {
            Debug.LogWarning("GpuPlanetSurface: compute o shader mancante → anteprima GPU non disponibile.");
            return;
        }
        kFace = cs.FindKernel("CSFaceGrid");
        kNorm = cs.FindKernel("CSFaceNormals");

        int totalVerts = vertsPerFace * 6;
        posBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts * 3, 4);
        nrmBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVerts * 3, 4);
        cs.SetBuffer(kFace, "_VPos", posBuf);
        cs.SetBuffer(kNorm, "_VPos", posBuf);
        cs.SetBuffer(kNorm, "_VNrm", nrmBuf);

        BuildIndexBuffer();

        mat = new Material(sh);
        mat.SetBuffer("_VPos", posBuf);
        mat.SetBuffer("_VNrm", nrmBuf);

        Rebuild(terrain);
        Ready = true;
    }

    /// <summary>Ricostruisce posizioni+normali sulla GPU dalla ricetta corrente (chiamato quando un edit si
    /// assesta). Cheap: è tutto sulla GPU, nessun readback.</summary>
    public void Rebuild(PlanetTerrain terrain)
    {
        if (cs == null) return;

        shape?.Dispose();
        shape = GpuShapeBuffers.Build(cs, terrain, new[] { kFace });

        cs.SetInt("_R", res);
        int gFace = (gp + 7) / 8;
        int gNrm = (res + 7) / 8;
        for (int f = 0; f < 6; f++)
        {
            Vector3 up = PlanetMeshBuilder.FaceNormals[f];
            PlanetMeshBuilder.FaceAxes(up, out var axisA, out var axisB);
            cs.SetVector("_FaceUp", up);
            cs.SetVector("_AxisA", axisA);
            cs.SetVector("_AxisB", axisB);
            cs.SetInt("_FaceOffset", f * vertsPerFace);
            cs.Dispatch(kFace, gFace, gFace, 1);   // posizioni (incl. bordo)
            cs.Dispatch(kNorm, gNrm, gNrm, 1);     // normali geometriche dei vertici interni
        }

        float radius = terrain.Recipe != null ? terrain.Recipe.baseRadius : terrain.BaseRadius;
        bounds = new Bounds(Vector3.zero, Vector3.one * (radius * 2.5f));
        ApplyColor(terrain);
    }

    /// <summary>Aggiorna SOLO gli uniform di colore (edit di colore: niente rigenerazione della geometria).</summary>
    public void RefreshColor(PlanetTerrain terrain) { if (Ready) ApplyColor(terrain); }

    /// <summary>Passa allo shader la luce della scena + gli uniform di colore dalla RICETTA (stessa mappa di
    /// PlanetBaker.BuildMaterial: suolo/bacini/mare/saturazione). Gli altri (minerali/vette/macro/tinta) usano i
    /// default dello shader, come fa la CPU. Il colore è ricalcolato nel fragment, niente texture bakate.</summary>
    void ApplyColor(PlanetTerrain terrain)
    {
        if (mat == null) return;

        var sun = FindAnyObjectByType<Light>();
        Vector3 dir = sun != null ? -sun.transform.forward : Vector3.up;
        Color sc = sun != null ? sun.color * sun.intensity : Color.white;
        mat.SetVector("_SunDir", dir.normalized);
        mat.SetVector("_SunColor", new Vector4(sc.r, sc.g, sc.b, 1f));
        Color amb = RenderSettings.ambientLight;
        mat.SetVector("_Ambient", new Vector4(amb.r, amb.g, amb.b, 1f));

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
            }
            else mat.SetFloat("_SeaOn", 0f);
        }
    }

    /// <summary>Triangoli dei soli vertici INTERNI (1..res) di ogni faccia. Costruito una volta: la topologia
    /// non cambia, solo le posizioni (sulla GPU) variano con la ricetta.</summary>
    void BuildIndexBuffer()
    {
        int quadsPerFace = (res - 1) * (res - 1);
        indexCount = quadsPerFace * 6 * 6;
        var idx = new int[indexCount];
        int k = 0;
        for (int f = 0; f < 6; f++)
        {
            int baseV = f * vertsPerFace;
            for (int iy = 0; iy < res - 1; iy++)
                for (int ix = 0; ix < res - 1; ix++)
                {
                    int px = ix + 1, py = iy + 1;          // salta il bordo
                    int i00 = baseV + px + py * gp;
                    int i10 = baseV + (px + 1) + py * gp;
                    int i01 = baseV + px + (py + 1) * gp;
                    int i11 = baseV + (px + 1) + (py + 1) * gp;
                    idx[k++] = i00; idx[k++] = i01; idx[k++] = i11;   // Cull Off → il verso non conta
                    idx[k++] = i00; idx[k++] = i11; idx[k++] = i10;
                }
        }
        idxBuf = new GraphicsBuffer(GraphicsBuffer.Target.Index, indexCount, 4);
        idxBuf.SetData(idx);
    }

    void Update()
    {
        if (!Active || !Ready) return;
        var rp = new RenderParams(mat) { worldBounds = bounds };
        Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Triangles, idxBuf, indexCount);
    }

    void OnDestroy()
    {
        posBuf?.Release(); nrmBuf?.Release(); idxBuf?.Release();
        shape?.Dispose();
        if (mat != null) Destroy(mat);
    }
}

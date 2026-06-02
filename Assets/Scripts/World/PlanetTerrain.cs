using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Definisce la forma del terreno di un pianeta. È la verità condivisa: lo stesso
/// SampleHeight è usato sia per generare la mesh che dal PlanetWalker per tenere il
/// giocatore sulla superficie. Mesh e collisione non possono mai divergere.
///
/// La forma è una PIPELINE di processi sovrapposti (come la geologia reale): la forma
/// di base, poi i processi che la scolpiscono (impatti, vulcanismo, ...). Ogni
/// <see cref="TerrainLayer"/> riceve l'altezza prodotta dai precedenti e la modifica;
/// l'ordine è fisico. Aggiungere un processo = aggiungere un layer in <see cref="RebuildLayers"/>,
/// senza toccare il resto.
/// </summary>
public class PlanetTerrain : MonoBehaviour
{
    [Header("Forma di base")]
    public float BaseRadius = 500f;
    public float Amplitude = 30f;     // dislivello massimo della base (± rispetto al raggio)
    public float Frequency = 2.5f;    // scala delle feature sulla sfera unitaria
    public int Octaves = 6;
    public float Lacunarity = 2f;
    public float Gain = 0.5f;
    public int Seed = 1337;

    [Header("Crateri (processo: bombardamento)")]
    public bool CratersEnabled = true;
    public int CraterSeed = 7777;
    public int CraterOctaves = 4;             // bande di taglia: raggio dimezzato per ottava
    public float CraterLargestRadius = 110f;  // m: raggio del cratere più grande del campo
    public float CraterDensity = 0.55f;       // prob. che una cella contenga un cratere
    public float CraterDepthRatio = 0.20f;    // profondità conca = ratio × raggio
    public float CraterRimRatio = 0.30f;      // altezza bordo = ratio × profondità
    [Space]
    public bool DominantCrater = true;        // un grande impatto (tipo Stickney su Phobos)
    public Vector3 DominantCraterDir = new Vector3(0.3f, 1f, 0.2f);
    public float DominantCraterRadius = 230f; // m

    readonly List<TerrainLayer> layers = new List<TerrainLayer>();
    bool built;

    /// <summary>
    /// (Ri)costruisce la pipeline dai parametri correnti. Lazy: parte da sola al primo
    /// SampleHeight. Chiamala a mano solo se cambi i parametri a runtime (es. dall'inspector).
    /// L'ORDINE in cui aggiungi i layer è l'ordine dei processi geologici.
    /// </summary>
    public void RebuildLayers()
    {
        layers.Clear();
        // 1. Forma di base del corpo.
        layers.Add(new BaseTerrainLayer(BaseRadius, Amplitude, Frequency, Octaves, Lacunarity, Gain, Seed));
        // 2. Bombardamento: crateri scavati nella forma.
        if (CratersEnabled)
        {
            var craters = new CraterTerrainLayer(BaseRadius, CraterSeed, CraterOctaves,
                CraterLargestRadius, CraterDensity, CraterDepthRatio, CraterRimRatio);
            if (DominantCrater) craters.AddManual(DominantCraterDir, DominantCraterRadius);
            layers.Add(craters);
        }
        // 3. ...i processi successivi (mari, ...) si aggiungono qui, in ordine fisico.
        built = true;
    }

    void EnsureBuilt() { if (!built) RebuildLayers(); }

    // Cambiare un parametro dall'inspector invalida la pipeline: si ricostruisce al prossimo sample.
    void OnValidate() { built = false; }

    /// <summary>Distanza dal centro della superficie nella direzione (unitaria) data.</summary>
    public float SampleHeight(Vector3 unitDir)
    {
        EnsureBuilt();
        float h = 0f;
        for (int i = 0; i < layers.Count; i++)
            h = layers[i].Apply(unitDir, h);
        return h;
    }

    /// <summary>
    /// Normale della superficie nella direzione data, calcolata dalla pendenza del terreno
    /// (differenze centrali sui due assi tangenti). Dipende solo da direzione e altezza,
    /// quindi è continua su tutta la sfera: niente cuciture tra le facce. Funziona con
    /// qualunque pipeline di layer, perché legge solo SampleHeight.
    /// </summary>
    public Vector3 SurfaceNormal(Vector3 dir, float eps)
    {
        dir = dir.normalized;
        Vector3 refV = Mathf.Abs(dir.y) < 0.99f ? Vector3.up : Vector3.right;
        Vector3 tA = Vector3.Normalize(Vector3.Cross(dir, refV));
        Vector3 tB = Vector3.Cross(dir, tA);

        Vector3 dAp = (dir + tA * eps).normalized;
        Vector3 dAm = (dir - tA * eps).normalized;
        Vector3 dBp = (dir + tB * eps).normalized;
        Vector3 dBm = (dir - tB * eps).normalized;

        Vector3 pAp = dAp * SampleHeight(dAp);
        Vector3 pAm = dAm * SampleHeight(dAm);
        Vector3 pBp = dBp * SampleHeight(dBp);
        Vector3 pBm = dBm * SampleHeight(dBm);

        Vector3 n = Vector3.Cross(pBp - pBm, pAp - pAm).normalized;
        if (Vector3.Dot(n, dir) < 0f) n = -n;   // sempre verso l'esterno
        return n;
    }
}

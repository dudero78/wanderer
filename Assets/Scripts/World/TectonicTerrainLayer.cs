using UnityEngine;

/// <summary>
/// Processo TETTONICA: dà al corpo la struttura su grande scala di un pianeta "terrestre" — CONTINENTI e
/// BACINI OCEANICI — e le catene/rift che nascono ai confini delle placche. Non simula il tempo: genera un
/// risultato finale plausibile, e la "storia" emerge dalle relazioni spaziali (montagne dove le placche si
/// scontrano, rift dove si separano).
///
/// Come: la sfera è partizionata in N PLACCHE (Voronoi sferico — ogni punto appartiene al seme più vicino).
/// Ogni placca è CONTINENTALE (quota alta) o OCEANICA (quota bassa) → continenti/oceani. Ai CONFINI fra due
/// placche si guarda il loro moto relativo: convergenti → SOLLEVAMENTO (catene); divergenti → RIFT (avvallano).
/// Un domain-warp piega i confini → coste frastagliate, non poligoni netti. La transizione continente↔oceano è
/// una SCARPATA dolce (mescola le due quote vicino al confine).
///
/// Per-campione, deterministico (dal seme), thread-safe: le placche si calcolano UNA volta nel costruttore
/// (sola lettura durante il sampling). Costo ≈ O(N placche) per campione. Portabile su GPU per B1 (le placche
/// = un piccolo buffer di semi/tipi/moti).
/// </summary>
public class TectonicTerrainLayer : TerrainLayer
{
    readonly int n;
    readonly Vector3[] seedDir;     // direzioni-seme delle placche (unitarie)
    readonly Vector3[] motion;      // moto della placca (tangente, ~unitario)
    readonly float[] elevJitter;    // piccola variazione di quota per placca
    readonly bool[] continental;
    readonly int seed;
    readonly float contrast;        // dislivello continente↔oceano (m)
    readonly float uplift;          // sollevamento/rift max ai confini (m)
    readonly float boundaryWidth;   // ampiezza della fascia di confine (in unità di differenza-di-coseno)
    readonly float warp;            // intensità del domain-warp dei confini
    readonly float coastSlope;      // 0 = costa a scogliera, 1 = piattaforma continentale dolce

    public TectonicTerrainLayer(float baseRadius, int seed, int plateCount, float continentalFraction,
                                float elevationContrast, float boundaryUplift, float boundaryWidth, float warp, float coastSlope)
    {
        this.seed = seed;
        n = Mathf.Clamp(plateCount, 2, 64);
        contrast = elevationContrast;
        uplift = boundaryUplift;
        this.boundaryWidth = Mathf.Max(0.005f, boundaryWidth);
        this.warp = Mathf.Max(0f, warp);
        this.coastSlope = Mathf.Clamp01(coastSlope);

        seedDir = new Vector3[n];
        motion = new Vector3[n];
        elevJitter = new float[n];
        continental = new bool[n];

        uint rng = (uint)(seed * 747796405 + 2891336453);
        for (int i = 0; i < n; i++)
        {
            seedDir[i] = RandUnit(ref rng);
            // moto: una direzione casuale resa TANGENTE al seme (deriva della placca sulla sfera)
            Vector3 m = RandUnit(ref rng);
            m -= seedDir[i] * Vector3.Dot(m, seedDir[i]);
            motion[i] = m.sqrMagnitude > 1e-6f ? m.normalized : Vector3.zero;
            continental[i] = Rand01(ref rng) < continentalFraction;
            elevJitter[i] = (Rand01(ref rng) - 0.5f) * 2f * (contrast * 0.15f);
        }
    }

    public override float Apply(Vector3 unitDir, float height)
    {
        // domain-warp FRATTALE (due scale): piega lo spazio prima di trovare le placche → coste molto più
        // frastagliate, irregolari a più frequenze. Più alto = più frastagliato.
        Vector3 d = unitDir;
        if (warp > 0f)
        {
            Vector3 w1 = new Vector3(
                Noise3D.Value(unitDir * 3f, seed + 101),
                Noise3D.Value(unitDir * 3f, seed + 202),
                Noise3D.Value(unitDir * 3f, seed + 303));
            Vector3 w2 = new Vector3(
                Noise3D.Value(unitDir * 7.3f, seed + 404),
                Noise3D.Value(unitDir * 7.3f, seed + 505),
                Noise3D.Value(unitDir * 7.3f, seed + 606));
            d = (unitDir + (w1 * 0.7f + w2 * 0.35f) * warp).normalized;
        }

        // placca più vicina e seconda (per il confine): massimo prodotto scalare = minima distanza angolare
        int i1 = 0, i2 = 0;
        float best = -2f, second = -2f;
        for (int i = 0; i < n; i++)
        {
            float dt = Vector3.Dot(d, seedDir[i]);
            if (dt > best) { second = best; i2 = i1; best = dt; i1 = i; }
            else if (dt > second) { second = dt; i2 = i; }
        }

        float b1 = (continental[i1] ? contrast * 0.5f : -contrast * 0.5f) + elevJitter[i1];
        float b2 = (continental[i2] ? contrast * 0.5f : -contrast * 0.5f) + elevJitter[i2];

        float edge = best - second;   // ~0 sul confine, cresce verso l'interno della placca

        // SCARPATA (quota): la transizione fra le due placche avviene su una fascia LARGA quanto 'coastSlope'
        // → piattaforme continentali dolci invece di altopiani a pareti verticali. Banda separata da quella
        // del sollevamento, così le coste sono morbide ma le catene restano localizzate sul confine.
        float coastBand = boundaryWidth * Mathf.Lerp(1f, 12f, coastSlope);
        float tc = Mathf.Clamp01(edge / coastBand);
        float wc = tc * tc * (3f - 2f * tc);
        float elev = Mathf.Lerp((b1 + b2) * 0.5f, b1, wc);

        // CONFINE (sollevamento/rift): fascia STRETTA su boundaryWidth, moto relativo lungo la normale.
        float tu = Mathf.Clamp01(edge / boundaryWidth);
        float boundary = 1f - tu * tu * (3f - 2f * tu);     // 1 sul confine, 0 dentro
        if (boundary > 0.001f && uplift > 0f)
        {
            Vector3 bn = (seedDir[i1] - seedDir[i2]);
            float bm = bn.magnitude;
            if (bm > 1e-5f)
            {
                bn /= bm;
                float conv = Vector3.Dot(motion[i2] - motion[i1], bn);   // >0 convergono → catena; <0 → rift
                elev += uplift * boundary * Mathf.Clamp(conv, -1f, 1f);
            }
        }

        return height + elev;
    }

    // --- RNG locale deterministico (LCG): niente UnityEngine.Random (globale, non thread-safe) ---
    static float Rand01(ref uint s) { s = s * 1664525u + 1013904223u; return (s >> 8) * (1f / 16777216f); }
    static Vector3 RandUnit(ref uint s)
    {
        float z = Rand01(ref s) * 2f - 1f;
        float a = Rand01(ref s) * 6.2831853f;
        float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
        return new Vector3(Mathf.Cos(a) * r, z, Mathf.Sin(a) * r);
    }
}

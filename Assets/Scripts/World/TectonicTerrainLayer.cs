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
    readonly float continentalRelief; // ampiezza (m) del rilievo INTERNO dei continenti (gli oceani restano lisci)
    readonly float riftBalance;       // 0 = solo catene; 1 = rift (divergenti) profondi quanto le catene

    // scale dei campi di rumore: COSTANTI condivise con PlanetHeight.compute (TectonicApply) per la parità.
    // Cambiarle qui = cambiarle lì identiche, o GPU e CPU divergono.
    const float ContReliefScale = 3.0f;   // rilievo continentale: scala dei crinali (basso = forme larghe)
    const float RidgeAlongScale = 1.8f;   // variazione di quota LUNGO la catena (passi/picchi)
    const float RidgeRoughScale = 9.0f;   // ruvidità FINE della cresta (frastagliata, non liscia)

    public TectonicTerrainLayer(float baseRadius, int seed, int plateCount, float continentalFraction,
                                float elevationContrast, float boundaryUplift, float boundaryWidth, float warp, float coastSlope,
                                float continentalRelief, float riftBalance)
    {
        this.seed = seed;
        n = Mathf.Clamp(plateCount, 2, 64);
        contrast = elevationContrast;
        uplift = boundaryUplift;
        this.boundaryWidth = Mathf.Max(0.005f, boundaryWidth);
        this.warp = Mathf.Max(0f, warp);
        this.coastSlope = Mathf.Clamp01(coastSlope);
        this.continentalRelief = Mathf.Max(0f, continentalRelief);
        this.riftBalance = Mathf.Clamp01(riftBalance);

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

    // Accessori per l'upload su GPU (parità della Tappa 2): le placche sono generate UNA volta qui nel
    // costruttore → la GPU le riceve identiche invece di rigenerarle (niente RNG da replicare in HLSL).
    public int PlateCount => n;
    public Vector3 PlateSeedDir(int i) => seedDir[i];
    public Vector3 PlateMotion(int i) => motion[i];
    public float PlateElevJitter(int i) => elevJitter[i];
    public bool PlateContinental(int i) => continental[i];
    public int Seed => seed;
    public float Contrast => contrast;
    public float Uplift => uplift;
    public float BoundaryWidth => boundaryWidth;
    public float Warp => warp;
    public float CoastSlope => coastSlope;
    public float ContinentalRelief => continentalRelief;
    public float RiftBalance => riftBalance;

    public override float Apply(Vector3 unitDir, float height)
    {
        // domain-warp FRATTALE (due scale): piega lo spazio prima di trovare le placche → coste molto più
        // frastagliate, irregolari a più frequenze. Più alto = più frastagliato.
        // L'ampiezza è SCALATA con la dimensione della placca (√(8/n), ancorata al default 8 → a 8 placche il
        // look è invariato): con molte placche le celle sono piccole e un warp grande le RIPIEGAVA, chiudendo
        // anse → grappoli di "bolle"/lobi ai confini. Tenendo lo spostamento proporzionato alla cella, il warp
        // frastaglia la costa senza ripiegarla. Clamp a 1.5 per non esagerare con pochissime placche.
        Vector3 d = unitDir;
        if (warp > 0f)
        {
            float warpAmp = warp * Mathf.Min(1.5f, Mathf.Sqrt(8f / n));
            Vector3 w1 = new Vector3(
                Noise3D.Value(unitDir * 3f, seed + 101),
                Noise3D.Value(unitDir * 3f, seed + 202),
                Noise3D.Value(unitDir * 3f, seed + 303));
            Vector3 w2 = new Vector3(
                Noise3D.Value(unitDir * 7.3f, seed + 404),
                Noise3D.Value(unitDir * 7.3f, seed + 505),
                Noise3D.Value(unitDir * 7.3f, seed + 606));
            d = (unitDir + (w1 * 0.7f + w2 * 0.35f) * warpAmp).normalized;
        }

        // SOFT VORONOI: la quota base è la media PESATA di TUTTE le placche (peso che sfuma con la distanza
        // angolare) → CONTINUA ovunque. Niente più salti dove cambia la "seconda placca più vicina" (era la
        // causa delle PARETI VERTICALI che la griglia scalinava). 'coastSlope' regola la nitidezza: alto =
        // coste ripide ma sempre continue (la griglia le risolve), basso = piattaforme dolci.
        float sharp = Mathf.Lerp(40f, 5f, coastSlope);
        float wsum = 0f, esum = 0f;
        // 3 più vicine: i1,i2 per la catena; 'third' serve SOLO al gate di continuità sotto.
        int i1 = 0, i2 = 0; float best = -2f, second = -2f, third = -2f;
        for (int i = 0; i < n; i++)
        {
            float dt = Vector3.Dot(d, seedDir[i]);
            float w = Mathf.Exp(sharp * (dt - 1f));           // 1 sulla placca, decade con la distanza
            float bi = (continental[i] ? contrast * 0.5f : -contrast * 0.5f) + elevJitter[i];
            wsum += w; esum += w * bi;
            if (dt > best) { third = second; second = best; i2 = i1; best = dt; i1 = i; }
            else if (dt > second) { third = second; second = dt; i2 = i; }
            else if (dt > third) { third = dt; }
        }
        float elev = esum / wsum;

        // RILIEVO INTERNO DEI CONTINENTI. Senza, i continenti sono altopiani biliardo (look CGI); con un fBm
        // piatto diventa "grana uniforme / guscio di noce". La cura è MULTI-SCALA: (1) 'contW' lo confina ai
        // continenti (oceani lisci); (2) 'mtn' (campo a bassa freq) lo MODULA nello spazio → alcune regioni
        // pianeggianti, altre montuose (rompe l'uniformità); (3) 'ridge' (ridged noise) dà CRINALI dissezionati
        // invece di gobbe tonde. Scale/seed IDENTICI a PlanetHeight.compute per la parità.
        // PERF: il lavoro è proporzionale a DOVE c'è rilievo. Gli oceani (contW≈0) saltano tutto; le pianure
        // (mtn≈0) saltano il 'ridge' (4 campioni). Le stesse soglie nei due path → parità intatta (il termine
        // saltato vale ~0, ha fattore contW·mtn). Così metà pianeta (oceano) non paga il rumore extra.
        if (continentalRelief > 0f)
        {
            float contW = Mathf.Clamp01((elev + contrast * 0.5f) / Mathf.Max(contrast, 1e-3f));
            if (contW > 0.01f)
            {
                float mtn = Mathf.Clamp01(Noise3D.Fbm(unitDir * 1.4f, 3, 2f, 0.5f, seed + 831) * 1.7f - 0.35f);
                if (mtn > 0.001f)
                {
                    float ridge = Noise3D.Ridged(unitDir * ContReliefScale, 4, 2f, 0.5f, seed + 821);   // [0,1] crinali
                    elev += continentalRelief * contW * mtn * (ridge - 0.30f) * 1.8f;
                }
            }
        }

        // CONFINI: catene (placche convergenti) / rift (divergenti), localizzati sulla fascia del confine i1↔i2.
        // La gobba liscia di prima dava "welt" di argilla a quota costante. Ora il profilo è MODULATO: 'along'
        // (bassa freq lungo il confine) fa salire/scendere la catena → picchi e valichi; 'rough' (ridged, freq
        // alta) la rende frastagliata invece di liscia. Insieme: catene vere, non vermi incollati.
        float tu = Mathf.Clamp01((best - second) / boundaryWidth);
        float boundary = 1f - tu * tu * (3f - 2f * tu);
        // GATE DI CONTINUITÀ (fix delle "crepe"): il termine usa l'IDENTITÀ della 2ª placca (i2) per conv/bn.
        // Lungo le linee dove la 2ª e la 3ª placca sono equidistanti, i2 SALTA da una placca all'altra → conv
        // salta → la quota fa un GRADINO verticale (la crepa, indipendente dalla risoluzione perché è nella
        // funzione). Smorzando il ridge A ZERO esattamente su quelle linee (gate→0 quando second≈third), il
        // termine è 0 su entrambi i lati del salto → quota CONTINUA, niente gradino. Lontano da lì gate→1 e la
        // catena è piena. Conseguenza voluta: i ridge si attenuano vicino ai punti tripli (naturale).
        float sd = Mathf.Clamp01((second - third) / boundaryWidth);
        float gate = sd * sd * (3f - 2f * sd);
        if (boundary * gate > 0.001f && uplift > 0f)
        {
            Vector3 bn = seedDir[i1] - seedDir[i2];
            float bm = bn.magnitude;
            if (bm > 1e-5f)
            {
                bn /= bm;
                float conv = Mathf.Clamp(Vector3.Dot(motion[i2] - motion[i1], bn), -1f, 1f);   // >0 catena; <0 rift
                float along = Noise3D.Fbm(d * RidgeAlongScale, 3, 2f, 0.5f, seed + 711);        // [0,1]
                float rough = 1f - Mathf.Abs(Noise3D.Value(d * RidgeRoughScale, seed + 712));   // [0,1] picchi dove |n|~0
                // D: i RIFT (divergenti, conv<0) sono molto meno marcati delle CATENE — più bassi (RiftScale) e
                // meno affilati (meno peso al rough). Sulla Terra i rift continentali sono tenui; le dorsali profonde
                // stanno sott'acqua. Evita le "crepe nere" affilate che tagliavano i continenti.
                bool rift = conv < 0f;
                float convEff = rift ? conv * riftBalance : conv;
                float rg = rift ? (0.70f + 0.30f * rough) : (0.45f + 0.55f * rough);
                float profile = boundary * gate * (0.30f + 0.70f * along) * rg;
                elev += uplift * profile * convEff;
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

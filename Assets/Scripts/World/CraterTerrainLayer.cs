using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Processo "bombardamento": scolpisce crateri nella forma del terreno (geometria vera,
/// non texture — ci cammini dentro). Il profilo segue la morfologia reale e dipende dalla
/// TAGLIA: i piccoli sono ciotole semplici; i grandi diventano "complessi" (fondo piatto +
/// PICCO CENTRALE). Sempre: BORDO rialzato che cattura la luce + coltre di EJECTA che sfuma.
/// Tutti i raccordi sono morbidi (C1, pendenza nulla alla cresta) per non aliasare sulla mesh.
///
/// Distribuzione a legge di potenza per OTTAVE di taglia: tanti piccoli, pochi grandi.
/// Ogni ottava ha raggio dimezzato e griglia più fitta. I crateri sono sparsi con una
/// griglia 3D hashata nello spazio (NON sulle facce del cubo) → niente cuciture ai bordi
/// faccia, continuo su tutta la sfera. A ogni SampleHeight si interrogano solo le celle
/// VICINE (intorno 3×3×3 per ottava): costo limitato e costante, niente liste da scorrere.
///
/// Composizione fra crateri sovrapposti: la conca più profonda VINCE (min), il bordo più
/// alto VINCE (max) — niente somma, così due crateri sovrapposti non scavano il doppio.
///
/// THREAD-SAFE: solo matematica e hash pure (gira sui thread di build del quadtree).
/// </summary>
public class CraterTerrainLayer : TerrainLayer
{
    readonly float baseRadius;
    readonly int seed, octaves;
    readonly float largestRadius;   // m: raggio del cratere più grande del campo
    readonly float density;         // prob. che una cella contenga un cratere [0..1]
    readonly float depthRatio;      // profondità conca = depthRatio × raggio
    readonly float rimRatio;        // altezza bordo   = rimRatio × profondità
    readonly float rimSharpness;    // esponente della parete: 1 = cono, >1 = fondo piatto + bordo a cresta netta
    readonly float wLarge, wMedium, wSmall;   // quota relativa per fascia di taglia (modula la densità per ottava)
    readonly float distribution;              // fase 0..1: ruota il campo procedurale → i crateri scorrono sul pianeta

    // Crateri "a mano" (es. il dominante tipo Stickney): valutati sempre, fuori dal campo.
    struct Manual { public Vector3 dir; public float radius; }
    readonly List<Manual> manual = new List<Manual>();

    // Spaziatura fra crateri della stessa ottava, in raggi. Il centro è jitterato di UNA cella
    // intera e la sua influenza arriva fino a OUTER × jitterMax (2.2 × 1.4 = 3.08) raggi: perché un
    // cratere influente cada SEMPRE dentro l'intorno 3×3×3 che interroghiamo, la cella deve essere
    // larga abbastanza da contenere centro + influenza, cioè SPACING ≳ 2 × OUTER × jitterMax in raggi.
    // Con SPACING = 10 il pop di un cratere al bordo della finestra scende sotto la pendenza che la
    // mesh sa risolvere → niente gradino d'altezza (la "lama" verticale sull'ejecta/rim sparisce).
    const float SPACING = 10f;
    const float OUTER = 2.2f;        // oltre 2.2 raggi il cratere non influenza più (ejecta esaurita)
    const float JITTER_MAX = 1.4f;   // raggio jitterato fino a 1.4× quello dell'ottava
    // PESO RADIALE sul GUSCIO unitario. Il reticolo 3D proiettato sulla sfera ha una degenerazione: celle a
    // raggi diversi lungo la stessa direzione proiettano sullo stesso punto e contribuiscono tutte; la finestra
    // di ricerca ne tronca un numero arbitrario → cambiando punto (o Raggio max, che riscala il reticolo) un
    // cratere appare/sparisce di colpo = gradino ("crepa circolare"). Pesando ogni cratere per quanto la sua
    // cella è vicina al guscio (|c|≈1) i duplicati fuori-guscio sfumano a 0 con CONTINUITÀ. + finestra 5×5×5
    // (RANGE) perché con celle grandi i crateri grandi cadono anche a 2 celle. Verificato: salto 3.1 m → <0.12 m.
    const float SHELL_HALF = 0.6f;   // mezza-larghezza del guscio (in unità-direzione)
    const int RANGE = 2;             // intorno ±2 = 5×5×5

    // Soglie di morfologia (raggio, metri): sotto SIMPLE = ciotola semplice; sopra COMPLEX =
    // pienamente complesso (fondo piatto + picco centrale). In mezzo si interpola.
    const float SIMPLE_MAX = 60f;
    const float COMPLEX_MAX = 160f;
    // Picco centrale dei crateri complessi: altezza (frazione della profondità) e larghezza
    // (frazione del raggio). Più alto e più stretto = pinnacolo più marcato.
    const float PEAK_HEIGHT = 0.8f;
    const float PEAK_WIDTH = 0.13f;

    public CraterTerrainLayer(float baseRadius, int seed, int octaves, float largestRadius,
                              float density, float depthRatio, float rimRatio, float rimSharpness = 2f,
                              float wLarge = 1f, float wMedium = 1f, float wSmall = 1f, float distribution = 0f)
    {
        this.baseRadius = baseRadius;
        this.seed = seed;
        this.octaves = Mathf.Max(1, octaves);
        this.largestRadius = largestRadius;
        this.density = Mathf.Clamp01(density);
        this.depthRatio = depthRatio;
        this.rimRatio = rimRatio;
        this.rimSharpness = Mathf.Max(1f, rimSharpness);
        this.wLarge = Mathf.Clamp01(wLarge);
        this.wMedium = Mathf.Clamp01(wMedium);
        this.wSmall = Mathf.Clamp01(wSmall);
        this.distribution = distribution;
    }

    // DISTRIBUZIONE = DRIFT DEL CENTRO. Far "scorrere" rigidamente i crateri sulla sfera sarebbe per forza una
    // ROTAZIONE (le uniche isometrie della sfera) → sembra girare il pianeta; deformare il campo (warp) li
    // STIRA. La via giusta: muovere il CENTRO di ogni cratere dentro la sua cella, ognuno nella propria
    // direzione/velocità (dall'hash) → l'insieme si ridistribuisce restando fatto di cerchi. PP = ping-pong
    // (onda triangolare) che tiene il centro in [0,1] senza salti; a distribuzione=0 ridà la posizione
    // originale (PP(j0)=j0). Identico in PlanetHeight.compute.
    static float PP(float x)
    {
        float r = x - 2f * Mathf.Floor(x * 0.5f);
        return 1f - Mathf.Abs(r - 1f);
    }

    static float Jitter(uint hBase, uint hSpeed, float dist)
    {
        float j0 = U01(hBase);
        if (dist <= 0f) return j0;
        float spd = 0.5f + U01(hSpeed);          // velocità per-cratere [0.5,1.5]
        return PP(j0 + dist * spd);
    }

    /// <summary>Peso della densità per l'ottava o (0 = taglia più grande … octaves-1 = più piccola): interpola
    /// fra grandi→medi→piccoli lungo la gamma di taglie. Una sola ottava = trattata come "grandi".</summary>
    float SizeWeight(int o)
    {
        if (octaves <= 1) return wLarge;
        float t = o / (float)(octaves - 1);                 // 0 = grandi, 1 = piccoli
        return t <= 0.5f ? Mathf.Lerp(wLarge, wMedium, t * 2f)
                         : Mathf.Lerp(wMedium, wSmall, (t - 0.5f) * 2f);
    }

    /// <summary>Aggiunge un cratere piazzato a mano (il dominante). dir non serve normalizzata.</summary>
    public void AddManual(Vector3 dir, float radius)
    {
        manual.Add(new Manual { dir = dir.normalized, radius = radius });
    }

    public override float Apply(Vector3 unitDir, float height)
    {
        // composizione ADDITIVA: somma dei contributi (ognuno una funzione LISCIA C1). La somma è
        // liscia → la mesh la rappresenta senza spigoli/seghettature. Crateri sovrapposti si
        // approfondiscono un po' (doppia conca), che è anche geologicamente vero.
        float total = 0f;

        // --- crateri a mano (dominante): nel frame del MONDO, NON ruotano con la distribuzione ---
        for (int i = 0; i < manual.Count; i++)
            Accumulate(unitDir, manual[i].dir, manual[i].radius, 1f, ref total);   // piazzato sulla sfera: peso 1

        // --- campo procedurale per ottave di taglia ---
        float radius = largestRadius;
        for (int o = 0; o < octaves; o++)
        {
            float spacing = radius * SPACING;            // m
            float cellAng = spacing / baseRadius;        // dimensione cella sulla sfera unitaria (~rad)
            float gscale = 1f / cellAng;                 // dir*gscale → coordinate di cella
            float octDensity = density * SizeWeight(o);  // meno/più crateri di questa fascia di taglia

            Vector3 g = unitDir * gscale;
            int cx = Mathf.FloorToInt(g.x), cy = Mathf.FloorToInt(g.y), cz = Mathf.FloorToInt(g.z);

            for (int dz = -RANGE; dz <= RANGE; dz++)
            for (int dy = -RANGE; dy <= RANGE; dy++)
            for (int dx = -RANGE; dx <= RANGE; dx++)
            {
                int X = cx + dx, Y = cy + dy, Z = cz + dz;
                uint h = Hash(X, Y, Z, seed + o * 9176);
                if (U01(h) > octDensity) continue;       // questa cella non ha cratere di questa fascia

                // centro jitterato dentro la cella + DRIFT della distribuzione (ogni cratere scivola nella sua cella)
                Vector3 c = new Vector3(
                    (X + Jitter(h * 0x9E3779B1u + 1u, h * 0x9E3779B1u + 11u, distribution)) / gscale,
                    (Y + Jitter(h * 0x85EBCA77u + 2u, h * 0x85EBCA77u + 12u, distribution)) / gscale,
                    (Z + Jitter(h * 0xC2B2AE3Du + 3u, h * 0xC2B2AE3Du + 13u, distribution)) / gscale);
                float cm = c.magnitude;
                if (cm < 1e-6f) continue;
                // peso radiale sul guscio: scarta/sfuma i duplicati fuori-guscio (vedi SHELL_HALF)
                float radOff = Mathf.Abs(cm - 1f) / SHELL_HALF;
                if (radOff >= 1f) continue;
                float radialW = Smooth01(1f - radOff);
                Vector3 cdir = c / cm;

                // raggio jitterato attorno al raggio dell'ottava, simmetrico: [2−JITTER_MAX .. JITTER_MAX]
                float lo = 2f - JITTER_MAX;
                float rad = radius * (lo + (JITTER_MAX - lo) * U01(h * 0x27D4EB2Fu + 4u));
                Accumulate(unitDir, cdir, rad, radialW, ref total);
            }

            radius *= 0.5f;
        }

        return height + total;
    }

    /// <summary>Profilo del cratere (conca + bordo + ejecta + picco) sommato al totale, scalato per 'weight'
    /// (peso radiale sul guscio: 1 per i crateri a mano, &lt;1 per le celle fuori-guscio).</summary>
    void Accumulate(Vector3 dir, Vector3 cdir, float radius, float weight, ref float total)
    {
        // distanza sulla superficie ≈ corda × raggio del corpo (errore trascurabile per crateri << corpo)
        float distM = baseRadius * (dir - cdir).magnitude;
        float r = distM / radius;                        // distanza normalizzata: 0 centro, 1 bordo
        if (r >= OUTER) return;

        float depth = radius * depthRatio;
        float rim = depth * rimRatio;
        // morfologia: 0 = ciotola semplice (piccoli), 1 = complesso (grandi: fondo piatto + picco)
        float cx = Mathf.Clamp01((radius - SIMPLE_MAX) / (COMPLEX_MAX - SIMPLE_MAX));

        // conca: fondo (eventualmente piatto) che RISALE al bordo. La parete usa una legge di potenza
        // (esponente rimSharpness): a 1 è un cono, sopra 1 il fondo si appiattisce e la parete impenna
        // verso il bordo → CRESTA NETTA (la pendenza alla cresta non è più zero: spigolo C0 voluto, è
        // il "bordo quasi tagliente"). Resta continua in valore → la mesh non si spezza. Solo i crateri
        // grandi prendono in più un fondo piatto (conca complessa).
        float floorR = 0.3f * cx;                        // raggio del fondo piatto, solo sui grandi
        float cav;
        if (r < floorR) cav = -1f;                       // fondo piatto
        else if (r < 1f) { float t = (r - floorR) / (1f - floorR); cav = -(1f - Mathf.Pow(t, rimSharpness)); }
        else cav = 0f;

        // bordo + ejecta: cresta più STRETTA (crestina marcata sul bordo), coda esterna più corta
        float dr = r - 1f;
        float w = (r <= 1f) ? 0.42f : 0.7f;
        float ring = Mathf.Exp(-(dr * dr) / (w * w));

        // picco centrale: solo crateri complessi. Pinnacolo che si alza dal fondo piatto.
        float peak = 0f;
        if (cx > 0f) { float pr = r / PEAK_WIDTH; peak = cx * depth * PEAK_HEIGHT * Mathf.Exp(-pr * pr); }

        float off = depth * cav + rim * ring + peak;     // metri
        // finestra C1 al bordo esterno (ejecta): porta off a 0 con CONTINUITÀ. Senza, il taglio netto
        // a r=OUTER è un gradino di altezza → lame verticali sulla mesh lungo gli anelli di ejecta.
        off *= Smooth01(Mathf.Clamp01((OUTER - r) / 0.6f));
        // ADDITIVO: somma del contributo (× peso radiale). Ogni cratere è C1, la somma è C1 → niente spigoli,
        // niente seghettature. Niente min/max (creano spigoli a V) né smin/smax (gobbe fantasma contro lo zero).
        total += off * weight;
    }

    // smoothstep [0,1]: pendenza 0 ai due estremi → raccordi C1, niente spigoli che aliasano
    static float Smooth01(float t) => t * t * (3f - 2f * t);

    // --- hash interi (mixing sequenziale, niente XOR lineare: vedi lezioni in CLAUDE.md) ---
    static uint Hash(int x, int y, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)seed;
            h = (h + (uint)x) * 0x9E3779B1u; h ^= h >> 16;
            h = (h + (uint)y) * 0x85EBCA77u; h ^= h >> 13;
            h = (h + (uint)z) * 0xC2B2AE3Du; h ^= h >> 16;
            h *= 0x27D4EB2Fu; h ^= h >> 15;
            return h;
        }
    }

    static float U01(uint h) => (h & 0xFFFFFFu) / 16777215f;
}

#ifndef WANDERER_PLANET_HEIGHT_CORE
#define WANDERER_PLANET_HEIGHT_CORE

// PlanetHeight.compute — calcola sulla GPU la stessa altezza di superficie che il walker
// (PlanetTerrain.SampleHeight) calcola sulla CPU. È il cuore della strada "geometria su GPU":
// il quadtree non ricalcola più il rumore per vertice su thread CPU (caldo, lento al load), lo
// fa qui in parallelo dove c'è il 95% di margine. Il walker resta analitico su CPU.
//
// PARITÀ ASSOLUTA con C# (Noise3D + BaseTerrainLayer + CraterTerrainLayer): se le due altezze
// divergono, il giocatore galleggia/sprofonda sui nodi. Quindi questo file è una traduzione
// FEDELE, non "equivalente". Trappole chiuse:
//  - cast int->uint con asuint() (REINTERPRETA i bit, come il cast unchecked di C#); (uint)x in
//    HLSL fa una CONVERSIONE che sui negativi non combacia → gradienti diversi → terreno diverso.
//  - gradiente diviso per 511.5 (non 1023/2): identico a Noise3D.Grad.
//  - rotazione del dominio per ottava (non traslazione), seed+i*1013, normalizzazione su 'norm'.
//  - hash crateri con le STESSE costanti di CraterTerrainLayer.Hash, U01 su 0xFFFFFF.


// ---- parametri della forma base (uniform, una volta per pianeta) ----
float  _BaseRadius;
float  _Amplitude;
float  _Frequency;
int    _Octaves;
float  _Lacunarity;
float  _Gain;
int    _Seed;

// ---- campo crateri: una voce per pipeline della ricetta ----
struct CraterGPU
{
    float seed;
    float octaves;
    float largestRadius;
    float density;
    float depthRatio;
    float rimRatio;
    float rimSharpness;
    float hasDominant;     // 0/1
    float3 dominantDir;    // unitaria
    float dominantRadius;
    float wLarge;          // pesi densità per fascia di taglia (grandi/medi/piccoli)
    float wMedium;
    float wSmall;
    float distribution;    // fase 0..1: drift dei centri (ridistribuzione)
    float domDepthRatio;   // profilo+irregolarità PROPRI del dominante
    float domRimRatio;
    float domRimSharp;
    float domIrregular;
    float domIrregScale;
    float bigCraters;      // flag owned-cell (0/1)
};
StructuredBuffer<CraterGPU> _Craters;

// ---- mare: una voce per processo "Mare" della ricetta (= SeaTerrainLayer) ----
struct SeaGPU { float seaRadius; float roughness; float roughScale; float forma; float seed; };
StructuredBuffer<SeaGPU> _Seas;

// ---- tettonica: una voce per processo + placche concatenate (= TectonicTerrainLayer) ----
// PlateGPU: float4 (non float3) per evitare il disallineamento dei float3 su Metal.
// seedDir.w = continentale (0/1); motion.w = elevJitter.
struct PlateGPU { float4 seedDir; float4 motion; };
StructuredBuffer<PlateGPU> _Plates;
struct TectonicGPU { float seed; float contrast; float uplift; float boundaryWidth; float warp; float coastSlope; float plateOffset; float plateCount; float continentalRelief; float riftBalance; };
StructuredBuffer<TectonicGPU> _Tectonics;

// ---- pipeline ORDINATA: (type, index) per processo. type 0=cratere 1=mare 2=tettonica ----
StructuredBuffer<float2> _Process;
int _ProcessCount;

// ---- parametri del nodo (per dispatch, kernel grid) ----
float3 _FaceUp, _AxisA, _AxisB;
float  _U0, _V0, _Step;
int    _NE;                       // lato della griglia estesa (nodeRes+3)
int    _HasSea;                   // 1 se la ricetta ha un mare → calcola la normale del fondo; 0 = salta (no spreco)
// posizioni locali (dir*altezza), NE*NE punti, scritte PIATTE come float (x,y,z consecutivi). Lo
// structured buffer di float3 in lettura su Metal può disallinearsi (stride 16 vs 12) → posizioni
// spazzatura; il buffer di float (4 byte) è privo di ambiguità — è ciò che il test di parità usa con
// successo.
RWStructuredBuffer<float> _Pout;

// ---- parità (kernel parità) ----
StructuredBuffer<float3>   _Dirs;
RWStructuredBuffer<float>   _Heights;
int _DirCount;

// ---- anteprima GPU dell'editor (render-dai-buffer, NESSUN readback) ----
// Una faccia per dispatch, scritta in una sezione del buffer condiviso dalle 6 facce. La griglia ha un BORDO
// di una cella per lato (lato = _R+2): estende la faccia di una cella oltre [0,1] → le 6 facce si
// SOVRAPPONGONO, coprendo le micro-fessure agli spigoli del cubo. Buffer PIATTI di float (3 per vertice) per la
// stessa ragione di _Pout: lo structured buffer di float3 si disallinea su Metal (stride 16 vs 12).
int _FaceOffset;                 // vertice di partenza di questa faccia nei buffer condivisi
int _R;                          // vertici per lato della faccia (la griglia padded è _R+2)
RWStructuredBuffer<float> _VPos; // posizioni locali (dir*altezza)
RWStructuredBuffer<float> _VNrm; // normali analitiche
RWStructuredBuffer<float> _VDepth; // profondità dell'acqua per vertice (pelo − fondo, m); 0 dove asciutto. Per la resa trasparente del mare
RWStructuredBuffer<float> _VBedNrm; // normale del FONDO sommerso (terreno senza allagamento): dà il rilievo del fondale visto attraverso l'acqua trasparente
RWStructuredBuffer<float> _VSurf; // QUOTA del pelo del mare attivo per vertice (m). La maschera del mare nel fragment confronta length(pos) con questo valore ESATTO invece di RICOSTRUIRE il pelo dal rumore: robusto a qualunque rugosità (la ricostruzione 3-vs-4 ottave sbagliava → "dipinto") e un fbm per-pixel in meno sul mare GPU-bound
RWStructuredBuffer<float> _VField;  // baseN per-vertice (ondulazione di BASE [0,1]): il fragment lo usa per le maschere maria/vette invece di rifare il rumore per-pixel

// ---- LOD in gioco (B1 Tappa 2): una FETTA del pool per NODO del quadtree. Riusa _FaceUp/_AxisA/_AxisB +
// _U0/_V0/_Step (sotto-regione del nodo) + _HasSea. La fetta = griglia interna (_NN×_NN) + anello di SKIRT.
int   _NN;            // vertici per lato del nodo (nodeRes+1)
int   _NSlabOff;      // offset (in vertici) del primo vertice della fetta nel pool condiviso
int   _NSkirtStart;   // offset dello skirt DENTRO la fetta (= _NN*_NN)
float _NSkirtDrop;    // abbassamento radiale dello skirt (nasconde le crepe ai confini di LOD)

// ---- LOD BATCH (un SOLO dispatch per molti nodi): invece di settare gli uniform e fare 1 dispatch PER NODO
// (~centinaia di chiamate API/frame nel churn), i parametri per-nodo stanno in un buffer e si dispatcha una
// volta sola con il NODO sull'asse z (slab) o y (skirt). Tutto float4 per evitare il disallineamento dei float3
// su Metal. uv = (u0,v0,step,slabOff); misc.x = skirtDrop. _NN/_NSkirtStart/ricetta restano uniform globali.
struct NodeJobGPU { float4 faceUp; float4 axisA; float4 axisB; float4 uv; float4 misc; };
StructuredBuffer<NodeJobGPU> _Jobs;

// =================================================================================================
// Noise3D (gradient noise di Perlin + fBm) — fedele a Noise3D.cs
// =================================================================================================

uint UHash(int x, int y, int z, int seed)
{
    // mixing sequenziale identico a Noise3D.UHash. asuint(): reinterpreta i bit (= cast unchecked C#).
    uint h = asuint(seed);
    h = (h + asuint(x)) * 0x9E3779B1u; h ^= h >> 16;
    h = (h + asuint(y)) * 0x85EBCA77u; h ^= h >> 13;
    h = (h + asuint(z)) * 0xC2B2AE3Du; h ^= h >> 16;
    h *= 0x27D4EB2Fu; h ^= h >> 15;
    return h;
}

float3 Grad(int x, int y, int z, int seed)
{
    uint h = UHash(x, y, z, seed);
    return float3(
        (h & 0x3FFu) / 511.5 - 1.0,
        ((h >> 10) & 0x3FFu) / 511.5 - 1.0,
        ((h >> 20) & 0x3FFu) / 511.5 - 1.0);
}

float Quintic(float t) { return t * t * t * (t * (t * 6.0 - 15.0) + 10.0); }

float NoiseValue(float3 p, int seed)
{
    int xi = (int)floor(p.x), yi = (int)floor(p.y), zi = (int)floor(p.z);
    float fx = p.x - xi, fy = p.y - yi, fz = p.z - zi;
    float u = Quintic(fx), v = Quintic(fy), w = Quintic(fz);

    float n000 = dot(Grad(xi,     yi,     zi,     seed), float3(fx,       fy,       fz));
    float n100 = dot(Grad(xi + 1, yi,     zi,     seed), float3(fx - 1.0, fy,       fz));
    float n010 = dot(Grad(xi,     yi + 1, zi,     seed), float3(fx,       fy - 1.0, fz));
    float n110 = dot(Grad(xi + 1, yi + 1, zi,     seed), float3(fx - 1.0, fy - 1.0, fz));
    float n001 = dot(Grad(xi,     yi,     zi + 1, seed), float3(fx,       fy,       fz - 1.0));
    float n101 = dot(Grad(xi + 1, yi,     zi + 1, seed), float3(fx - 1.0, fy,       fz - 1.0));
    float n011 = dot(Grad(xi,     yi + 1, zi + 1, seed), float3(fx,       fy - 1.0, fz - 1.0));
    float n111 = dot(Grad(xi + 1, yi + 1, zi + 1, seed), float3(fx - 1.0, fy - 1.0, fz - 1.0));

    float x00 = lerp(n000, n100, u);
    float x10 = lerp(n010, n110, u);
    float x01 = lerp(n001, n101, u);
    float x11 = lerp(n011, n111, u);
    return lerp(lerp(x00, x10, v), lerp(x01, x11, v), w);
}

float3 RotateDomain(float3 v)
{
    return float3(
         0.00 * v.x + 0.80 * v.y + 0.60 * v.z,
        -0.80 * v.x + 0.36 * v.y - 0.48 * v.z,
        -0.60 * v.x - 0.48 * v.y + 0.64 * v.z);
}

float Fbm(float3 p, int octaves, float lacunarity, float gain, int seed)
{
    float sum = 0.0, amp = 0.5, freq = 1.0, norm = 0.0;
    float3 q = p;
    for (int i = 0; i < octaves; i++)
    {
        sum += amp * NoiseValue(q * freq, seed + i * 1013);
        norm += amp;
        freq *= lacunarity;
        amp *= gain;
        q = RotateDomain(q);
    }
    return saturate(sum / norm * 0.5 + 0.5);
}

// fBm "ridged" (= Noise3D.Ridged): crinali affilati invece di gobbe. [0,1]. Parità col C#.
float Ridged(float3 p, int octaves, float lacunarity, float gain, int seed)
{
    float sum = 0.0, amp = 0.5, freq = 1.0, norm = 0.0;
    float3 q = p;
    for (int i = 0; i < octaves; i++)
    {
        float r = 1.0 - abs(NoiseValue(q * freq, seed + i * 1013));
        sum += amp * r * r;
        norm += amp;
        freq *= lacunarity;
        amp *= gain;
        q = RotateDomain(q);
    }
    return sum / norm;
}

// =================================================================================================
// Campo crateri — fedele a CraterTerrainLayer.cs
// =================================================================================================

static const float CR_SPACING    = 10.0;
static const float CR_OUTER      = 2.2;
static const float CR_JITTER_MAX = 1.4;
static const float CR_SHELL_HALF = 0.6;   // peso radiale sul guscio (modalità flag "Crateri grandi" OFF)
static const float CR_SIMPLE_MAX = 60.0;
static const float CR_COMPLEX_MAX= 160.0;
static const float CR_PEAK_HEIGHT= 0.8;
static const float CR_PEAK_WIDTH = 0.13;

uint CHash(int x, int y, int z, int seed)
{
    uint h = asuint(seed);
    h = (h + asuint(x)) * 0x9E3779B1u; h ^= h >> 16;
    h = (h + asuint(y)) * 0x85EBCA77u; h ^= h >> 13;
    h = (h + asuint(z)) * 0xC2B2AE3Du; h ^= h >> 16;
    h *= 0x27D4EB2Fu; h ^= h >> 15;
    return h;
}

float U01(uint h) { return (h & 0xFFFFFFu) / 16777215.0; }

float Smooth01(float t) { return t * t * (3.0 - 2.0 * t); }

// DISTRIBUZIONE = DRIFT DEL CENTRO (= CraterTerrainLayer). Lo slider muove il centro di ogni cratere dentro
// la sua cella, ognuno nella propria direzione/velocità (dall'hash) → l'insieme si RIDISTRIBUISCE restando
// fatto di cerchi (si muove un punto, non si deforma il campo). PP = onda triangolare (ping-pong) che tiene
// il centro in [0,1] SENZA salti; a distribuzione=0 ridà esattamente la posizione originale (PP(j0)=j0).
float PP(float x)
{
    float r = x - 2.0 * floor(x * 0.5);
    return 1.0 - abs(r - 1.0);
}

float Jitter(uint hBase, uint hSpeed, float dist)
{
    float j0 = U01(hBase);
    float v = j0;
    if (dist > 0.0)
    {
        float spd = 0.5 + U01(hSpeed);      // velocità per-cratere [0.5,1.5]
        v = PP(j0 + dist * spd);
    }
    return v;
}

// peso densità per l'ottava o (0 = grandi … octaves-1 = piccoli), interpolando wLarge→wMedium→wSmall
// (= CraterTerrainLayer.SizeWeight). Una sola ottava = "grandi".
float SizeWeight(int o, int octaves, float wL, float wM, float wS)
{
    float w = wL;
    if (octaves > 1)
    {
        float t = o / (float)(octaves - 1);
        w = (t <= 0.5) ? lerp(wL, wM, t * 2.0) : lerp(wM, wS, (t - 0.5) * 2.0);
    }
    return w;
}

// profilo di un singolo cratere sommato a 'total' (= Accumulate in C#)
void Accumulate(float3 dir, float3 cdir, float radius, float baseRadius,
                float depthRatio, float rimRatio, float rimSharpness, float irregular, float irregScale, int irrSeed, float weight, inout float total)
{
    float distM = baseRadius * length(dir - cdir);
    float r = distM / radius;
    // IRREGOLARITÀ (= CraterTerrainLayer): warp del raggio per-direzione → rim frastagliato/asimmetrico. Solo dominante.
    if (irregular > 0.0)
    {
        float n = Fbm(dir * irregScale, 4, 2.0, 0.5, irrSeed);
        r *= 1.0 + irregular * 0.5 * (n * 2.0 - 1.0);
    }
    if (r >= CR_OUTER) return;

    float depth = radius * depthRatio;
    float rim = depth * rimRatio;
    float cxm = saturate((radius - CR_SIMPLE_MAX) / (CR_COMPLEX_MAX - CR_SIMPLE_MAX));

    float floorR = 0.3 * cxm;
    float cav;
    if (r < floorR) cav = -1.0;
    else if (r < 1.0) { float t = (r - floorR) / (1.0 - floorR); cav = -(1.0 - pow(max(t, 0.0), rimSharpness)); }
    else cav = 0.0;

    // bordo NETTO: dentro (r≤1) raccordo morbido, fuori (r>1) caduta esponenziale stretta → cresta affilata +
    // fianco ripido, non un bulge a cupola (= CraterTerrainLayer).
    float dr = r - 1.0;
    float ring = (dr <= 0.0) ? exp(-(dr * dr) / (0.42 * 0.42)) : exp(-dr / 0.16);

    float peak = 0.0;
    if (cxm > 0.0) { float pr = r / CR_PEAK_WIDTH; peak = cxm * depth * CR_PEAK_HEIGHT * exp(-pr * pr); }

    float off = depth * cav + rim * ring + peak;
    off *= Smooth01(saturate((CR_OUTER - r) / 0.6));
    total += off * weight;
}

// contributo di UNA pipeline di crateri su una direzione (= CraterTerrainLayer.Apply, parte additiva)
float CraterApply(CraterGPU c, float3 unitDir, float baseRadius)
{
    float total = 0.0;
    int seed = (int)c.seed;
    int octaves = max(1, (int)c.octaves);
    float depthRatio = c.depthRatio, rimRatio = c.rimRatio;
    float rimSharp = max(1.0, c.rimSharpness);

    // cratere dominante (piazzato a mano): nel frame del MONDO, NON scivola con la distribuzione
    if (c.hasDominant > 0.5)
        Accumulate(unitDir, c.dominantDir, c.dominantRadius, baseRadius, c.domDepthRatio, c.domRimRatio,
                   max(1.0, c.domRimSharp), c.domIrregular, c.domIrregScale, seed + 4242, 1.0, total);   // profilo+irregolarità propri

    float dist = c.distribution;

    float radius = c.largestRadius;
    for (int o = 0; o < octaves; o++)
    {
        float spacing = radius * CR_SPACING;
        float cellAng = spacing / baseRadius;
        float gscale = 1.0 / cellAng;
        float octDensity = c.density * SizeWeight(o, octaves, c.wLarge, c.wMedium, c.wSmall);

        float3 g = unitDir * gscale;
        int cx = (int)floor(g.x), cy = (int)floor(g.y), cz = (int)floor(g.z);

        for (int dz = -2; dz <= 2; dz++)
        for (int dy = -2; dy <= 2; dy++)
        for (int dx = -2; dx <= 2; dx++)
        {
            int X = cx + dx, Y = cy + dy, Z = cz + dz;
            uint h = CHash(X, Y, Z, seed + o * 9176);
            if (U01(h) > octDensity) continue;

            // centro jitterato + DRIFT della distribuzione (ogni cratere scivola nella sua cella)
            float3 cc = float3(
                (X + Jitter(h * 0x9E3779B1u + 1u, h * 0x9E3779B1u + 11u, dist)) / gscale,
                (Y + Jitter(h * 0x85EBCA77u + 2u, h * 0x85EBCA77u + 12u, dist)) / gscale,
                (Z + Jitter(h * 0xC2B2AE3Du + 3u, h * 0xC2B2AE3Du + 13u, dist)) / gscale);
            float cm = length(cc);
            if (cm < 1e-6) continue;
            float3 cdir = cc / cm;
            // anti-crepa: owned-cell (flag ON, c.bigCraters) o peso radiale sul guscio (flag OFF) — = CraterTerrainLayer
            float weight;
            if (c.bigCraters > 0.5)
            {
                if ((int)floor(cdir.x * gscale) != X || (int)floor(cdir.y * gscale) != Y || (int)floor(cdir.z * gscale) != Z) continue;
                weight = 1.0;
            }
            else
            {
                float radOff = abs(cm - 1.0) / CR_SHELL_HALF;
                if (radOff >= 1.0) continue;
                weight = Smooth01(1.0 - radOff);
            }

            float lo = 2.0 - CR_JITTER_MAX;
            float rad = radius * (lo + (CR_JITTER_MAX - lo) * U01(h * 0x27D4EB2Fu + 4u));
            Accumulate(unitDir, cdir, rad, baseRadius, depthRatio, rimRatio, rimSharp, 0.0, 1.0, 0, weight, total);   // campo: niente irregolarità
        }
        radius *= 0.5;
    }
    return total;
}

// =================================================================================================
// MARE (= SeaTerrainLayer): allaga fino al pelo dell'acqua (eventualmente increspato)
// =================================================================================================

// modella il rumore centrato 'c' secondo 'forma' (= SeaTerrainLayer.Shape)
float SeaShape(float c, float forma)
{
    float ridged = 1.0 - 2.0 * abs(c);
    float billow = 2.0 * abs(c) - 1.0;
    return (forma < 0.0) ? lerp(ridged, c, forma + 1.0) : lerp(c, billow, forma);
}

// quota del pelo del mare nella direzione data (= SeaTerrainLayer.Surface)
float SeaSurface(SeaGPU s, float3 unitDir)
{
    float v = s.seaRadius;
    if (s.roughness > 0.0)
    {
        float c = (Fbm(unitDir * s.roughScale, 4, 2.0, 0.5, (int)s.seed) - 0.5) * 2.0;
        v = s.seaRadius + SeaShape(c, s.forma) * s.roughness;
    }
    return v;
}

// =================================================================================================
// TETTONICA (= TectonicTerrainLayer): soft-Voronoi (quota continua) + catene/rift ai confini
// =================================================================================================

float TectonicApply(TectonicGPU t, float3 unitDir)
{
    int seed = (int)t.seed;

    // domain-warp frattale (due scale) → coste frastagliate (= TectonicTerrainLayer.Apply). Ampiezza scalata con
    // la dimensione placca (√(8/n)) per non ripiegare le celle piccole (→ "bolle"/lobi ai confini). Parità col C#.
    float3 d = unitDir;
    if (t.warp > 0.0)
    {
        float warpAmp = t.warp * min(1.5, sqrt(8.0 / t.plateCount));
        float3 w1 = float3(NoiseValue(unitDir * 3.0, seed + 101), NoiseValue(unitDir * 3.0, seed + 202), NoiseValue(unitDir * 3.0, seed + 303));
        float3 w2 = float3(NoiseValue(unitDir * 7.3, seed + 404), NoiseValue(unitDir * 7.3, seed + 505), NoiseValue(unitDir * 7.3, seed + 606));
        d = normalize(unitDir + (w1 * 0.7 + w2 * 0.35) * warpAmp);
    }

    float sharp = lerp(40.0, 5.0, t.coastSlope);
    int off = (int)t.plateOffset;
    int n = (int)t.plateCount;

    // SOFT VORONOI: media pesata di TUTTE le placche (peso che sfuma con la distanza angolare) → continua
    float wsum = 0.0, esum = 0.0;
    int i1 = off, i2 = off; float best = -2.0, second = -2.0, third = -2.0;
    for (int i = 0; i < n; i++)
    {
        PlateGPU pl = _Plates[off + i];
        float dt = dot(d, pl.seedDir.xyz);
        float w = exp(sharp * (dt - 1.0));
        float bi = ((pl.seedDir.w > 0.5) ? t.contrast * 0.5 : -t.contrast * 0.5) + pl.motion.w;  // motion.w = elevJitter
        wsum += w; esum += w * bi;
        if (dt > best) { third = second; second = best; i2 = i1; best = dt; i1 = off + i; }
        else if (dt > second) { third = second; second = dt; i2 = off + i; }
        else if (dt > third) { third = dt; }
    }
    float elev = esum / wsum;

    // rilievo INTERNO dei continenti (= TectonicTerrainLayer): fBm pesato sulla continentalità (oceani lisci).
    // Scale e seed IDENTICI al C# per la parità.
    // PERF: oceani (contW≈0) e pianure (mtn≈0) saltano il rumore extra; soglie identiche al C# → parità.
    if (t.continentalRelief > 0.0)
    {
        float contW = saturate((elev + t.contrast * 0.5) / max(t.contrast, 1e-3));
        if (contW > 0.01)
        {
            float mtn = saturate(Fbm(unitDir * 1.4, 3, 2.0, 0.5, seed + 831) * 1.7 - 0.35);
            if (mtn > 0.001)
            {
                float ridge = Ridged(unitDir * 3.0, 4, 2.0, 0.5, seed + 821);
                elev += t.continentalRelief * contW * mtn * (ridge - 0.30) * 1.8;
            }
        }
    }

    // confini: catene (convergenti) / rift (divergenti) sulla fascia del confine i1↔i2, profilo MODULATO
    // (along = quota variabile lungo il confine; rough = cresta frastagliata) — identico al C#.
    float tu = saturate((best - second) / t.boundaryWidth);
    float boundary = 1.0 - tu * tu * (3.0 - 2.0 * tu);
    // GATE DI CONTINUITÀ (fix crepe): annulla il ridge dove la 2ª e la 3ª placca sono equidistanti (lì i2
    // salterebbe → gradino di quota = crepa). 0 su quelle linee → quota continua. Identico al C#.
    float sd = saturate((second - third) / t.boundaryWidth);
    float gate = sd * sd * (3.0 - 2.0 * sd);
    if (boundary * gate > 0.001 && t.uplift > 0.0)
    {
        float3 bn = _Plates[i1].seedDir.xyz - _Plates[i2].seedDir.xyz;
        float bm = length(bn);
        if (bm > 1e-5)
        {
            bn /= bm;
            float conv = clamp(dot(_Plates[i2].motion.xyz - _Plates[i1].motion.xyz, bn), -1.0, 1.0);
            float along = Fbm(d * 1.8, 3, 2.0, 0.5, seed + 711);
            float rough = 1.0 - abs(NoiseValue(d * 9.0, seed + 712));
            // D: rift (conv<0) meno profondi (×0.35) e meno affilati (meno rough) delle catene — identico al C#.
            bool rift = conv < 0.0;
            float convEff = rift ? conv * t.riftBalance : conv;
            float rg = rift ? (0.70 + 0.30 * rough) : (0.45 + 0.55 * rough);
            float profile = boundary * gate * (0.30 + 0.70 * along) * rg;
            elev += t.uplift * profile * convEff;
        }
    }
    return elev;
}

// =================================================================================================
// SampleHeight completo = base + PIPELINE ORDINATA di processi (= PlanetTerrain.SampleHeight)
// =================================================================================================

// Versione con la PROFONDITÀ dell'acqua: per ogni processo "Mare" registra (pelo − fondo) prima
// dell'allagamento → quanto è spessa l'acqua in quel punto. La usa la resa trasparente del mare (il fondo
// non è geometria: la superficie disegnata È il pelo, quindi la profondità va portata fuori esplicitamente).
// Un cratere scavato DOPO il mare riabbassa h sotto il pelo, ma waterDepth resta quella del mare: lì la
// maschera del mare nel fragment è 0 (h lontano dal pelo) → asciutto, la profondità non viene usata.
// seaSurf (out) = quota del pelo dell'ULTIMO mare applicato (= quello che ApplyColor passa al fragment via
// Recipe.LastSea). 0 se la ricetta non ha mari (lì _SeaOn=0 nel fragment → la maschera è comunque 0).
float SampleHeightD(float3 unitDir, out float waterDepth, out float seaSurf)
{
    float n = Fbm(unitDir * _Frequency, _Octaves, _Lacunarity, _Gain, _Seed);  // [0,1]
    float h = _BaseRadius + (n - 0.5) * 2.0 * _Amplitude;
    waterDepth = 0.0;
    seaSurf = 0.0;

    // processi nell'ORDINE della ricetta (un cratere dopo un mare scava all'asciutto)
    for (int pi = 0; pi < _ProcessCount; pi++)
    {
        float2 pr = _Process[pi];
        int type = (int)pr.x;
        int idx = (int)pr.y;
        if (type == 0)      h += CraterApply(_Craters[idx], unitDir, _BaseRadius);
        else if (type == 1) { float s = SeaSurface(_Seas[idx], unitDir); seaSurf = s; waterDepth = max(0.0, s - h); h = max(h, s); }
        else                h += TectonicApply(_Tectonics[idx], unitDir);
    }
    // guardia alla FONTE (deve combaciare col C# PlanetTerrain.SampleHeight per la parità walker):
    //  - NaN/Inf/assurdo-alto → raggio base ("h < 3·base" è FALSO per NaN/Inf → base).
    //  - h ≤ minimo → fondo-CIOTOLA positivo: un cratere più profondo del raggio (es. scavato DOPO il mare) darebbe
    //    h≤0 = geometria degenere/auto-intersecante → schiacciarlo sul raggio base faceva il "disco piatto + schegge"
    //    nel cratere dominante. Clampandolo a 0.2·base resta una conca profonda, niente degenere. No-op su h validi.
    h = (h < 3.0 * _BaseRadius) ? h : _BaseRadius;
    return max(h, _BaseRadius * 0.2);
}

float SampleHeight(float3 unitDir) { float wd, ss; return SampleHeightD(unitDir, wd, ss); }

// Quota del FONDO: la pipeline completa MA senza allagamento (il Mare non alza la quota). Crateri/tettonica
// prima e dopo il mare contano (un cratere sommerso fa parte del fondale) → è il rilievo del terreno sotto
// l'acqua. La sua normale analitica dà il rilievo del fondale visto in trasparenza.
float BedHeight(float3 unitDir)
{
    float n = Fbm(unitDir * _Frequency, _Octaves, _Lacunarity, _Gain, _Seed);
    float h = _BaseRadius + (n - 0.5) * 2.0 * _Amplitude;
    for (int pi = 0; pi < _ProcessCount; pi++)
    {
        float2 pr = _Process[pi];
        int type = (int)pr.x;
        int idx = (int)pr.y;
        if (type == 0)      h += CraterApply(_Craters[idx], unitDir, _BaseRadius);
        else if (type == 2) h += TectonicApply(_Tectonics[idx], unitDir);
        // type 1 (mare): non allaga → resta il fondo
    }
    return h;
}

// (tx,ty)∈[0,1]² della faccia → direzione unitaria. Identico a PlanetMeshBuilder.ParamToDir.
float3 ParamToDir(float tx, float ty)
{
    float3 pointOnCube = _FaceUp + (tx - 0.5) * 2.0 * _AxisA + (ty - 0.5) * 2.0 * _AxisB;
    return normalize(pointOnCube);
}

// Variante con gli assi della faccia ESPLICITI (per i kernel batch, che leggono gli assi dal job invece che dagli
// uniform globali). Stessa formula → parità per costruzione con ParamToDir quando gli assi coincidono.
float3 ParamToDirA(float tx, float ty, float3 up, float3 a, float3 b)
{
    return normalize(up + (tx - 0.5) * 2.0 * a + (ty - 0.5) * 2.0 * b);
}

// =================================================================================================
// KERNEL
// =================================================================================================



// ---- anteprima GPU: posizioni di una faccia (griglia padded _R+2) -------------------------------

// ---- anteprima GPU: normali ANALITICHE (= PlanetTerrain.SurfaceNormal) --------------------------
// Pendenza del terreno da 4 campioni di SampleHeight su assi tangenti: dipende solo dalla direzione →
// CONTINUA fra le facce (niente cucitura di shading). Centro su direzione SNAPPATA (identica fra facce
// adiacenti allo spigolo). eps ≈ UNA cella (l'arco per cella è (π/2)/(_R-1)) → stesso passo della
// differenza centrale geometrica: nitido come quella, senza il mismatch di un eps troppo fine/grosso.

// ---- anteprima GPU: riempie l'INDEX BUFFER sulla GPU ---------------------------------------------
// La topologia (6 triangoli per quad, su 6 facce di griglia padded) dipende solo dalla risoluzione. A
// 2048 l'array è ~150M indici (~600 MB): costruirlo su CPU e caricarlo faceva uno scatto sul main thread.
// Qui ogni thread scrive i 6 indici del suo quad → niente array gestito, niente upload, niente readback.
RWStructuredBuffer<uint> _Indices;
uint _IdxGp;             // lato della griglia padded (res+2)
uint _IdxVertsPerFace;   // gp*gp
uint _IdxQuadsPerFace;   // (gp-1)*(gp-1)
uint _IdxQuadsTotal;     // quadsPerFace*6 (bound del dispatch)
uint _IdxThreadW;        // larghezza in thread della griglia di dispatch (per linearizzare id 2D)

// Dispatch 2D: a 2048 i quad sono ~25M → i gruppi su un solo asse supererebbero il limite di 65535. Si spalma
// su (x,y) e si linearizza con _IdxThreadW (= gruppi_x · 64). Il bound _IdxQuadsTotal scarta i thread in eccesso.
// Tutto in uint: gli indici sono non negativi e su Metal le divisioni/moduli uint sono più veloci degli int.

// ---- LOD: posizione di superficie a (tx,ty) della faccia corrente (dir * altezza) -------------------
float3 NPosAt(float tx, float ty)
{
    float3 d = ParamToDir(tx, ty);
    return d * SampleHeight(d);
}

// ---- LOD: griglia INTERNA di un nodo nella sua fetta del pool (pos + normale analitica + profondità) ----

// ---- LOD: anello di SKIRT del nodo (bordo abbassato radialmente) → nasconde le crepe fra LOD diversi ----

#endif

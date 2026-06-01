#ifndef WANDERER_PLANET_NOISE
#define WANDERER_PLANET_NOISE

// Funzioni di rumore condivise tra lo shader di superficie e quello di bake: UNA sola
// fonte di verità, così il rilievo bakeato e quello (eventuale) calcolato a runtime non
// possono divergere. Tutte pure (nessuna uniform): i parametri arrivano come argomenti.
// Richiede #pragma target 4.0 (uint per l'hash PCG).

// --- hash intero (PCG 3D): stabile a qualunque coordinata, anche negativa ---
uint pcg3d(uint3 v)
{
    v = v * 1664525u + 1013904223u;
    v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
    v ^= v >> 16u;
    v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
    return v.x;
}

// gradiente casuale in [-1,1]^3 per il punto di reticolo dato
float3 hashGrad(float3 lp)
{
    uint3 up = (uint3)((int3)lp + 4194304);
    uint h = pcg3d(up);
    float3 g;
    g.x = float(h & 0x3ffu)         * (2.0 / 1023.0) - 1.0;
    g.y = float((h >> 10) & 0x3ffu) * (2.0 / 1023.0) - 1.0;
    g.z = float((h >> 20) & 0x3ffu) * (2.0 / 1023.0) - 1.0;
    return g;
}

// Gradient noise 3D con derivate analitiche (iq). Ritorna float4(valore, d/dx, d/dy, d/dz).
float4 noised(float3 x)
{
    float3 p = floor(x);
    float3 w = frac(x);
    float3 u = w * w * w * (w * (w * 6.0 - 15.0) + 10.0);
    float3 du = 30.0 * w * w * (w * (w - 2.0) + 1.0);

    float3 ga = hashGrad(p + float3(0,0,0));
    float3 gb = hashGrad(p + float3(1,0,0));
    float3 gc = hashGrad(p + float3(0,1,0));
    float3 gd = hashGrad(p + float3(1,1,0));
    float3 ge = hashGrad(p + float3(0,0,1));
    float3 gf = hashGrad(p + float3(1,0,1));
    float3 gg = hashGrad(p + float3(0,1,1));
    float3 gh = hashGrad(p + float3(1,1,1));

    float va = dot(ga, w - float3(0,0,0));
    float vb = dot(gb, w - float3(1,0,0));
    float vc = dot(gc, w - float3(0,1,0));
    float vd = dot(gd, w - float3(1,1,0));
    float ve = dot(ge, w - float3(0,0,1));
    float vf = dot(gf, w - float3(1,0,1));
    float vg = dot(gg, w - float3(0,1,1));
    float vh = dot(gh, w - float3(1,1,1));

    float v = va
        + u.x * (vb - va)
        + u.y * (vc - va)
        + u.z * (ve - va)
        + u.x * u.y * (va - vb - vc + vd)
        + u.y * u.z * (va - vc - ve + vg)
        + u.z * u.x * (va - vb - ve + vf)
        + u.x * u.y * u.z * (-va + vb + vc - vd + ve - vf - vg + vh);

    float3 d = ga
        + u.x * (gb - ga)
        + u.y * (gc - ga)
        + u.z * (ge - ga)
        + u.x * u.y * (ga - gb - gc + gd)
        + u.y * u.z * (ga - gc - ge + gg)
        + u.z * u.x * (ga - gb - ge + gf)
        + u.x * u.y * u.z * (-ga + gb + gc - gd + ge - gf - gg + gh)
        + du * float3(
            vb - va + u.y * (va - vb - vc + vd) + u.z * (va - vb - ve + vf) + u.y * u.z * (-va + vb + vc - vd + ve - vf - vg + vh),
            vc - va + u.z * (va - vc - ve + vg) + u.x * (va - vb - vc + vd) + u.z * u.x * (-va + vb + vc - vd + ve - vf - vg + vh),
            ve - va + u.x * (va - vb - ve + vf) + u.y * (va - vc - ve + vg) + u.x * u.y * (-va + vb + vc - vd + ve - vf - vg + vh));

    return float4(v, d);
}

// --- value noise ECONOMICO per le maschere colore: serve solo il valore, non il gradiente ---
float vhash(float3 lp)
{
    return float(pcg3d((uint3)((int3)lp + 4194304)) & 0xffffu) / 65535.0;
}

float vnoise(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(vhash(i + float3(0,0,0)), vhash(i + float3(1,0,0)), f.x),
                     lerp(vhash(i + float3(0,1,0)), vhash(i + float3(1,1,0)), f.x), f.y),
                lerp(lerp(vhash(i + float3(0,0,1)), vhash(i + float3(1,0,1)), f.x),
                     lerp(vhash(i + float3(0,1,1)), vhash(i + float3(1,1,1)), f.x), f.y), f.z);
}

// 2 ottave, ~[0,1]: sufficiente per le macchie di colore
float fbm(float3 p)
{
    return 0.6 * vnoise(p) + 0.4 * vnoise(p * 2.03 + 11.7);
}

// fBm di Perlin per il rilievo: valore + gradiente analitico (rispetto a p).
// 'oct' frazionario: l'ultima ottava entra in dissolvenza (continuità del LOD).
float4 fbmRelief(float3 p, float oct)
{
    float v = 0.0;
    float3 d = float3(0,0,0);
    float a = 0.5;
    float freq = 1.0;
    float3 off = float3(0,0,0);
    for (int i = 0; i < 6; i++)
    {
        float w = saturate(oct - (float)i);
        if (w <= 0.0) break;   // ottave oltre 'oct' non contribuiscono: salta
        float4 nd = noised(p * freq + off);
        v += a * w * nd.x;
        d += a * w * freq * nd.yzw;
        freq *= 2.02; a *= 0.5; off += 19.3;
    }
    return float4(v, d);
}

// fBm di Perlin che calcola SOLO le ottave [startOct, 6), con la stessa progressione di
// ampiezza/frequenza/offset di fbmRelief — così la somma (bakeato 0..startOct) + (questo
// startOct..6) ricostruisce ESATTAMENTE il rilievo a 6 ottave, senza giunte.
//
// È il cuore dell'ibrido: le ottave grosse (0..startOct) stanno in texture (lette una volta,
// fredde), le ottave fini (startOct..6) si calcolano qui per pixel ma sfumano con la distanza
// via 'lod' → crisp da vicino, costo zero da lontano. 'startOct' va passato come LITERALE
// costante perché il loop si srotoli (es. fbmReliefRange(p, 3, lod)).
float4 fbmReliefRange(float3 p, int startOct, float lod)
{
    float v = 0.0;
    float3 d = float3(0,0,0);
    float a = 0.5;
    float freq = 1.0;
    float3 off = float3(0,0,0);
    // avanza fino a startOct senza accumulare (stessa progressione di fbmRelief)
    [unroll] for (int s = 0; s < 6; s++) { if (s < startOct) { freq *= 2.02; a *= 0.5; off += 19.3; } }
    [unroll] for (int i = 0; i < 6; i++)
    {
        int gi = startOct + i;
        float w = (gi < 6) ? saturate(lod - (float)gi) : 0.0;
        if (w > 0.0)   // su pixel lontani le ottave fini hanno w=0 → noised() saltata
        {
            float4 nd = noised(p * freq + off);
            v += a * w * nd.x;
            d += a * w * freq * nd.yzw;
        }
        freq *= 2.02; a *= 0.5; off += 19.3;
    }
    return float4(v, d);
}

#endif

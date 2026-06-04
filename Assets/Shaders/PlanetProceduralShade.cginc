#ifndef WANDERER_PLANET_PROCEDURAL_SHADE
#define WANDERER_PLANET_PROCEDURAL_SHADE

// Colore PROCEDURALE della superficie dei pianeti, calcolato nel fragment dai parametri della RICETTA
// (niente texture bakate). È l'UNICA copia di questa catena: la condividono l'anteprima dell'editor
// (Wanderer/PlanetProcedural) e la resa in gioco (Wanderer/PlanetSurfaceGPU), così non possono divergere.
// Mirroring della catena di Wanderer/PlanetBaked: suolo+macro, minerali, vette, bacini, MARE+saturazione,
// con fbm/n3_fbm fedeli a Noise3D → il mare segue la geometria allagata.
//
// Va incluso DOPO UnityCG.cginc (usa _WorldSpaceCameraPos).

#include "PlanetNoise.cginc"

float _BaseRadius, _Amplitude, _Frequency, _Lacunarity, _Gain;
int _Octaves, _Seed;
float4 _SoilMean, _SoilTint, _MineralA, _MineralB, _PeakColor, _MariaColor, _SeaColor;
float _MacroVar, _MacroScale, _MineralStr, _MineralScale, _PeakStr, _MariaScale, _MariaStr;
float _SeaOn, _SeaLevel, _SeaSat, _SeaRough, _SeaRoughScale, _SeaForma, _SeaSeed, _SeaLiquid, _Saturation;
float _SeaClear, _SeaClarity;
float3 _SunDir, _SunColor, _Ambient;
// TORCIA del giocatore (spot): luce manuale in gioco. _TorchColor = 0 quando spenta o assente (es. editor) →
// termine nullo, nessun costo visivo. Posizione/direzione in spazio MONDO (la lampada è figlia della camera).
float3 _TorchPos, _TorchDir, _TorchColor;
float _TorchRange, _TorchCosInner, _TorchCosOuter;
// 1 = baseN arriva GIÀ PRONTO per-vertice (gioco: il fragment non rifà il rumore gradiente per ogni pixel).
// 0 = calcolalo qui per-pixel (editor: massima qualità, non è perf-critico). Default 0 se non impostato.
float _PerVertexFields;

// modella il rumore centrato 'c' secondo 'forma' (= SeaTerrainLayer.Shape / PlanetBaked.SeaShape)
float SeaShape(float c, float forma)
{
    float ridged = 1.0 - 2.0 * abs(c);
    float billow = 2.0 * abs(c) - 1.0;
    return (forma < 0.0) ? lerp(ridged, c, forma + 1.0) : lerp(c, billow, forma);
}

// Pobj   = posizione in spazio OGGETTO (pianeta centrato all'origine): i campi di colore sono radiali da qui.
// worldP = posizione in spazio MONDO: serve solo al vettore vista del glint del mare.
// nrmW   = normale del pelo in spazio MONDO (per la luce). bnrmW = normale del fondo sommerso (mondo).
// depth  = profondità dell'acqua al vertice (pelo − fondo), interpolata; 0 dove asciutto.
// baseNField = ondulazione di base [0,1] PRE-CALCOLATA per-vertice (usata solo se _PerVertexFields>0.5).
// seaSurfField = QUOTA del pelo del mare attivo, PRE-CALCOLATA per-vertice dal compute (= SeaSurface esatta).
float3 PlanetShade(float3 Pobj, float3 worldP, float3 nrmW, float3 bnrmW, float depth, float baseNField, float seaSurfField)
{
    float3 P = Pobj;
    float h = max(length(P), 1e-4);
    float3 sdir = P / h;        // direzione radiale (spazio oggetto) per i campi di colore

    // MARE: il pelo arriva ESATTO per-vertice dal compute (= SeaSurface della geometria allagata). Tingo dove la
    // quota h coincide col pelo; un cratere scavato DOPO il mare abbassa h sotto il pelo → asciutto, niente acqua.
    // Niente più ricostruzione del rumore nel fragment: la 3-vs-4 ottave sbagliava ad alta rugosità (acqua "dipinta")
    // e costava un fbm per-pixel sul mare GPU-bound. La banda 2..4 m dà il bordo (pelo netto + battigia anti-alias).
    float seaSurf = seaSurfField;
    float seaMask = (_SeaOn > 0.5) ? (1.0 - smoothstep(2.0, 4.0, abs(h - seaSurf))) : 0.0;

    // suolo: colore base × variazione MACRO a bassa frequenza (campo dunale) — procedurale, niente texture
    float macroV = fbm(sdir * _MacroScale);
    float3 alb = _SoilMean.rgb * lerp(1.0, 0.78 + macroV * 0.44, _MacroVar) * _SoilTint.rgb;

    // regioni minerali: velatura di TINTA larga (calda/fredda), bassa frequenza
    float zz = fbm(sdir * _MineralScale);
    float3 mineral = float3(1.0, 1.0, 1.0);
    mineral = lerp(mineral, _MineralB.rgb, smoothstep(0.45, 0.28, zz));
    mineral = lerp(mineral, _MineralA.rgb, smoothstep(0.55, 0.72, zz));
    alb *= lerp(float3(1.0, 1.0, 1.0), mineral, _MineralStr);

    // cappucci/maria seguono l'ondulazione di BASE (dune/bacini), NON i crateri: altrimenti ogni
    // cratere innescherebbe vette/maria → grandi blob. Ricostruisco la quota base nel fragment.
    // 2 ottave: serve solo il TREND a bassa frequenza per le maschere maria/vette. In GIOCO arriva già pronto
    // per-vertice (interpolato): il fragment NON rifà questo rumore gradiente per ogni pixel (era il costo maggiore).
    // Nell'editor (_PerVertexFields=0) lo calcola qui, per-pixel, a piena qualità. Il valore è identico (Fbm≡n3_fbm).
    float baseN = (_PerVertexFields > 0.5) ? baseNField : n3_fbm(sdir * _Frequency, 2, _Lacunarity, _Gain, _Seed);
    float baseH = _BaseRadius + (baseN - 0.5) * 2.0 * _Amplitude;
    float t = saturate((baseH - (_BaseRadius - _Amplitude)) / (2.0 * _Amplitude));
    alb = lerp(alb, _PeakColor.rgb, smoothstep(0.74, 0.97, t) * _PeakStr);

    // bacini scuri (dove è basso) in ALCUNE regioni → il colore segue il rilievo
    float low = 1.0 - smoothstep(0.16, 0.46, t);
    float region = smoothstep(0.42, 0.64, fbm(sdir * _MariaScale));
    alb = lerp(alb, alb * _MariaColor.rgb, low * region * _MariaStr);

    // MARE. Tinta dell'acqua (saturazione propria). Se TRASPARENTE l'acqua bassa lascia vedere il fondo
    // (l'albedo del suolo, tinto d'acqua) e si scurisce verso il colore profondo con la profondità
    // (Beer-Lambert: exp(−depth/limpidezza)); profonda → opaca. La profondità arriva per-vertice dal compute.
    float seaLuma = dot(_SeaColor.rgb, float3(0.2126, 0.7152, 0.0722));
    float3 seaCol = lerp(float3(seaLuma, seaLuma, seaLuma), _SeaColor.rgb, _SeaSat);
    float3 waterAlb = seaCol;
    float seaTrans = 0.0;   // quanto si vede il fondo (1 = secca, 0 = fondale opaco); 0 se non trasparente
    if (_SeaClear > 0.5 && _SeaLiquid > 0.5)
    {
        seaTrans = exp(-max(depth, 0.0) / max(_SeaClarity, 0.05));
        float3 waterTint = _SeaColor.rgb / max(seaLuma, 0.04);             // tinta a luminosità neutra (non scurisce)
        float3 bedSeen = alb * lerp(float3(1.0, 1.0, 1.0), waterTint, 0.6);
        waterAlb = lerp(seaCol, bedSeen, seaTrans);
    }
    alb = lerp(alb, waterAlb, seaMask);

    // saturazione finale
    float luma = dot(alb, float3(0.2126, 0.7152, 0.0722));
    alb = lerp(float3(luma, luma, luma), alb, _Saturation);

    // luce: normale geometrica del vertice (il pelo). Sul mare NON va appiattita: con la rugosità il pelo
    // È geometria ondulata e la normale la cattura (come PlanetBaked).
    float3 nrm = normalize(nrmW);
    // RILIEVO DEL FONDALE: dove guardo il fondo in trasparenza (seaTrans·seaMask) illumino con la normale
    // del FONDO sommerso; acqua profonda/terra → torna alla normale del pelo.
    float3 shadeN = nrm;
    if (seaTrans > 0.0)
        shadeN = normalize(lerp(nrm, normalize(bnrmW), seaTrans * seaMask));
    float ndl = saturate(dot(shadeN, _SunDir));
    float3 col = alb * (ndl * _SunColor + _Ambient);

    // TORCIA: spot manuale (diffuso). Attenuazione con la distanza + cono morbido. Spenta/assente → _TorchColor=0.
    float3 toL = _TorchPos - worldP;
    float dL = length(toL);
    float3 L = toL / max(dL, 1e-4);
    float att = saturate(1.0 - dL / max(_TorchRange, 1.0)); att *= att;
    float cone = smoothstep(_TorchCosOuter, _TorchCosInner, dot(-L, _TorchDir));
    float ndlT = saturate(dot(nrm, L));
    col += alb * (_TorchColor * (ndlT * att * cone));

    // MARE LIQUIDO: aspetto d'ACQUA. Glint speculare del sole + schiarita di Fresnel ai bordi radenti.
    if (_SeaLiquid > 0.5 && seaMask > 0.0)
    {
        float3 V = normalize(_WorldSpaceCameraPos - worldP);
        float3 H = normalize(_SunDir + V);
        // larghezza del riflesso = quanto è mossa l'acqua: liscia → glint quasi puntiforme; mossa → scia larga
        float gloss = lerp(2200.0, 90.0, saturate(_SeaRough / 12.0));
        float spec = pow(saturate(dot(nrm, H)), gloss);
        float fres = pow(1.0 - saturate(dot(nrm, V)), 5.0) * ndl;
        col += (_SunColor * spec * 1.1 + _SeaColor.rgb * fres * 0.3) * seaMask;
    }
    return col;
}

#endif

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

// INCRESPATURA dell'acqua: perturba la normale del pelo con due ottave di rumore SCORREVOLE (gradiente
// ANALITICO da noised → niente differenze finite). È ciò che dà identità di SUPERFICIE all'acqua: spezza il
// glint del sole in scintille e toglie il look "dipinto" (un pelo perfettamente liscio riflette il sole come
// un'unica macchia enorme). Tutto in spazio MONDO (worldP/Nw coerenti): sotto la floating origin il dominio
// può "saltare" a un ri-ancoraggio, ma è impercettibile perché l'acqua è già in animazione. ampiezza/scala/
// velocità costanti (acqua stilizzata, GPU-first; si potranno esporre come manopole della ricetta).
float3 WaterRippleNormal(float3 worldP, float3 Nw, float strength)
{
    if (strength <= 0.0) return Nw;
    float tt = _Time.y;   // secondi
    float4 n1 = noised(worldP * 0.35 + float3(0.6, 0.0, 0.37) * tt);   // onda fine, scorre in una direzione
    float4 n2 = noised(worldP * 0.13 + float3(-0.27, 0.0, 0.5) * tt);  // onda larga, scorre nell'altra
    float3 grad = n1.yzw * 0.6 + n2.yzw * 1.0;          // gradiente combinato (mondo)
    float3 slope = grad - dot(grad, Nw) * Nw;           // componente TANGENTE (la pendenza del pelo)
    return normalize(Nw - slope * strength);
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
    // banda STRETTA attorno al pelo: il blu finisce netto al bordo, la rampa di mesh sopra il pelo (transizione
    // acqua→terra del heightfield allagato) resta spiaggia invece di colorarsi d'acqua → l'acqua non "si arrampica"
    // sui corpi che affiorano. (Il pelo geometricamente È piatto dove allagato; la rampa è inevitabile a mesh
    // singola — la cura definitiva del tutto-piatto sarebbe un guscio d'acqua separato. Stringere basta per ora.)
    float seaSurf = seaSurfField;
    float seaMask = (_SeaOn > 0.5) ? (1.0 - smoothstep(0.15, 0.75, abs(h - seaSurf))) : 0.0;

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

    // MARE SOLIDO (non liquido): è SUOLO di colore diverso — antiche colate laviche (maria), ghiaccio piatto…
    // → tinta piatta del _SeaColor nell'albedo, niente trattamento d'acqua. (L'acqua LIQUIDA è una superficie a
    // sé, sotto la luce.) Così un mare di maria resta roccia colorata, non diventa acquamarina.
    if (_SeaOn > 0.5 && _SeaLiquid < 0.5)
    {
        float seaLuma = dot(_SeaColor.rgb, float3(0.2126, 0.7152, 0.0722));
        float3 seaCol = lerp(float3(seaLuma, seaLuma, seaLuma), _SeaColor.rgb, _SeaSat);
        alb = lerp(alb, seaCol, seaMask);
    }

    // saturazione finale del SUOLO (l'acqua liquida ha la sua, sotto)
    float luma = dot(alb, float3(0.2126, 0.7152, 0.0722));
    alb = lerp(float3(luma, luma, luma), alb, _Saturation);

    // ===== LUCE DEL SUOLO (terra asciutta) =====
    float3 nrm = normalize(nrmW);
    float ndlLand = saturate(dot(nrm, _SunDir));
    float3 col = alb * (ndlLand * _SunColor + _Ambient);

    // TORCIA: spot manuale (diffuso). Attenuazione con la distanza + cono morbido. Spenta/assente → _TorchColor=0.
    float3 toL = _TorchPos - worldP;
    float dL = length(toL);
    float3 L = toL / max(dL, 1e-4);
    float att = saturate(1.0 - dL / max(_TorchRange, 1.0)); att *= att;
    float cone = smoothstep(_TorchCosOuter, _TorchCosInner, dot(-L, _TorchDir));
    float ndlT = saturate(dot(nrm, L));
    col += alb * (_TorchColor * (ndlT * att * cone));

    // ===== ACQUA LIQUIDA (solo se _SeaLiquid) =====
    // Resa come SUPERFICIE, non come tinta sul terreno. Tre cue che la fanno leggere come acqua:
    //  1) COLORE per PROFONDITÀ: bassofondo turchese chiaro → profondo blu scuro (indipendente dal fondo).
    //  2) INCRESPATURA animata (WaterRippleNormal): spezza il glint e dà microstruttura → niente "macchia" enorme.
    //  3) RIFLESSO: glint stretto del sole + Fresnel verso un cielo chiaro ai bordi radenti.
    // TRASPARENZA: solo in acqua BASSA (Beer-Lambert sulla profondità) si vede il fondo, illuminato dalla sua
    // normale (rilievo del fondale). L'acqua PROFONDA è opaca e piatta → non segue più i dossi sommersi.
    if (seaMask > 0.0 && _SeaLiquid > 0.5)
    {
        float seaLuma = dot(_SeaColor.rgb, float3(0.2126, 0.7152, 0.0722));
        float3 deepCol = _SeaColor.rgb * 0.52;
        float3 shallowCol = lerp(_SeaColor.rgb, float3(0.42, 0.78, 0.85), 0.55);   // turchese di bassofondo
        float dN = saturate(depth / 36.0);                                        // gradiente più dolce → meno "muro" opaco
        float3 waterCol = lerp(shallowCol, deepCol, dN);
        waterCol = lerp(float3(seaLuma, seaLuma, seaLuma), waterCol, _SeaSat);     // saturazione propria del mare

        // trasparenza: il fondo (alb del suolo, tinto d'acqua) emerge in acqua bassa. pow(.,0.75) alza la
        // trasparenza per le profondità medie → acqua più limpida. La PROFONDITÀ-soglia resta _SeaClarity (ricetta).
        float seaTrans = 0.0;
        if (_SeaClear > 0.5)
        {
            seaTrans = pow(exp(-max(depth, 0.0) / max(_SeaClarity, 0.05)), 0.75);
            float3 waterTint = _SeaColor.rgb / max(seaLuma, 0.04);                 // tinta a luminosità neutra
            float3 bedSeen = alb * lerp(float3(1.0, 1.0, 1.0), waterTint, 0.7);
            waterCol = lerp(waterCol, bedSeen, seaTrans);
        }

        // normale: il pelo resta SEMPRE acqua increspata (è la superficie!). Il fondo si vede come COLORE, non
        // sostituendo la normale → un filo di rilievo del fondale (peso BASSO), non una lastra vetrosa liscia
        // alla riva (era l'"effetto strano sulle coste": la normale-fondo liscia spegneva le increspature).
        float3 rN = WaterRippleNormal(worldP, nrm, 0.22);
        float3 shadeN = (seaTrans > 0.0) ? normalize(lerp(rN, normalize(bnrmW), seaTrans * 0.30)) : rN;
        float ndl = saturate(dot(shadeN, _SunDir));
        float3 wcol = waterCol * (ndl * _SunColor + _Ambient);

        // riflesso d'acqua: glint del sole spezzato dalle increspature + Fresnel verso un cielo chiaro ai bordi
        float3 V = normalize(_WorldSpaceCameraPos - worldP);
        float3 H = normalize(_SunDir + V);
        float spec = pow(saturate(dot(rN, H)), 600.0);                 // glint stretto (le increspature lo spezzano)
        float fres = pow(1.0 - saturate(dot(rN, V)), 5.0);            // riflesso radente
        float3 skyCol = lerp(_SunColor, float3(0.55, 0.72, 1.0), 0.6) * (_Ambient + 0.6);
        wcol += _SunColor * (spec * 1.4 * saturate(ndlLand + 0.05));   // sole sull'acqua
        wcol = lerp(wcol, skyCol, fres * 0.45 * saturate(ndlLand + 0.25));   // cielo riflesso ai bordi

        // BATTIGIA: linea di schiuma dove l'acqua è bassissima (depth ~0) → riva netta
        float foam = (1.0 - smoothstep(0.0, 1.0, depth));
        wcol = lerp(wcol, wcol * 0.5 + float3(0.85, 0.9, 0.95) * 0.5, foam * 0.35);

        col = lerp(col, wcol, seaMask);
    }
    return col;
}

#endif

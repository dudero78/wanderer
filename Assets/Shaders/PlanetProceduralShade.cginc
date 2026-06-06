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
// LUCE AUSILIARIA point (manuale): es. la SONDA che illumina il terreno attorno. _AuxLightColor=0 (default) → nullo.
float3 _AuxLightPos, _AuxLightColor; float _AuxLightRange;
// 1 = baseN arriva GIÀ PRONTO per-vertice (gioco: il fragment non rifà il rumore gradiente per ogni pixel).
// 0 = calcolalo qui per-pixel (editor: massima qualità, non è perf-critico). Default 0 se non impostato.
float _PerVertexFields;
// 1 = i 3 fbm value-noise di colore (macro/minerali/maria) arrivano GIÀ PRONTI per-vertice (gioco, GPU-1): 6 vnoise
// per-pixel in meno, prerequisito del PBR. 0 = calcolati qui per-pixel (editor). Default 0 se non impostato.
float _PerVertexColor;

// ECLISSI (ombra analitica di un altro corpo): le imposta EclipseDriver per frame sul materiale del renderer GPU
// (come già su PlanetBaked). Raggio 0 (default, mai impostato) = nessuna eclissi → costo/effetto nullo.
float4 _EclipseOccluderPos; float _EclipseOccluderRadius; float3 _EclipseSunDir; float _EclipseSunAngular;

// PBR / MATERIALI PER PENDENZA (GPU-4, look SC/ED): roccia esposta sui versanti ripidi (bordi/pareti dei crateri,
// scarpate), sedimento/suolo nel piano → e un GGX leggero per il "luccichio" minerale radente. Gated da keyword
// _PBR_TERRAIN (→ WANDERER_PBR): default OFF nelle Properties, acceso dal C# in gioco; A/B da GameBootstrap.
// Sfrutta che obj→mondo è pura TRASLAZIONE (i corpi non ruotano) → la normale di mondo e la radiale d'oggetto sdir
// sono confrontabili direttamente per la PENDENZA, senza conoscere il centro nel fragment.
float4 _RockColor; float _RockSlopeStart, _RockSlopeEnd, _RockStr, _SpecStr, _Gloss;

// INCRESPATURA dell'acqua: perturba la normale del pelo con due ottave di rumore SCORREVOLE (gradiente
// ANALITICO da noised → niente differenze finite). È ciò che dà identità di SUPERFICIE all'acqua: spezza il
// glint del sole in scintille e toglie il look "dipinto" (un pelo perfettamente liscio riflette il sole come
// un'unica macchia enorme). DOMINIO = spazio OGGETTO (fisso al pianeta): così il flusso lo muove SOLO il tempo,
// non il moto del corpo nello spazio — usando la pos di MONDO, un corpo che ti orbita attorno aggiungeva il suo
// spostamento al dominio → l'acqua scorreva più o meno veloce a seconda della sua velocità. Il gradiente resta
// valido per perturbare la normale di MONDO perché pianeta→mondo è pura TRASLAZIONE (i corpi non ruotano su sé
// stessi): la traslazione non cambia direzioni/gradienti. velocità/scala costanti (acqua stilizzata, GPU-first).
float3 WaterRippleNormal(float3 objP, float3 Nw, float strength)
{
    if (strength <= 0.0) return Nw;
    float tt = _Time.y;   // secondi
    float4 n1 = noised(objP * 0.35 + float3(0.6, 0.0, 0.37) * tt);   // onda fine, scorre in una direzione
    float4 n2 = noised(objP * 0.13 + float3(-0.27, 0.0, 0.5) * tt);  // onda larga, scorre nell'altra
    float3 grad = n1.yzw * 0.6 + n2.yzw * 1.0;          // gradiente combinato (= mondo: niente rotazione)
    float3 slope = grad - dot(grad, Nw) * Nw;           // componente TANGENTE (la pendenza del pelo)
    return normalize(Nw - slope * strength);
}

// Pobj   = posizione in spazio OGGETTO (pianeta centrato all'origine): i campi di colore sono radiali da qui.
// worldP = posizione in spazio MONDO: serve solo al vettore vista del glint del mare.
// nrmW   = normale del pelo in spazio MONDO (per la luce). bnrmW = normale del fondo sommerso (mondo).
// depth  = profondità dell'acqua al vertice (pelo − fondo), interpolata; 0 dove asciutto.
// baseNField = ondulazione di base [0,1] PRE-CALCOLATA per-vertice (usata solo se _PerVertexFields>0.5).
// seaSurfField = QUOTA del pelo del mare attivo, PRE-CALCOLATA per-vertice dal compute (= SeaSurface esatta).
// colorFields = i 3 fbm value-noise di colore (x=macro, y=minerali, z=maria) PRE-CALCOLATI per-vertice (GPU-1),
//   usati solo se _PerVertexColor>0.5 (gioco); nell'editor sono ignorati e i fbm si fanno per-pixel.
float3 PlanetShade(float3 Pobj, float3 worldP, float3 nrmW, float3 bnrmW, float depth, float baseNField, float seaSurfField, float3 colorFields)
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
    // maschera del mare = "SOTTO il pelo è acqua" (un lato), non una banda stretta attorno al pelo. Così un punto
    // PIÙ BASSO del pelo (un cratere scavato DOPO il mare, o il terreno mosso) resta SOMMERSO — l'acqua lo copre, il
    // fondo si vede in trasparenza — invece di leggere "asciutto" e renderlo crosta grigia (bug Valentina2). Sopra il
    // pelo (terra/montagne emerse) → asciutto. La banda 0.15..0.75 m resta solo per la riva (l'acqua non si arrampica).
    float seaMask = (_SeaOn > 0.5) ? (1.0 - smoothstep(0.15, 0.75, h - seaSurf)) : 0.0;

    // suolo: colore base × variazione MACRO a bassa frequenza (campo dunale) — procedurale, niente texture.
    // GPU-1: in gioco macroV/zz/region arrivano per-vertice (interpolati) → niente 6 vnoise/pixel; nell'editor per-pixel.
    float macroV = (_PerVertexColor > 0.5) ? colorFields.x : fbm(sdir * _MacroScale);
    float3 alb = _SoilMean.rgb * lerp(1.0, 0.78 + macroV * 0.44, _MacroVar) * _SoilTint.rgb;

    // regioni minerali: velatura di TINTA larga (calda/fredda), bassa frequenza
    float zz = (_PerVertexColor > 0.5) ? colorFields.y : fbm(sdir * _MineralScale);
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
    float region = smoothstep(0.42, 0.64, (_PerVertexColor > 0.5) ? colorFields.z : fbm(sdir * _MariaScale));
    alb = lerp(alb, alb * _MariaColor.rgb, low * region * _MariaStr);

    // PBR — ROCCIA PER PENDENZA: i versanti ripidi (bordi/pareti dei crateri, scarpate) espongono roccia nuda; il
    // piano resta suolo/sedimento. La pendenza = quanto la normale devia dalla radiale (sdir), valida senza il
    // centro perché obj→mondo non ruota. Tinta di base × _RockColor (modula, non sostituisce → conserva l'identità).
#if defined(WANDERER_PBR)
    {
        float slope = 1.0 - saturate(dot(normalize(nrmW), sdir));
        float rockM = smoothstep(_RockSlopeStart, _RockSlopeEnd, slope) * _RockStr;
        alb = lerp(alb, alb * _RockColor.rgb, rockM);
    }
#endif

    // MARE SOLIDO OPACO (né liquido né trasparente): è SUOLO di colore diverso — antiche colate laviche (maria)…
    // → tinta piatta del _SeaColor nell'albedo, niente trattamento d'acqua. (Liquido O trasparente = superficie a
    // sé, sotto la luce: vedi il blocco ACQUA.) Così un mare di maria resta roccia colorata, non acquamarina.
    // _HAS_SEA (GPU-2): tutto il trattamento del mare è STRIPPATO sui corpi asciutti (la keyword definisce
    // WANDERER_HAS_SEA solo dove la ricetta ha un mare) → fragment più snello su Cetra/Luna6. Nell'editor è sempre on.
#if defined(WANDERER_HAS_SEA)
    if (_SeaOn > 0.5 && _SeaLiquid < 0.5 && _SeaClear < 0.5)
    {
        float seaLuma = dot(_SeaColor.rgb, float3(0.2126, 0.7152, 0.0722));
        float3 seaCol = lerp(float3(seaLuma, seaLuma, seaLuma), _SeaColor.rgb, _SeaSat);
        alb = lerp(alb, seaCol, seaMask);
    }
#endif

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

    // LUCE AUSILIARIA (point): es. la sonda. Diffusa, niente cono. Attenuazione LINEARE (non quadratica come la
    // torcia) → arriva PIÙ LONTANO a parità di range, così la sonda illumina un'intera stanza/zona buia. _AuxLightColor=0 → nullo.
    float3 toA = _AuxLightPos - worldP;
    float dA = length(toA);
    float3 LA = toA / max(dA, 1e-4);
    float nA = dA / max(_AuxLightRange, 1.0);
    float attA = 1.0 - smoothstep(0.30, 1.0, nA);          // PLATEAU fino a ~0.3·range (fade lento), poi crollo dolce
    float lambA = 0.30 + 0.70 * saturate(dot(nrm, LA));    // morbido: i versanti radenti non restano neri (niente anello scuro)
    col += alb * (_AuxLightColor * (lambA * attA));

    // PBR — SPECULARE GGX LEGGERO sul suolo (riflesso minerale radente, look SC/ED): broad lobe, basso peso → non
    // "plasticoso". Solo sul lato illuminato (×ndlLand). L'acqua, sotto, sovrascrive col dove allagato (suo speculare).
#if defined(WANDERER_PBR)
    {
        float3 Vp = normalize(_WorldSpaceCameraPos - worldP);
        float3 Hp = normalize(_SunDir + Vp);
        float specL = pow(saturate(dot(nrm, Hp)), max(_Gloss, 1.0)) * _SpecStr * ndlLand;
        col += _SunColor * specL;
    }
#endif

    // ===== ACQUA (superficie): mare LIQUIDO e/o TRASPARENTE =====
    // Il COLORE viene tutto dagli slider R/G/B (_SeaColor): bassofondo CHIARO → profondo SCURO, sempre nella tinta
    // scelta → puoi fare acqua, acido, qualunque colore; più scuro = meno si vede il fondo. TRASPARENTE (_SeaClear,
    // indipendente da liquido → vale anche per ghiaccio) lascia vedere il fondo, TINTO dal colore (scuro/saturo =
    // fondo più nascosto). LIQUIDO (_SeaLiquid) aggiunge increspatura animata + glint + Fresnel + battigia; senza
    // liquido (es. ghiaccio) la superficie è liscia. Almeno uno dei due acceso → questo blocco; nessuno → mare opaco.
#if defined(WANDERER_HAS_SEA)
    if (seaMask > 0.0 && (_SeaLiquid > 0.5 || _SeaClear > 0.5))
    {
        float seaLuma = dot(_SeaColor.rgb, float3(0.2126, 0.7152, 0.0722));
        float3 shallowCol = lerp(_SeaColor.rgb, saturate(_SeaColor.rgb * 1.4 + 0.12), 0.5);   // bassofondo più chiaro (stessa tinta, gentile)
        float3 deepCol = _SeaColor.rgb * 0.5;                                                 // profondo più scuro
        float dN = saturate(depth / 34.0);
        float3 waterCol = lerp(shallowCol, deepCol, dN);
        waterCol = lerp(float3(seaLuma, seaLuma, seaLuma), waterCol, _SeaSat);     // saturazione propria del mare

        // TRASPARENZA: il fondo emerge in acqua bassa, visto ATTRAVERSO l'acqua → MOLTIPLICATO per la TRASMISSIONE
        // del colore (l'acqua ASSORBE, non amplifica): colore CHIARO ⇒ trasmette ~tutto (fondo limpido, niente
        // bianco); colore SCURO/saturo ⇒ trasmette poco (fondo scuro/tinto = meno visibile). cap a 1.1 = niente
        // sovra-esposizione. pow(.,0.6) = più limpido alle profondità medie. Soglia di profondità = _SeaClarity.
        float seaTrans = 0.0;
        if (_SeaClear > 0.5)
        {
            // TRASPARENZA ripensata (bug #2): (a) a limpidezza MAX il fondo si vede a QUALUNQUE profondità — il
            // Beer-Lambert puro non ci arriva mai (exp→0 sul profondo), quindi sfumo verso 1 vicino al massimo dello
            // slider; (b) la TINTA di trasmissione è separata dalla LUMINOSITÀ e tende al BIANCO alzando la limpidezza
            // → il fondo emerge col SUO colore, MAI più scuro (basta lo "scurimento al contrario" col mare scuro/saturo).
            float clarityN = saturate(_SeaClarity / 150.0);                 // 0..1 sul range dello slider (150 = cristallina)
            float bl = exp(-max(depth, 0.0) / max(_SeaClarity, 0.05));      // assorbimento con la profondità (acqua torbida)
            float crystal = smoothstep(0.45, 1.0, clarityN);                // metà-alta dello slider: via via cristallina
            seaTrans = lerp(pow(bl, 0.6), 1.0, crystal);                    // → 1 a limpidezza max: fondo pieno a ogni profondità
            // il fondo visto sott'acqua = albedo del fondo TINTO d'acqua (bluastro), MAI bianco: a limpidezza max vedi il
            // fondo CHIARO ma sempre "sotto un velo d'acqua" (tinta blu + glint/ripple sopra), non terra asciutta grigia.
            // (Era il bug: la tinta tendeva al bianco al massimo → alb×bianco = albedo nudo = grigio indistinguibile.)
            float3 transTint = min(_SeaColor.rgb * 1.6, 1.1);
            float3 bedThroughWater = alb * transTint;
            waterCol = lerp(waterCol, bedThroughWater, seaTrans);
        }

        // normale: con LIQUIDO il pelo è acqua increspata; senza (ghiaccio) è liscio. Dove si vede il fondo in
        // trasparenza, un filo di rilievo del fondale (non sostituisce la normale → niente lastra vetrosa alla riva).
        // INCRESPATURA con LOD per DISTANZA (collo GPU del mare a bassa quota): l'onda è SUB-PIXEL sull'acqua
        // lontana → la sfumo verso il pelo liscio e SALTO le 2 noised quando il contributo è trascurabile. A bassa
        // quota guardando il mare verso l'orizzonte la maggior parte dei pixel è acqua lontana → enorme risparmio
        // sul fragment; il vicino resta pienamente increspato. (Soglie ~30..250 m, scala "media" dei corpi.)
        float3 rN;
        if (_SeaLiquid > 0.5)
        {
            float rippleLod = saturate((250.0 - distance(worldP, _WorldSpaceCameraPos)) / 220.0);
            rN = (rippleLod > 0.004) ? lerp(nrm, WaterRippleNormal(Pobj, nrm, 0.22), rippleLod) : nrm;
        }
        else rN = nrm;
        float3 shadeN = (seaTrans > 0.0) ? normalize(lerp(rN, normalize(bnrmW), seaTrans * 0.30)) : rN;
        float ndl = saturate(dot(shadeN, _SunDir));
        float3 wcol = waterCol * (ndl * _SunColor + _Ambient);

        if (_SeaLiquid > 0.5)
        {
            // riflesso d'ACQUA: glint del sole spezzato dalle increspature + Fresnel verso un cielo chiaro + battigia
            float3 V = normalize(_WorldSpaceCameraPos - worldP);
            float3 H = normalize(_SunDir + V);
            float spec = pow(saturate(dot(rN, H)), 600.0);
            float fres = pow(1.0 - saturate(dot(rN, V)), 5.0);
            float3 skyCol = lerp(_SunColor, float3(0.55, 0.72, 1.0), 0.6) * (_Ambient + 0.6);
            wcol += _SunColor * (spec * 1.4 * saturate(ndlLand + 0.05));
            wcol = lerp(wcol, skyCol, fres * 0.45 * saturate(ndlLand + 0.25));
            float foam = (1.0 - smoothstep(0.0, 1.0, depth));
            wcol = lerp(wcol, wcol * 0.5 + float3(0.85, 0.9, 0.95) * 0.5, foam * 0.35);
        }

        col = lerp(col, wcol, seaMask);
    }
#endif // WANDERER_HAS_SEA

    // ===== ECLISSI: ombra analitica di un altro corpo (stesso calcolo di PlanetBaked) =====
    // Copertura del disco solare vista da P in coordinate ANGOLARI: vicino all'occlusore umbra piena, allontanandosi
    // l'occlusore rimpicciolisce → umbra corta, poi solo penombra. Attenua il termine SOLE (l'eclissi blocca il sole,
    // non la torcia/l'ambiente). Niente shadow map → zero acne. Raggio 0 → ecl=1 (nessun effetto).
    if (_EclipseOccluderRadius > 0.0)
    {
        float3 Lsun = _EclipseSunDir;
        float3 mo = _EclipseOccluderPos.xyz - P;
        float tca = dot(mo, Lsun);                          // l'occlusore è verso il sole?
        if (tca > 1e-3)
        {
            float dperp = sqrt(max(dot(mo, mo) - tca * tca, 0.0));
            float rhoOcc = _EclipseOccluderRadius / tca;    // raggio angolare dell'occlusore
            float rhoSun = max(_EclipseSunAngular, 1e-4);   // raggio angolare del sole
            float sigma  = dperp / tca;                     // separazione angolare sole↔occlusore
            float fcov = 1.0 - smoothstep(abs(rhoSun - rhoOcc), rhoSun + rhoOcc, sigma);
            float peak = saturate((rhoOcc * rhoOcc) / (rhoSun * rhoSun));   // <1 se anulare (occlusore più piccolo del sole)
            col *= 1.0 - fcov * peak * 0.92;
        }
    }
    return col;
}

#endif

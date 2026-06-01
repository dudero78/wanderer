Shader "Wanderer/PlanetBaked"
{
    // Shader di superficie del pianeta. Quattro fonti, ognuna usata dove FUNZIONA:
    //   - RILIEVO (forma del terreno): texture bakeata (Wanderer/PlanetBake), valore+gradiente,
    //     con mipmap → niente sfarfallio, freddo, coerente a ogni distanza.
    //   - GRANA (micro-rilievo come NORMALE): da foto PBR, SOLO ravvicinata. Il bump si vede
    //     bene solo da vicino e a sguardo ripido; a luce radente/distanza collassa, quindi lì
    //     non lo usiamo.
    //   - COLORE / SUOLI: tre foto (terra bruna, sabbia grigia, fango oliva) usate per BANDA DI
    //     DISTANZA — ognuna dove rende meglio: terra coi sassolini da vicino, sabbia nel medio,
    //     fango a macchie larghe in lontananza. La variazione tonale la danno le foto stesse.
    //
    // PERCORSO FREDDO: il costo per-pixel è tenuto basso apposta — i masks (rugosità, minerali)
    // usano value-noise a 1 ottava, e il lavoro di dettaglio (terra vicina, bump, grana) si
    // SALTA via branch quando la distanza lo annulla. Così il caso peggiore (pianeta intero a
    // video) resta leggero: è ciò che tiene fresco il Mac.
    Properties
    {
        _PeakColor  ("Vette (cappucci chiari)", Color) = (0.74, 0.76, 0.70, 1)
        _RockColor  ("Roccia (zone rugose)", Color) = (0.46, 0.42, 0.36, 1)

        _BaseRadius ("Raggio base",      Float) = 500
        _Amplitude  ("Ampiezza terreno", Float) = 30

        _MottleStr  ("Variazione colore avvallamenti", Range(0,1)) = 0.12

        // SUOLO LISCIO: colore quasi uniforme. Il bello è la FORMA del terreno e la LUCE, non il
        // dettaglio di superficie. _SoilMean = colore base (grigio lunare); _SoilTint lo modula.
        // Per un pianeta giallo/rosso bastano questi due colori (è la "identità" del pianeta).
        _SoilTint ("Suolo: tinta/luminosità", Color) = (1.0, 1.0, 1.02, 1)
        _SoilMean ("Suolo: colore base", Color) = (0.44, 0.44, 0.45, 1)
        _MacroVar ("Variazione macro (campo dunale)", Range(0,1)) = 0.45
        _MacroScale ("Variazione macro: scala (basso = chiazze larghe)", Float) = 5
        // contrasto della grana fotografica: 0 = sabbia perfettamente liscia, alto = grana visibile.
        // Tenuto BASSO apposta: sulla sabbia l'alta frequenza diventa "rumore"/"neve TV".
        _SandDetail ("Sabbia: contrasto grana (0 = liscia)", Range(0,1)) = 0.18

        // --- regioni minerali: variazione di TINTA larga (non di luminosità) ---
        // base neutra + chiazze calde (A) e fredde (B) distinte: regioni vere, non una velatura.
        _MineralA ("Minerale: chiazze calde (ruggine)", Color) = (1.18, 0.92, 0.74, 1)
        _MineralB ("Minerale: chiazze fredde (ardesia)", Color) = (0.82, 0.92, 1.08, 1)
        _MineralFreq ("Minerale: scala regioni", Float) = 1.8
        _MineralStr  ("Minerale: forza", Range(0,1)) = 0.18

        _PeakStr  ("Forza cappucci vetta", Range(0,1)) = 0.5

        _RoughFreq  ("Scala zone rugose",     Float) = 0.8
        _RoughThresh ("Soglia zone rugose",   Range(0,1)) = 0.60
        _RoughBoost ("Rilievo extra zone rugose", Range(1,4)) = 1.8

        _BaseFreq   ("Scala rilievo (ottava base)", Float) = 0.25
        _DetailStr  ("Forza rilievo (media/lontana dist.)", Range(0,1)) = 0.25
        // bias di mip sul rilievo: negativo = mip piu' nitida. Tenuto MITE (-0.2): ora il dettaglio
        // medio lo fa la GEOMETRIA (ottava 7), non serve spremere la texture, e un bias aggressivo
        // dava "rugosità che striscia" (sfarfallio) a volo radente.
        _ReliefBias ("Rilievo: bias mip (neg = nitido)", Range(-2,0)) = -0.2

        // micro-grana come NORMALE, solo da naso a terra (< ~13 m): un soffio. Sulla sabbia la
        // normale ad alta frequenza è la prima causa di sparkle/moiré sotto luce → quasi spenta.
        _GrainStr    ("Micro-grana (normale, solo vicino)", Range(0,1)) = 0.06
        // scala FISSA (world-fixed) della grana fotografica/normale: ripetizioni sulla faccia.
        _DetailScale ("Dettaglio: ripetizioni sulla faccia", Float) = 320

        // GEOMORPH: ampiezza della banda di morphing come frazione della distanza di split del
        // nodo. 0.4 = il nodo è coerente col genitore tra splitDist e 1.4×splitDist, poi "cresce"
        // nel dettaglio fine. Più alto = transizione più lunga e morbida (ma dettaglio un filo più
        // tardi); più basso = dettaglio prima ma transizione più corta.
        _MorphRange ("Geomorph: ampiezza banda", Range(0.1, 0.9)) = 0.4

        [NoScaleOffset] _ReliefMap ("Rilievo bakeato (ottave grosse)", 2D) = "black" {}
        [NoScaleOffset] _MaskMap ("Maschere bakeate (R=minerali, G=rugosità)", 2D) = "gray" {}
        [NoScaleOffset] _DetailNormal ("Grana suolo (normal tileable)", 2D) = "bump" {}
        [NoScaleOffset] _SoilSand ("Suolo: sabbia grigia (diffuse)", 2D) = "gray" {}
        [NoScaleOffset] _SoilMud  ("Suolo: fango oliva (diffuse)",   2D) = "gray" {}
        [NoScaleOffset] _SoilDirt ("Suolo: terra bruna (diffuse)",   2D) = "gray" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Lambert vertex:vert
        #pragma target 4.0
        #include "PlanetNoise.cginc"

        fixed4 _PeakColor, _RockColor, _SoilTint, _SoilMean, _MineralA, _MineralB;
        float _MineralFreq, _MineralStr;
        float _BaseRadius, _Amplitude, _MottleStr;
        float _RoughFreq, _RoughThresh, _RoughBoost;
        float _BaseFreq, _DetailStr, _ReliefBias;
        float _GrainStr, _DetailScale, _MacroVar, _MacroScale, _SandDetail;
        float _PeakStr;
        float _MorphRange;
        sampler2D _ReliefMap;
        sampler2D _MaskMap;
        sampler2D _DetailNormal;
        sampler2D _SoilSand, _SoilMud, _SoilDirt;

        // DETTAGLIO WORLD-FIXED: la UV è ancorata alla superficie del pianeta (texUV globale della
        // faccia), quindi grani e sassi NON scivolano mai. UNA scala fissa, mipmappata + aniso:
        // nitida fino al texel da vicino, smorzata in lontananza dal MIPMAP hardware → niente moiré,
        // niente sfocatura. UNA sola ottava di colore: due copie della stessa foto a scale diverse
        // davano l'effetto "sdoppiato". Il micro-dettaglio più fine lo porta la NORMALE (è luce, non
        // ridisegna il pattern dell'albedo). La ripetizione la rompe la variazione macro.
        float3 detailNrm(sampler2D tex, float2 uv, float scale)
        {
            return tex2D(tex, uv * scale).xyz * 2.0 - 1.0;
        }

        struct Input
        {
            float3 localPos;
            float3 worldPos;
            float3 objT;
            float3 objB;
            float2 texUV;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);

            // GEOMORPH (CDLOD): UV2 porta xyz = spostamento verso la griglia del genitore, w =
            // distanza di split del nodo. Lontano (appena comparso) il vertice sta sulla forma del
            // genitore; avvicinandosi si trasforma con CONTINUITÀ nel dettaglio fine → niente pop,
            // niente anello di LOD. È puramente visivo: il giocatore segue SampleHeight (la verità),
            // e da vicino il morph è 0, quindi mesh e collisione combaciano dove conta.
            float4 mph = v.texcoord1;
            if (mph.w > 0.0)
            {
                float3 wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float d = distance(wpos, _WorldSpaceCameraPos);
                float mf = saturate((d - mph.w) / (mph.w * _MorphRange));   // 0 vicino, 1 lontano
                v.vertex.xyz += mph.xyz * mf;
            }

            o.localPos = v.vertex.xyz;
            o.objT = v.tangent.xyz;
            o.objB = cross(v.normal, v.tangent.xyz) * v.tangent.w;
            o.texUV = v.texcoord.xy;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float3 P = IN.localPos;
            float h = max(length(P), 1e-4);
            float3 N = P / h;               // normale radiale (spazio oggetto)

            float dist = distance(IN.worldPos, _WorldSpaceCameraPos);

            // regioni minerali (bassa frequenza): solo per la TINTA larga della sabbia. UNA lettura.
            float z = tex2D(_MaskMap, IN.texUV).r;

            // === SABBIA LISCIA ===
            // Il bello del pianeta sabbioso è la FORMA (dune) e la LUCE, non il dettaglio di
            // superficie. Quindi: colore quasi uniforme, variazione SOLO a grande scala (campo
            // dunale, ~150 m), più una grana fotografica a BASSO contrasto che emerge solo da vicino.
            // Niente alta frequenza diffusa = niente "rumore"/"neve TV". Pochissimo costo per pixel.
            float macroV = tex2D(_SoilSand, IN.texUV * _MacroScale).g;        // bassa freq → blob morbidi
            float3 sand = _SoilMean.rgb * lerp(1.0, 0.78 + macroV * 0.44, _MacroVar);

            // grana fotografica a basso contrasto, solo abbastanza vicino da risolverla (il mip la
            // smorza da lontano). Normalizzata sul suo grigio medio → MODULA la sabbia senza
            // cambiarne il colore. detW = 0 oltre ~120 m → da lontano sabbia perfettamente liscia.
            float detW = (1.0 - smoothstep(45.0, 120.0, dist)) * _SandDetail;
            if (detW > 0.0)
            {
                // letta SFOCATA (mip bias +2): la sorgente è sassosa, a piena risoluzione darebbe
                // speckle. Sfocata diventa TONO morbido (chiaroscuro fine), non puntini → sabbia.
                float3 g = tex2Dbias(_SoilSand, float4(IN.texUV * _DetailScale, 0.0, 2.0)).rgb;
                float gm = max(dot(g, float3(0.333, 0.333, 0.333)), 0.04);
                sand *= lerp(float3(1.0, 1.0, 1.0), g / gm, detW);
            }
            float3 alb = sand * _SoilTint.rgb;

            // regioni minerali: SOLO variazione di TINTA larga (hue), bassa frequenza → grandi
            // regioni morbide (sabbia più calda/fredda), mai chiazze ad alta frequenza.
            float3 mineral = float3(1.0, 1.0, 1.0);
            mineral = lerp(mineral, _MineralB.rgb, smoothstep(0.45, 0.28, z));   // freddo, z basso
            mineral = lerp(mineral, _MineralA.rgb, smoothstep(0.55, 0.72, z));   // caldo, z alto
            alb *= lerp(float3(1.0, 1.0, 1.0), mineral, _MineralStr);

            // cappucci appena più chiari sulle creste delle dune (tenui).
            float t = saturate((h - (_BaseRadius - _Amplitude)) / (2.0 * _Amplitude));
            alb = lerp(alb, _PeakColor.rgb, smoothstep(0.74, 0.97, t) * _PeakStr);

            // --- normale: un SOFFIO di micro-grana, solo da naso a terra (< ~13 m). Sulla sabbia la
            // normale ad alta frequenza è la prima causa di sparkle/moiré sotto luce, quindi è quasi
            // spenta: la forma la dà la GEOMETRIA delle dune. Premio "microscopio" da vicinissimo.
            float2 nxy = float2(0.0, 0.0);
            float nW = (1.0 - smoothstep(4.0, 13.0, dist)) * _GrainStr;
            if (nW > 0.0)
            {
                float3 dn = detailNrm(_DetailNormal, IN.texUV, _DetailScale);
                nxy += dn.xy * nW;
            }
            o.Normal = normalize(float3(nxy, 1.0));

            o.Albedo = alb;
        }
        ENDCG
    }

    FallBack "Diffuse"
}

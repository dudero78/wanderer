Shader "Wanderer/PlanetBaked"
{
    // Shader di superficie del pianeta (Lambert). Filosofia: la FORMA del terreno (colline/dune) e
    // la LUCE fanno il lavoro; il colore è quasi uniforme e WORLD-FIXED, niente alta frequenza
    // diffusa (che su una superficie liscia diventa "rumore"/"neve TV"). Fonti:
    //   - COLORE: colore base (_SoilMean) modulato da una variazione MACRO a bassa frequenza (campo
    //     dunale) + una grana fotografica a BASSO contrasto, solo da vicino e letta sfocata.
    //   - REGIONI MINERALI (_MaskMap, bakeato per faccia): tinta larga calda/fredda, bassa frequenza.
    //   - MICRO-GRANA (_DetailNormal): un soffio di normale solo da naso a terra (premio microscopio).
    //   - GEOMORPH (UV2): transizione LOD continua, niente pop.
    // Tutto world-fixed (UV ancorata alla faccia) + mipmap hardware → niente moiré, niente
    // scivolamento, nitido a ogni distanza senza trucchi che galleggiano con la camera.
    //
    // NOTA perf: la GPU è ampiamente sotto-utilizzata (profilo ~1 ms); il collo di bottiglia è la
    // CPU. Quindi qui c'è margine: il dettaglio per-pixel ricco (crateri, parallax, roccia) andrà
    // messo NELLO SHADER per i pianeti strutturati, non in geometria che pesa sulla CPU.
    Properties
    {
        _PeakColor  ("Vette (cappucci chiari)", Color) = (0.74, 0.76, 0.70, 1)
        _PeakStr  ("Forza cappucci vetta", Range(0,1)) = 0.5

        _BaseRadius ("Raggio base",      Float) = 500
        _Amplitude  ("Ampiezza terreno", Float) = 30

        // SUOLO LISCIO: colore quasi uniforme. Il bello è la FORMA del terreno e la LUCE, non il
        // dettaglio di superficie. _SoilMean = colore base (grigio lunare); _SoilTint lo modula.
        // Per un pianeta giallo/rosso bastano questi due colori (è la "identità" del pianeta).
        _SoilTint ("Suolo: tinta/luminosità", Color) = (1.0, 1.0, 1.02, 1)
        _SoilMean ("Suolo: colore base", Color) = (0.44, 0.44, 0.45, 1)
        _MacroVar ("Variazione macro (campo dunale)", Range(0,1)) = 0.45
        _MacroScale ("Variazione macro: scala (basso = chiazze larghe)", Float) = 5
        // contrasto della grana fotografica: 0 = superficie perfettamente liscia, alto = grana visibile.
        // Tenuto BASSO apposta: l'alta frequenza diffusa diventa "rumore"/"neve TV".
        _SandDetail ("Suolo: contrasto grana (0 = liscio)", Range(0,1)) = 0.18

        // regioni minerali (da _MaskMap bakeato): tinta larga calda (A) / fredda (B), bassa frequenza.
        // Tenuta tenue (Str basso): è una velatura regionale, non chiazze. Forte = pianeti più "vari".
        _MineralA ("Minerale: chiazze calde (ruggine)", Color) = (1.18, 0.92, 0.74, 1)
        _MineralB ("Minerale: chiazze fredde (ardesia)", Color) = (0.82, 0.92, 1.08, 1)
        _MineralStr  ("Minerale: forza", Range(0,1)) = 0.18

        // micro-grana come NORMALE, solo da naso a terra (< ~13 m): un soffio. La normale ad alta
        // frequenza è la prima causa di sparkle/moiré sotto luce → quasi spenta.
        _GrainStr    ("Micro-grana (normale, solo vicino)", Range(0,1)) = 0.06
        // scala FISSA (world-fixed) della grana/normale: ripetizioni sulla faccia.
        _DetailScale ("Dettaglio: ripetizioni sulla faccia", Float) = 320

        // GEOMORPH: ampiezza della banda di morphing come frazione della distanza di split del nodo.
        // Più alto = transizione più lunga e morbida; più basso = dettaglio prima ma transizione corta.
        _MorphRange ("Geomorph: ampiezza banda", Range(0.1, 0.9)) = 0.4

        [NoScaleOffset] _MaskMap ("Maschere bakeate (R=minerali)", 2D) = "gray" {}
        [NoScaleOffset] _DetailNormal ("Grana suolo (normal tileable)", 2D) = "bump" {}
        [NoScaleOffset] _SoilSand ("Suolo: foto diffuse (grana/macro)", 2D) = "gray" {}
        // normale dei crateri bakeata per faccia + mippata: alta freq nitida, antialiased dal mip.
        [NoScaleOffset] _CraterNormalMap ("Crateri (normal bakeata)", 2D) = "bump" {}
        _CraterNormalApply ("Crateri: forza in resa", Range(0,2)) = 0.85
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Lambert vertex:vert
        #pragma target 4.0
        #include "PlanetNoise.cginc"

        fixed4 _PeakColor, _SoilTint, _SoilMean, _MineralA, _MineralB;
        float _MineralStr, _PeakStr;
        float _BaseRadius, _Amplitude;
        float _GrainStr, _DetailScale, _MacroVar, _MacroScale, _SandDetail;
        float _MorphRange;
        float _CraterNormalApply;
        sampler2D _MaskMap;
        sampler2D _DetailNormal;
        sampler2D _SoilSand;
        sampler2D _CraterNormalMap;

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

            // CRATERI: normale bakeata+mippata, world-fixed. Applicata in modo COSTANTE (niente
            // dissolvenza con la distanza: feature che spariscono avvicinandoti rompono l'immersione).
            // Resta un dettaglio di transizione: l'obiettivo è portare i crateri visibili in GEOMETRIA
            // vera (mesh ad alta risoluzione, build su thread) — questa normale resterà solo per la
            // grana più fine del passo-vertice.
            float3 cn = tex2D(_CraterNormalMap, IN.texUV).xyz * 2.0 - 1.0;
            nxy += cn.xy * _CraterNormalApply;

            o.Normal = normalize(float3(nxy, 1.0));

            o.Albedo = alb;
        }
        ENDCG
    }

    FallBack "Diffuse"
}

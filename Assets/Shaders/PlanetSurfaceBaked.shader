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

        // MARI legati alla GEOMETRIA (tipo lunari): scuriscono i BACINI bassi (fondi di cratere/avvallamenti)
        // in CERTE regioni → il colore ha un perché (segue il rilievo), non macchie a caso. _MariaColor =
        // scurezza dei bacini; _MariaScale = scala delle regioni; _MariaStr = quanto scuriscono.
        _MariaColor ("Mari: scurezza dei bacini", Color) = (0.52, 0.52, 0.56, 1)
        _MariaScale ("Mari: scala delle regioni", Float) = 2.2
        _MariaStr ("Mari: forza", Range(0,1)) = 0.7

        // MARE GEOMETRICO: dove la mesh è stata allagata (SeaTerrainLayer) il terreno è piatto a quota
        // _SeaLevel (raggio ASSOLUTO del pelo dell'acqua). Qui tingiamo quei punti col colore mare e
        // lisciamo la normale (acqua piatta). _SeaOn = 0/1.
        _SeaOn ("Mare attivo", Float) = 0
        _SeaLevel ("Mare: raggio del pelo dell'acqua", Float) = 0
        _SeaColor ("Mare: colore", Color) = (0.13, 0.33, 0.52, 1)
        _SeaSat ("Mare: saturazione", Range(0,2)) = 1
        _SeaRough ("Mare: ampiezza increspatura (m)", Float) = 0

        // saturazione del colore finale: 0 = grigio, 1 = naturale, >1 = carico.
        _Saturation ("Saturazione", Range(0,2)) = 1

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
        // quanto la TINTA della texture del suolo colora la superficie (distingue Terra/Rosso/Roccia).
        _SoilHue ("Suolo: forza tinta texture", Range(0,1)) = 0.55

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
        _MorphRange ("Geomorph: ampiezza banda", Range(0.1, 0.9)) = 0.5

        [NoScaleOffset] _MaskMap ("Maschere bakeate (R=minerali)", 2D) = "gray" {}
        [NoScaleOffset] _DetailNormal ("Grana suolo (normal tileable)", 2D) = "bump" {}
        [NoScaleOffset] _SoilSand ("Suolo: foto diffuse (grana/macro)", 2D) = "gray" {}
        // normale dei crateri bakeata per faccia + mippata: alta freq nitida, antialiased dal mip.
        [NoScaleOffset] _CraterNormalMap ("Crateri (normal bakeata)", 2D) = "bump" {}
        _CraterNormalApply ("Crateri: forza in resa", Range(0,2)) = 0.85
        // dissolvenza della normale crateri con la DISTANZA: da lontano i crateri sono sub-pixel e la loro
        // normale ad alta frequenza diventa "sgranato"/sparkle su pochi pixel. Oltre _CraterFadeFar la
        // superficie torna liscia (normale geometrica della sfera) → disco pulito e ben illuminato.
        _CraterFadeNear ("Crateri: inizio dissolvenza (m)", Float) = 2500
        _CraterFadeFar  ("Crateri: fine dissolvenza (m)",   Float) = 9000

        // ALBEDO da MAPPA EQUIRECT (dati reali / mappa autorata): se _AlbedoMapStr>0, l'albedo viene letto da
        // questa texture campionata per DIREZIONE (lon/lat) invece che dal procedurale. È la "fonte di verità"
        // del colore (validazione pipeline mappe→render). _AlbedoMapStr=1 = solo mappa.
        [NoScaleOffset] _AlbedoMap ("Albedo equirect (mappa reale)", 2D) = "gray" {}
        _AlbedoMapStr ("Albedo map: forza", Range(0,1)) = 0

        // ECLISSI (ombra analitica di un ALTRO corpo): occlusore = sfera (centro+raggio in spazio oggetto),
        // direzione del sole in spazio oggetto. Li imposta EclipseDriver per frame. Raggio 0 = nessuna eclissi.
        _EclipseOccluderPos    ("Eclissi: centro occlusore (obj)", Vector) = (0,0,0,0)
        _EclipseOccluderRadius ("Eclissi: raggio occlusore (obj)", Float) = 0
        _EclipseSunDir         ("Eclissi: direzione del sole (obj)", Vector) = (0,1,0,0)
        _EclipseStrength       ("Eclissi: profondità ombra", Range(0,1)) = 0.92
        // raggio ANGOLARE del sole (rad) visto dal corpo: governa la penombra e la LUNGHEZZA dell'umbra
        // → l'ombra sbiadisce con la distanza dall'occlusore (umbra che si accorcia, poi solo penombra).
        _EclipseSunAngular     ("Eclissi: raggio angolare del sole (rad)", Float) = 0.033
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Lambert vertex:vert
        #pragma target 4.0
        #include "PlanetNoise.cginc"

        fixed4 _PeakColor, _SoilTint, _SoilMean, _MineralA, _MineralB, _MariaColor;
        float _MineralStr, _PeakStr, _MariaScale, _MariaStr;
        float _BaseRadius, _Amplitude;
        float _GrainStr, _DetailScale, _MacroVar, _MacroScale, _SandDetail, _SoilHue;
        float _MorphRange;
        float _SeaOn, _SeaLevel, _SeaSat, _SeaRough;
        fixed4 _SeaColor;
        float _Saturation;
        float _CraterNormalApply, _CraterFadeNear, _CraterFadeFar;
        sampler2D _MaskMap;
        sampler2D _DetailNormal;
        sampler2D _SoilSand;
        sampler2D _CraterNormalMap;
        sampler2D _AlbedoMap;
        float _AlbedoMapStr;
        float4 _EclipseOccluderPos, _EclipseSunDir;
        float _EclipseOccluderRadius, _EclipseStrength, _EclipseSunAngular;

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
                // mf: 0 = dettaglio fine (vicino), 1 = forma del genitore (lontano). Il morph COMPLETA a
                // d = splitDist (la distanza di split del nodo), morfando nella banda [splitDist·(1-range), splitDist].
                // Così quando una patch arriva al suo limite — dove può confinare con una vicina più GROSSA — è
                // già del tutto sulla forma del genitore (= la forma della vicina) → i gradini di LOD si chiudono,
                // la transizione è graduale, niente scalini seghettati. (Prima completava a 1.4·splitDist, troppo tardi.)
                float mf = saturate((d - mph.w * (1.0 - _MorphRange)) / (mph.w * _MorphRange));
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

            // MARE: tinge solo il PELO dell'acqua (mesh allagata e piatta a _SeaLevel). NON le buche scavate
            // sotto: un cratere DOPO il mare nella pipeline rimane sotto il pelo → resta ASCIUTTO (l'ordine
            // dei processi cambia l'effetto). 'above' = fino al pelo; 'below' = esclude i fondi ben sotto.
            // banda allargata dall'ampiezza dell'increspatura (_SeaRough): il pelo ondulato va da
            // _SeaLevel−rough a _SeaLevel+rough; le buche scavate DOPO il mare (più profonde) restano escluse.
            float seaAbove = 1.0 - smoothstep(_SeaLevel + _SeaRough, _SeaLevel + _SeaRough + 2.0, h);
            float seaBelow = smoothstep(_SeaLevel - _SeaRough - 5.0, _SeaLevel - _SeaRough - 1.0, h);
            float seaMask = (_SeaOn > 0.5) ? seaAbove * seaBelow : 0.0;

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

            // TINTA dalla texture del suolo (Terra/Rosso/Roccia): colore a BASSA frequenza, sfocato (mip +3)
            // → distingue i materiali senza alta frequenza, niente moiré. Normalizzato sul grigio medio →
            // sposta la TINTA (hue), non la luminosità. _SoilHue regola quanto la texture colora.
            float3 texMacro = tex2Dbias(_SoilSand, float4(IN.texUV * _MacroScale, 0.0, 3.0)).rgb;
            float texLum = max(dot(texMacro, float3(0.333, 0.333, 0.333)), 0.05);
            sand *= lerp(float3(1.0, 1.0, 1.0), texMacro / texLum, _SoilHue);

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

            // MARI legati alla GEOMETRIA: scuriscono i BACINI bassi (t = quota normalizzata, già calcolata) ma
            // solo in CERTE regioni (maschera a grande scala). Così il colore SEGUE il rilievo — scuro dove è
            // basso/allagato, chiaro sull'altopiano — e ha sempre un perché, niente macchie scollegate.
            float3 sdir = normalize(P);
            float low = 1.0 - smoothstep(0.16, 0.46, t);                      // 1 nei bacini bassi, 0 in alto
            float region = smoothstep(0.42, 0.64, fbm(sdir * _MariaScale));   // solo ALCUNE regioni hanno mari
            alb = lerp(alb, alb * _MariaColor.rgb, low * region * _MariaStr);

            // ALBEDO da MAPPA EQUIRECT (dati reali): campiona per DIREZIONE (lon/lat) → fonte di verità del
            // colore. Mappa la sfera dei punti: lon = atan2(z,x), lat = asin(y). Sostituisce il procedurale.
            if (_AlbedoMapStr > 0.0)
            {
                float3 d = normalize(P);
                float2 euv = float2(atan2(d.z, d.x) * (0.5 / UNITY_PI) + 0.5,
                                    asin(clamp(d.y, -1.0, 1.0)) * (1.0 / UNITY_PI) + 0.5);
                alb = lerp(alb, tex2D(_AlbedoMap, euv).rgb, _AlbedoMapStr);
            }

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
            // dissolvenza con la distanza: da lontano i crateri sono sub-pixel → la loro normale fa solo
            // "sgranato". Oltre _CraterFadeFar resta la normale liscia della sfera → disco pulito.
            float craterFade = 1.0 - smoothstep(_CraterFadeNear, _CraterFadeFar, dist);
            nxy += cn.xy * (_CraterNormalApply * craterFade);

            // sull'acqua liscia la normale: niente grana/crateri, è un pelo piatto (riflette uniforme).
            o.Normal = normalize(float3(nxy * (1.0 - seaMask), 1.0));

            // tinta del MARE: copre suolo e crateri sommersi col colore dell'acqua (saturazione propria, _SeaSat,
            // indipendente dal globale). Prima dell'eclissi così l'ombra scurisce anche il mare.
            float seaLuma = dot(_SeaColor.rgb, float3(0.2126, 0.7152, 0.0722));
            float3 seaCol = lerp(float3(seaLuma, seaLuma, seaLuma), _SeaColor.rgb, _SeaSat);
            alb = lerp(alb, seaCol, seaMask);

            // === ECLISSI: ombra analitica di un altro corpo ===
            // Dal punto P guardo verso il sole (L) e calcolo quanto del DISCO solare è coperto dal disco
            // dell'occlusore, in coordinate ANGOLARI viste da P. Così l'ombra dipende dalla geometria reale:
            // vicino all'occlusore esso ingloba il sole → umbra piena; allontanandosi rimpicciolisce
            // angolarmente → l'umbra si accorcia e resta solo penombra sbiadita (come nella realtà).
            // Niente shadow map → zero acne, nessun limite di shadow distance.
            if (_EclipseOccluderRadius > 0.0)
            {
                float3 L = _EclipseSunDir.xyz;
                float3 m = _EclipseOccluderPos.xyz - P;     // dal punto al centro dell'occlusore
                float tca = dot(m, L);                       // distanza occlusore→P lungo la direzione del sole
                if (tca > 1e-3)                              // l'occlusore è verso il sole, non dietro
                {
                    float dperp = sqrt(max(dot(m, m) - tca * tca, 0.0));
                    float rhoOcc = _EclipseOccluderRadius / tca;     // raggio angolare dell'occlusore
                    float rhoSun = max(_EclipseSunAngular, 1e-4);    // raggio angolare del sole
                    float sigma  = dperp / tca;                      // separazione angolare sole↔occlusore
                    // sovrapposizione dei due dischi: 1 quando l'occlusore ingloba il sole, sfuma alla tangenza
                    float f = 1.0 - smoothstep(abs(rhoSun - rhoOcc), rhoSun + rhoOcc, sigma);
                    // copertura massima: <1 se l'occlusore è angolarmente più piccolo del sole (anulare → sbiadita)
                    float peak = saturate((rhoOcc * rhoOcc) / (rhoSun * rhoSun));
                    alb *= 1.0 - f * peak * _EclipseStrength;
                }
            }

            // SATURAZIONE: modula verso il grigio (luminanza) o esalta. Ultimo passo sul colore.
            float luma = dot(alb, float3(0.2126, 0.7152, 0.0722));
            alb = lerp(float3(luma, luma, luma), alb, _Saturation);

            o.Albedo = alb;
        }
        ENDCG
    }

    FallBack "Diffuse"
}

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

        // --- tre suoli per banda di distanza ---
        _SoilTint ("Suolo: tinta/luminosità", Color) = (1.3, 1.3, 1.3, 1)
        _TileNear ("Suolo vicino (terra): ripetizioni", Float) = 180
        _TileMid  ("Suolo medio (sabbia): ripetizioni", Float) = 50
        _TileFar  ("Suolo lontano (fango): ripetizioni", Float) = 14
        _DistNear ("Banda vicino: fino a (m)", Float) = 45
        _DistMid  ("Banda medio: fino a (m)",  Float) = 130
        _DistFar  ("Banda lontano: pieno oltre (m)", Float) = 320
        // l'albedo si appiattisce a distanza verso questo colore medio → lontano PULITO (niente
        // chiazze d'albedo che fanno "cavolfiore"), indipendente dalla luminosità.
        _SoilMean ("Suolo: colore medio (lontano)", Color) = (0.34, 0.33, 0.29, 1)
        _FlatDist ("Suolo: distanza appiattimento (m)", Float) = 250
        _FlatStr  ("Suolo: forza appiattimento", Range(0,1)) = 0.9

        // --- regioni minerali: variazione di TINTA larga (non di luminosità) ---
        // base neutra + chiazze calde (A) e fredde (B) distinte: regioni vere, non una velatura.
        _MineralA ("Minerale: chiazze calde (ruggine)", Color) = (1.18, 0.92, 0.74, 1)
        _MineralB ("Minerale: chiazze fredde (ardesia)", Color) = (0.82, 0.92, 1.08, 1)
        _MineralFreq ("Minerale: scala regioni", Float) = 1.8
        _MineralStr  ("Minerale: forza", Range(0,1)) = 0.5

        _PeakStr  ("Forza cappucci vetta", Range(0,1)) = 0.5

        _RoughFreq  ("Scala zone rugose",     Float) = 0.8
        _RoughThresh ("Soglia zone rugose",   Range(0,1)) = 0.60
        _RoughBoost ("Rilievo extra zone rugose", Range(1,4)) = 1.8

        _BaseFreq   ("Scala rilievo (ottava base)", Float) = 0.25
        _DetailStr  ("Forza rilievo",         Range(0,1)) = 0.42
        // bias di mip sul rilievo: negativo = mip piu' nitida. Tenuto MITE (-0.2): ora il dettaglio
        // medio lo fa la GEOMETRIA (ottava 7), non serve spremere la texture, e un bias aggressivo
        // dava "rugosità che striscia" (sfarfallio) a volo radente.
        _ReliefBias ("Rilievo: bias mip (neg = nitido)", Range(-2,0)) = -0.2

        _GrainTiling ("Ripetizioni grana fine", Float) = 2400
        _GrainStr    ("Forza grana fine",     Range(0,1)) = 0.10
        _GrainDist   ("Distanza grana fine (m)", Float) = 14

        [NoScaleOffset] _ReliefMap ("Rilievo bakeato (ottave grosse)", 2D) = "black" {}
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
        float _GrainTiling, _GrainStr, _GrainDist;
        float _TileNear, _TileMid, _TileFar, _DistNear, _DistMid, _DistFar;
        float _FlatDist, _FlatStr, _PeakStr;
        sampler2D _ReliefMap;
        sampler2D _DetailNormal;
        sampler2D _SoilSand, _SoilMud, _SoilDirt;

        // Campiona un suolo due volte alla STESSA frequenza, ma il secondo campione RUOTATO
        // di 90°: rompe l'allineamento a griglia della piastrella. Importante che la seconda
        // frequenza resti ~uguale: un secondo campione più "largo" (bassa freq) creerebbe
        // chiazze a 50–200 m — esattamente l'artefatto "cavolfiore" da evitare.
        float3 soilSample(sampler2D tex, float2 uv, float tiling)
        {
            float3 a = tex2D(tex, uv * tiling).rgb;
            float2 ruv = float2(uv.y, -uv.x) * (tiling * 0.97) + 0.31;
            float3 b = tex2D(tex, ruv).rgb;
            return lerp(a, b, 0.4);
        }

        // versione a UN campione: per il fango lontano, già appiattito → il de-repeat è inutile lì
        float3 soilSample1(sampler2D tex, float2 uv, float tiling)
        {
            return tex2D(tex, uv * tiling).rgb;
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

            // --- gate del bump (solo ALU, niente texture) — calcolato SUBITO perché decide anche
            // se vale la pena LEGGERE il rilievo. Due assi:
            //   ANGOLO (grazeW): spento al limbo/sguardo radente → niente striature.
            //   DISTANZA (farW): svanisce 480-780 m, dove il texel del rilievo (0.4 m) va sub-pixel
            //     e mipperebbe in blob; lì il dettaglio lo fa la mesh (ottava 7, geometria netta).
            //   nearW: pieno da vicino a prescindere (gradevole a terra, sguardo ripido).
            float3 worldN  = normalize(mul((float3x3)unity_ObjectToWorld, N));
            float3 viewDir = normalize(_WorldSpaceCameraPos - IN.worldPos);
            float ndotv = dot(worldN, viewDir);
            float grazeW = smoothstep(0.18, 0.5, ndotv);
            // col quadtree la mesh si fa grossa a distanza: il bump bakeato (texture, senza LOD)
            // deve restare acceso a lungo per portare LUI il dettaglio sulla geometria coarse →
            // il pianeta resta ruvido in orbita e il salto di LOD si nota meno. Svanisce solo nello
            // spazio profondo (>~3.5 km), dove comunque mippa via da solo.
            float farW   = 1.0 - smoothstep(1500.0, 3500.0, dist);
            float nearW  = 1.0 - smoothstep(60.0, 140.0, dist);
            float gate = saturate(max(nearW, grazeW * farW));
            float nearF = saturate(1.0 - dist / 120.0);   // mottle avvallamenti: solo da vicino

            // rilievo bakeato: SALTATO quando non serve (pixel lontani o al limbo → bump spento e
            // mottle a zero). Toglie una lettura texture sui pixel del pianeta visto da lontano o
            // di taglio: è gran parte del calore "sprecato", invisibile a video.
            float reliefVal = 0.0;
            float3 G = float3(0.0, 0.0, 0.0);
            if (gate > 0.0 || nearF > 0.0)
            {
                float4 baked = tex2Dbias(_ReliefMap, float4(IN.texUV, 0.0, _ReliefBias));
                reliefVal = baked.x;
                G = baked.yzw * _BaseFreq;   // gradiente del rilievo grosso, spazio oggetto
            }

            // zone rugose: rare e grandi → value-noise a 1 ottava (vnoise), non fbm: a questa
            // scala il dettaglio in più non si vede e costa la metà.
            float rough = smoothstep(_RoughThresh, _RoughThresh + 0.12, vnoise(N * _RoughFreq + 5.1));
            float str = _DetailStr * lerp(1.0, _RoughBoost, rough);

            // --- colore: TRE SUOLI per BANDA DI DISTANZA (ognuno dove rende meglio) ---
            //   vicino  = terra bruna (Dirt01): sassolini, dettaglio a portata di mano;
            //   medio   = sabbia grigia: grana uniforme, pulita nella fascia media;
            //   lontano = fango oliva (Dirt09): macchie larghe morbide, leggono bene da quota.
            // Crossfade morbidi per distanza; ogni suolo a due scale (soilSample) anti-ripetizione.
            // Tinta tenuta bassa: superficie scura e lunare → la mesh bumpy illuminata NON fa
            // creste bianche/cavolfiore (quello nasceva dall'albedo troppo chiaro).
            float fNear = saturate(1.0 - dist / _DistNear);
            float fFar  = saturate((dist - _DistMid) / max(_DistFar - _DistMid, 1.0));
            float fMid  = saturate(1.0 - fNear - fFar);
            float wsum  = max(fNear + fMid + fFar, 1e-4);
            // appiattimento a distanza: l'albedo tende a un colore medio → da lontano resta solo
            // l'ombreggiatura della FORMA (mesh), niente chiazze d'albedo che fanno "cavolfiore".
            // Indipendente dalla luminosità: il colore medio può essere chiaro quanto serve.
            float flatF = saturate(dist / _FlatDist) * _FlatStr;
            float3 soil;
            if (dist < 300.0)
            {
                // sabbia (medio, de-repeat) + fango (lontano, 1 campione). La TERRA vicina costa due
                // campioni: la calcoliamo SOLO se siamo abbastanza vicini (a quota fNear=0 → saltata).
                soil = soilSample(_SoilSand, IN.texUV, _TileMid) * fMid
                     + soilSample1(_SoilMud, IN.texUV, _TileFar) * fFar;
                if (fNear > 0.001) soil += soilSample(_SoilDirt, IN.texUV, _TileNear) * fNear;
                soil = lerp(soil / wsum, _SoilMean.rgb, flatF);
            }
            else
            {
                // oltre 300 m l'albedo è già appiattito (>=90%): il ~10% di texture residuo è
                // invisibile → uso direttamente il colore medio e salto 2-3 letture texture/pixel.
                // È il grosso del risparmio quando il pianeta riempie lo schermo da lontano.
                soil = _SoilMean.rgb;
            }
            float3 alb = soil * _SoilTint.rgb;

            // --- regioni minerali: tinta larga (hue) da noise a bassa freq sulla sfera.
            // È COLORE, non luminosità → grandi zone di terreno "diverso" visibili a ogni
            // distanza (sopravvivono all'appiattimento) SENZA chiazze chiaro/scuro (quelle
            // facevano "cavolfiore"). Le tinte sono bilanciate in luminosità: spostano il
            // colore, non la luce → restano pulite anche su un pianeta luminoso.
            // UN solo value-noise: dove è alto → chiazza calda (A), dove è basso → fredda (B),
            // in mezzo neutro (grigio lunare). Regioni distinte con metà del costo di prima.
            float z = vnoise(N * _MineralFreq);
            float3 mineral = float3(1.0, 1.0, 1.0);
            mineral = lerp(mineral, _MineralB.rgb, smoothstep(0.45, 0.28, z));   // freddo, z basso
            mineral = lerp(mineral, _MineralA.rgb, smoothstep(0.55, 0.72, z));   // caldo, z alto
            alb *= lerp(float3(1.0, 1.0, 1.0), mineral, _MineralStr);

            // accenti procedurali sopra il bioma: cappucci chiari sulle vette, tinta roccia nelle
            // zone rugose. Tenui, per non cancellare la varietà di colore dei suoli.
            float t = saturate((h - (_BaseRadius - _Amplitude)) / (2.0 * _Amplitude));
            alb = lerp(alb, _PeakColor.rgb, smoothstep(0.74, 0.97, t) * _PeakStr);
            alb = lerp(alb, _RockColor.rgb, rough * 0.30);
            alb *= 1.0 + _MottleStr * reliefVal * nearF;   // avvallamenti piu' scuri, solo da vicino (nearF in cima)

            // --- normale: rilievo bakeato (gradiente proiettato sui tangenti della mesh T,B,
            // sopra la forma vera della Noise3D) + grana fine. Il gate e G sono già stati calcolati
            // in cima (lì decidevano se leggere il rilievo): qui si applicano soltanto.
            float2 nxy = float2(0.0, 0.0);
            if (gate > 0.0)
            {
                float3 T = normalize(IN.objT);
                float3 B = normalize(IN.objB);
                nxy = float2(-dot(G, T) * str, -dot(G, B) * str) * gate;
            }

            // Grana del suolo come NORMALE, una sola banda, ravvicinata. Il bump si usa solo
            // dove funziona: vicino e con lo sguardo ripido sul terreno. Oltre pochi metri, o a
            // luce radente, una normal map collassa (niente ombre). A media/alta distanza il
            // dettaglio lo dà il colore dei suoli, che si vede a qualunque angolo.
            float fine = 1.0 - smoothstep(_GrainDist * 0.5, _GrainDist, dist);
            if (fine > 0.0)
            {
                float3 dn = tex2D(_DetailNormal, IN.texUV * _GrainTiling).xyz * 2.0 - 1.0;
                nxy += dn.xy * (_GrainStr * fine);
            }
            o.Normal = normalize(float3(nxy, 1.0));

            o.Albedo = alb;
        }
        ENDCG
    }

    FallBack "Diffuse"
}

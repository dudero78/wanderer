Shader "Wanderer/PlanetProcedural"
{
    // Resa dell'anteprima GPU dell'editor (Tappe 1-3 del percorso "GPU per l'editor").
    // Disegna la superficie leggendo posizioni e normali DIRETTAMENTE da due StructuredBuffer riempiti dal
    // compute (PlanetHeight.compute), SENZA readback: il vertice indicizza il buffer con SV_VertexID.
    //
    // COLORE (Tappa 3): ricalcolato nel fragment dai parametri della RICETTA, NON da texture bakate. Scelta
    // architetturale (vedi CLAUDE.md / TODO): risoluzione infinita, zero bake all'avvio, GPU-first; il bake
    // resta utile solo per simulazioni costose (erosione/AO), non per il colore. Mirroring della catena di
    // Wanderer/PlanetBaked (suolo+macro, minerali, vette, bacini, MARE+saturazione), con fbm/n3_fbm fedeli a
    // Noise3D → il mare segue la geometria allagata. È la FONDAZIONE su cui crescerà il layering per pendenza/quota.
    Properties
    {
        _BaseRadius ("Raggio base", Float) = 500
        _Amplitude  ("Ampiezza", Float) = 45

        _SoilMean ("Suolo: colore base", Color) = (0.44, 0.44, 0.45, 1)
        _SoilTint ("Suolo: tinta", Color) = (1.0, 1.0, 1.02, 1)
        _MacroVar ("Variazione macro", Range(0,1)) = 0.45
        _MacroScale ("Variazione macro: scala", Float) = 5

        _MineralA ("Minerale: caldo", Color) = (1.18, 0.92, 0.74, 1)
        _MineralB ("Minerale: freddo", Color) = (0.82, 0.92, 1.08, 1)
        _MineralStr ("Minerale: forza", Range(0,1)) = 0.18
        _MineralScale ("Minerale: scala", Float) = 1.8

        _PeakColor ("Vette", Color) = (0.74, 0.76, 0.70, 1)
        _PeakStr ("Vette: forza", Range(0,1)) = 0.5

        _MariaColor ("Bacini: scurezza", Color) = (0.52, 0.52, 0.56, 1)
        _MariaScale ("Bacini: scala", Float) = 2.2
        _MariaStr ("Bacini: forza", Range(0,1)) = 0.7

        _SeaOn ("Mare attivo", Float) = 0
        _SeaLevel ("Mare: raggio pelo", Float) = 0
        _SeaColor ("Mare: colore", Color) = (0.13, 0.33, 0.52, 1)
        _SeaSat ("Mare: saturazione", Range(0,2)) = 1
        _SeaRough ("Mare: rugosità (m)", Float) = 0
        _SeaRoughScale ("Mare: scala rugosità", Float) = 3
        _SeaForma ("Mare: forma fondale", Range(-1,1)) = 0
        _SeaSeed ("Mare: seme", Float) = 4242

        _Saturation ("Saturazione", Range(0,2)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            // Cull Off: il verso dei triangoli dipende dall'orientamento degli assi delle 6 facce del cubo.
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5          // StructuredBuffer nel vertex shader (Metal lo regge)
            #include "UnityCG.cginc"
            #include "PlanetNoise.cginc"

            StructuredBuffer<float> _VPos;   // 3 float per vertice (x,y,z), buffer piatto
            StructuredBuffer<float> _VNrm;

            float _BaseRadius, _Amplitude;
            float4 _SoilMean, _SoilTint, _MineralA, _MineralB, _PeakColor, _MariaColor, _SeaColor;
            float _MacroVar, _MacroScale, _MineralStr, _MineralScale, _PeakStr, _MariaScale, _MariaStr;
            float _SeaOn, _SeaLevel, _SeaSat, _SeaRough, _SeaRoughScale, _SeaForma, _SeaSeed, _Saturation;
            float3 _SunDir, _SunColor, _Ambient;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 nrm : TEXCOORD0;
                float3 lp  : TEXCOORD1;   // posizione in spazio oggetto (= mondo, pianeta all'origine)
            };

            v2f vert(uint vid : SV_VertexID)
            {
                v2f o;
                float3 p = float3(_VPos[vid * 3], _VPos[vid * 3 + 1], _VPos[vid * 3 + 2]);
                float3 n = float3(_VNrm[vid * 3], _VNrm[vid * 3 + 1], _VNrm[vid * 3 + 2]);
                o.pos = UnityObjectToClipPos(p);
                o.nrm = UnityObjectToWorldNormal(n);
                o.lp = p;
                return o;
            }

            // modella il rumore centrato 'c' secondo 'forma' (= SeaTerrainLayer.Shape / PlanetBaked.SeaShape)
            float SeaShape(float c, float forma)
            {
                float ridged = 1.0 - 2.0 * abs(c);
                float billow = 2.0 * abs(c) - 1.0;
                return (forma < 0.0) ? lerp(ridged, c, forma + 1.0) : lerp(c, billow, forma);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float3 P = IN.lp;
                float h = max(length(P), 1e-4);
                float3 sdir = P / h;        // direzione radiale (spazio oggetto) per i campi di colore

                // MARE: ricostruisco il pelo (eventualmente rugoso) come SeaTerrainLayer → tingo dove h È quel pelo
                // (i crateri scavati DOPO il mare restano sotto e asciutti). Stesso n3_fbm della geometria.
                float seaSurf = _SeaLevel;
                if (_SeaRough > 0.0)
                {
                    float cc = (n3_fbm(sdir * _SeaRoughScale, 4, 2.0, 0.5, (int)_SeaSeed) - 0.5) * 2.0;
                    seaSurf += SeaShape(cc, _SeaForma) * _SeaRough;
                }
                float seaMask = (_SeaOn > 0.5) ? (1.0 - smoothstep(2.0, 4.0, abs(h - seaSurf))) : 0.0;

                // suolo: colore base × variazione MACRO a bassa frequenza (campo dunale) — procedurale, niente texture
                float macroV = fbm(sdir * _MacroScale);
                float3 alb = _SoilMean.rgb * lerp(1.0, 0.78 + macroV * 0.44, _MacroVar) * _SoilTint.rgb;

                // regioni minerali: velatura di TINTA larga (calda/fredda), bassa frequenza
                float z = fbm(sdir * _MineralScale);
                float3 mineral = float3(1.0, 1.0, 1.0);
                mineral = lerp(mineral, _MineralB.rgb, smoothstep(0.45, 0.28, z));
                mineral = lerp(mineral, _MineralA.rgb, smoothstep(0.55, 0.72, z));
                alb *= lerp(float3(1.0, 1.0, 1.0), mineral, _MineralStr);

                // cappucci chiari sulle creste (quota normalizzata)
                float t = saturate((h - (_BaseRadius - _Amplitude)) / (2.0 * _Amplitude));
                alb = lerp(alb, _PeakColor.rgb, smoothstep(0.74, 0.97, t) * _PeakStr);

                // bacini scuri (dove è basso) in ALCUNE regioni → il colore segue il rilievo
                float low = 1.0 - smoothstep(0.16, 0.46, t);
                float region = smoothstep(0.42, 0.64, fbm(sdir * _MariaScale));
                alb = lerp(alb, alb * _MariaColor.rgb, low * region * _MariaStr);

                // tinta del MARE (saturazione propria), copre suolo/crateri sommersi
                float seaLuma = dot(_SeaColor.rgb, float3(0.2126, 0.7152, 0.0722));
                float3 seaCol = lerp(float3(seaLuma, seaLuma, seaLuma), _SeaColor.rgb, _SeaSat);
                alb = lerp(alb, seaCol, seaMask);

                // saturazione finale
                float luma = dot(alb, float3(0.2126, 0.7152, 0.0722));
                alb = lerp(float3(luma, luma, luma), alb, _Saturation);

                // luce: normale geometrica del vertice. Sul mare NON va appiattita: con la rugosità il pelo È
                // geometria ondulata e la normale la cattura (come PlanetBaked, che sul mare tiene la normale mesh).
                float3 nrm = normalize(IN.nrm);
                float ndl = saturate(dot(nrm, _SunDir));
                float3 col = alb * (ndl * _SunColor + _Ambient);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}

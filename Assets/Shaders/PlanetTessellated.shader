Shader "Wanderer/PlanetTessellated"
{
    // Stage 2 della rifondazione terreno: la GEOMETRIA viene dislocata dalla HEIGHTMAP bakeata, con
    // TASSELLAZIONE GPU distance-based → vicino alla camera la mesh si sottodivide e segue la heightmap a
    // risoluzione fine (crateri come geometria VERA, niente normalmap finta); lontano resta la mesh base
    // (economica). La normale è calcolata dai VICINI campionati sulla heightmap (esatta, per costruzione →
    // niente tangent-frame da indovinare, il punto fragile del vecchio approccio). Il colore è come
    // Wanderer/PlanetBaked. Il crater-normal-map NON si usa qui: i crateri sono geometria.
    //
    // Usato solo quando esistono gli asset bakeati con heightmap (PlanetBaker.BuildMaterial). Fallback =
    // Wanderer/PlanetBaked (liscio). Mesh base + walker invariati (il giocatore segue SampleHeight).
    Properties
    {
        _PeakColor  ("Vette (cappucci chiari)", Color) = (0.74, 0.76, 0.70, 1)
        _PeakStr  ("Forza cappucci vetta", Range(0,1)) = 0.5
        _BaseRadius ("Raggio base",      Float) = 500
        _Amplitude  ("Ampiezza terreno", Float) = 30
        _SoilTint ("Suolo: tinta/luminosità", Color) = (1.0, 1.0, 1.02, 1)
        _SoilMean ("Suolo: colore base", Color) = (0.44, 0.44, 0.45, 1)
        _MacroVar ("Variazione macro (campo dunale)", Range(0,1)) = 0.45
        _MacroScale ("Variazione macro: scala", Float) = 5
        _SandDetail ("Suolo: contrasto grana (0 = liscio)", Range(0,1)) = 0.18
        _MineralA ("Minerale: chiazze calde", Color) = (1.18, 0.92, 0.74, 1)
        _MineralB ("Minerale: chiazze fredde", Color) = (0.82, 0.92, 1.08, 1)
        _MineralStr  ("Minerale: forza", Range(0,1)) = 0.18
        _GrainStr    ("Micro-grana (normale, solo vicino)", Range(0,1)) = 0.06
        _DetailScale ("Dettaglio: ripetizioni sulla faccia", Float) = 320

        [NoScaleOffset] _MaskMap ("Maschere bakeate (R=minerali)", 2D) = "gray" {}
        [NoScaleOffset] _DetailNormal ("Grana suolo (normal tileable)", 2D) = "bump" {}
        [NoScaleOffset] _SoilSand ("Suolo: foto diffuse", 2D) = "gray" {}
        [NoScaleOffset] _CraterNormalMap ("Crateri (non usata qui)", 2D) = "bump" {}
        [NoScaleOffset] _HeightMap ("Heightmap (displacement, m)", 2D) = "black" {}

        // tassellazione: max suddivisioni vicino, sfuma a 1 oltre _TessFar. Cap basso per restare bounded.
        _TessMax  ("Tessellazione: max suddivisioni", Range(1,16)) = 4
        _TessNear ("Tessellazione: distanza tess piena (m)", Float) = 12
        _TessFar  ("Tessellazione: distanza tess minima (m)", Float) = 300
        // passo (in UV) per la differenza centrale della normale geometrica dalla heightmap.
        _NormalStep ("Normale: passo UV", Float) = 0.001
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Lambert vertex:disp tessellate:tessEdge nolightmap
        #pragma target 4.6
        #include "Tessellation.cginc"

        fixed4 _PeakColor, _SoilTint, _SoilMean, _MineralA, _MineralB;
        float _MineralStr, _PeakStr, _BaseRadius, _Amplitude;
        float _GrainStr, _DetailScale, _MacroVar, _MacroScale, _SandDetail;
        float _TessMax, _TessNear, _TessFar, _NormalStep;
        float4 _FaceUp, _FaceAxisA, _FaceAxisB;
        sampler2D _MaskMap, _DetailNormal, _SoilSand, _HeightMap;

        struct appdata
        {
            float4 vertex : POSITION;
            float4 tangent : TANGENT;
            float3 normal : NORMAL;
            float2 texcoord : TEXCOORD0;
            float2 texcoord1 : TEXCOORD1;
            float2 texcoord2 : TEXCOORD2;
        };

        // (tx,ty) faccia → direzione unitaria sulla sfera (stessa formula di PlanetMeshBuilder.ParamToDir).
        float3 paramToDir(float2 t)
        {
            float3 p = _FaceUp.xyz + (t.x - 0.5) * 2.0 * _FaceAxisA.xyz + (t.y - 0.5) * 2.0 * _FaceAxisB.xyz;
            return normalize(p);
        }

        float4 tessEdge(appdata v0, appdata v1, appdata v2)
        {
            return UnityDistanceBasedTess(v0.vertex, v1.vertex, v2.vertex, _TessNear, _TessFar, _TessMax);
        }

        // disloca il vertice sulla superficie esatta della heightmap e calcola la normale GEOMETRICA dai vicini.
        void disp(inout appdata v)
        {
            float2 uv = v.texcoord.xy;
            float e = _NormalStep;
            float H  = tex2Dlod(_HeightMap, float4(uv, 0, 0)).r;
            float Hu = tex2Dlod(_HeightMap, float4(uv + float2(e, 0), 0, 0)).r;
            float Hv = tex2Dlod(_HeightMap, float4(uv + float2(0, e), 0, 0)).r;

            float3 d  = paramToDir(uv);
            float3 du = paramToDir(uv + float2(e, 0));
            float3 dv = paramToDir(uv + float2(0, e));
            float3 P  = d  * (_BaseRadius + H);
            float3 Pu = du * (_BaseRadius + Hu);
            float3 Pv = dv * (_BaseRadius + Hv);

            float3 nrm = normalize(cross(Pu - P, Pv - P));
            if (dot(nrm, d) < 0.0) nrm = -nrm;       // sempre verso l'esterno

            v.vertex.xyz = P;
            v.normal = nrm;
            v.tangent = float4(normalize(Pu - P), -1.0);
        }

        struct Input
        {
            float2 uv_MaskMap;
            float3 worldPos;
        };

        void surf(Input IN, inout SurfaceOutput o)
        {
            float2 uv = IN.uv_MaskMap;
            float H = tex2D(_HeightMap, uv).r;
            float h = _BaseRadius + H;
            float dist = distance(IN.worldPos, _WorldSpaceCameraPos);

            float z = tex2D(_MaskMap, uv).r;

            // colore: come PlanetBaked (suolo liscio + variazione macro + grana + minerali + cappucci).
            float macroV = tex2D(_SoilSand, uv * _MacroScale).g;
            float3 sand = _SoilMean.rgb * lerp(1.0, 0.78 + macroV * 0.44, _MacroVar);
            float detW = (1.0 - smoothstep(45.0, 120.0, dist)) * _SandDetail;
            if (detW > 0.0)
            {
                float3 g = tex2Dbias(_SoilSand, float4(uv * _DetailScale, 0.0, 2.0)).rgb;
                float gm = max(dot(g, float3(0.333, 0.333, 0.333)), 0.04);
                sand *= lerp(float3(1.0, 1.0, 1.0), g / gm, detW);
            }
            float3 alb = sand * _SoilTint.rgb;

            float3 mineral = float3(1.0, 1.0, 1.0);
            mineral = lerp(mineral, _MineralB.rgb, smoothstep(0.45, 0.28, z));
            mineral = lerp(mineral, _MineralA.rgb, smoothstep(0.55, 0.72, z));
            alb *= lerp(float3(1.0, 1.0, 1.0), mineral, _MineralStr);

            float t = saturate((h - (_BaseRadius - _Amplitude)) / (2.0 * _Amplitude));
            alb = lerp(alb, _PeakColor.rgb, smoothstep(0.74, 0.97, t) * _PeakStr);

            // normale: SOLO un soffio di micro-grana da vicino. I crateri sono nella GEOMETRIA (disp), non qui.
            float2 nxy = float2(0.0, 0.0);
            float nW = (1.0 - smoothstep(4.0, 13.0, dist)) * _GrainStr;
            if (nW > 0.0)
                nxy += (tex2D(_DetailNormal, uv * _DetailScale).xy * 2.0 - 1.0) * nW;

            o.Normal = normalize(float3(nxy, 1.0));   // base (0,0,1) = normale GEOMETRICA dislocata da disp
            o.Albedo = alb;
        }
        ENDCG
    }

    FallBack "Diffuse"
}

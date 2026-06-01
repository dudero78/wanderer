Shader "Wanderer/PlanetBake"
{
    // Bake del rilievo in "spazio texture": disegna la mesh di una faccia usando le sue UV
    // come posizione su schermo (uv -> clip), così ogni texel della RenderTexture riceve il
    // valore calcolato ESATTAMENTE nel punto della mesh che gli corrisponde. Usando la mesh
    // vera (con la sua posizione gia' spostata in altezza) il bake e' coerente al 100% con la
    // forma del pianeta: niente ricostruzione di coordinate, niente drift.
    //
    // Output (RGBAHalf, valori con segno):
    //   R   = valore del rilievo  (per le ombreggiature di colore degli avvallamenti)
    //   GBA = gradiente analitico del rilievo (per ricostruire la normale, leggero, a runtime)
    //
    // Questo e' SOLO la parte costosa (6 ottave di Perlin con derivate). Il colore e le zone
    // rugose restano procedurali nello shader di superficie: sono gia' economici.
    // NOTA: si bakeano SOLO le ottave grosse (default 3). Le fini restano procedurali nello
    // shader di superficie, perché una texture a risoluzione fissa le sfocherebbe da vicino.
    Properties
    {
        _BaseFreq ("Scala rilievo (ottava base)", Float) = 0.25
        _BakeOct  ("Ottave grosse da bakeare",    Float) = 3.0
        _MineralFreq ("Maschera: scala regioni minerali", Float) = 1.8
        _RoughFreq   ("Maschera: scala zone rugose",      Float) = 0.8
    }
    SubShader
    {
        // niente cull/depth: stiamo riempiendo una texture, non una scena 3D
        Cull Off
        ZTest Always
        ZWrite Off

        // vert condiviso dai due pass: srotola la mesh-faccia sul quadrato della texture.
        CGINCLUDE
        #include "UnityCG.cginc"
        #include "PlanetNoise.cginc"
        struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
        struct v2f     { float4 pos : SV_POSITION; float3 localPos : TEXCOORD0; };
        v2f vert(appdata v)
        {
            v2f o;
            float2 p = v.uv * 2.0 - 1.0;
            #if UNITY_UV_STARTS_AT_TOP
                p.y = -p.y;
            #endif
            o.pos = float4(p, 0.0, 1.0);
            o.localPos = v.vertex.xyz;   // posizione oggetto GIA' spostata in altezza (la mesh vera)
            return o;
        }
        ENDCG

        // PASS 0 — rilievo: R = valore, GBA = gradiente (RGBAHalf).
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.0
            float _BaseFreq, _BakeOct;
            float4 frag(v2f i) : SV_Target { return fbmRelief(i.localPos * _BaseFreq, _BakeOct); }
            ENDCG
        }

        // PASS 1 — maschere (sostituiscono i vnoise per-pixel nello shader di superficie):
        //   R = rumore regioni minerali, G = rumore zone rugose. Valori grezzi: lo shader di
        //   superficie applica soglie/smoothstep (così le manopole restano vive). Bassa frequenza
        //   → bastano pochi texel, e si toglie il calore di 2 rumori procedurali per pixel.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.0
            float _MineralFreq, _RoughFreq;
            float4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.localPos);
                float mineral = vnoise(N * _MineralFreq);
                float rough   = vnoise(N * _RoughFreq + 5.1);
                return float4(mineral, rough, 0.0, 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}

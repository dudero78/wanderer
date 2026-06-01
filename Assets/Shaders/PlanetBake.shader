Shader "Wanderer/PlanetBake"
{
    // Bake della MASCHERA delle regioni minerali in "spazio texture": disegna la mesh di una faccia
    // usando le sue UV come posizione su schermo (uv -> clip), così ogni texel della RenderTexture
    // riceve il valore calcolato ESATTAMENTE nel punto della mesh che gli corrisponde. Usando la
    // mesh vera (con la sua posizione gia' spostata in altezza) il bake e' coerente al 100% con la
    // forma del pianeta: niente ricostruzione di coordinate, niente drift.
    //
    // Output (ARGB32): R = rumore regioni minerali, G = rumore zone rugose. Valori grezzi: lo shader
    // di superficie applica soglie/smoothstep (così le manopole restano vive). Bassa frequenza →
    // bastano pochi texel, e si toglie il rumore procedurale per-pixel a ogni frame.
    Properties
    {
        _MineralFreq ("Maschera: scala regioni minerali", Float) = 1.8
        _RoughFreq   ("Maschera: scala zone rugose",      Float) = 0.8
    }
    SubShader
    {
        // niente cull/depth: stiamo riempiendo una texture, non una scena 3D
        Cull Off
        ZTest Always
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.0
            #include "UnityCG.cginc"
            #include "PlanetNoise.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos : SV_POSITION; float3 localPos : TEXCOORD0; };

            // srotola la mesh-faccia sul quadrato della texture: uv -> clip space
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

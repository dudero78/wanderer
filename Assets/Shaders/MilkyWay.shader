Shader "Wanderer/MilkyWay"
{
    // Banda della Via Lattea (+ tenue alone di stelle non risolte) come texture equirettangolare EQUATORIALE
    // (NASA Deep Star Map 2020, pubblico dominio) mappata sull'interno di una sfera orientata col frame equatoriale
    // → allineata con le stelle del catalogo. Additiva sul nero (Blend One One), dietro i punti-stella.
    // Black-point sottrattivo: il vuoto resta NERO e solo la banda/le zone luminose aggiungono luce (niente velo grigio).
    Properties
    {
        _MainTex  ("Cielo equirettangolare", 2D) = "black" {}
        _Strength ("Intensità", Float) = 0.6
        _Boost    ("Guadagno pre-soglia", Float) = 1.6
        _Floor    ("Black-point (sottratto)", Float) = 0.06
        _Tint     ("Tinta", Color) = (0.85, 0.9, 1.0, 1)
        _FlipU    ("Specchia U (0/1)", Float) = 0
        _OffsetU  ("Offset U", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Background+5" "RenderType"="Background" "IgnoreProjector"="True" "PreviewType"="Skybox" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Front     // sfera vista da DENTRO
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f     { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };

            sampler2D _MainTex;
            float _Strength, _Boost, _Floor, _FlipU, _OffsetU;
            fixed4 _Tint;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float u = lerp(v.uv.x, 1.0 - v.uv.x, _FlipU);
                o.uv = float2(frac(u + _OffsetU), v.uv.y);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed3 c = tex2D(_MainTex, i.uv).rgb;
                c = max(c * _Boost - _Floor, 0.0);     // black-point: il vuoto resta nero
                return fixed4(c * _Strength * _Tint.rgb, 1.0);
            }
            ENDCG
        }
    }
}

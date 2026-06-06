Shader "Wanderer/StarGlow"
{
    // Alone di luce additivo attorno a una stella: un quad billboard con caduta radiale morbida (centro luminoso →
    // bordo trasparente). Additivo (Blend One One) → AGGIUNGE luce sul nero come un bagliore vero, niente bordo scuro.
    // ZTest Always: disegnato SOPRA la stella e lo spazio (è solo un glow), ZWrite Off (non scrive profondità).
    Properties
    {
        _Color ("Colore", Color) = (1, 0.9, 0.6, 1)
        _Strength ("Intensità", Float) = 1.0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        ZTest Always
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f     { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
            fixed4 _Color; float _Strength;

            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 d = i.uv * 2.0 - 1.0;       // -1..1 sul quad
                float r = length(d);
                float a = saturate(1.0 - r);
                a = a * a * a;                      // caduta morbida (alone concentrato al centro, lunga coda tenue)
                return fixed4(_Color.rgb * (a * _Strength), 1.0);
            }
            ENDCG
        }
    }
}

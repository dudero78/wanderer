Shader "Wanderer/ConstellationLine"
{
    // Linee delle costellazioni LISCE: strisce a spessore COSTANTE in pixel (espansione in spazio schermo nel vertex,
    // come Wanderer/OrbitLine) con sezione morbida (nucleo + alone) → bordo anti-aliasato per costruzione, niente
    // seghettature. Additive, tenui. Compaiono/svaniscono via _Alpha (fade su toggle).
    Properties
    {
        _Color      ("Colore", Color) = (0.45, 0.7, 0.95, 1)
        _PixelWidth ("Spessore (px)", Float) = 2.2
        _Core       ("Nitidezza nucleo", Float) = 6.0
        _Alpha      ("Opacità", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Background+18" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; };
            struct v2f     { float4 pos:SV_POSITION; float side:TEXCOORD0; };
            fixed4 _Color; float _PixelWidth, _Core, _Alpha;

            v2f vert(appdata v)
            {
                v2f o;
                float3 ahead = v.vertex.xyz + v.normal * (0.002 * length(v.vertex.xyz) + 0.001);
                float4 c0 = UnityObjectToClipPos(v.vertex.xyz);
                float4 c1 = UnityObjectToClipPos(ahead);

                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 d = (c1.xy / c1.w) - (c0.xy / c0.w);
                d.x *= aspect; d = normalize(d + 1e-6);
                float2 perp = float2(-d.y, d.x); perp.x /= aspect;

                float side = v.uv.y * 2.0 - 1.0;
                float2 off = perp * side * (_PixelWidth / _ScreenParams.y);
                if (c0.w > 1e-4 && c1.w > 1e-4) c0.xy += off * c0.w;
                o.pos = c0; o.side = side;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float a = exp(-i.side * i.side * _Core);   // sezione morbida → bordo anti-aliasato
                return fixed4(_Color.rgb * (a * _Alpha), 1.0);
            }
            ENDCG
        }
    }
}

Shader "Wanderer/MilkyWay"
{
    // Via Lattea (+ alone di stelle non risolte) come PASS A SCHERMO INTERO: un quad che copre lo schermo e ricostruisce
    // per ogni pixel la DIREZIONE di vista nel mondo (interpolando i 4 raggi d'angolo passati dalla CPU), la converte in
    // coordinate EQUATORIALI (inverso dell'allineamento eclittica) e campiona la texture equirettangolare equatoriale
    // (NASA Deep Star Map 2020, pubblico dominio). Niente sfera → nessun problema di orientamento/culling.
    // Additivo, dietro le stelle (queue Background+5). Black-point: il vuoto resta nero.
    Properties
    {
        _MainTex  ("Cielo equirettangolare", 2D) = "black" {}
        _Strength ("Intensità", Float) = 0.42
        _Boost    ("Guadagno pre-soglia", Float) = 1.15
        _Floor    ("Black-point (sottratto)", Float) = 0.22
        _Tint     ("Tinta", Color) = (0.9, 0.94, 1.0, 1)
        _FlipU    ("Specchia U (0/1)", Float) = 0
        _OffsetU  ("Offset U", Float) = 0
        _ZoomBoost("Risalto sullo zoom", Float) = 0.25
    }
    SubShader
    {
        Tags { "Queue"="Background+5" "RenderType"="Background" "IgnoreProjector"="True" "PreviewType"="Skybox" }
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

            struct appdata { float4 vertex:POSITION; float4 uv:TEXCOORD0; };   // uv.xyz = raggio mondo dell'angolo
            struct v2f     { float4 pos:SV_POSITION; float3 ray:TEXCOORD0; };

            sampler2D _MainTex;
            float _Strength, _Boost, _Floor, _FlipU, _OffsetU, _ZoomBoost;
            fixed4 _Tint;
            float _SkyZoom;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = float4(v.vertex.xy, 1.0, 1.0);   // angoli in clip space: copre lo schermo, profondità = far
                o.ray = v.uv.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.ray);

                // game → equatoriale (inverso dell'allineamento eclittica, ε = 23.439°)
                const float ce = 0.917482, se = 0.397777;
                float ex = d.x;
                float ey = d.z * ce - d.y * se;
                float ez = d.z * se + d.y * ce;

                float ra = atan2(ey, ex);                 // -π..π
                float dec = asin(clamp(ez, -1.0, 1.0));   // -π/2..π/2
                float u = ra * 0.1591549;                 // /(2π)
                u = lerp(u, -u, _FlipU);
                float2 uv = float2(frac(u + _OffsetU + 1.0), dec * 0.3183099 + 0.5);

                fixed3 c = tex2D(_MainTex, uv).rgb;
                c = max(c * _Boost - _Floor, 0.0);
                float zoom = 1.0 + (max(_SkyZoom, 1.0) - 1.0) * _ZoomBoost;   // un filo più luminosa zoomando
                return fixed4(c * _Strength * zoom * _Tint.rgb, 1.0);
            }
            ENDCG
        }
    }
}

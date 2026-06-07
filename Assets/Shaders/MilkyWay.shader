Shader "Wanderer/MilkyWay"
{
    // Via Lattea (texture equirettangolare EQUATORIALE, NASA Deep Star Map) su una SFERA WATERTIGHT (nessuna cucitura
    // geometrica: il wrap in RA usa il modulo, vertici condivisi). Le UV sono calcolate PER-PIXEL dalla direzione
    // (niente colonna di cucitura duplicata → niente "riga nera" sul meridiano). Il salto della atan2 al meridiano è
    // gestito con gradienti CONTINUI (tex2Dgrad) → niente artefatto neanche di mipmap. Additiva, dietro le stelle.
    Properties
    {
        _MainTex  ("Cielo equirettangolare", 2D) = "black" {}
        // Il fondo è già stato azzerato NELLA texture (StarData/process_milkyway.py sottrae il cielo vuoto) → il vuoto è
        // NERO a prescindere dal guadagno. Qui resta solo da amplificare la banda. _Floor minimo = pulizia del residuo.
        _Strength ("Intensità", Float) = 0.55
        _Boost    ("Guadagno", Float) = 3.0
        _Floor    ("Pulizia residuo", Float) = 0.01
        _Tint     ("Tinta", Color) = (0.9, 0.94, 1.0, 1)
        _OffsetU  ("Offset U (rotazione fine)", Float) = 0
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

            struct appdata { float4 vertex:POSITION; };
            struct v2f     { float4 pos:SV_POSITION; float3 dir:TEXCOORD0; };

            sampler2D _MainTex;
            float _Strength, _Boost, _Floor, _OffsetU;
            fixed4 _Tint;

            v2f vert(appdata v)
            {
                v2f o;
                // skybox all'infinito: solo rotazione camera, coordinate oggetto piccole (niente tremolio lontano dall'origine)
                float3 wd = mul((float3x3)unity_ObjectToWorld, v.vertex.xyz);
                o.pos = mul(UNITY_MATRIX_P, float4(mul((float3x3)UNITY_MATRIX_V, wd), 1.0));
                o.dir = v.vertex.xyz;   // direzione in spazio oggetto = frame di gioco (skyRoot ha rotazione identità)
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                // game → equatoriale (inverso dell'allineamento eclittica, ε = 23.439°)
                const float ce = 0.917482, se = 0.397777;
                float ex = d.x;
                float ey = d.z * ce - d.y * se;
                float ez = d.z * se + d.y * ce;

                float uRaw = atan2(ey, ex) * 0.1591549;          // RA/(2π): mappa u=RA/360 (come il bake originale). NIENTE +0.5
                                                                 // (spostava la banda di 180° → centro galattico fuori dal Sagittario)
                float v = asin(clamp(ez, -1.0, 1.0)) * 0.3183099 + 0.5;

                // gradienti CONTINUI: ddx/ddy della uRaw, col salto del meridiano corretto (|d|>0.5 → era un wrap)
                float dux = ddx(uRaw), duy = ddy(uRaw);
                if (abs(dux) > 0.5) dux -= sign(dux);
                if (abs(duy) > 0.5) duy -= sign(duy);
                float dvx = ddx(v), dvy = ddy(v);

                float2 uv = float2(frac(uRaw + _OffsetU), v);
                fixed3 c = tex2Dgrad(_MainTex, uv, float2(dux, dvx), float2(duy, dvy)).rgb;

                c = max(c * _Boost - _Floor, 0.0);
                return fixed4(c * _Strength * _Tint.rgb, 1.0);
            }
            ENDCG
        }
    }
}

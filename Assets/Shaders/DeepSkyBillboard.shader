Shader "Wanderer/DeepSkyBillboard"
{
    // Oggetti del cielo profondo (galassie, nebulose, ammassi) come billboard TIPIZZATI con sprite dall'atlante 2×2
    // (forma per tipo, con FINESTRA radiale → alpha 0 sul bordo, mai un quadrato). Tinta per-vertice. Dimensione dal
    // RAGGIO ANGOLARE → crescono restringendo il campo (binocolo/telescopio): una macchia che "si risolve" zoomando.
    // Molto tenui a occhio nudo (solo le poche grandi/luminose) → EMERGONO con lo zoom.
    Properties
    {
        _Atlas      ("Atlante (2×2)", 2D) = "black" {}
        _DsoM0      ("Magnitudine di riferimento", Float) = 5.5
        _DsoExposure("Esposizione", Float) = 1.0
        _DsoGain    ("Guadagno tone-map", Float) = 0.8
        _DsoZoomPow ("Risalto sullo zoom", Float) = 0.85
        _MinPx      ("Dimensione minima (px)", Float) = 2.5
        _MaxPx      ("Dimensione massima (px)", Float) = 6000.0
    }
    SubShader
    {
        Tags { "Queue"="Background+12" "RenderType"="Background" "IgnoreProjector"="True" "PreviewType"="Skybox" }
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

            struct appdata { float4 vertex:POSITION; float4 uv:TEXCOORD0; float4 uv1:TEXCOORD1; fixed4 color:COLOR; };
            struct v2f     { float4 pos:SV_POSITION; float2 auv:TEXCOORD0; fixed3 col:TEXCOORD1; };

            sampler2D _Atlas;
            float _DsoM0, _DsoExposure, _DsoGain, _DsoZoomPow, _MinPx, _MaxPx;
            float _SkyZoom, _SkyPxScale, _SkyTanHalfFov;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(float4(v.vertex.xyz, 1));

                float radArcmin = v.uv.z, mag = v.uv.w, type = v.uv1.x;
                float zoom = max(_SkyZoom, 1.0);
                float pxScale = _SkyPxScale <= 0.0 ? 1.0 : _SkyPxScale;

                float radRad = radArcmin / 60.0 * 0.0174533;
                float px = clamp(radRad * (_ScreenParams.y * 0.5) / max(_SkyTanHalfFov, 1e-4), _MinPx, _MaxPx) * pxScale;

                float I = pow(10.0, 0.4 * (_DsoM0 - mag)) * _DsoExposure * pow(zoom, _DsoZoomPow);
                float lum = 1.0 - exp(-I * _DsoGain);

                float tile = min(type, 3.0);
                float2 t = float2(fmod(tile, 2.0), floor(tile / 2.0));
                float2 local = v.uv.xy * 0.5 + 0.5;
                o.auv = (t + local) * 0.5;

                o.col = v.color.rgb * lum;
                o.pos.xy += v.uv.xy * px * (2.0 / _ScreenParams.xy) * o.pos.w;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed a = tex2D(_Atlas, i.auv).a;
                return fixed4(i.col * a, 1.0);
            }
            ENDCG
        }
    }
}

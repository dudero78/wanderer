Shader "Wanderer/DeepSkyBillboard"
{
    // Oggetti del cielo profondo (galassie, nebulose, ammassi) come MACCHIE MORBIDE tipizzate (tinta per-vertice).
    // Forma procedurale nel fragment (caduta radiale → cerchio morbido, MAI un quadrato). Dimensione dal RAGGIO
    // ANGOLARE → crescono restringendo il campo (binocolo/telescopio): una macchia che "si risolve" zoomando.
    // Molto TENUI a occhio nudo (solo le poche grandi e luminose, tipo Andromeda, si intravedono) → EMERGONO con lo zoom.
    Properties
    {
        _DsoM0      ("Magnitudine di riferimento", Float) = 5.0
        _DsoExposure("Esposizione", Float) = 1.0
        _DsoGain    ("Guadagno tone-map", Float) = 0.8
        _DsoZoomPow ("Risalto sullo zoom", Float) = 0.8
        _MinPx      ("Dimensione minima (px)", Float) = 2.0
        _MaxPx      ("Dimensione massima (px)", Float) = 5000.0
        _Softness   ("Morbidezza bordo", Float) = 2.0
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
            struct v2f     { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; fixed3 col:TEXCOORD1; float core:TEXCOORD2; };

            float _DsoM0, _DsoExposure, _DsoGain, _DsoZoomPow, _MinPx, _MaxPx, _Softness;
            float _SkyZoom, _SkyPxScale, _SkyTanHalfFov;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(float4(v.vertex.xyz, 1));

                float radArcmin = v.uv.z;
                float mag = v.uv.w;
                float type = v.uv1.x;
                float zoom = max(_SkyZoom, 1.0);
                float pxScale = _SkyPxScale <= 0.0 ? 1.0 : _SkyPxScale;

                // raggio angolare → pixel: cresce restringendo la FOV (zoom)
                float radRad = radArcmin / 60.0 * 0.0174533;
                float px = clamp(radRad * (_ScreenParams.y * 0.5) / max(_SkyTanHalfFov, 1e-4), _MinPx, _MaxPx) * pxScale;

                // luminosità dal flusso integrato, fortemente legata allo zoom (a occhio nudo quasi invisibili)
                float I = pow(10.0, 0.4 * (_DsoM0 - mag)) * _DsoExposure * pow(zoom, _DsoZoomPow);
                float lum = 1.0 - exp(-I * _DsoGain);

                // ammassi/globulari (type 1,2): nucleo più marcato; galassie/nebulose: diffuse
                o.core = (type >= 0.5 && type <= 2.5) ? 1.0 : 0.0;
                o.col = v.color.rgb * lum;
                o.uv = v.uv.xy;
                o.pos.xy += v.uv.xy * px * (2.0 / _ScreenParams.xy) * o.pos.w;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float r = length(i.uv);
                float a = saturate(1.0 - r);
                a = pow(a, _Softness);                       // bordo morbido → cerchio, mai quadrato
                a += i.core * 0.6 * exp(-r * r * 14.0);      // nucleo per gli ammassi
                return fixed4(i.col * a, 1.0);
            }
            ENDCG
        }
    }
}

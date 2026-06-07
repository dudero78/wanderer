Shader "Wanderer/DeepSkyBillboard"
{
    // Oggetti del cielo profondo come billboard con FOTO VERE (Hubble/ESO ecc.) da un atlante 16×16. Lo sfondo nero
    // della foto, in additivo, non aggiunge nulla → si vede solo l'oggetto (una vignetta radiale, bakeata, spegne i
    // bordi → niente quadrato). Dimensione dal RAGGIO ANGOLARE → crescono restringendo il campo (binocolo/telescopio):
    // una macchia che a occhio nudo è un fiocco, ingrandendo "si risolve" nella nebulosa/galassia vera. La luminosità
    // dipende dalla magnitudine e dallo zoom (le deboli EMERGONO ingrandendo).
    Properties
    {
        _Atlas      ("Atlante foto (16×16)", 2D) = "black" {}
        _DsoM0      ("Magnitudine di riferimento", Float) = 6.5
        // esposizione BASSISSIMA: a occhio nudo i deep-sky sono fiochi aloni; la luminosità sale FORTE con lo zoom
        // (_DsoZoomPow=1 → ∝ _SkyZoom) → solo col binocolo/telescopio "si accendono" (come nella realtà: ogni oggetto
        // ha bisogno del suo ingrandimento per emergere). I deboli compaiono solo ad alti ingrandimenti.
        _DsoExposure("Esposizione", Float) = 0.0005
        _DsoGain    ("Guadagno tone-map", Float) = 0.7
        _DsoZoomPow ("Risalto sullo zoom", Float) = 1.5
        _SizeScale  ("Scala dimensione (inquadratura)", Float) = 2.2
        _MinPx      ("Dimensione minima (px)", Float) = 3.0
        _MaxPx      ("Dimensione massima (px)", Float) = 4000.0
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

            #define ATLAS_DIM 16.0

            struct appdata { float4 vertex:POSITION; float4 uv:TEXCOORD0; float4 uv1:TEXCOORD1; fixed4 color:COLOR; };
            struct v2f     { float4 pos:SV_POSITION; float2 auv:TEXCOORD0; float lum:TEXCOORD1; };

            sampler2D _Atlas;
            float _DsoM0, _DsoExposure, _DsoGain, _DsoZoomPow, _SizeScale, _MinPx, _MaxPx;
            float _SkyZoom, _SkyTanHalfFov;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(float4(v.vertex.xyz, 1));

                float radArcmin = v.uv.z, mag = v.uv.w, tile = v.uv1.x;
                float zoom = max(_SkyZoom, 1.0);

                // dimensione ANGOLARE (già indipendente dalla risoluzione: NON moltiplicare per _SkyPxScale, o pulsa col
                // RenderScaler). ×_SizeScale perché nelle foto l'oggetto riempie solo una frazione dell'inquadratura.
                float radRad = radArcmin / 60.0 * 0.0174533 * _SizeScale;
                float px = clamp(radRad * (_ScreenParams.y * 0.5) / max(_SkyTanHalfFov, 1e-4), _MinPx, _MaxPx);

                float I = pow(10.0, 0.4 * (_DsoM0 - mag)) * _DsoExposure * pow(zoom, _DsoZoomPow);
                o.lum = 1.0 - exp(-I * _DsoGain);

                // UV nell'atlante: tile → cella (col, riga). L'atlante è scritto con la riga 0 IN ALTO (PIL/numpy), ma
                // le UV di Unity hanno V=0 IN BASSO → ribalto la riga, altrimenti i DSO pescano i riquadri vuoti = neri.
                float tx = fmod(tile, ATLAS_DIM);
                float ty = (ATLAS_DIM - 1.0) - floor(tile / ATLAS_DIM);
                float2 local = (v.uv.xy * 0.5 + 0.5) * 0.98 + 0.01;
                o.auv = (float2(tx, ty) + local) / ATLAS_DIM;

                o.pos.xy += v.uv.xy * px * (2.0 / _ScreenParams.xy) * o.pos.w;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed3 c = tex2D(_Atlas, i.auv).rgb;   // foto vera (colori reali); sfondo nero = additivo trasparente
                return fixed4(c * i.lum, 1.0);
            }
            ENDCG
        }
    }
}

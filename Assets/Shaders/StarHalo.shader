Shader "Wanderer/StarHalo"
{
    // Alone morbido attorno alle stelle PIÙ BRILLANTI (showpiece: Sirio, Vega, Betelgeuse, Rigel...). È il segnale
    // percettivo del "quella è una stella luminosa": l'occhio vede i punti molto luminosi come dischi con un bagliore.
    // Quad billboard additivo come Wanderer/StarPoint, ma più grande e con caduta più dolce e debole. Raggio ∝ flusso
    // (più brillante = alone più ampio), clampato; colore = colore vero della stella (le brillanti mostrano il colore).
    // Compensato dalla risoluzione dinamica (_SkyPxScale) e influenzato dallo zoom dello strumento (_SkyZoom).
    Properties
    {
        _M0        ("Magnitudine di riferimento (flusso=1)", Float) = 6.5
        _HaloBasePx("Raggio base (px)", Float) = 7.0
        _HaloPow   ("Esponente flusso→raggio", Float) = 0.25
        _HaloMinPx ("Raggio minimo (px)", Float) = 8.0
        _HaloMaxPx ("Raggio massimo (px)", Float) = 55.0
        _HaloFall  ("Durezza caduta", Float) = 2.5
        _HaloStr   ("Intensità", Float) = 0.65
        _HaloFluxRef ("Flusso di riferimento (forza alone)", Float) = 250.0
    }
    SubShader
    {
        Tags { "Queue"="Background+15" "RenderType"="Background" "IgnoreProjector"="True" "PreviewType"="Skybox" }
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

            struct appdata { float4 vertex:POSITION; float4 uv:TEXCOORD0; fixed4 color:COLOR; };
            struct v2f     { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; fixed3 col:TEXCOORD1; };

            float _M0, _HaloBasePx, _HaloPow, _HaloMinPx, _HaloMaxPx, _HaloFall, _HaloStr, _HaloFluxRef;
            float _SkyZoom, _SkyPxScale;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(float4(v.vertex.xyz, 1));

                float mag = v.uv.z;
                float flux = pow(10.0, 0.4 * (_M0 - mag));
                float zoom = max(_SkyZoom, 1.0);
                float pxScale = _SkyPxScale <= 0.0 ? 1.0 : _SkyPxScale;

                // raggio dell'alone: cresce col flusso e (poco) con lo zoom, clampato per non esplodere
                float px = clamp(_HaloBasePx * pow(flux, _HaloPow) * sqrt(zoom), _HaloMinPx, _HaloMaxPx) * pxScale;

                // intensità ∝ flusso: solo le DAVVERO brillanti fioriscono; le showpiece deboli (mag~2) quasi niente
                // → poche perle che spiccano invece di un campo ovattato.
                float s = saturate(flux / _HaloFluxRef);
                o.col = v.color.rgb * _HaloStr * s;
                o.uv = v.uv.xy;
                o.pos.xy += v.uv.xy * px * (2.0 / _ScreenParams.xy) * o.pos.w;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float r = length(i.uv);
                float a = saturate(1.0 - r);
                a = pow(a, _HaloFall);     // bagliore concentrato con coda lunga e tenue
                return fixed4(i.col * a, 1.0);
            }
            ENDCG
        }
    }
}

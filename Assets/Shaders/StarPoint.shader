Shader "Wanderer/StarPoint"
{
    // Le stelle del cielo come PUNTI: ogni stella è un quad di 4 vertici (tutti alla stessa direzione unitaria),
    // espanso in spazio SCHERMO nel vertex shader → dischetto morbido di dimensione in PIXEL controllata, non un
    // punto sub-pixel che sfarfalla girandosi. Additivo (Blend One One) sul nero del cielo: AGGIUNGE luce.
    //
    // Magnitudine → flusso (legge di Pogson: ogni magnitudine = ×2.512) → intensità a schermo. La luminosità sta
    // nell'INTENSITÀ, non nella dimensione (solo le poche brillanti crescono in disco), e un tone-map fa SATURARE e
    // fiorire le brillanti invece di clippare. _SkyZoom (= magnificazione² dello strumento) moltiplica l'intensità:
    // restringendo il campo (binocolo/telescopio) le deboli attraversano la soglia di visibilità → "emergono", come
    // davanti a un vero strumento che concentra più luce nello stesso schermo. Le sotto-soglia collassano a quad di
    // area nulla (zero fragment) → niente overdraw.
    //
    // Colore: B−V→RGB pre-bakeato (per-vertice). Le deboli appaiono ~bianche e il colore SATURA salendo d'intensità
    // (zoom o stella brillante) — fedele all'occhio: il colore "sboccia" quando c'è più luce.
    Properties
    {
        _M0        ("Magnitudine di riferimento (flusso=1)", Float) = 6.5
        _Exposure  ("Esposizione", Float) = 0.72
        _Gain      ("Guadagno tone-map", Float) = 0.5
        _MinPx     ("Disco minimo (px)", Float) = 1.3
        _MaxPx     ("Disco massimo (px)", Float) = 14.0
        _SizeScale ("Crescita disco con luminosità", Float) = 0.22
        _ZoomGrow  ("Crescita disco con lo zoom", Float) = 0.0
        _RevealThresh ("Soglia di comparsa", Float) = 0.45
        _SatStart  ("Inizio saturazione colore", Float) = 0.12
        _SatScale  ("Pendenza saturazione colore", Float) = 1.6
    }
    SubShader
    {
        Tags { "Queue"="Background+10" "RenderType"="Background" "IgnoreProjector"="True" "PreviewType"="Skybox" }
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

            struct appdata
            {
                float4 vertex : POSITION;   // direzione unitaria × R (la stessa per i 4 angoli)
                float4 uv     : TEXCOORD0;   // xy = angolo del quad (±1), z = magnitudine, w = tier
                fixed4 color  : COLOR;       // RGB del B−V
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;       // angolo del quad (per il profilo radiale)
                fixed3 col : TEXCOORD1;       // colore già desaturato + intensità
            };

            float _M0, _Exposure, _Gain, _MinPx, _MaxPx, _SizeScale, _ZoomGrow, _RevealThresh, _SatStart, _SatScale;
            float _SkyZoom;     // globale: magnificazione² dello strumento (1 a occhio nudo)
            float _SkyPxScale;  // globale: scala del RenderScaler (RT-pixel per pixel finale). Compensa la risoluzione
                                // dinamica → dimensione APPARENTE costante (niente pulsare/sfocatura al variare dei pixel).

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(float4(v.vertex.xyz, 1));

                float mag = v.uv.z;
                float flux = pow(10.0, 0.4 * (_M0 - mag));           // Pogson
                float zoom = max(_SkyZoom, 1.0);
                float I = flux * zoom * _Exposure;                    // intensità a schermo

                // comparsa: sotto soglia → disco nullo (quad degenere, niente fragment)
                float keep = step(_RevealThresh, I);

                // dimensione: minima per (quasi) tutte (I≤1 → disco minimo), cresce solo per le luminose
                float grow = saturate(log2(max(I, 1.0)) * _SizeScale);
                float pxScale = _SkyPxScale <= 0.0 ? 1.0 : _SkyPxScale;   // compensa la risoluzione dinamica → apparenza costante
                float zoomGrow = 1.0 + _ZoomGrow * log2(zoom);            // col binocolo/telescopio le stelle "ingrandiscono"
                // i dischi delle BRILLANTI crescono SOLO ad alto ingrandimento (zoom 100→400 ≈ 40×→100×) → più varietà
                // di dimensione al telescopio spinto, mentre a occhio nudo/binocolo restano invariati.
                float effMaxPx = lerp(_MaxPx, _MaxPx * 1.6, saturate((zoom - 100.0) / 300.0));
                float px = lerp(_MinPx, effMaxPx, grow) * keep * pxScale * zoomGrow;

                // tone-map: deboli nel tratto lineare, brillanti che saturano (fioriscono)
                float lum = 1.0 - exp(-I * _Gain);

                // saturazione del colore con l'intensità: deboli bianche, brillanti colorate
                float sat = saturate((lum - _SatStart) * _SatScale);
                fixed3 col = lerp(fixed3(1,1,1), v.color.rgb, sat) * lum;

                o.col = col;
                o.uv = v.uv.xy;

                // espansione in spazio schermo: offset in pixel → NDC (×2/risoluzione) × w (annulla la divisione prospettica)
                o.pos.xy += v.uv.xy * px * (2.0 / _ScreenParams.xy) * o.pos.w;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float r = length(i.uv);
                // nucleo che si stringe salendo d'ingrandimento (a occhio nudo/binocolo morbido com'è; al telescopio
                // spinto più nitido/puntiforme). _SkyZoom 50→400 ≈ 20×→100×.
                float sharp = lerp(3.0, 6.0, saturate((max(_SkyZoom, 1.0) - 50.0) / 350.0));
                float a = pow(saturate(1.0 - r), sharp);
                return fixed4(i.col * a, 1.0);
            }
            ENDCG
        }
    }
}

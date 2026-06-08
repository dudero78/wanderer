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
        _HaloMaxPx ("Raggio massimo (px)", Float) = 90.0
        _HaloFall  ("Durezza caduta", Float) = 2.5
        _HaloStr   ("Intensità", Float) = 0.42
        _HaloFluxRef ("Flusso di riferimento (forza alone)", Float) = 55.0
        // raggi di diffrazione: prima una croce a 4 punte (assi), poi — più in alto — una seconda croce ruotata di 45°
        // (8 punte in tutto). Appaiono e CRESCONO salendo d'ingrandimento (telescopio), come nelle foto vere.
        _SpikeSharp ("Finezza dei raggi", Float) = 320.0
        _SpikeStr   ("Intensità dei raggi", Float) = 0.55
        _SpikeOn    ("Zoom inizio croce dritta", Float) = 20.0
        _SpikeOn2   ("Zoom inizio croce a 45°", Float) = 70.0
        _SpikeRamp  ("Velocità comparsa raggi", Float) = 0.016
        _SpikeShort ("Quanto è corta la croce a 45° (0..1)", Float) = 0.55
        // NUCLEO NITIDO: un cerchietto a bordo definito che compare COI raggi (sopra l'alone morbido) → la stella ha un
        // centro netto + il bagliore intorno. _CoreR = raggio (frazione del quad), _CoreEdge = quanto è netto il bordo.
        _CoreR    ("Raggio nucleo nitido", Float) = 0.085
        _CoreEdge ("Morbidezza bordo nucleo", Float) = 0.03
        _CoreStr  ("Forza nucleo nitido", Float) = 0.9
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
            struct v2f     { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; fixed3 col:TEXCOORD1; float2 spikes:TEXCOORD2; };

            float _M0, _HaloBasePx, _HaloPow, _HaloMinPx, _HaloMaxPx, _HaloFall, _HaloStr, _HaloFluxRef;
            float _SpikeSharp, _SpikeStr, _SpikeOn, _SpikeOn2, _SpikeRamp, _SpikeShort;
            float _CoreR, _CoreEdge, _CoreStr;
            float _SkyZoom, _SkyPxScale;

            v2f vert(appdata v)
            {
                v2f o;
                // skybox all'infinito: solo rotazione camera, coordinate oggetto piccole (niente tremolio lontano dall'origine)
                float3 wd = mul((float3x3)unity_ObjectToWorld, v.vertex.xyz);
                o.pos = mul(UNITY_MATRIX_P, float4(mul((float3x3)UNITY_MATRIX_V, wd), 1.0));

                float mag = v.uv.z;
                float flux = pow(10.0, 0.4 * (_M0 - mag));
                float zoom = max(_SkyZoom, 1.0);
                float pxScale = _SkyPxScale <= 0.0 ? 1.0 : _SkyPxScale;

                // raggio dell'alone: cresce col flusso e con lo zoom (sqrt). Il TETTO cresce dal binocolo in su → le
                // brillanti si INGRANDISCONO salendo d'ingrandimento invece di restare "tappate" a un disco fisso (che,
                // mentre tutto il resto magnifica, sembrava rimpicciolire da 7× a 20×). A occhio nudo/binocolo invariato.
                float haloMax = _HaloMaxPx * (1.0 + saturate((zoom - 13.0) / 100.0) * 2.0);
                float px = clamp(_HaloBasePx * pow(flux, _HaloPow) * sqrt(zoom), _HaloMinPx, haloMax) * pxScale;

                // intensità ∝ flusso: solo le DAVVERO brillanti fioriscono; le showpiece deboli (mag~2) quasi niente
                // → poche perle che spiccano invece di un campo ovattato.
                float s = saturate(flux / _HaloFluxRef);
                o.col = v.color.rgb * _HaloStr * s;
                o.uv = v.uv.xy;
                // i raggi compaiono e crescono salendo d'ingrandimento (0 a occhio nudo/binocolo). La croce dritta parte
                // prima (~telescopio), quella a 45° più in alto; entrambe continuano a intensificarsi (clamp generoso).
                o.spikes.x = clamp((zoom - _SpikeOn)  * _SpikeRamp, 0.0, 1.8);
                o.spikes.y = clamp((zoom - _SpikeOn2) * _SpikeRamp, 0.0, 1.4);
                o.pos.xy += v.uv.xy * px * (2.0 / _ScreenParams.xy) * o.pos.w;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float r = length(i.uv);
                float a = pow(saturate(1.0 - r), _HaloFall);   // bagliore radiale concentrato
                // raggi di diffrazione: croce dritta (assi x/y) + croce a 45° (assi ruotati), gaussiane sottili, sfumate al bordo
                float fA = saturate(1.0 - r); fA *= fA;                          // croce DRITTA: lunghezza piena
                float fB = saturate(1.0 - r / _SpikeShort); fB *= fB;            // croce a 45°: PIÙ CORTA (sfuma a r=_SpikeShort)
                float2 d = i.uv * 0.70710678;
                float2 rot = float2(d.x - d.y, d.x + d.y);     // uv ruotato di 45°
                float crossA = (exp(-i.uv.x * i.uv.x * _SpikeSharp) + exp(-i.uv.y * i.uv.y * _SpikeSharp)) * fA;
                float crossB = (exp(-rot.x  * rot.x  * _SpikeSharp) + exp(-rot.y  * rot.y  * _SpikeSharp)) * fB;
                float cross = crossA * i.spikes.x + crossB * i.spikes.y;
                // NUCLEO NITIDO: cerchietto a bordo definito, che compare insieme ai raggi (gate su i.spikes.x). L'alone
                // morbido (a) resta sotto/intorno → centro netto + bagliore. Sotto la soglia dei raggi: niente nucleo.
                float core = (1.0 - smoothstep(_CoreR, _CoreR + _CoreEdge, r)) * saturate(i.spikes.x) * _CoreStr;
                return fixed4(i.col * (a + core + cross * _SpikeStr), 1.0);
            }
            ENDCG
        }
    }
}

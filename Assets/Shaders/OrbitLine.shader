Shader "Wanderer/OrbitLine"
{
    // Linea d'orbita "alla Outer Wilds": un FILO LUMINOSO a spessore COSTANTE in pixel, non un nastro in
    // unità-mondo (che da vicino diventa una strada e da lontano sparisce).
    //
    // Lo spessore costante si ottiene espandendo la linea IN SPAZIO SCHERMO nel vertex shader: ogni vertice
    // ha la tangente (NORMAL) e un lato (uv.y = 0/1); proiettiamo punto e vicino, prendiamo la perpendicolare
    // sullo schermo e spostiamo di _PixelWidth/2 pixel. Così l'arco vicino e quello lontano della stessa
    // orbita hanno lo STESSO spessore (un singolo width per-linea non potrebbe). Tutto sulla GPU.
    //
    // - Additivo puro (Blend One One): la linea AGGIUNGE luce sul nero → filamento che brilla, non vernice;
    //   incroci che si sommano, nessun bordo scuro.
    // - Sezione (lato): nucleo sottile + alone morbido (gaussiana) = bordo anti-aliasato per costruzione.
    // - Lungo l'anello (uv.x = 0..1): luce PIENA dove sta il pianeta ADESSO (_PeakU), coda che sfuma andando
    //   indietro nel verso del moto; un floor tiene l'intera ellisse debolmente visibile (la vedi tutta).
    // ZWrite Off + ZTest LEqual: i corpi la occludono (profondità vera) ma lei non scrive depth.
    Properties
    {
        _Color      ("Tint",             Color) = (0.55, 0.78, 1.0, 1)
        _PixelWidth ("Spessore (px)",    Float) = 6
        _Core       ("Nitidezza nucleo", Float) = 20
        _Halo       ("Larghezza alone",  Float) = 3.5
        _PeakU      ("Fase pianeta (U)", Float) = 0
        _TailLen    ("Lunghezza coda",   Float) = 0.55
        _TailPow    ("Caduta coda",      Float) = 1.6
        _Floor      ("Luce minima anello", Float) = 0.10
        _HeadBoost  ("Bagliore di testa", Float) = 0.8
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; };
            struct v2f     { float4 pos:SV_POSITION; float side:TEXCOORD0; float2 bh:TEXCOORD1; };

            fixed4 _Color;
            float _PixelWidth, _Core, _Halo, _PeakU, _TailLen, _TailPow, _Floor, _HeadBoost;

            v2f vert(appdata v)
            {
                // punto e un vicino lungo la tangente → direzione della linea in spazio schermo
                float3 ahead = v.vertex.xyz + v.normal * (0.0015 * length(v.vertex.xyz) + 0.001);
                float4 c0 = UnityObjectToClipPos(v.vertex.xyz);
                float4 c1 = UnityObjectToClipPos(ahead);

                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 d = (c1.xy / c1.w) - (c0.xy / c0.w);
                d.x *= aspect;
                d = normalize(d + 1e-6);
                float2 perp = float2(-d.y, d.x);
                perp.x /= aspect;                                  // torna in NDC

                float side = v.uv.y * 2.0 - 1.0;                   // -1 / +1
                float2 off = perp * side * (_PixelWidth / _ScreenParams.y);   // NDC per px·(W/2)·2 = W px tot
                // GUARDIA dietro-camera: un vertice con w<=0 (dietro di te) espanso in spazio schermo esplode in
                // triangoli enormi (il glitch sulle orbite del sistema in cui ti trovi). Se questo vertice o il vicino
                // sono dietro, NON espandere: la xy resta in clip valida → il near-clip della GPU taglia l'orbita
                // pulita dietro di te (invisibile comunque). Davanti, espansione normale.
                if (c0.w > 1e-4 && c1.w > 1e-4) c0.xy += off * c0.w;   // in clip space (contrasta la divisione per w)

                // LUMINOSITÀ LUNGO L'ANELLO calcolata QUI, per-vertice (uv.x = posizione vera del campione).
                // Si interpola poi il VALORE di luminosità, che è continuo attorno all'anello → alla cucitura
                // (campione n-1 → 0) va liscio. Se invece si interpolasse uv.x e si ricalcolasse frac() nel
                // fragment, sul segmento di chiusura uv.x spazzerebbe 1→0 all'indietro e il nucleo bianco
                // comparirebbe come un nodo in un punto fisso (la cucitura), non sul pianeta.
                float back   = frac(_PeakU - v.uv.x);              // 0 al pianeta, cresce ANDANDO INDIETRO
                float tail   = pow(saturate(1.0 - back / _TailLen), _TailPow);
                float bright = _Floor + (1.0 - _Floor) * tail;
                float headD  = min(back, 1.0 - back);
                float head   = exp(-headD * headD * 500.0) * _HeadBoost;   // nucleo sul pianeta

                v2f o;
                o.pos  = c0;
                o.side = v.uv.y;                                   // 0..1 attraverso il filo (niente cucitura)
                o.bh   = float2(bright, head);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // sezione trasversale (lato): nucleo sottile + alone morbido
                float a     = abs(i.side * 2.0 - 1.0);            // 0 al centro, 1 ai bordi
                float cross = exp(-a * a * _Core) + 0.5 * exp(-a * a * _Halo);

                float3 light = _Color.rgb * i.bh.x + i.bh.y;      // 'head' schiarisce verso il bianco
                light *= cross * _Color.a;
                return fixed4(light, 1);
            }
            ENDCG
        }
    }
}

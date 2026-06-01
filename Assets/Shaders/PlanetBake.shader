Shader "Wanderer/PlanetBake"
{
    // Bake del rilievo in "spazio texture": disegna la mesh di una faccia usando le sue UV
    // come posizione su schermo (uv -> clip), così ogni texel della RenderTexture riceve il
    // valore calcolato ESATTAMENTE nel punto della mesh che gli corrisponde. Usando la mesh
    // vera (con la sua posizione gia' spostata in altezza) il bake e' coerente al 100% con la
    // forma del pianeta: niente ricostruzione di coordinate, niente drift.
    //
    // Output (RGBAHalf, valori con segno):
    //   R   = valore del rilievo  (per le ombreggiature di colore degli avvallamenti)
    //   GBA = gradiente analitico del rilievo (per ricostruire la normale, leggero, a runtime)
    //
    // Questo e' SOLO la parte costosa (6 ottave di Perlin con derivate). Il colore e le zone
    // rugose restano procedurali nello shader di superficie: sono gia' economici.
    // NOTA: si bakeano SOLO le ottave grosse (default 3). Le fini restano procedurali nello
    // shader di superficie, perché una texture a risoluzione fissa le sfocherebbe da vicino.
    Properties
    {
        _BaseFreq ("Scala rilievo (ottava base)", Float) = 0.25
        _BakeOct  ("Ottave grosse da bakeare",    Float) = 3.0
    }
    SubShader
    {
        // niente cull/depth: stiamo riempiendo una texture, non una scena 3D
        Cull Off
        ZTest Always
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.0
            #include "UnityCG.cginc"
            #include "PlanetNoise.cginc"

            float _BaseFreq;
            float _BakeOct;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos : SV_POSITION; float3 localPos : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                // uv [0,1] -> clip [-1,1]: la mesh viene "srotolata" sul quadrato della texture.
                float2 p = v.uv * 2.0 - 1.0;
                // su Metal/D3D l'origine della texture e' in alto: ribaltiamo Y in scrittura
                // cosi' che, leggendo a runtime con la stessa uv, il texel torni allineato.
                #if UNITY_UV_STARTS_AT_TOP
                    p.y = -p.y;
                #endif
                o.pos = float4(p, 0.0, 1.0);
                o.localPos = v.vertex.xyz;   // posizione oggetto GIA' spostata in altezza (la mesh vera)
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // identica alla riga dello shader di superficie: fbmRelief(P * _BaseFreq, oct)
                return fbmRelief(i.localPos * _BaseFreq, _BakeOct);
            }
            ENDCG
        }
    }
    FallBack Off
}

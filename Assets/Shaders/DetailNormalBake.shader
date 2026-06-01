Shader "Wanderer/DetailNormalBake"
{
    // Genera UNA volta una detail normal map TILEABLE della grana del suolo, derivata da una
    // FOTO reale di regolite (_Source). La scrive in una RenderTexture con mipmap + wrap Repeat.
    //
    // Perché una texture e non rumore per-pixel: una texture ha mipmap, quindi in lontananza la
    // GPU campiona un livello mediato (la grana svanisce a piatto) → MAI sfarfallio. Il rumore
    // procedurale per-pixel non ha mipmap → "rumore TV". È il motivo per cui i giochi veri usano
    // detail texture, ed è l'errore che avevamo fatto con la micro-grana.
    //
    // Perché NORMAL e non albedo: una foto ha le ombre "cotte" dentro. Usata come colore
    // illuminerebbe anche il lato in ombra. Derivandone solo la NORMALE (dalla luminanza →
    // altezza → gradiente) otteniamo il rilievo della grana, e la luce della scena la spegne
    // correttamente di notte.
    //
    // TILEABLE: la foto non si ripete senza cuciture. Le nascondiamo fondendo due copie sfasate
    // di mezza texture (offset-blend): vicino ai bordi di una copia usiamo l'altra, i cui bordi
    // cadono al centro. La regolite è poco strutturata, quindi il blend è invisibile.
    //
    // Output: normale tangent-space impacchettata in RGB ([-1,1]→[0,1]). z dominante (xy lievi)
    // → perturbazioni piccole attorno alla verticale: il rilievo si sente ma le normali non
    // diventano mai orizzontali/casuali.
    Properties
    {
        _Source   ("Foto sorgente (regolite)", 2D) = "gray" {}
        _Strength ("Pendenza grana", Float) = 2.0
        _Contrast ("Contrasto altezza", Float) = 1.3
    }
    SubShader
    {
        Cull Off ZTest Always ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.0
            #include "UnityCG.cginc"

            sampler2D _Source;
            float _Strength;
            float _Contrast;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                float2 p = v.uv * 2.0 - 1.0;
                #if UNITY_UV_STARTS_AT_TOP
                    p.y = -p.y;
                #endif
                o.pos = float4(p, 0.0, 1.0);
                o.uv = v.uv;
                return o;
            }

            // luminanza percettiva → altezza, con un po' di contrasto attorno a 0.5
            float lum(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }
            float heightAt(float2 uv)
            {
                float l = lum(tex2D(_Source, frac(uv)).rgb);
                return saturate((l - 0.5) * _Contrast + 0.5);
            }

            // altezza TILEABLE: fonde la copia normale con una sfasata di mezzo. La maschera
            // 'edge' è ~1 vicino ai bordi (dove cadrebbe la cucitura) e 0 al centro.
            float heightTile(float2 uv)
            {
                float2 w = frac(uv);
                float2 d = min(w, 1.0 - w);          // distanza dal bordo (0..0.5)
                float edge = 1.0 - smoothstep(0.0, 0.18, min(d.x, d.y));
                float a = heightAt(w);
                float b = heightAt(frac(w + 0.5));   // bordi al centro: lontani dalle cuciture
                return lerp(a, b, edge);
            }

            float4 frag(v2f i) : SV_Target
            {
                float e = 1.0 / 256.0;   // passo per la differenza centrale
                float hx0 = heightTile(i.uv - float2(e, 0));
                float hx1 = heightTile(i.uv + float2(e, 0));
                float hy0 = heightTile(i.uv - float2(0, e));
                float hy1 = heightTile(i.uv + float2(0, e));
                float2 grad = float2(hx1 - hx0, hy1 - hy0) / (2.0 * e);
                float3 n = normalize(float3(-grad * _Strength, 1.0));
                // RGB = normale (rilievo della grana). A = variazione tonale dell'albedo,
                // centrata ~0.5: lo shader di superficie la usa come MOLTIPLICATORE di colore.
                // È ciò che dà texture a media/alta distanza, dove il bump collassa.
                float hC = heightTile(i.uv);
                return float4(n * 0.5 + 0.5, hC);   // pack normale [-1,1]->[0,1], albedo in A
            }
            ENDCG
        }
    }
    FallBack Off
}

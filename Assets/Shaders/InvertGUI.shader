Shader "Wanderer/InvertGUI"
{
    // Disegna una texture-MASCHERA (alfa = forma) INVERTENDO il colore di sfondo dove la forma è opaca → il mirino
    // è SEMPRE visibile, sia su sfondo chiaro che scuro. Usato via Graphics.DrawTexture(rect, tex, material) in OnGUI.
    //
    // Blend OneMinusDstColor OneMinusSrcAlpha, col fragment che ritorna (1,1,1, alfa-forma):
    //   output = src·(1−dst) + dst·(1−src.a)
    //   sulla forma (a=1): 1·(1−dst) + 0 = 1−dst  → colore di sfondo INVERTITO
    //   fuori  (a=0):      0       + dst·1 = dst   → sfondo invariato
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue" = "Overlay" "IgnoreProjector" = "True" "RenderType" = "Transparent" }
        Cull Off ZWrite Off ZTest Always Lighting Off
        Blend OneMinusDstColor OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed4 frag(v2f_img i) : SV_Target
            {
                fixed a = tex2D(_MainTex, i.uv).a;   // la forma del mirino sta nell'alfa
                return fixed4(1, 1, 1, a);
            }
            ENDCG
        }
    }
}

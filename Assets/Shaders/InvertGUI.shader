Shader "Wanderer/InvertGUI"
{
    // Disegna una texture-MASCHERA (alfa = forma) INVERTENDO il colore di sfondo dove la forma è opaca → il mirino
    // è SEMPRE visibile, sia su sfondo chiaro che scuro. Usato via Graphics.DrawTexture(rect, tex, material) in OnGUI.
    //
    // Blend OneMinusDstColor OneMinusSrcAlpha, col fragment che ritorna RGB PREMOLTIPLICATO per l'alfa = (a,a,a,a):
    //   output = src.rgb·(1−dst) + dst·(1−src.a)
    //   sulla forma (a=1): 1·(1−dst) + dst·0 = 1−dst  → colore di sfondo INVERTITO
    //   fuori  (a=0):      0·(1−dst) + dst·1 = dst     → sfondo INVARIATO (niente più quadrato bianco)
    //   (col vecchio (1,1,1,a): fuori dava 1·(1−dst)+dst·1 = 1 = bianco → il quadrato spurio attorno al mirino)
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
                return fixed4(a, a, a, a);            // RGB premoltiplicato → fuori dalla forma non tinge nulla
            }
            ENDCG
        }
    }
}

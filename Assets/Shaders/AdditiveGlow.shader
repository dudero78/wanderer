Shader "Wanderer/AdditiveGlow"
{
    // BAGLIORE additivo: un quad (billboard verso la camera) con alfa RADIALE (centro pieno → bordi 0), sommato allo
    // sfondo (Blend One One) → un alone morbido luminoso attorno a una sorgente (es. la sonda). _Color ne dà tinta e forza.
    Properties { _Color ("Color", Color) = (0.5, 0.9, 1, 1) }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off Cull Off Lighting Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                float r = saturate(1.0 - length(i.uv - 0.5) * 2.0);   // 1 al centro, 0 al bordo
                float a = r * r * r;                                  // alone morbido (coda dolce)
                return _Color * a;
            }
            ENDCG
        }
    }
}

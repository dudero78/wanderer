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
                float d = length(i.uv - 0.5) * 2.0;          // 0 al centro, 1 al bordo
                float core = saturate(1.0 - d * 1.7);        // disco PIENO centrale (la "sfera")
                float halo = saturate(1.0 - d); halo *= halo; // ALONE largo che sfuma ai bordi
                float a = core * 1.3 + halo * 0.55;          // additivo: il core satura verso il bianco, l'alone resta tinto
                return _Color * a;
            }
            ENDCG
        }
    }
}

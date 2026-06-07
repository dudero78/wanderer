Shader "Wanderer/ConstellationLine"
{
    // Linee delle costellazioni: sottili, additive, tenui. Compaiono/svaniscono via _Alpha (fade su toggle).
    // Topologia Lines, nessuna profondità (sono sul cielo, dietro tutto). Colore freddo, discreto.
    Properties
    {
        _Color ("Colore", Color) = (0.45, 0.7, 0.95, 1)
        _Alpha ("Opacità", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Background+18" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Skybox" }
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
            struct appdata { float4 vertex:POSITION; };
            struct v2f { float4 pos:SV_POSITION; };
            fixed4 _Color; float _Alpha;
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }
            fixed4 frag(v2f i) : SV_Target { return fixed4(_Color.rgb * _Alpha, 1.0); }
            ENDCG
        }
    }
}

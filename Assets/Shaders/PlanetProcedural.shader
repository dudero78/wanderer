Shader "Wanderer/PlanetProcedural"
{
    // Anteprima GPU dell'editor — TAPPA 1 del percorso "GPU per l'editor".
    // Disegna la superficie del pianeta leggendo posizioni e normali DIRETTAMENTE da due
    // StructuredBuffer riempiti dal compute (PlanetHeight.compute, kernel CSFaceGrid/CSFaceNormals),
    // SENZA readback su CPU: il vertice indicizza il buffer con SV_VertexID (l'index buffer della
    // DrawProcedural risolve l'indice del vertice). Serve a PROVARE lo schema no-readback; la
    // colorazione ricca (Wanderer/PlanetBaked) arriverà più avanti — qui basta un Lambert grigio.
    //
    // La luce è passata a mano dal driver (GpuPlanetSurface) come uniform (_SunDir/_SunColor/_Ambient)
    // così la resa è deterministica e non dipende dal binding della luce del forward pass.
    Properties
    {
        _Color ("Colore suolo", Color) = (0.44, 0.44, 0.45, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            // Cull Off: il verso dei triangoli dipende dall'orientamento degli assi delle 6 facce del
            // cubo (handedness variabile). Disegnando entrambe le facce si evita ogni rischio di buchi;
            // la normale (orientata verso l'esterno nel compute) regge comunque la luce. Tappa 1.
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5          // StructuredBuffer nel vertex shader (Metal lo regge)
            #include "UnityCG.cginc"

            StructuredBuffer<float> _VPos;   // 3 float per vertice (x,y,z), buffer piatto
            StructuredBuffer<float> _VNrm;

            float4 _Color;
            float3 _SunDir;     // direzione VERSO il sole (unitaria)
            float3 _SunColor;
            float3 _Ambient;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 nrm : TEXCOORD0;
            };

            v2f vert(uint vid : SV_VertexID)
            {
                v2f o;
                float3 p = float3(_VPos[vid * 3], _VPos[vid * 3 + 1], _VPos[vid * 3 + 2]);
                float3 n = float3(_VNrm[vid * 3], _VNrm[vid * 3 + 1], _VNrm[vid * 3 + 2]);
                // il pianeta dell'editor è all'origine: spazio oggetto = mondo (model = identità).
                o.pos = UnityObjectToClipPos(p);
                o.nrm = UnityObjectToWorldNormal(n);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.nrm);
                float ndl = saturate(dot(n, _SunDir));
                float3 col = _Color.rgb * (ndl * _SunColor + _Ambient);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}

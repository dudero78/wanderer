Shader "Wanderer/CraterNormalBake"
{
    // Bake della NORMALE dei crateri in spazio texture (uv -> clip), per faccia. Disegna la mesh
    // della faccia usando le sue UV come posizione: ogni texel riceve la normale del cratere
    // calcolata ESATTAMENTE nel suo punto. Output = normal map tangent-space (RGB = n*0.5+0.5),
    // nello STESSO frame tangente (objT/objB) che usa lo shader di superficie → si applica diretta.
    //
    // Perché bakeata e non per-pixel: le normali ad alta frequenza calcolate per-pixel ALIASANO
    // (sparkle, luce sbagliata). Cotte in texture, il MIPMAP le media automaticamente in lontananza
    // → niente sparkle, luce corretta a ogni distanza. È la cura standard, e qui il bake gira su GPU
    // (veloce, niente freeze di caricamento) mentre la mesh resta grezza.
    Properties
    {
        _BaseRadius ("Raggio base", Float) = 500
        _CraterSeed ("Crateri: seed", Float) = 7777
        _CraterOctaves ("Crateri: ottave", Float) = 6
        _CraterLargest ("Crateri: raggio max (m)", Float) = 110
        _CraterDensity ("Crateri: densità", Float) = 0.55
        _CraterDepthRatio ("Crateri: profondità/raggio", Float) = 0.2
        _CraterRimRatio ("Crateri: bordo/profondità", Float) = 0.3
        _CraterNormalStr ("Crateri: forza (ripidità ottica)", Float) = 0.6
    }
    SubShader
    {
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

            float _BaseRadius, _CraterSeed, _CraterOctaves, _CraterLargest, _CraterDensity;
            float _CraterDepthRatio, _CraterRimRatio, _CraterNormalStr;

            uint cHash(int3 p, uint seed)
            {
                uint h = seed;
                h = (h + (uint)p.x) * 0x9E3779B1; h ^= h >> 16;
                h = (h + (uint)p.y) * 0x85EBCA77; h ^= h >> 13;
                h = (h + (uint)p.z) * 0xC2B2AE3D; h ^= h >> 16;
                h *= 0x27D4EB2F; h ^= h >> 15;
                return h;
            }
            float cU01(uint h) { return (h & 0xFFFFFF) / 16777215.0; }

            // Gradiente (pendenza dH/dx,dH/dy in T,B) del campo crateri. Stessa distribuzione del
            // campo geometrico (griglia 3D hashata, 3×3×3, ottave). NESSUN fade per distanza: qui si
            // valutano TUTTE le ottave a piena forza; l'antialiasing lo fa il mipmap della texture.
            float2 craterGrad(float3 dir, float3 T, float3 B)
            {
                float2 grad = float2(0.0, 0.0);
                float radius = _CraterLargest;
                int oct = (int)_CraterOctaves;
                [loop] for (int o = 0; o < 7; o++)
                {
                    if (o >= oct) break;
                    float gscale = _BaseRadius / (radius * 10.0);    // 1 / dimensione cella (SPACING=10, lockstep con la geometria)
                    int3 c = (int3)floor(dir * gscale);
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int3 C = c + int3(dx, dy, dz);
                        uint hsh = cHash(C, (uint)_CraterSeed + (uint)(o * 9176));
                        if (cU01(hsh) <= _CraterDensity)
                        {
                            float3 cen = float3(C.x + cU01(hsh * 0x9E3779B1 + 1),
                                                C.y + cU01(hsh * 0x85EBCA77 + 2),
                                                C.z + cU01(hsh * 0xC2B2AE3D + 3)) / gscale;
                            cen = normalize(cen);
                            float rad = radius * (0.6 + 0.8 * cU01(hsh * 0x27D4EB2F + 4));
                            float3 d3 = dir - cen;
                            float uu = dot(d3, T), vv = dot(d3, B);
                            float duv = sqrt(uu * uu + vv * vv) + 1e-9;
                            float r = (_BaseRadius * duv) / rad;
                            if (r < 2.2)
                            {
                                float depth = rad * _CraterDepthRatio;
                                float rim = depth * _CraterRimRatio;
                                float floorR = 0.15;
                                float dcav = 0.0;
                                if (r > floorR && r < 1.0) { float t = (r - floorR) / (1.0 - floorR); dcav = 6.0 * t * (1.0 - t) / (1.0 - floorR); }
                                float wri = (r <= 1.0) ? 0.5 : 0.9;
                                float drr = r - 1.0;
                                float ring = exp(-(drr * drr) / (wri * wri));
                                float dOdr = depth * dcav + rim * ring * (-2.0 * drr / (wri * wri));
                                float win = 1.0 - smoothstep(2.2 - 0.6, 2.2, r);    // fade C1 al bordo esterno
                                grad += win * (dOdr / rad) * float2(uu, vv) / duv;
                            }
                        }
                    }
                    radius *= 0.5;
                }
                return grad;
            }

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float3 normal : NORMAL; float4 tangent : TANGENT; };
            struct v2f { float4 pos : SV_POSITION; float3 localPos : TEXCOORD0; float3 objT : TEXCOORD1; float3 objB : TEXCOORD2; };

            // srotola la mesh-faccia sul quadrato della texture: uv -> clip space
            v2f vert(appdata v)
            {
                v2f o;
                float2 p = v.uv * 2.0 - 1.0;
                #if UNITY_UV_STARTS_AT_TOP
                    p.y = -p.y;
                #endif
                o.pos = float4(p, 0.0, 1.0);
                o.localPos = v.vertex.xyz;                                   // posizione oggetto (mesh vera)
                o.objT = v.tangent.xyz;                                      // stesso frame del surface shader
                o.objB = cross(v.normal, v.tangent.xyz) * v.tangent.w;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.localPos);
                float3 T = normalize(i.objT);
                float3 B = normalize(i.objB);
                float2 g = craterGrad(dir, T, B);
                // CLAMP del modulo del gradiente: dove più bordi/ottave si sommano la pendenza esplode
                // e sotto luce radente il Lambert oscilla tra nero e bianco su una banda sub-pixel
                // (= "lame"). Saturazione morbida: tiene la direzione, satura l'ampiezza a ~1.
                float gl = length(g);
                g *= 1.0 / (1.0 + gl);
                float3 n = normalize(float3(-g * _CraterNormalStr, 1.0));    // normale tangente
                return float4(n * 0.5 + 0.5, 1.0);                          // encode [0,1]
            }
            ENDCG
        }
    }
    FallBack Off
}

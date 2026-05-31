Shader "Wanderer/Planet"
{
    // Shader procedurale del pianeta.
    // - GRADIENT NOISE (Perlin) con hash intero (PCG): zero sui punti del reticolo, gradienti
    //   casuali -> niente artefatti di griglia / glifi del value noise, e stabile a qualunque
    //   distanza dall'origine (il pianeta sta in spazio oggetto a raggio ~500).
    // - Rilievo = fBm di Perlin con GRADIENTE ANALITICO: la normale esce dalla stessa formula,
    //   senza campionamenti extra (piu' leggero). Numero di ottave variabile per distanza:
    //   nitido da vicino, morbido e leggero da lontano.
    // - Normale calcolata in spazio OGGETTO (gradiente proiettato sul piano tangente) e
    //   convertita allo spazio tangente solo alla fine: continua ovunque, niente artefatti ai poli.
    // - Regolite liscia ovunque; zone rugose limitate (piu' rilievo + tinta roccia).
    Properties
    {
        _LowColor   ("Mari (basso)",     Color) = (0.19, 0.22, 0.20, 1)
        _HighColor  ("Altopiani",        Color) = (0.54, 0.60, 0.53, 1)
        _PeakColor  ("Vette",            Color) = (0.74, 0.76, 0.70, 1)
        _RockColor  ("Roccia (zone rugose)", Color) = (0.46, 0.42, 0.36, 1)

        _BaseRadius ("Raggio base",      Float) = 500
        _Amplitude  ("Ampiezza terreno", Float) = 30

        _PatchFreq  ("Scala macchie colore",  Float) = 2.0
        _MottleStr  ("Variazione colore",     Range(0,1)) = 0.12

        _RoughFreq  ("Scala zone rugose",     Float) = 0.8
        _RoughThresh ("Soglia zone rugose",   Range(0,1)) = 0.60
        _RoughBoost ("Rilievo extra zone rugose", Range(1,4)) = 1.8

        _BaseFreq   ("Scala rilievo (ottava base)", Float) = 0.25
        _DetailStr  ("Forza rilievo",         Range(0,1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Lambert vertex:vert
        #pragma target 4.0

        fixed4 _LowColor, _HighColor, _PeakColor, _RockColor;
        float _BaseRadius, _Amplitude, _PatchFreq, _MottleStr;
        float _RoughFreq, _RoughThresh, _RoughBoost;
        float _BaseFreq, _DetailStr;

        // --- hash intero (PCG 3D): stabile a qualunque coordinata ---
        uint pcg3d(uint3 v)
        {
            v = v * 1664525u + 1013904223u;
            v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
            v ^= v >> 16u;
            v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
            return v.x;
        }

        // gradiente casuale in [-1,1]^3 per il punto di reticolo dato (interi, anche negativi)
        float3 hashGrad(float3 lp)
        {
            uint3 up = (uint3)((int3)lp + 4194304);
            uint h = pcg3d(up);
            float3 g;
            g.x = float(h & 0x3ffu)        * (2.0 / 1023.0) - 1.0;
            g.y = float((h >> 10) & 0x3ffu) * (2.0 / 1023.0) - 1.0;
            g.z = float((h >> 20) & 0x3ffu) * (2.0 / 1023.0) - 1.0;
            return g;
        }

        // Gradient noise 3D con derivate analitiche (iq). Ritorna float4(valore, d/dx, d/dy, d/dz).
        float4 noised(float3 x)
        {
            float3 p = floor(x);
            float3 w = frac(x);
            float3 u = w * w * w * (w * (w * 6.0 - 15.0) + 10.0);
            float3 du = 30.0 * w * w * (w * (w - 2.0) + 1.0);

            float3 ga = hashGrad(p + float3(0,0,0));
            float3 gb = hashGrad(p + float3(1,0,0));
            float3 gc = hashGrad(p + float3(0,1,0));
            float3 gd = hashGrad(p + float3(1,1,0));
            float3 ge = hashGrad(p + float3(0,0,1));
            float3 gf = hashGrad(p + float3(1,0,1));
            float3 gg = hashGrad(p + float3(0,1,1));
            float3 gh = hashGrad(p + float3(1,1,1));

            float va = dot(ga, w - float3(0,0,0));
            float vb = dot(gb, w - float3(1,0,0));
            float vc = dot(gc, w - float3(0,1,0));
            float vd = dot(gd, w - float3(1,1,0));
            float ve = dot(ge, w - float3(0,0,1));
            float vf = dot(gf, w - float3(1,0,1));
            float vg = dot(gg, w - float3(0,1,1));
            float vh = dot(gh, w - float3(1,1,1));

            float v = va
                + u.x * (vb - va)
                + u.y * (vc - va)
                + u.z * (ve - va)
                + u.x * u.y * (va - vb - vc + vd)
                + u.y * u.z * (va - vc - ve + vg)
                + u.z * u.x * (va - vb - ve + vf)
                + u.x * u.y * u.z * (-va + vb + vc - vd + ve - vf - vg + vh);

            float3 d = ga
                + u.x * (gb - ga)
                + u.y * (gc - ga)
                + u.z * (ge - ga)
                + u.x * u.y * (ga - gb - gc + gd)
                + u.y * u.z * (ga - gc - ge + gg)
                + u.z * u.x * (ga - gb - ge + gf)
                + u.x * u.y * u.z * (-ga + gb + gc - gd + ge - gf - gg + gh)
                + du * float3(
                    vb - va + u.y * (va - vb - vc + vd) + u.z * (va - vb - ve + vf) + u.y * u.z * (-va + vb + vc - vd + ve - vf - vg + vh),
                    vc - va + u.z * (va - vc - ve + vg) + u.x * (va - vb - vc + vd) + u.z * u.x * (-va + vb + vc - vd + ve - vf - vg + vh),
                    ve - va + u.x * (va - vb - ve + vf) + u.y * (va - vc - ve + vg) + u.x * u.y * (-va + vb + vc - vd + ve - vf - vg + vh));

            return float4(v, d);
        }

        // --- value noise ECONOMICO per le maschere colore: serve solo il valore (zone di
        // colore a bassa frequenza), non il gradiente. Niente formula analitica costosa. ---
        float vhash(float3 lp)
        {
            return float(pcg3d((uint3)((int3)lp + 4194304)) & 0xffffu) / 65535.0;
        }

        float vnoise(float3 x)
        {
            float3 i = floor(x);
            float3 f = frac(x);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(vhash(i + float3(0,0,0)), vhash(i + float3(1,0,0)), f.x),
                             lerp(vhash(i + float3(0,1,0)), vhash(i + float3(1,1,0)), f.x), f.y),
                        lerp(lerp(vhash(i + float3(0,0,1)), vhash(i + float3(1,0,1)), f.x),
                             lerp(vhash(i + float3(0,1,1)), vhash(i + float3(1,1,1)), f.x), f.y), f.z);
        }

        // 2 ottave, ~[0,1]: sufficiente per le macchie di colore
        float fbm(float3 p)
        {
            return 0.6 * vnoise(p) + 0.4 * vnoise(p * 2.03 + 11.7);
        }

        // fBm di Perlin per il rilievo: valore + gradiente analitico (rispetto a p).
        float4 fbmRelief(float3 p, float oct)
        {
            float v = 0.0;
            float3 d = float3(0,0,0);
            float a = 0.5;
            float freq = 1.0;
            float3 off = float3(0,0,0);
            for (int i = 0; i < 6; i++)
            {
                float w = saturate(oct - (float)i);
                if (w <= 0.0) break;   // ottave oltre 'oct' non contribuiscono: salta (risparmio a distanza)
                float4 nd = noised(p * freq + off);
                v += a * w * nd.x;
                d += a * w * freq * nd.yzw;
                freq *= 2.02; a *= 0.5; off += 19.3;
            }
            return float4(v, d);
        }

        struct Input
        {
            float3 localPos;
            float3 worldPos;
            float3 objT;
            float3 objB;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.localPos = v.vertex.xyz;
            o.objT = v.tangent.xyz;
            o.objB = cross(v.normal, v.tangent.xyz) * v.tangent.w;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float3 P = IN.localPos;
            float h = max(length(P), 1e-4);
            float3 N = P / h;               // normale radiale (verso l'esterno), spazio oggetto

            float dist = distance(IN.worldPos, _WorldSpaceCameraPos);

            // zone rugose: rare e grandi (bassa frequenza). Regolite liscia ovunque, roccia qui.
            float rough = smoothstep(_RoughThresh, _RoughThresh + 0.12, fbm(N * _RoughFreq + 5.1));

            // LOD continuo: il numero di ottave dipende dalla dimensione in PIXEL delle feature.
            // Raddoppiando la distanza si perde un'ottava (le feature dimezzano l'angolo che
            // sottendono). Così c'è sempre il dettaglio che lo schermo può risolvere: mai
            // sfocato (vicino tante ottave), mai aliasato (lontano poche), senza fade brutali.
            float oct = clamp(13.6 - log2(max(dist, 1.0)), 2.0, 6.0);

            // rilievo + gradiente analitico (niente differenze finite, niente artefatti di griglia)
            float4 r = fbmRelief(P * _BaseFreq, oct);
            float3 G = r.yzw * _BaseFreq;                        // gradiente del rilievo, spazio oggetto
            float str = _DetailStr * lerp(1.0, _RoughBoost, rough);

            // --- colore ---
            float t = saturate((h - (_BaseRadius - _Amplitude)) / (2.0 * _Amplitude));
            float patch = fbm(N * _PatchFreq);
            float3 alb = lerp(_LowColor.rgb, _HighColor.rgb, smoothstep(0.40, 0.66, patch));
            alb = lerp(alb, _PeakColor.rgb, smoothstep(0.72, 0.95, t));
            alb = lerp(alb, _RockColor.rgb, rough * 0.55);
            float nearF = saturate(1.0 - dist / 120.0);
            alb *= 1.0 + _MottleStr * (r.x) * nearF;             // avvallamenti piu' scuri, solo da vicino
            o.Albedo = alb;

            // bump STANDARD in spazio tangente: proietto il gradiente sui due assi tangenti
            // della mesh. La base (0,0,1) lascia intatta la normale della mesh, quindi NON c'è
            // distorsione dipendente dalla pendenza (era quella, nella conversione precedente
            // che usava dir come base, a creare i "glifi"). Tangente e bitangente si ribaltano
            // insieme ai poli, quindi la normale di mondo resta continua: niente cucitura.
            float3 T = normalize(IN.objT);
            float3 B = normalize(IN.objB);
            o.Normal = normalize(float3(-dot(G, T) * str, -dot(G, B) * str, 1.0));
        }
        ENDCG
    }

    FallBack "Diffuse"
}

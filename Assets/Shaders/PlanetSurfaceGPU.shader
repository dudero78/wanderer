Shader "Wanderer/PlanetSurfaceGPU"
{
    // Resa della superficie dei pianeti IN GIOCO (percorso B1): la geometria è calcolata sulla GPU
    // (PlanetHeight.compute) in un POOL di "fette" (una per nodo) e disegnata con UN SOLO draw INDIRECT
    // istanziato — niente Mesh Unity, niente readback, niente draw call per-nodo sulla CPU.
    //
    // Ogni istanza (SV_InstanceID) è una fetta del pool: _SlabOfInstance dice quale, e il vertice indicizza
    // il pool con slab*_VertsPerSlab + SV_VertexID. A differenza dell'anteprima editor (pianeta all'origine),
    // in gioco il pianeta sta altrove e si muove con la floating origin → trasformo le posizioni LOCALI con
    // _ObjectToWorld passata ogni frame. Il COLORE è l'include condiviso PlanetShade (una sola verità).
    // Default IDENTICI a Wanderer/PlanetProcedural: gli uniform che ApplyColor NON imposta (tinta suolo, macro,
    // minerali, vette...) DEVONO avere un default qui, o con material vuoto valgono 0 → _SoilTint=0 → albedo NERO.
    Properties
    {
        _BaseRadius ("Raggio base", Float) = 500
        _Amplitude  ("Ampiezza", Float) = 45
        _Frequency  ("Forma base: frequenza", Float) = 2
        _Octaves    ("Forma base: ottave", Int) = 5
        _Lacunarity ("Forma base: lacunarità", Float) = 2
        _Gain       ("Forma base: gain", Float) = 0.55
        _Seed       ("Forma base: seme", Int) = 1337

        _SoilMean ("Suolo: colore base", Color) = (0.44, 0.44, 0.45, 1)
        _SoilTint ("Suolo: tinta", Color) = (1.0, 1.0, 1.02, 1)
        _MacroVar ("Variazione macro", Range(0,1)) = 0.45
        _MacroScale ("Variazione macro: scala", Float) = 5

        _MineralA ("Minerale: caldo", Color) = (1.18, 0.92, 0.74, 1)
        _MineralB ("Minerale: freddo", Color) = (0.82, 0.92, 1.08, 1)
        _MineralStr ("Minerale: forza", Range(0,1)) = 0.18
        _MineralScale ("Minerale: scala", Float) = 1.8

        _PeakColor ("Vette", Color) = (0.74, 0.76, 0.70, 1)
        _PeakStr ("Vette: forza", Range(0,1)) = 0.5

        _MariaColor ("Bacini: scurezza", Color) = (0.52, 0.52, 0.56, 1)
        _MariaScale ("Bacini: scala", Float) = 2.2
        _MariaStr ("Bacini: forza", Range(0,1)) = 0.7

        _SeaOn ("Mare attivo", Float) = 0
        _SeaLevel ("Mare: raggio pelo", Float) = 0
        _SeaColor ("Mare: colore", Color) = (0.13, 0.33, 0.52, 1)
        _SeaSat ("Mare: saturazione", Range(0,2)) = 1
        _SeaRough ("Mare: rugosità (m)", Float) = 0
        _SeaRoughScale ("Mare: scala rugosità", Float) = 3
        _SeaForma ("Mare: forma fondale", Range(-1,1)) = 0
        _SeaSeed ("Mare: seme", Float) = 4242
        _SeaLiquid ("Mare: liquido (acqua)", Float) = 0
        _SeaClear ("Mare: trasparente", Float) = 0
        _SeaClarity ("Mare: limpidezza (m)", Float) = 8

        _Saturation ("Saturazione", Range(0,2)) = 1
        _Cull ("Cull mode (0=Off 1=Front 2=Back)", Float) = 0   // guidato dal MATERIALE (interno=Back, skirt=Off): dimezza l'overdraw
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            // Cull Off: gli SKIRT (anelli tappabuchi ai confini di LOD) DEVONO essere a doppia faccia — coprono la
            // fessura da qualunque angolo. Con Cull Back sparivano dal lato sbagliato → buchi/muri. La superficie
            // interna avrebbe verso coerente, ma non si può avere Cull diverso per interno e skirt nello stesso draw.
            // OVERDRAW: il valore è guidato dal MATERIALE (NON da un MaterialPropertyBlock, che in built-in non
            // cambia lo stato fisso Cull — verificato). Due materiali: interno con Cull Back (verso coerente → niente
            // retro-facce ombreggiate = metà overdraw del fragment) e skirt con Cull Off (devono restare doppia faccia).
            Cull [_Cull]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5          // StructuredBuffer + SV_InstanceID nel vertex shader (Metal lo regge)
            #include "UnityCG.cginc"
            #include "PlanetProceduralShade.cginc"

            StructuredBuffer<float> _VPos;       // pool: 3 float per vertice (x,y,z), spazio OGGETTO (pianeta centrato)
            StructuredBuffer<float> _VNrm;       // pool: normale del pelo (3 float/v)
            StructuredBuffer<float> _VBedNrm;    // pool: normale del fondo sommerso (3 float/v)
            StructuredBuffer<float> _VDepth;     // pool: profondità acqua (1 float/v)
            StructuredBuffer<float> _VField;     // pool: baseN per-vertice (ondulazione di base, per le maschere colore)
            StructuredBuffer<float> _VSurf;      // pool: quota del pelo del mare (1 float/v) → maschera del mare esatta, niente ricostruzione
            StructuredBuffer<uint>  _SlabOfInstance;   // istanza → indice di fetta nel pool
            uint _VertsPerSlab;                  // vertici per fetta (gp*gp)
            float4x4 _ObjectToWorld;             // pianeta locale → mondo (floating origin: aggiornata ogni frame)
            float _DebugView;                    // diagnosi: >0.5 → colore radiale (ignora la luce) per isolare geometria vs shading

            // --- GEOMORPH (CDLOD): transizione LISCIA fra livelli di LOD, niente pop né cuciture/lamelle nere.
            StructuredBuffer<float> _SplitDistOfInstance;  // distanza di split per istanza (= worldSize·lodFactor del nodo)
            uint _NN;                            // vertici per lato del nodo (nodeRes+1): per ricavare (i,j) dal vid e leggere i vicini
            float _MorphRange;                   // ampiezza della banda di morph (frazione di splitDist)
            float _UseGeomorph;                  // 1 = geomorph attivo, 0 = solo skirt (confronto A/B)
            float3 _CamPosWorld;                 // posizione camera in mondo (shader dai-buffer: passata a mano)

            float3 GeoLoadPos(uint gi) { return float3(_VPos[gi * 3], _VPos[gi * 3 + 1], _VPos[gi * 3 + 2]); }

            // Spostamento del vertice (i,j) verso la forma del GENITORE (metà risoluzione), trasposto dal path CPU
            // (PlanetQuadtree.AssembleFromGrid): i vertici a indice PARI sono già sulla griglia del genitore (delta 0);
            // i DISPARI diventano la media dei due pari adiacenti — lungo l'asse dispari, o la DIAGONALE per gli odd-odd
            // (coerente con la diagonale della triangolazione, i00→i11). Letto da _VPos (niente buffer extra: il collo
            // è il fragment). nodeRes pari → i bordi sono pari → i vicini cadono sempre dentro la griglia.
            float3 GeoMorphDelta(uint slabBase, int i, int j, float3 p)
            {
                int N = (int)_NN;
                bool ox = (i & 1) == 1, oy = (j & 1) == 1;
                if (!ox && !oy) return float3(0, 0, 0);
                if (ox && !oy)  return 0.5 * (GeoLoadPos(slabBase + (uint)((i - 1) + j * N)) + GeoLoadPos(slabBase + (uint)((i + 1) + j * N))) - p;
                if (!ox && oy)  return 0.5 * (GeoLoadPos(slabBase + (uint)(i + (j - 1) * N)) + GeoLoadPos(slabBase + (uint)(i + (j + 1) * N))) - p;
                return 0.5 * (GeoLoadPos(slabBase + (uint)((i - 1) + (j - 1) * N)) + GeoLoadPos(slabBase + (uint)((i + 1) + (j + 1) * N))) - p;
            }

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 nrm : TEXCOORD0;   // normale del pelo in MONDO
                float3 lp  : TEXCOORD1;   // posizione OGGETTO (per i campi di colore radiali)
                float  depth : TEXCOORD2;
                float3 bnrm : TEXCOORD3;  // normale del fondo sommerso in MONDO
                float3 wp  : TEXCOORD4;   // posizione MONDO (per il vettore vista del glint)
                float  baseN : TEXCOORD5; // ondulazione di base per-vertice (interpolata → niente rumore per-pixel)
                float  seaSurf : TEXCOORD6; // quota del pelo del mare per-vertice (maschera esatta)
            };

            v2f vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                v2f o;
                uint g = _SlabOfInstance[iid] * _VertsPerSlab + vid;   // vertice globale nel pool
                float3 p  = float3(_VPos[g * 3],    _VPos[g * 3 + 1],    _VPos[g * 3 + 2]);
                float3 n  = float3(_VNrm[g * 3],    _VNrm[g * 3 + 1],    _VNrm[g * 3 + 2]);
                float3 bn = float3(_VBedNrm[g * 3], _VBedNrm[g * 3 + 1], _VBedNrm[g * 3 + 2]);

                // GEOMORPH: morfa p verso la forma del genitore in base alla distanza camera. Puramente visivo —
                // il walker segue SampleHeight (CPU) e da vicino mf→0, quindi mesh e collisione combaciano dove conta.
                if (_UseGeomorph > 0.5)
                {
                    uint slabBase = _SlabOfInstance[iid] * _VertsPerSlab;
                    uint interior = _NN * _NN;
                    int i, j;
                    if (vid < interior) { i = (int)(vid % _NN); j = (int)(vid / _NN); }
                    else
                    {   // SKIRT: morfa col vertice di BORDO corrispondente (resta attaccato mentre il bordo morfa)
                        uint res = _NN - 1u;
                        uint k = vid - interior, e = k / res, t = k % res;
                        if (e == 0u)      { i = (int)t;          j = 0; }
                        else if (e == 1u) { i = (int)res;        j = (int)t; }
                        else if (e == 2u) { i = (int)(res - t);  j = (int)res; }
                        else              { i = 0;               j = (int)(res - t); }
                    }
                    // per gli skirt il delta è quello del vertice di BORDO (pRef = posizione del bordo, non dello skirt)
                    float3 pRef = (vid < interior) ? p : GeoLoadPos(slabBase + (uint)(i + j * (int)_NN));
                    float3 delta = GeoMorphDelta(slabBase, i, j, pRef);
                    float splitDist = _SplitDistOfInstance[iid];
                    if (splitDist > 0.0)
                    {
                        // CLAMP di sicurezza: lo spostamento di morph non può superare la SCALA del nodo (splitDist).
                        // Sul morph normale delta è piccolissimo (curvatura locale ≪ splitDist) → nessun effetto; ma
                        // se per qualunque ragione un vicino fosse anomalo, il vertice NON può schizzare via e
                        // ribaltare il triangolo in uno spuntone. Rete di sicurezza, non cambia la transizione liscia.
                        float dl = length(delta);
                        if (dl > splitDist) delta *= splitDist / dl;
                        float3 w0 = mul(_ObjectToWorld, float4(p, 1.0)).xyz;
                        float d = distance(w0, _CamPosWorld);
                        float mf = saturate((d - splitDist * (1.0 - _MorphRange)) / (splitDist * _MorphRange));
                        p += delta * mf;
                    }
                }

                // RETE DI SICUREZZA: nessun vertice spazzatura (LONTANO o NaN/Inf) diventa uno spuntone. La superficie
                // sta a ~_BaseRadius, gli skirt scendono; niente di legittimo supera ~1.1× il raggio. La forma negata
                // "!(plen < 1.3×)" è vera SIA oltre 1.3× SIA per i NaN (un NaN non è "< " di niente → la versione
                // "plen > soglia" li lasciava passare, ecco perché lo spuntone tornava). Lo collasso sul vertice
                // d'ANGOLO della fetta (posizione valida e vicina) → l'invalido sparisce in un triangolo degenere.
                float plen = length(p);
                if (!(plen < _BaseRadius * 1.3)) p = GeoLoadPos(_SlabOfInstance[iid] * _VertsPerSlab);

                float3 world = mul(_ObjectToWorld, float4(p, 1.0)).xyz;
                o.pos  = UnityWorldToClipPos(world);
                o.nrm  = normalize(mul((float3x3)_ObjectToWorld, n));    // rotazione+scala uniforme: la 3x3 basta
                o.bnrm = normalize(mul((float3x3)_ObjectToWorld, bn));
                o.lp   = p;        // spazio oggetto: i campi di colore sono centrati sul pianeta
                o.wp   = world;
                o.depth = _VDepth[g];
                o.baseN = _VField[g];
                o.seaSurf = _VSurf[g];
                return o;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // DIAGNOSI. 2 = NORMALE di mondo (ciò che la luce consuma): rainbow liscio = normali OK → il bug è
                // il sole; nero/uniforme/garbage = normali rotte. 1 = posizione radiale (geometria, già confermata OK).
                if (_DebugView > 1.5)
                    return fixed4(normalize(IN.nrm) * 0.5 + 0.5, 1);
                if (_DebugView > 0.5)
                    return fixed4(normalize(IN.lp) * 0.5 + 0.5, 1);

                // ANTI-ALIASING della normale (mipmap ANALITICO): da lontano il corpo è piccolo e il rilievo dei
                // crateri cade sotto il pixel → la normale "scintilla" (sale e pepe). fwidth(N) misura quanto la
                // normale cambia PER PIXEL: dove cambia tanto (dettaglio sub-pixel) sfumo verso la normale LISCIA
                // della sfera (radiale) → shading liscio, niente scintillio. Da vicino (fwidth piccolo) resta il
                // dettaglio pieno. Costo: due derivate. (La via giusta per questo progetto: niente texture da mippare.)
                float3 N = normalize(IN.nrm);
                float nv = length(fwidth(N));
                float3 center = mul(_ObjectToWorld, float4(0.0, 0.0, 0.0, 1.0)).xyz;
                float3 radialW = normalize(IN.wp - center);                  // normale liscia della sfera (mondo)
                float detail = smoothstep(0.8, 0.12, nv);                    // nv alto → 0 (liscio); nv basso → 1 (pieno)
                float3 nrmAA = normalize(lerp(radialW, N, detail));
                return fixed4(PlanetShade(IN.lp, IN.wp, nrmAA, IN.bnrm, IN.depth, IN.baseN, IN.seaSurf), 1);
            }
            ENDCG
        }
    }
}

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
        _Cull ("Cull mode (0=Off 1=Front 2=Back)", Float) = 0   // guidato dal MATERIALE: scartare le retro-facce dimezza l'overdraw

        // PBR / materiali per pendenza (GPU-4): roccia sui versanti ripidi + speculare GGX leggero. Default tenui.
        _RockColor ("PBR: tinta roccia (versanti)", Color) = (0.62, 0.58, 0.54, 1)
        _RockSlopeStart ("PBR: pendenza inizio roccia", Range(0,1)) = 0.30
        _RockSlopeEnd ("PBR: pendenza roccia piena", Range(0,1)) = 0.72
        _RockStr ("PBR: forza roccia", Range(0,1)) = 0.55
        _SpecStr ("PBR: forza speculare suolo", Range(0,1)) = 0.10
        _Gloss ("PBR: lucentezza (esponente)", Float) = 22
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            // OVERDRAW: il _Cull è guidato dal MATERIALE (NON da un MaterialPropertyBlock, che in built-in non cambia
            // lo stato fisso Cull — verificato). Front scarta le retro-facce della superficie (verso coerente, niente
            // skirt da tenere a doppia faccia) → metà ombreggiatura per-pixel del terreno in un solo draw.
            Cull [_Cull]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5          // StructuredBuffer + SV_InstanceID nel vertex shader (Metal lo regge)
            // VISTE DEBUG isolate: in gioco (keyword OFF) tutto il codice di diagnosi è STRIPPATO dalla variante →
            // costo ZERO. La C# accende PLANET_DEBUG_VIEW solo quando DebugView>0 (cambia la variante on-demand).
            #pragma multi_compile _ PLANET_DEBUG_VIEW
            // _HAS_SEA (GPU-2): variante col mare SOLO sui corpi che hanno acqua (C# accende la keyword se la ricetta
            // ha un mare). Sui corpi asciutti (Cetra/Luna6) tutto il blocco acqua del fragment è STRIPPATO (più snello).
            #pragma shader_feature_local _HAS_SEA
            // _PBR_TERRAIN (GPU-4): roccia per pendenza + GGX. Variante separata → costo zero quando spento (A/B da C#).
            #pragma shader_feature_local _PBR_TERRAIN
            #include "UnityCG.cginc"
            #ifdef _HAS_SEA
            #define WANDERER_HAS_SEA
            #endif
            #ifdef _PBR_TERRAIN
            #define WANDERER_PBR
            #endif
            #include "PlanetProceduralShade.cginc"

            StructuredBuffer<float> _VPos;       // pool: 3 float per vertice (x,y,z), spazio OGGETTO (pianeta centrato)
            StructuredBuffer<float> _VNrm;       // pool: normale del pelo (3 float/v)
            StructuredBuffer<float> _VBedNrm;    // pool: normale del fondo sommerso (3 float/v)
            StructuredBuffer<float> _VDepth;     // pool: profondità acqua (1 float/v)
            StructuredBuffer<float> _VField;     // pool: baseN per-vertice (ondulazione di base, per le maschere colore)
            StructuredBuffer<float> _VSurf;      // pool: quota del pelo del mare (1 float/v) → maschera del mare esatta, niente ricostruzione
            StructuredBuffer<float> _VColor;     // pool: 3 fbm value-noise di colore per-vertice (macro/minerali/maria) → GPU-1
            StructuredBuffer<uint>  _SlabOfInstance;   // istanza → indice di fetta nel pool
            uint _VertsPerSlab;                  // vertici per fetta (gp*gp)
            float4x4 _ObjectToWorld;             // pianeta locale → mondo (floating origin: aggiornata ogni frame)
            float _DebugView;                    // diagnosi: >0.5 → colore radiale (ignora la luce) per isolare geometria vs shading

            // --- GEOMORPH (CDLOD): transizione LISCIA fra livelli di LOD, niente pop né cuciture/lamelle nere.
            StructuredBuffer<float> _SplitDistOfInstance;  // distanza di split per istanza (= worldSize·lodFactor del nodo)
            StructuredBuffer<float4> _DirOfInstance;        // anti-spuntone: direzione-centro del nodo per istanza (w inutilizzato)
            StructuredBuffer<uint>  _RegionOfInstance;      // region-stamp: id regione UINT atteso per istanza
            StructuredBuffer<uint>  _SlabRegion;            // region-stamp: id regione UINT DAVVERO nella fetta (scritto dal fill)
            uint _NN;                            // vertici per lato del nodo (nodeRes+1): per ricavare (i,j) dal vid e leggere i vicini
            float _MorphRange;                   // ampiezza della banda di morph (frazione di splitDist)
            float _UseGeomorph;                  // 1 = geomorph attivo, 0 = spento (confronto A/B)
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
                float3 colorF : TEXCOORD8; // campi colore per-vertice (macro/minerali/maria), interpolati → GPU-1
            #ifdef PLANET_DEBUG_VIEW
                float3 dbg : TEXCOORD7;   // DIAGNOSI (solo variante debug): colore per-istanza (livello/faccia/fetta)
            #endif
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
                    int i = (int)(vid % _NN), j = (int)(vid / _NN);   // la fetta è solo griglia interna (niente skirt)
                    float3 delta = GeoMorphDelta(slabBase, i, j, p);
                    // CLAMP di sicurezza: lo spostamento non può superare la SCALA del nodo (anti-spuntone su vicino anomalo).
                    float splitDist = _SplitDistOfInstance[iid];
                    float dl = length(delta);
                    if (splitDist > 0.0 && dl > splitDist) delta *= splitDist / dl;
                    // MORPH CDLOD CONTINUO (la sola cosa che chiude le cuciture, senza skirt né stitch): il fattore mf è una
                    // funzione CONTINUA della distanza, uguale per tutte le foglie dello stesso livello → due foglie vicine
                    // alla stessa distanza calcolano lo STESSO mf e combaciano per costruzione. Completa quando il nodo si
                    // FONDE nel genitore (≈2·splitDist): a quel confine la foglia è sulla forma del genitore (mf=1) e il
                    // vicino più grosso, lì appena comparso a pieno dettaglio, È quella stessa forma → niente crepa. Richiede
                    // confini di LOD NETTI (mergeHysteresis=1, lato CPU): senza banda morta il mf combacia esatto.
                    if (splitDist > 0.0)
                    {
                        float3 w0 = mul(_ObjectToWorld, float4(p, 1.0)).xyz;
                        float d = distance(w0, _CamPosWorld);
                        float mergeDist = splitDist * 2.0;
                        float mf = saturate((d - mergeDist * (1.0 - _MorphRange)) / (mergeDist * _MorphRange));
                        p += delta * mf;
                    }
                }

                // RETE DI SICUREZZA anti-spuntone — LUNGHEZZA **e** DIREZIONE. Un vertice spazzatura è (a) LONTANO o
                // NaN [la forma negata !(plen<1.3×) prende anche i NaN], OPPURE (b) punta in una DIREZIONE troppo
                // diversa dal centro del nodo = la fetta tiene la geometria di un'ALTRA regione del pianeta (lunghezza
                // giusta, posto sbagliato — ed è la causa vera trovata dall'audit: la sola magnitudine non la vedeva).
                // In entrambi i casi collasso sull'ÀNCORA valida data dalla CPU (direzione-centro × raggio): la
                // geometria invalida sparisce in un triangolo degenere vicino, mai uno spuntone in cielo. I vertici
                // interni (direzione entro il nodo, lunghezza ≈ raggio) passano indenni.
                float3 expectedDir = _DirOfInstance[iid].xyz;
                float plen = length(p);
                float3 vdir = (plen > 1e-4) ? (p / plen) : expectedDir;
                float nodeAng = (_SplitDistOfInstance[iid] / 3.0) / max(_BaseRadius, 1.0);   // ≈ worldSize/raggio (lodFactor=3)
                float maxAng = clamp(nodeAng * 2.5, 0.02, 1.6);                              // estensione del nodo + margine
                // REGION-STAMP: la fetta tiene DAVVERO la regione attesa? L'id UINT scritto dal fill (_SlabRegion) deve
                // combaciare ESATTAMENTE con quello dell'istanza (_RegionOfInstance). Se no, è una fetta-fantasma (churn
                // evict→refill non ancora applicato) → collasso TUTTI i suoi vertici → la geometria vecchia sparisce,
                // niente lama in cielo. Confronto INTERO esatto (id uint fino a 2^32) → niente più limite ~7 corpi vivi.
                bool stale = _SlabRegion[_SlabOfInstance[iid]] != _RegionOfInstance[iid];
                if (stale || !(plen < _BaseRadius * 1.3) || dot(vdir, expectedDir) < cos(maxAng))
                    p = expectedDir * _BaseRadius;

                float3 world = mul(_ObjectToWorld, float4(p, 1.0)).xyz;
                o.pos  = UnityWorldToClipPos(world);
                o.nrm  = normalize(mul((float3x3)_ObjectToWorld, n));    // rotazione+scala uniforme: la 3x3 basta
                o.bnrm = normalize(mul((float3x3)_ObjectToWorld, bn));
                o.lp   = p;        // spazio oggetto: i campi di colore sono centrati sul pianeta
                o.wp   = world;
                o.depth = _VDepth[g];
                o.baseN = _VField[g];
                o.seaSurf = _VSurf[g];
                o.colorF = float3(_VColor[g * 3], _VColor[g * 3 + 1], _VColor[g * 3 + 2]);   // GPU-1: campi colore per-vertice
            #ifdef PLANET_DEBUG_VIEW
                // DIAGNOSI per-istanza (mostrata nel frag come colore piatto). Modalità in _DebugView:
                //   3 = LIVELLO di LOD (da splitDist)  ·  4 = FACCIA del cubo  ·  5 = FETTA (hash dell'indice)
                o.dbg = float3(0.0, 0.0, 0.0);
                if (_DebugView > 4.5)        // 5: ogni fetta un colore (vedi bordi di fetta + dimensioni)
                {
                    uint si = _SlabOfInstance[iid];
                    uint hh = si * 2654435761u; hh ^= hh >> 15; hh *= 2246822519u; hh ^= hh >> 13;
                    o.dbg = float3((hh & 255u) / 255.0, ((hh >> 8) & 255u) / 255.0, ((hh >> 16) & 255u) / 255.0);
                }
                else if (_DebugView > 3.5)   // 4: colore per faccia del cubo (cuciture fra facce)
                {
                    float3 cd = _DirOfInstance[iid].xyz; float3 ad = abs(cd);
                    float fidx = (ad.x >= ad.y && ad.x >= ad.z) ? (cd.x > 0.0 ? 0.0 : 1.0)
                               : (ad.y >= ad.z)                 ? (cd.y > 0.0 ? 2.0 : 3.0)
                                                                : (cd.z > 0.0 ? 4.0 : 5.0);
                    o.dbg = frac(fidx * float3(0.37, 0.61, 0.83) + 0.13);
                }
                else if (_DebugView > 2.5)   // 3: colore per livello di LOD (transizioni di livello)
                {
                    o.dbg = frac(log2(max(_SplitDistOfInstance[iid], 1e-3)) * float3(0.37, 0.61, 0.83) + 0.5);
                }
            #endif
                return o;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
            #ifdef PLANET_DEBUG_VIEW
                // DIAGNOSI (solo variante debug, strippata in gioco). _DebugView: 1=posizione radiale (geometria) ·
                // 2=normale di mondo (shading) · 3=livello LOD · 4=faccia del cubo · 5=fetta. Le 3/4/5 usano o.dbg
                // (per-istanza) × luce semplice; un taglio DENTRO lo stesso colore-fetta = feature della geometria.
                if (_DebugView > 2.5)
                {
                    float lit = 0.35 + 0.65 * saturate(dot(normalize(IN.nrm), _SunDir));
                    return fixed4(IN.dbg * lit, 1);
                }
                if (_DebugView > 1.5)
                    return fixed4(normalize(IN.nrm) * 0.5 + 0.5, 1);
                if (_DebugView > 0.5)
                    return fixed4(normalize(IN.lp) * 0.5 + 0.5, 1);
            #endif

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
                return fixed4(PlanetShade(IN.lp, IN.wp, nrmAA, IN.bnrm, IN.depth, IN.baseN, IN.seaSurf, IN.colorF), 1);
            }
            ENDCG
        }
    }
}

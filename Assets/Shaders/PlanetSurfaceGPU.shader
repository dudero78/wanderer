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
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            // Cull Off: gli SKIRT (anelli tappabuchi ai confini di LOD) DEVONO essere a doppia faccia — coprono la
            // fessura da qualunque angolo. Con Cull Back sparivano dal lato sbagliato → buchi/muri. La superficie
            // interna avrebbe verso coerente, ma non si può avere Cull diverso per interno e skirt nello stesso draw.
            // Il dimezzamento del per-pixel via culling tornerà col quadtree 2:1 (niente skirt) o un depth pre-pass.
            Cull Off
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
                return fixed4(PlanetShade(IN.lp, IN.wp, IN.nrm, IN.bnrm, IN.depth, IN.baseN, IN.seaSurf), 1);
            }
            ENDCG
        }
    }
}

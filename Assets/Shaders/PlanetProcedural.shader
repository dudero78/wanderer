Shader "Wanderer/PlanetProcedural"
{
    // Resa dell'anteprima GPU dell'editor (Tappe 1-3 del percorso "GPU per l'editor").
    // Disegna la superficie leggendo posizioni e normali DIRETTAMENTE da due StructuredBuffer riempiti dal
    // compute (PlanetHeight.compute), SENZA readback: il vertice indicizza il buffer con SV_VertexID.
    //
    // COLORE (Tappa 3): ricalcolato nel fragment dai parametri della RICETTA, NON da texture bakate. Scelta
    // architetturale (vedi CLAUDE.md / TODO): risoluzione infinita, zero bake all'avvio, GPU-first; il bake
    // resta utile solo per simulazioni costose (erosione/AO), non per il colore. Mirroring della catena di
    // Wanderer/PlanetBaked (suolo+macro, minerali, vette, bacini, MARE+saturazione), con fbm/n3_fbm fedeli a
    // Noise3D → il mare segue la geometria allagata. È la FONDAZIONE su cui crescerà il layering per pendenza/quota.
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
            // Cull Off: il verso dei triangoli dipende dall'orientamento degli assi delle 6 facce del cubo.
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5          // StructuredBuffer nel vertex shader (Metal lo regge)
            #include "UnityCG.cginc"
            #include "PlanetProceduralShade.cginc"   // colore procedurale CONDIVISO con la resa in gioco

            StructuredBuffer<float> _VPos;   // 3 float per vertice (x,y,z), buffer piatto
            StructuredBuffer<float> _VNrm;
            StructuredBuffer<float> _VBedNrm; // 3 float per vertice: normale del fondo sommerso (rilievo del fondale)
            StructuredBuffer<float> _VDepth; // 1 float per vertice: profondità dell'acqua (pelo − fondo)

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 nrm : TEXCOORD0;
                float3 lp  : TEXCOORD1;   // posizione in spazio oggetto (= mondo, pianeta all'origine)
                float  depth : TEXCOORD2; // profondità dell'acqua al vertice, interpolata sul triangolo
                float3 bnrm : TEXCOORD3;  // normale del fondo sommerso (rilievo del fondale)
            };

            v2f vert(uint vid : SV_VertexID)
            {
                v2f o;
                float3 p = float3(_VPos[vid * 3], _VPos[vid * 3 + 1], _VPos[vid * 3 + 2]);
                float3 n = float3(_VNrm[vid * 3], _VNrm[vid * 3 + 1], _VNrm[vid * 3 + 2]);
                float3 bn = float3(_VBedNrm[vid * 3], _VBedNrm[vid * 3 + 1], _VBedNrm[vid * 3 + 2]);
                o.pos = UnityObjectToClipPos(p);
                o.nrm = UnityObjectToWorldNormal(n);
                o.bnrm = UnityObjectToWorldNormal(bn);
                o.lp = p;
                o.depth = _VDepth[vid];
                return o;
            }

            // anteprima editor: pianeta all'origine → spazio oggetto == mondo (lp serve sia ai campi di colore
            // sia al vettore vista del glint). Il colore è nell'include condiviso PlanetShade.
            fixed4 frag(v2f IN) : SV_Target
            {
                return fixed4(PlanetShade(IN.lp, IN.lp, IN.nrm, IN.bnrm, IN.depth), 1);
            }
            ENDCG
        }
    }
}

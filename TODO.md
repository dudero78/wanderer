# Wanderer — TODO

Lista di lavoro che sopravvive tra le sessioni. Aggiornata al **3 giugno 2026** (sessione editor estesa).
Dettaglio tecnico nel `CLAUDE.md`.

> **PROSSIMA SESSIONE — PARTI DA QUI:** GPU per l'editor (B1), **tappa 1 = render diretto dai buffer GPU**
> (no readback) col pezzo HLSL già provato (base+crateri). Vedi sezione "Prossimo". Tutto committato su `main`.

## Fatto (milestone)

- ✅ Fondamenta: doppia precisione + floating origin, orbita kepleriana, gravità radiale.
- ✅ Volo a due modelli (`N`: Crociera / Newtoniano), tuta + torcia, volo libero, rollio Q/E.
- ✅ **Viaggio fra corpi**: origine ancorata al corpo di riferimento, **match-velocity (`X`)**, spinta scalata
  alla gravità (decolli da qualunque corpo), velocità-universo preservata allo switch. `TimeScale=1`.
- ✅ **Mappa (`M`)** + selezione destinazione, **indicatore di rotta** (`RouteIndicator`).
- ✅ **Orbite a schermo (`O`)**: fili luminosi alla Outer Wilds (shader `Wanderer/OrbitLine`, spessore
  costante in px, glow + coda al pianeta; mesh-nastro cacheata, zero alloc).
- ✅ **Autopilota (`T`)** hands-off, **impostazioni a TAB (`à`)**, **gauge di frenata** onesta. **Stop dolce**
  all'interruzione (opzione, default ON, frenata > X). **Nessun tetto di crociera** (solo soffitto di sicurezza
  alto): l'autopilota va più veloce sulle tratte lunghe.
- ✅ **Freno X**: decel a tre fasce (alta velocità proporzionale → frena forte da migliaia di m/s; coda con
  floor che fa scorrere svelti gli ultimi numeri). Isteresi sull'ancora (`NearestBody`) → niente sobbalzo di
  inquadratura a metà fra due corpi.
- ✅ **Build standalone** funziona (scena nei Build Settings + shader Always Included; HUD scalato).
- ✅ **Crateri** come geometria vera (`CraterTerrainLayer`, profilo a legge di potenza `rimSharpness`) + normale
  bakeata per i bordi fini.
- ✅ **#14 Editor di pianeti + ricette**: scena separata (menu "Apri editor pianeti"), `PlanetRecipe`
  (forma base + pipeline crateri + colore), anteprima live, salva/carica JSON. Ricette ufficiali in
  `Resources/Planets/*.json`. `ScaledTo(raggio)` conserva l'aspetto su raggi diversi.
- ✅ **Quadtree CDLOD** (`PlanetQuadtree`) = renderer attivo dei corpi rocciosi (geomorph + skirt + cache LRU +
  async). Toggle `useQuadtree` (default ON); `SingleMeshPlanet` fallback. Geomorph completa entro splitDist;
  skirt dimensionato sul salto di morph del bordo (niente fessure).
- ✅ **#7 Secondo corpo: Cetra** (luna marziana craterizzata, r300, g3, orbita attorno al pianeta).
- ✅ **#13 Bake su disco multi-corpo** ("Bake planet assets": pianeta + Cetra in cartelle dedicate;
  `TryLoadBakedMaterials(terrain, dir)`). `BakedPlanet*` in `.gitignore` (cache rigenerabili).
- ✅ Colore dei corpi dalla ricetta (`BuildMaterial` imposta `_SoilMean/_MariaColor/...`).
- ✅ Menu "Crea scena di gioco" (crea `Game.unity` + la registra nei Build Settings).
- ✅ **Mappa potenziata**: marker **"TU SEI QUI"** alla posizione del giocatore (sollevato sopra il corpo su cui
  sei) + **scia della traiettoria** percorsa (filo a coda di cometa, in coordinate-universo, ring buffer ~43 km,
  scarta i salti da ri-ancoraggio) + **#8 corpi reali**: ogni corpo con ricetta è un proxy craterizzato (mesh a
  bassa res + materiali bakeati, illuminato dal sole) al posto del disco piatto; il marker-sfera resta bersaglio
  di click invisibile.
- ✅ **Eclissi analitiche** (`EclipseDriver` + shader): un corpo fra il sole e un altro gli proietta un'ombra
  vera. Calcolata nello shader come copertura del disco solare via dimensioni ANGOLARI (spazio oggetto) → niente
  shadow map, zero acne, nessun limite di shadow distance, e l'ombra **sbiadisce con la distanza** dall'occlusore
  (umbra finita → penombra). Visibile anche sui proxy in mappa.
- ✅ **EDITOR = generatore di pianeti ricco (sessione 3 giu):** la ricetta è una **lista ORDINATA di PROCESSI**
  tipizzati (`ProcessStep`/`ProcessType`), l'ordine conta. Tipi:
  - **Crateri**: rimescola/casuale, quote per taglia (grandi/medi/piccoli), "distribuzione" (ruota il campo → li
    fa scorrere sul pianeta), seed casuale sui nuovi bombardamenti.
  - **Mari GEOMETRICI** (allagamento solido walkable, non più solo colore): livello (range fine), saturazione
    propria, rilievo del fondale con "forma" creste↔liscio↔gobbe. Lo shader ricostruisce il pelo via `n3_fbm`
    (fedele a `Noise3D`) per tingere seguendo la geometria.
  - **Tettonica**: placche (soft Voronoi → quota CONTINUA, niente muri-bug), continenti/oceani, catene/rift ai
    confini, coste frastagliate (warp frattale) + dolcezza coste. Col Mare = look terrestre.
  - UI a fisarmonica + tooltip; riordino Su/Giù; "+ Nuova pipeline" sceglie il tipo. Texture suolo (tinta
    visibile) + saturazione. **Anteprima ASINCRONA su thread** (slider fluidi: bassa res nel drag, full res al
    rilascio). **Bake dal pulsante**; **"Carica" = file picker** sulla cartella dei pianeti.
- ✅ **Luna** (terzo corpo): creato nell'editor, r800, in orbita al SOLE (semiasse 95000). Ricetta versionata
  `Resources/Planets/Luna.json`; aggiunta al comando "Bake planet assets".

## Accantonato (deciso ma rimandato)

- ⏸️ **Stitch di LOD** (transizioni di shading "scalini" ai confini): niente fessure/buchi, ma restano i salti
  di shading (peggio coi salti di 2+ livelli). Fix definitivo = **quadtree bilanciato 2:1** (vicini ≤ 1 livello
  → il morph di un livello basta, si possono togliere gli skirt). Rimandato: troppo tempo, avanti col gioco.
- ⏸️ **Salti/scarpate netti scalettati nell'ANTEPRIMA editor** (3 giu, decisione: NON priorità ora). I gradini sul
  bordo di un salto netto sono **aliasing dell'heightfield** (una linea netta su griglia a res fissa = scala, come
  una diagonale su pixel). NON risolvibile con shading (provata "roccia sulle scarpate" → gole nere, scartata).
  Cura vera = **LOD**: il GIOCO ce l'ha (quadtree, fine vicino alla camera → gradini sub-pixel); l'anteprima editor
  usa mesh a res fissa. Fix = far usare all'editor il quadtree (rebuild + switch drag/zoom). RIMANDATO: il GPU per
  l'editor (sotto) lo risolve gratis (res altissima a costo nullo). Verificare se il gioco già le rende pulite.

## Prossimo — FOCUS: GPU per l'editor (= B1)

- ⬜ **GPU per l'editor — render diretto dai buffer (deciso con Dario, È la rotta).** Spostare TUTTO il calcolo
  della geometria dell'editor sulla GPU e disegnarla **dai buffer GPU senza readback** (era il blocco di B2).
  L'editor è il **beachhead ideale**: nessun walker, nessuna collisione → puro visivo. Doppio guadagno: anteprima
  **full-res LIVE** (niente thread/bassa-res, e la res altissima risolve anche i gradini delle scarpate) **+** il
  lavoro è riusabile pari pari per i corpi in gioco (B1). La colorazione resta lo shader `PlanetBaked` (lavora su
  qualunque mesh) → NON va rifatta; si GPU-genera solo la GEOMETRIA. **TAPPE (verificabili una a una):**
  - **(1) ← PARTI DA QUI: render-dai-buffer** col pezzo HLSL GIÀ PROVATO (base+crateri, `PlanetHeight.compute`):
    compute riempie un vertex buffer → `DrawProcedural`/`RenderPrimitives`, niente readback, disegnato nell'editor.
    Sblocca lo schema (prova che il no-readback funziona).
  - **(2) estendere l'HLSL** a mari (rugosità/forma) e tettonica (placche/soft-Voronoi/warp/uplift), con **parità**
    a ogni passo (menu "Test parità altezza GPU↔CPU", da estendere per processo).
  - **(3) cablare l'editor** sulla mesh GPU (anteprima full-res live), dismettere il percorso CPU del preview.
  - Disciplina: pipeline in 2 lingue (C# per walker/bake + HLSL per render GPU), la **parità fa da rete**.
  - Fondazione già pronta: `PlanetHeight.compute` (base+crateri provati al millimetro), `GpuHeightBaker.cs`,
    `PlanetGpuParityTest.cs`; bug Metal `float3` chiuso → **buffer piatto di float**. B2 (compute→readback→mesh CPU)
    PARCHEGGIATO perché il readback TRASCINA (il commento in `GameBootstrap.AddSurface` spiega come riattivare il
    path CPU del gioco). Poi: stessa resa GPU sostituisce SingleMeshPlanet/quadtree IN GIOCO.
- ⬜ **Acqua liquida** (toggle "liquido" sul Mare): resa trasparente + nuoto/affondamento. Condivide l'allagamento.
- ⬜ **Altri processi**: montagne (ridged noise per la texture delle catene), ghiaccio, erosione (bake?).
- ⬜ **Migliorie editor**: editing per-feature (cancella/modifica singolo cratere), più preset.

## Il GIOCO

- ⬜ **#10 Teletrasporto** a un corpo selezionato (appoggiato al ri-ancoraggio; corpi residenti all'avvio).
- ⬜ **#9 Mini-loop giocabile (IL VERBO)**: atterra · cammina · raccogli · vai altrove · puoi fallire. L'MVP.
- ⬜ Altri corpi DIVERSI (creati con l'editor).

## Più avanti (idee concordate, NON ora)

- Generazione pianeti da composizione chimica → ricetta → parametri (l'editor/ricetta è la base).
- Giganti gassosi / stelle come **volumi** (secondo renderer volumetrico raymarch), non mesh walkable.
- Acqua e atmosfera come pass separati (guscio/volumetrico).
- Proiezione non-rettilineare (stereografica/Panini) come post-process per tenere i corpi tondi a FOV ampio.
- 6DOF pieno con roll come modalità astronave, se mai servirà.

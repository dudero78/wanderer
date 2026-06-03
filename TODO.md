# Wanderer — TODO

Lista di lavoro che sopravvive tra le sessioni. Aggiornata al **3 giugno 2026** (sessione GPU-editor, Tappe 1-3).
Dettaglio tecnico nel `CLAUDE.md`.

> **PROSSIMA SESSIONE — PARTI DA QUI:** l'anteprima GPU dell'editor è COMPLETA (geometria+colore+normali,
> Tappe 1-3 fatte). Prossimi bivi: (a) portare la resa GPU **IN GIOCO** (B1, sostituire quadtree/SingleMeshPlanet);
> (b) **materiali per pendenza/quota/curvatura** + tiling triplanare + PBR (roadmap SC/ED, [[wanderer-rendering-roadmap]]);
> (c) tornare al GIOCO (teletrasporto, il VERBO). Tutto su `main`.

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
- ✅ **GPU per l'editor — TAPPA 1 (render-dai-buffer, NO readback):** la geometria dell'anteprima editor è
  calcolata sulla GPU (`PlanetHeight.compute`, kernel `CSFaceGrid`+`CSFaceNormals`) e disegnata direttamente dai
  `GraphicsBuffer` con `Graphics.RenderPrimitivesIndexed` (`GpuPlanetSurface.cs` + shader `Wanderer/PlanetProcedural`),
  niente mesh CPU di mezzo. Toggle **G** nell'editor (GPU↔CPU, confronto A/B). Anteprima **full-res LIVE** (512,
  default `gpuRes`): rigenera a ogni edit, niente bassa-res/attesa. Cuciture fra facce chiuse con lo **snap a
  lattice** del punto-cubo (come il quadtree). Normali geometriche segnaposto (la resa vera = PlanetBaked, tappa
  dopo). **Crateri a PARITÀ COMPLETA** con la CPU: portati in HLSL anche i pesi per taglia (Grandi/Medi/Piccoli)
  e la "Distribuzione" — quest'ultima ri-disegnata come **DRIFT del centro** (ogni cratere scivola nella sua
  cella, l'insieme si ridistribuisce, i crateri restano tondi; era una rotazione che "girava il pianeta").
  Test parità GPU↔CPU verde sub-mm (incluso il caso pesi+distribuzione).
- ✅ **GPU per l'editor — TAPPA 2 (mari + tettonica in HLSL, pipeline ORDINATA):** il path GPU non fa più
  `base + somma crateri` ma applica i processi **nell'ordine della ricetta** (un cratere dopo un mare scava
  all'asciutto), come `PlanetTerrain.SampleHeight`. `GpuShapeBuffers` (nuovo) = unica fonte: buffer ordinato
  `(tipo,indice)` + buffer per-tipo (crateri/mari/tettonica + placche). **Mare** (`SeaSurface`/`SeaShape`) e
  **Tettonica** (`TectonicApply`: soft-Voronoi, continenti/oceani, catene/rift, warp coste) portati in HLSL;
  le placche sono generate UNA volta in C# e caricate (niente RNG da replicare → parità per costruzione).
  Test parità esteso (Crateri+Mare ordine, Tettonica): verde sub-mm.
- ✅ **GPU per l'editor — TAPPA 3 (colore + normali analitiche):** l'anteprima GPU non è più grigia. Il COLORE è
  calcolato **nel fragment dalla ricetta** (`Wanderer/PlanetProcedural`), **niente texture bakate** (scelta
  architetturale: risoluzione infinita, niente bake all'avvio, GPU-first; il bake resta solo per simulazioni
  costose tipo erosione/AO — vedi [[wanderer-rendering-roadmap]]). Catena mirror di PlanetBaked: suolo+macro,
  minerali, vette, bacini, MARE (blu+saturazione). **Maria/vette seguono la quota di BASE** (ricostruita nel
  fragment), non i crateri (altrimenti ogni cratere faceva grandi blob). **Normali ANALITICHE** (gradiente di
  SampleHeight, eps≈1 cella). Cuciture agli spigoli del cubo chiuse facendo **sovrapporre le facce di una cella**
  (lo snap a lattice, provato prima, terrazzava i versanti dei crateri → crepe → rimosso).
- ✅ **Rifiniture editor (sessione 3 giu):**
  - **Modo luce, `L`** (`EditorLightMode`): ancorata (sole fisso, default) / libera (sole agganciato alla vista, da
    destra-alto, ~1/8 in ombra → orbiti = ruoti il pianeta sotto il sole, ispezioni ogni faccia illuminata).
  - **Mare LIQUIDO** (flag `liquid`, toggle nella sezione Mare): resa acqua (glint speculare + fresnel sul lato
    illuminato), larghezza del glint **legata alla rugosità** (liscio = punto da specchio). Solo visivo.
  - **Dettaglio anteprima GPU** (512/1024/2048 + **Auto** opt-in legato allo zoom con isteresi). Default 512 fisso.
    **Index buffer generato sulla GPU** (kernel `CSIndices`, dispatch 2D in `uint`, buffer `Index|Structured`,
    cache per livello) → niente alloc/upload da ~600 MB sul main thread. Lo scatto residuo del 2048 (alloc VRAM)
    si paga solo scegliendolo.
  - **Leggibilità del pannello (UX)**: colore-firma per zona (header colorato + icona + velo + zebra), pulsanti
    "Che tipo?" tipizzati, regione PROCESSI distinta (divisoria + titolo "stack" + sottotitolo). Tutto IMGUI.

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

## Prossimo

L'anteprima GPU dell'editor è COMPLETA (Tappe 1-3: geometria + colore + normali analitiche, a parità col walker).
Bivi possibili (da decidere con Dario):

- ⬜ **Resa GPU IN GIOCO (B1)**: usare il render-dai-buffer (`GpuPlanetSurface`/`PlanetHeight.compute`) anche per i
  corpi in gioco, sostituendo quadtree/SingleMeshPlanet → elimina pure il bake da 1.9 s all'avvio. Va aggiunto il
  LOD (l'editor è a faccia singola full-res; in gioco serve view-dependent). Fondazione pronta; il readback (B2)
  resta parcheggiato (TRASCINA), si usa il render-dai-buffer.
- ⬜ **Look SC/ED — materiali per pendenza/quota/curvatura** (roccia su bordi cratere, sedimento nel fondo,
  pinnacolo a parte, neve in quota) + tiling **triplanare** + **PBR**, sopra `PlanetProcedural`. Aggiungere anche
  la **grana del suolo** (texture tileabile) che ora manca sulla GPU (sfera liscia troppo "pulita"). Vedi
  [[wanderer-rendering-roadmap]].
- ⬜ **Residuo minore**: forse restano marcature di shading molto tenui qua e là sull'anteprima GPU (da indagare
  solo se danno fastidio).
- ⬜ **Acqua liquida — resto**: il toggle "liquido" + il look acqua (glint+fresnel) sono FATTI; restano la resa
  **trasparente/depth** (vedere il fondale sommerso) e il **nuoto/affondamento** del walker (gameplay).
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

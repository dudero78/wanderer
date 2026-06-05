# Wanderer — TODO

Lista di lavoro che sopravvive tra le sessioni. Aggiornata al **4 giugno 2026** (sessione B1 — resa GPU in gioco).
Dettaglio tecnico nel `CLAUDE.md`.

> **PARTI DA QUI:** **B1 GIRA** (resa GPU in gioco: quadtree CDLOD su GPU + 1 draw indirect + colore procedurale +
> LOD + walker analitico). Artefatti spariti, fermo/crociera 60 fps. **Load RISOLTO** (era la compilazione del
> compute: split + `[loop]`, vedi sotto). Restano due colli, entrambi sul **cambio-quota** (avvicinamento/radente):
> **(1)** churn del LOD = 64 fill/frame → **batch debuggato** (con banco di verifica) + budget nodi; **(2)** il
> **fragment del mare** GPU-bound (~21 ms) → per-vertice + overdraw. La strategia è confermata e raffinata in
> `RENDERING_STRATEGY.md` §13 (R1-R5). Cap fps a **60** (`PerformanceGovernor`).
>
> **MISURA-CAUTELA (R5):** il "CPU ms" e la traccia CPU rossa includono l'**attesa-GPU** quando sei GPU-bound. La
> verità GPU è **GPU Frametime** (Stats). Conferma lì prima di ottimizzare la CPU.
>
> **DECISIONE (4 giu, dopo confronto con Dario): obiettivo = ROCK-SOLID SMOOTH (alla Quake/Doom moderno) PRIMA della
> grafica.** Non sono "ottimizzazioni finite": fluidità è un obiettivo a sé, NON fatto. Ma prima di altri fix:
> **MISURARE LA VERITÀ SU UNA BUILD** — l'editor gonfia la CPU (lezione dura nel CLAUDE.md), i "14 ms" potrebbero
> essere in gran parte overhead-editor. Aggiunto contatore FPS+**picco/sec** nell'HUD (visibile in build) +
> `EnsureIncludedShaders` (auto: la build non esce magenta). PIANO: build → misura reale (fermo / avvicinamento /
> radente veloce) → fix del collo VERO (taratura o passo strutturale, deciso dal dato). Grafica e Fase 2-scala = DOPO
> la fluidità. ✅ Always Included Shaders ora gestito da `EnsureIncludedShaders` (era un TODO B1).
>
> **ESITO FLUIDITÀ (5 giu) — il meter `trav·fill·invio` ha inchiodato il collo:**
> - **Lo stutter era la TRAVERSATA CPU del quadtree** (`trav` 14ms, fill/invio≈0). DUE fix STRUTTURALI: (1) `UpdateLod`
>   non passa più matrice+vettori PER COPIA a ogni nodo (→ campi del frame) + costanti orizzonte calcolate una volta;
>   (2) **`ComputeBounds` non chiama più `SampleHeight`** (3×/nodo=12×/split, il picco) → per il LOD basta la SFERA.
>   **→ 60 FPS in gioco normale** (era 11-22). Walker intatto.
> - **Valentina2 (mare) è GPU-bound** (fragment del mare ~120-140ms a bassa quota, NON CPU). Leva messa: **RISOLUZIONE
>   DINAMICA** (`RenderScaler` adattivo). **`Cull Back` ROTTO** (skirt a doppia faccia → buchi; serve 2:1/depth-prepass).
> - **ARCHITETTURA:** estratto **`PlayerSpawn`** (spawn isolato) + **`spawnOnBody`** (default "Valentina2", test rapido).
>   GameBootstrap ora è regìa. **Da estrarre ancora:** LightingSetup (sole+ambient+eclissi) e UiSetup (mappa+rotta+orbite+HUD+impostazioni).
> - ✅ **MARE STRUTTURALE = pelo per-vertice (FATTO).** Il compute emette la quota del pelo `SeaSurface` per-vertice
>   (`_VSurf`, come `depth`/`baseN`); il fragment costruisce la maschera del mare da `abs(length(pos) − seaSurf)`
>   ESATTO, niente più ricostruzione del rumore. Quella ricostruzione (3-vs-4 ottave) sbagliava ad alta rugosità →
>   acqua "dipinta" a chiazze. Ora: pelo netto, trasparenza/fondale affidabili, glint dove serve — e un `fbm`
>   per-pixel in meno sul mare GPU-bound. Editor e gioco condividono il dato (niente divergenza). **NB resa acqua:** a
>   rugosità alta (es. terra-test3 ~17 m) il pelo È geometricamente ondulato di ±17 m → legge come colline blu; per un
>   mare calmo abbassare `seaRoughness` nell'editor. Increspatura animata (normal-map sul pelo piatto) = polish futuro.
> - **PROSSIMO (da fresco):** batch dei fill in 1 dispatch CON banco di verifica (R1). Poi look/Fase 2.

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

- ✅ **"Crepe" nella tettonica RISOLTE** (non erano cuciture né aliasing: discontinuità di `SampleHeight` al
  salto d'identità della 2ª placca → gate di continuità). Resta solo, minore, la banda all'**orizzonte** dai
  lembi di overlap delle 6 facce (bassa priorità).
- ⏸️ **Stitch di LOD** (transizioni di shading "scalini" ai confini): niente fessure/buchi, ma restano i salti
  di shading (peggio coi salti di 2+ livelli). Fix definitivo = **quadtree bilanciato 2:1** (vicini ≤ 1 livello
  → il morph di un livello basta, si possono togliere gli skirt). Rimandato: troppo tempo, avanti col gioco.
- ⏸️ **Salti/scarpate netti scalettati nell'ANTEPRIMA editor** (3 giu, decisione: NON priorità ora). I gradini sul
  bordo di un salto netto sono **aliasing dell'heightfield** (una linea netta su griglia a res fissa = scala, come
  una diagonale su pixel). NON risolvibile con shading (provata "roccia sulle scarpate" → gole nere, scartata).
  Cura vera = **LOD**: il GIOCO ce l'ha (quadtree, fine vicino alla camera → gradini sub-pixel); l'anteprima editor
  usa mesh a res fissa. Fix = far usare all'editor il quadtree (rebuild + switch drag/zoom). RIMANDATO: il GPU per
  l'editor (sotto) lo risolve gratis (res altissima a costo nullo). Verificare se il gioco già le rende pulite.

## B1 — resa GPU in gioco (IN CORSO)

Obiettivo: la superficie dei corpi rocciosi calcolata e disegnata **sulla GPU**, come nell'editor, ma con **LOD**
view-dependent e **1 draw indirect** (niente Mesh Unity, niente upload sul main thread, niente readback, niente
draw call per-nodo). Il walker resta analitico su CPU (`SampleHeight` in 1 punto) → collisione intatta. La parità
GPU↔CPU fa da rete. Componenti nuovi: `GpuPlanetRenderer.cs`, shader `Wanderer/PlanetSurfaceGPU`, include condiviso
`PlanetProceduralShade.cginc` (colore = una sola copia, editor+gioco). Toggle `useGpuSurface` su `GameBootstrap`.

- ✅ **Tappa 1 — pool GPU + 1 draw indirect (FATTA, in gioco, 60 fps).** 6 facce a risoluzione FISSA in un solo
  `RenderPrimitivesIndexedIndirect` istanziato (istanza = fetta del pool, `SV_InstanceID`→fetta), colore procedurale
  nel fragment, piazzamento con matrice oggetto→mondo (floating origin). Niente LOD ancora: tris alto e COSTANTE,
  crateri morbidi da vicino — atteso. Struttura indirect già definitiva (Tappa 2 non riscrive il draw).
  - ✅ **Geometria CONFERMATA** (test diagnostico `debugView` = colore radiale: sfera pulita, ben piazzata).
  - ✅ **CAUSA VERA del "pianeta nero": Properties VUOTE nello shader in gioco** → gli uniform che `ApplyColor`
    non imposta valgono 0; in particolare `_SoilTint=(0,0,0)` azzera l'albedo (`alb = _SoilMean × … × _SoilTint`)
    → nero a prescindere dalla luce (sole/torcia ininfluenti, perché `col = 0 × luce`). I debug si vedevano perché
    BYPASSANO `PlanetShade`. Fix: `PlanetSurfaceGPU` ha gli STESSI default Properties di `PlanetProcedural`.
    LEZIONE: uno shader disegnato dai buffer non eredita né luci né default — ogni uniform letto dal fragment
    deve avere un valore (default Properties o set da codice). (Le mie diagnosi "luce sbagliata" e "terminatore"
    erano errate: la geometria/normali erano OK e la luce era agganciata, ma l'albedo era zero.)
  - ✅ **LUCE A MANO** (lo shader GPU non riceve le luci di Unity): **SOLE** via `SunLight.Instance` (statico in
    Awake) + **TORCIA** spot (pos/dir/cono/range per-frame; `_TorchColor=0` da spenta → costo nullo).
  - ⬜ **ECLISSI** ancora da portare nel path GPU: altra luce/ombra che `PlanetBaked` aveva e questo no.
  - ⬜ verificare la resa LIT vera (sole di giorno + torcia di notte).
  - ✅ **Mappa**: la superficie GPU entrava in TUTTE le camere (anche la mappa → la superficie reale del corpo
    sopra i proxy → taglie incoerenti). Risolto con `GpuPlanetRenderer.SuppressDraw` (statico) che MapMode accende
    in mappa: la camera del giocatore è spenta lì, quindi non c'è nulla da disegnare comunque.
  - ⬜ GPU Frametime alto già ora (10–35 ms @2× con fragment di DEBUG cheap) → conferma il fronte fragment/overdraw
    (Tappa 4); il fragment vero sarà più caro.
- 🟡 **Tappa 2 — LOD (quadtree GPU) (SCRITTA, da testare).** Quadtree di nodi LEGGERI (niente GameObject) in
  `GpuPlanetRenderer`: split/merge per distanza + horizon culling; ogni foglia = una FETTA del pool riempita dai
  kernel nuovi `CSNodeSlab`+`CSNodeSkirt`; lista foglie visibili → 1 draw indirect. Niente thread/readback/coda (sulla
  GPU il "build" è un dispatch). Skirt nel compute (nasconde le crepe fra LOD). Budget split/frame (no spike).
  **Fix multi-corpo:** ogni renderer `Instantiate` il proprio ComputeShader (lo shared si clobbererebbe i binding
  fra i 4 corpi). Atteso: crateri NITIDI sotto i piedi, rado/cullato lontano → calore GIÙ. Debug `debugMode` 1/2 se rotto.
  - ✅ **CACHE LRU delle fette** (fix del "delirio": redraw/spariscono/stutter). Una regione che esce di vista NON
    si ricalcola: la fetta (geometria statica) resta in cache e si riusa al ritorno. Pool 512→1024, budget split
    24→64, isteresi 1.4→2.0. Ogni corpo `Instantiate` il proprio ComputeShader (no clobber multi-corpo).
  - ⬜ **Tappa 2b — GEOMORPH** (transizioni LOD lisce, niente "pop"): morph delta per-vertice dal compute + lerp nel
    vertex shader con la distanza camera. Resta il "pop" allo split/merge (skirt evita i buchi, non il pop).
  - ⬜ warning compute `CSNodeSlab/Skirt` ("uint if possible") = perf, non bug.
  - 🟡 **MISURATO (4 giu mattina):** è **GPU-bound dal FRAGMENT**. Test con `debugMode=1` (fragment banale):
    GPU fermo **9.3→2.3 ms**, volo **20.4→5.6 ms** → ~7–15 ms erano il rumore per-pixel (`n3_fbm` 5 ott. per `baseN`
    + mare). + picco CPU intermittente. Applicato: **baseN 5→2 ott.** e **mare 4→3 ott.** nel fragment.
  - ✅ **TAGLI FILL SICURI (4 giu, geometria invariata):** **normali a 2 campioni** (differenza-in-avanti, riusa il
    centro → fill GPU ~dimezzato), **property-ID cachati** (niente hash-stringa per chiamata CPU), `_NN`/`_NSkirtStart`
    una volta sola. **`lodFactor` 4→3** (R3): `visibili` ~1023→~700 (era al tetto del pool 1024) → meno disegno + meno fill.
  - 🟡 **BATCH dei fill (1 dispatch) — RIFATTO, PARITÀ CONFERMATA (5 giu): `[batch-fill] PARITÀ OK max diff 0.00000 m`
    su tutti i corpi** → il batch è bit-esatto col per-nodo, attivo. Resta da misurare in gioco se taglia il churn CPU
    (R5: confermare con GPU Frametime che il lag non era attesa-GPU). Era stato annullato
    (corruzione di geometria, bug d'indicizzazione non trovato). Ora i kernel batch (`CSNodeSlabBatch/SkirtBatch`)
    sono identici ai per-nodo ma con assi/uv/slabOff/skirtDrop letti da `_Jobs[nodo]` (nodo = asse z/y del dispatch);
    tutto float4. È **OPT-IN** (`GameBootstrap.useBatchFill`, default OFF) e si attiva SOLO se `VerifyBatchFill()`
    trova **parità sub-cm** batch↔per-nodo sui 6 root (readback all'avvio, log `[batch-fill]`); altrimenti fallback
    automatico al per-nodo. **Da fare:** accendere il toggle, leggere il log (PARITÀ OK?), e SE verde misurare se
    taglia davvero il churn CPU (R5: prima conferma con GPU Frametime che non è attesa-GPU). NB: i 2 kernel in più
    allungano la compilazione del compute all'avvio (costo di sviluppo, R2).
  - ⬜ se il churn resta: batch debuggato + per-vertice i campi a bassa freq (`baseN` interpolato → fragment quasi gratis).
  - 🟡 **SINTOMI segnalati da Dario (4 giu, bersaglio del prossimo lavoro LOD) — "nessun caricamento lungo IN GIOCO"
    (§13 R2) è IL requisito:** (a) ambiente che **carica troppo tardi** (fill a budget + split solo dentro splitDist
    → ritardo) → cura: fill economici/batch + **LOD predittivo** (split un filo prima); (b) **scarica troppo presto**
    → isteresi di merge più larga; (c) **scarica e ricarica un pezzo DAVANTI** (il più importante) = **thrashing**
    sulla soglia o **cache LRU che sfratta una fetta ancora in vista** → isteresi + non sfrattare regioni visibili +
    budget nodi. Questi tre sono l'acceptance-test del batch/tuning LOD.
  - ✅ **LOD PREDITTIVO** (4 giu): split valutato dalla posizione "dove sarai fra ~0.7s" (`lookaheadTime`), velocità
    relativa al centro pianeta (stabile con floating origin) → il dettaglio davanti si carica PRIMA. Fermo = identico.
  - 🟡 **POP all'ORIZZONTE a quota bassa (diagnosticato dai frame di Dario):** un pezzo nero (=niente geometria) che
    compare/sparisce alle STESSE quote = **horizon culling**: a quota bassa `acos(R/camDist)` è ipersensibile, e il
    test culla in base al CENTRO ignorando che le **creste delle dune bucano l'orizzonte** (visibili). **Cerotto
    applicato:** **isteresi per-nodo** (margine ampio per nascondere ≈8°, stretto per ri-mostrare ≈2° → banda morta,
    niente flip) — `Node.horizonHidden`. **Fix VERO (dopo la perf):** orizzonte **height-aware** (quanto sporgono le
    creste) + **geomorph** (sfuma le transizioni invece di farle scattare). NON bloccante (parola di Dario).
- 🟡 **LOAD = compilazione della pipeline Metal del compute** (la `SampleHeight` enorme), NON bake/alloc. **PRIORITÀ
  DI SOLO SVILUPPO** — decisione di Dario (§13 R2): il load iniziale del gioco NON è un problema per il giocatore;
  l'unico requisito è **nessun caricamento lungo MENTRE giochi**. In build gli shader sono precompilati. Quindi pesa
  solo sulla nostra iterazione → non spenderci troppo. **Cosa ha aiutato:** **SPLIT del compute** (22→15 s) in
  `PlanetHeightCore.hlsl` (core condiviso = UNA `SampleHeight`, parità intatta) + `PlanetHeight.compute` (gioco: 2
  kernel) + `PlanetHeightEditor.compute` (editor/baker: 5; loader aggiornati). **Cosa ha FALLITO:** `[loop]` sul
  ciclo crateri 5×5×5 → PEGGIORATO (15→50 s + rotella 25 s allo stop): l'unroll a limiti letterali compila più
  veloce. **`[loop]` ANNULLATO.** Lezione: misura il compile, non assumere "meno codice = compila prima".
- ⬜ **Tappa 3 — spegnere il bake in gioco.** Colore tutto procedurale → via la dipendenza da `PlanetBaked` per la
  superficie. NB: i **proxy della mappa** usano ancora i materiali bakeati → "togliere gli 1.9 s" è parziale, da
  ragionare (serve un materiale per i proxy comunque).
- ⬜ **Tappa 4 (fronte GPU, pari rango) — 60 fps a SCHERMO INTERO.** È il vero collo per "fluido a meraviglia":
  fragment più snello + taglio **overdraw** (disegno fronte→retro, meno/niente skirt col 2:1) + `RenderScaler` < 1.
  ⚠️ rischio: il colore procedurale nel fragment può essere **più caro** del texture-lookup → tenerlo leggero per
  non peggiorare proprio questo caso. Misurare a schermo intero (il caso 29.9 ms).

**Da non dimenticare (B1):**
- ⬜ **Always Included Shaders**: aggiungere `Wanderer/PlanetSurfaceGPU` (è creato via `Shader.Find` → in build
  sarebbe strippato = pianeta magenta/invisibile). In Play dall'editor funziona già. Vale anche per ogni nuovo
  shader del percorso.
- ⬜ **Eclissi nel path GPU**: `PlanetSurfaceGPU` non ha l'ombra di eclissi (ce l'ha solo `PlanetBaked`/proxy mappa).
  Portarla nell'include/shader quando la superficie GPU è il renderer in gioco.
- ⬜ **Cap fps**: alzato a 60 (`PerformanceGovernor`). Quando la CPU è scarica (post-B1) valutare di toglierlo del
  tutto: il cap era la pezza per il costo CPU che B1 rimuove (performance = architettura, non patch).
- ⬜ **Dedup shader**: oggi `PlanetProcedural` (editor) e `PlanetSurfaceGPU` (gioco) condividono SOLO l'include del
  colore. Quando il path in gioco è provato, l'editor potrà passare a `PlanetSurfaceGPU` (un solo shader).
- ⏸️ **B2 — readback** (GPU→CPU→Mesh) resta PARCHEGGIATO: TRASCINA (i nodi compaiono in ritardo). B1 lo bypassa.

## Prossimo

L'anteprima GPU dell'editor è COMPLETA (Tappe 1-3: geometria + colore + normali analitiche, a parità col walker).
Bivi possibili (da decidere con Dario):

- ⬜ **Look SC/ED — materiali per pendenza/quota/curvatura** (roccia su bordi cratere, sedimento nel fondo,
  pinnacolo a parte, neve in quota) + tiling **triplanare** + **PBR**, sopra `PlanetProcedural`. Aggiungere anche
  la **grana del suolo** (texture tileabile) che ora manca sulla GPU (sfera liscia troppo "pulita"). Vedi
  [[wanderer-rendering-roadmap]].
- ⬜ **Residuo minore**: forse restano marcature di shading molto tenui qua e là sull'anteprima GPU (da indagare
  solo se danno fastidio).
- ✅ **Acqua come SUPERFICIE in gioco**: pelo per-vertice (`_VSurf` → maschera esatta), increspatura animata
  (`WaterRippleNormal`, gradiente analitico da `noised`), colore per profondità, riflesso sole+Fresnel-cielo,
  trasparenza solo in acqua bassa, battigia. Mare SOLIDO (maria/ghiaccio, non `_SeaLiquid`) = tinta piatta, NON
  acqua. Riva stretta (banda 0.15..0.75 m) → l'acqua non si arrampica sui corpi che affiorano.
- ⬜ **GUSCIO D'ACQUA SEPARATO (fix definitivo del pelo PIATTO, da fare al gameplay-acqua).** Oggi l'acqua è il
  terreno ALLAGATO (una mesh: `h=max(terreno, livello)`) → alle coste la griglia fa una RAMPA (l'acqua si
  "arrampica"; stretta la maschera è un cerotto, non la cura). La cura vera = DUE superfici: (1) terreno col
  rilievo pieno (niente allagamento) + (2) un **guscio** = sfera sottile trasparente al livello del mare. Il
  terreno buca il guscio di NETTO (zero rampe), trasparenza/rifrazione/onde vere, riflessi puliti. Costo: 2°
  draw + **blending trasparenza** (ordinamento), il walker deve sapere di 2 superfici (nuoto vs cammino), un po'
  più di complessità nel renderer GPU. È il modo "giusto"; abbinarlo al **nuoto/affondamento** del walker.
- ⬜ **Acqua — minori**: increspatura/colori esposti come manopole della ricetta (ora costanti nello shader);
  trasparenza anche sul path CPU `PlanetBaked` (non ha la profondità per-vertice) se mai servirà lì.
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

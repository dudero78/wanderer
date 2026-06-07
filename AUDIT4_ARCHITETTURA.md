# AUDIT #4 — Architettura, Filosofia di gioco, Performance

*7 giugno 2026, notte. Tavolo tecnico (audit autonomo). Letti: codice (Assets/Scripts/**, Assets/Shaders/**),
tutti i `.md` di progetto, e gli audit precedenti `AUDIT.md`/`AUDIT2.md`/`AUDIT3.md`. Ogni affermazione è
ancorata a codice realmente letto. Compagno: `AUDIT4_CODICE.md` (igiene + bug).*

> **TL;DR** — È un motore hobbistico **insolitamente ben disciplinato**. Le fondamenta (doppia precisione +
> determinismo on-rails, single-source-of-truth con i parity gate, lo split #18 del renderer) sono di livello
> "principal engineer". I rischi sono concentrati nei pezzi **non ancora costruiti** (corpi volumetrici, il VERBO
> di gioco) e in **un file** che scivola verso god-object (`MapMode`), non nelle fondamenta. **Sulla performance,
> su un M3 Pro Max il progetto è lontanissimo dal suo budget** — le leve (RenderScaler, cap fps) sono headroom
> deliberatamente non sfruttato. "Se lo mangia a colazione": confermato.

---

## 1. Inventario dei sistemi

**Strato di verità (Core/Physics)**
- `Vector3d` + `FloatingOrigin` — coordinate-universo a doppia precisione; `SceneOrigin` è l'unico globale che il
  renderer deve conoscere; azzerato a ogni Play.
- `SolarSystem` (exec order −100) — orchestratore globale: avanza `SimTime` da un **conteggio di tick intero**
  (deterministico), aggiorna Kepler le posizioni/velocità, ri-ancora la floating origin al corpo più vicino /
  destinazione / punto-nave nel vuoto, pilota sveglia/sonno interstellare. Possiede `TargetInfo`/`DormantTarget`.
- `StarSystem` — contenitore-dato puro (stella + corpi + `SystemOrigin` + `Recipe`), stato sveglia/sonno. Non MonoBehaviour.
- `KeplerOrbit` — posizione analitica in forma chiusa + derivata di velocità analitica, solve Newton-Raphson. Unity-free.
- `CelestialBody` — posizione/velocità-universo, `Mu`, gravità di superficie, flag `Massless` (baricentro virtuale).

**Renderer del terreno GPU (il cuore, spaccato col #18)**
- `GpuPlanetRenderer` (584 righe) — orchestratore + `ISlafFiller`: compute per-corpo + materiale, pilota il tree a
  ogni frame, riempie le fette (immediato o batch), **1 `RenderPrimitivesIndexedIndirect`**, illuminazione manuale,
  gate di parità runtime, uniform eclissi.
- `PlanetLodTree` — quadtree CDLOD su cube-sphere: split/merge per distanza predittiva, horizon culling height-aware,
  raccolta foglie visibili, region-stamp. Pooling node/child zero-alloc.
- `SlabPool` — pool VRAM **condiviso refcountato** (un solo pool per tutti i corpi), free-list + cache LRU O(1), BodyId riciclabile.
- `GpuHeightBaker` / `GpuShapeBuffers` / `PlanetHeight.compute` — altezza GPU = parità col walker; pipeline ordinata.
- Shader: `PlanetSurfaceGPU` (vertex: geomorph + anti-spuntone + region-stamp; fragment delega al core condiviso),
  `PlanetProceduralShade.cginc` (UNA copia di colore+luce+acqua+eclissi).
- Fallback CONGELATI: `PlanetQuadtree` (CDLOD CPU), `SingleMeshPlanet` (res fissa + proxy mappa), `PlanetBaker`.

**Autorazione terreno**: `PlanetRecipe` + `TerrainLayer` ordinati; `PlanetTerrain.SampleHeight` = unica verità CPU
per il walker. `PlanetEditor` (830 righe) + `GpuPlanetSurface` (anteprima GPU live). `PlanetParityGate` (parità
C#↔HLSL a ogni ricompila).

**Giocatore/navigazione**: `PlanetWalker` (638 righe, input in Update / fisica in FixedUpdate), `MapMode` (1186
righe), `RouteIndicator`, `OrbitDisplay`, `Probe`/`ProbeController`, `Flashlight`, `SpeedLines`, `OpticalInstrument`.

**Cielo (stasera, World/Sky/)**: `SkyController`, `SkyData`, `StarFieldRenderer`, `MilkyWayBand`, `DeepSkyRenderer`,
`ConstellationLines` + i 5 shader. + `DistantStars`/`StarGlow`/`StarRenderClamp`/`SunLight`.

**Luce/bootstrap/perf/UI**: `EclipseDriver` (~10 Hz, O(n²)), `GameBootstrap` → `SolarSystemSetup`/`PlayerSpawn`/
`LightingSetup`/`UiSetup` (+ coroutine boot + `LoadingScreen`), `PerformanceGovernor` (60 attivo/30 idle),
`RenderScaler` (risoluzione dinamica, due camere, minScale 0.4), `GameSettings`, `SettingsMenu`/`PauseMenu`/`DebugHud`.

---

## 2. Qualità dell'architettura

**Forte sul serio (non a parole):**
- **Single-source-of-truth imposto MECCANICAMENTE, non per convenzione.** La duplicazione altezza C#↔HLSL (il
  vero #17) è protetta dal `PlanetParityGate` a ogni ricompila E da un gate runtime che cerca esplicitamente
  NaN/Inf (un `d>maxDiff` naive mascherebbe i NaN). Colore/luce hanno UNA copia (`PlanetProceduralShade.cginc`)
  condivisa fra editor e gioco. È la disciplina giusta per una pipeline transpilata a mano.
- **Lo split #18 del god-object è reale e pulito.** `ISlabFiller` è un giunto vero: il tree sa la topologia/LOD, il
  pool la memoria, il renderer compute+draw+luce. La parte meglio fattorizzata del codice.
- **Bootstrap disciplinato.** `GameBootstrap.Boot` è una sequenza leggibile; ogni sottosistema isolato nel suo file;
  i flag statici spinti PRIMA del Build; sveglia/sonno multi-sistema come callback `WakeSystem`/`SleepSystem`
  (la policy "quando" in `SolarSystem`, il meccanismo "cosa" nel bootstrap). Buona separazione.
- **Singleton ri-puntabili per il multi-sistema.** `SunLight.Retarget`, `EclipseDriver.Rebuild`, `Reference` come
  proprietà; lo stato che deve sopravvivere a uno swap di sistema (l'ancora del giocatore) sta in `SolarSystem`,
  non nel contenitore per-sistema. Lungimirante.
- **Determinismo preso sul serio.** `SimTime = tick * fixedDeltaTime * TimeScale` (intero, no deriva float); Kepler
  in forma chiusa. È il substrato giusto per l'ambizione "online un giorno".
- **Fallback espliciti, non silenziosi.** `SlabPool.Mismatched` → `Ready=false` → quadtree; starvation loggata una
  volta; guardie domain-reload. Niente degrado muto.

**Fragile / da tenere d'occhio:**
- **`MapMode` (1186 righe) è il prossimo god-object.** Possiede build+sync visuali di corpi E sistemi, camera
  trackball, input, scia, layout etichette, OnGUI. È il file più grosso del 40% e l'unico dove la regola "una
  responsabilità per file" non è applicata. → **Spaccarlo prima che si ossifichi** (vedi raccomandazioni).
- **Tre reti di sicurezza indipendenti nel vertex shader più caldo** (geomorph clamp + anti-spuntone direzione/
  lunghezza + region-stamp): ognuna giustificata da un bug passato, ma tre "se è spazzatura, collassa il vertice"
  sovrapposte indicano che l'invariante di churn (la fetta corrisponde sempre all'istanza che la referenzia) è
  DIFESO a posteriori, non garantito strutturalmente. Carico portante che un futuro cambio all'ordine fill/upload
  può rompere in silenzio.
- **Illuminazione manuale duplicata.** Lo shader fed-by-buffer non riceve le luci Unity → sole/torcia/aux/eclissi
  spinti a mano ogni frame. `AuxPointLight` è UN solo slot statico ("uno per ora"). Una seconda sonda/luce non ha casa.

---

## 3. Allineamento con la filosofia (universo seamless)

L'architettura serve l'obiettivo **meglio della maggioranza dei tentativi hobbistici, e i problemi difficili sono
risolti a livello di substrato:**
- **Superficie→orbita→interplanetario** seamless e provato: ri-ancoraggio floating-origin con preservazione della
  velocità, isteresi di decollo, CDLOD predittivo. Niente muri di caricamento fra camminata e volo.
- **Interplanetario→interstellare** ha una risposta vera: `deepMode` ri-centra l'origine sul giocatore oltre 50 km
  → coord-scena sempre piccole su milioni di metri. Il jitter è gestito strutturalmente.
- **Il nuovo cielo a catalogo è correttamente "all'infinito"**: frame equatoriale fisso, bolla camera-following,
  parallasse nullo, e `EquatorialToGame` lega lo zodiaco al piano orbitale → Antares/Vega nelle loro direzioni
  REALI. Coerenza bellissima: volare verso Vega = volare dov'è Vega in cielo.

**Soffitti/muri che morderanno scalando:**
1. **Giganti gassosi e stelle come VOLUMI non esistono ancora** (TODO §"secondo renderer volumetrico"). Oggi ogni
   corpo è un `PlanetTerrain` roccioso. `EclipseDriver`, il sole, i proxy mappa, `NearestBody` assumono tutti corpi
   mesh. Quando arriverà il volumetrico dovrà integrarsi con la STESSA macchina floating-origin/ancora/eclissi/mappa.
   **È il pezzo non costruito più grande e portante per l'ambizione NMS.**
2. **Il VERBO / loop di gioco è esplicitamente assente** (teletrasporto e mini-loop non fatti; "il verbo è in fondo
   alla lista"). Tutto il motore è un substrato di traversata senza nulla da FARE. Scelta deliberata di priorità —
   non un difetto — ma significa che nessun gameplay sta ancora forzando i sistemi a comporsi sotto pressione
   (inventario, interazione, stati di fallimento). Un'architettura non testata da un verbo tende a crescere
   l'astrazione sbagliata.
3. **La mappa è O(tutti i sistemi × corpi) per frame** per il sync proxy. Con la `Galaxy` statica a 3 sistemi è
   banale; ma `MapMode` ricostruisce/sincronizza imperativamente e servirà un passo di LOD/culling quando la
   galassia cresce.
4. **Un quadtree per corpo guidato da UN solo punto di vista** (il giocatore; le sonde solo evitano il culling).
   Split-screen / più osservatori vicini / client online che vedono un corpo da vicino non sono esprimibili.
   Limite noto, documentato. OK in single-player; muro per "online un giorno".
5. **`SlabPool` è statico globale di processo.** Corretto per una scena/un giocatore. Due osservatori simultanei o
   un server headless servirebbero pool per-contesto. Riciclo refcount/BodyId è buona pulizia, ma la staticità è un
   angolo per il path online.

Niente è dipinto in un angolo IRREVERSIBILE — la verità a doppia precisione e il determinismo on-rails sono
esattamente le fondamenta giuste. I muri sono tutti "non costruiti", non "mal costruiti": il tipo buono.

---

## 4. Performance (M3 Pro Max) — verdetto: largo margine

**Per centro di costo:**
- **Fragment del pianeta (storicamente dominante, ora ben ottimizzato).** Il rumore costoso per-pixel (3 fbm colore
  + ondulazione base) è **precalcolato per-vertice in gioco** e interpolato → il fragment NON gira gradient-noise
  per pixel. `_HAS_SEA` strippa il blocco acqua sui corpi asciutti. Resta: una normalize, AA della normale via
  fwidth, qualche lerp/smoothstep, diffuso + GGX opzionale + torcia/aux/eclissi. Shader opaco **economico**.
  Overdraw dimezzato da `_Cull=Front`. **L'unico caso davvero pesante = acqua a bassa quota all'orizzonte**, già
  LOD-ato per distanza e assorbito dal `RenderScaler`.
- **Il nuovo cielo — l'overdraw additivo è la cosa da osservare, ma va bene.** ~119k quad-stella = ~476k vertici, 1
  draw. **Cruciale: le stelle sotto-soglia collassano a quad di area NULLA nel vertex** (`keep = step(...)`, `px *
  keep`) → **zero fragment**, niente overdraw. A occhio nudo solo poche migliaia di quad producono pixel, ognuno
  minuscolo. Il conteggio 119k è un non-problema. Via Lattea = 1 pass fullscreen economico. DSO/aloni/linee =
  trascurabili. Rischio reale = overdraw additivo impilato SOLO zoomando col telescopio → comunque sub-ms su M3 Pro Max.
- **CPU / main thread (la vera fonte di calore documentata).** Traversata LOD ~0.1 ms, zero-alloc, upload solo su
  `SelectionChanged`, corpi sub-pixel early-out. `EclipseDriver` O(n²) ma a 10 Hz su ~6 corpi = rumore. Già snello e strumentato.

**Le leve sono costruite bene:** `PerformanceGovernor` (60/30, la leva diretta sul calore visto che la GPU non è mai
stata il collo) e `RenderScaler` (banda 0.4–1.0, a 1.0 di default = **headroom non sfruttato**). **Il build NON è
vicino al suo budget su un M3 Pro Max.**

---

## 5. Rischi principali & raccomandazioni (in priorità)

1. **Il renderer volumetrico è il pezzo critico non costruito — progetta ORA i giunti d'integrazione, non il renderer.**
   Introdurre un'astrazione minima `ICelestialRenderer`/body-kind così che il path volumetrico si infili nella stessa
   macchina ancora/eclissi/mappa invece di un retrofit. **Costruisci il giunto, non (ancora) il renderer.**
2. **Spacca `MapMode` (1186 righe) prima che si ossifichi** — `MapVisuals` (build/sync proxy) + `MapCameraRig`
   (trackball) + `MapLabels` (OnGUI), come lo split `SlabPool`/`PlanetLodTree`/`GpuPlanetRenderer` che ha già funzionato.
3. **Rendi STRUTTURALE l'invariante di churn delle fette** (oggi difeso da 3 reti nel vertex più caldo): documenta
   l'invariante come un contratto unico (fill scrive il region-stamp atomicamente PRIMA che l'istanza che lo
   referenzia sia caricata) + un assert/parità che lo valida diretto; tieni le reti shader come cinture+bretelle.
4. **Astrai l'illuminazione manuale in una piccola light-list** (array per-frame pos/colore/range/tipo, cap ~4) — toglie
   il plumbing per-tipo e dà casa a una seconda sonda/luce.
5. **Cabla anche un VERBO banale** per mettere sotto pressione il substrato (un raccoglibile che cambia stato e può
   fallire) — assicurazione a basso costo che i sistemi si compongono sotto uso reale, prima di costruire altra
   superficie di motore. **Vedi anche il "gate operativo" in §6.**
6. **Registra le assunzioni single-context** (`SlabPool` statico, quadtree single-viewpoint, `SceneOrigin` globale) come
   "costo noto" per il futuro netcode — nessuna azione ora, ma che non si scoprano per sorpresa.

---

## 6. Verifica degli audit precedenti (AUDIT/2/3) — cosa ri-porto al tavolo

Confronto con `AUDIT.md`/`AUDIT2.md`/`AUDIT3.md` (i corpi degli audit precedenti sono in parte **datati**: molti
item "aperti" sono stati poi implementati e committati — vedi sotto).

**Tema ricorrente da TUTTI gli audit, ancora valido e NON scritto nel TODO come gate operativo:**
- **Il VERBO è il rischio TERMINALE del progetto hobbistico** ("un bel motore che nessuno gioca"). `AUDIT.md`
  raccomandava un **gate esplicito**: "look SC/ED + polish acqua + scala-galassia si sbloccano SOLO dopo il primo
  loop giocabile su 2 corpi". → **Raccomandazione: scrivere questo gate in TODO/CLAUDE.** Lo scope sta salendo
  (galassia/esotici/guscio d'acqua/volumi) mentre il verbo è zero.

**Item architetturali ancora aperti (deduplicati):**
- **#17 fonte unica dell'altezza (transpiler/DSL C#→HLSL)** — la duplicazione è il debito #1; i gate sono una RETE,
  non una cura. Alto valore, alto rischio: decisione d'indirizzo, non refactor cieco.
- **ARCH-7 split `PlanetEditor` (830 righe)** — editor-only, da fare con Dario.
- **Scalabilità multi-sistema profonda** — `Bodies` flat O(N), pool eager (non locality-driven), doppia
  rappresentazione dell'identità (`Key` bit-layout vs `RegionId` formula decimale = doppia manutenzione). Additivo,
  ma è "il vero collo per più sistemi", non il LOD.
- **`SolarSystemSetup` lista hardcoded → descrittori pienamente data-driven** (`SystemRecipe` c'è; i corpi no).
- **Loading BLOCCO 2** (scene+prefab+`LoadSceneAsync`+warm-up shader) — il grosso (16s→2s) è già preso col bake
  offline + spread; residuo = compile pipeline compute Metal al 1° frame. Ora più "struttura" che "togliere il freeze".

**Incoerenze/datati negli audit precedenti (da sapere leggendoli):** il corpo di AUDIT3 (tabella "Salute per area"
B+/B/B−…) è uno **snapshot PRIMA** che la sessione autonoma landasse i fix: per-vertex color, `_HAS_SEA`, eclissi
sul renderer autoritativo, gate NaN, SuppressDraw reset, e il **PD1 starfield** (oggi FATTO) risultano "aperti" lì
ma sono stati implementati. Anche i "3 bug editor — RISOLTI" in un header di TODO è **stale**: realtà = #1 aperto
(ordine ricetta), #2 parziale (modello shader), #3 fix-candidato non verificato. **`AUDIT4_CODICE.md` §6** elenca i
bug aperti aggiornati.

---

*Fine AUDIT #4 — Architettura. Bottom line: fondamenta principal-engineer-grade; i rischi sono nei pezzi non
costruiti (volumi, verbo) e in un file che deriva (`MapMode`); performance non è una preoccupazione sul target.*

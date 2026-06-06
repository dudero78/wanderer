# Wanderer — TODO

Lista di lavoro che sopravvive tra le sessioni. Aggiornata al **6 giugno 2026** (sessioni UX: sonda, modelli
intercambiabili, menu ESC, multi-sistema/interstellare, mappa, effetto velocità, loading). Dettaglio tecnico nel `CLAUDE.md`.

> ## 🔴 PROSSIMA SESSIONE → vedi **`NEXT_SESSION_PROMPT.md`** (piano dettagliato, concordato con Dario)
> **Direttiva di fase:** basi ULTRA solide → si **riscrive liberamente** per la perfezione, niente pezze (memoria
> `riscrivere-per-perfezione`). Due blocchi, in ordine:
> 1. **Mappa multi-sistema** con SPAZIO-MAPPA LOCALE (precisione a qualunque distanza) + proxy STATICI di tutti i sistemi
>    dai `SystemRecipe` (Keplero) + centra/scala sul sistema in vista + **camera a ORBITA LIBERA** (no snap).
> 2. **Architettura a SCENE + PREFAB + loading ASYNC** (risolve il loading da ~1 min e dà la "scena con asset"):
>    Loading scene leggera → `LoadSceneAsync` in background + warm-up shader. Il compile COMPUTE su Metal resta l'unico
>    pezzo sincrono irriducibile.

> ## 🟢 PARTI DA QUI (6 giu+)
> **Motore terreno = CDLOD PURO, crack-free.** Le "crepe/tagli" di Valentina2, inseguite per settimane, sono RISOLTE
> alla FONTE — e NON erano il LOD: erano la ricetta. Catena di toppe sbagliate (skirt → morph-stitch → two-mask) tutte
> **rimosse**: il crack-free viene dal **morph continuo + `mergeHysteresis=1`** (confini netti). `trav` ~0.1ms.
> Cause vere chiuse in HLSL+C# identici (parità):
> 1. **CRATERI** — duplicati radiali del peso-a-guscio fuori dalla finestra ±2 celle (ottave fini) → pop ~0.5m. Fix:
>    `shellHalf = min(0.6, 1.7/gscale)`. Crateri piccoli MANTENUTI.
> 2. **TETTONICA** — 3 soglie-skip (`contW>0.01`, `mtn>0.001`, `boundary·gate>0.001`) accendevano il termine non-nullo
>    → gradino. Fix: sfumatura `Smooth01` (C1).
>
> **PRINCIPIO (guardia universale):** ogni processo di `SampleHeight` dev'essere **C0-continuo** (mai un salto di valore).
> **Skirt geometria RIMOSSA** (vertsPerSlab=n², niente kernel skirt, un draw). Il **#14 "quadtree 2:1" è OBSOLETO**.
>
> **STRUMENTI DI DIAGNOSI (costo zero in gioco):** `DebugView` 0–5 (off/radiale/normale/livello-LOD/faccia/fetta) dietro
> keyword `multi_compile PLANET_DEBUG_VIEW`, da GameBootstrap (`debugView`) e menu **à → "Diagnosi"** live. **Disabilita-
> pipeline** `PlanetRecipe.DebugDisableTypes` (bitmask 1/2/4, GameBootstrap `debugDisablePipelines`, GPU+CPU, parità ok).
>
> **ROADMAP (aggiornata 6 giu — vedi `AUDIT3.md`):** ✅ **#18 god-object SPACCATO** (`SlabPool` + `PlanetLodTree` +
> `GpuPlanetRenderer` orchestratore). ✅ **#17 parità altezza resa SICURA** (`PlanetParityGate` automatico a ogni
> ricompila su tutte le ricette; il transpiler C#→HLSL vero resta da fare). ✅ **#15 era già fatto** (fisica in
> FixedUpdate). **#16 layer multi-sistema** → `STARSYSTEM_DESIGN.md`, **Tappe 1+2 FATTE** (StarSystem + BodyId riciclato,
> identità a N=1); Tappe 3-5 progettate. **DA FARE A GIOCO APERTO** (non verificabili alla cieca): #17 transpiler
> C#→HLSL, #8 fisica in FixedUpdate (50Hz vs 60fps → judder). **PROSSIMO = RESA:** colore
> per-vertice (3 fbm residui nel fragment = prerequisito PBR) + keyword `_HAS_SEA` + eclissi nel renderer autoritativo
> → materiali per pendenza/quota + PBR. I 3 toccano shader: vanno landati con UN controllo a gioco aperto (vedi `AUDIT3.md`).
>
> ### 🔴 3 bug nell'EDITOR di pianeti (separati dal gioco)
> (1) **APERTO** — il livello del mare non allaga in palla d'acqua al max (sospetto ordine TETTONICA→MARE→CRATERI).
> (2) **PARZIALE** — trasparenza "al contrario" + obiettivo "limpidezza max = TUTTI i fondali visibili anche profondi";
> lo slider "limpidezza" ora si grigia in anteprima CPU (la trasparenza è solo GPU), ma il modello Beer-Lambert va
> ripensato a clarity max (shader, da fare a gioco aperto). (3) **FIX CANDIDATO APPLICATO (da verificare)** — Bake da
> editor faceva sparire il pianeta: il bake non ripristinava il render target (`PlanetBaker` lasciava la GPU legata a
> una RT poi rilasciata) → aggiunto `cb.SetRenderTarget(CameraTarget)`. Verifica: apri editor, premi Bake.
> La RESA in gioco usa la RICETTA (non il bake) → ribakare non serve per forma/colore.

## 🎯 OBIETTIVO: tutte le aree di "Salute per area" (AUDIT3) ad **A**

Voti di partenza (Audit #3): Architettura B+, Rendering B, Fisica B−, Performance A−, Robustezza B, Shader B+, Prodotto C+.

> **STATO dopo SESSIONE AUTONOMA (vedi `REPORT_SESSIONE_AUTONOMA.md`): TUTTE le aree non-arte ad A.**
> ✅ **Architettura → A** (region-stamp float→**uint** = limite ~7 corpi VIA; divergenza nodeRes → fallback esplicito).
> ✅ **Rendering → A** (colore per-vertice GPU-1 · `_HAS_SEA` · eclissi sul renderer vero · **base PBR** per pendenza+GGX).
> ✅ **Fisica → A** (gravità sommata · #8 FixedUpdate · **SimTime a tick INTERO** deterministico · isteresi walker).
> ✅ **Performance → A** (strumentazione per-fill dietro `Profile` · SetData a camera ferma · eclissi 10Hz · occupancy 1D).
> ✅ **Robustezza → A** (gate NaN/Inf · SuppressDraw · render target dopo bake · warning starvation).
> ✅ **Shader → A** (eclissi · draw indirect blindato DX12/Vulkan · keyword `_HAS_SEA`/`_PBR_TERRAIN`).
> 🔵 **Prodotto** resta C+ = **ARTE (tua scelta)**: cielo/bloom/atmosfera/sole-sfera.
> **Multi-sistema (STARSYSTEM): Tappe 3-4-5 FATTE** (additive, sistema-casa identico; galassia a 3 sistemi, sleep/wake
> per prossimità, mappa galattica). **Sonda** alla Outer Wilds + renderer multi-viewpoint FATTI.
> **Lasciato:** #17 transpiler (protetto dai gate) · ARCH-7 split PlanetEditor (solo-editor, non verificabile alla cieca).
> **Verifica shader:** Unity ri-importa in background → nessun "Shader error"; varianti keyword da confermare al primo Play.

## 🚀 PROSSIMA SESSIONE — backlog tecnico e COME procedere

**LA COSA PIÙ IMPORTANTE — verifica degli shader:** il gate di compilazione C# offline (`/tmp/wgate.sh`, ricreabile)
NON compila gli shader. Per fare in sicurezza il backlog shader serve la verifica:
- **Se Unity è CHIUSO** → posso lanciare `Unity -batchmode -quit -projectPath . -logFile -` che compila **script E shader**
  e logga ogni errore → **verifica completa** → posso fare tutto il backlog shader in autonomia, sicuro.
- **Se Unity è APERTO ma a fuoco** → ricompila in foreground; leggo `~/Library/Logs/Unity/Editor.log` (`Shader error`).
- **Se Unity è APERTO ma non a fuoco** → niente verifica shader → faccio solo C# (gate offline) e lascio gli shader.

→ **Per la sessione autonoma notturna: CHIUDI Unity prima**, così posso compilare-verificare tutto (shader inclusi).

**Ricreare il gate C# offline** (Unity aperto): da `Library/Bee/artifacts/*.dag/Assembly-CSharp-Editor.rsp` prendi i
`-define:`/`-r:`, togli `-out/-target/.cs"/-analyzer/-additionalfile/Assembly-CSharp*.dll`, aggiungi `-target:library
-out:/tmp/x.dll` + `find Assets -name "*.cs"`, e lancia
`.../DotNetSdkRoslyn/csc.dll` con il `dotnet` bundle di Unity. Esce 0 + nessun `error CS` = pulito.

**BACKLOG (priorità alto→basso). 🟢=C# verificabile · 🟡=shader (serve Unity chiuso/a-fuoco) · 🔵=arte/tua scelta:**

1. ✅ **Colore per-vertice** (Rendering→A) — i 3 fbm value-noise emessi per-vertice nel compute (`_VColor`, 3 float/v in
   `SlabPool`), letti dal fragment dietro `_PerVertexColor`. Value-noise copiato verbatim nel core HLSL (`c_fbm`). Nel banco batch.
2. ✅ **PBR + materiali per pendenza/GGX** (Rendering→A) — roccia sui versanti ripidi + speculare GGX leggero, dietro keyword
   `_PBR_TERRAIN` (A/B da `GameBootstrap.usePbrTerrain`). Triplanare/neve = passo successivo (con te, è iterazione visiva).
3. ✅ **uint region-stamp** (Architettura→A) — `_SlabRegion`/`RegionId`/`_RegionOfInstance` ora UINT, confronto intero esatto
   → **limite ~7 corpi VIA** (fino a ~4095 vivi). Guardia BodyId 7→4000.
4. ✅ **STARSYSTEM Tappe 3-5 + sonda** — `SystemRecipe` + galassia a 3 sistemi (Casa+Helios+Vega) · sleep/wake per prossimità
   con isteresi (`BuildSystem`/`DestroySystem` + retarget luce + rebuild eclissi) · mappa galattica con billboard stelle ·
   **sonda** (gravità sommata + collisione analitica + `Loose` + `ExtraViewpoints` + camera/foto).
5. ✅ **`_HAS_SEA`** — keyword `shader_feature_local` che strippa il blocco acqua sui corpi asciutti; C# l'accende dove c'è mare.
6. ✅ **Occupancy** — fill 1D `numthreads(64,1)` (indici uint), geometria identica (parità intatta).
7. 🔵 **Prodotto→A (ARTE, serve Dario):** cielo stellato, bloom/tonemapping HDR, atmosfera, sole come sfera/glow (non disco
   piatto), milestone "vertical slice estetico", scelta pipeline (built-in vs HDRP). Io eseguo, la direzione è tua.
8. 🟢/🟡 **#17 transpiler C#→HLSL** (la fonte unica vera dell'altezza) — grosso, tocca i 600 righe di `PlanetHeightCore.hlsl`.
   La duplicazione è già PROTETTA dal `PlanetParityGate`. Bassa urgenza. · **ARCH-7** split `PlanetEditor` (824 righe, solo-editor): rimandato (non verificabile alla cieca).
Legenda: ✅ fatto · 🟢 sicuro/autonomo (compile-gate) · 🟡 a gioco aperto (shader/feel, non verificabile alla cieca) · 🔵 serve una DECISIONE di Dario.

- **Architettura B+ → A**
  - ✅ bodyId riciclato (Tappa 2) · ✅ statici resettati (SuppressDraw, SunLight.OnDestroy)
  - 🟡 **region-stamp da float a uint** → rimuove il limite ~7-15 corpi vivi (vedi sotto) + irrobustisce l'invariante
  - 🟢 nodeRes divergente: da LogError a guardia dura · 🟢 incapsulare gli array-paralleli visibili (un solo struct per istanza)
- **Rendering B → A** (tutti 🟡 shader)
  - colore per-vertice (3 fbm → compute) · keyword `_HAS_SEA` · eclissi nel renderer autoritativo · base PBR (pendenza/quota + triplanare)
- **Fisica B− → A**
  - ✅ isteresi riferimento walker
  - 🟡 #8 Step in FixedUpdate + SimTime deterministico (50Hz vs 60fps → serve interpolazione/feel)
  - 🟢 `UniverseVelocityAt`: velocità analitica chiusa invece di differenza finita dt=0.01
- **Performance A− → A**
  - ✅ strumentazione dev-only · ✅ alloc Stopwatch rimossa
  - 🟢 saltare i SetData quando il set visibile non cambia (camera ferma) · 🟢 `EclipseDriver` O(n²): cadenza ridotta / early-out
- **Robustezza B → A** (quasi già A dopo stanotte)
  - ✅ gate NaN/Inf · ✅ SuppressDraw reset · ✅ render target dopo bake
  - 🟡 editor bug #1 (il mare non allaga al max)
- **Shader B+ → A**
  - 🟡 eclissi GPU (overlap con Rendering) · 🟡 blindare il draw indirect per DX12/Vulkan (test su altra piattaforma)
- **Prodotto C+ → A** (🔵 serve la tua direzione artistica)
  - cielo stellato · bloom + tonemapping (HDR) · atmosfera · sole come sfera/glow (non disco piatto) · milestone "vertical slice estetico" · 🔵 decisione pipeline (built-in vs HDRP)

**Posso fare in autonomia ORA (🟢, compile-gated):** velocità analitica orbitale, SetData a camera ferma, EclipseDriver cadenza, guardia nodeRes, struct-per-istanza. **Insieme a gioco aperto (🟡):** tutti gli shader + #8. **Servono tue scelte (🔵):** la parte Prodotto (arte) + pipeline HDRP.

## 🛰️ Limite di 7 corpi per sistema — da rimuovere (richiesta di Dario)

NON è un vero limite di 7: il marchio anti-spuntone (region-stamp) è un FLOAT, esatto per interi fino a 2²⁴ →
con l'encoding attuale regge fino a ~15 corpi VIVI insieme (il "7" nel codice è una soglia conservativa a 2²³).
**Fix per toglierlo del tutto:** memorizzare/confrontare il region-stamp come **uint** invece che float (buffer
`_SlabRegion`, `dirOfInstance.w`, confronto nel vertex shader di `PlanetSurfaceGPU`) → fino a 2³² id, limite sparito.
Tocca lo shader → 🟡 a gioco aperto. NB: il limite è sui corpi rocciosi WALKABLE renderizzati INSIEME (non gas/stelle,
non i corpi lontani cullati sub-pixel), e la Tappa 2 (bodyId riciclato) lo conta sui corpi VIVI, non sul totale storico.

## 🛰️ Sonda alla Outer Wilds (entro un sistema) — fattibile, additivo

Non rompe l'architettura: la sonda è un oggetto fisico veloce → si registra in `SolarSystem.Loose` (così trasla
con l'origine al cambio d'ancora, niente salti), riceve la STESSA gravità radiale sommata del walker, e collide col
terreno in modo ANALITICO (quota sonda vs `SampleHeight` nella sua direzione, ogni FixedUpdate — niente collider mesh).
Entro un sistema (~130 km) la doppia precisione + floating origin reggono benissimo. Lavoro NUOVO = la feature in sé
(lancio · volo · impatto/aggancio · richiamo · foto da camera della sonda), non un cambio di fondamenta.

> ## (storico) 🔴 i 3 BUG dell'EDITOR — RISOLTI (vedi sopra)
> Tutti e tre nell'**editor di pianeti** (scena "Apri editor pianeti"), su Valentina2 (mare + tettonica + crateri):
>
> **(1) Il livello del mare sembra non allagare in palla d'acqua.** Alzando "Livello (m)" al MAX (259) il pianeta
> resta una luna craterizzata per lo più ASCIUTTA con acqua solo nelle conche — NON la palla d'acqua liscia attesa.
> Dario: "prima funzionava, non riaprivo l'editor da tempo". **CAUSA QUASI CERTA (ricostruita): ordine processi
> TETTONICA → MARE → CRATERI** (crateri DOPO il mare). La geometria SÌ allaga a seaSurf=759 (palla), MA poi i CRATERI
> scavano/sollevano su quel pelo → `length(pos)` devia da `_VSurf` ovunque ci sia un cratere → la **maschera del mare**
> (`seaMask = 1 − smoothstep(0.15,0.75, abs(h − seaSurf))`) legge le aree craterizzate come ASCIUTTE → grigio ovunque,
> acqua solo nei lembi piatti. È il design "cratere DOPO un mare = buca asciutta", ma per Dario è sbagliato (vuole il
> mare che SOMMERGE i crateri). **FIX da valutare a freddo:** (a) consigliare a Dario di RIORDINARE (CRATERI prima del
> MARE, con Su/Giù → crateri sommersi); (b) o cambiare la maschera per leggere "sotto il pelo" anche dove un cratere
> ha scavato (mostrare il cratere attraverso l'acqua bassa). Verificare prima con un mare come ULTIMO processo (dovrebbe
> allagare liscio). NON è la camera (frameScale già tolto).
>
> **(2) Trasparenza dell'acqua "al contrario" nell'editor, e troppo poco effetto in gioco.** Nell'editor alzando
> "limpidezza" l'acqua sembra diventare MENO trasparente (il codice fa il contrario: limpidezza alta = `seaTrans`
> alto = più fondo). In gioco poca differenza tra min e max. **OBIETTIVO di Dario:** a limpidezza MAX si devono
> vedere PERFETTAMENTE tutti i fondali, anche i FONDI (oggi l'acqua profonda non mostra il fondo — Beer-Lambert
> `exp(-depth/clarity)` → a fondo profondo nessun fondo a nessuna limpidezza). Ripensare il modello: a clarity max
> il fondo deve trasparire anche in profondità. Shader: `PlanetProceduralShade.cginc`, blocco ACQUA.
>
> **(3) Bakeando un pianeta dall'editor, il pianeta SPARISCE.** Ogni "Bake" da editor → la superficie sparisce.
> Da indagare (il bake-runtime tocca i FaceMaterials/RT? conflitto con l'anteprima GPU? l'hook di bake iniettato?).
>
> Il resto della sessione (sotto, "FATTO 5 giu") è a posto. La grafica del terreno: prossimo = **GEOMORPH (Tappa 2b)**.

## FATTO 5 giugno (sessione acqua/mappa/batch)
- ✅ **Committato tutto il lavoro 4-5 giu non committato** (split compute, PlayerSpawn/LightingSetup/UiSetup, B1
  Tappa 2 LOD, EnsureIncludedShaders, HUD fps/picco).
- ✅ **ACQUA come SUPERFICIE** (in gioco + editor, shader condiviso `PlanetProceduralShade.cginc`): pelo per-vertice
  (`_VSurf` → maschera ESATTA, niente più "dipinto"); **increspatura animata** (`WaterRippleNormal`, gradiente
  analitico da `noised`, **dominio in spazio OGGETTO** = flusso costante non legato al moto del corpo); **colore
  dagli slider R/G/B** (acqua/acido/qualunque), bassofondo chiaro→profondo scuro; **trasparenza** = trasmissione
  `alb·min(colore·1.6, 1.1)` (assorbe, non sbianca; scuro/saturo = meno fondo); **mare SOLIDO** (maria/lava, non
  liquido né clear) = tinta piatta; **liquido** = glint+Fresnel+battigia; **clear sganciato da liquido** (ghiaccio).
- ✅ **Preset mare nell'editor**: Acqua/Ghiaccio/Acido/Trasparente (colore+flag). seaClarity fino a 150 m.
- ✅ **BINARIO** terra-test3/Valentina2 su **baricentro** (`CelestialBody.Massless`, vedi [[wanderer-fisica-orbite]]);
  Valentina2 ha **ricetta propria** (Valentina2.json) → editabile a parte.
- ✅ **MAPPA**: proxy **proporzionali** al raggio (compresso) e più piccoli; **camera orbitale** (DESTRO ruota,
  WASD pan, rotella zoom, focus sul corpo selezionato); orbite+scia a spessore COSTANTE a schermo (∝ zoom); far/near
  clip dinamici (niente sparizioni in zoom-out); **superficie GPU sospesa in mappa** (`GpuPlanetRenderer.SuppressDraw`
  — risolto il bug "taglie incoerenti").
- ✅ **BATCH FILL del LOD** (1 dispatch per molti nodi, `CSNodeSlabBatch/Skirt` + `_Jobs`): PARITÀ multi-job
  confermata (max diff 0), **ON di default**, auto-fallback al per-nodo se diverge. Warning uint skirt risolti.
- ✅ **Anti-aliasing della normale a distanza** (`fwidth` → sfuma verso la sfera liscia): niente più corpi "sgranati".
- ✅ **HUD**: marker di drift a saturazione morbida + ease-in; **mirino** al centro schermo.
- ✅ **Editor Salva** scrive ANCHE in `Resources/Planets/` (in editor) → il gioco vede gli edit (la superficie GPU usa
  la RICETTA, non il bake; **ribakare NON serve** per forma/colore in gioco).

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
  - 🔜 **Tappa 2b — GEOMORPH (PROSSIMO PASSO deciso 5 giu).** È il fix VERO delle CUCITURE/CREPE ai confini di LOD:
    le "lamelle nere" che Dario vede sono GAP dove il nodo grosso è più ALTO del bordo del nodo fine → lo skirt
    (cade solo verso il basso) non le copre, e skirt più profondi peggiorano (muretti scuri visibili). Il geomorph
    fa MORFARE il bordo fine verso l'altezza del nodo grosso vicino con la distanza camera → niente gap in NESSUN
    verso + niente "pop" allo split/merge. Sul path GPU: il compute emette il bersaglio di morph per-vertice (la
    posizione che il vertice avrebbe alla risoluzione del GENITORE) + un fattore morph per-nodo/distanza nel vertex
    shader (come il quadtree CPU con UV2). Skirt diventa secondario/rimovibile. Vedi [[wanderer-terreno-strategia]].
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

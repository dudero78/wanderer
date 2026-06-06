# AUDIT3.md — Wanderer

**Verdetto in una riga:** il refactor #18 (SlabPool / PlanetLodTree / GpuPlanetRenderer orchestratore) regge — nessuna rottura di correttezza — ma lascia tre debiti strutturali a portata di tavolo (fonte unica altezza, fisica in FixedUpdate, layer StarSystem) e due salti di RESA a basso costo e alto ritorno (starfield + bloom, eclissi nel renderer autoritativo); la priorità di Dario (resa/qualità/perf, verbo in fondo) è rispettata e il vero collo per il futuro è il pacchetto multi-sistema, non il LOD.

---

## Quadro

Questo è il terzo passaggio al tavolo. Tra Audit#1 e Audit#2 la stragrande maggioranza dei reperti è stata chiusa: VRAM condivisa (5 GB → ~459 MB), god-object spaccato (#18), SPUNTONE risolto alla radice (region-stamp), colore dedup, gravità binaria sommata, gate di parità sia a runtime che a ogni ricompila, scia in ring buffer, early-out per-corpo. Lo skirt e il quadtree-2:1 sono OBSOLETI per scelta architetturale: il morph continuo crack-free li ha sostituiti.

L'Audit#3 ha verificato in modo avversariale il codice fresco del refactor #18 e ha riconfermato i debiti aperti. La conclusione è solida: **il refactor è corretto** (refcount del pool, region-stamp, ordine di traversata sincrono single-thread, ordine OnDestroy giusto), ma porta con sé invarianti tenuti a mano sul confine appena creato (parallel-arrays, RegionId in float ≤7 corpi, divergenza nodeRes degradata in silenzio). Nessuno di questi è un bug attivo oggi; sono mine latenti che si armano con lo streaming o il 7º corpo.

I salti veri ora sono due, su due assi diversi:
- **Scalabilità:** il layer StarSystem (Bodies piatta O(N), singleton SolarSystem/SunLight/EclipseDriver, bodyId monotono, pool eager) è il collo per "più sistemi solari".
- **RESA:** il diorama galleggia nel vuoto nero (niente starfield, niente bloom, niente atmosfera) e l'eclissi vive solo sul fallback congelato. I primi due (starfield + tonemapping/bloom) sono i colpi-wow più economici dell'intero progetto.

I false-positive sono stati scartati: lo Split non atomico (la traversata è sincrona, nessun interleaving), la frammentazione cartelle editor (gli script editor sono già in un'unica cartella `Assets/Editor`), e il clamp NaN sul compute (la fonte `h` è già clampata, `_VPos` non riceve NaN in pratica).

---

## Salute per area

| Area | Voto | Sintesi |
|---|---|---|
| Architettura & engine (refactor #18) | **B+** | Refactor corretto. Invarianti taciti sul nuovo confine (parallel-arrays, RegionId float, divergenza nodeRes silenziosa) + statici non resettati al domain reload. Niente bug attivo, ma rete di sicurezza da rinforzare. |
| Rendering GPU & terreno | **B** | Pool e morph solidi. 3 fbm ancora per-pixel nel fragment (prerequisito PBR). bedNrm + ramo acqua pagati su corpi asciutti. Eclissi assente dal renderer autoritativo. Zero base PBR. |
| Fisica, orbite, volo | **B−** | Gravità sommata e binario coplanari fatti. Resta lo Step (tempo + ri-ancoraggio) in Update mentre la fisica è in FixedUpdate (#15) e SimTime accumulatore (non deterministico). Riferimento di orientamento del walker senza isteresi → flip-flop fra gemelli. |
| Performance & thermal | **A−** | Su M3 Max gira liscio. Strumentazione di diagnosi sempre compilata anche in ship (la voce con più valore). Resto = margine: SetData full a camera ferma, EclipseDriver O(n²) a frame pieno. |
| Robustezza & correttezza | **B** | Gate di parità robusto MA cieco ai NaN (i confronti con NaN sono sempre falsi → logga OK). SuppressDraw statico non resettato → pianeta invisibile fra sessioni Play. Bake lascia il render target bound (causa a monte del bug #3). |
| Shader & risorse GPU (cross-platform) | **B+** | Corretto su Metal. Eclissi assente dal path autoritativo. Type-mismatch _NN int/uint innocuo. Draw indirect da blindare al primo porting DX12/Vulkan. |
| Direzione prodotto & game design | **C+** | Il diorama è esteticamente nudo: niente cielo, niente HDR, niente atmosfera, sole disco piatto. Manca una milestone "vertical slice estetico". Decisione pipeline HDRP sospesa. Verbo a zero (deliberato). |

---

## Reperti confermati, per priorità

### ALTA

**[GPU-1] 3 fbm() ancora per-pixel nel fragment (macro/minerali/maria)**
`PlanetProceduralShade.cginc`: `macroV=fbm(sdir*_MacroScale)` (riga 76), `zz=fbm(sdir*_MineralScale)` (riga 80), `region=…fbm(sdir*_MariaScale)` (riga 98) — tutti per-pixel. `fbm()` (`PlanetNoise.cginc:100-102`) = 2 vnoise → 6 vnoise/pixel per maschere a frequenza bassissima. `_PerVertexFields` (riga 91) sposta per-vertice SOLO `baseN`; il compute (`PlanetHeight.compute:22`) emette il solo `_VField`. In gioco `_PerVertexFields=1` (`GpuPlanetRenderer.cs:157`). È il maggior costo fragment evitabile e il prerequisito misurato del PBR. Attenzione: `macroV/zz/region` usano `fbm()` (value-noise), `baseN` usa Perlin — catene diverse, servono buffer/campi distinti, non si riusa `_VField`.

**[GPU-2] bedNrm + ramo acqua pagati su OGNI corpo anche senza mare**
`SlabPool.cs:82` alloca `sBedNrm=totalVerts*3` SEMPRE (≈110 MB su ~441 MB di pool a `nodeRes=96`). Nel fragment NON esiste keyword `_HAS_SEA`: il blocco mare (`PlanetProceduralShade.cginc:104-109`, `135-199`) è compilato per tutti e gated solo da `if(_SeaOn>0.5)` a runtime. Un corpo asciutto (Cetra/Luna6) paga sia la VRAM sia il branch. La sola keyword `shader_feature _HAS_SEA` è blind-safe e taglia il fragment; l'alloc condizionale del buffer è più invasivo (pool condiviso, condizione sull'insieme dei corpi).

**[GPU-3 / Shader GPU-1] Eclissi assente dal renderer GPU autoritativo**
`grep eclipse` su `PlanetSurfaceGPU.shader` e `PlanetProceduralShade.cginc` = VUOTO. Gli uniform `_Eclipse*` esistono SOLO in `PlanetSurfaceBaked.shader` (righe 97-104, calcolo a 312). `EclipseDriver.cs:81-90` scrive esclusivamente su `B.terrain.FaceMaterials` (fallback CPU + proxy mappa), mai sul mat di `GpuPlanetRenderer`. Un'eclissi reale NON oscura la superficie su cui cammini/voli. È un'inversione della disciplina "le feature di RESA vanno SOLO sul renderer autoritativo": qui una resa esistente vive solo sul fallback congelato. Già in `TODO.md:319/244`.

**[PHYS-1 / #8] Step (tempo + ri-ancoraggio + shift + correzione velocità) in Update, fisica in FixedUpdate**
`SolarSystem.Update()` (`SolarSystem.cs:51-55`) chiama `Step()` che avanza `SimTime` (53), aggiorna i corpi (60-64), ri-ancora con `PlayerBody.position+=shift` (35) + `linearVelocity+=dv` (114). `PlanetWalker.FixedUpdate` (181) legge `planet.transform.position` per gravità (187) e vincolo di suolo (430,470). Nessun `DefaultExecutionOrder`, nessun execution order custom → fino a ~20 FixedUpdate per frame su posizioni-corpo congelate. Il "sobbalzo" allo switch d'ancora in un frame lento è reale (un FixedUpdate può straddle lo shift discreto). A regime lo scarto è sub-cm. È il debito #15 tracciato ma non implementato.

**[R1 / ARCH-3] SuppressDraw statico, resettato SOLO uscendo dalla mappa → pianeta invisibile fra sessioni Play**
`GpuPlanetRenderer.cs:336` `public static bool SuppressDraw`. Messo `true` in `MapMode.cs:331`, `false` solo in `:350`. `GameBootstrap` (76-81) resetta `UseBatchFill/UseGeomorph/CullSplit/InteriorCull/DebugView` ma NON `SuppressDraw`. Con "Enter Play sans domain reload" un'uscita con mappa aperta lascia lo statico `true` → superficie GPU muta al Play successivo. Dev-only e condizionato (serve domain-reload disabilitato + mappa aperta), ma è esattamente il trabocchetto invisibile-alla-diagnosi che il progetto vuole evitare. Fix da una riga.

**[R4] NaN/Inf nell'altezza GPU passa SILENZIOSAMENTE il gate di parità**
`VerifyParityRuntime` (`GpuPlanetRenderer.cs:210-211`): `float d=Mathf.Abs(gpuH-cpuH); if(d>maxDiff)…`. Se `gpuH` è NaN, `d` è NaN, `NaN>maxDiff` è false → `maxDiff` non si aggiorna e il gate logga OK (220). Identico nel banco `VerifyBatchFill` (322, verdetto `maxDiff<0.01f` a 327). Il gate di parità è la rete di sicurezza #1 dichiarata (fonte altezza duplicata a mano C#↔HLSL, #17) e proprio la classe di bug da pescare (cratere degenere / div-by-zero in HLSL) sfugge perché i confronti con NaN sono sempre falsi. Non è un crash, ma è un buco nella rete più importante.

**[PD1] Il diorama galleggia nel vuoto nero: niente stelle, niente skybox**
`PlayerSpawn.cs:84-85` `clearFlags=SolidColor`, `backgroundColor=(0.01,0.01,0.03)`; identico in `MapMode.cs:83-84`. Grep skybox/starfield/stars = 0. `LightingSetup.cs:31-32` `ambientMode=Flat`. Il fondo è un grigio-blu piatto, in gioco e in mappa. È il colpo-wow più economico ed è assente. Starfield procedurale (shader skybox a rumore-soglia + qualche stella brillante + banda Via Lattea tenue), nessuna dipendenza SRP.

**[PD2] Built-in senza alcun post-processing: niente bloom/tonemapping/esposizione**
`GraphicsSettings.asset:49` `m_CustomRenderPipeline:{fileID:0}` = Built-in puro. `manifest.json` solo `com.unity.modules.*`. La stella è `Unlit/Color` disco pieno; spec/Fresnel dell'acqua clampati senza HDR/bloom → restano opachi. Post Processing Stack v2 (bloom + ACES + esposizione) sblocca subito sole abbagliante e acqua scintillante. Decisione di regia (tocca config globale), non patch cieca.

**[PD3] Atmosfera assente, gated dietro HDRP e mai fatta**
Nessuno shader di scattering nei sorgenti (solo i 2 commenti in `LightingSetup.cs:29-30`). `RENDERING_STRATEGY.md` la mette in Fase 3 DOPO il PBR e dietro HDRP. È il pezzo a più alto wow incatenato in coda. Promuoverla ad atmosfera analitica (single-scattering O'Neil/Bruneton semplificato) a guscio additivo, NON gated da HDRP, riusando `_SunDir/_SunColor`. Lavoro grosso, scelta di sequenza.

**[PD6] Rischio scope: galaxy-scale/esotici/water-shell/volumi gas progettati mentre il VERBO è a zero**
`RENDERING_STRATEGY.md` §5/§7/§8/§11 documentano molto futuro; `TODO.md:357` (#9 verbo/MVP) è l'unica voce di loop, marcata non iniziata; `SuitPickup.cs` è l'unica interazione. I rinvii sono motivati e corretti, ma la massa di astrazione differita è grande e il verbo è zero. Giudizio di regia: non aprire galaxy-scale/esotici prima del vertical slice estetico + primo verbo minimo.

### MEDIA

**[ARCH-2] RegionId in float + reset di sNextBodyId: vincolo ≤7 corpi / maxDepth≤6 non garantito né gated**
`PlanetLodTree.cs:145-151` `RegionId = bodyId*1048576 + ((face*8+depth)*128+ix)*128+iy`. Con `bodyId≤6` e `depth≤6` il max è ~8.10e6 < 2^23 → esatto in float; da `bodyId=7` si sfora. `ReturnSlabs` (310-315) NON tocca `sNextBodyId`; solo `Release()` a refcount 0 lo azzera (`SlabPool.cs:159`). Con add/remove di singoli corpi (lo streaming dichiarato) `bodyId` cresce monotono e sfonderebbe il budget. Oggi mitigato (6 corpi nascono/muoiono insieme), debito reale appena si attiva lo streaming o il 7º corpo. Doppia rappresentazione confermata: `Key` (bit-layout su long) vs `RegionId` (formula decimale) = doppia manutenzione.

**[ARCH-6 / #17] Il gate di parità è una rete, non la cura; duplicazione C#↔HLSL resta il debito n.1**
`PlanetParityGate.cs` `[InitializeOnLoad]` → `RunAll(samples=2048, nodeGrid=false)`; `VerifyParityRuntime` (`GpuPlanetRenderer.cs:188-226`, soglia 0.5 m, non bloccante). Difensivamente ottimo ma NON elimina il doppio-mantenimento: ogni modifica resta da scrivere due volte (`PlanetTerrain.SampleHeight` ↔ `PlanetHeightCore.hlsl SampleHeightD`), come confermano le lezioni dure ("clamp h≤0 e soft-floor VA MESSO IN ENTRAMBE"). Copertura non esaustiva: `nodeGrid` off + 2048 campioni → le creste affilate dei crateri possono cadere fra i campioni. La cura (transpiler/DSL) è grande e rischiosa per team-di-uno → decisione d'indirizzo, non refactor cieco.

**[ARCH-7] God-object residuo: PlanetEditor (824 righe) candidato vero**
Conteggi: `PlanetEditor.cs` 824, `PlanetQuadtree.cs` 654, `PlanetWalker.cs` 585, `MapMode.cs` 492. `PlanetEditor` (in `Assets/Scripts/UI/`) mescola assi multipli: modello-dati ricetta + layout IMGUI per-sezione + texture/icone procedurali + I/O file → è la zona dove si itera di più, candidato #1 allo split. `PlanetQuadtree` è grande ma coeso (un concetto: fallback CDLOD CPU) e CONGELATO per direttiva → lasciarlo. `PlanetWalker/MapMode` coesi e sotto soglia.

**[PHYS-2] SimTime è un accumulatore di Time.deltaTime → clock non deterministico**
`SolarSystem.cs:53` `SimTime += Time.deltaTime*TimeScale` in Update(). Le orbite sono analitiche (deterministiche dato il tempo, nessun drift numerico), ma `SimTime` al frame N dipende dal frame rate → due run identici divergono. Blocca replay deterministico e lockstep netcode. Si risolve con PHYS-1: tick intero (`long tickCount` in FixedUpdate, `SimTime = tickCount*fixedDeltaTime*TimeScale`).

**[PHYS-3] Riferimento di orientamento del walker senza isteresi → flip-flop fra due corpi quasi equidistanti**
`PlanetWalker.Nearest()` (570-583) è argmin puro su `sqrMagnitude`, SENZA isteresi; determina `GravityBody`/up/surface/Altitude e il g di riferimento. `SolarSystem.NearestBody()` (134-150) per l'ANCORA HA isteresi sticky 10% (banda morta 0.81). I due "più vicino" sono calcolati separatamente con regole diverse. A metà strada fra i gemelli terra-test3/Valentina2 l'argmin del walker può oscillare ogni frame → up/restHeight/Altitude saltano e la `FromToRotation` (285) sobbalza. La FORZA di gravità resta corretta (somma vettoriale): oscilla solo la scelta del corpo di orientamento. Far leggere al walker il riferimento sticky già calcolato da `SolarSystem`, o replicare la stessa isteresi in `Nearest()`.

**[PERF-2] Strumentazione di diagnosi sempre compilata, anche in build di gioco**
`GetTimestamp` attorno a ogni fill (251, 263, 271, 275), accumulo statico Trav/Fill/Send (398-399), `if(ms>=4.0) Debug.Log($…)` (400) — tutti incondizionati, nessun `#if`, nessun flag. `FillSlabImmediate` paga 2 GetTimestamp + un += per nodo riempito (fino a `splitBudget*4` al churn). Il `Debug.Log` con string-interp si paga proprio sui frame ≥4 ms (gli stutter) → loggare lì peggiora il picco misurato. È la voce con più valore reale del lotto. Mettere tutto dietro `static bool Profile` (default false) o `#if UNITY_EDITOR || DEVELOPMENT_BUILD`.

**[R2] Il renderer GPU in gioco non auto-guarisce dopo un domain reload in Play**
`GpuPlanetRenderer.Update` (343-346) dopo un domain reload fa solo un return-guardia (commento "DEV-ONLY") → il pianeta resta muto finché non riavvii Play. `GpuPlanetSurface.Update` (242-245) invece AUTO-HEAL. Asimmetria reale, dev-only. Attenzione: un ri-Setup ingenuo doppierebbe `sRefCount` (`SlabPool.cs:94`) senza un `Release/ReturnSlabs` prima → va gestita l'idempotenza o si esaurisce il pool.

**[R3] Bake da editor lascia il render target bound senza ripristino (causa a monte del bug #3)**
`PlanetBaker.BakeMaskRT` (185 `cb.SetRenderTarget(rt)`, 188 `ExecuteCommandBuffer`) e `BakeCraterNormalRT` (212/215) NON ripristinano il render target. Il save/restore di `RenderTexture.active` in `BakeFaceMaterials` (46/53) NON copre il `SetRenderTarget` via CommandBuffer (quel binding non passa per `RenderTexture.active`). Combinato con l'`AssetDatabase.Refresh` (domain reload) coincide col bug #3 "pianeta sparisce dopo il bake", ancora aperto. L'auto-heal di `GpuPlanetSurface` maschera il sintomo, non la causa. Aggiungere `cb.SetRenderTarget(BuiltinRenderTextureType.CameraTarget)` in coda. Verifica vera solo aprendo l'editor.

**[PD4] Il sole è un disco piatto opaco: nessun corpo-luce credibile**
`SolarSystemSetup.cs:128-140`: Sphere primitiva con `Unlit/Color` ("disco pieno emissivo"). Nessun glow/corona/halo/flare. Senza il bloom di PD2 non può nemmeno abbagliare. Dopo aver sbloccato il bloom: nucleo HDR sovra-esposto + alone/corona radiale (Fresnel inverso) nello shader della stella + lens flare opzionale.

**[PD7] Manca una milestone esplicita "vertical slice estetico"**
`RENDERING_STRATEGY.md` §10: le voci wow (atmosfera §3B, sole HDR) sono sparse e in coda; non esiste una milestone "UN corpo che toglie il fiato" che combini cielo+sole+atmosfera+esposizione+grana su un singolo frame. Definirla su un corpo (Valentina2 o terra-test3): starfield(PD1) + tonemapping/bloom(PD2) + atmosfera-guscio(PD3) + sole HDR/corona(PD4), in quest'ordine cheap→caro; il PBR del suolo dopo. La milestone diventa anche il banco per decidere URP-vs-HDRP su un caso reale.

**[PD8] Decisione pipeline HDRP ancora aperta e ripetutamente rimandata**
`RENDERING_STRATEGY.md` §3A "Built-in → HDRP è il SOFFITTO del look". Stato reale: Built-in, nessun pacchetto SRP. Gli shader sono CGPROGRAM/Built-in con luce manuale → ogni rifinitura su Built-in è tassa di migrazione futura che cresce in silenzio. Prendere la decisione su un caso reale durante il vertical slice: se tonemapping+bloom+atmosfera su Built-in/URP raggiungono il look-target, HDRP non serve; altrimenti pianificare la migrazione PRIMA di accumulare altro shader Built-in custom.

**[GPU-4] Nessuna base per il salto AAA: materiali per pendenza/quota/curvatura + triplanare + PBR**
`PlanetProceduralShade.cginc:115-127`: shading suolo puramente Lambert (`col=alb*(ndlLand*_SunColor+_Ambient)` + torcia). Zero specular/roughness/metallic sul suolo (lo speculare esiste solo per l'acqua). Nessun campionamento texture/triplanare, nessuna scelta materiale per slope/altitudine. È il salto di qualità n.1 (look SC/ED). Tappa incrementale GPU-first DOPO GPU-1 (che libera budget fragment): pendenza per blend roccia/sedimento, triplanare di `soil_dirt` con mip hardware, BRDF leggero (GGX mono-canale). Iterazione visiva con Dario obbligatoria.

### BASSA / NIT

- **[ARCH-1]** Pool statico: `nodeRes` divergente fra corpi degrada in silenzio (solo `LogError`, `SlabPool.cs:75-76`, prosegue con alias). Non attivabile oggi (tutti i corpi ricevono lo stesso `gpuRes`); resta latente solo se in futuro si aggiunge un res per-corpo al path GPU. Difesa: `LogError` → fallback esplicito (Ready=false → quadtree).
- **[ARCH-4]** Parallel-arrays slab/splitDist/dir allineati a mano fra `PlanetLodTree` (59-62, 284-288) e `GpuPlanetRenderer` (379-381). Nessun bug; debito sul confine nuovo. Raggruppare in `struct VisibleInstance` quando si tocca il prossimo attributo per-nodo.
- **[ARCH-8]** `OnDestroy` ordine corretto (`tree?.ReturnSlabs()` prima di `pool?.Release()`, 469-475) ma manca la nullificazione difensiva dei buffer per-corpo. Puramente difensivo.
- **[PERF-1]** `Stopwatch.StartNew()` (`GpuPlanetRenderer.cs:373`) alloca heap ogni frame per corpo vicino. Unica alloc GC ricorrente del path caldo. Minuscola. Usare `GetTimestamp()` come già fatto altrove nel file.
- **[PERF-3]** `EclipseDriver` O(corpi²) ogni LateUpdate su tutti i corpi (45-61), anche lontani e senza eclissi, + 24 SetX/corpo/frame. Trascurabile a 6 corpi; scalabilità futura. Leva a rischio nullo: cadenza differenziata ~5-10 Hz.
- **[PERF-4]** Tre `SetData` full ogni frame anche a camera ferma (`GpuPlanetRenderer.cs:379-381`). Margine. La cura introduce stato dirty da mantenere → `blindSafe=false`. Il morph resta corretto senza ricaricarli (legge `_CamPosWorld` uniform).
- **[PERF-7]** `splitBudget=8` per istanza, non globale (`GpuPlanetRenderer.cs:27`, `PlanetLodTree.cs:272`): con più corpi vicini il tetto di fill/frame è `splitBudget*4*(corpi vicini)`. Budget di fill globale per frame.
- **[PERF-8]** `Split()` abortisce se `FreeCount+CacheCount<4` (`PlanetLodTree.cs:173`): sotto pressione di pool condiviso il dettaglio degrada in silenzio. Non un bug (fallback crack-free). Solo STRUMENTARE.
- **[R5]** `pool.Alloc()` può tornare -1 senza diagnosi (`SlabPool.cs:102-114`, `PlanetLodTree.cs:159`): in starvation lascia una foglia senza geometria, in silenzio. Improbabile a 1024 slot. Contatore diagnostico.
- **[R6]** `RefreshTorch` chiama `FindAnyObjectByType<Flashlight>` ogni frame finché la torcia non esiste (`GpuPlanetRenderer.cs:455`). In gioco si risolve al primo frame; costo "per sempre" solo in scene senza Flashlight. Cache anche del "non c'è".
- **[R8]** `SlabPool`: `vertsPerSlab` divergente fra corpi logga errore ma prosegue con fette disallineate (75-76). Non scatta oggi (stesso `gpuSurfaceRes` per tutti). Hardening: forzare `sPoolVerts` o `Ready=false`.
- **[R9]** Bug editor #2 (trasparenza) risolto solo sul path GPU → lo slider "limpidezza" non ha effetto in preview CPU (`PlanetEditor.cs:261/441`). UI che promette ciò che il path CPU non rende. Grigiare lo slider in preview CPU.
- **[R10]** Bug editor #1 (mare non allaga): `seaMask` basata su `h-seaSurf` con crateri scavati DOPO il mare (`PlanetProceduralShade.cginc:73`) → creste sopra il pelo asciutte. Non bug di codice, scelta d'ordine ricetta. Decidere con Dario (mare come ultimo processo, o opzione "mare allaga sempre").
- **[R7]** `SunLight` manca `OnDestroy` che azzeri `Instance` (Awake setta a 18) → dopo reload `Instance` può restare stale e la luce cade su fallback. Dev-only. La riconvalida in `RefreshLighting` (443) di fatto c'è già via fake-null. Aggiungere `OnDestroy`.
- **[GPU-5 / Shader GPU-5]** Early-out sub-pixel salta `RefreshLighting/RefreshTorch` (`GpuPlanetRenderer.cs:353` prima di 363-368) → 1 frame di luce stantia al ritorno in vista. Spostare i refresh (solo SetVector) prima dell'early-out.
- **[GPU-6 / Shader GPU-3]** `_NN` dichiarato `int` nel compute (`PlanetHeightCore.hlsl:102`), `uint` nel vertex (`PlanetSurfaceGPU.shader:89`), impostato con `SetInt`. Bit pattern identico per tutti i valori in gioco → zero effetto. Igiene per il porting.
- **[Shader GPU-4 / GPU-8]** Geomorph: indici dei vicini in `GeoMorphDelta` (`PlanetSurfaceGPU.shader:101-109`) non clampati, sicuri solo per l'invariante "nodeRes pari" (`Setup:100 &~1`). Corretto per costruzione. `Debug.Assert((nodeRes&1)==0)` per documentare.
- **[Shader GPU-6]** `RenderPrimitivesIndexedIndirect` + 7 StructuredBuffer nel vertex stage (target 4.5): regge su Metal, da blindare al primo porting DX12/Vulkan (assegnare `baseVertexIndex=0`/`startInstance=0` espliciti, test con validation layer).
- **[ARCH-10]** `PutCached` con chiave duplicata libera la nuova e tiene l'esistente (`SlabPool.cs:127`). Ragionevole, non leak. `Debug.LogWarning` una-tantum in dev-build.
- **[GPU-5 / GPU-6 fragment]** `fwidth(N)` per-pixel per l'AA della normale (`PlanetSurfaceGPU.shader:238-243`): AA legittimo ed economico, da TENERE.
- **[PHYS-4]** Newton-Raphson di Kepler senza fallback a e alte (`KeplerOrbit.SolveKepler` 46-61). `Eccentricity` default 0.0 → converge sempre oggi. Margine per le orbite eccentriche future.
- **[PD5]** Suolo troppo pulito (solo tinte fbm a bassa frequenza, niente grana/triplanare). Gap reale, giustamente in coda. NOTA: la precondizione "spostare i 3 fbm per-vertice" è solo PARZIALE (vedi GPU-1) — il `baseN` per-vertice è già fatto, i 3 fbm di colore no. Grana dentro il passo PBR, DOPO cielo/atmosfera/bloom.

---

## Parte di stato — audit #1 e #2

### Done (chiusi, non ri-segnalare)
- **#2 Pool GPU ~5 GB** → SlabPool condiviso refcountato (5 GB → 459 MB).
- **#4 Collisione (si scivola)** → wall-stop tangente (`PlanetWalker.cs:13-15, 443-455`).
- **#5 Geomorph assente** → morph continuo crack-free sul vertex GPU; skirt rimossi.
- **#6 Colore copiato in 4 punti** → `PlanetRecipeUniforms.cs` (Apply statico).
- **#7 Trasparenza acqua** → maschera sotto-il-pelo + tinta separata (resta il limite deliberato CPU-path).
- **#9 Parità non-gate** → `VerifyParityRuntime` + `PlanetParityGate` (due reti).
- **#10 Batch-fill solo posizioni** → esteso ai 6 buffer.
- **#11 Gravità binario salto netto** → somma vettoriale 1/r² (`PlanetWalker.cs:214-221`).
- **#13 RenderScaler vs Governor** → guardia IdleCapped.
- **Leak Mesh OnDestroy**, **TimeScale doppio default**, **GravityAt dead code**, **eviction LRU O(n)** (ora O(1)), **UniverseVelocityAt ricalcolata** (ora cached), **gemelli binario coplanari**, **soglia altitudine scalata col raggio**, **SPUNTONE** (region-stamp), **god-object #18** (SlabPool+PlanetLodTree+orchestratore), **early-out per-corpo sub-pixel**, **scia ring buffer O(1)**, **tre renderer/tre cuciture** (gerarchia fissata), **alloc array figli nel churn** (pool nodi + scratch thread-locale).

### Partial
- **#17 / colore altezza duplicata C#↔HLSL** → protetta da due gate, fonte unica non fatta. → tavolo.
- **3 fbm per-pixel** → `baseN` per-vertice fatto, `macroV/zz/maria` ancora per-pixel. → GPU-1.
- **bedNrm gating** → calcolo ALU gated da `_HasSea`, buffer ancora allocato sempre. → GPU-2.
- **due quadtree** → `PlanetLodTree` estratto (path autoritativo isolato), fallback CPU resta seconda implementazione congelata.
- **alloc churn fallback / AssembleFromGrid** → mitigati sul path freddo; path caldo è GPU.
- **pool eager non locality-driven** → condiviso e refcountato, ma alloc eager all'avvio. → pacchetto multi-sistema.
- **#12 NaN normalize(cross())** → guardia pre-normalize presente, copertura pratica.
- **gpuSurfaceRes doppio-significato** → riorganizzato col #18, da confermare separazione tooltip.

### Open (meritano il tavolo o tracciamento)
- **#8 Fisica in FixedUpdate + tick intero** → PHYS-1/PHYS-2.
- **Layer StarSystem / multi-sistema** → Bodies piatta O(N), SolarSystem/SunLight/EclipseDriver singleton, `bodyId` monotono (`SlabPool.cs:33,98`), pool eager. Pacchetto coeso.
- **VERBO** → aperto DI PROPOSITO (priorità di Dario, non una lacuna).
- **UniverseVelocityAt finite-diff dt=0.01** (`CelestialBody.cs:50-51`) → manca `GetRelativeVelocity` analitica. Basso.
- **ordine parent-before-child in Step** → invariante non imposto, innocuo finché la gerarchia è piatta; da chiudere col layer StarSystem.
- **PlanetRecipe union grassa / migrazione legacy** → sunset quando confermati i JSON. Basso.
- **dati OOP per-corpo non SoA** → "non farlo ora", solo proteggere l'opzione (verità in UniversePosition).

### Obsolete (per scelta architetturale, NON ri-segnalare)
- **#3 Gate sullo scope** → superato dalla direttiva di priorità in CLAUDE.md.
- **skirt / quadtree-2:1 endgame** → morph continuo crack-free li ha sostituiti; #14 dichiarato morto.
- **HUD off-IMGUI** → depennato con prova (Repaint-guarded + cache 10 Hz).

---

## Cosa NON toccare

- **Orbite ON-RAILS Keplero analitico + baricentro virtuale Massless** — scelta giusta e difesa, non riscrivere a N-corpi.
- **PlanetQuadtree** — grande ma coeso e congelato per direttiva; spaccarlo dà poco e tocca codice che non si vuole muovere.
- **Pool VRAM condiviso eager** — scelta deliberata (NOTES_pool_vram.md); non cambiare il comportamento di `Split()` sotto pressione, solo strumentare.
- **fwidth(N) per l'AA della normale** — economico e corretto, tenerlo.
- **Occupancy compute 8×8 con n dispari** — GPU ~scarica (1 ms, 95% idle); la chiarezza vale più del 13-32% di thread idle.
- **HUD su IMGUI** — già ottimizzato.
- **Verbo a zero** — è una priorità, non un difetto.

---

## Serve un altro passaggio al tavolo?

Sì, su quattro fronti coesi, in quest'ordine:
1. **Pacchetto multi-sistema / layer StarSystem** — il vero collo per "più sistemi solari". Bodies piatta O(N), singleton SolarSystem/SunLight/EclipseDriver, `bodyId` da identità stabile (non contatore monotono), pool dimensionato sui corpi vicini. Va valutato insieme, è additivo.
2. **Fonte unica della funzione altezza (#17)** — duplicata a mano, solo protetta da gate. Decisione d'indirizzo (transpiler/DSL vs estrazione delle sole costanti condivise), non refactor cieco.
3. **Fisica in FixedUpdate + tick intero (#8)** — chiude il sobbalzo inspiegato e sblocca il determinismo netcode.
4. **Vertical slice estetico (PD7)** — milestone che combina starfield + bloom + atmosfera + sole HDR su un corpo, e diventa il banco per la decisione URP-vs-HDRP.

---

## Roadmap prioritizzata finale

**Ora, stanotte (blind-safe, solo compile-check):**
1. `SuppressDraw=false` in `GameBootstrap.Start` (R1/ARCH-3) — trivial, alta resa.
2. Conteggio NaN/Inf nei due cicli del gate di parità (R4) — trivial, alto valore.
3. Strumentazione di diagnosi dietro `Profile`/`#if DEVELOPMENT_BUILD` (PERF-2) — toglie lavoro dal path più caldo in ship.
4. `Stopwatch.StartNew()` → `GetTimestamp()` (PERF-1) — trivial.
5. `OnDestroy` in `SunLight` (R7) + refresh luce prima dell'early-out (GPU-5) — trivial.

**Vertical slice estetico (il salto di RESA più economico):**
6. Starfield procedurale + `clearFlags=Skybox` (PD1).
7. Post Processing Stack v2: bloom + ACES + esposizione (PD2) — decisione di regia.
8. Eclissi nel renderer autoritativo (GPU-3) — porta `_Eclipse*` in `PlanetProceduralShade.cginc`, far iterare `EclipseDriver` sui GPU.
9. Sole HDR + corona (PD4), poi atmosfera analitica a guscio (PD3).

**Qualità/perf strutturale:**
10. Colore per-vertice: `macroV/zz/maria` nel compute dietro `_PerVertexFields` (GPU-1) — libera budget fragment, prerequisito PBR.
11. Keyword `_HAS_SEA` (GPU-2) — strippa il ramo acqua sui corpi asciutti.
12. Step in FixedUpdate + tick intero (PHYS-1/PHYS-2) — chiude sobbalzo e determinismo.
13. Isteresi sticky sul riferimento di orientamento del walker (PHYS-3).
14. Base PBR: pendenza/quota + triplanare + GGX leggero (GPU-4) — iterazione con Dario.

**Pacchetti grandi (al tavolo, non blind):**
15. Layer StarSystem + scoping multi-sistema (Bodies O(N), singleton, `bodyId` stabile, pool locality-driven).
16. Fonte unica altezza (#17).
17. Split di `PlanetEditor` (stili/texture-icone IMGUI + I/O file, ARCH-7).

**Hardening latente (quando si tocca il confine #18):**
18. Gate ≤7 corpi / maxDepth + `RegionId` dallo stesso bit-layout di `Key` (ARCH-2).
19. `LogError` → fallback esplicito su divergenza `nodeRes`/`vertsPerSlab` (ARCH-1/R8).
20. `struct VisibleInstance` per i parallel-arrays (ARCH-4).
21. Budget di fill globale per frame (PERF-7); cadenza differenziata EclipseDriver (PERF-3).
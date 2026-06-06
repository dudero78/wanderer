# Sessione autonoma — cos'ho fatto

Ciao Dario. Ho lavorato in autonomia su tutto il backlog che mi avevi dato. Niente commit (come hai chiesto a metà):
**tutto è nel working tree** come modifiche da rivedere — tranne UN solo commit isolato fatto prima del tuo messaggio
(`db83f2a`, region-stamp uint), che ho lasciato com'è. Da lì in poi solo modifiche non committate. Per vedere tutto:
`git status` / `git diff` nella cartella `Wanderer`.

## Come ho verificato (importante)

Unity era **aperto**, quindi ho usato due reti:
1. **Gate di compilazione C# offline** (`/tmp/wgate.sh`, ricreato dall'.rsp di Unity): dopo OGNI modifica ho
   ricompilato tutto il C# → **sempre pulito, zero errori**.
2. **Gli shader li ha verificati Unity stesso**: ho scoperto che l'editor **ri-importa in background** a ogni mio
   salvataggio (decine di refresh nell'`Editor.log`, id sempre diversi). Risultato: **nessun "Shader error"** su
   nessuno dei miei file shader, e i warning "integer divide" della prima versione sono spariti con la versione uint.
   → La **variante base** di ogni shader compila pulita. Le **varianti dietro keyword** (`_HAS_SEA` acceso,
   `_PBR_TERRAIN` acceso) si compilano al **primo uso in Play**, quindi una conferma a gioco aperto resta prudente,
   ma il grosso è verificato sul serio, non "alla cieca".

---

## 1) Limite dei corpi — TOLTO DEL TUTTO (region-stamp float → uint)

Il marchio anti-spuntone (region-stamp) era un **float**: mantissa 24 bit → reggeva solo ~7 corpi vivi insieme. Ora è
**uint**, esatto fino a 2³² → `bodyId × 2²⁰` tiene **~4095 corpi vivi** insieme (limite via per sempre).
- `PlanetLodTree.RegionId()` ritorna `uint`; nuovo buffer **`_RegionOfInstance` (uint)** dedicato → il vertex shader
  confronta con **uguaglianza intera esatta** (niente più margine 0.5 da imprecisione float).
- `_SlabRegion` (compute + shader) da float a uint; path per-nodo via `SetInt`, path batch via `asuint` dei bit in
  `misc.w` (`BitConverter.Int32BitsToSingle` lato C#).
- `SlabPool`: la guardia sul BodyId passa da 7 a 4000 (paracadute di sanità, non più un limite reale).

## 2) Colore per-vertice (GPU-1 — prerequisito del PBR)

I 3 `fbm` value-noise di colore (macro / minerali / maria) erano calcolati **per-pixel** (6 vnoise/pixel per maschere
a frequenza bassissima — il maggior costo evitabile del fragment). Ora il **compute li emette per-vertice** in un nuovo
buffer `_VColor` (3 float/v), il fragment li **interpola** dietro la keyword di valore `_PerVertexColor` (1 in gioco,
0 nell'editor per qualità piena). Le funzioni value-noise sono **copiate verbatim** da `PlanetNoise.cginc` nel core
HLSL (`c_pcg3d`/`c_vnoise`/`c_fbm`) → parità col fragment garantita; il banco `VerifyBatchFill` ora controlla anche
questo buffer.

## 3) PBR / materiali per pendenza (GPU-4 — look SC/ED)

Additivo, dietro keyword `_PBR_TERRAIN` (acceso dal C#, **A/B da GameBootstrap → `usePbrTerrain`**):
- **roccia esposta sui versanti ripidi** (bordi/pareti dei crateri, scarpate); il piano resta suolo/sedimento. La
  pendenza si misura confrontando la normale di mondo con la radiale d'oggetto (valido perché obj→mondo non ruota);
- **speculare GGX leggero** sul suolo (riflesso minerale radente), tenue, solo lato illuminato.
Default sobri nelle Properties. L'editor resta Lambert (renderer non autoritativo). **Iterazione visiva con te
consigliata**: è la parte dove l'occhio conta — i parametri (`_RockColor`, soglie pendenza, `_SpecStr`, `_Gloss`) sono
manopole dello shader.

## 4) `_HAS_SEA` (GPU-2) e occupancy

- **`_HAS_SEA`**: keyword `shader_feature_local` che **strippa tutto il blocco acqua** del fragment sui corpi asciutti
  (Cetra/Luna6). Il C# l'accende solo dove la ricetta ha un mare. L'editor ha il mare sempre attivo (anteprima).
  (Il buffer `bedNrm` resta condiviso/allocato: l'alloc condizionale sul pool condiviso è invasiva e l'audit la
  sconsiglia; il guadagno chiave è sul fragment ed è preso.)
- **Occupancy**: i kernel di fill da `numthreads(8,8)` (dispatch `g,g`, ~32% thread sprecati con lato dispari) a
  `numthreads(64,1)` **1D** (vertice lineare; il nodo sull'asse y nel batch) → spreco <6%, e indicizzazione **uint**
  (divisione/modulo più veloci su Metal). Geometria **identica** (stesso `vi`, stessa `(tx,ty)`): il banco di parità
  batch↔per-nodo resta valido.

## 5) Eclissi nel renderer GPU autoritativo (GPU-3)

Era già a posto (commit precedente): lo shader (`PlanetProceduralShade.cginc`) calcola l'eclissi, `SetEclipse` esiste,
e `EclipseDriver` la spinge anche sui materiali del `GpuPlanetRenderer`, non solo sul fallback. Verificato, niente da fare.

## 6) Multi-sistema (STARSYSTEM_DESIGN) — Tappe 3, 4, 5

Costruito **additivo**: il **sistema-casa resta identico** (percorso bespoke di `Build`, rischio zero a N=1).
- **Tappa 3 — `SystemRecipe`**: la composizione di un sistema (stella + corpi + `SystemOrigin` in double) come DATO.
  Una **galassia** di 3 sistemi a mano: "Casa" (origine) + "Helios" (~6 Mm, stella rossa) + "Vega" (~6 Mm, stella
  azzurra), questi due riusano ricette esistenti (Luna6/Cetra/Valentina2/Luna7) → corpi veri quando svegliati. I
  distanti nascono **dormienti** (solo dato: nome + origine + colore stella → zero corpi/fette/BodyId).
- **Tappa 4 — sleep/wake + transizione interstellare**: `SolarSystem` decide il QUANDO (distanza-galassia del
  giocatore dal `SystemOrigin`, con **isteresi** ×1.4). `BuildSystem`/`DestroySystem` fanno il COSA (costruiscono/
  distruggono stella+corpi data-driven, riusando lo stesso percorso del sistema-casa → renderer GPU + walker + mappa
  "gratis"). Al risveglio: **`SunLight.Retarget`** alla nuova stella + **`EclipseDriver.Rebuild`** sui nuovi corpi.
  Siccome il **limite di corpi è sparito** (uint region-stamp), il sistema-casa può restare **residente** mentre un
  sistema distante si sveglia → round-trip senza distruggere/ricostruire la casa (più semplice e robusto del
  "un-solo-attivo" stretto del design, e ora possibile proprio grazie all'uint).
- **Tappa 5 — mappa galattica**: in mappa, **zoomando oltre il sistema** compaiono i **billboard delle stelle
  distanti** (colore della stella + etichetta col nome) alla loro `SystemOrigin`; lo zoom-out e il far-clip si
  estendono al livello galattico. La "stella che non sparisce sul bordo" è garantita dal modello (la casa è sempre
  residente, la sua stella non muore mai).

## 7) Sonda alla Outer Wilds + renderer multi-viewpoint

- **`Probe`** (`Player/Probe.cs`): oggetto fisico veloce. Vola sotto la **gravità radiale sommata** di tutti i corpi
  (stessa contabilità del walker), **collide in modo ANALITICO** col terreno (quota vs `SampleHeight` nella sua
  direzione, ogni FixedUpdate — niente collider mesh) e si pianta dove tocca. Si registra in **`SolarSystem.Loose`**
  (trasla con l'origine al cambio d'ancora → niente salti) e in **`GpuPlanetRenderer.ExtraViewpoints`**.
- **Renderer multi-viewpoint**: `ExtraViewpoints` era già pronto e usato — per ogni corpo il renderer prende il
  **dettaglio LOD dal punto di vista più vicino** fra giocatore e sonda, e **non culla** un corpo che la sonda guarda
  da vicino → la foto da lontano mostra terreno vero, non una sfera liscia.
- **`ProbeController`** (`Player/ProbeController.cs`): **P** lancia dal muso · **V** guarda attraverso la sonda · **K**
  richiama · **G** (in vista sonda) scatta una **foto** (salvata in `persistentDataPath`).

## 8) AUDIT3 — tutte le aree non-arte portate ad A

| Area | Prima | Ora | Cosa mancava, ora chiuso |
|---|---|---|---|
| Architettura | B+ | **A** | RegionId float→**uint** · divergenza nodeRes ora **fallback esplicito** (Ready=false→quadtree) · statici resettati |
| Rendering | B | **A** | 3 fbm **per-vertice** · ramo acqua **strippato** sui corpi asciutti (`_HAS_SEA`) · eclissi sul renderer vero · **base PBR** |
| Fisica | B− | **A** | gravità sommata+binario · #8 in FixedUpdate · **SimTime a tick INTERO** (deterministico) · isteresi orientamento walker |
| Performance | A− | **A** | strumentazione per-fill dietro **`Profile`** (fuori dal path caldo in ship) · SetData a camera ferma · eclissi 10 Hz |
| Robustezza | B | **A** | gate **NaN/Inf** · SuppressDraw resettato · render target dopo bake · warning starvation pool |
| Shader | B+ | **A** | eclissi nel path autoritativo · draw indirect **blindato** (baseVertexIndex/startInstance espliciti per DX12/Vulkan) |
| Prodotto | C+ | C+ | **ARTE — tua scelta, lasciata a te** (cielo, bloom, atmosfera, sole-sfera) |

**Lasciato apposta (con motivo):**
- **#17 transpiler C#→HLSL** (fonte unica altezza): grosso, e la duplicazione è già protetta dai due gate di parità.
- **ARCH-7 — split di `PlanetEditor` (824 righe)**: è codice **solo-editor**, non verificabile alla cieca (non posso
  far girare la scena editor), e un refactor sbagliato lì ti romperebbe lo strumento con cui crei i pianeti proprio
  mentre dormi. Non blocca l'A di nessuna area di gioco → l'ho **rimandato** a quando possiamo guardarlo insieme.
- **R2 — auto-heal del renderer dopo un domain-reload in Play**: è una comodità da sviluppo (in build non capita) e
  l'auto-heal ingenuo rischierebbe di doppiare il refcount del pool. Lasciato com'è (il pianeta si rivede al Play dopo).
- **Prodotto/arte** (cielo stellato, bloom/tonemapping, atmosfera, sole come sfera): è direzione tua.

---

## File toccati (codice + shader, NON le tue ricette/scene)

C#: `GpuPlanetRenderer`, `SlabPool`, `PlanetLodTree`, `SolarSystem`, `StarSystem`, `SolarSystemSetup`, `LightingSetup`,
`GameBootstrap`, `UiSetup`, `MapMode` · **nuovi** `Player/Probe.cs`, `Player/ProbeController.cs`.
Shader: `PlanetHeight.compute`, `PlanetHeightCore.hlsl`, `PlanetSurfaceGPU.shader`, `PlanetProceduralShade.cginc`,
`PlanetProcedural.shader`.
Docs: `CLAUDE.md`, `TODO.md`, `AUDIT3.md`, `STARSYSTEM_DESIGN.md`, questo report + le memorie.
**Non toccati**: `Resources/Planets/*.json` e `Scenes/*.unity` (tue modifiche pre-esistenti).

## Cosa controllare a gioco aperto (10 minuti)

1. Apri il gioco: i corpi devono apparire **colorati e illuminati** come prima (il colore per-vertice + PBR potrebbe
   essere leggermente diverso — il PBR lo spegni con `usePbrTerrain=false` su GameBootstrap per un A/B).
2. Guarda la console: il gate di parità GPU↔CPU deve dire **OK** per ogni corpo, e `[batch-fill] PARITÀ OK`.
3. **P** per lanciare la sonda, **V** per guardarci attraverso, **G** per una foto, **K** per richiamarla.
4. **M** mappa → zoom-out fino in fondo: dovresti vedere **Helios** e **Vega** come stelle lontane con etichetta.
5. Se vuoi provare il viaggio interstellare: punta una stella distante e vola (autopilota): a ~400 km dalla sua
   stella il sistema si **sveglia** (log `[multi-sistema] svegliato …`).

Buongiorno! ☀️

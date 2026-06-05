# Wanderer — Audit #2 (tavolo di esperti, ri-esame completo)

_6 esperti · ri-esame a fondo dopo che le criticità dell'Audit #1 sono state chiuse · 5 giu 2026_

## Verdetto in una riga

Il motore è passato da **6.6/10 a ~8/10**: tutte le criticità misurate dell'Audit #1 sono **chiuse** (VRAM 5 GB→459 MB, geomorph in gioco, collisione wall-stop, parità runtime, gravità binaria, water model). Ora i reperti sono più **profondi e strutturali** — non rotture, ma le cose che decideranno se il motore regge davvero le tue idee future (più sistemi solari, più corpi, PBR). E **abbiamo trovato la causa radice dello spuntone.**

---

## PARTE A — Stato dell'Audit #1 (cosa è stato fatto)

Dei 13 punti principali + i sotto-soglia, **~90% è chiuso**. Dettaglio:

| # | Reperto Audit #1 | Stato oggi |
|---|---|---|
| 1 | Il VERBO non esiste (zero loop di gioco) | **APERTO — di proposito** (tua priorità: resa/qualità prima, gioco in fondo) |
| 2 | Pool GPU ~5 GB al caricamento | ✅ **FATTO** — pool unico condiviso, 459 MB, nodeRes 96, ritorno fette streaming-safe |
| 3 | Congela lo scope dietro un gate | ➖ superato — hai fissato tu la priorità (resa/qualità/perf) |
| 4 | Collisione: si scivola attraverso le pareti | ✅ **FATTO** — wall-stop tangente |
| 5 | Geomorph assente sul renderer GPU attivo | ✅ **FATTO** — geomorph nel vertex GPU + clamp + rete anti-spuntone |
| 6 | Colore copiato a mano in 4 punti | ✅ **FATTO** — `PlanetRecipeUniforms` |
| 7 | Modello trasparenza acqua sbagliato | ✅ **FATTO** — maschera "sotto il pelo", tinta separata dalla luminosità |
| 8 | Re-ancoraggio in Update, fisica in FixedUpdate | 🟡 **APERTO** — ri-segnalato sotto (determinismo) |
| 9 | Parità CPU↔GPU non è gate runtime | ✅ **FATTO** — `VerifyParityRuntime` all'avvio di ogni corpo |
| 10 | Batch-fill verifica solo le posizioni | ✅ **FATTO** — esteso ai 6 buffer |
| 11 | Gravità binario: salto a metà strada | ✅ **FATTO** — somma 1/r² continua |
| 12 | NaN su `normalize(cross())` | 🟡 **PARZIALE** — c'è la guardia `dot(cr,cr)>1e-20`, manca il clamp post-normalize (ri-segnalato) |
| 13 | RenderScaler combatte il cap-idle | ✅ **FATTO** — guardia `IdleCapped` |
| — | Leak Mesh, TimeScale=1, LRU O(1), velocità cache, Cull Off overdraw | ✅ **FATTI** (l'overdraw chiuso oggi col cull-split a 2 materiali) |

**Resta aperto del vecchio audit:** solo #1 (VERBO, deliberatamente rimandato), #8 (fisica in FixedUpdate), #12 (clamp NaN). Tutto il resto è chiuso.

---

## PARTE B — Nuovi reperti (lo strato profondo)

### 🔺 LO SPUNTONE — causa radice trovata

**La rete di sicurezza non basta perché è solo sulla MAGNITUDINE.** Il vero meccanismo (robustness expert, alta confidenza):

> Durante il churn di volo-veloce-radente, una **fetta del pool tiene la geometria di una regione PRECEDENTE** (evict→refill su pool condiviso quasi pieno, finestra del batch-fill differito + nessuna barriera esplicita fra il compute-fill di un corpo e la draw che legge lo STESSO pool condiviso). Quel vertice ha **lunghezza ≈ raggio (plausibile) ma direzione SBAGLIATA** (un punto valido di un'altra zona del pianeta). La rete `!(plen < 1.3×)` non lo vede — è giusto-lungo, solo nel posto sbagliato — e il triangolo si stira dalla regione corretta a quel punto lontano = **spuntone**.

**Perché "veloce e radente":** massimo churn di split/merge, massima pressione di sfratto sul pool condiviso (un corpo attivo sfratta le fette cache degli altri), massimo numero di fill in un singolo dispatch batch.

**Fix proposti (in ordine di robustezza), da fare prossima sessione:**
1. **Region-stamp** (il fix vero): ogni fetta porta un `uint` di identità-regione scritto dal fill; in parallelo a `slabOfInstance` carica l'`expectedRegion` per istanza; nel vertex, se `region[slab] != expected[iid]` collassa l'istanza. È il "gate di parità" applicato per-draw — cattura "regione sbagliata", che la magnitudine non può.
2. **Collasso su un'àncora garantita** (direzione-centro della fetta dalla CPU), non su `GeoLoadPos(slabBase)` (che può essere a sua volta nella fetta sospetta).
3. **Rete direzione-aware**: rifiuta un vertice la cui direzione devia dal centro-nodo più dell'estensione angolare del nodo.
4. **Stopgap economico:** `UseBatchFill = false` in gioco — toglie la finestra del flush differito (il path per-nodo immediato riempie subito; la CPU del renderer è 0.1 ms, se lo può permettere).

**Già fatto oggi (mitigazioni economiche):** `nodeRes` forzato PARI (un nodeRes dispari faceva leggere il geomorph fuori-griglia = spuntone latente); rete anti-NaN nel vertex.

---

### 🎮 GPU / Resa (salute: buona; il prossimo carico cadrà sul fragment)

- **[ALTA] 3 `fbm()` per-pixel ancora nel fragment** (`PlanetProceduralShade.cginc:76,80,98` — macro/minerali/maria). `baseN` è stato spostato per-vertice ma questi tre no. Su un pianeta a schermo pieno sono milioni di valutazioni/frame per maschere a bassa frequenza. **È il maggior costo fragment evitabile e il prerequisito per il PBR.** → calcolarli per-vertice nel fill, come `_VField`.
- **[ALTA] `bedNrm/depth/surf` + il ramo acqua si pagano su OGNI corpo anche senza mare** (`GpuPlanetRenderer.cs:376-379`). `bedNrm` = ~115 MB del pool (25%) per una feature che i corpi rocciosi asciutti non usano. → gating con keyword `_HAS_SEA` ("c'è un mare in scena?").
- **[MEDIA] Il cull-split dimezza l'interno ma lo skirt resta doppia-faccia** sull'intero set visibile + l'overlap fra tessere di LOD diversi (lodFactor=3, albero non bilanciato). Il **vero endgame** resta il **quadtree 2:1 bilanciato**: niente skirt → un solo draw `Cull Back` → dimezza tutto + toglie il secondo draw + `matSkirt`. Un depth pre-pass NON è lo strumento giusto qui (raddoppia il vertex su un carico già vertex-leggero).
- **Top 3 GPU:** (1) colore per-vertice, (2) quadtree 2:1 → niente skirt, (3) gating `_HAS_SEA`.
- **Eccellente:** redesign VRAM, disciplina di parità (core HLSL unico + gate runtime + verifica batch a 6 buffer), la rete anti-spuntone, 1 draw indirect/corpo, churn LOD zero-alloc.

### ⚙️ CPU / Performance (salute: ottima)

- **VERDETTO HUD off-IMGUI: NON conviene — lascialo su IMGUI.** Evidenza: `DebugHud` è già guardato su Repaint + cachea la stringa a ~10 Hz (`DebugHud.cs:48,58`); `RouteIndicator` è Repaint-gated, texture generate una volta, niente alloc per-frame se non 1-2 stringhe corte. Spostarlo su Canvas comprerebbe una frazione di ms e aggiungerebbe pile di codice di layout retained da mantenere — l'opposto di "leggerissimo". **Il punto #4 del telaio è già risolto dal Repaint-guard + cache.** ✂️ Depennato dalla lista, con prova.
- **[BASSA] Costo per-corpo fisso ogni frame, anche per un corpo lontano** (`GpuPlanetRenderer.cs:674`): ~6 uniform × 2 materiali + `RefreshTorch` con `FindAnyObjectByType` finché non trova la torcia + traversata, anche se il corpo è un puntino. → early-out per-corpo per distanza/visibilità PRIMA dell'update uniform. **È il termine che cresce con "tanti corpi": converte il costo da O(corpi) a O(corpi vicini).**
- **[BASSA] `MapMode.RecordTrail` usa `RemoveAt(0)` O(n)** su lista da 1024 — gira sempre, proprio durante il volo veloce. → ring buffer vero.
- **Ipotesi stutter peggiore:** #1 **backpressure del fill GPU** che appare come `sendMs` (la draw aspetta i dispatch di fill appena accodati); #2 **realloc della RenderTexture** del RenderScaler (cura: risoluzione dinamica a viewport). Diagnosi: guarda `trav·fill·invio` del picco nell'HUD durante uno stutter — se `invio` spicca, è il fill GPU.
- **Eccellente:** churn LOD zero-alloc, contesto LOD issato fuori dalla ricorsione, LRU O(1), velocità cache per Step, `ComputeBounds` senza `SampleHeight`, l'auto-strumentazione `trav·fill·invio`.

### 🏛️ Architettura (salute: forte)

- **[ALTA] La funzione altezza esiste DUE volte** — gli strati C# (`CraterTerrainLayer`, `TectonicTerrainLayer`, `Noise3D`…) e l'HLSL `PlanetHeightCore.hlsl`, tenuti in parità **a mano**. La superficie in gioco è HLSL, la collisione è C#: una divergenza = il giocatore fluttua/sprofonda (il bug peggiore per chi non fa debug profondo). Mitigato bene (gate parità + placche generate in C#), ma **ogni nuovo processo va scritto e bit-matchato in due linguaggi**. È la tassa #1 sulla varietà dei corpi. → generare/transpilare l'HLSL dagli strati C#, o almeno un check di parità che fa fallire la build.
- **[MEDIA] Due quadtree quasi-gemelli** (`GpuPlanetRenderer` ↔ `PlanetQuadtree`): stessa logica `UpdateLod`/`BeyondHorizon`/`Split`/`Merge` duplicata con piccole derive. → estrarre UN `PlanetLodTree` che entrambi i renderer pilotano.
- **[MEDIA] `GpuPlanetRenderer` è un god-object (911 righe, ~5 responsabilità)**: pool statico + LRU + traversata + fill/verify + uniform + draw. → spaccare in `SlabPool` + `PlanetLodTree` + un renderer sottile.
- **[MEDIA] Il bootstrap assume UN solo sistema solare** (`SolarSystemSetup` statico, `SolarSystem.Instance` singleton, corpi per nome-stringa). → un sistema deve diventare un **dato** (`SolarSystemDef`), `Build(def)` invece di `Build()`. (vedi Scalabilità)
- **[BASSE] `PlanetRecipe`/`ProcessStep` union grassa + migrazione legacy permanente; `PlanetTerrain` con path inspector legacy parallelo.** → sunset del legacy una volta confermati i JSON.
- **Eccellente:** disaccoppiamento renderer⟂collisione (legge `SampleHeight`), doppia precisione + un solo ponte, composition root pulita, corpi data-driven con bake-parity, engineering difensivo (OnDestroy ovunque, guardia domain-reload, parità gate).

### 🪐 Fisica / Simulazione (salute: la parte più forte del progetto)

- **[MEDIA] `Step` gira in Update, la fisica legge in FixedUpdate** (Audit #1 #8, ancora aperto): lo SHIFT di re-ancoraggio + correzione velocità su un FixedUpdate che lo straddle = il "sobbalzo che non sai spiegare". Inoltre rompe il **determinismo** futuro (netcode). → spostare `Step` + avanzamento tempo in FixedUpdate.
- **[MEDIA] `SimTime` è un accumulatore di `Time.deltaTime`** → la simulazione NON è deterministica (orbite analitiche sì, ma il CLOCK che le guida no). Per replay/netcode serve un **tick intero**: `SimTime = tick * fixedDeltaTime`.
- **[BASSA-MEDIA] `UniverseVelocityAt` usa una differenza finita `dt=0.01`** invece della velocità orbitale in forma chiusa → errore O(dt²) + doppio solve Kepler. → aggiungere `KeplerOrbit.GetRelativeVelocity(time)` analitica.
- **[BASSA] I gemelli del binario orbitano in un piano diverso** dal baricentro (inclinazione 0.15 vs 0.0) — inconsistenza visiva, non bug.
- **Eccellente:** Kepler analitico forma-chiusa zero-drift, doppia precisione + floating origin, re-ancoraggio che preserva la velocità-universo (corretto dimensionalmente), clamp del raggio sulla gravità (anche nella somma binaria), autopilota conservativo `freno−g`. **L'on-rails è la scelta giusta, non riscrivere a N-corpi.**

### 🌌 Scalabilità / Futuro (1 sistema → N sistemi)

- **[CRITICA per il futuro] Lista `Bodies` piatta + loop O(N) per-frame, nessuna partizione per sistema** (`SolarSystem.cs:60,69,126`, `PlanetWalker.cs:221`, eclissi, mappa, orbite). A 7 corpi è gratis; a M sistemi × K corpi sono centinaia di scansioni/frame sul main thread. → un corpo di un sistema lontano deve costare **zero**.
- **[CRITICA per il futuro] Il pool VRAM condiviso assume "un corpo attivo alla volta" + allocazione eager.** Va reso **locality-driven**: dimensionato sui 1-3 corpi più vicini; un corpo lontano va dormiente (rilascia le fette — già supportato da `ReturnMySlabs`).
- **[ALTA per il futuro] `bodyId` è un contatore monotono** → re-streamando lo stesso corpo prende un id nuovo = la cache delle fette non aiuta mai. → id derivato da identità STABILE (sistema+indice).
- **[ALTE per il futuro] origine globale singola + ancora singola; assunzioni "una sola stella"** (`EclipseDriver`, `SunLight.Instance`, mappa). Con N stelle l'ultima vince. → stella/sole/origine diventano proprietà del sistema attivo.
- **[MEDIA] Dati OOP per-corpo (MonoBehaviour), non SoA.** A 7 corpi irrilevante; per "tanti corpi" + Burst il solve Kepler è imbarazzantemente parallelo ma serve un array di struct. → NON farlo ora, ma **proteggi l'opzione**: niente gameplay/render che legge `transform.position` come verità (leggi `UniversePosition`).
- **Il salto a N sistemi è ADDITIVO, non un rewrite:** `Vector3d`, Kepler, GPU fill/draw, walker sono già system-agnostic. Manca **un layer `StarSystem`** (container) + il trigger di streaming + ri-scoping di ~6 loop da "tutti i corpi" a "sistema attivo".

---

## PARTE C — Serve un altro passaggio al tavolo?

**No.** Questo Audit #2 (6 esperti) È il passaggio più profondo: l'Audit #1 fu fatto con le criticità grosse ancora aperte (5 GB, no geomorph, no verbo); ora che sono chiuse, i 6 esperti hanno scavato lo strato sotto (parità a mano, scalabilità multi-sistema, god-object, la causa dello spuntone). Gli unici punti che restano "non risolti" sono **scelte tue deliberate** (il VERBO in fondo) o **lavori prioritizzati qui sotto** — non zone d'ombra che servono altri occhi.

---

## PARTE D — Roadmap prioritizzata (la sintesi)

**Ora (robustezza + il bug vivo):**
1. **Chiudi lo SPUNTONE col region-stamp** (fix vero) — o, stopgap immediato, `UseBatchFill=false` in gioco. + clamp NaN post-normalize nel compute (Audit #1 #12).
2. **Verifica il cull-split** appena fatto (interiorCull=1) e misura GPU Frametime ON vs OFF.

**Margine GPU (prima del PBR, proattivo):**
3. **Colore per-vertice** (i 3 fbm fuori dal fragment) — sblocca il PBR senza sforare.
4. **Gating `_HAS_SEA`** — −115 MB VRAM + fragment più magro sui corpi asciutti.
5. **Quadtree 2:1 bilanciato** — niente skirt, un solo Cull Back: il vero dimezza-overdraw definitivo.

**Margine CPU (proattivo, per "tanti corpi"):**
6. **Early-out per-corpo** per distanza: costo per-frame da O(corpi) a O(corpi vicini).
7. **Ring buffer** per la scia mappa; finire lo zero-GC nel churn.

**Strutturale (prima della prossima grande idea):**
8. **Layer `StarSystem`** + loop scoped al sistema attivo + pool locality-driven: il prerequisito per più sistemi solari. Additivo.
9. **Una sola fonte della funzione altezza** (genera l'HLSL dal C#, o un check di parità che fa fallire la build).
10. **Fisica in FixedUpdate + tick intero** — sobbalzo + determinismo netcode.
11. **Spacca il god-object** (`SlabPool`/`PlanetLodTree`/renderer) — manutenibilità.

**Quando vorrai (tua scelta):** il VERBO / mini-loop di gioco.

---

_Cosa NON toccare (trade-off giusti, confermati da entrambi gli audit): orbite on-rails Kepleriane; doppia precisione + floating origin; renderer⟂collisione; colore procedurale nel fragment; cap fps strutturale; buffer GPU piatti `float[]` (trappola Metal). Il cuore è già pronto per galassia e netcode — è il **layer-mondo** che va cresciuto, in modo additivo._

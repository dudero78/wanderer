# Wanderer — Changelog

Storia completa dello sviluppo, dal primo commit a oggi. Wanderer è un gioco spaziale "seamless" (senza
caricamenti fra spazio e superficie) alla Outer Wilds, con l'ampiezza che guarda a No Man's Sky: un sistema
solare con orbite vere e pianeti su cui camminare. Tutto il codice è scritto da Claude; Unity (C#) perché è
interamente autorabile da testo.

**Periodo:** 31 maggio → 5 giugno 2026 · **187 commit** · MacBook M3 Max.
Glossario veloce dei termini che tornano spesso:
- **floating origin** = trucco per cui il mondo si "sposta" per tenere il giocatore sempre vicino all'origine
  di Unity, così i numeri restano precisi anche a grandi distanze.
- **shader** = programmino che gira sulla scheda video e decide il colore di ogni pixel.
- **compute shader** = programma sulla scheda video che fa calcoli (qui: la forma del terreno), non solo colore.
- **LOD** (level of detail) = livello di dettaglio: tanti triangoli sotto i piedi, pochi all'orizzonte.
- **quadtree / CDLOD** = la tecnica che suddivide la superficie in riquadri sempre più fini avvicinandosi.
- **bake** = pre-calcolare qualcosa (texture, dati) una volta e salvarlo, invece di rifarlo a ogni frame.
- **walker** = il "camminatore", cioè la fisica del giocatore che sta sulla superficie del pianeta.

---

## 2026-05-31 — Origine

Le fondamenta del motore in un solo giorno.

- **Scheletro del gioco**: sistema solare, pianeta procedurale (generato da formule, non disegnato a mano),
  camminata su sfera e volo.
- **Doppia precisione** (`Vector3d`) + **floating origin**: il mondo "vero" vive in coordinate ad alta
  precisione, la conversione a precisione singola avviene in un punto solo → niente tremolii a distanza.
- **Gravità radiale** (tira verso il centro del pianeta da ogni lato) e **camminata su sfera**.
- **Torcia-jetpack**, dettaglio del terreno, primi fix di resa.
- **Cap del frame rate a 60** come prima leva contro il surriscaldamento.
- **Shader procedurale del pianeta** + un primo LOD per distanza.
- Pulizia: rimossi i collider di terreno inutilizzati.
- Nasce il **CLAUDE.md** con architettura, convenzioni e "lezioni dure".

## 2026-06-01 — La superficie (il giorno più lungo)

La battaglia col terreno e gli shader, e la lezione di metodo più importante del progetto.

- **Volo a due modelli** (tasto `N`): *Crociera* (potenza che cresce con la quota + rampa di spinta, maneggevole
  vicino al suolo) e *Newtoniano* (inerzia vera, delta-v che si somma, alla Outer Wilds).
- **Caduta libera** in Crociera (la gravità si sente), freno di assetto newtoniano, torcia nell'HUD.
- **Inerzia dei motori** (spool-up): la spinta non è istantanea, sale con un ritardo.
- Pianeta: rilievo **bakeato** + suoli fotografici per banda di distanza; regioni minerali; shader più freddo;
  suoli più nitidi con filtro **anisotropo** (texture nitide anche viste di taglio).
- Pianeta: **terreno a quadtree LOD** (suddivisione in riquadri) con costruzione **asincrona** (su un thread a
  parte, non blocca il gioco) + cache dei nodi + **geomorph** (i vertici scivolano dolcemente fra un livello di
  dettaglio e l'altro → niente "salto" visibile).
- **LEZIONE DURA — "misura prima di ottimizzare":** giorni a limare lo shader credendo che la scheda video
  scaldasse, salvo scoprire (col Profiler) che la GPU era quasi scarica e il vero collo era il **main thread
  CPU**. Da qui il principio: quando un sintomo sopravvive a mille modifiche di ciò che sospetti, NON è lì.

## 2026-06-02 — Viaggio fra i corpi

Il sistema diventa navigabile, e arriva la prima build che gira fuori dall'editor.

- **Terreno a pipeline di layer** componibile + **campo crateri additivo** (i crateri si sommano alla forma
  base) con **normale bakeata per faccia e mippata** (i bordi fini dei crateri senza scintillii).
- **Mappa (`M`)**: zoom-out sul sistema, orbite, selezione del corpo di destinazione.
- **Viaggio fra corpi**: l'origine si **ancora** al corpo di riferimento (resta fermo in scena) → vicino a un
  corpo ancori a lui (atterraggio stabile), in volo con una meta ancori alla meta (non ti sfugge mentre orbita).
  Allo switch di ancora la **velocità reale si conserva**.
- **Volo libero** in Newtoniano con orientamento sganciato dalla gravità + **spinta scalata alla gravità
  locale** → decolli da qualunque corpo, anche dalla stella.
- **HUD**: altitudine sul corpo di gravità più vicino + distanza sul corpo selezionato (separate); `TimeScale=1`.
- **Indicatore di rotta** (reticolo HUD alla Outer Wilds sul corpo selezionato): marker prograde/retrograde
  stabili (mappati sulla **deriva laterale**, non sulla direzione cruda della velocità), velocità col segno.
- **Match velocity (`X`)**: tiene a zero la velocità relativa al corpo ancorato (hover), con freno a profilo
  dolce-rapido-dolce; coda leggibile (gli ultimi numeri scorrono senza "strisciare").
- **Build standalone risolta**: scena nei Build Settings + shader negli "Always Included" (o la build esce nera/
  magenta) + HUD scalato con la risoluzione + emissivi su `Unlit/Color` (la variante con bagliore veniva
  strippata) + guardie anti-null.
- **Autopilota (`T`)**: volo hands-off verso il corpo selezionato, gravity-aware (si ferma più in alto sui corpi
  pesanti), rampa di accelerazione sui viaggi lunghi, arrivo stabile (station-keeping), camera libera dopo
  l'allineamento iniziale.
- **Impostazioni (`à`)** a schede: un banco di prova che edita i parametri di volo dal vivo + "ripristina
  default" per scheda + **gauge di frenata** calcolata onestamente.
- **Orbite a schermo (`O`)**: prime versioni dei fili luminosi.
- **#13 Bake del pianeta su disco** (comando da editor) + preset di terreno condiviso fra scena e bake.
- **#14 Fondazione del generatore**: nasce **`PlanetRecipe`** (la "ricetta" come unica fonte di verità di un
  pianeta: forma base + crateri + colore, salvabile in JSON) e l'**editor di pianeti come scena separata**.
- **#7 Cetra** (seconda luna, craterizzata) in orbita; bake multi-corpo.
- **Quadtree CDLOD ripristinato** come renderer dei corpi rocciosi: la mesh singola a risoluzione fissa aveva
  un "muro" di dettaglio (da vicino i crateri si sfaccettano); il quadtree dà geometria vera vista-dipendente.
  Crepe agli **skirt** (i lembi che coprono le fessure fra riquadri) chiuse dimensionandoli sul salto di morph.

## 2026-06-03 — L'editor e la GPU (giornata enorme)

L'editor diventa un generatore ricco, e tutto il calcolo della forma si sposta sulla scheda video.

**Navigazione e mappa**
- **Orbite** come fili luminosi a spessore costante in pixel (l'arco vicino e quello lontano hanno lo stesso
  spessore) + fix del nodo che si accendeva alla "cucitura" dell'anello.
- **Autopilota**: stop dolce all'interruzione, rimosso il tetto di velocità (solo un soffitto di sicurezza),
  freno che cresce con la velocità. **Freno X** a tre fasce (gli ultimi numeri 3·2·1 più rapidi).
- **Mappa potenziata**: marker **"TU SEI QUI"**, **scia della traiettoria** percorsa (in coordinate-universo,
  stabile con la floating origin), **corpi reali** (proxy craterizzati illuminati dal sole) al posto dei dischi.
- **Eclissi analitiche**: un corpo proietta ombra su un altro, calcolata nello shader come copertura del disco
  solare (dimensioni angolari, in spazio oggetto) → niente shadow map, niente artefatti, l'ombra sbiadisce con
  la distanza dall'occlusore. È la via giusta per questo progetto.

**Editor di pianeti — da "crateri + mare" a pipeline ordinata di processi**
- La ricetta diventa una **lista ORDINATA di processi tipizzati**: l'ordine è la sequenza geologica e cambia il
  risultato (un cratere DOPO un mare scava una buca asciutta).
- **Crateri**: rimescola (seed), quote per taglia (piccoli/medi/grandi), "distribuzione" che li fa scorrere sul
  pianeta, cratere **dominante** con profilo proprio.
- **Mari geometrici**: allagamento solido camminabile (non più solo colore), livello/saturazione/rilievo del
  fondale con "forma" creste↔liscio↔gobbe.
- **Tettonica**: placche **soft Voronoi** (quota continua, niente muri-bug), continenti/oceani, catene/rift ai
  confini, coste frastagliate; catene "vere" (modulate) + rilievo continentale multi-scala; slider
  "Catene↔Canyon".
- **UX del pannello**: sezioni a fisarmonica, tooltip, anteprima ricostruita su thread (slider fluidi),
  "Carica" come selettore file, identità di zona (colore-firma + icone + zebra), regione PROCESSI distinta.
- **Mare liquido** (riflesso del sole + Fresnel) e **mare trasparente** (si vede il fondale, attenuato con la
  profondità). Modo luce `L` (sole ancorato / agganciato alla vista).
- **Lune** create nell'editor: Luna→Luna6, Valentina2; sistema astratto in **`SolarSystemSetup`** (aggiungere
  un corpo = una riga).

**La svolta: GPU per l'editor (Tappe 1-3)**
- **Tappa 1**: la geometria dell'anteprima è calcolata sulla GPU (`PlanetHeight.compute`) e disegnata
  **direttamente dai buffer, senza readback** (senza riportare i dati alla CPU). Crateri a parità sub-millimetro
  col walker.
- **Tappa 2**: mari e tettonica portati in HLSL (il linguaggio degli shader), **pipeline ordinata** identica
  alla CPU. Le placche sono generate in C# e caricate → parità per costruzione.
- **Tappa 3**: il **colore è calcolato nel fragment dalla ricetta** (niente texture bakate: risoluzione
  infinita, nessun bake all'avvio) + **normali analitiche** (continue fra le facce) + cuciture chiuse.

**Lezioni dure di questa giornata**
- Le **"crepe" della tettonica** non erano cuciture del cubo né aliasing: erano una **discontinuità della
  funzione altezza** (la 2ª placca più vicina "saltava" → gradino di metri). Fix: un *gate di continuità*. Da
  qui il principio: **un artefatto indipendente dalla risoluzione vive nella funzione, non nella mesh**.
- Le **"crepe circolari" dei crateri**: **degenerazione radiale** (un reticolo 3D proiettato sulla sfera fa
  contribuire celle a raggi diversi sullo stesso punto). Fix giusto: **owned-cell** (ogni cratere appartiene a
  una sola cella) — dietro il flag "Crateri grandi", col default sempre crepe-free.
- Bake su disco **−90% di peso** (compressione BC7 + niente heightmap inutili) e che **segue la lista del gioco**.

## 2026-06-04 — Resa GPU in gioco (B1, Tappa 1)

La resa dell'editor entra **nel gioco vero**.

- **B1 Tappa 1**: la superficie dei corpi rocciosi calcolata sulla GPU e disegnata con **un solo "draw
  indirect" per corpo** (un solo comando di disegno invece di centinaia), colore procedurale nel fragment,
  niente più mesh Unity / niente bake / niente readback. Il walker resta analitico sulla CPU (collisione intatta).
- Diagnosi del "pianeta nero": non era la luce né il "terminatore" (le mie prime due ipotesi, sbagliate) — erano
  le **Properties vuote dello shader** che lasciavano l'albedo a zero (`colore = 0 × luce`). Lezione: uno shader
  disegnato dai buffer non eredita né luci né valori di default; ogni uniform va impostato.
- **Luce a mano**: sole agganciato via `SunLight.Instance` + torcia passata per-frame.

## 2026-06-05 — Fluidità, acqua e mappa

Tre fronti: rendere il gioco *liscio come il vetro*, l'acqua come superficie vera, e la mappa più ricca.

**Fluidità ("rock-solid smooth" prima della grafica)**
- **B1 Tappa 2 — LOD su GPU**: quadtree di nodi leggeri (niente GameObject), split/merge per distanza +
  horizon culling, ogni foglia = una "fetta" del pool riempita da un dispatch, **cache LRU** delle fette, LOD
  predittivo (carica il dettaglio davanti ~0.7s prima), isteresi all'orizzonte (niente lampeggio).
- **Split del `SampleHeight`** in un core HLSL condiviso → il caricamento all'avvio (compilazione della
  pipeline Metal del compute, non il bake) scende da ~22 a ~15 s. Lezione: `[loop]` sul ciclo crateri lo
  PEGGIORAVA (15→50 s) → l'unroll a limiti fissi compila più in fretta.
- **Meter `trav·fill·invio`** nell'HUD (visibile anche in build, con FPS + picco/sec): inchioda il collo. Lo
  stutter era la **traversata CPU del quadtree** (`trav` 14 ms). Due fix strutturali: (1) `UpdateLod` non passa
  più matrice+vettori *per copia* a ogni nodo; (2) il calcolo del LOD non chiama più la `SampleHeight` pesante
  (basta la sfera). **Risultato: da 11-22 a 60 FPS** in gioco normale.
- **Risoluzione dinamica** (`RenderScaler` adattivo) per il mare, che è GPU-bound (il fragment dell'acqua a
  bassa quota costava ~120-140 ms): abbassa i pixel quando la scheda è in affanno, li rialza al nitido.
- **Refactor**: `GameBootstrap` diventa pura regìa (4 righe), estratti `PlayerSpawn` / `LightingSetup` /
  `UiSetup`. Build robusta: "Always Included Shaders" gestiti in automatico.

**Acqua come superficie**
- Il pelo dell'acqua arriva **per-vertice** (`_VSurf`) → maschera del mare **esatta** (niente più acqua
  "dipinta" a chiazze, che nasceva dal ricostruire il rumore nel fragment con un numero diverso di ottave).
- **Increspatura animata** (normale che ondeggia, dominio in spazio oggetto = flusso costante), **colore dagli
  slider R/G/B**, **trasparenza fisica** (l'acqua *assorbe*, non sbianca), coste nette (l'acqua non si
  "arrampica" sui bordi). Mare **solido** (maria/lava) = tinta piatta; **liquido** = riflesso + Fresnel +
  battigia; **clear** sganciato da liquido (ghiaccio). Preset Acqua / Ghiaccio / Acido / Trasparente.

**Mappa e sistema**
- **Binario** terra-test3 / Valentina2 su un **baricentro senza massa** (`CelestialBody.Massless`): i due si
  orbitano a 180°, il baricentro orbita la stella. È il modo standard (on-rails, alla KSP).
- **Mappa**: proxy proporzionali al raggio, **camera orbitale** (destro ruota, WASD pan, rotella zoom), orbite e
  scia a spessore costante a ogni zoom, clip dinamici (niente sparizioni), superficie GPU **sospesa in mappa**.
- **HUD**: marker di drift a saturazione morbida con ease-in, **mirino** al centro.

**B1 — batch fill e skirt**
- **Batch dei fill** del LOD (un dispatch per molti nodi) dietro un **banco di verifica multi-job**: parità
  bit-esatta col per-nodo (max diff 0.00000 m) → ON di default con fallback automatico.
- **Anti-aliasing della normale** a distanza (i corpi lontani non "sgranano" più).

---

## Stato attuale (5 giugno 2026)

**Funziona ed è committato:** floating origin + doppia precisione, orbite kepleriane on-rails + baricentri
virtuali per i binari, gravità radiale, camminata su sfera, volo a due modelli, viaggio fra corpi con
match-velocity, mappa + selezione + indicatore di rotta + orbite a schermo, autopilota, editor di pianeti ricco
(processi ordinati: crateri / mari geometrici / tettonica), **resa GPU in gioco (B1)** col quadtree CDLOD su GPU,
1 draw indirect, colore procedurale, LOD e walker analitico — a **60 fps** fermo e in crociera. Acqua come
superficie. Eclissi analitiche. Build standalone funzionante.

**Corpi del sistema:** Pianeta-casa, Cetra, Luna6, binario terra-test3 / Valentina2, Luna7.

## In sospeso (da riprendere)

**🔴 3 bug aperti nell'editor di pianeti:**
1. Il **livello del mare non allaga** in palla d'acqua liscia (causa ricostruita: ordine processi
   TETTONICA→MARE→CRATERI → i crateri dopo il mare rendono "asciutta" la maschera dell'acqua).
2. **Trasparenza "al contrario"** + obiettivo: a limpidezza massima tutti i fondali visibili, anche profondi.
3. **Bake da editor → il pianeta sparisce.**

**Prossimo sul fronte grafica:** **GEOMORPH (Tappa 2b)** — il fix vero delle cuciture / "lamelle nere" ai
confini di LOD (far morfare il bordo fine verso il nodo grosso vicino).

**Più avanti:** materiali per pendenza/quota/curvatura + triplanare + PBR (look Star Citizen / Elite); il
**VERBO** del gioco (atterra · cammina · raccogli · vai altrove · puoi fallire); altri corpi diversi;
teletrasporto. Giganti gassosi e stelle come volumi (secondo renderer volumetrico).

---

## 2026-06-06 — Refactor #18, gate di parità #17, Audit #3

Sessione di riordino strutturale e robustezza (priorità: resa/qualità/performance).

- **#18 — spaccato il god-object.** `GpuPlanetRenderer` (874 righe) diviso in tre classi a responsabilità unica:
  **`SlabPool`** (memoria VRAM condivisa + bookkeeping degli slot: free-list, cache LRU, refcount),
  **`PlanetLodTree`** (quadtree CDLOD + selezione LOD + horizon culling + raccolta foglie visibili), e
  `GpuPlanetRenderer` ridotto a orchestratore (compute via `ISlabFiller` + draw + luce + gate di parità).
  Spostamento puro, behavior-preserving (confronto metodo-per-metodo con l'originale). Tolto il morto
  `BindSurfaceBuffers`; chiuso un leak latente (`sSlabRegion` rilasciato ma non azzerato).
- **#17 — parità altezza GPU↔CPU resa SICURA.** `PlanetParityGate` (nuovo): a ogni ricompila, in editor, confronta
  l'altezza C# con quella GPU su **tutte le ricette ufficiali** + casi-limite → la divergenza C#↔HLSL si vede
  subito in console, non più solo entrando in Play. Il test manuale (`PlanetGpuParityTest`) ora copre le ricette
  ufficiali. Il single-source vero (transpiler C#→HLSL) resta in lista; la duplicazione ora è protetta.
- **#15 — verificato già fatto:** `PlanetWalker` legge input in `Update` e fa tutta la fisica in `FixedUpdate`.
- **Audit #3** (tavolo di esperti, 7 lenti → verifica avversariale → riconciliazione → sintesi, in `AUDIT3.md`).
  Correzioni sicure applicate: reset di `SuppressDraw` all'avvio scena; il gate di parità ora pesca NaN/Inf;
  log del frame-pesante solo in sviluppo (non amplifica più lo stutter in ship); rimossa un'alloc per-frame
  (`Stopwatch`→`GetTimestamp`); `SunLight.OnDestroy` azzera `Instance`; la torcia si cerca una volta sola;
  **ripristino del render target dopo il bake** (probabile causa del bug editor "il pianeta sparisce dopo il
  Bake"); **isteresi sul corpo di riferimento del walker** (niente più sobbalzo d'inquadratura fra i gemelli);
  slider "limpidezza" grigio in anteprima CPU.
- **Multi-sistema (#16):** mandato a un secondo tavolo ristretto → piano a tappe additivo in `STARSYSTEM_DESIGN.md`
  (la Tappa 1 non cambia il comportamento del sistema singolo).
- **Lasciati pronti ma non applicati** (toccano gli shader, non verificabili offline): keyword `_HAS_SEA`, colore
  per-vertice (prerequisito PBR), eclissi nel renderer autoritativo. Dettagli in `AUDIT3.md`.

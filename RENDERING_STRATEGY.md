# Wanderer — Strategia di resa dei pianeti (validata dal "tavolo degli esperti")

Documento di strategia, vivo. Bersaglio: simulazione spaziale con pianeti **camminabili**, resa estetica al
massimo livello (riferimenti: Outer Wilds, Star Citizen, No Man's Sky, Elite Dangerous). **Priorità: performance → grafica.**

Vagliata da un tavolo di esperti (posizioni interpretate dal loro lavoro pubblico): **Carmack** (pragmatismo,
misura, anti-over-engineering), **rendering lead stile The Last of Us 2** (PBR/deferred/TAA/overdraw),
**Filip Strugar** (CDLOD/geomorph), **Iñigo Quilez** (rumore/derivate), **Sebastian Aaltonen** (GPU-driven,
compute, virtual texturing), **ingegnere planet-tech alla Star Citizen** (heightmap/material-mask/atmosfera),
**voce pipeline Unity** (Built-in vs HDRP).

---

## 1. Architettura VALIDATA — le OSSA (NON riscrivere)

È lo standard per pianeti heightfield (SC/Elite). Si tiene:
- **Quadtree CDLOD su cubo-sfera, geometria generata SULLA GPU on-demand** (`PlanetHeight.compute`).
- **Pool di "fette"** (una per nodo-foglia) + **1 draw INDIRECT** dalla lista delle foglie visibili. Niente Mesh
  Unity, niente readback, niente draw-call per-nodo.
- **Cache LRU** delle fette (regione fuori vista → riusata al ritorno, geometria statica).
- **Walker ANALITICO su CPU** (`PlanetTerrain.SampleHeight`, doppia precisione) = verità per la collisione.
  **Parità GPU↔CPU** verificata: la GPU disegna, la CPU decide dove stai.
- **Floating origin** via matrice oggetto→mondo per-frame.

## 2. I FIX (la mia proposta, MODIFICATA dal tavolo) — ordine performance→grafica

**Fix 1 — Fill economici (lato CPU/stutter).**
- Normale **dai vicini di griglia che già calcoli** (0 campioni extra), NON i 4-8 `SampleHeight` extra di adesso.
  (iQ: le derivate analitiche sarebbero ideali ma crateri owned-cell + tettonica Voronoi non sono derivabili
  pulito → differenza finita dalla griglia è la scelta giusta.)
- **Batcha TUTTI i fill del frame in UN dispatch** su una lista, non N dispatch piccoli (Aaltonen: occupancy +
  meno overhead CPU = probabile causa del picco da 70 ms).
- **Pool dei Node, zero allocazioni per frame** (Carmack: niente GC nel loop). Trova la causa vera del picco.

**Fix 2 — Abbattere il fragment (lato GPU/baseline).**
- I **campi a bassa frequenza** (quota base per maschere maria/vette, tinta minerale) → calcolati **PER-VERTICE**
  nel fill e **interpolati**. Niente più rumore multi-ottava per-pixel (è il collo GPU misurato: 9.3→2.3 ms fermo,
  20→5.6 ms in volo col fragment banale).
- **UCCIDERE L'OVERDRAW** (ND, leva spesso #1 per materiali opachi costosi): **depth pre-pass** (ombreggia ogni
  pixel UNA volta) e/o disegno **fronte→retro**. Skirt + LOD + draw non ordinati = ombreggi pixel nascosti.

**Fix 3 — Materiale + transizioni (grafica del suolo).**
- **Materiale PBR** (albedo/normale/roughness/AO) — **progettato portabile** (vedi HDRP sotto).
- **Texture tileabili TRIPLANARI** mescolate per **pendenza/quota/curvatura** (roccia sui bordi cratere, sedimento
  nei fondi, neve in quota). ND: il triplanare **NON è gratis** → a budget (2-3 strati, proiezione "biased" dove
  la pendenza è bassa, triplanare pieno solo sui versanti, blend **mip-corretto** o brilla). iQ: variazione macro
  a bassa freq per rompere la ripetizione delle texture.
- **GEOMORPH** (Strugar: non opzionale, toglie il "pop") — **morfa anche la normale** (o ricalcolala dalla griglia
  morfata) o lo shading si desincronizza dalla geometria durante la transizione.
- **TAA** (ND: quasi obbligatorio a questo livello — abilita shading ricco e ammazza lo sfarfallio specular).

## 3. Le 2 STRATEGICHE emerse dal tavolo (più grandi del materiale del suolo)

**A. Pipeline: Built-in → HDRP è il SOFFITTO del look.** Atmosfera physically-based, nuvole volumetriche, TAA,
esposizione/HDR vivono in HDRP (fai-da-te doloroso in Built-in). Per il livello SC/Elite serve HDRP.
→ progetta il materiale PBR ORA (portabile), fai i fix di perf (agnostici alla pipeline), migra in modo
DELIBERATO. L'indirect-draw gira sotto SRP via custom pass.

**B. Atmosfera + illuminazione/HDR = maggior ritorno visivo** (ingegnere SC). Cielo, scattering, prospettiva aerea,
sole HDR fanno più "wow" del materiale perfetto a terra. È il vero salto estetico.

## 4. ORDINE OPERATIVO
1. Fill economici (normali-da-griglia + batch 1 dispatch + pool Node) → toglie stutter/"caricamento".
2. Per-vertice i campi a bassa freq + depth pre-pass/overdraw → abbatte il baseline GPU.
3. Materiale PBR + triplanare a budget + geomorph + TAA → look del suolo.
4. (scope a parte) HDRP, poi atmosfera + volumetriche → il salto estetico AAA.

I log `[GpuPlanet …] CPU … ms · fills=…` decidono solo se partire da 1 (CPU alta) o 2 (GPU). Entrambi vanno fatti.

## 5. ARCHETIPI DI CORPO — heightfield NON è l'unico (caves, Brittle Hollow, interni, distruttibili)

L'heightfield (UNA quota per direzione radiale) **non può** rappresentare: grotte/strapiombi, camminare sull'INTERNO
di un guscio, pianeti cavi con buco nero al centro, distruzione progressiva (Brittle Hollow). Se questi sono in
scopo, NON invalidano il renderer heightfield: vanno trattati come **ARCHETIPI separati**. Principio guida:

> **Disaccoppia il "corpo celeste" dal renderer.** Un corpo ha: una *rappresentazione di superficie*, un *campo di
> gravità*, una *query di collisione*, *contenuti* — tutti **pluggabili**.

**TRE archetipi di renderer** (la stessa astrazione corpo li ospita tutti):
1. **Rocciosi walkable = heightfield CDLOD GPU** (questo lavoro). La maggioranza.
2. **Giganti gassosi / stelle = corpo a STRATI CONCENTRICI** (non un solo guscio): es. gigante gassoso = gas/nuvole
   volumetrico (ci **voli dentro**) → **oceano LIQUIDO nuotabile** (volume + isole/tornado, tipo Profondo Gigante) →
   **nucleo roccioso / ferro fuso** (heightfield o mesh, calpestabile/collisione). Quindi un corpo è uno **stack di
   strati** `{mezzo (vuoto|gas|liquido|solido), renderer, modalità di movimento (vola/nuota/cammina), gravità}`. La
   gravità è radiale ma il MEZZO cambia con la profondità → il walker chiede "in che mezzo sono a questo raggio?" e
   commuta modalità. Gli strati **compongono** gli archetipi: il nucleo riusa il renderer heightfield (1). Recuperato
   (CLAUDE.md "secondo renderer volumetrico" + scala: gas 1.5-3 km nuotabili, stelle 3-5 km).
3. **Esotici (Brittle Hollow & co.) = bespoke** (mesh autorata o SDF/voxel) + gravità custom; pochi, tech diversa.

Gli esotici **meritano una loro sessione del tavolo** (è un problema diverso: geometria non-heightfield + gravità
non-radiale + dinamica/distruttibile). Da fare quando li si affronta; intanto teniamo l'astrazione pluggable per
non chiuderci in un angolo heightfield-only.

## 6. SCALA MULTI-CORPO — "aggiungere/togliere quanti corpi voglio"

Due cose distinte (NON confonderle):
- **(a) Sistema multi-corpo UNIFICATO e data-driven** (un solo pool condiviso, 1 draw indirect per tutti, corpi =
  lista di dati): dà "aggiungi quanti corpi vuoi" in modo efficiente (memoria + draw + CPU). **NON è throwaway** →
  vale la pena farlo presto se "tanti corpi" è un obiettivo. Oggi: 1 renderer/pool/ComputeShader PER corpo =
  spreco (memoria + draw multipli).
- **(b) LOD/culling GPU-driven** (la selezione gira in compute, la CPU non traversa più): l'ottimizzazione PROFONDA,
  forzata solo quando la traversata CPU di MOLTI corpi/alberi enormi diventa il collo. **Gated dall'ambizione di
  conteggio corpi:** sistema disegnato alla Outer Wilds (~decine) → forse mai necessario; galassie procedurali alla
  NMS → necessario, e allora non è prematuro.

Decisione: fare **(a)** quando si vuole libertà di aggiungere molti corpi (non throwaway); committare **(b)** solo
quando lo scale lo impone (misura) o quando si sa con certezza che l'ambizione è NMS-scale.

**DECISIONE (4 giu): endgame = scala-galassia (NMS-class).** Vale la pena puntarci PER IL RENDERER/MANAGEMENT/
COORDINATE, perché **NON preclude** qualità per-corpo né corpi bespoke/esotici (la qualità del singolo corpo è
indipendente da quanti corpi esistono). L'**UNICA cosa che precluderebbe la ricchezza** è impegnarsi su "tutto
generato da seed, nessuna via di authoring" → da NON fare. Tieni il **contenuto DUALE**: corpo = *descrittore*, e
il descrittore può essere **generato** (seed→ricetta) **o autorato a mano** o **bespoke-esotico** (NMS e SC sono
entrambi ibridi). Fisica: **attivazione per-sistema** (solo il sistema corrente simula le orbite; gli altri
dormienti finché non li visiti) → scala-galassia a costo basso. Coordinate **gerarchiche** (galassia→sistema→corpo)
+ streaming; la floating origin copre il dentro-sistema. **Progetta le astrazioni ORA (leggere, data-driven),
implementa lo scaling DOPO il minimo-giocabile** — non throwaway, non rallenta il "playable".

## 8. CORPI ESOTICI (Brittle Hollow & co.) — prima discussione al tavolo (4 giu, BASE da rivedere)

Sfide: grotte/strapiombi, camminare sull'INTERNO di un guscio, cristalli gravitazionali, buco nero al centro,
distruzione progressiva. L'heightfield non li copre → **archetipo separato**. Verdetto del tavolo:

- **Per i POCHI bespoke (un Brittle Hollow, set-piece narrativi): AUTÓRALI** (Carmack/ND/SC). Mesh modulare +
  mesh collider + **chunk pre-fratturati** (rigidbody) che si staccano su trigger/timer per la distruzione +
  eventi **scriptati** (la distruzione si coreografa, non si simula in generale) + shader bespoke (distorsione del
  buco nero). NON costruire un motore voxel/distruttibile generale per un one-off.
- **Per esotici PROCEDURALI/diffusi (grotte ovunque, molti distruttibili): SDF** (iQ/Aaltonen). Un campo di
  distanza dà **rendering** (raymarch lontano / mesh-via-dual-contouring vicino) **E collisione** (distanza +
  gradiente=normale) in modo uniforme, e compone in CSG (guscio = sfera − sfera; tunnel = sottrai). Sistema grosso:
  investilo solo se gli esotici sono pervasivi.
- **DA METTERE ORA (renderer-independent, economico, non throwaway):**
  1. **Campo di GRAVITÀ astratto** — il corpo ritorna il vettore gravità in un punto. Radiale = un'implementazione;
     cristalli = volumi locali; interno-guscio = verso il centro (cadi nel core/buco nero se non sei su una
     superficie); multi-sorgente. Il walker chiede QUESTO, non assume radiale.
  2. **Query di SUPERFICIE/collisione astratta** — "superficie più vicina + normale". Heightfield `SampleHeight` =
     un'impl; mesh collider e SDF (distanza+gradiente) per gli esotici.
  3. **Corpo = DESCRITTORE** con {renderer, collider, gravità, contenuti} pluggabili.
- **Buco nero/interno:** regione con gravità propria + trigger di teletrasporto/transizione + shader di distorsione.
- Le IMPLEMENTAZIONI esotiche (mesh autorata / SDF) si fanno quando si costruisce il primo corpo esotico — **sessione
  del tavolo dedicata e più profonda allora.** Questo NON blocca né cambia il lavoro heightfield di adesso.

## 9. Punti che avevo DIMENTICATO di portare al tavolo (recuperati 4 giu)

- **Eclissi / ombre analitiche fra corpi** (sistema esistente, spazio-oggetto): vanno **portate nel nuovo materiale
  + HDRP**. Il tavolo aveva discusso atmosfera/luce ma non l'eclissi.
- **Lezioni dure sulle TEXTURE/dettaglio** (vincolano il piano triplanare-PBR): struttura **multi-scala**, NON grana
  uniforme; la sabbia/liscio = **forma+luce**, non texture ad alta frequenza; **alta frequenza nelle normali solo
  via mipmap** o sfarfalla. Scegliere texture STRUTTURATE, non sovra-texturizzare le superfici lisce.
- **Accoppiamento con l'EDITOR**: l'anteprima editor condivide lo shader di superficie (`PlanetProcedural` +
  include `PlanetShade`). Il passaggio a PBR deve **tenere l'editor in sync** (l'include condiviso lo fa, ma la
  catena PBR va resa comune a editor+gioco).
- **Proxy della mappa** (rappresentazione a bassa res dei corpi nella vista mappa): serve un posto nel nuovo
  sistema (oggi `SingleMeshPlanet` con materiali bakeati) + il fix "la superficie GPU si disegna in TUTTE le
  camere → filtrare per camera/mappa".
- **Lezione "niente shadow MAP"**: l'auto-ombra del terreno sotto HDRP non deve reintrodurre l'acne a luce radente
  → decidere (contact shadows / analitico / con cura).

## 10. PIANO DI LAVORO (workflow validato 4 giu)

1. **Fase 1 — Performance → MINIMO GIOCABILE.** Fix 1 (fill economici) + Fix 2 (per-vertice + overdraw): superficie
   liscia, camminabile, fluida. Si rimanda il look.
2. **Fase 2 — PAUSA del piano → fondamenta multi-corpo.** Sistema multi-corpo **unificato e data-driven** (un pool,
   1 draw) + le **astrazioni** {descrittore di corpo, campo gravità, query superficie} (che servono ANCHE agli
   esotici) + **LOD/culling GPU-driven**. → "aggiungo/tolgo quanti corpi voglio". Time-boxata (non far esplodere).
3. **Fase 3 — Riprendi il piano (grafica).** Materiale PBR + triplanare a budget + geomorph + TAA. Poi, scope a
   parte, **HDRP → atmosfera + volumetriche + HDR** = il salto estetico AAA.

Convalidato dal tavolo: prima provi UN pianeta che funziona (fase 1), POI scali la tecnica (fase 2) — l'ordine giusto.

## 11. CONTENUTO = DATI (descrittore). Generazione + authoring, stesso formato

Decisione di Dario (4 giu): **non** tutto-procedurale-da-seed. Si vuole **autorare qualunque cosa**, ma poter
**generare le BASI** da seed (lavora da solo → più spazio di manovra, potenza, bellezza generabile senza limiti).
Visione a lungo termine: nel corso degli anni, **costruendo mondi (suoi o di ALTRI), l'universo può diventare
davvero vasto** (→ hint a modding / contenuti condivisi / persistenza).

Implicazione architetturale: **un corpo/mondo è un DESCRITTORE serializzabile e componibile.** Due percorsi che
producono lo **stesso dato**: (a) **generazione** (seed → descrittore di base) e (b) **authoring** (editor che lo
modifica/crea). L'esotico/bespoke è solo un descrittore con renderer dedicato. **L'engine carica descrittori, non
hardcoda MAI i mondi** (oggi `SolarSystemSetup` è una lista hardcoded → da rendere data-driven in Fase 2).
North star: formato dato pulito + versionato + condivisibile → modding/persistenza → universo che cresce nel tempo.

## 12. PRINCIPIO DI METODO: l'ANALISI è l'autorità — niente sunk-cost

Direttiva esplicita di Dario (4 giu): **l'approccio lo decide l'analisi** (che dev'essere concreta, realistica,
fatta alla perfezione e ri-vagliata quando serve). **Le scelte/codice fatti finora NON vincolano**: sono *input*
(lezioni, feature esistenti), non vincoli sulla conclusione. **Ciò che è incompatibile con le decisioni
dell'analisi si CAMBIA o si RISCRIVE — anche codice scritto da Claude.** Questo documento è la fonte di verità; il
codice si conforma al documento, non viceversa.

Cosa l'analisi GIÀ implica di cambiare (non sacro):
- `SolarSystemSetup` lista hardcoded → **data-driven** (descrittori). [Fase 2]
- 1 `GpuPlanetRenderer`+pool+ComputeShader PER corpo → **sistema unificato** (un pool, 1 draw). [Fase 2]
- Gravità radiale assunta nel walker → **campo di gravità astratto**. [Fase 2]
- `CelestialBody`/`PlanetTerrain` accoppiati al solo heightfield → **archetipo pluggabile** (surface/gravità/layer). [Fase 2]
- Colore procedurale per-pixel → **materiale PBR**. [Fase 3]
- Bake / `FaceMaterials` / proxy mappa → ripensare (no-bake; rappresentazione mappa). [Fase 3]
- Pipeline Built-in → **HDRP** (per atmosfera/volumetriche/TAA/HDR). [strategico]
- Eclissi/ombre, texture di dettaglio, "niente shadow map": **input/lezioni**, da riconfermare o rifare sotto la
  pipeline/architettura scelta — non si tengono per sunk-cost.

## 13. RISCONTRI DALLA SESSIONE B1 (4 giu) — strategia CONFERMATA + raffinata

B1 (resa GPU in gioco) è stata costruita e gira: quadtree CDLOD su GPU, pool + 1 draw indirect, colore
procedurale nel fragment, LOD view-dependent, walker analitico, parità. **Le OSSA (sez. 1) sono validate IN
ESECUZIONE**, non più solo a ragionamento. L'ordine perf→grafica e le strategiche (HDRP/atmosfera) restano.
Cinque raffinamenti emersi dai dati reali:

**(R1) Il "batch dei fill in 1 dispatch" (Fix 1) NON è un win pulito.**
- Ha introdotto **corruzione di geometria** (spuntoni/lamine: vertici a posizioni sbagliate) — bug sottile
  (indicizzazione o hazard compute→graphics) NON ancora trovato → **annullato**, tornati ai fill per-nodo (corretti).
- Il suo valore CPU è **parziale**: il "CPU 5-9 ms" misurato include l'**attesa-GPU** (vedi R5). Batchare le
  chiamate API aiuta solo la quota CPU VERA.
- **Verdetto:** resta la direzione giusta per il costo CPU reale, ma si ri-fa **con un banco di verifica**
  (confronto vertici batch↔per-nodo su pochi nodi → inchioda il bug), e si prioritizza solo dopo aver confermato
  che è la CPU (non l'attesa-GPU) il collo durante il lag. **Tagli SICURI già applicati** (geometria invariata):
  **property-ID cachati** (niente hash-stringa per chiamata), **normali a differenza-in-avanti, 2 campioni invece
  di 4** (fill GPU ~dimezzato), costanti (`_NN`/`_NSkirtStart`) settate una volta. Nota: il Fix-1 ideale era
  "normali dai vicini di griglia, 0 campioni extra" — quello vero (0-extra) richiede la **griglia padded**
  (groupshared o due passate); il 2-campioni è il compromesso sicuro intanto.

**(R2) Il TEMPO DI COMPILAZIONE del compute — è una PRIORITÀ DI SOLO SVILUPPO, non di gioco.**
**DECISIONE DI DARIO (4 giu, a documento su sua richiesta):** il **tempo di caricamento iniziale del gioco NON è un
problema** per il giocatore finale — un'attesa all'avvio è accettabile. **L'UNICO requisito reale è: una volta IN
GIOCO, nessun caricamento lungo** (niente stalli mentre giochi — è il churn del LOD/fill di R4 che conta). Quindi:
- Il load lento (la rotella all'avvio) pesa **solo sulla NOSTRA iterazione** (rilanciare mille volte in debug), non
  sul prodotto. Ottimizzarlo è comodo per sviluppare, **non è un obiettivo del gioco**. Non spendere troppo lì.
- In build gli shader sono **precompilati** → il problema non esiste affatto a runtime.

**Cosa è il load (diagnosi):** ~22 s → la **creazione della pipeline Metal del compute** (la `SampleHeight` enorme),
NON il bake/alloc. **Cosa ha aiutato:** lo **SPLIT del compute** (gioco = 2 kernel, editor/baker = 5, core HLSL
condiviso → parità intatta) → ~22→15 s. **Cosa ha FALLITO:** mettere **`[loop]`** sul ciclo crateri 5×5×5 per evitare
lo srotolamento → ha **PEGGIORATO** (15→50 s + rotella di 25 s allo stop): contro-intuitivo ma l'unroll con limiti
letterali `-2..2` è **più veloce da compilare** (il compilatore piega/elimina ogni iterazione coi `dz/dy/dx` noti;
il loop dinamico lo costringe ad allocare registri su tutto il corpo). **`[loop]` ANNULLATO.** Lezione: misura il
compile, non assumere che "meno codice = compila prima".

**Implicazione per l'ambizione "tante ricette/processi":** la `SampleHeight` che cresce allunga la compilazione —
ma essendo solo costo di sviluppo/avvio, è un fastidio gestibile (split, batch degli edit al compute), non un muro.

**(R3) NUOVO: il CONTEGGIO NODI va a BUDGET (Strugar).** `visibili` arrivava a 1023 = il **tetto del pool**: il LOD
ne voleva più di quanti il pool ne avesse. Il conteggio guida SIA il **disegno GPU** SIA il **churn dei fill** → è
una manopola di perf di **prima classe** (`lodFactor`, `maxDepth`, futuro 2:1), non un valore emergente da subire.
`lodFactor` 4→3 ha portato ~1023→~700. NON tocca il dettaglio sotto i piedi (i nodi vicinissimi vanno comunque a
profondità massima), solo quanto lontano si estende.

**(R4) Il lag è il CHURN del LOD in CAMBIO-QUOTA** (avvicinamento/allontanamento/radente), non fermo/crociera (lì
60 fps). È il costo dei fill (CPU+GPU) mentre il LOD genera dettaglio. Fix 1 (fill economici) + budget nodi (R3) +
Fix 2 (fragment) lo aggrediscono: la strategia è giusta, cambia l'**enfasi** (il bersaglio preciso è quel churn).

**(R5) CAUTELA DI MISURA (metodo, da non ripetere).** La traccia CPU rossa nel Profiler e il "CPU ms" includono
l'**attesa-GPU** quando sei GPU-bound (il `SetData`/present si blocca → conta come CPU; CPU Main 70-97% = fermo
sulla GPU). La verità per il GPU è **GPU Frametime** (Stats) + i log self-profiler. Letta come "CPU sovraccarica"
ci ha sviati più volte. **Prima di ottimizzare la CPU, conferma con GPU Frametime che non è attesa-GPU.**

## 7. Alternative valutate e SCARTATE (con motivo)
- **Virtual-texture/clipmap + tessellation** (geometria): potente, ma la tessellation su Metal è già stata provata
  e tolta (guadagno marginale, aliasing), e reintroduce il problema parità-heightmap-vs-walker già rifiutato.
- **Voxel/marching globale (NMS)**: serve solo per strapiombi/grotte → riservato agli archetipi esotici, non al
  pianeta heightfield standard.
- **Bake heightmap + sample**: rimette il muro del texel e la divergenza mesh/walker. Scartato.

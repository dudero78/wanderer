# Wanderer — guida per Claude

Gioco spaziale seamless "alla Outer Wilds" che punta verso No Man's Sky. Progetto
nel tempo libero di Dario. **Claude scrive tutto il codice**; Dario fa il minimo
nell'editor. Per questo si usa Unity (tutto autorabile da testo) e non UE5.

## Principi

- **Robustezza prima dell'eleganza.** Dario non fa debug profondo: il codice deve
  essere a prova di errore, non furbo.
- **Spiega il *perché*** delle scelte, nei commenti e nelle risposte.
- Comunicazione: dritti al punto, niente note difensive, ammetti gli errori subito.
- Debug per **screenshot**: Dario manda immagini, Claude diagnostica.
- Italiano.

## Stato attuale (vedi git log per il dettaglio)

Funziona: floating origin + doppia precisione, orbita Kepleriana, **gravità radiale**,
**volo col jetpack** (tuta da raccogliere), **torcia** (F), ciclo giorno/notte.

**Renderer dei corpi rocciosi: QUADTREE CDLOD** (`PlanetQuadtree`, cube-sphere a 6 facce-radice).
Geometria view-dependent: fitta sotto i piedi (crateri nitidi calpestabili, look Elite/Star Citizen),
rada all'orizzonte. Build async su thread + **geomorph** (UV2, niente pop) + **skirt** + horizon culling +
cache LRU dei nodi + LOD predittivo. Toggle `useQuadtree` su `GameBootstrap` (default ON); `SingleMeshPlanet`
(res fissa, niente LOD) resta come fallback. Walker/gravità/collisione NON dipendono dal renderer: leggono
`PlanetTerrain.SampleHeight` (una sola verità; il morph è puramente visivo e vale 0 da vicino, dove mesh e
collisione combaciano). **Limite noto, ACCANTONATO:** ai confini fra livelli di LOD restano transizioni di
shading ("scalini") — niente fessure/buchi (lo skirt è dimensionato sul salto di morph del bordo, copre per
costruzione). Il fix definitivo è il **CDLOD bilanciato 2:1** — deciso ma RIMANDATO (vedi "Lezioni dure").

**Crateri: geometria vera nell'heightfield** (`CraterTerrainLayer`: composizione additiva, griglia 3D hashata
seam-free, profilo a **legge di potenza** con bordo netto regolabile `rimSharpness` 1=cono…4=quasi tagliente)
+ normale bakeata (`CraterNormalBake`) per i bordi fini, filtrata dal mipmap. Col quadtree i crateri
grandi/medi sono GEOMETRIA, i fini li dà la normale.

**Editor di pianeti = GENERATORE RICCO** (scena separata, menu "Wanderer → Apri editor pianeti",
`PlanetEditor`/`PlanetEditorBootstrap`). La RICETTA (`PlanetRecipe`) NON è più `crateri[] + colore`: è una **lista
ORDINATA di PROCESSI tipizzati** (`ProcessStep`/`ProcessType`), e **l'ordine è la sequenza geologica → cambia il
risultato** (un cratere DOPO un mare scava una buca asciutta nell'acqua). Tipi: **Crateri** (rimescola/casuale,
quote per taglia, "distribuzione" = ruota il campo e li fa scorrere), **Mari GEOMETRICI** (allagamento solido
walkable: livello/saturazione/rilievo-fondale con "forma" creste↔liscio↔gobbe; lo shader ricostruisce il pelo via
`n3_fbm` per tingere seguendo la geometria), **Tettonica** (placche **soft Voronoi** → quota CONTINUA, continenti/
oceani + catene/rift ai confini, coste frastagliate; col Mare = look terrestre). UI a fisarmonica + tooltip,
riordino Su/Giù, "+ Nuova pipeline" sceglie il tipo. Texture suolo + saturazione. **Anteprima ASINCRONA su thread**
(`SingleMeshPlanet.RebuildAsync`: slider fluidi, bassa res nel drag → full res al rilascio). **Bake dal pulsante**
(hook iniettato dall'assembly Editor); **"Carica" = file picker**. Salva in `persistentDataPath/planets`; le ricette
"ufficiali" in `Assets/Resources/Planets/<nome>.json` (→ build). `ScaledTo(raggio)` scala le misure assolute.
**GPU PER L'EDITOR — TAPPE 1-3 FATTE (anteprima GPU completa):** l'anteprima editor gira sulla GPU (toggle **G**),
geometria+normali calcolate in `PlanetHeight.compute` e disegnate **dai buffer senza readback** (`GpuPlanetSurface`
+ shader `Wanderer/PlanetProcedural`, `Graphics.RenderPrimitivesIndexed`). Full-res LIVE (512), rigenera a ogni edit.
**Pipeline ORDINATA** (`GpuShapeBuffers`: buffer ordinato di processi + buffer per-tipo): crateri (pesi per taglia +
"Distribuzione" come **drift dei centri**), **mari**, **tettonica** (placche generate in C# e caricate) — a parità
sub-mm col walker. **COLORE calcolato nel fragment dalla ricetta** (NON texture bakate; vedi [[wanderer-rendering-roadmap]]):
suolo/macro/minerali/vette/bacini/mare; maria/vette seguono la quota di BASE (non i crateri). **Normali ANALITICHE**.
Cuciture agli spigoli del cubo chiuse facendo **sovrapporre le facce di una cella** (lo snap a lattice terrazzava i
versanti dei crateri → rimosso). **PROSSIMO:** resa GPU IN GIOCO (B1) · materiali per pendenza/quota + triplanare + PBR
(look SC/ED) · il GIOCO (teletrasporto, VERBO). Vedi `TODO.md`, [[wanderer-rendering-roadmap]], [[wanderer-terreno-strategia]].

**Rifiniture editor (sessione 3 giu):**
- **Modo luce, tasto `L`** (`EditorLightMode`): **ancorata** (default — sole fisso, il pianeta non gira → orbiti
  ma resta illuminata la stessa faccia) / **libera** (il sole resta agganciato alla vista, da destra e dall'alto,
  ~1/8 in ombra → orbitare è come ruotare il pianeta sotto il sole: ispezioni ogni faccia illuminata). Non tocca i
  controlli: cambia solo se `_SunDir` è ancorato al mondo o al frame della camera. Vale per mesh CPU (ruota la luce
  vera) e anteprima GPU (`RefreshLighting`).
- **Mare LIQUIDO** (flag `liquid` su `ProcessStep`, toggle nella sezione Mare): resa come acqua — riflesso speculare
  del sole + schiarita di Fresnel ai bordi (solo lato illuminato), in entrambi gli shader. La **larghezza del glint
  è legata alla rugosità del mare** (liscio = punto netto da specchio; mosso = scia larga). Solo aspetto: la
  geometria resta il pelo piatto, il nuoto sarà gameplay.
- **Mare TRASPARENTE** (flag `seaClear` + `seaClarity`, sotto "Liquido"): l'acqua limpida lascia vedere il **fondale
  sommerso**, che sbiadisce verso il colore profondo con la profondità (Beer-Lambert `exp(−depth/seaClarity)`).
  La **profondità dell'acqua** (pelo − fondo) non è geometria — la superficie disegnata È il pelo — quindi il
  compute la emette **per-vertice** (`_VDepth`, calcolata in `SampleHeightD` al momento dell'allagamento) e il
  fragment la interpola e la usa. **Solo sul path GPU** (l'anteprima vera, tasto G): il path CPU/in gioco
  (`PlanetBaked`) non ha la profondità per-vertice e resta opaco. `seaClarity` = profondità a cui l'acqua diventa
  ~opaca (torbida↔cristallina). **Rilievo del fondale:** il fondo visto in trasparenza è illuminato dalla
  **normale del FONDO sommerso** (`_VBedNrm`, normale analitica di `BedHeight` = pipeline senza allagamento),
  pesata da `seaTrans·seaMask` → l'acqua bassa mostra il rilievo del fondale; profonda/terra torna alla normale
  del pelo. Il glint resta sul pelo (il riflesso è sulla superficie).
- **Dettaglio anteprima GPU** (toolbar 512/1024/2048 + **Auto**): la risoluzione della mesh GPU. Default **512**
  fisso (niente scatti durante l'editing). **Auto** = opt-in (lo attiva chi zooma sui dettagli senza editare): segue
  lo zoom con ISTERESI (soglie relative al raggio) — vicino 2048, lontano 512. L'**index buffer è generato sulla
  GPU** (kernel `CSIndices`, dispatch 2D in `uint`, buffer `Index|Structured`) per non allocare/caricare ~600 MB sul
  main thread; cache per livello (`GpuPlanetSurface`). Lo scatto residuo del 2048 (allocazione VRAM) si paga solo
  scegliendolo. `SetResolution` rialloca i buffer a runtime.
- **Leggibilità del pannello (UX)**: non più un "muro di manopole". Ogni zona ha un **colore-firma** (Forma=ardesia,
  Colore=sabbia, Crateri=ambra, Mare=azzurro, Tettonica=verde): header a barra colorata + **icona** procedurale +
  **velo tenue** dello stesso colore dietro le righe, con **zebra** (due intensità) per seguire la riga in
  orizzontale. I pulsanti "Che tipo?" portano colore+icona del tipo. I **PROCESSI** sono una REGIONE distinta
  (divisoria + titolo marcato con icona "stack" + sottotitolo "l'ordine conta"), separata dalla base e dai comandi
  file. Tutto in `PlanetEditor` (stili IMGUI + texture procedurali). **Trappola IMGUI:** `GUI.backgroundColor` con
  alpha bassa per lo sfondo-riga tinge ANCHE i figli (maniglia slider, casella toggle) → spariscono; va ripristinato
  SUBITO dopo `BeginHorizontal(rowStyle)` (lo sfondo è già disegnato, i figli no).

**Tre corpi**: il **Pianeta** (lunare, raggio 500), **Cetra** (luna marziana craterizzata, raggio 300, g 3.0,
in orbita attorno al pianeta — ricetta creata nell'editor) e **Luna** (creato nell'editor, raggio 800, in orbita
al SOLE; ricetta `Resources/Planets/Luna.json`). Aggiungere un corpo roccioso = una `CelestialBody`
+ `PlanetTerrain` (ApplyRecipe) sullo stesso GameObject; walker/mappa/viaggio "gratis".

**Superficie — base lunare liscia.** Colore quasi uniforme grigio (`_SoilMean`) + variazione
macro a bassa frequenza; il bello lo fanno la FORMA (crateri + colline) e la LUCE. Dettaglio
WORLD-FIXED + mipmap → niente moiré/scivolamento. **Lezione dura, da ricordare:** la base NON
deve competere coi crateri — ampiezza base BASSA, struttura quasi tutta dai crateri (come
Phobos/Luna). Identità pianeta = 2 colori (`_SoilMean`/`_SoilTint`) + manopole crateri/terreno.

**Volo a due modelli, toggle con `N`** (`PlanetWalker`):
- *Crociera* (default tuta): la potenza dei motori cresce con la quota e con quanto
  tieni la spinta (rampa `boost01`), così resti maneggevole vicino al suolo
  (atterraggio intatto) e veloce in alto. Comandi sugli assi tangenti del pianeta.
  Smorzamento **anisotropo**: frena moto orizzontale e salita ma NON la caduta, così
  la gravità si sente (precipiti accelerando). Conseguenza: il jetpack non galleggia
  da solo, per tenere quota dai un filo di Space.
- *Newtoniano*: nessun attrito, la spinta si somma (delta-v reale, alla Outer
  Wilds). Comandi **relativi allo sguardo** (puntare e andare). In **volo libero**
  (Newtoniano staccato dal suolo) l'orientamento NON si aggancia alla gravità: ruoti
  solo col mouse — altrimenti un pianeta che orbita ti ruoterebbe la vista e il
  bersaglio "scivolerebbe" via. Spinta **scalata alla gravità locale**
  (`max(newtonThrust, 1.6·g)`) → decolli da QUALUNQUE corpo, anche la stella (g=100):
  invariante "ciò su cui atterri, lo puoi lasciare". Sarà il default dell'astronave.
  **Match velocity** (`X`): TIENE a zero la velocità relativa al **corpo ancorato** — finché premi, annulla
  lo slancio E contrasta la gravità (in proporzione allo spool del freno) → resti FERMO rispetto al corpo
  (hover vicino a un pianeta, sincronizzato con la destinazione in viaggio). Non è "frena e cadi": per
  scendere/atterrare **rilasci X** (la gravità ti riprende) o usi Shift. In spazio profondo (g≈0) = puro freno.
  Decelerazione a **tre fasce**: ALTA velocità → proporzionale (`sp/brakeTimeConstant`, frena molto più forte
  del picco da migliaia di m/s); fascia media → picco costante (`brakeAccel`); CODA sotto `brakeKnee` →
  `sp/brakeEaseTau` + `brakeFloor` (governa gli ultimi numeri 3·2·1: floor alto = scorrono svelti). Stessi
  parametri tarabili in Impostazioni (tab Volo).

HUD volo: **altitudine** sul corpo di gravità più vicino + **distanza** sul corpo
selezionato (separate); velocità, **radiale con segno** (− = ti avvicini),
**tangenziale**, modello attivo, stato `FRENO` e **torcia**.

## Viaggio fra corpi (sistema di riferimento)

Scala compressa = il sistema sta in float (a 60 km la precisione è ottima). Per viaggiare
alla Outer Wilds l'origine si **ancora a un corpo di riferimento** (`SolarSystem.Reference`),
che resta FERMO in scena:
- nella **zona locale** di un corpo (quota sotto la soglia di decollo ~`raggio·0.5`, con
  isteresi) ancori a lui → camminata e atterraggio stabili;
- in **volo con una destinazione** selezionata ancori alla **destinazione** → è ferma e
  raggiungibile (non sfugge mentre orbita).

Allo switch di riferimento si **preserva la velocità-universo** del giocatore (correzione =
differenza di velocità dei due corpi × `TimeScale`, via `CelestialBody.UniverseVelocityAt`):
cambiare ancora NON altera il moto reale. Conseguenza voluta: appena decolli mantieni lo
slancio orbitale e la destinazione "scorre"; è il **freno X (match velocity)** a sincronizzarti,
poi punti e vai. **`TimeScale = 1`** in gioco (3 era l'acceleratore di debug: gonfiava le
velocità orbitali e rendeva il match-velocity ingiocabile).

## Mappa e navigazione

- **Mappa (`M`)**: zoom-out sul sistema con le orbite; clicca un corpo per **selezionarlo**
  come destinazione (`MapMode`, camera dedicata, comandi del walker congelati).
  - **Corpi reali** (non più dischi piatti): ogni corpo con ricetta è un **proxy** `SingleMeshPlanet` a bassa res
    (mesh craterizzata + `PlanetTerrain.FaceMaterials`, gli stessi materiali bakeati del corpo) illuminato dal
    sole → si vede il terminatore, legge come pianeta visto dall'orbita. La stella resta disco emissivo. Il
    marker-sfera diventa un **bersaglio di click INVISIBILE** (renderer spento, collider attivo) → la selezione
    funziona come prima. I proxy sono scalati a dimensione-schermo costante ogni frame.
  - **"TU SEI QUI"**: marker verde + etichetta alla posizione del giocatore. L'etichetta è **sollevata** lungo
    l'alto-schermo del raggio apparente del corpo su cui sei (`GravityBody`) → galleggia sopra il pianeta, non lo
    attraversa.
  - **Scia della traiettoria**: filo verde a coda di cometa (brilla al capo recente, sfuma sul vecchio). Registrata
    SEMPRE (anche fuori mappa) in **coordinate-universo** (`FloatingOrigin.SceneOrigin + posScena`) e riconvertita
    a scena ogni frame → stabile con la floating origin, coerente con stella e orbite. Ring buffer (1024 punti,
    passo ~42 m → ultimi ~43 km). **Trappola chiusa:** al ri-ancoraggio (cambio di `Reference`) la posizione-scena
    "salta" verso la stella per un frame; la pos-universo NON cambia (è solo un cambio di coordinate), quindi
    qualunque salto enorme fra due frame è un artefatto → si scarta (`trailMaxJump`).
- **Indicatore di rotta** (`RouteIndicator`): reticolo HUD sul corpo selezionato — anello a parentesi
  + chevron + distanza + **velocità di avvicinamento COL SEGNO** (− = ti allontani). **Due marker**:
  prograde (⊕ pieno) e retrograde (cerchietto vuoto), col tratteggio di collegamento; l'offset è la
  **velocità LATERALE** (perpendicolare alla rotta) × pixel/(m/s), NON la direzione cruda — vicino a 0
  resta al centro, niente sbando. **ALLINEATO** (verde) quando deriva laterale ~0 e ti avvicini;
  **SINCRONIZZATO** (verde) quando la velocità relativa ~0. Freccia al bordo se fuori vista, si
  dissolve quando il corpo riempie lo schermo. Tutto scalato con la risoluzione. Texture procedurali una
  volta all'avvio. Drift residuo dopo il match = FISICA (gravità), si trimma a mano (→ autopilota #12).
  I numeri (distanza/velocità) stanno appena FUORI dall'anello e vengono clampati al bordo schermo SOLO
  quando l'anello è enorme (vicino) → finché c'è spazio restano fuori dal reticolo, non si appiccicano al
  centro troppo presto. **Compare anche in MAPPA** sul corpo selezionato (usa la camera attiva via
  `MapMode.ViewCamera`): anello + chevron + NOME del corpo → si vede subito quale è selezionato.
  Le texture del reticolo sono generate **con mipmap + trilinear** (`Make(..., mip:true, ss:4)`) → linea
  nitida e pulita a ogni distanza, niente granulosità da lontano (era una texture senza mip che aliasava).
  **Gauge di frenata** (in basso al centro, solo in volo libero MANUALE newtoniano): barra verso la tacca
  "ORA". Distanza necessaria calcolata ONESTAMENTE dai valori in gioco (non va più ritoccata): `d_react`
  (continui ad avvicinarti mentre reagisci + lo spool del freno: `closing·(brakeRampTime + ReactionTime)`)
  `+ d_brake` (`closing²/(2·aEff)`, con `aEff = brakeAccel − g_superficie`, perché la gravità erode la frenata).
  `u = d_required / distanza-dalla-superficie`: ambra "FRENA" vicino a 1, ROSSA "TROPPO VELOCE" oltre. Arriva
  PRIMA dell'ultimo istante grazie al margine di reazione. Disegnata anche quando il reticolo svanisce. Sotto
  autopilota è nascosta (frena lui). Compare SOLO oltre `WarnMinClosing` (~50 m/s): è un avviso da viaggio
  interplanetario, non per volo radente / saltelli / manovra fine vicino al suolo (lì usi i motori, non il freno;
  e lo skim tangenziale ha closing ~0, quindi è già escluso).
- **Orbite a schermo** (`O`, `OrbitDisplay` + shader `Wanderer/OrbitLine`): mostra/nasconde le orbite del
  sistema come **fili luminosi alla Outer Wilds** anche in volo. Spessore COSTANTE in pixel (espansione in
  spazio schermo nel vertex shader: l'arco vicino e quello lontano della stessa orbita hanno lo stesso
  spessore — impossibile con la larghezza per-linea del `LineRenderer`); additivo, nucleo+alone gaussiano;
  la linea brilla dove sta il pianeta ADESSO (`_PeakU = frac(SimTime/Period)`) e sfuma a coda andando
  indietro. L'ellisse (Kepler, fissa nel frame del genitore) è una mesh-nastro costruita UNA volta; ogni
  frame solo trasla il GameObject col genitore (floating origin) + aggiorna un uniform di fase → zero alloc,
  niente solve orbitale, niente loop per-vertice. **Trappola chiusa:** la luminosità lungo l'anello va
  calcolata PER-VERTICE e interpolata (valore continuo attorno all'anello); calcolarla nel fragment da una
  coordinata interpolata accende un nodo alla cucitura (il segmento di chiusura spazza 1→0 all'indietro).

Comandi volo: `WASD` spinta · `Space`/`Shift` su/giù · `Q/E` rollio (volo libero) · `N` Crociera/Newtoniano
· `X` match-velocity · `T` autopilota · `F` torcia · `M` mappa · `O` orbite · `à` impostazioni.

**Autopilota (`T`, toggle)**: hands-off completo verso il corpo selezionato. Si inserisce solo con la tuta e
con una destinazione scelta sulla mappa; passa a Newtoniano. Orienta il muso al bersaglio, pilota la velocità
RADIALE verso/dal corpo con profilo "frena in tempo" **bidirezionale** `vWant = sign(dtg)·√(2·a·|dtg|)`:
fuori dal sorvolo si avvicina, dentro risale → il **punto di sorvolo** è un EQUILIBRIO STABILE. Componente
laterale desiderata = 0 (annulla la deriva). Il Δv si applica a `rb.linearVelocity` (identico in ogni
riferimento inerziale → indipendente dall'ancora). **NESSUN tetto di crociera**: il limite è il `√(2·a·d)`
stesso (per costruzione la velocità max da cui riesce a fermarsi), sopra c'è solo un **soffitto di sicurezza
alto** (`autoMaxSpeed` 50000, di norma non si tocca). `autoBrakeAccel` più alto → frena più forte → crociera
più veloce restando in grado di fermarsi.
- **Rampa di accelerazione** (`autoTransitTime`): parte gentile (`autoAccel` per `autoAccelGentle` secondi →
  tempo di cambiare idea se sfreccia un corpo interessante), poi sale da `autoAccel` a `autoAccelMax` in
  `autoAccelRampTime` FINCHÉ resti sullo stesso bersaglio → i viaggi lunghi (al sole) prendono velocità in
  fretta. Cambiare destinazione o disinserire azzera la rampa: la tratta seguente riparte gentile.
- **Punto di sorvolo gravity-aware**: il PIÙ ESTERNO tra `autoHoverRadii` raggi sopra la superficie e la
  distanza dove la gravità LOCALE scende a `autoHoverG` (`√(μ/autoHoverG)`). Su un corpo pesante (la stella)
  ti fermi MOLTO più in alto, dove `g` è dolce → hai tempo di manovrare prima di cadere.
- **Profilo di frenata conservativo**: la decel del profilo è `freno − g_superficie` (non il freno pieno).
  Tuffandoti verso un corpo pesante la gravità erode la frenata reale (decel netta = freno − g); col freno
  pieno freneresti troppo tardi e SFONDERESTI (era il bug sul sole). Autorità effettiva ≥ profilo ovunque.
- **Camera libera dopo l'allineamento** (`autoAligned`): l'autopilota punta il muso al target solo all'INIZIO
  (slerp); appena allineato (~3°) sblocca il mouse → guardi dove vuoi mentre lui continua a volare. La ROTTA
  NON dipende dalla vista (spinge lungo la direzione-mondo verso il target, Δv su `rb.linearVelocity`), quindi
  girarti non la cambia. Spegnere/riaccendere (T) o cambiare destinazione ri-allinea. (Stessa logica della
  tuta in newtoniano: il moto è inerziale, girarti non lo altera — cambia solo se SPINGI.)
- **Arrivo (dipende dall'impostazione `à` → "Autopilota stazionario", default OFF):** OFF = arrivi a
  distanza di sicurezza e l'autopilota DISINSERISCE (manovri tu, hai tempo perché `g` lì è dolce). ON =
  tiene la STAZIONE (`AutoHolding`, hover contro gravità) finché non dai un comando (WASD/Space/Shift/X).
Si disinserisce anche atterrando o con `N`. È la soluzione hands-off al drift residuo del newtoniano.
- **Stop dolce all'interruzione** (opzione `GameSettings.AutopilotSoftStop`, default ON): interrompendo
  l'autopilota con `T` mentre voli, la nave FRENA da sola fino a fermarsi rispetto al corpo ancorato (= la
  destinazione in viaggio) invece di restare alla deriva. Riusa il blocco freno (`SoftStopping` → `Braking`)
  ma più DECISO della X (`softStopAccel`); si annulla appena prendi il controllo (WASD/Space/Shift), con `N`,
  atterrando o ri-inserendo l'autopilota; vale solo in volo libero newtoniano. HUD: `STOP` (vs `FRENO` di X).

**Impostazioni (`à`)** (`SettingsMenu` + `GameSettings`): schermata opzioni a TAB (IMGUI), congela i comandi e
libera il cursore. È un banco di prova: gli slider editano i campi LIVE del `PlanetWalker` → effetto immediato.
Tab attuali: **Autopilota** (stazionario, crociera, accel iniziale/max, fase gentile, rampa, freno, dolcezza
allineamento, quota sorvolo raggi/g), **Volo** (spinta newtoniana, onset, freno X, rollio, crociera...),
**Camera** (sensibilità mouse, velocità a piedi, **FOV** — abbassalo per ridurre la deformazione prospettica
delle sfere ai bordi). Ogni manopola persiste in PlayerPrefs (chiave `wanderer.tune.*`);
il toggle stazionario persiste via `GameSettings`. Estendere = una riga `F(...)`/`B(...)` nella tab giusta in
`SettingsMenu.Build()`. Le preferenze "vere" del giocatore stanno in `GameSettings` (statiche + PlayerPrefs).
**Default originali + "Ripristina default" per scheda**: `Build()` gira PRIMA di applicare i PlayerPrefs, quindi
cattura come default di ogni manopola il valore di codice (quelli decisi insieme, nei field initializer del
`PlanetWalker` = unica fonte di verità). Il pulsante reimposta quei valori e cancella la taratura salvata →
si sperimenta senza paura.

## Scala (decisa)

Compressa, stile Outer Wilds (NON reale): asteroidi 80-300 m, lune 300-800 m, rocciosi
0.8-1.5 km **walkable**; giganti gassosi 1.5-3 km in cui **voli dentro** (volume nuotabile +
isole + tornado, tipo Profondo Gigante); stelle 3-5 km a cui **ti avvicini ed entri**. I corpi
non-walkable (gas/stelle) saranno un **secondo renderer volumetrico** (raymarch su sfera-guscio),
non mesh. I rocciosi usano il **quadtree CDLOD** (vedi "Stato attuale").

## Direzione (il GIOCO, non il renderer)

I pianeti si **creano nell'editor da una ricetta** (`PlanetRecipe`), poi si **FISSANO** (bake su disco):
il procedurale è uno strumento di CREAZIONE, non un sistema runtime. La superficie ravvicinata col quadtree
è a target — costruire il GIOCO: più corpi DIVERSI + un VERBO (atterra · cammina · raccogli · vai altrove ·
puoi fallire). **MVP: mini-loop su 2-3 corpi.**
FATTO: hand-off di gravità, mappa+selezione, **viaggio fra corpi + match-velocity**, indicatore di rotta,
**autopilota**, **editor RICCO (processi ordinati: crateri/mari geometrici/tettonica)**, **quadtree CDLOD**, **tre
corpi (Pianeta, Cetra, Luna)**, **GPU per l'editor Tappe 1-3 (anteprima GPU completa: geometria+colore+normali a parità)** —
puoi volare da un corpo all'altro, atterrare, ripartire. MANCANO: il teletrasporto, il VERBO, altri corpi diversi.
**PROSSIMO:** resa GPU IN GIOCO (B1) · materiali per pendenza/quota + PBR (SC/ED) · il GIOCO. Vedi `TODO.md` / [[wanderer-rendering-roadmap]].

## Come si avvia

Unity 6, menu **Wanderer → Crea scena di gioco**, poi **Play** (il comando crea `Game.unity` e la
registra nei Build Settings → niente "build nera"). Tutta la scena è costruita da codice in
`GameBootstrap.cs`: niente setup manuale nell'editor. I parametri (raggi, gravità, terreno, orbite,
torcia) sono lì, commentati. Altri menu: **Wanderer → Apri editor pianeti** (scena editor) e
**Wanderer → Bake planet assets** (bake offline su disco di pianeta + Cetra).

## Architettura

```
Core/      Vector3d, FloatingOrigin   — doppia precisione, origine ancorata al pianeta
           PerformanceGovernor        — cap fps (30 attivi / 15 idle): leva sul calore CPU
           RenderScaler               — render a frazione di risoluzione (ora 1.0: GPU libera)
           GameSettings               — opzioni runtime (facilitazioni) statiche + PlayerPrefs
Physics/   KeplerOrbit, CelestialBody (UniversePosition + UniverseVelocityAt), SolarSystem (Reference: corpo ancorato; preserva la velocità allo switch)
World/     PlanetTerrain     — SampleHeight/SurfaceNormal: pipeline di TerrainLayer, unica verità mesh+walker. Recipe + ApplyRecipe + FaceMaterials (per proxy mappa ed eclissi)
           PlanetRecipe      — RICETTA salvabile (JSON): forma base + N pipeline crateri + colore. LoadResource / ScaledTo(raggio)
           TerrainLayer      — astrazione di un processo (forma → altezza); base, poi crateri, ...
           BaseTerrainLayer  — forma di base (fBm)
           CraterTerrainLayer— processo "bombardamento": crateri additivi, griglia 3D hashata; profilo a legge di potenza (rimSharpness)
           Noise3D           — gradient noise (Perlin) CPU per la forma della mesh
           PlanetMeshBuilder — cube-sphere; ComputeFaceData (thread-safe) + CreateMesh (main thread); FaceAxes/ParamToDir
           PlanetQuadtree    — RENDERER attivo: chunked LOD CDLOD (geomorph, skirt, cache LRU, async). Init(terrain, faceMats, cam)
           SingleMeshPlanet  — 6 facce, niente LOD, build su thread + proxy. FALLBACK (useQuadtree=OFF)
           GpuHeightBaker    — calcola le altezze sulla GPU (PlanetHeight.compute) per il quadtree. Parità col walker
           GpuShapeBuffers   — UNICA fonte dei parametri GPU: pipeline ORDINATA (buffer (tipo,indice) + buffer per-tipo crateri/mari/tettonica+placche). Build(cs,terrain,kernels)
           GpuPlanetSurface  — anteprima GPU dell'editor: geometria+normali+colore sulla GPU, RenderPrimitivesIndexed dai buffer, NO readback (Tappe 1-3). Toggle G nell'editor
           PlanetPresets     — ConfigureDemoPlanet → ApplyRecipe(PlanetRecipe.Demo()) (condiviso scena + bake)
           PlanetBaker       — bakea per faccia (mask + normale crateri dalla RICETTA + colori): runtime (RT, fallback) o
                               da disco per-corpo (TryLoadBakedMaterials(terrain, dir) ← Resources/BakedPlanet[_Cetra])
           SunLight
           EclipseDriver  — ombre di eclissi analitiche: sceglie l'occlusore allineato col sole, passa gli uniform ai materiali (vedi "Eclissi")
Player/    PlanetWalker   — camminata su sfera + volo jetpack (volo libero in Newtoniano, spinta scalata alla gravità)
           Flashlight     — torcia che scala con la quota
           MapMode        — mappa (M): zoom-out + orbite + selezione corpo destinazione. Corpi REALI (proxy craterizzato), "TU SEI QUI" + scia della traiettoria (universo, ring buffer)
           RouteIndicator — reticolo di rotta sul corpo selezionato (HUD, texture procedurali)
           OrbitDisplay   — orbite a schermo (O): fili luminosi OW (shader Wanderer/OrbitLine, mesh-nastro cacheata, spessore costante in px)
Items/     SuitPickup
UI/        SettingsMenu   — schermata impostazioni (à): congela i comandi, regola le facilitazioni
           PlanetEditor   — UI dell'editor di pianeti (scena separata): modifica la RICETTA, anteprima live, salva/carica
           EditorOrbitCam — camera orbitale dell'editor (tasto destro ruota, rotella zoom)
           EditorLightMode— modo luce dell'editor (L): ancorata (sole fisso) / libera (sole agganciato alla vista)
Bootstrap/ GameBootstrap        — costruisce la scena di gioco (parametri qui; toggle useQuadtree; ApplyCetraRecipe + BuildCetra)
           PlanetEditorBootstrap— costruisce la scena editor (pianeta da SmoothSphere + camera orbitale + UI)
Editor/    SceneSetup (menu "Crea scena di gioco" / "Apri editor pianeti"), PlanetBakeTool ("Bake planet assets": bake offline pianeta + Cetra, #13)
Debug/     DebugHud
Shaders/   PlanetSurfaceBaked (Wanderer/PlanetBaked) — superficie del pianeta + GEOMORPH CDLOD nel vert (quadtree) + ECLISSI analitiche nel surf + mare liquido (_SeaLiquid)
           CraterNormalBake (Wanderer/CraterNormalBake) — bake normale crateri per faccia (mippata)
           PlanetBake (Wanderer/PlanetBake)          — bake maschera minerale
           DetailNormalBake                          — bake grana → normal map tileable
           OrbitLine (Wanderer/OrbitLine)            — filo d'orbita: additivo, spessore costante in px (espansione screen-space nel vert), glow + coda al pianeta
           PlanetProcedural (Wanderer/PlanetProcedural) — anteprima GPU: legge pos+normali dai buffer via SV_VertexID; COLORE procedurale dalla ricetta (no texture bakate); mare liquido (_SeaLiquid: glint+fresnel, larghezza ∝ rugosità)
           PlanetHeight.compute                      — altezze sulla GPU = walker, PIPELINE ORDINATA (base+crateri/mari/tettonica). Kernel: CSParity, CSNodeGrid, CSFaceGrid/CSFaceNormals, CSIndices (index buffer su GPU). Parità col CPU
           PlanetSurface (Wanderer/Planet)           — vecchio shader procedurale, solo fallback
           PlanetNoise.cginc                         — libreria noise condivisa (vnoise, fbm...)
```

Regola di fondo: ciò che è "vero" vive in coordinate-universo (`double`); la
conversione a float avviene in un solo punto. La floating origin tiene il pianeta
vicino all'origine di Unity → la precisione non degrada mai.

## Lezioni dure (NON ripetere questi errori)

- **Oggetti statici del mondo si posizionano al caricamento, da dati noti e
  stabili — mai leggendo transform gestiti dalla fisica al frame 0** (il Rigidbody
  non è ancora sincronizzato, legge (0,0,0)). Vedi come la tuta riceve la posizione
  calcolata in `GameBootstrap`, non auto-rilevata.
- **Gravità radiale: clampa `r` al raggio** (`rEff = max(r, radius)`) nel calcolo
  di `g`, altrimenti il picco 1/r² al centro catapulta il giocatore nello spazio.
- **Quando un artefatto sopravvive a più cambi della cosa che sospetti, NON è in
  quella cosa.** I "glifi" sulla superficie sono stati inseguiti per ~10 giri nel
  noise (hash, interpolazione, ottave, value→Perlin) invano: erano nella
  **conversione della normale nello shader** (usavo `dir` radiale come base invece
  della normale della mesh → distorsione dipendente dalla pendenza). Il segnale
  decisivo è stato di Dario: *"prima delle modifiche non committate non c'erano"*.
  Metodo giusto: partire da lì, fare `git diff`, bisezione.
- **Normali da heightfield: usa il bump tangente STANDARD** `float3(-dot(G,T),
  -dot(G,B), 1)` con i tangenti della mesh come base (T,B,N). La normale di mondo
  resta continua anche ai poli perché tangente e bitangente si ribaltano insieme.
  Niente conversioni object-space "furbe".
- **Value noise → struttura a celle visibile nelle normali sotto luce radente.**
  Per le normali serve **gradient noise (Perlin)**, interpolazione **quintica**
  (C2), e **rotazione del dominio per ottava**. Il value noise va bene solo per
  maschere di colore (dove serve il valore, non il gradiente — ed è più economico).
- **Hash: mai combinare le coordinate con XOR semplice** (lineare → pattern
  strutturati). Mixing sequenziale (multiply+shift) o PCG.
- **`Mathf.SmoothStep(a,b,t)` NON è la `smoothstep` di GLSL.** In Unity interpola
  l'OUTPUT tra `a` e `b` secondo `t∈[0,1]`; non soglia l'input fra due edge. Usata come
  edge-threshold (`1 - Mathf.SmoothStep(e0,e1,x)`) torna ~costante → texture/forme
  generate PIENE (il reticolo "disco in un quadrato"). Smoothstep vera a mano:
  `t=saturate((x-e0)/(e1-e0)); return t*t*(3-2t);`.
- **Dettaglio di superficie WORLD-FIXED, mai a frequenza che galleggia con la camera.**
  Provato il "trucco microscopio" (frequenza di campionamento ∝ 1/dist per texel costante
  a schermo): sembra magico ma è non-fisico → i dettagli (sassi) SCIVOLANO e cambiano scala
  mentre ti muovi, e le ottave galleggianti generano MOIRÉ permanente. La via giusta: UV
  ancorata al mondo, scala FISSA, e l'antialiasing/lontananza li fa il MIPMAP HARDWARE. Una
  sola ottava di colore (due copie della stessa foto a scale diverse = effetto "sdoppiato").
- **Sabbia/suolo liscio: la bellezza è FORMA + LUCE, non alta frequenza.** Il dettaglio fine
  di sabbia È grana uniforme = letteralmente rumore ("neve TV") quando ci zoomi. "Nitidezza
  microscopio" e "liscio pulito" sono in conflitto PER LA SABBIA. La magia del dettaglio
  appartiene alle superfici STRUTTURATE (roccia, regolite, crateri), non alla sabbia. Errore
  di categoria costato un giorno: inseguire dettaglio dove serviva smoothness.
- **Texture: serve STRUTTURA multi-scala, non grana uniforme.** Una foto d'asfalto (grana
  fitta uniforme) tiled legge come rumore; una con chiazze medie + sassi + grana (es. soil_dirt)
  legge come terreno vero. La differenza non è la risoluzione, è la struttura.
- **Spotlight su Metal: non abilitarlo/disabilitarlo** per accendere/spegnere (il
  primo render carica la cookie interna pigramente → lampo di memoria non
  inizializzata). Tienilo `enabled`, commuta l'**intensità**. La torcia ora non usa
  cookie esplicita (lo spot di default è già rotondo e più luminoso).
- **Destroy è differito a fine frame**: se un oggetto emissivo va distrutto a
  contatto ravvicinato (la tuta alla raccolta), disabilita renderer/luci
  nell'istante, prima del frame, o lampeggia in faccia.
- **Calore: MISURA prima di ottimizzare. La GPU NON era il collo di bottiglia.** Per due
  giorni ottimizzato lo shader contro il calore; il profilo (Stats → GPU Frametime) ha detto
  **GPU ~1 ms (95% scarica)**, calore = **CPU main thread** che a 60 fps rifà il loop 60
  volte/s per niente. La leva DIRETTA sul calore è quindi il **cap fps** (PerformanceGovernor:
  30 attivi / 15 idle), non lo shader. Corollario per il futuro: **GPU-FIRST.** La GPU ha
  margine enorme → metti lì il lavoro nuovo (dettaglio per-pixel/parallax negli shader, GPU
  instancing per rocce/vegetazione via `RenderMeshIndirect`, compute shader), tieni leggero il
  main thread (le ~400 draw call del quadtree sono il costo CPU principale). Il `RenderScaler`
  è a 1.0 (piena risoluzione): la GPU se lo permette; è la prima leva da riabbassare (0.85) SE
  un domani la carichiamo di effetti. `TimeScale 3` (acceleratore orbite di debug) triplica la
  fisica: in gioco normale è 1.
- **BUILD STANDALONE ≠ editor (causa di bug invisibili nell'editor).** La scena è costruita da
  `GameBootstrap` ma DEVE essere nei **Build Settings** (`EditorBuildSettings.asset`), altrimenti la
  build apre una scena vuota → nera. Gli shader usati SOLO via `Shader.Find` (tutti i materiali sono
  creati a runtime) vengono **strippati** dalla build: vanno messi negli **Always Included Shaders**
  (`GraphicsSettings.asset`) — i custom `Wanderer/*` E i built-in usati (`Standard`, `Unlit/Color`;
  `Sprites/Default` c'era già). Anche le **varianti keyword** si strippano: `Standard` + `_EMISSION`
  attivato a runtime → in build niente bagliore (sfera scura) → per la stella/tuta usa `Unlit/Color`
  (disco pieno, niente variante). Mai `new Material(Shader.Find(...))` senza guardia: se null lancia e
  aborta `Start` → nero totale (ora c'è la guardia: logga e continua). HUD IMGUI a **pixel fissi** →
  minuscolo su schermi Retina/4K: scala i font/marker con `Screen.height` (rif. 1080p).
- **Misura la performance/il calore SU UNA BUILD, non nell'editor.** L'editor (EditorLoop + Profiler in
  Live) gonfia CPU e calore e non dorme tra i frame. Col profilo della build: GPU ~4.5ms, scena banale,
  capped a 30fps → il gioco è leggero; l'apparente calore nell'editor era l'editor. Vedi anche la leva
  fps in PerformanceGovernor e l'architettura performance-first nella memoria.
- **Load time = bake GPU all'avvio (~1.9s), non le mesh.** Le mesh d'appoggio del bake servono solo a
  coprire le UV / dare il frame tangente: tienile a bassa risoluzione (il dettaglio lo fa il fragment
  per-pixel sulle RT a piena risoluzione). Il vero azzeramento del load è il bake-su-disco (#13).
- **Niente ombre da SHADOW MAP** (direzionale e torcia): su questa mesh a luce radente
  danno "crepe" (shadow acne) e lo "schiarimento" oltre la shadow distance. Il
  rilievo emerge bene dalle sole normali. **Ma le ombre fra corpi (eclissi) si fanno
  ANALITICHE nello shader** (`EclipseDriver` + `Wanderer/PlanetBaked`): raggio→disco
  solare in spazio oggetto, niente shadow map → zero acne, nessun limite di shadow
  distance. È la via giusta per questo progetto: quando serve un'ombra geometrica
  precisa, calcolala (come walker/normali), non affidarla alla shadow map.
- **Tassellatura: Metal la regge** (Unity 6, pipeline built-in; `#pragma target 4.6`,
  `tessellate:` + `vertex:disp` a UN parametro — la forma a due parametri con
  `out Input o` NON compila con la tassellatura). Provata e poi **rimossa** dal
  pianeta: il guadagno è marginale finché le ombre proiettate sono spente (il vero
  regalo della geometria reala sono le ombre), e la fascia che soffre (60–800 m) è
  troppo lontana per tassellarla senza scaldare. Inoltre va displacata solo con le
  ottave grosse (~1–4 m): le ottave fini aliasano in schegge a punta col fattore di
  tassellatura. Resta la via giusta SE un giorno si risolvono le ombre o si fa il
  quadtree LOD. Nota di coerenza: il walker segue `SampleHeight` (Noise3D, CPU)
  mentre il displacement userebbe `fbmRelief` (HLSL) → il giocatore "fluttua" sui
  bump nuovi finché le due altezze non si uniscono.
- **Quadtree CDLOD: è il renderer GIUSTO per i corpi rocciosi (era accantonato per errore).** La mesh singola a
  res fissa ha un MURO di risoluzione: da vicino i crateri si sfaccettano e non puoi avere bordi nitidi
  calpestabili. Il quadtree (chunked LOD + geomorph + skirt) dà geometria vera view-dependent → look Elite/Star
  Citizen. Era stato tolto pensando "a questa scala la mesh singola basta": sbagliato appena servono crateri
  nitidi a terra. Lo shader teneva già il geomorph nel vert → riesumarlo (`git show d798107:.../PlanetQuadtree.cs`)
  è stato il 90% del lavoro. NON ri-accantonarlo.
- **Geomorph: il vertice COMPLETA la morph verso il genitore entro la PROPRIA distanza di split**, non a quella di
  merge. `mf = saturate((d − splitDist·(1−range))/(splitDist·range))`. Così quando una patch arriva al suo limite —
  dove può confinare con una più grossa — è già sulla forma del genitore (= la forma della vicina) → i gradini si
  chiudono. Se completa più tardi (1.4·splitDist) resta di dettaglio dove la vicina è già grossa → scalino.
- **Skirt: la profondità NON è arbitraria, è il salto di morph del bordo.** Il gap massimo a un confine di LOD è
  `|delta di morph|` del vertice dispari (scarto fra forma fine e forma del genitore), l'unica cosa che muove quei
  vertici. `skirtDrop = max(worldSize·skirtFactor, maxEdgeMorphDelta·2)`, clamp [3, worldSize] → niente fessure per
  costruzione. Lo skirt deve anche **morfare col bordo** (stesso delta) e avere **normale radiale** (verso l'alto),
  o: crepa sopra lo skirt durante il morph, e lametta verticale scura ai confini.
- **Stitch di LOD (transizioni di shading ai confini): ACCANTONATO.** Niente fessure/buchi, ma restano "scalini" di
  shading dove due livelli si toccano (peggio coi salti di 2+ livelli: l'albero non è bilanciato). Il fix definitivo
  è il **quadtree bilanciato 2:1** (vicini ≤ 1 livello → il morph di un livello basta; si possono togliere gli skirt).
  Deciso ma RIMANDATO: ci si è persi troppo tempo, si va avanti col gioco.
- **Colore dalla ricetta.** `PlanetBaker.BuildMaterial` DEVE impostare `_SoilMean/_MariaColor/_MariaScale/_MariaStr`
  da `terrain.Recipe`, o un corpo marziano esce grigio (lo shader resta sul default lunare). L'editor li spingeva a
  mano; in gioco serve qui.
- **Performance/load del quadtree (prossimo passo, opzione "a" decisa, NON ancora fatto):** il collo di bottiglia è la
  CPU che ricalcola il rumore per ogni vertice di ogni nodo (load lento + finestra "seghettata" finché rifinisce). Il
  bake produce GIÀ le HEIGHTMAP per faccia: far campionare al quadtree la heightmap (un fetch) invece del rumore =
  CPU scarica, build veloce. Attenzione: campionare per DIREZIONE→faccia (non per-faccia con clamp) o si reintroducono
  giunture ai 6 spigoli del cubo. Walker resta analitico (opzione a).

## Superficie e shader (Wanderer/PlanetBaked)

Lo shader USATO è `Wanderer/PlanetBaked`, assegnato per faccia dal quadtree via
`PlanetBaker.BakeFaceMaterials`. Lavora in spazio oggetto (stabile con floating origin).
Catena del colore in `surf`:
1. colore base `_SoilMean` (grigio lunare) × variazione **macro** a bassa frequenza
   (`_MacroVar`/`_MacroScale`, campo dunale ~150 m, NON alta frequenza → niente cavolfiore);
2. **grana** fotografica a basso contrasto (`_SandDetail`), solo < ~120 m, letta SFOCATA
   (mip bias +2) → tono, non puntini; normalizzata sul grigio medio (non sposta il colore);
3. **regioni minerali** (`_MaskMap` R, bakeato per faccia): tinta larga calda/fredda
   (`_MineralA`/`_MineralB`/`_MineralStr`), bassa frequenza — tenue ora, leva per pianeti vari;
4. cappucci chiari sulle creste (`_PeakColor`/`_PeakStr`);
5. **normale**: un soffio di micro-grana (`_GrainStr`) solo < ~13 m (la normale ad alta
   frequenza è la prima causa di sparkle/moiré sotto luce → quasi spenta).
6. **eclissi**: ombra analitica di un altro corpo (vedi sotto). Moltiplica l'albedo dove il disco
   dell'occlusore copre il sole.

**Eclissi (ombre fra corpi)** — `EclipseDriver` (LateUpdate) sceglie per ogni corpo roccioso l'occlusore più
allineato col sole e gli passa, sui materiali bakeati per faccia, gli uniform `_EclipseOccluderPos/Radius`,
`_EclipseSunDir`, `_EclipseSunAngular` (= raggio stella / distanza). Nel `surf` si calcola la **copertura del
disco solare** vista dal punto: separazione angolare sole↔occlusore vs somma dei raggi angolari → umbra piena
quando l'occlusore ingloba il sole, **sbiadita quando è angolarmente più piccolo** (anulare). Conseguenza voluta:
l'ombra **si attenua con la distanza** dall'occlusore (l'umbra ha lunghezza finita, oltre resta solo penombra).
Tutto in **spazio oggetto** (centrato sul corpo, condiviso da mesh in gioco e proxy della mappa → l'eclissi compare
in entrambi). Le eclissi dipendono dalla geometria: con Cetra inclinata ~23° capitano solo vicino ai nodi (rare,
come le stagioni delle eclissi reali).

`vert` fa il **geomorph** (CDLOD) leggendo UV2 (xyz = spostamento verso il genitore, w = splitDist): il vertice
morfa verso la forma del genitore **completando entro la propria distanza di split** (banda [splitDist·(1−`_MorphRange`),
splitDist]) → quando confina con una patch più grossa è già sulla sua forma, transizione continua, niente pop. Anche il
**COLORE** viene dalla ricetta (`_SoilMean`/`_MariaColor`/`_MariaScale`/`_MariaStr`, impostati in `PlanetBaker.BuildMaterial`).
Tutto world-fixed + mipmappato.

Manopole identità pianeta: ora la **RICETTA** (`PlanetRecipe`): colore suolo/mari, forma base (ampiezza/freq/ottave),
pipeline di crateri (raggio/densità/profondità/bordo/`rimSharpness`). Texture: solo `soil_dirt` è usata (base+grana+normale);
`soil_red`/`soil_rock` importate per pianeti futuri, non ancora cablate.

## Generazione pianeti (roadmap concordata)

**Stato:** esiste l'**editor di pianeti** (scena separata) e il modello-dati **`PlanetRecipe`** (forma base +
pipeline di crateri + colore, salvabile in JSON). Cetra è stata creata così. La RICETTA è la fonte di verità
condivisa da editor, bake e quadtree. **Prossima sessione: migliorie a editor + ricette** (es. swap/scala texture,
più tipi di pipeline — mari/tettonica/montagne/ghiaccio —, editing per-feature dei singoli crateri, più preset).

Obiettivo a tendere: dare a Claude la **composizione chimica** (+ proprietà fisiche) di un corpo e
generare un pianeta "tipo-Mercurio / tipo-Luna / tipo-Ganimede".

**Verità tecnica:** la composizione NON produce l'aspetto in modo deterministico — l'aspetto
nasce dai PROCESSI (impatti, vulcanismo, ghiaccio, atmosfera) sulla storia del corpo. Non
serve accuratezza fisica: serve una mappatura **plausibile e coerente**. Architettura:

```
composizione + fisica  →  [ricetta: regole + preset di riferimento]  →  parametri generatore  →  pianeta
(ferro, silicati, ghiaccio,                                            (colore, ottave/ampiezza,
 zolfo; massa, temp, atmosfera)                                         crateri, ghiaccio, atmosfera)
```

Un "archetipo" = struct/ScriptableObject di parametri. La ricetta li riempie (regole +
interpolazione tra corpi reali). Sfrutta la separazione già esistente FORMA (noise) / ASPETTO
(shader). Mappature: silicati→grigio-bruno · ossidi di ferro→rosso · ghiaccio→chiaro/liscio/
alto albedo · zolfo→giallo · massa/raggio→gravità→ripidità · temperatura→roccia/ghiaccio.

**Ordine di costruzione (modo Carmack — NON costruire l'astrazione per prima):** fai 2-3
pianeti A MANO con manopole dirette (Luna, Marte, Mercurio), guarda cosa li distingue davvero,
POI estrai la ricetta dai pianeti veri. Aggiungi UN processo alla volta (prima crateri, poi
ghiaccio, poi atmosfera). Mai costruire sul vuoto.

## Git

Repo su `dudero78/wanderer`, branch `main`, via host SSH `github.com-dudero78`.
Dario lavora su `main` (progetto solo). Commit/push solo su richiesta.

Le cartelle `Assets/Resources/BakedPlanet*` (texture bakeate) sono in `.gitignore`: sono cache PESANTI ma
RIGENERABILI dal comando "Bake planet assets". Le **ricette** (`Resources/Planets/*.json`) e le texture sorgente
(`Resources/Textures`) restano versionate: sono le fonti, il bake è derivato.

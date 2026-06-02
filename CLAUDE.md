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

**Pianeta walkable a MESH SINGOLA** (cube-sphere, 6 facce, NESSUN LOD; `SingleMeshPlanet`).
A scala compressa (corpi ≤ ~1.5 km, vedi "Scala") la mesh singola basta e scioglie alla
radice cuciture/skirt/popping — difetti **inerenti** al chunked LOD, provato e **abbandonato**
(vedi "Lezioni dure"). La build full-res gira su thread (niente freeze), con un proxy a bassa
risoluzione mostrato nel frattempo. Mesh+walker leggono la stessa `SampleHeight`: una sola verità.

**Crateri: FATTI** — geometria vera nell'heightfield (`CraterTerrainLayer`: composizione
additiva, griglia 3D hashata seam-free, profilo C1, conca/bordo/ejecta/picco) + normale
bakeata (`CraterNormalBake`) per i bordi nitidi, filtrata dal mipmap.

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
- **Orbite a schermo** (`O`, `OrbitDisplay`): mostra/nasconde le orbite del sistema come linee anche in
  volo. L'ellisse (Kepler, fissa nel frame del genitore) è cacheata una volta e ogni frame solo traslata
  con la floating origin → niente solve orbitale per frame.

Comandi volo: `WASD` spinta · `Space`/`Shift` su/giù · `Q/E` rollio (volo libero) · `N` Crociera/Newtoniano
· `X` match-velocity · `T` autopilota · `F` torcia · `M` mappa · `O` orbite · `à` impostazioni.

**Autopilota (`T`, toggle)**: hands-off completo verso il corpo selezionato. Si inserisce solo con la tuta e
con una destinazione scelta sulla mappa; passa a Newtoniano. Orienta il muso al bersaglio, pilota la velocità
RADIALE verso/dal corpo con profilo "frena in tempo" **bidirezionale** `vWant = sign(dtg)·√(2·a·|dtg|)`
(capato a `autoCruiseSpeed`, tetto largo → di norma comanda il √): fuori dal sorvolo si avvicina, dentro
risale → il **punto di sorvolo** è un EQUILIBRIO STABILE. Componente laterale desiderata = 0 (annulla la
deriva). Il Δv si applica a `rb.linearVelocity` (identico in ogni riferimento inerziale → indipendente
dall'ancora).
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
non mesh. Conseguenza chiave: i rocciosi stanno in una mesh singola → niente LOD.

## Direzione (il GIOCO, non il renderer)

I pianeti si **generano da una descrizione, poi si FISSANO** (bake) come asset fatti a mano:
il procedurale è uno strumento di CREAZIONE, non un sistema runtime. La superficie ravvicinata
è già a target — **smettere di limarla** e costruire il GIOCO: più corpi DIVERSI + un VERBO
(atterra · cammina · raccogli · vai altrove · puoi fallire). **MVP: mini-loop su 2-3 corpi.**
FATTO: hand-off di gravità, mappa+selezione, **viaggio fra corpi + match-velocity**, indicatore
di rotta — puoi già volare da un corpo all'altro, atterrare e ripartire. MANCANO: più pianeti, il
teletrasporto, il VERBO. NON costruire ora l'astrazione ricetta composizione→pianeta (trappola
identica al quadtree: prima 2-3 corpi a mano).

## Come si avvia

Unity 6, menu **Wanderer → Crea scena demo**, poi **Play**. Tutta la scena è
costruita da codice in `GameBootstrap.cs`: niente setup manuale nell'editor.
I parametri (raggi, gravità, terreno, torcia) sono lì, commentati.

## Architettura

```
Core/      Vector3d, FloatingOrigin   — doppia precisione, origine ancorata al pianeta
           PerformanceGovernor        — cap fps (30 attivi / 15 idle): leva sul calore CPU
           RenderScaler               — render a frazione di risoluzione (ora 1.0: GPU libera)
           GameSettings               — opzioni runtime (facilitazioni) statiche + PlayerPrefs
Physics/   KeplerOrbit, CelestialBody (UniversePosition + UniverseVelocityAt), SolarSystem (Reference: corpo ancorato; preserva la velocità allo switch)
World/     PlanetTerrain     — SampleHeight/SurfaceNormal: pipeline di TerrainLayer, unica verità mesh+walker
           TerrainLayer      — astrazione di un processo (forma → altezza); base, poi crateri, ...
           BaseTerrainLayer  — forma di base (fBm)
           CraterTerrainLayer— processo "bombardamento": crateri additivi, griglia 3D hashata, profilo C1
           Noise3D           — gradient noise (Perlin) CPU per la forma della mesh
           PlanetMeshBuilder — cube-sphere; ComputeFaceData (thread-safe) + CreateMesh (main thread)
           SingleMeshPlanet  — 6 facce, no LOD, build su thread + proxy
           PlanetPresets     — parametri terreno dei corpi (preset condiviso scena + bake offline: una verità)
           PlanetBaker       — bakea per faccia: maschera minerale + normale crateri; detail-normal condivisa.
                               Runtime (RT, ~1.9s, fallback) o da disco (TryLoadBakedMaterials ← asset bakeati)
           SunLight
Player/    PlanetWalker   — camminata su sfera + volo jetpack (volo libero in Newtoniano, spinta scalata alla gravità)
           Flashlight     — torcia che scala con la quota
           MapMode        — mappa (M): zoom-out + orbite + selezione corpo destinazione
           RouteIndicator — reticolo di rotta sul corpo selezionato (HUD, texture procedurali)
           OrbitDisplay   — orbite del sistema a schermo (O), anche in volo (ellisse cacheata)
Items/     SuitPickup
UI/        SettingsMenu   — schermata impostazioni (à): congela i comandi, regola le facilitazioni
Bootstrap/ GameBootstrap  — costruisce la scena (tutti i parametri sono qui)
Editor/    SceneSetup, PlanetBakeTool (menu "Wanderer → Bake planet assets": bake offline su disco, #13)
Debug/     DebugHud
Shaders/   PlanetSurfaceBaked (Wanderer/PlanetBaked) — superficie del pianeta (la mesh singola usa questo)
           CraterNormalBake (Wanderer/CraterNormalBake) — bake normale crateri per faccia (mippata)
           PlanetBake (Wanderer/PlanetBake)          — bake maschera minerale
           DetailNormalBake                          — bake grana → normal map tileable
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
- **Niente ombre proiettate** (direzionale e torcia): su questa mesh a luce radente
  danno "crepe" (shadow acne) e lo "schiarimento" oltre la shadow distance. Il
  rilievo emerge bene dalle sole normali.
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

`vert` fa il **geomorph** (CDLOD) leggendo UV2 (xyz = spostamento verso il genitore, w =
splitDist): transizione LOD continua, niente pop. Tutto è world-fixed + mipmappato.

Manopole identità pianeta: `_SoilMean`/`_SoilTint` (colore), `Amplitude`/`Octaves` nel
terreno (forma). Texture: solo `soil_dirt` è usata (base+grana+normale). `soil_red`/`soil_rock`
sono importate per i pianeti futuri (rosso/scuro), non ancora cablate.

## Generazione pianeti (roadmap concordata)

Obiettivo: dare a Claude la **composizione chimica** (+ proprietà fisiche) di un corpo e
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

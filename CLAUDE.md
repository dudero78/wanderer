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

Funziona: floating origin + doppia precisione, orbita Kepleriana, **pianeta
walkable** con **quadtree LOD** (cube-sphere chunked, build asincrona, node-cache
LRU, geomorph CDLOD → niente pop, ritorno/orbita istantanei), **gravità radiale**,
**volo col jetpack** (tuta da raccogliere), **torcia** (F), ciclo giorno/notte.

**Superficie — base lunare liscia, committata e stabile.** Colore quasi uniforme
grigio (`_SoilMean`) + variazione macro a bassa frequenza; il bello lo fanno la
FORMA del terreno (colline morbide, 5 ottave) e la LUCE, non il dettaglio di
superficie. Tutto il dettaglio è **WORLD-FIXED** (UV ancorata alla faccia) + mipmap
hardware → niente moiré, niente scivolamento, nitido a ogni distanza. L'identità di
un pianeta = 2 colori (`_SoilMean`/`_SoilTint`) + manopole di terreno (`Amplitude`,
`Octaves`). Il PERCHÉ di queste scelte (due giorni di vicoli ciechi) è in "Lezioni dure".

**Volo a due modelli, toggle con `N`** (`PlanetWalker`):
- *Crociera* (default tuta): la potenza dei motori cresce con la quota e con quanto
  tieni la spinta (rampa `boost01`), così resti maneggevole vicino al suolo
  (atterraggio intatto) e veloce in alto. Comandi sugli assi tangenti del pianeta.
  Smorzamento **anisotropo**: frena moto orizzontale e salita ma NON la caduta, così
  la gravità si sente (precipiti accelerando). Conseguenza: il jetpack non galleggia
  da solo, per tenere quota dai un filo di Space.
- *Newtoniano*: nessun attrito, la spinta si somma (delta-v reale, alla Outer
  Wilds). Comandi **relativi allo sguardo** (puntare e andare), non agli assi
  tangenti — altrimenti da lontano non si torna indietro. Sarà il default
  dell'astronave. **Freno di assetto** (`X`, match velocity): controspinta
  automatica che azzera la velocità relativa al pianeta — serve per uscire
  dall'orbita e atterrare (a 500 m di quota bastano ~50 m/s di traverso per orbitare
  stabile: senza freno giri in tondo all'infinito).

HUD volo: velocità, **radiale con segno** (− = ti avvicini), **tangenziale**
(quanta orbita hai), modello attivo, stato `FRENO` e stato **torcia**.

Prossimo passo visivo concordato: **crateri**.

## Come si avvia

Unity 6, menu **Wanderer → Crea scena demo**, poi **Play**. Tutta la scena è
costruita da codice in `GameBootstrap.cs`: niente setup manuale nell'editor.
I parametri (raggi, gravità, terreno, torcia) sono lì, commentati.

## Architettura

```
Core/      Vector3d, FloatingOrigin   — doppia precisione, origine ancorata al pianeta
           PerformanceGovernor        — cap fps (30 attivi / 15 idle): leva sul calore CPU
           RenderScaler               — render a frazione di risoluzione (ora 1.0: GPU libera)
Physics/   KeplerOrbit, CelestialBody, SolarSystem
World/     PlanetTerrain     — SampleHeight/SurfaceNormal (unica fonte di verità mesh+walker)
           Noise3D           — gradient noise (Perlin) CPU per la forma della mesh
           PlanetMeshBuilder — cube-sphere, normali analitiche, tangenti
           PlanetQuadtree    — quadtree LOD chunked: split/merge, node-cache LRU, geomorph,
                               build asincrona, horizon culling, predictive LOD
           PlanetBaker       — bakea la maschera minerale per faccia + detail-normal condivisa
           SunLight
Player/    PlanetWalker   — camminata su sfera + volo jetpack
           Flashlight     — torcia che scala con la quota
Items/     SuitPickup
Bootstrap/ GameBootstrap  — costruisce la scena (tutti i parametri sono qui)
Debug/     DebugHud
Shaders/   PlanetSurfaceBaked (Wanderer/PlanetBaked) — superficie, USATO dal quadtree
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

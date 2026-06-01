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
walkable** con terreno procedurale, **gravità radiale**, **volo col jetpack**
(tuta da raccogliere), **torcia** (F), ciclo giorno/notte, shader procedurale del
pianeta con LOD per distanza.

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
Physics/   KeplerOrbit, CelestialBody, SolarSystem
World/     PlanetTerrain  — SampleHeight/SurfaceNormal (unica fonte di verità mesh+walker)
           Noise3D        — gradient noise (Perlin) CPU per la forma della mesh
           PlanetMeshBuilder — cube-sphere, normali analitiche, tangenti
           SunLight
Player/    PlanetWalker   — camminata su sfera + volo jetpack
           Flashlight     — torcia che scala con la quota
Items/     SuitPickup
Bootstrap/ GameBootstrap  — costruisce la scena
Debug/     DebugHud
Shaders/   PlanetSurface.shader — Wanderer/Planet
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
- **LOD del dettaglio = numero di ottave in funzione della dimensione in PIXEL**
  delle feature (`oct ~ K - log2(dist)`). Così non è mai sfocato (vicino) né
  aliasato (lontano), senza fade artificiali.
- **Spotlight su Metal: non abilitarlo/disabilitarlo** per accendere/spegnere (il
  primo render carica la cookie interna pigramente → lampo di memoria non
  inizializzata). Tienilo `enabled`, commuta l'**intensità**. La torcia ora non usa
  cookie esplicita (lo spot di default è già rotondo e più luminoso).
- **Destroy è differito a fine frame**: se un oggetto emissivo va distrutto a
  contatto ravvicinato (la tuta alla raccolta), disabilita renderer/luci
  nell'istante, prima del frame, o lampeggia in faccia.
- **Calore (anche su M3 Max):** uno shader procedurale full-screen a risoluzione
  Retina tiene la GPU al 100%. Tenere le ottave basse dove si può, value noise
  economico per le maschere, break anticipato nei cicli per distanza. Cap fps a 60
  (`GameBootstrap`). Se scotta ancora: 30 fps, o meno ottave vicine.
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

## Fasce di distanza del terreno (stato attuale, texture-based)

- **vicino (≤45 m):** terra coi sassolini + grana + bump → buono.
- **media (45–800 m):** è il punto debole strutturale. Solo texture su mesh liscia;
  qualunque dettaglio aggiunto (bump/albedo) oscilla tra "fuso", "cavolfiore" (chiazze
  d'albedo) e "striature" (bump a sguardo radente). La config stabile committata
  evita gli artefatti accettando un dettaglio "pulito ma non scolpito". Il salto vero
  lì lo danno solo CRATERI (geometria con struttura) o LOD della mesh, non altri knob.
- **lontano (>800 m):** lunare pulita.
- Manopole/scelte anti-artefatto nel materiale `Wanderer/PlanetBaked`: `_FlatDist`/`_FlatStr`
  (appiattisce l'albedo a distanza → niente cavolfiore, che era l'albedo a chiazze, non il
  bump); il **bump del rilievo è gated su due assi**: per ANGOLO (pieno dove la superficie
  guarda la camera, spento al limbo/sguardo radente → niente striature) e per DISTANZA LONTANA
  (svanisce tra 480 e 780 m: lì il rilievo bakeato a 0.4 m/texel va sub-pixel e mippa in blob,
  quindi si lascia la sola ombreggiatura pulita della mesh, ottava 6). Resta sempre pieno da
  vicino. De-repeat dei suoli a doppio campione ruotato. `_ReliefBias` (mip bias negativo, -0.6)
  de-sfuoca la fascia 450-600 m dove la texture sta per esaurirsi (troppo negativo → sfarfallio).
  Limite strutturale: oltre ~600 m la texture del rilievo è esaurita (texel sub-pixel), la
  definizione lì può venire SOLO dalla mesh/crateri, non da altri knob.

## Shader PlanetSurface (Wanderer/Planet)

`#pragma target 4.0` (serve uint per l'hash PCG). Lavora in spazio oggetto
(stabile con la floating origin). Manopole utili nel materiale: `_DetailStr`
(forza rilievo), `_BaseFreq` (scala), `_RoughThresh`/`_RoughBoost` (zone rugose),
colori mari/altopiani/vette. La mesh fa la forma larga (5 ottave), lo shader il
dettaglio fine con LOD continuo.

## Git

Repo su `dudero78/wanderer`, branch `main`, via host SSH `github.com-dudero78`.
Dario lavora su `main` (progetto solo). Commit/push solo su richiesta.

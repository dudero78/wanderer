# Wanderer — TODO

Lista di lavoro che sopravvive tra le sessioni. Aggiornata al **3 giugno 2026**.
Dettaglio tecnico nel `CLAUDE.md`.

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

## Accantonato (deciso ma rimandato)

- ⏸️ **Stitch di LOD** (transizioni di shading "scalini" ai confini): niente fessure/buchi, ma restano i salti
  di shading (peggio coi salti di 2+ livelli). Fix definitivo = **quadtree bilanciato 2:1** (vicini ≤ 1 livello
  → il morph di un livello basta, si possono togliere gli skirt). Rimandato: troppo tempo, avanti col gioco.

## Prossimo

- ⬜ **Migliorie editor + ricette (FOCUS prossima sessione, deciso con Dario):** swap/scala texture, più tipi di
  pipeline (mari, tettonica, montagne, ghiaccio…), editing per-feature (cancella/modifica singolo cratere),
  più preset, bake da dentro l'editor.
- ⏸️ **Perf/load del quadtree — strada GPU (PARCHEGGIATA il 3 giu, non cancellata):** spostare il calcolo della
  forma sulla GPU (compute shader), walker analitico su CPU. La **parità C#↔HLSL è PROVATA** (menu "Test parità
  altezza GPU↔CPU", verde anche sulla griglia del nodo; bug Metal `float3` chiuso → buffer piatto di float). Codice
  dormiente su disco: `Resources/Shaders/PlanetHeight.compute`, `GpuHeightBaker.cs`, `PlanetGpuParityTest.cs`; il
  build resta su CPU (un commento in `GameBootstrap.AddSurface` spiega come riattivarlo). **Bloccante:** la lettura
  asincrona (`AsyncGPUReadback`) TRASCINA → i nodi non diventano visibili in tempo (superficie lontana/buchi). Via
  giusta = **B1: render diretto dai buffer GPU, niente readback** — sessione dedicata.

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

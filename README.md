# Wanderer

Scheletro per un gioco spaziale seamless: sistema solare con fisica orbitale
reale e pianeti su cui camminare, costruito attorno a un sistema di coordinate
a doppia precisione fin dalle fondamenta. Tutto guidato da codice — nessun
setup manuale nell'editor.

## Cosa fa già

- Coordinate-universo in `double`, isolate dietro un'unica astrazione.
- Floating origin: il pianeta su cui stai resta vicino all'origine di Unity,
  il resto dell'universo si muove. La precisione non degrada mai.
- Orbita Kepleriana analitica, deterministica, senza drift.
- Camminata su sfera con gravità radiale verso il centro del pianeta.
- Ciclo giorno/notte che emerge dall'orbita (il sole sorge e tramonta davvero).

## Come avviarlo

1. Apri la cartella `Wanderer/` con **Unity 6** (Unity Hub → Add → seleziona la
   cartella). Se la versione esatta differisce, Unity propone di aggiornare:
   accetta, è normale.
2. In alto, menu **Wanderer → Crea scena demo**. Genera e apre la scena.
3. Premi **Play**.

Controlli: `WASD` muovi · `Space` salta · mouse per guardare · `Esc` libera il
cursore · click per riagganciarlo.

L'HUD in alto a sinistra mostra il punto chiave: la posizione del player nello
spazio scena resta piccola, mentre `SceneOrigin` — la tua distanza reale
nell'universo — cresce senza limiti. È la floating origin al lavoro.

## Architettura

```
Core/
  Vector3d        coordinate-universo a doppia precisione (la fondazione)
  FloatingOrigin  il punto dell'universo mappato sull'origine di Unity
Physics/
  KeplerOrbit     orbita analitica (C# puro, testabile, niente Unity)
  CelestialBody   stella/pianeta: posizione-universo + gravità
  SolarSystem     orchestra tempo, orbite, origine, rendering (in quest'ordine)
Player/
  PlanetWalker    camminata su sfera con gravità radiale
World/
  SunLight        orienta la luce dalla stella al pianeta
Bootstrap/
  GameBootstrap   costruisce l'intera scena da codice
Debug/
  DebugHud        HUD IMGUI di diagnostica
Editor/
  SceneSetup      voce di menu che genera la scena
```

La regola di fondo: tutto ciò che è "vero" vive in coordinate-universo (double).
Unity lavora in float vicino all'origine. La conversione avviene in **un solo
punto** (`CelestialBody.SyncTransform`). Qualunque cambio di direzione futuro
non tocca questa fondazione.

## Da regolare

In `GameBootstrap` trovi i numeri (raggi, gravità, parametri d'orbita) e in
`SolarSystem.TimeScale` la velocità di simulazione. Sono tutti commentati e
liberi da modificare.

## Prossimi passi naturali

1. Terreno procedurale: quad-sphere + LOD a quadtree + noise su compute shader.
2. Atmosfera: shader di scattering.
3. Volo: nave + transizione superficie ↔ spazio, con la floating origin
   agganciata alla nave invece che al pianeta.
4. Più corpi: lune, altri pianeti, gerarchie orbitali.
5. Grafica: passaggio a HDRP quando si lavora sul look (l'architettura è già
   indipendente dalla pipeline di rendering).
```

Nota: i materiali usano lo shader `Standard` (pipeline built-in). Passando a
URP/HDRP andranno aggiornati — è l'unico punto legato alla pipeline.

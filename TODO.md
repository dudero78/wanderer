# Wanderer — TODO

Lista di lavoro che sopravvive tra le sessioni. Rispecchia la todo-list di lavoro di Claude.
Aggiornata al **2 giugno 2026**. Dettaglio tecnico nel `CLAUDE.md`.

## Fatto (milestone)

- ✅ Fondamenta: doppia precisione + floating origin, orbita kepleriana, gravità radiale.
- ✅ Pianeta walkable a **mesh singola** (quadtree LOD cancellato — difetti inerenti).
- ✅ **Crateri** come geometria vera + normale bakeata.
- ✅ Volo a due modelli (`N`: Crociera / Newtoniano), tuta + torcia.
- ✅ **Igiene**: build mesh su thread, quadtree rimosso, doc allineata.
- ✅ **Mappa (`M`)**: zoom-out sul sistema, orbite, selezione corpo destinazione.
- ✅ **Viaggio fra corpi**: origine ancorata al corpo di riferimento, **match-velocity (`X`)**,
  **volo libero** in Newtoniano (no aggancio gravità), spinta scalata alla gravità (decolli da
  qualunque corpo), velocità-universo preservata allo switch. `TimeScale=1`.
- ✅ Primo viaggio completo pianeta→stella con atterraggio e ripartenza.
- ✅ **#11 Indicatore di rotta — baseline** (`RouteIndicator`): anello a parentesi + chevron +
  pip + freccia prograde + distanza/velocità a lato; freccia al bordo se fuori vista. Committato.

## PROSSIMO: rifinire l'HUD di navigazione (Dario riparte da qui)

La baseline funziona ma è migliorabile. Piano completo (dalla critica "da team", da rifare PULITO):

1. **Marker del vettore velocità (prograde) stile cockpit**: NON un triangolo incollato all'anello,
   ma un **cerchietto con tacche (⊕)** piazzato nel **punto di fuga** della velocità relativa
   (`WorldToScreenPoint(camPos + relVel.normalized * K)`). Se si sovrappone al bersaglio → sei in
   **rotta d'intercetto**. È lo strumento vero per pilotare verso un corpo.
2. **Anello più nitido + alone tenue** (la baseline è timida). Per forme con gradiente serve la
   **smoothstep scritta a mano** — `Mathf.SmoothStep` di Unity NON è la smoothstep di GLSL
   (interpola l'output → riempie la texture: era il bug del "disco in un quadrato"). A mano:
   `t = saturate((x-e0)/(e1-e0)); return t*t*(3-2t);` (lezione nel CLAUDE.md).
3. **Marker che toccano l'anello** (chevron senza buco).
4. **Stato SINCRONIZZATO**: quando la velocità relativa ≈ 0 → reticolo **verde** + "sincronizzato"
   (sai che puoi puntare e andare dritto).
5. **Testo leggibile su corpi chiari**: usare l'**ombra** del testo (nero sfalsato 1px), NON un
   fondino/box scuro (copriva il reticolo).
6. **Dissolvenza ravvicinata**: quando il corpo riempie lo schermo (raggio VERO, non clampato) il
   reticolo svanisce — sei arrivato, non intralcia.
7. **Velocità solo in volo**: a terra la velocità relativa al corpo selezionato è l'orbita del
   pianeta (es. ~685 m/s da fermo) — corretta ma confonde a piedi → mostrarla solo quando airborne
   (`HasJetpack && Altitude > 3`).
8. **⚠️ REQUISITO (Dario): velocità CON SEGNO.** La velocità nel reticolo deve essere **negativa
   quando ti ALLONTANI** dal corpo (distanza in aumento), positiva quando ti avvicini. Oggi mostra
   il modulo (sempre positivo). = velocità di avvicinamento (componente radiale verso il corpo, col
   segno). NB: nel `DebugHud` il "radiale" ha la convenzione OPPOSTA (− = ti avvicini); qui Dario
   vuole esplicitamente − = ti allontani per il reticolo.

Riferimento visivo: **Outer Wilds** — parentesi attorno al corpo, chevron in alto, distanza+velocità
a lato, freccia tratteggiata prograde (screenshot Luna Quantica nella cronologia).

Note tecniche: la velocità relativa al bersaglio è già calcolata bene in
`RouteIndicator.RelativeVelocity` (sottrae la velocità-scena del bersaglio via `UniverseVelocityAt` ×
`TimeScale`). Texture procedurali generate UNA volta all'avvio (~KB, non per frame → nessuna
degradazione runtime; il bake-su-disco è il #13, per il load time). Su Metal+IMGUI le texture runtime
vanno bene (il quadrato era `SmoothStep`, non Metal); se mai servisse, alternativa = linee GL.

## Altri lavori in corso

- 🔄 **#8 Mappa**: cosmetico residuo — mostrare i corpi reali (cratered) invece di dischi uniformi.
- 🔄 **#6 Hand-off di gravità tra corpi**: funziona (gravità dal corpo più vicino + viaggio);
  resta da verificare ai limiti con più corpi.

## Prossimi (il GIOCO)

- ⬜ **#7 Più pianeti** nel sistema (es. Mercurio + Phobos con Stickney) — 2-3 corpi DIVERSI a mano.
- ⬜ **#10 Teletrasporto** istantaneo a un corpo selezionato (appoggiato al ri-ancoraggio; richiede
  corpi residenti → buildarli tutti all'avvio). Ordine richiesto: indicatore → autopilota → teletrasporto.
- ⬜ **#12 Autopilota** stile Outer Wilds (aggancia, allinea, match-velocity, avvicina).
- ⬜ **#9 Mini-loop giocabile (IL VERBO)**: atterra · cammina · raccogli · vai altrove · puoi fallire,
  su 2-3 corpi. È l'MVP.

## Igiene / infrastruttura

- ⬜ **#13 Bake procedurale → asset su disco** (comando editor `Wanderer → Bake assets`): genera
  una volta le texture procedurali (bake pianeta: normali crateri 1024²×6 + maschere; HUD opzionale)
  e le salva sotto `Resources/`; a runtime si caricano con fallback procedurale. Risparmia load time.
  Priorità: pianeta.
- ⬜ **#4 Unificare la verità crateri**: il campo C# (`CraterTerrainLayer`) e la formula HLSL del bake
  (`CraterNormalBake`) descrivono lo stesso cratere in due posti → rischio di divergenza.

## Più avanti (idee concordate, NON ora)

- Generazione pianeti da composizione chimica → ricetta → parametri (PRIMA 2-3 corpi a mano, POI la
  ricetta — trappola identica al quadtree se astrai troppo presto).
- Giganti gassosi / stelle come **volumi** (secondo renderer volumetrico raymarch), non mesh walkable.
- 6DOF pieno con roll: solo come modalità astronave, se mai servirà (per il viaggio attuale non serve).

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

## In corso

- 🔄 **#11 Indicatore di rotta** sul corpo selezionato (reticolo HUD: anello + chevron +
  distanza/velocità relativa + marker del vettore velocità; verde se sincronizzato; freccia al
  bordo se fuori vista). Implementato, **in test** — da rifinire grafica e committare.
  - Possibile problema da verificare: texture runtime via IMGUI su Metal (se il marker esce come
    quadrato → passare a disegno con linee GL).
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

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
- ✅ **#11 Indicatore di rotta — RIFINITO** (`RouteIndicator`): anello a parentesi nitido (smoothstep
  a mano) + alone, varchi ampi, pip ciano, chevron a casetta; anello a ~1.35× il disco (poco fuori);
  testo con ombra; dissolvenza ravvicinata sul raggio VERO; velocità solo in volo; **velocità di
  avvicinamento COL SEGNO** (− = ti allontani); stato **SINCRONIZZATO** (verde) a velocità relativa ~0.
  **Marker velocità a 2 (prograde ⊕ pieno + retrograde vuoto)** mappati sulla **velocità LATERALE**
  (perpendicolare alla rotta) × pixel/(m/s) — NON la direzione cruda (instabile vicino a 0): vicino
  allo zero restano al centro, niente sbando, controllo fine. Tratteggio di collegamento su entrambi.
  Verde "ALLINEATO" quando deriva laterale ~0 e ti avvicini.
- ✅ **Controlli di volo**: freno X (match velocity) forte e graduale vicino a 0; **rollio Q/E** in volo
  libero; spinta newtoniana più dolce (22 m/s², spool 1.8s) per assetto fine. Crociera invariata.
- ✅ **Re-ancoraggio origine senza scatto/frame nero**: al teletrasporto di switch-riferimento
  l'interpolazione del Rigidbody si spegne e si riaccende a buffer rinfrescato (`SolarSystem`).

Lezione (volo newtoniano puro, scelta di Dario): dopo il match-velocity un drift residuo CRESCE piano
mentre spingi — è FISICA (gravità del corpo vicino + accumulo se miri storto), non un bug. Si trimma
con prograde/retrograde. Azzerarlo del tutto = lavoro dell'**autopilota** (#12), non toccare la fisica.

## PROSSIMO: #12 Autopilota (Dario riparte da qui)

Stile Outer Wilds: tieni un tasto → la nave si allinea al bersaglio, fa match-velocity, accelera e
**frena all'arrivo**. È la soluzione "hands-off" al drift residuo del newtoniano. Ordine nel piano:
indicatore (fatto) → autopilota → teletrasporto. Mattoni già pronti: `RelativeVelocity`, freno X
graduale, velocità laterale/closing già calcolate nel `RouteIndicator`.

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

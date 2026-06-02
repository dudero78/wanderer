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
- ✅ **Controlli di volo**: **rollio Q/E** in volo libero; spinta newtoniana più dolce (22 m/s², spool
  1.8s) per assetto fine. **Match-velocity (X) tarata**: profilo dolce→rapido→dolce — spool anti-tap
  (`brakeRampTime`), forte nel mezzo, coda proporzionale leggibile vicino a 0 (`brakeKnee 40`,
  `brakeEaseTau 0.5`, `brakeFloor 5`). Crociera invariata.
- ✅ **Orbite a schermo (`O`)** (`OrbitDisplay`): linee delle orbite anche in volo; ellisse cacheata una
  volta, traslata ogni frame con la floating origin (niente solve per frame).
- ✅ **Re-ancoraggio origine senza scatto/frame nero**: `rb.interpolation = None` (a 30fps con fisica
  60Hz il moto resta fluido e i teletrasporti dello switch-riferimento sono sempre puliti).
- ✅ **Pianeta da lontano non più "fuzzy"**: la normale crateri si dissolve con la distanza
  (`_CraterFadeNear/_Far`, 2.5→9km) → oltre, sfera liscia ben illuminata. Niente impostor: a ~50px il
  pianeta è già quasi gratis (no trappola dell'ottimizzazione prematura).
- ✅ **Build standalone funziona** (prima nera): scena in Build Settings + shader `Wanderer/*` e built-in
  (`Standard`, `Unlit/Color`) in **Always Included Shaders** (i `Shader.Find` venivano strippati);
  emissivi (stella/tuta) su `Unlit/Color` (la variante `_EMISSION` dello Standard si strippa → scura).
  Guardie su `Shader.Find` null → la scena si carica con log, non va nera. HUD **scalato con la
  risoluzione** (Retina/4K) — prima a pixel fissi era minuscolo in build.
- ✅ **Load più veloce**: mesh d'appoggio del bake a bassa risoluzione (mask 64, crateri 48) — le texture
  restano a piena risoluzione (qualità identica). Resta ~1.9s di bake GPU (crater 1024²×6 + mips).

Lezione (volo newtoniano puro, scelta di Dario): dopo il match-velocity un drift residuo CRESCE piano
mentre spingi — è FISICA (gravità del corpo vicino + accumulo se miri storto), non un bug. Si trimma
con prograde/retrograde. Azzerarlo del tutto = lavoro dell'**autopilota** (#12), non toccare la fisica.

- ✅ **#12 Autopilota** (`T`, toggle) — hands-off completo verso il corpo selezionato (`PlanetWalker`).
  Orienta il muso al bersaglio (slerp con ease-out, `autoTurnTau`), pilota la velocità RADIALE con profilo
  "frena in tempo" BIDIREZIONALE `sign(dtg)·√(2·a·|dtg|)` (capato a `autoCruiseSpeed`, tetto largo → comanda
  il √, auto-dosato sulla tratta) + laterale desiderata = 0 → la **quota di sorvolo** (`autoHoverRadii` raggi
  sopra la superficie) è un EQUILIBRIO STABILE. Accel morbida (`autoAccel`), freno forte (`autoBrakeAccel`),
  autorità ≥1.6·g in entrambe → regge anche la stella. Il Δv si applica a `rb.linearVelocity` (identico in ogni
  riferimento inerziale). **Rampa di accelerazione** (`autoTransitTime`): gentile all'inizio, poi sale da
  `autoAccel` a `autoAccelMax` finché resti sullo stesso bersaglio → i viaggi lunghi accelerano in fretta;
  cambiare meta azzera. **Punto di sorvolo gravity-aware** (più esterno tra `autoHoverRadii` raggi e dove
  g locale scende a `autoHoverG`): su corpi pesanti ti fermi più in alto, hai tempo. **Profilo di frenata
  conservativo** (freno − g_superficie): non sfonda più tuffandosi verso la stella. **Arrivo** secondo
  l'impostazione: default DISINSERISCE a distanza di sicurezza; con "Autopilota stazionario" ON tiene l'hover
  (`AutoHolding`) finché non dai un comando. Si disinserisce anche atterrando o con `N`. **Gauge di frenata**
  HUD in volo libero manuale. Riusa `RelativeVelocityTo` (contabilità del `RouteIndicator`).
- ✅ **Schermata impostazioni a TAB** (`à`) — `SettingsMenu` + `GameSettings`. Banco di prova: slider che
  editano i campi LIVE del `PlanetWalker` (effetto immediato), ogni manopola persiste in PlayerPrefs. Tab:
  Autopilota / Volo / Camera. Toggle "Autopilota stazionario" (default OFF) via `GameSettings`. **Default
  originali catturati al primo avvio + "Ripristina default" per scheda** → si sperimenta senza paura.
  Estendibile: riga `F(...)`/`B(...)` in `Build()`. Futuro: rebinding tasti, volume, qualità.
- ✅ **Gauge di frenata auto-calcolata** — distanza di non ritorno onesta dai valori reali (spool freno +
  margine reazione + frenata erosa dalla gravità `aEff = brakeAccel − g`): arriva in tempo, da non ritoccare.
  Compare solo oltre `WarnMinClosing` (~50 m/s): avviso da viaggio, non per volo radente / saltelli / manovra fine.

## PROSSIMO: #10 Teletrasporto / #7 più pianeti (Dario riparte da qui)

Ordine del piano: indicatore (fatto) → autopilota (fatto) → teletrasporto / più corpi. Il teletrasporto
richiede corpi residenti (buildarli tutti all'avvio); più pianeti dà materiale vero da raggiungere.

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

- ✅ **#13 Bake procedurale → asset su disco** — comando editor **`Wanderer → Bake planet assets`**
  (`Editor/PlanetBakeTool`): bakea offline a piena risoluzione (mask mesh 256, crateri 200) le 13 texture di
  superficie (mask×6 + crater normal×6 + detail) e le salva in `Assets/Resources/BakedPlanet`. A runtime
  `PlanetBaker.TryLoadBakedMaterials` le carica → avvio quasi istantaneo, niente ~1.9s di bake GPU.
  **OPT-IN e sicuro**: senza la cartella il gioco usa il bake runtime (fallback, invariato); per tornare
  indietro cancella la cartella; ri-lancia il comando se cambi `PlanetPresets`. Parametri terreno condivisi
  in `PlanetPresets.ConfigureDemoPlanet` (scena + bake una sola verità). **DA PROVARE: lanciare il comando,
  verificare load veloce e che la superficie sia identica.**
- ⬜ **#4 Unificare la verità crateri**: il campo C# (`CraterTerrainLayer`) e la formula HLSL del bake
  (`CraterNormalBake`) descrivono lo stesso cratere in due posti → rischio di divergenza.

## Più avanti (idee concordate, NON ora)

- Generazione pianeti da composizione chimica → ricetta → parametri (PRIMA 2-3 corpi a mano, POI la
  ricetta — trappola identica al quadtree se astrai troppo presto).
- Giganti gassosi / stelle come **volumi** (secondo renderer volumetrico raymarch), non mesh walkable.
- 6DOF pieno con roll: solo come modalità astronave, se mai servirà (per il viaggio attuale non serve).
- **Proiezione non-rettilineare (stereografica/Panini)** come post-process: tiene i corpi TONDI anche a FOV
  ampio (la deformazione ai bordi è inerente alla proiezione rettilineare, non riducibile a FOV fisso). In un
  gioco spaziale ha quasi solo vantaggi (poche linee rette da incurvare). Rimandata: per ora il FOV è la leva
  (default 52°, slider 35–80 nel menù à). Si farà col look definitivo, non ora (priorità al GIOCO).

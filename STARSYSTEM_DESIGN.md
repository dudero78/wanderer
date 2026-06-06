# STARSYSTEM_DESIGN.md

## Tesi

La galassia non si costruisce: si **incapsula** il sistema che già esiste in un contenitore chiamato `StarSystem`, in modo che **con un solo sistema il comportamento sia identico bit-per-bit a oggi**. Tutto il resto — scoping dell'attività, sonno/risveglio dei sistemi, transizione interstellare — si scrive **solo quando esiste davvero un secondo sistema da accendere**, perché prima quel codice non è nemmeno collaudabile (il ramo "dormiente" non gira mai).

Il fondamento è già al suo posto: `FloatingOrigin.SceneOrigin` vive in `double`, e una galassia ci sta dentro senza perdita di precisione. Il problema non è la matematica delle coordinate: è lo **scoping** — decidere *cosa è attivo* — e un paio di **vincoli duri nel pool VRAM** che oggi sono invisibili solo perché tutti i corpi nascono e muoiono insieme.

Il `StarSystem` giusto è la fusione delle tre proposte:
- è un **contenitore-dato** (lista dei suoi corpi + la sua stella) — dal 1º architetto;
- ha uno **stato di attività** {Dormiente, Attivo} e un solo sistema vivo per volta — dal 2º;
- ha un **frame** `Vector3d SystemOrigin` (posizione della stella nello spazio-galassia, in double) su cui si compongono le orbite Kepler relative — dal 3º.

Niente "manager di sistemi" elaborato, niente generatore procedurale, niente netcode. Il principio di CLAUDE.md ("astrai il nodo-sistema SOLO quando serve") governa l'intero piano: la Tappa 1 introduce solo il NOME e il contenitore, a comportamento immutato.

---

## Cosa è GLOBALE vs cosa è PER-SISTEMA

| Resta GLOBALE (uno solo, sopravvive alle transizioni) | Perché |
|---|---|
| `FloatingOrigin.SceneOrigin` (double) | Una sola origine-universo. La galassia ci sta. È già il fondamento giusto. |
| `SimTime` / `TimeScale` | **Un solo orologio per tutta la galassia**: le orbite di sistemi diversi restano deterministiche e in fase. È il regalo dell'on-rails Kepler — non sprecarlo con stato integrato per-sistema. |
| Il pool VRAM (`SlabPool`, statico/refcountato) | È una risorsa **hardware**, non logica di sistema. Un solo corpo è attivo per volta → 1×~843 MB serve N sistemi quanti 1. |
| Player/Rigidbody, camera, HUD/UI | A un dato istante il giocatore è in UN posto. |
| `SunLight.Instance` (la direzionale) | C'è sempre **una sola** stella che illumina il giocatore (è in un solo sistema). Resta singleton; cambia solo *quale* stella legge. |
| `Reference`, `Anchor`, `Destination`, `currentAnchor`, `traveling`, `lastNearest` | **Stato del GIOCATORE**, non del sistema. A quale corpo è ancorata l'origine ADESSO. Restano in `SolarSystem`. Spostarli in `StarSystem` è l'errore che rompe il viaggio interstellare (l'ancora vive FRA i sistemi). |

| Diventa PER-SISTEMA (`StarSystem`) | Note |
|---|---|
| La lista `Bodies` di quel sistema | Oggi è l'unica lista; con N>1 ognuno ha la sua. |
| La stella (`Star`) | Oggi scoperta come "corpo senza orbita"; diventa un campo del sistema. |
| La composizione (l'array `Orbiting[]`) | Diventa il **dato** di QUEL sistema (una `SystemRecipe`), non più `static`. |
| `Vector3d SystemOrigin` | Posizione della stella nello spazio-galassia. **Oggi = `Vector3d.Zero` → caso degenere.** |
| Stato `Active` | Un solo sistema `Active` per volta. |

**Astrazione minima:** `StarSystem { string Name; Vector3d SystemOrigin; CelestialBody Star; List<CelestialBody> Bodies; bool Active; }`. `SolarSystem.Instance` NON sparisce: diventa l'orchestratore che possiede `List<StarSystem> Systems` + `StarSystem Active`, mantenendo l'API che il resto del gioco già consuma (`Bodies`, `Reference`, `Register`, `Step`).

**Regola d'oro delle coordinate (vale due volte, annidata):** la posizione-universo di un corpo = `SystemOrigin (galassia, double) + UniversePosition-nel-sistema (double)`, sommate **sempre in double** prima di sottrarre `SceneOrigin`. Con `SystemOrigin = Zero` è l'identità esatta di oggi.

---

## Interest management (scoping dell'attività) — RINVIATO, ma non precluso

Con N=1 **non serve nessuna decisione attivo/dormiente**. Il loop O(N) su pochi corpi costa ~0 (il profilo dice GPU 95% scarica, collo = main thread quasi vuoto). Introdurre ORA pool-per-prossimità, sistemi dormienti o gating della gravità è esattamente l'over-engineering che CLAUDE.md vieta — e per giunta è codice non testabile (il ramo "dormiente" non gira mai con un solo sistema).

La **regola che abilita il rinvio senza precludere il futuro**: già da Tappa 1, lo `Step` itera concettualmente "i corpi del sistema attivo" (`Active.Bodies`), non una lista globale anonima. Il giorno del 2º sistema, il punto d'innesto dell'interest-management è **una riga** (`if vicino al SystemOrigin di un altro → promuovilo`), non una riscrittura.

Quando servirà (Tappa di accensione), il modello è **"un solo sistema Attivo per volta"**:
- **Attivo** = i suoi `Bodies` sono in `Active.Bodies` → partecipano a Step/gravità/ancora/render/eclissi; i loro `GpuPlanetRenderer` esistono e consumano fette del pool.
- **Dormiente** = nessuno Step, nessun corpo istanziato, nessun renderer, **zero fette nel pool**. Esiste solo lo struct `StarSystem` con il suo `SystemOrigin` e la sua `SystemRecipe` (kB di dati).
- **Criterio di sveglia:** distanza-galassia del player dal `SystemOrigin` vs un raggio-di-sistema (es. ~3× l'orbita più esterna), **con isteresi** (banda morta come il "takeoff" già collaudato in `NearestBody`) → niente flip-flop sul bordo che farebbe lampeggiare la stella e ri-streammare le fette ogni frame.

Dentro il sistema attivo, l'interest esistente resta intatto: `NearestBody`+isteresi per l'ancora, early-out sub-pixel del renderer, horizon culling, LOD predittivo.

---

## Pool VRAM e bodyId — l'UNICO vincolo DURO

Qui sta il motivo per cui **Tappa 1 e 2 non toccano il pool**. Due bug sono latenti e diventano reali solo col multi-sistema:

1. **`RegionId(nd) = pool.BodyId * 1048576 + …` è un FLOAT** (`PlanetLodTree.cs:150`, region-stamp anti-spuntone confrontato nel vertex shader). Mantissa float = 24 bit → `2^24 / 2^20 ≈ 16` → l'invariante regge **≤7 corpi VIVI con BodyId distinti esatti** (margine già documentato nel codice: "≤7 corpi in 2^23"). Oltre, il region-stamp si corrompe → falsi spuntoni / purge che non matchano.

2. **`sNextBodyId++` è MONOTONO** (`SlabPool.cs:98`) e si azzera solo a refcount 0 (`:159`). Oggi va bene: tutti i corpi nascono/muoiono insieme col lifecycle di scena. Ma con sonno/risveglio ripetuti il contatore cresce senza limite → in pochi salti supera 7 → bug 1. In più la cache LRU si riempie di regioni di sistemi spenti → leak.

**La cura è strutturale, non "più bit":**
- **Riciclare il BodyId** via free-stack nel pool: `Release` fa `push` del BodyId, il costruttore fa `pop` (o `++` se vuota). Così l'occupazione è "corpi VIVI contemporaneamente" (≤ corpi del sistema attivo, ben sotto 7), non il totale storico. L'id resta piccolo → l'invariante `<16` vale sempre.
- **Purgare la cache all'uscita di un corpo**: il meccanismo **esiste già** — `OnDestroy → tree.ReturnSlabs()` fa `FreeRaw` dei nodi vivi + `pool.PurgeCache(k => (k>>40)==BodyId)` (`PlanetLodTree.cs:314`). Addormentare un sistema = `Destroy` dei suoi renderer = **il percorso single-body già testato**, eseguito N volte.

Il pool fisico (statico, refcountato) **non si dealloca** finché resta almeno un corpo vivo: il sistema nuovo nasce mentre il vecchio muore → refcount non tocca 0 → niente realloc dei ~843 MB. Invariante condiviso da rispettare: **tutti i corpi di tutti i sistemi hanno lo stesso `vertsPerSlab`/`nodeRes`** (il pool è condiviso) — già un assert nel costruttore.

**Nessuna di queste due va fatta in Tappa 1.** Il riciclo BodyId (Tappa 2) è un no-op esatto a N=1 (gli slot 0..6 si pescano in ordine come oggi) e va messo PRIMA che il multi-sistema possa innescare il bug. Il purge all'uscita è già scritto: si attiva da solo quando un corpo viene davvero distrutto a runtime.

---

## Scioglimento dei singleton — additivo, senza toccare i chiamanti

I tre singleton hanno nature diverse; nessuno va distrutto, vanno **ri-puntati al sistema attivo**.

- **`SolarSystem.Instance`** non è "il sistema solare", è di fatto **il contesto/sessione del gioco** (orologio + player + Reference). Resta. Acquisisce `List<StarSystem> Systems` + `StarSystem Active`. `Bodies`/`Register`/`Reference` continuano a funzionare invariati per tutti i consumatori (MapMode, OrbitDisplay, PlanetWalker, EclipseDriver, RouteIndicator). **Dettaglio cruciale di robustezza** (confermato leggendo il codice): `Bodies` è oggi un `public List<CelestialBody>` mutato da `Register` e iterato **per indice** da 5+ consumatori. Per garantire identità N=1 senza rischio di divergenza, `Active.Bodies` deve essere **la stessa istanza di lista** di `SolarSystem.Bodies` (un riferimento condiviso, non un alias copiato): `Register` continua ad aggiungere a quell'unica lista, e `Active.Bodies` la riferisce. Zero possibilità di viste divergenti.

- **`SunLight.Instance`** resta singleton (la direzionale è una sola, ha senso anche con N sistemi). Cambia solo *cosa* legge: oggi ha campi `star`/`planet` cablati una volta in `LightingSetup`; diventano riassegnabili via un metodo `Retarget(starTf, planetTf)` chiamato dalla promozione di sistema. `GpuPlanetRenderer.RefreshLighting` legge già `SunLight.Instance` → gratis.

- **`EclipseDriver`** oggi fa `Init(solar, sun)` scoprendo la stella ("corpo senza orbita") e i rocky una volta. Diventa **ri-inizializzabile**: un `Rebuild()` che azzera `rocky[]`/`star` e li riscopre da `Active.Bodies`. Con N=1 e `Active` sempre = l'unico sistema, il comportamento è identico; chiamato una volta sola allo startup come oggi, solo reso ri-chiamabile.

- **"Una stella"** in `SolarSystemSetup`: l'assunzione da sciogliere è "una stella **nell'universo**" → "una stella **nel sistema corrente**". Ogni `StarSystem` ha la sua `Star` (corpo senza orbita all'origine del proprio frame). Non è un vincolo da rompere: è la definizione del nodo.

Passo conservativo: **l'API pubblica di tutti questi non cambia**, cambia solo CHI viene puntato.

---

## Transizione interstellare (viaggio fra sistemi)

Rinviata come feature, ma il design è già non-precluso. È una **promozione/retrocessione di sistema**, costruita sopra il meccanismo di ri-ancoraggio fra corpi che **esiste già** in `SolarSystem.Step` (blocco `currentAnchor != target`, `:101-118`): trasla player+Loose per restare nello stesso punto-universo e corregge la velocità della differenza dei due frame. Il viaggio interstellare è **lo stesso atto un livello sopra**.

- **Dentro un sistema:** tutto invariato.
- **Uscendo (oltre il raggio-di-sistema, con isteresi):** entri nel "vuoto interstellare". Il sistema corrente NON si addormenta subito (isteresi, come il takeoff) → la stella non sparisce sul bordo. Nessun corpo ancorabile → l'ancora diventa la nave stessa in coord-galassia (floating-origin pura: la nave ferma a ~0, la galassia scorre). La direzionale può affievolirsi (spazio profondo).
- **Avvicinandoti al `SystemOrigin` di destinazione:** (1) **svegli** il sistema bersaglio (`Build` dei corpi dalla sua `SystemRecipe`, `Setup` dei renderer che pescano BodyId riciclati), col frame a `SystemOrigin`; (2) lo **promuovi** ad `Active` → `SceneOrigin` si ri-ancora a un suo corpo riusando lo switch d'ancora esistente (preserva la velocità-universo); (3) `SunLight.Retarget` alla nuova stella, `EclipseDriver.Rebuild` sui nuovi corpi; (4) **retrocedi** il vecchio sistema → `Destroy` dei renderer (ritorna fette+BodyId). **Handover sequenziale, non sovrapposto** (distruggi il vecchio prima/durante la nascita del nuovo) per non superare mai 7 corpi vivi.
- **Stella illuminante:** cambia con `Active` (una sola direzionale). **Eclissi:** l'`EclipseDriver` riparte da zero (rocky[] ricostruito), nessuna eclissi cross-sistema (corretto: i sistemi sono lontanissimi). Nel vuoto, rocky[] vuota → nessun calcolo.

**Galassia STATICA** (i `SystemOrigin` non derivano nel tempo): semplifica la correzione di velocità allo switch e resta coerente con le orbite on-rails. Da fissare come scelta.

---

## ROADMAP A TAPPE

> **STATO: Tappe 1-2-3-4-5 IMPLEMENTATE** (vedi `REPORT_SESSIONE_AUTONOMA.md`). Differenza voluta dal design originale:
> il **limite di corpi è sparito** (region-stamp uint), quindi il sistema-CASA resta **residente** invece di essere
> distrutto/ricostruito — i sistemi DISTANTI si svegliano/addormentano additivamente per prossimità (`BuildSystem`/
> `DestroySystem` in `SolarSystemSetup`, interest in `SolarSystem.UpdateInterstellar`, isteresi ×1.4). Più semplice e
> robusto del "un-solo-attivo" stretto, e round-trip senza ricostruire la casa. Galassia = `SolarSystemSetup.Galaxy`
> (Casa + Helios + Vega). Tappa 5: billboard delle stelle distanti in `MapMode` a zoom galattico. Da collaudare a
> gioco aperto (la transizione interstellare è ora costruibile e testabile: vola verso Helios/Vega).

Ogni tappa è piccola, additiva, verificabile, e dà valore. Le prime due sono `risk: low` e non cambiano il comportamento; la galassia vera nasce solo dalla Tappa 4.

### Tappa 1 — `StarSystem` come contenitore, N=1 = identità  · risk: low
**Cosa:** nuovo tipo `StarSystem` (contenitore-dato: `Name`, `Vector3d SystemOrigin = Zero`, `CelestialBody Star`, `List<CelestialBody> Bodies`, `bool Active`). `SolarSystem` acquisisce `List<StarSystem> Systems` + `StarSystem Active`; `Active.Bodies` **riferisce la stessa istanza** di `SolarSystem.Bodies`; `Register` resta invariato (popola quell'unica lista). `SolarSystemSetup.Build` crea UN `StarSystem`, vi mette stella+corpi, lo registra come unico+attivo. `Step`/`NearestBody`/i consumatori continuano a leggere `Bodies` invariato.
**File:** `Physics/StarSystem.cs` (NUOVO, ~25 righe); `Physics/SolarSystem.cs` (campi `Systems`/`Active` + cablaggio in `Awake`/`Register`); `Bootstrap/SolarSystemSetup.cs` (`Build` avvolge stella+`Orbiting[]` in uno `StarSystem`, lo assegna ad `Active`).
**Valore:** esiste il nome/contenitore su cui appendere tutto il resto; appiglio mentale "i corpi appartengono a un sistema". Diff minimo.
**Verifica:** il gioco gira identico (stesso spawn, stessa mappa, stesse orbite, stesse eclissi); `Active != null` e `Active.Bodies == Bodies` (stessa reference).

### Tappa 2 — bodyId da contatore monotono a slot riciclabile  · risk: low
**Cosa:** in `SlabPool`, `sNextBodyId++` → una free-stack di id `0..6`; `Release` ridà lo slot. Assert `BodyId < 7`. `CelestialBody` conosce il suo sistema (`StarSystem System`) e `UniversePosition` diventa `System.SystemOrigin + keplerRelative` (con `Origin=Zero` è identità esatta — verificare parità). `SunLight`/`EclipseDriver` leggono `Active.Star`/`Active.Bodies` (resi ri-chiamabili: `Retarget`/`Rebuild`), chiamati una volta sola come oggi.
**File:** `World/SlabPool.cs` (free-stack BodyId); `Physics/CelestialBody.cs` (campo `System` + somma `SystemOrigin`); `World/SunLight.cs` (`Retarget`); `World/EclipseDriver.cs` (`Rebuild`); `Bootstrap/LightingSetup.cs` (usa i nuovi metodi).
**Valore:** chiude i due bug latenti (RegionId>2^23 e leak di cache da identità monotona) e l'assunzione "una stella nell'universo", PRIMA che il multi-sistema possa innescarli. Costo zero a N=1.
**Verifica:** a N=1 gli slot 0..6 si pescano in ordine come oggi; parità altezza C#↔HLSL intatta (gate di parità all'avvio); gioco identico.

### Tappa 3 — composizione per-sistema (`SystemRecipe`), definire 2-3 sistemi senza accenderne più d'uno  · risk: medium
**Cosa:** estrarre l'array `Orbiting[]` da `static` a un dato per-sistema (`SystemRecipe`: stella + corpi + `SystemOrigin`); `Build(recipe)` costruisce QUALUNQUE sistema. La galassia = array di 2-3 `SystemRecipe` hardcodati (modo Carmack: dati a mano, non generatore). All'avvio si costruisce ancora **solo** il sistema-casa.
**File:** `Bootstrap/SolarSystemSetup.cs` (`SystemRecipe` + `Build(recipe)`); `Bootstrap/GameBootstrap.cs` (registra i recipe dei sistemi). `BodyBakeTargets` resta per-sistema.
**Valore:** si possono DEFINIRE più sistemi (e vederli come puntini lontani nella mappa) senza ancora viaggiarci. Il sistema-casa è ancora identico.
**Verifica:** sistema-casa invariato; un 2º `SystemRecipe` con `SystemOrigin` lontano esiste come dato e appare come stella-billboard in mappa, nessun corpo istanziato.

### Tappa 4 — Sleep/Wake + transizione interstellare (qui nasce la galassia)  · risk: high
**Cosa:** interest L1 in `SolarSystem.Update`: distanza-galassia player↔`SystemOrigin`, promozione per prossimità **con isteresi**. Promozione = `Build(recipe)` + `Setup` renderer (pesca BodyId riciclati) + `SunLight.Retarget` + `EclipseDriver.Rebuild` + ri-ancora `SceneOrigin` riusando lo switch d'ancora esistente (preserva velocità). Retrocessione = `Destroy` renderer (`ReturnSlabs`+`Release` già corretti). Handover **sequenziale** (≤7 corpi vivi sempre). `Active` segue il `NearestBody` che appartiene a un altro sistema.
**File:** `Physics/SolarSystem.cs` (Promote/Demote + interest + stato "fra i sistemi"); `World/SunLight.cs`/`EclipseDriver.cs` (chiamati allo switch); `Bootstrap/GameBootstrap.cs`.
**Valore:** multi-sistema VERO e navigabile, costruito **interamente sopra meccanismi già testati** (ri-ancoraggio, re-streaming single-body). Prima esperienza interstellare end-to-end.
**Verifica:** voli da un sistema all'altro; fette/BodyId si riciclano (assert `<7` mai violato); niente spuntoni, niente leak di cache, illuminazione/eclissi/ancora corrette; nessun flip-flop sul bordo.

### Tappa 5 — rifiniture (presentazione, non scoping)  · risk: medium
**Cosa:** isteresi della stella nel vuoto (non sparisce sul bordo); mappa galattica (zoom-out oltre il sistema mostra i nuclei come stelle); 1-2 sistemi vicini in "stato proxy" render-only (stella visibile da lontano, **zero corpi/fette/BodyId**). Eventuale spalmatura del `Build` su più frame per togliere l'hitch al primo ingresso (prima fallo corretto, poi liscio).
**File:** `Player/MapMode.cs` (livello galattico); `World/SystemProxy.cs` (NUOVO, stella lontana); `Physics/SolarSystem.cs` (stato proxy).
**Valore:** resa e leggibilità del viaggio. Non tocca scoping/pool: è solo presentazione.
**Verifica:** la stella non lampeggia sul bordo; in mappa si vedono i sistemi vicini; i proxy non consumano fette.

---

## Rischi e mitigazioni (riepilogo)

1. **Over-scoping in Tappa 1** (spostare Reference/Anchor/Destination in `StarSystem`). Sono stato del GIOCATORE → restano in `SolarSystem`, altrimenti il viaggio interstellare si rompe (l'ancora vive FRA i sistemi).
2. **Vincolo float di RegionId (≤7 corpi vivi).** Invisibile finché i corpi nascono/muoiono insieme; bug silenzioso (spuntoni, purge mancati) appena si fa streaming. Cura = **riciclo BodyId** (Tappa 2), non più bit. Assert duro `BodyId < 7`. Se un sistema avesse >7 corpi, o attivo+proxy superassero 7, va allargato il RegionId (più bit → meno esatto in float, o `uint` nel vertex shader).
3. **Divergenza della lista `Bodies`.** È letta per indice da 5+ consumatori. `Active.Bodies` DEVE essere la stessa istanza di `SolarSystem.Bodies` (riferimento condiviso) finché c'è un solo sistema → zero viste divergenti.
4. **Un solo `SimTime` globale.** Giusto (determinismo, fase). Se un domani i sistemi distanti devono "congelarsi" per perf, **NON congelare il tempo** — congela solo l'esecuzione dello `Step` su quel sistema: le orbite restano funzione analitica di `SimTime`, quindi riaccendere è esatto, niente deriva (regalo dell'on-rails Kepler).
5. **Flip-flop sul bordo di sistema.** Isteresi spaziale (banda morta come `NearestBody`/takeoff) sulla promozione/retrocessione.
6. **Hitch di promozione** (Build+Setup+bake di un intero sistema). Mitigabile spalmando il Build su più frame o pre-warmando in zona-proxy (Tappa 5), NON in Tappa 4.
7. **`Reference` null nel vuoto interstellare.** I consumatori (HUD/walker) devono tollerare l'ancora-su-punto-nave; il ramo `Anchor` probabilmente già copre, ma da verificare.
8. **Precisione galattica.** `SystemOrigin + UniversePosition` sempre sommati in double prima del cast a float (disciplina "conversione in un punto solo", già principio del progetto). Galassia STATICA per semplificare la correzione di velocità allo switch.

---

# Appendice — Tappa 1 in dettaglio (pronta da implementare)

# Tappa 1 — `StarSystem` come contenitore, N=1 = identità

## Obiettivo
Introdurre il NOME e il CONTENITORE `StarSystem` senza cambiare di una virgola il comportamento attuale. Dopo questa tappa il gioco gira identico: stesso spawn, stessa mappa, stesse orbite, stesse eclissi. È il prerequisito non rischioso su cui poggiano tutte le tappe successive.

## Cosa cambiare, file per file

### 1. NUOVO — `Physics/StarSystem.cs` (~25 righe)
Un contenitore-dato puro, nessuna logica:
- `string Name;`
- `Vector3d SystemOrigin = Vector3d.Zero;` — posizione della stella nello spazio-galassia. Oggi Zero = caso degenere (identità esatta con il comportamento attuale).
- `CelestialBody Star;` — la stella del sistema (oggi scoperta come "corpo senza orbita"; qui diventa un campo esplicito).
- `List<CelestialBody> Bodies;` — i corpi del sistema.
- `bool Active;` — un solo sistema attivo per volta (con N=1 sempre true).

Nessun metodo, nessun MonoBehaviour: è un oggetto-dato che `SolarSystem` possiede.

### 2. `Physics/SolarSystem.cs`
- Aggiungere `public List<StarSystem> Systems = new List<StarSystem>();` e `public StarSystem Active;`.
- **Punto load-bearing per la sicurezza N=1:** `Active.Bodies` deve essere **la stessa istanza di lista** del campo `Bodies` già esistente, non una copia. Oggi `Bodies` è un `public List<CelestialBody>` mutato da `Register` (`:48`) e iterato **per indice** da 5+ consumatori (`Step`, `NearestBody`, EclipseDriver, OrbitDisplay, PlanetWalker, MapMode). Se `Active.Bodies` riferisce quella stessa istanza, `Register` continua a popolarla e non può esistere alcuna vista divergente.
- `Register(b)` resta invariato (continua ad aggiungere all'unica lista `Bodies`).
- `Step`, `NearestBody` e tutti i consumatori restano invariati: leggono ancora `Bodies`. Concettualmente quella lista È ora `Active.Bodies`, ma nessun codice esistente deve cambiare riga.

### 3. `Bootstrap/SolarSystemSetup.cs`
- In `Build(...)`: creare un `StarSystem` (Name es. "Casa", `SystemOrigin = Vector3d.Zero`), assegnargli `Star = star` e `Bodies = solar.Bodies` (la stessa istanza), `Active = true`.
- Registrarlo: `solar.Systems.Add(sys); solar.Active = sys;` — fatto **dopo** la creazione della stella e prima/durante la registrazione dei corpi, in modo che la lista riferita sia già quella che `Register` popola.
- Tutto il resto di `Build` (stella, pianeta-casa, `Orbiting[]`, baricentro, ancora di spawn, `solar.Step()`) resta identico. Opzionalmente `Built` può esporre il `StarSystem` creato, ma non è necessario per questa tappa.

## Perché è sicura (N=1 invariato)
- `Bodies` resta l'unica lista autoritativa, la stessa istanza fisica di prima. `Active.Bodies` la riferisce: nessuna copia, nessun alias da tenere sincronizzato, nessuna possibilità di divergenza.
- Nessun consumatore cambia: `Step`, `NearestBody`, EclipseDriver, OrbitDisplay, PlanetWalker, MapMode continuano a leggere `solar.Bodies` per indice esattamente come oggi.
- `SystemOrigin = Zero` → nessuna somma di offset entra ancora in gioco (quella arriva in Tappa 2, e anche lì è identità a Origin=Zero).
- Pool VRAM e BodyId **non vengono toccati**: il vincolo float di RegionId e il contatore monotono restano esattamente come oggi (corretti finché i corpi nascono/muoiono insieme, cosa che in Tappa 1 non cambia).
- Reference/Anchor/Destination **restano in `SolarSystem`** (stato del giocatore): non vengono spostati nel contenitore — sarebbe l'over-scoping che romperebbe il futuro viaggio interstellare.

## Come si verifica
1. **Compila e avvia** (`Wanderer → Crea scena di gioco` → Play). Spawn sul corpo di test (`spawnOnBody`, default "Valentina2") identico a prima.
2. **Mappa (M):** stessi corpi, stesse orbite, stessa scia.
3. **Orbite (O)** e **eclissi:** invariate (EclipseDriver legge ancora `solar.Bodies`, che è la stessa lista).
4. **Volo/ancora:** decolla, vola fra corpi, atterra — il ri-ancoraggio e il match-velocity (X) si comportano identici.
5. **Assert/log di sanità** (rimuovibili dopo la verifica): all'avvio `solar.Active != null`, `solar.Active.Bodies` è **reference-equal** a `solar.Bodies` (`ReferenceEquals`), `solar.Active.Star` è la stella, `solar.Systems.Count == 1`.
6. **Parità altezza C#↔HLSL** (gate all'avvio): invariata — la Tappa 1 non tocca la generazione del terreno.

Nessun comportamento osservabile deve cambiare. Se qualcosa cambia, è un bug della tappa, non una feature.
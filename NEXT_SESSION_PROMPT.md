# Wanderer — PROSSIMA SESSIONE (piano dettagliato)

Aggiornato: **6 giugno 2026**, fine sessione UX (mappa/sonda/menu/interstellare/loading).

> **Direttiva di fase (Dario, 6 giu):** stiamo costruendo le BASI → vanno **ultra solide**. *"Rifai tutto quello che
> serve, non c'è nulla a cui siamo affezionati, vogliamo solo la perfezione; se serve riscrivere, si riscrive."*
> Niente pezze. Solo molto più avanti si inizierà a preservare l'esistente. (Vedi memoria `riscrivere-per-perfezione`.)

I due blocchi sotto sono **concordati con Dario** e vanno fatti **in quest'ordine**.

---

## 1) MAPPA MULTI-SISTEMA — riscrittura con SPAZIO-MAPPA LOCALE

### Perché
Oggi la mappa disegna i corpi **alle loro posizioni-mondo reali** (scene coords ancorate a `FloatingOrigin.SceneOrigin`).
Per il sistema-casa va bene (vicino allo zero), ma un sistema distante è a **~6 milioni di metri** → il `float` della
camera-mappa lì **trema**. Da qui i bug visti da Dario:
- **drift di zoom** quando zoomma su Vega;
- **rotazione "sballata"** vicino a Vega (scala/centra ancora sul sistema-casa, perché `SystemCenter()/SystemRadius()`
  sono home-based e iterano `solar.Bodies`);
- **non si vedono gli altri sistemi**: oggi un sistema distante si mostra in mappa solo da SVEGLIO (corpi reali nel mondo);
  Dario vuole vederli **TUTTI**, anche **statici**, senza caricarli nel mondo (abbiamo i dati nei `SystemRecipe`).

### In cosa consiste (la soluzione, NON una pezza)
Dare alla mappa un **suo spazio di coordinate LOCALE**: la mappa diventa un **rendering di PROXY** costruiti dai DATI
(posizioni-universo dei corpi vivi + i `SolarSystemSetup.SystemRecipe` per i sistemi dormienti), centrati su un'
**origine-mappa** (`Vector3d`) che segue il fuoco. Tutto si disegna a `(posUniverso − origineMappa)` → coordinate sempre
piccole → **precisione perfetta a qualunque distanza**.

Componenti:
- **Proxy statici da recipe** per OGNI sistema (anche dormiente): per ogni `OrbitBody` calcolo la posizione con
  `KeplerOrbit.GetRelativePosition(SimTime)` relativa a `SystemOrigin` (immobili = "statici", coerente con on-rails: a
  questa distanza non vediamo il moto, ha senso in-game). Disco colorato + anello d'orbita; clic = waypoint del sistema.
  (I corpi del sistema-casa / dei sistemi SVEGLI restano i loro corpi veri, ma anch'essi disegnati in spazio-mappa locale.)
- **Origine-mappa** = il `SystemOrigin` del sistema più vicino al fuoco (o il fuoco stesso). `SystemCenter()/SystemRadius()`
  diventano **relativi al sistema in vista**, non a `solar.Bodies` globale → centra/scala correttamente su Vega/Helios.
- **Camera a ORBITA LIBERA** (refactor richiesto da Dario): oggi la camera-mappa punta SEMPRE il focus (`ComputeOverview`
  fa `LookRotation(focus−pos)`), quindi mettere il pivot sul punto cliccato la fa **ri-mirare = snap**. Riscrivere la
  camera-mappa come **orbita attorno a un pivot arbitrario** (pivot ≠ bersaglio-di-sguardo): rotazione col destro che
  orbita attorno al punto cliccato **senza snap**; pan e zoom-verso-cursore restano. Il pivot/click NON deve mai far
  saltare la vista.

### Cosa comporta
Riscrittura sostanziosa di `MapMode` (è il momento giusto: niente da preservare). Toccare: `BuildBodyVisuals`/
`RebuildBodyVisuals` (→ build proxy per tutti i sistemi), `UpdateVisuals` (posizioni in spazio-mappa locale),
`ComputeOverview` (camera a orbita libera), `SystemCenter/SystemRadius/ClampFocus` (relativi al sistema in vista),
`SelectAtCursor` (selezione corpi anche dei sistemi statici → waypoint).

---

## 2) ARCHITETTURA A SCENE + PREFAB + LOADING ASYNC

### Perché
Il gioco è **costruito da codice** (`GameBootstrap.Start` crea tutto a runtime): non c'è un file `.unity` con oggetti/asset
pre-posati (solo le texture bakeate in `Resources`). Conseguenze:
- **Loading da ~1 minuto** in build (Dario): NON è il caricamento dei corpi (log build: 5–8 ms a corpo) — è la
  **compilazione di shader/compute + upload texture alla PRIMA RESA**, tutto in un colpo sul primo frame → lo spinner si
  freeza. (In editor avviene a ogni Play per il domain-reload; la compilazione della pipeline COMPUTE su Metal è sincrona
  sul main thread → l'unico pezzo davvero irriducibile.)
- Dario vuole esplicitamente una **"scena con asset"** (autorabilità, e per intrattenere durante il loading).

### In cosa consiste
1. **Prefab-izzare** ciò che oggi si crea da codice: un prefab *Pianeta* (CelestialBody + PlanetTerrain + renderer), un
   prefab *Player*, *Tuta*, *Sonda*, ecc. Le **ricette** (`PlanetRecipe`/`SystemRecipe`) diventano **ScriptableObject**
   referenziati dai prefab (oggi `SystemRecipe` è già un oggetto-dato → conversione naturale).
2. **Scena Loading** leggerissima (solo lo spinner `LoadingScreen`) come scena d'avvio → `SceneManager.LoadSceneAsync`
   della scena di Gioco **in background** (deserializzazione + warm-up su thread worker di Unity) mentre la scena Loading
   **gira liscia**. Aggiungere un **warm-up esplicito degli shader** (`ShaderVariantCollection`) durante il caricamento.
3. La scena di Gioco contiene la composizione (o la istanzia da una lista di prefab) invece di costruirla a mano.

### Cosa comporta
Migra la regìa da `GameBootstrap` (build da codice) a scena+prefab. **Onestà:** il pezzo COMPUTE (compile pipeline
Metal) resta sincrono; ma con le scene il grosso (asset + shader grafici) va in background e lo spinner si anima — quel
residuo lo isoli dietro lo spinner. È *il* modo giusto e diventa una base riusabile.

---

## Coda minore (dopo i due blocchi)
- 3 bug editor pianeti ancora aperti (mare-non-allaga ecc.) — vedi REPORT precedenti.
- Eventuale rename `SolarSystem` → `Universe`/`Simulation` (è il gestore GLOBALE, non il sistema-casa: nome legacy).
- Marker sonda che copre il sole a distanze enormi: verificare dopo la riscrittura mappa/distant-rendering.

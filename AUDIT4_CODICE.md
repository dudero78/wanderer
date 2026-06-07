# AUDIT #4 — Codice, Igiene, Bugfixing

*7 giugno 2026, notte. Tavolo tecnico (audit autonomo). Focus sul codice del CIELO (il più fresco e meno testato)
+ igiene generale + i bug aperti dagli audit precedenti. Ogni voce cita `file:riga`. Compagno:
`AUDIT4_ARCHITETTURA.md`.*

> **Reperto a confidenza più alta, impatto più alto — BUG-1:** la finestra dell'atlante deep-sky usa
> `Mathf.SmoothStep(1.0f, 0.5f, r)` che **NON è la smoothstep di GLSL**: in Unity è un LERP fra 1.0 e 0.5 → l'alpha
> **non scende mai sotto 0.5 sul bordo del tile** → bordi quadri. È lo **stesso trap già scritto in CLAUDE.md/memoria
> ("Mathf.SmoothStep ≠ GLSL")**, ricomparso. Spiega completamente i "blob/quadrati" rosa-ciano fra le stelle.

---

## 1. BUG / rischi di correttezza (cielo)

### BUG-1 (CRITICO) — La finestra dell'atlante non arriva a 0 → i "blob/quadrati"
`DeepSkyRenderer.cs` (`FillTile`): `float w = Mathf.SmoothStep(1.0f, 0.5f, r);` — il commento dice "→0 sul bordo".
**Non ci arriva.** `Mathf.SmoothStep(a,b,t)` interpola l'OUTPUT fra `a=1.0` e `b=0.5` → minimo **0.5**. Verificato:
r=1.0 → 0.5; r=1.414 (angolo del quad) → 0.5. Concorrono:
- ogni tile mantiene alpha non-nullo al bordo → taglio rettangolare visibile;
- gli angoli del quad (`local = uv·0.5+0.5 ∈ {0,1}`) cadono **esattamente sulla cucitura** dei tile dell'atlante; e
  l'atlante è **mippato + Trilinear + Clamp** → a mip ≠ base il tile vicino si media dentro → **bleeding** (seconda fonte di quadrati);
- a forte zoom la luminosità DSO **satura** (`lum→1`) e lo sprite cresce (M31 ≈ 765 px) → il bordo non-nullo diventa
  un blocco additivo grande e luminoso ("i blob peggiorano zoomando").
**Fix:** (a) finestra fatta a mano che arrivi a 0 PRIMA del bordo del quad — `1 - Smooth01(0.78, 1.0, r)` con
`Smooth01(e0,e1,x){ float t=saturate((x-e0)/(e1-e0)); return t*t*(3-2*t); }`; (b) **inset UV** dei quad (gutter ~3%
per tile, niente cucitura), o clamp per-tile nello shader; (c) atlante **senza mipmap** (`Alpha8,false` + `Apply(false)`
+ Bilinear) per un 2×2.

### BUG-2 (ALTO) — L'etichetta del telescopio MENTE; magnificazione incoerente
`OpticalInstrument.cs`: i fattori sono `Mag = {1, 8, 25}` ma le etichette OnGUI dicono **"BINOCOLO 10×"** e
**"TELESCOPIO 50×"** (e il commento d'intestazione dice ~10×/~50×). Parte del "lo zoom rivela troppo poco": gli si
promette 50× e si dà 25× (FOV scende a 2.2°, non ~1°). **Allineare label↔fattori**; se si alza a 50× reali, alzare
anche i guadagni reveal stelle/DSO (a 50× il catalogo da 119k è sparso).

### BUG-3 (MEDIO) — Nessun tetto sensato alla dimensione reale dei DSO
`DeepSkyBillboard.shader`: `_MaxPx = 6000`. Un DSO grande (M31) a telescopio può clampare a 6000 px → un singolo
sprite copre tutto lo schermo. Con BUG-1 = quadrato luminoso a schermo intero. **Abbassare a ~1200** e/o sfumare
l'alpha quando lo sprite diventa enorme.

### BUG-4 (BASSO/MEDIO) — Deep-sky troppo luminosi / nessun vero "si risolve"
`DeepSkyBillboard.shader`: a telescopio anche i DSO mag-8 saturano (`lum≈1` → bianco piatto). La STRUTTURA (spirale,
granelli) è nella forma-alpha ma viene lavata dalla saturazione. **Abbassare `_DsoZoomPow`/`_DsoGain`** così i DSO
brillanti restano sotto saturazione e la forma si legge.

### BUG-5 (BASSO) — `tier`/`uv.w` impacchettato ma inutilizzato
`StarFieldRenderer.cs` impacchetta `tier` in `uv.w`; `StarPoint.shader` non lo legge mai. Dato per-vertice morto
(×476k vertici). Toglierlo o usarlo.

### BUG-6 (BASSO) — Etichette costellazioni proiettate sempre con la camera GIOCATORE
`ConstellationLines.cs` (OnGUI) proietta sempre via `playerCam`. In vista SONDA (che `ActiveSkyCamera()` supporta
per il cielo) le etichette finiscono nella posizione della camera giocatore → staccate dalle stelle. Le LINEE (mesh)
sono ok. Bassa priorità (le costellazioni sono feature a occhio-giocatore).

---

## 2. Diagnosi dei sintomi riferiti da Dario (stasera)

- **(a) Blob/quadrati** → **BUG-1** (primario, verificato) + BUG-3/BUG-4. Meccanismo concreto: finestra che floora a
  0.5, angoli del quad sulla cucitura, atlante mippato che fa bleeding, saturazione+ingrandimento a zoom. **È il
  reperto a confidenza più alta.**
- **(b) Telescopio buio/rivela poco** → BUG-2 (promette 50×, dà 25×) + reveal conservativo (`_RevealThresh=0.45` con
  `_Exposure=0.55`: una mag-6.5 sta a I=0.55, lum=0.24 = fioca). Il reveal FUNZIONA a zoom, ma il mag reale è metà di
  quello scritto e l'esposizione è tarata scura. Leva rapida: su `_Exposure`/`_Gain` e/o mag reale a 50× con più reveal.
- **(c) Poche costellazioni + linee** → NON un bug: il catalogo ha **~22 figure con segmenti + 2 sole-etichetta** (cielo
  reale = 88) → completezza dati. La QUALITÀ linea è sana (espansione screen-space a spessore costante, sezione
  morbida `exp(-side²·_Core)`, guardia dietro-camera). Se sembrano deboli: alzare il cap `_Alpha` (oggi `alpha*0.9`)
  o `_PixelWidth`. **NB confondente: gli shader potrebbero non essersi re-importati quando Dario ha testato** (il C#
  ricompila subito, gli shader Metal no) → ri-testare da reimport pulito PRIMA di concludere.
- **(d) Via Lattea "nuotava"** → il codice ATTUALE è corretto e NON dovrebbe nuotare: il quad è emesso diretto in
  clip space; i raggi vengono da `cam.transform.rotation` in mondo e **non sono normalizzati sulla CPU** (così
  l'interpolazione screen-space dà la direzione esatta per-pixel — commento corretto in `MilkyWayBand`). Il
  back-transform equatoriale è l'inverso di `EquatorialToGame`. SkyRoot è identity-rotation (niente doppio
  transform). **Se nuota ancora: residuo = calibrazione `_FlipU`/`_OffsetU`, non geometria** — oppure lo shader non
  era ricompilato. Alternativa robusta: Via Lattea su GEOMETRIA sfera (stessa proiezione delle stelle).

---

## 3. Igiene del codice
- **HYG-1 (MEDIO):** il misuso `Mathf.SmoothStep`-come-soglia compare **due volte** — `DeepSkyRenderer` (BUG-1) e
  `OpticalInstrument.BuildEyepiece` (`SmoothStep(0.80,0.96,r)`, lì accidentalmente ok). **Standardizzare un helper
  `Smooth01(e0,e1,x)` ovunque**, come da lezione in CLAUDE.md.
- **HYG-2 (BASSO):** numeri magici sparsi in `BuildAtlas` (ok per arte procedurale), ma il magico **portante**
  sbagliato è la finestra (`1.0, 0.5`).
- **HYG-3 (BASSO):** `SkyData.Color[i].a` documentato "inutilizzato" ma sempre 255; `Flags` bit0 (tier occhio-nudo)
  caricato ma mai consumato (il campo disegna TUTTE le stelle). Dead-ish.
- **HYG-4 (BASSO):** boilerplate del quad billboard duplicato in `StarFieldRenderer`/`DeepSkyRenderer`/
  `ConstellationLines` (4 vert ±1, stesso pattern tri, stessi bounds `1e9f`, stesso blocco flag MeshRenderer). Un
  helper unico, altrimenti marcisce in parallelo.
- **HYG-5 (BASSO):** `DeepSkyBillboard` espone `_DsoExposure/_DsoGain/...` come Properties ma il C# non li imposta →
  tarare = editare lo shader, non il renderer (incoerente con come `_SkyZoom` è globale).

---

## 4. Robustezza (cielo)
- **BUONO:** dati bakeati mancanti gestiti senza throw (`SkyData.Load` warn+false, `LoadDso` no-op); shader mancanti
  controllati con `LogError` in ogni renderer + tutti i 6 shader cielo registrati in `EnsureIncludedShaders`; layer
  "Sky" mancante → fallback `~0` su Default; RenderScaler gestito (`_SkyPxScale`) + etichette riscalate RT→schermo;
  mappa esclude il cielo (`ActiveSkyCamera` null su `MapMode.IsOpen`).
- **ROB-4 (MEDIO):** `SkyData.Load` legge la versione del blob ma **la ignora** → un `stars.bytes` vecchio verrebbe
  letto come spazzatura. Guardia di versione a costo zero.
- **ROB-6 (BASSO):** `DeepSkyBillboard` fa `min(type,3)` → la planetaria (type 4) usa il tile nebulosa con tinta verde
  (probabilmente voluto: l'atlante ha 4 tile per 5 tipi) — meritava un commento.

---

## 5. Quick-win (priorità)
1. **Fix finestra atlante (BUG-1)**: `1 - Smooth01(0.78,1.0,r)` + inset UV + atlante senza mipmap. Da solo dovrebbe uccidere i quadrati.
2. **Abbassa `_MaxPx`→~1200 e doma il gain DSO** (BUG-3/4).
3. **Allinea label↔mag del telescopio** (BUG-2).
4. **Stelle a occhio nudo un filo più luminose** se il "buio" persiste (`_Exposure` 0.55→~0.7).
5. **Guardia di versione** in `SkyData.Load`; **togli `uv.w`** in StarPoint; **helper `Smooth01`** condiviso.
6. **Più costellazioni** (solo dati in `Catalog()`); eventualmente alza il cap `_Alpha`/`_PixelWidth`.

*(Tutti questi sono nel TODO §"🌌 CIELO STELLATO" con i dettagli, da fare INSIEME a Dario — la questione è parcheggiata
per debug condiviso, non per assenza di diagnosi.)*

---

## 6. Bug NON-cielo ancora aperti (dagli audit precedenti, riconfermati)
- **Editor #1 — il mare non allaga a palla d'acqua al max.** Causa: ordine ricetta TETTONICA→MARE→CRATERI (crateri
  DOPO il mare) → `seaMask` legge le aree craterizzate come asciutte. Non è un bug di codice ma di ordine/design →
  decidere con Dario (mare come ultimo processo, o opzione "il mare allaga sempre"). **APERTO.**
- **Editor #2 — trasparenza acqua "al contrario" + fondo profondo mai visibile.** Beer-Lambert `exp(-depth/clarity)`
  → a clarity finita il fondo profondo non traspare mai. Lo slider si grigia in anteprima CPU, ma il modello va
  ripensato a clarity-max. **PARZIALE / shader da rivedere.**
- **Editor #3 — Bake dall'editor fa SPARIRE il pianeta.** Causa identificata (RT lasciata legata e poi rilasciata),
  fix-candidato applicato (`cb.SetRenderTarget(CameraTarget)`) **non verificato a editor aperto.** **FIX-CANDIDATO.**
- **Errore console "Screen position out of view frustum"** — proiezione di target lontani, benigno. **APERTO (benigno).**
- **Collisione solo radiale-sotto-i-piedi** (wall-stop tangente aggiunto, ma l'ingresso laterale in pareti ripide può
  ancora scivolare finché il centro capsula non scende sotto l'altezza) — indebolisce il verbo land/walk alla base.
- **Da CONFERMARE in gioco (dichiarati fatti ma i doc si contraddicono):** #8 Step in FixedUpdate + SimTime tick
  intero (judder 50Hz vs 60fps?), isteresi orientamento walker (flip-flop fra i gemelli terra-test3/Valentina2),
  EclipseDriver a 10 Hz, SuppressDraw reset fra sessioni Play, animazione apertura mappa.

---

*Fine AUDIT #4 — Codice. La nota d'oro: il bug dei "blob" è il trap Mathf.SmoothStep≠GLSL già documentato — fix
chiaro. Il resto del cielo è robusto; i bug aperti non-cielo sono per lo più nell'editor di pianeti.*

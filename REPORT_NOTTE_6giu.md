# Cos'ho fatto mentre dormivi — notte del 6 giugno 2026

Ciao Dario. Qui c'è tutto quello che ho toccato stanotte, in ordine, col perché. Il rapporto del **tavolo degli
esperti** è separato (`AUDIT3.md`), e il piano per i "più sistemi solari" è in `STARSYSTEM_DESIGN.md`.

**Niente è stato committato** (come chiesto). Trovi tutto come modifiche da rivedere. Per controllare a colpo d'occhio:
`git status` e `git diff` dentro la cartella `Wanderer`.

---

## Come ho lavorato in sicurezza (la cosa più importante)

Unity era aperto sul progetto, quindi non potevo avviare il gioco né compilare in modo affidabile dall'editor.
Per non rischiare di lasciarti un progetto rotto al risveglio, mi sono costruito un **controllo di compilazione
offline**: prende gli stessi identici parametri che usa Unity (li ho estratti dai file interni che Unity stesso
aveva già generato) e ricompila tutto il codice C# in una cartella temporanea, senza toccare nulla del progetto.
Così, **dopo ogni modifica, ho verificato che tutto compili pulito** (zero errori).

Il limite onesto: questo controlla il **codice C#**, non gli **shader** (i programmini della scheda video) e non
può *avviare* il gioco. Per questo le modifiche che toccano gli shader le ho **lasciate pronte ma non applicate**
(vedi in fondo): applicarle "alla cieca" rischierebbe un pianeta magenta che scopriresti solo aprendo il gioco, ed
è esattamente il tipo di sorpresa che vogliamo evitare.

---

## 1) Spaccato il "god-object" — il punto #18 della nostra lista

`GpuPlanetRenderer` era un file solo da **874 righe** che faceva tre lavori diversi insieme. L'ho diviso in tre,
ognuno con UN compito (è il refactor che avevamo deciso — "spostamento puro", nessun cambio di comportamento):

- **`SlabPool.cs`** (nuovo) — la **memoria video condivisa**: il magazzino delle "fette" di terreno (la griglia di
  triangoli di ogni tassello), con la lista di quelle libere e la cache di quelle che escono di vista. Un solo
  magazzino per tutti i corpi (così niente 5 GB sprecati).
- **`PlanetLodTree.cs`** (nuovo) — il **quadtree**: decide quali tasselli mostrare in base alla distanza della
  camera, li suddivide avvicinandosi, nasconde quelli dietro la curvatura del pianeta.
- **`GpuPlanetRenderer.cs`** (riscritto, ora ~430 righe) — il **regista**: possiede lo shader di calcolo, disegna,
  illumina a mano (sole + torcia), e fa da ponte fra gli altri due.

**Perché conta:** non dà un guadagno visibile *oggi*, ma rende ogni lavoro futuro più facile e meno rischioso
(ognuno dei tre pezzi si capisce e si modifica da solo). Era il prerequisito che avevamo concordato.

**Come ho verificato che non ho rotto niente:** oltre alla compilazione pulita, ho confrontato **metodo per metodo**
il nuovo codice con l'originale (estratto dal git): allocazione fette, cache, suddivisione, fusione, distruzione,
LOD predittivo — tutti **identici riga per riga**, solo spostati. Ho anche **chiuso un piccolo difetto latente** che
c'era già nell'originale (un buffer veniva liberato ma non azzerato).

> ⚠️ Questo è codice fresco e grosso: il tavolo degli esperti l'ha esaminato apposta e l'ha promosso "corretto",
> ma quando riapri il gioco tieni un occhio sul terreno dei corpi (specie Valentina2/terra-test3) per conferma sul
> campo.

---

## 2) Reso SICURA la "fonte unica dell'altezza" — il punto #17

Il problema: la forma del terreno è scritta **due volte**, una in C# (per il camminatore) e una in HLSL (per la
grafica). Devono dare lo *stesso identico* risultato, o il giocatore galleggia/sprofonda. Tenerle allineate a mano
è la "fonte numero uno di bug fantasma" — ti ha morso per un'intera sessione.

La soluzione "definitiva" (un generatore che produce l'HLSL dal C#) è troppo rischiosa da fare alla cieca: se il
generatore sbaglia, rompe la geometria in silenzio e me ne accorgerei solo avviando il gioco. Quindi ho fatto la
cosa **robusta**: ho reso la doppia scrittura **sicura**.

- **`PlanetParityGate.cs`** (nuovo) — un **controllo automatico** che, **a ogni ricompilazione** dentro l'editor,
  confronta l'altezza C# con quella della GPU su **tutte le ricette ufficiali** (Cetra, Luna6, Luna7, terra-test3,
  Valentina2) + casi-limite. Se divergono di più di 1 cm, te lo **urla subito in console** — non aspetti più di
  entrare in gioco e notare l'artefatto. Lo puoi spegnere dal menu se ti dà fastidio (`Wanderer → Gate parità
  automatico`).
- **`PlanetGpuParityTest.cs`** (potenziato) — il test manuale ora copre anche le ricette ufficiali, non solo quelle
  di prova.

Il generatore vero (single-source) resta in lista, ma ora la duplicazione non può più morderti di nascosto.

---

## 3) Gli altri due punti della lista (#15 e #16)

- **#15 — fisica in FixedUpdate: era già fatto.** Ho verificato `PlanetWalker`: l'input lo legge in `Update`, ma
  **tutta la fisica** (gravità, spinta, freno, autopilota) gira già in `FixedUpdate` con il passo fisso. Niente da
  rifare. (Resta una sfumatura sul "tick intero" del tempo di simulazione: l'ho girata al tavolo.)
- **#16 — layer multi-sistema: l'ho mandato al tavolo invece di costruirlo alla cieca.** È architettura per il
  futuro ("più sistemi solari"), il nostro stesso principio dice "astrai solo quando serve", ed è troppo grosso e
  speculativo per farlo senza poterlo provare. Il tavolo ristretto sta producendo il **piano a tappe**
  (`STARSYSTEM_DESIGN.md`), con una prima tappa sicura che non cambia nulla del sistema attuale.

---

## 4) Le correzioni dal tavolo degli esperti (già applicate, codice C#)

Il tavolo ha prodotto una lista di interventi sicuri. Quelli **solo-codice** (verificabili in compilazione) li ho
applicati tutti:

| # | Cosa | File | Perché |
|---|------|------|--------|
| 1 | Azzero `SuppressDraw` all'avvio scena | `GameBootstrap.cs` | Evita il caso "pianeta GPU invisibile" fra due sessioni di Play se chiudi con la mappa aperta. |
| 2 | Il gate di parità ora pesca **NaN/Inf** | `GpuPlanetRenderer.cs` | Prima un'altezza "non-numero" passava il controllo come OK (per come funziona il confronto). Buco nella rete chiuso. |
| 3 | Il log "frame pesante" solo in sviluppo | `GpuPlanetRenderer.cs` | Quel `Debug.Log` scattava *proprio* sui frame di scatto, peggiorando lo scatto che voleva misurare. Fuori dalle build finali. |
| 4 | Tolto un'allocazione per-frame | `GpuPlanetRenderer.cs` | Uno `Stopwatch` creava un oggetto ogni frame per ogni corpo vicino → ora niente spazzatura da raccogliere. |
| 5 | `SunLight` azzera il suo riferimento alla distruzione | `SunLight.cs` | Dopo una ricompilazione in Play, un riferimento "morto" faceva cadere l'illuminazione sul default. |
| 7 | La torcia si cerca **una volta**, non ogni frame | `GpuPlanetRenderer.cs` | `FindAnyObjectByType` è caro e girava ogni frame per ogni corpo finché la torcia non c'era. |
| 8 | Ripristino il target di render dopo il bake | `PlanetBaker.cs` | **Probabile causa del bug #3** (il pianeta sparisce dopo un Bake da editor): il bake lasciava la GPU "puntata" su una texture poi liberata. |
| 11 | Isteresi sul corpo di riferimento del camminatore | `PlanetWalker.cs` | Fra i due gemelli (terra-test3/Valentina2) la scelta del "suolo di riferimento" oscillava ogni frame → sobbalzo di inquadratura. Ora cambia solo se il nuovo è più vicino del 10% (la stessa logica che l'ancora già usava). La forza di gravità non cambia. |
| 12 | Slider "limpidezza" grigio in anteprima CPU | `PlanetEditor.cs` | La trasparenza si vede solo in anteprima GPU; lo slider in CPU prometteva un effetto che non c'era. |

**Uno l'ho scartato di proposito:** il tavolo suggeriva di spostare l'aggiornamento della luce *prima* del taglio
"corpo troppo lontano". L'ho rifiutato: scambierebbe un principio di performance reale (non lavorare sui corpi
lontani) per un beneficio impercettibile (un frame di luce leggermente vecchia quando un corpo rientra in vista).

---

## 5) Lasciato pronto ma NON applicato — i salti di RESA (toccano gli shader)

Questi sono i tre interventi di **maggior valore visivo**, ma toccano gli shader, che **non posso verificare in
compilazione offline**. Per non rischiare un pianeta magenta, li ho **specificati con precisione** ma non applicati:
sono pronti da fare insieme, con un solo controllo a gioco aperto. Sono il **primo lavoro consigliato** quando torni.

- **[9] keyword `_HAS_SEA`** — i corpi asciutti (Cetra, Luna6) pagano comunque il ramo "acqua" nello shader. Una
  keyword che lo elimina dove non c'è mare alleggerisce il fragment. *(valore alto, sforzo medio)*
- **[10] colore per-vertice** — 3 calcoli di rumore ancora fatti **per-pixel** (macro/minerali/maria) a frequenza
  bassissima: spostarli per-vertice taglia il costo del fragment ed è il **prerequisito del PBR**. *(valore alto)*
- **[13] eclissi nel renderer vero** — oggi l'ombra di eclissi vive solo sul renderer di riserva, non su quello con
  cui cammini/voli. Va portata nello shader del renderer autoritativo. *(valore medio)*

Dettagli file-per-file nella tabella "actionable" dell'`AUDIT3.md`.

---

## In sintesi

- ✅ #18 (god-object) — fatto, verificato in compilazione e metodo-per-metodo.
- ✅ #17 (parità altezza) — reso sicuro con gate automatico.
- ✅ #15 (FixedUpdate) — era già a posto, verificato.
- ✅ #16 (multi-sistema) — al tavolo, piano a tappe in arrivo.
- ✅ 9 correzioni del tavolo applicate (incluso un probabile fix al bug editor #3).
- 📋 3 interventi di resa specificati e pronti (toccano gli shader → un controllo a gioco aperto).

Tutto compila pulito. Buongiorno! ☀️

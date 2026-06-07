# Pitch al Consiglio — "Wanderer"

*Da leggere prima di esplorare il codice. È il gioco su cui vi chiediamo un parere.*

**Cos'è.** "Wanderer" è un gioco spaziale **seamless**, idealmente da *Outer Wilds* verso *No Man's Sky*. Lo
sviluppa **un solo hobbyist (Dario)** nel tempo libero, e **tutto il codice lo scrive un'AI** (in Unity, pipeline
built-in, su Mac/Metal — target anche PC). Dario non è un programmatore: dirige, dà la direzione artistica, fa
debug per screenshot.

**La visione.** Un universo **continuo, senza caricamenti**: cammini sulla superficie di una luna, decolli col
jetpack, voli fra i pianeti, attraversi lo spazio interstellare verso altre stelle — tutto seamless. **Scala
compressa alla Outer Wilds** (lune 300–800 m, rocciosi ~1.5 km, un sistema sta in ~130 km), non realistica, scelta
per intimità e meraviglia. Doppia precisione + floating origin (niente jitter). Orbite kepleriane **on-rails
deterministiche** (niente deriva).

**Cosa c'è già (un motore sorprendentemente solido per un hobbista):**
- Terreno procedurale calcolato e disegnato **sulla GPU** (CDLOD, 1 draw indirect per corpo, colore procedurale,
  **crateri/mari/tettonica calpestabili**), crack-free via morph continuo.
- Un **editor di pianeti ricco**: la "ricetta" è una **pipeline ordinata di processi geologici** (l'ordine conta —
  un cratere dopo un mare scava una buca asciutta).
- Volo a due modelli (crociera/newtoniano), **match-velocity**, **autopilota**, mappa multi-sistema con orbite a
  fili luminosi alla Outer Wilds.
- Una **sonda alla Outer Wilds** (la lanci, ci guardi attraverso, scatti foto), eclissi analitiche, multi-sistema
  interstellare (sistemi che si svegliano/dormono per prossimità).
- **Da stasera: un CIELO STELLATO REALE.** Il catalogo **HYG di ~119.000 stelle vere** (posizione, colore B−V,
  magnitudine), la **Via Lattea fotografica della NASA**, le **costellazioni** riconoscibili, **oggetti del
  profondo cielo** (galassie/nebulose/ammassi) che **si risolvono** col binocolo e il telescopio. Allineato
  all'**eclittica** del sistema; le stelle-destinazione (Vega, Antares) sono messe **dove stanno davvero in cielo**
  — *la stella che ammiri è la stella dove vai*. Il giocatore può sdraiarsi e **osservare il cielo** come con un
  binocolo d'estate: puntare, scoprire, riconoscere.

**Priorità dichiarata di Dario, oggi:** **resa grafica + qualità + performance PRIMA del gameplay.**

**Cosa NON è ancora deciso (qui vogliamo le vostre teste):**
- **IL VERBO.** Non c'è ancora un loop di gioco. *Cosa si fa?* Atterra·cammina·raccogli·vai·puoi-fallire è l'MVP
  ipotizzato, ma non scelto. Sopravvivenza? Commercio? Esplorazione/scoperta pura? Narrazione? **Un "naturalista
  dello spazio"** che cataloga pianeti e cieli? Astrofotografia? Nessuna decisione presa.
- **Giganti gassosi e stelle come VOLUMI** (ci voli/nuoti dentro), **corpi esotici** (mondi cavi, grotte) —
  progettati, non costruiti.
- **Online** un giorno, forse — molto lontano.
- **Pipeline** built-in vs HDRP, **atmosfera**, **bloom/HDR**, sole come sfera/glow — da decidere/fare.
- **Il TONO:** contemplativo e poetico (Outer Wilds) · vasto e da sopravvivenza (NMS) · qualcos'altro?

**La domanda al tavolo:** esplorate il codice e i documenti liberamente. *Cosa vedete? Cosa fareste? Dove sta la
magia da coltivare e dove il rischio? Cosa può diventare questo gioco?*

---
*Dove guardare nel codice:* `Assets/Scripts/**` (World/, World/Sky/, Player/, Physics/, Bootstrap/), `Assets/Shaders/**`;
i documenti `CLAUDE.md`, `TODO.md`, `RENDERING_STRATEGY.md`, `STARSYSTEM_DESIGN.md`, `AUDIT4_ARCHITETTURA.md`,
`AUDIT4_CODICE.md`, e l'album in `/Users/darioleone/Desktop/wanderer-history/README.md` (il diario per immagini).

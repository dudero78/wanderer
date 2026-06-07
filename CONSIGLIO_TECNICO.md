# Il Gran Consiglio — Considerazioni TECNICHE

*7 giugno 2026, notte. 35 game designer invitati a 6 tavoli, hanno esplorato il codice di "Wanderer" e parlato con
la propria voce. Qui le considerazioni TECNICHE dei più tecnici fra loro (motore, sistemi, performance, strumenti,
audio, ritmo). Le considerazioni creative sono in `CONSIGLIO_CREATIVO.md`. Gli audit di dettaglio in `AUDIT4_*.md`.
Pitch in `PITCH_CONSIGLIO.md`.*

---

## 0. La convergenza tecnica (su cosa sono d'accordo TUTTI i tecnici)

1. **Le fondamenta sono di livello principal-engineer e NON vanno toccate.** Doppia precisione + floating-origin
   (conversione a float in un punto solo), `KeplerOrbit` in forma chiusa, e soprattutto **`SimTime` deterministico a
   tick INTERO** (non accumulatore float). Carmack: *"è la riga che mi convince che chi ha scritto questo capisce la
   differenza tra «funziona oggi» e «è riproducibile bit-per-bit»."* Newell: è anche lo strumento di **replay /
   telemetria / netcode lockstep** — proteggetelo religiosamente. **Azione concreta (Carmack):** un test che gira il
   sistema 10.000 tick due volte e confronta gli hash.

2. **Debito tecnico #1, unanime: l'altezza duplicata a mano in C# e HLSL.** Il `PlanetParityGate` è una **RETE, non
   una cura** — dice "sono diversi" *dopo* il fatto. Carmack: la cosa semplice non è un transpiler elaborato, è
   *ridurre la superficie duplicata finché la parità è banale*, o generare l'HLSL dal C# con un passo di build idiota.
   Blow: la storia del clamp del fondo-ciotola (da mettere in *entrambe* o walker e mesh divergono) è già il sintomo.

3. **Debito #2: le tre reti di sicurezza nel vertex shader più caldo** (geomorph clamp + anti-spuntone + region-stamp).
   Carmack/Blow: tre "se è spazzatura, collassa il vertice" impilati = l'invariante "la fetta del pool corrisponde
   all'istanza che la referenzia" è difeso *a posteriori*, non garantito. **Renderlo un invariante vero con UN assert**,
   non tre cinture.

4. **Costruire ORA il GIUNTO, non il contenuto.** Howard/Jones/Carmack: prima dei corpi volumetrici (gas/stelle) e
   delle entità mobili, introdurre un'astrazione minima `ICelestialRenderer` / `IWorldEntity` così che il volumetrico
   e qualunque "cosa che esiste e si muove" si infilino nella **stessa** macchina ancora/eclissi/mappa, invece di un
   retrofit. La sonda (`Probe.cs`, gravità+collisione analitica, registrata in `Loose`) è **già** la prova di concetto
   di un'entità non-giocatore: generalizzatela.

5. **Performance: NON è una preoccupazione sul target (M3 Pro Max).** La leva sul calore è il **cap fps**, non lo
   shader (la GPU è scarica, il collo storico era la CPU main-thread, già snella e strumentata). `RenderScaler` a 1.0
   = banda 0.4–1.0 di **headroom non sfruttato**. Le 119k stelle sono un non-problema: i quad sotto-soglia collassano
   ad **area nulla** nel vertex (`px = lerp(...)*keep`, `keep = step(_RevealThresh,I)`) → zero fragment, niente overdraw.

6. **Cablare UN verbo banale ADESSO per mettere i sistemi sotto pressione.** (Carmack, Blow, Newell, Spector.) Tutta
   questa infrastruttura di traversata non è ancora stata testata da un'azione del giocatore → "un'architettura non
   testata da un verbo cresce l'astrazione sbagliata" (Blow). Il primo raccoglibile-che-può-fallire è l'unico test che conta.

7. **L'OMISSIONE PIÙ GRAVE non è il verbo: è l'AUDIO.** (Sakaguchi, assenso implicito di tutti.) Nessuna riga sul
   suono in tutto il progetto. Un sistema di **musica adattiva** + un **leitmotiv di casa che si vela con la distanza**
   è lo strumento a più alto rapporto emozione/costo, vive *dentro* il seamless senza romperlo, ed è il momento (lo
   sbucare nello spazio) che il giocatore racconterà. **Da costruire prima del verbo.**

---

## 1. Substrato numerico, determinismo, netcode (Carmack, Newell, Koster)
- **Proteggere il determinismo come un tesoro.** Il momento in cui qualcuno mette `Time.deltaTime` nella sim invece
  che nel tick fisso, il determinismo muore in silenzio (Carmack). Test di hash a 10k tick.
- **È già strumentabile:** una sessione = sequenza di input + seed → ri-eseguibile bit-per-bit (Newell) = base di
  replay, telemetria onesta, e un domani netcode lockstep. *Trasformatelo in capacità reale presto* — è lo strumento
  di iterazione più potente che avete.
- **Angoli single-context da registrare come "costo noto"** (Newell, dall'AUDIT4): `SlabPool` statico globale,
  quadtree guidato da UN solo punto di vista, `SceneOrigin` globale. Irrilevanti in single-player; lavoro il giorno
  del multiplayer. Il peggior debito è quello che scopri il giorno in cui spedisci.
- **Misurare sulla BUILD, su più macchine** (Newell), non nell'editor (le lezioni dure sono tutte "build ≠ editor").
  `RenderScaler` è l'assicurazione per girare su hardware modesto → allarga il pubblico.

## 2. Renderer GPU + il nuovo cielo (Carmack, Yokoi, Will Wright)
- **Il cielo è "withered technology" usata con ingegno** (Yokoi, e ne è fiero): 119k stelle = 1 draw call, mesh
  statica, quad billboard, `Blend One One` additivo, pipeline **built-in** (non HDRP). Il vincolo della built-in ha
  *costretto* a soluzioni migliori (eclissi analitica vs shadow map, colore procedurale vs texture bakate). **La
  risposta di Yokoi al "built-in vs HDRP?" è netta: tenete la built-in.** HDRP è tecnologia non-matura, costosa,
  instabile; non risolve nessun problema che avete e ne crea di nuovi.
- **Il binocolo nasce da un vincolo, ed è geniale** (Yokoi): con un catalogo finito, a forte zoom vedresti il vuoto.
  La soluzione *laterale* non è "più stelle" ma **far sì che lo strumento RIVELI, non ingrandisca** (`I = flux*zoom`,
  le deboli attraversano `_RevealThresh` ed emergono) — come un vero binocolo concentra luce. Un limite del catalogo
  trasformato nel meccanismo di meraviglia centrale. **Non spingete l'ingrandimento** (`{1,8,25}` è saggio).
- **`_SkyPxScale` che compensa il `RenderScaler` per non far pulsare le stelle** (Carmack): una correzione che si
  trova solo iterando sul vero artefatto a schermo — segno di lavoro serio.
- **L'editor-ricetta è un MOTORE DI SIMULAZIONE, non un generatore di texture** (Will Wright): pipeline ordinata di
  processi geologici dove *l'ordine è la storia del corpo*. Oggi gira una volta e si congela nel bake. **Opportunità
  tecnica:** una ricetta *funzione del tempo* (deterministica come Kepler) = una **macchina del tempo geologica**
  (erosione che procede, crateri che si accumulano), calcolabile a un tempo arbitrario in forma chiusa. L'architettura
  è già stranamente pronta.

## 3. Game-feel, input, ritmo (Romero, Druckmann, Straley, Mikami)
- **Il game-feel artigianale è già buono** (Romero): smorzamento *anisotropo* in crociera (frena orizzontale e salita
  ma non la caduta → la gravità si sente), il freno X a tre fasce col `brakeFloor` che fa scorrere gli ultimi numeri.
  Sono decisioni di *feel*, non di fisica.
- **Input:** già splittato bene (raccolto in Update, applicato in FixedUpdate). **Tenetelo così ovunque.** Romero:
  **mettete un gamepad** prima di decidere il tono — volare a stick analogici è un'esperienza diversa, più morbida,
  e potrebbe sciogliere il dubbio contemplativo-vs-vasto da solo.
- **Problema di ritmo = problema tecnico** (Straley, il più serio del tavolo D): tutto ha la *stessa curva* (voli
  liscio, atterri liscio). "Seamless" applicato all'esperienza = "senza articolazioni", e la memoria si aggrappa ai
  *cambi*. **Costruite la VALLE prima del PICCO:** le grotte/mondi-cavi già previsti sono la valle; il volo che già
  avete diventerà 10× più potente come *liberazione*, senza toccarne una riga. **Metodo:** mappate il viaggio di
  un'ora minuto-per-minuto su carta; se al minuto 3, 18, 47 il giocatore prova "la stessa cosa", è il bug più
  importante del progetto.
- **Tensione fisica, non finta** (Druckmann, Mikami): l'autopilota che frena per te *seppellisce* l'unico momento col
  battito (la gauge "TROPPO VELOCE" mentre il pianeta riempie lo schermo). L'autopilota deve **potermi tradire**, o
  devo poterne fare a meno. Mikami: il contemplativo ha bisogno di un *battito* (trattieni nel viaggio, rilascia
  nell'approdo); il silenzio è anche "il corridoio prima dello zombie".

## 4. L'economia mancante — la struttura formale (Garfield, Knizia, Rosenberg, Meier, Chvátil)
*Il tavolo da gioco da tavolo è il più competente sul buco preciso: niente costa, quindi niente vale.*
- **UNA sola risorsa scarsa** (Knizia, il più severo: mai tre). Convergenza su **delta-v / propellente** (è già nel
  motore newtoniano, rende fisica la tensione "questo posto vale il viaggio per lasciarlo?"). Finita, ricaricabile
  *solo atterrando e raccogliendo* su una superficie → il volo diventa una catena di decisioni di portata.
- **Il valore-scoperta EMERGE dal generatore, non va inventato** (Knizia): contate i processi geologici attivi della
  ricetta, pesate per rarità → un pianeta con tettonica+mari+crateri "vale" più di una luna nuda. Il punteggio è già lì.
- **La tensione, in forma di problema** (Knizia): *budget di viaggio finito; N corpi con valore-scoperta e costo-di-
  accesso (distanza × gravità); massimizza la scoperta prima di esaurire il budget; alcune scoperte ricaricano, alcune
  (eclissi/transiti) sono disponibili solo in una finestra temporale.* = **uno zaino con vincolo temporale su un
  grafo**, giocato tutto davanti alla **mappa multi-sistema che già avete** (è il tabellone di punteggio).
- **L'orologio deterministico è una risorsa di DESIGN** (Chvátil, Bauza, Knizia): eclissi/transiti/allineamenti =
  finestre che creano urgenza *gratis*, senza un timer cattivo. *"L'eclissi su Cetra è fra due orbite — ci arrivo?"*
  Il vostro `SimTime` è un game designer che non state pagando.
- **Engine-building / il catalogo che SBLOCCA** (Rosenberg): cataloga N rocciosi → migliore serbatoio; risolvi M
  deep-sky → binocolo diventa telescopio (i due livelli ottici già ci sono); mappa K sistemi → autonomia
  interstellare. Ogni scoperta *finanzia la prossima spedizione più lontana*.
- **Il loop ripetibile (Meier):** l'unità di decisione è **il salto di corpo**; a ogni arrivo, *una decisione
  interessante* (cosa raccolgo, dove vado, cosa mi serve per arrivare più lontano). Catena di scelte-di-portata alla
  Pirates!/FTL. **La profondità dev'essere accessibile:** una barra, una soglia, una decisione chiara; nascondete Kepler.

## 5. Gli strumenti narrativi che ESISTONO già nel motore (Kojima, Lake, Cage, Druckmann)
*Considerazioni tecniche su come consegnare emozione SENZA un sistema di cutscene (che non c'è — ed è un dono).*
- **La foto della sonda (`ScreenCapture.CaptureScreenshot` → `sonda_*.png`) è un DISPOSITIVO DI MEMORIA DIEGETICO**,
  non una funzione "fotografia" (Kojima). Caricatela di senso: **timestamp di gioco, coordinate-universo, nome della
  stella sopra la testa**, eventualmente una **didascalia** scritta (Lake: un'immagine + una riga è un medium
  narrativo completo). Oggi la foto muore in una cartella — è lo spreco più grande.
- **La sonda è una SECONDA macchina da presa** (Cage): grandangolo, free-look, vista separata, già renderizzata da
  `ExtraViewpoints`. Avete il *montaggio* in mano e non lo usate: lo split tra dove *sei* (corpo piccolo laggiù) e
  dove *guardi* (la sonda) è linguaggio cinematografico già nel motore.
- **I NOMI sono mitologia importata gratis** (Lake): Vega, Antares, le costellazioni — nomi reali del catalogo HYG,
  con millenni di storie addosso ("Antares = rivale di Marte"). Quando punti il binocolo, *una riga* (non una scheda
  Wikipedia). È il vostro "libro" già scritto dall'umanità.
- **Manca l'AUDIO** (Sakaguchi, §0.7): musica adattiva che parte quando il cielo si apre; **leitmotiv di casa** che si
  vela con la distanza (Druckmann vuole "non perderti"; Sakaguchi dà il *come*: lo senti con le orecchie prima che con
  gli occhi). Tecnicamente è il sistema più adatto al seamless (non interrompe nulla).

## 6. Lista d'azione TECNICA (priorità, dal consiglio + AUDIT4)
**Subito / a basso costo:**
1. **Finire il cielo** (i 4 bug parcheggiati in `TODO.md §🌌`; il principale è il trap `Mathf.SmoothStep≠GLSL` →
   `AUDIT4_CODICE.md` BUG-1). *Prima regola: reimport shader pulito e ri-test.*
2. **Caricare di senso la foto della sonda** (timestamp/coord/nome stella) — strumento narrativo a costo quasi nullo.
3. **Mostrare al giocatore che la stella che guarda è la stella dove va** (un evidenziatore/nome nel binocolo).
4. **Test di determinismo** (hash a 10k tick) per blindare il substrato.
5. **Gamepad** (Romero) — può chiarire il tono da solo.

**Strutturale / prossimi blocchi:**
6. **Sistema di musica adattiva + leitmotiv di casa** (Sakaguchi) — *prima del verbo*.
7. **UN verbo banale con stato e fallimento** + **UNA risorsa scarsa** (delta-v) per pressurizzare i sistemi.
8. **Il giunto `ICelestialRenderer`/`IWorldEntity`** prima del volumetrico.
9. **Ridurre la duplicazione altezza C#↔HLSL** (debito #1) e **rendere strutturale l'invariante di churn** (un assert).
10. **Una piccola light-list** al posto del singolo `AuxPointLight`; registrare gli angoli single-context.

**Da NON fare ora (gate operativo, vedi `CONSIGLIO_CREATIVO.md`):** niente giganti gassosi nuotabili, guscio d'acqua,
scala galattica generata — finché non esiste UN loop giocabile su 2-3 corpi.

---
*Sintesi del moderatore tecnico: le fondamenta sono migliori di quanto un hobbista abbia il diritto di avere. I debiti
sono noti e circoscritti (altezza duplicata, invariante di churn, il giunto volumetrico). Il vero rischio non è
tecnico: è non aver ancora messo un'azione del giocatore sotto a tutto questo. E manca l'audio — che a questo tavolo
pesa più del verbo.*

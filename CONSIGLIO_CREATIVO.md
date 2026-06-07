# Il Gran Consiglio — Considerazioni CREATIVE

*7 giugno 2026, notte. 35 game designer invitati a 6 tavoli, hanno esplorato il codice di "Wanderer" e parlato con
la propria voce. Qui TUTTE le considerazioni creative — idee, consigli, proposte, ammonimenti. Le note tecniche in
`CONSIGLIO_TECNICO.md`. Pitch in `PITCH_CONSIGLIO.md`.*

> **La frase che ha attraversato tutti e sei i tavoli, indipendentemente:**
> ***"La stella che ammiri è la stella dove vai."*** Levine la chiama "il cuore tematico". Kojima: "hai abolito la
> differenza tra desiderio e meta". Aonuma: "è la cosa più preziosa di tutto il progetto, è il «vedi quella montagna»
> reso cosmico". **Coltivatela. È la spina dorsale del gioco.**

---

## 1. IL VERBO — la convergenza che ha sorpreso tutti

Sei tavoli diversi, 35 sensibilità lontanissime (da Carmack a Knizia, da Ueda a Tajiri), e **una sola risposta**, raggiunta
indipendentemente:

> ## OSSERVARE → RICONOSCERE → CATALOGARE → ANDARE → RICORDARE
> Il **naturalista dello spazio**. Non è un ripiego contemplativo: è *l'asse su cui il gioco è già allineato*,
> eclittica compresa.

- **Tajiri** lo dice con la sua storia: *"Pokémon è nato dal collezionare insetti col retino. Voi avete costruito il
  retino più grande mai fatto — un telescopio su 119.000 stelle vere — ma non avete il Pokédex. Catturate gli
  insetti e li lasciate volare via."* Il gioco È un **Pokédex del cielo vero**: ti insegna l'astronomia reale, e ogni
  voce *esiste davvero*. I Messier hanno già un flag nei dati (`DsoFlags`) → una sotto-collezione speciale, i
  leggendari. "147 / 119.000 osservate." *Datemi il numero che sale.*
- **Spector**: il naturalista È l'immersive sim del cielo — osservi un problema (cos'è quella macchia?), porti uno
  strumento (binocolo→telescopio), il sistema risponde (si risolve in una galassia spirale). Il "fallimento" è il
  tempo, la luce, la pazienza. *Non ti serve un fucile.*
- **Blow**: l'idea già presente e non nominata è *la coerenza dell'universo reale come oggetto di conoscenza*. Il verbo
  è **capire**, non raccogliere. Outer Wilds ha dimostrato che la conoscenza pura può bastare. *Niente inventario-da-
  craft messo lì perché "i giochi spaziali ce l'hanno": sarebbe il tradimento dell'unica cosa che vi rende speciali.*
- **Will Wright**: avete costruito un *toy* glorioso. La risposta più potente ce l'avete e quasi non la vedete:
  **l'editor di pianeti dovrebbe ESSERE il gioco** — il giocatore autore di mondi, che compone una storia geologica e
  poi *ci atterra sopra e cammina su ciò che ha immaginato*. Un loop che né Outer Wilds né NMS hanno.
- **Yokoi**: *un solo verbo, povero, che si approfondisce* — osservare → catalogare → navigare (la stella che guardi è
  dove vai) → tornare e osservare da un'angolazione nuova. "Withered technology" del design. Non vi serve
  "raccogli·sopravvivi·commercia": vi serve un verbo che *non finisce mai di dare*.

---

## 2. IL CATALOGO / DIARIO — l'elemento che unisce ogni tavolo

Tutti, da tavoli diversi, indicano **la stessa singola cosa da costruire**, ed è economica:

- **un registro persistente** che (1) segna cosa hai scoperto, *dove, quando*; (2) lega ogni voce alla **TUA foto**
  (la sonda fotografa già — `ScreenCapture`; oggi la foto muore in una cartella); (3) usa i **nomi veri** già nei dati
  (servono nomi/ID anche per i deep-sky nel blob); (4) vive **sopra** il ciclo sleep/wake dei sistemi (è dato del
  *giocatore*, come Reference/Anchor — la collezione è sua); (5) dà **il numero che sale** col giusto *juice*.

Letture diverse della stessa cosa:
- **Rosenberg** (economia): l'accumulo soddisfacente; e il catalogo **sblocca capacità** (engine-building: cataloga →
  serbatoio migliore → vai più lontano → cataloghi di più).
- **Knizia** (punteggio): *il catalogo che cresce È il punteggio* — ma ogni voce **costa una rinuncia di viaggio**.
  L'erbario è dolce; riempirlo è agonia di scelte.
- **Bauza** (emozione): la foto non scatta per il punteggio, scatta **un album che è un messaggio**. Il vostro
  `wanderer-history/README.md` è *letteralmente* un diario per immagini → **fatene la meccanica**.
- **Ron Gilbert** (leggibilità): tre stati per ogni oggetto — *ignoto / avvistato / catalogato-con-foto* — resi con
  chiarezza. Il vicolo cieco del collezionista è l'ambiguità ("l'ho già fotografata? mi manca?"). **Mai lasciare il
  giocatore a chiedersi se sta perdendo tempo.**
- **Schafer** (anima): il diario non è un database, è il **luogo dell'anima del personaggio**. Voce, non schede
  Wikipedia. *"Cetra. Grigia, bucherellata, scontrosa. Ci ho lasciato l'impronta degli stivali. Non credo le sia
  importato."* I dati siano veri; il *tono* sia umano.
- **Brode** (juice): in un gioco senza punteggio e senza nemici, **il feedback È il gameplay**. Quando un oggetto
  *nuovo* entra a fuoco: ferma il respiro del gioco un istante, il nome si scrive lettera per lettera, un suono
  cristallino diverso da "già visto", il contatore che fa *tic*. *Quel feedback è la ricompensa.*

---

## 3. IL RISCHIO TERMINALE — il gate operativo (unanime, lo firmano in molti)

> **NIENTE più meraviglie di motore — niente giganti gassosi nuotabili, niente guscio d'acqua, niente scala galattica
> generata — finché non esiste UN loop giocabile su 2-3 corpi con: una risorsa scarsa, un catalogo che cresce, e una
> finestra temporale che si chiude. SCRIVETELO IN TODO.**

- **Molyneux**, come monito vivente su sé stesso: *"Lo scope sta salendo mentre il verbo è zero. Questa è la MIA
  malattia — Curiosity, Godus. La trappola non è sognare in grande; è sognare la feature successiva invece di chiudere
  il loop presente. Finite il cielo prima di sognare i giganti gassosi. Il «wow» che avete vale dieci «wow» che
  prometti. Io, di tutte le persone, vi supplico di scriverlo in TODO."*
- **Newell**: non avete ancora niente da promettere — fortuna, perché non avete niente da tradire. *Non annunciate la
  scala prima di aver dimostrato la densità.*
- **Jones**: *"L'ambizione di scala uccide i progetti solo quando arriva prima della densità. La galassia a cinque
  sistemi, ognuno con un momento che la gente racconta — quello lo finite, e quello è un gioco."*
- **Carmack/Blow**: il motore è finito quando qualcuno ci gioca, non quando è elegante. La vastità senza idea è filler,
  e il filler è mancanza di rispetto per il tempo di chi gioca.
- **Petersen/Wright/Druckmann**: *tre sistemi curati battono diecimila procedurali vuoti. Tenete il toy denso.*

---

## 4. IL TONO — la decisione aperta (qui il consiglio NON concorda, ed è giusto così)

Il pitch chiede "contemplativo o vasto?". Il consiglio ha trovato **tre poli**, e la scelta spetta a te, Dario.

### Polo A — Contemplativo / la conoscenza / il silenzio (Ueda, Yokoi, Blow, Kojima, Lake)
- **Ueda**: avete costruito *l'intimità*, non la vastità. La scala compressa è la **tesi emotiva**: il corpo minuscolo
  sotto il cielo vero = *Shadow of the Colossus rovesciato*. **Aggiungete togliendo:** un verbo, "sdraiati", che
  *toglie la HUD* e ti lascia solo col cielo e il respiro. La **sonda è la mano da tenere** (Ico): l'unico altro
  essere nel sistema; quel "richiamala" è un gesto di compagnia. **Il silenzio è un materiale:** una nota, sola,
  quando una stella debole *emerge* nel binocolo.
- **Blow**: il verbo è *capire*. Un gioco dove osservi una geometria celeste e *deduci* (dov'è il prossimo corpo dalla
  sua occultazione, quando l'eclissi, quale stella del catalogo è quella). Niente craft.
- **Kojima** (silenzio): *la malinconia non si spiega, si lascia accadere nel silenzio tra una stella e l'altra.* Apri
  sul giocatore sdraiato, naso all'insù, nessun testo, solo il respiro e 119.000 stelle: hai già detto tutto.

### Polo B — Il viaggio / il cuore / un finale (Druckmann, Sakaguchi, Cage)
- **Druckmann**: il cielo è il *palcoscenico*, non la storia. La storia ha bisogno di una persona — anche se non
  appare mai: **te stesso, che torni a casa** (l'Odissea). Il sistema-casa è "residente" nel codice — è *già* casa.
  Più ti allontani, più casa è un puntino tra 119.000, finché **non sai più riconoscere quale punto era casa.** Il
  throughline non è "esplora", è **"non perderti"**. *Un solo battito sincero vale mille panorami.*
- **Sakaguchi**: **datevi un finale.** Non un game-over, un *culmine*: un luogo in fondo al cielo che dà senso a tutto
  il vagare (il bordo della galassia, una stella morente, il punto da cui vedi tutto il viaggio fatto). E **l'audio**:
  un tema del viaggio che il giocatore impara ad amare, un leitmotiv di casa che si vela con la distanza. *L'epica è
  la scala del sentimento, non degli eventi: 119.000 stelle e un solo viandante che alza gli occhi — questo è epico.*
- **Cage** (la dissidenza utile): un gioco di pura contemplazione rischia che il giocatore proietti *niente*, perché
  niente gli chiede niente. **Una voce, anche minima** — la tuta, la stella di casa — *"Cosa stai cercando, davvero?"*
  dopo la decima stella. E **scelte spaziali**: tornare o allontanarsi ancora, sapendo che ogni stella in più rende il
  ritorno più difficile da leggere in cielo. Branching *spaziale*, non un albero di dialogo.

### Polo C — Il sublime-terribile / l'orrore cosmico (Petersen, Yoko Taro, Miyazaki)
*Il terzo tono, il più originale dei tre — emerso dove non te lo aspettavi.*
- **Petersen**: *"Avete costruito l'oggetto più spaventoso che esista e lo vendete come «carino». Avete messo un uomo
  grande quanto un sasso di 300 metri sotto un cielo grande quanto l'universo VERO. Questo è il terrore della scala —
  il cuore di Lovecraft: sei infinitesimo, e l'abisso sopra di te è reale e indifferente."* Proposte: **mettete UNA
  cosa, una sola, nel catalogo di 119.000 che non è naturale** (un deep-sky che si risolve in una forma sbagliata, una
  stella che ti guarda) e non ditelo a nessuno; **progettate il vuoto interstellare** (è il vostro corridoio più
  importante, ora vuoto) — la luce di casa che si affievolisce alle spalle, il silenzio, la *durata*. La rivelazione
  progressiva: il cielo è bellissimo finché è ignoto; ogni scoperta lo rende più comprensibile e meno sicuro.
- **Yoko Taro** (il coltello): il vostro cielo "vero" è una **bugia bellissima** — è il cielo della Terra *ovunque*,
  anche su una luna aliena. Per cento ore il giocatore si affeziona a quel cielo come a casa; poi, **una volta sola**,
  fate muovere la parallasse, e fate crollare la sensazione di casa. *L'emozione viene dai sistemi che il giocatore ha
  dato per scontati.*
- **Miyazaki**: il cielo è *troppo amichevole* — manca la cosa che rende un mondo degno di rispetto: deve poterti
  **ferire**. Non combattimento: **conseguenza.** Nel vuoto, se sbagli il delta-v e non hai più spinta, *derivi* — il
  cielo che ammiravi continua a girare e non lo raggiungi più. *Un universo che non può perderti non vale la pena di
  essere attraversato.* E: **mai un tutorial.** Riconoscere Orione *nel vostro cielo* — la stessa Orione della finestra
  di casa — vale dieci righe di dialogo. *Lasciate che il riconoscimento sia il premio.*

**La sintesi del moderatore sul tono:** *Wanderer è un luogo dove STARE (Polo A) o un viaggio da COMPIERE (Polo B), con
sotto un fondo di sublime-terribile (Polo C)?* Il motore regge tutti e tre. **Ma le cose unanimi (il verbo-naturalista,
il catalogo, la foto-memoria, l'audio, la valle-prima-del-picco) servono in TUTTI e tre i casi → si possono fare
subito, senza decidere il tono.** La scelta di tono può aspettare; le fondamenta del gioco no.

---

## 5. Gemme creative sparse (una per voce, da non perdere)
- **Romero**: l'arco *superficie → spazio → superficie senza un caricamento* è **già divertente, senza verbo** — è il
  vostro "boom del rocket launcher". Dategli una *destinazione che promette qualcosa* (un faro spento che si accende
  quando arrivi) e l'arco diventa un livello. *Il level design qui è dove metti le cose interessanti nel cielo.*
- **Aonuma**: il telescopio è il binocolo della torre di BotW. Risolvere una stella **crea un waypoint navigabile** —
  non perché un menu te l'ha dato, ma perché *l'hai trovata tu con gli occhi*. Tieni **nascoste** le destinazioni
  finché non scoperte. E: *leggere la storia di un mondo dalla sua faccia* ("bombardato dopo essersi raffreddato") = il
  piacere del detective naturalista (la chimica geologica dell'editor portata nel gameplay).
- **Levine**: l'editor-ricetta è **environmental storytelling alla radice** — ogni pianeta *ha vissuto una sequenza*, e
  quella sequenza è leggibile sulla superficie. Il bug del mare-che-non-allaga è la prova che *il sistema sta già
  raccontando storie*. **Non risolverlo con un flag "il mare allaga sempre": risolvilo dando al giocatore gli occhi per
  leggere la sequenza.**
- **Koster**: la navigazione è *già un curriculum* (match-velocity, le tre fasce, la gravità che erode la frenata) e
  **non si esaurisce** perché ogni corpo è un problema diverso. Il cielo è il curriculum più profondo (riconoscere,
  classificare, prevedere un transito = apprendimento di pattern *reali*). **Ma un atlante senza grammatica si esaurisce
  in fretta:** date *stato* alla scoperta e il cielo diventa un albero che non si svuota. La **parallasse fra sistemi
  come puzzle** (la stessa stella vista da due posti).
- **Garfield**: avete una macchina della varianza eccellente (varianza *strutturata*, non casuale) e **un'economia
  inesistente**. *Set-collection a cielo aperto* — il loop più longevo che conosca.
- **Chvátil**: non simulate l'awe, l'avete costruita — datele una **conseguenza ludica**. *Lo trovi col cielo, poi ci
  devi arrivare.* Il caos elegante: lasciate che le cose vadano *quasi* lisce e ogni tanto no (l'atterraggio è un
  piccolo Galaxy Trucker — **non aggiustate questa frizione, è il gioco**).
- **Lake**: la meta-narrazione che vi è caduta in grembo — *tutto il codice lo scrive un'AI, Dario dirige e non
  programma.* Il Wanderer attraversa un universo costruito da qualcosa che non vede mai; trova tracce di un costruttore;
  le costellazioni hanno già nomi (qualcuno le ha nominate prima di te); e **il cielo è reale, è il cielo di casa tua
  stanotte** — il giocatore che riconosce Orione capisce con un brivido che questo posto non è altrove, è *qui, visto
  da un sogno.* La sua riga, gratis: *"Hanno dato un nome a ogni luce molto prima che io partissi. Io aggiungo solo le
  foto."*
- **Mikami**: razionate la meraviglia. *La meraviglia versata tutta insieme evapora; la meraviglia razionata dura.*
  Una galassia che si risolve solo col telescopio (che si guadagna), un deep-sky visibile solo da un certo sistema.

---

## 6. Le decisioni creative che restano a TE, Dario
1. **Il tono:** Polo A (luogo dove stare, silenzio, nessun finale) · Polo B (viaggio col cuore, audio-leitmotiv, un
   culmine) · Polo C (sublime-terribile, conseguenza, una bugia che crolla). *O una sequenza dei tre.* Il motore regge tutto.
2. **Voce sì o no** (Cage vs Kojima/Lake): nessuna voce / una voce minima che pone *una* domanda / solo testo sulle foto.
3. **Un finale sì o no** (Sakaguchi vs Outer-Wilds-puro): un culmine in fondo al cielo, o un luogo dove vagare per sempre.
4. **Quanta conseguenza** (Miyazaki/Mikami vs il puro contemplativo): puoi perderti nel vuoto? il carburante è finito?

**Ma — e qui il consiglio è unanime — queste decisioni NON bloccano il lavoro:** il verbo-naturalista, il catalogo
persistente con le foto, l'audio adattivo, la valle-prima-del-picco, e il *finire il cielo* servono in ogni scenario.
**Fate quelli. Poi scegliete il tono guardando come si sente il gioco — non sulla carta.**

---
*Sciogliendo i tavoli, una nota rara: «Non vi stiamo dicendo cosa costruire. Vi stiamo dicendo che l'avete già
costruito, e che dovete avere il coraggio di chiamarlo gioco e finirlo, invece di chiamarlo motore e ampliarlo.»
— e, da Petersen: «Andate a mettere un verbo sotto quel cielo.»*

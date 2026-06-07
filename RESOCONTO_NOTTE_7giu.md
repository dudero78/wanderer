# Resoconto della notte — 7 giugno 2026

*Buongiorno Dario. Ecco tutto quello che ho fatto mentre dormivi, in ordine di lettura. Niente commit (come hai
chiesto). Tutto è nel working tree e nei file qui sotto.*

---

## 0. In una pagina (se hai fretta)

- **Album `wanderer-history` ripulito** e aggiornato fino a ieri: i ~190 screenshot sciolti nella root sono archiviati
  per giorno, i doppioni ravvicinati spostati nel **Cestino** (recuperabili), e ho aggiunto al diario i due giorni
  nuovi (06-06 "Vita e profondità", 06-07 "Il cielo stellato"). Ho anche **copiato** in album una selezione degli
  scatti del cielo di stasera che erano sul Desktop (Desktop lasciato intatto).
- **Cielo stellato — tutti i bug di stasera DOCUMENTATI in `TODO.md §🌌`** con le cause-radice. La più importante: i
  "blob" sono **lo stesso trap già nelle nostre lezioni dure — `Mathf.SmoothStep ≠ GLSL`** (ci sono ricascato). È una
  riga. Tutti i 4 punti parcheggiati hanno una diagnosi precisa, da fare insieme: **prima regola domani = reimport
  shader pulito e ri-test** (sospetto che stasera gli shader non si fossero ri-importati quando hai testato).
- **3 tavoli di audit fatti:** Architettura/Filosofia/Performance (`AUDIT4_ARCHITETTURA.md`), Codice/Bug
  (`AUDIT4_CODICE.md`), e ho **riletto gli audit precedenti** (AUDIT/2/3): molti loro "punti aperti" erano in realtà
  già risolti e committati — l'ho segnalato. Verdetto: fondamenta principal-grade, **performance con largo margine su
  M3 Pro Max** (sì, "se lo mangia a colazione"), rischi nei pezzi non-costruiti.
- **Il Gran Consiglio dei game designer (35 persone, 6 tavoli)** ha esplorato il codice e parlato. Due documenti:
  `CONSIGLIO_TECNICO.md` e `CONSIGLIO_CREATIVO.md`. **Hanno trovato il tuo verbo, all'unanimità** (sotto).

---

## 1. Pulizia album (FATTO)

`/Users/darioleone/Desktop/wanderer-history/` — album dei ricordi, una cartella per giorno:
- Archiviati i file sciolti: **06-03** (1 tenuto), **06-05** (33 tenuti), **06-06** (73 tenuti). I doppioni
  ravvicinati (~83 file) sono in `~/.Trash/wanderer-history-grezzi/` — recuperabili se ne vuoi qualcuno.
- **06-07 "Il cielo stellato"**: 25 scatti curati copiati dal Desktop (il Desktop non l'ho toccato — ci sono ancora
  gli originali, li sistemi tu se vuoi).
- `README.md` aggiornato con le due giornate nuove e il footer. Cartelle ora: 05-31 → 06-07.

## 2. Cielo stellato — cosa rifare domani (in `TODO.md §🌌`, dettaglio in `AUDIT4_CODICE.md`)

Hai parcheggiato la questione: la debugghiamo insieme. Ho diagnosticato tutto così domani è veloce.

1. **Blob/quadrati deep-sky** → **ROOT CAUSE: `Mathf.SmoothStep(1.0f,0.5f,r)` in `DeepSkyRenderer.FillTile`** non è la
   smoothstep di GLSL — è un LERP fra 1.0 e 0.5 → la "finestra" non scende mai sotto 0.5 sul bordo → bordi quadri.
   (Concorrono: gli angoli del quad cadono sulla cucitura dei tile + l'atlante è mippato → bleeding; e a forte zoom i
   DSO saturano a bianco pieno e diventano enormi.) **Fix:** finestra fatta a mano (`Smooth01`) + inset UV + atlante
   senza mipmap + `_MaxPx`→1200 + non saturare il gain.
2. **Telescopio** → l'etichetta dice "10×/50×" ma i fattori reali sono `{1,8,25}` (da allineare); il catalogo è sparso
   a forte zoom (serve la Via Lattea visibile come sfondo + reveal più generoso). Da ridisegnare il "feel" insieme.
3. **Via Lattea che "nuota"** → il codice attuale dovrebbe essere esatto (raggi non normalizzati). Sospetto forte che
   **gli shader non si fossero re-importati** quando hai testato. Prima regola: reimport pulito + ri-test. Se persiste:
   è calibrazione `_FlipU`/`_OffsetU`, o passiamo a Via Lattea su sfera-geometria.
4. **Costellazioni** → poche = dato (ho ~22 figure su 88; ne aggiungiamo). Linee: ora strisce morbide; se ancora
   seghettate, è reimport shader.

> **Lezione che mi segno (di nuovo):** ogni `smoothstep`-come-soglia in C# va fatta a mano (`t*t*(3-2t)`);
> `Mathf.SmoothStep` è un lerp. È già in memoria `[[wanderer-hud-navigazione]]`, e ci sono ricascato.

## 3. Gli audit tecnici (`AUDIT4_ARCHITETTURA.md`, `AUDIT4_CODICE.md`)

**Architettura — verdetto:** motore *insolitamente ben disciplinato*. Single-source-of-truth imposto meccanicamente
(parity gate), split #18 del renderer pulito, determinismo on-rails serio. **Performance: largo margine su M3 Pro Max**
— il cielo (119k stelle) è un non-problema (i quad invisibili collassano ad area nulla = zero overdraw); la leva sul
calore è il cap fps, e c'è tutta la banda del RenderScaler come headroom non sfruttato.

**I rischi NON sono nelle fondamenta, sono nei pezzi non costruiti** + un file che deriva:
- **`MapMode` (1186 righe) = prossimo god-object** → spaccarlo (MapVisuals/MapCameraRig/MapLabels).
- **Debito #1: l'altezza duplicata C#↔HLSL** — i gate sono una *rete*, non una *cura*.
- **Costruire ORA il giunto `ICelestialRenderer`** prima dei corpi volumetrici (gas/stelle), o il "vedi quella stella,
  puoi andarci" si infrange sul primo gigante gassoso.
- **Cablare un verbo banale** per mettere i sistemi sotto pressione.

**Riletti gli audit precedenti:** parecchi loro "punti aperti" (per-vertex color, eclissi GPU, starfield, region-stamp
uint…) **erano già fatti e committati** — i corpi di AUDIT3 sono uno snapshot vecchio. I bug davvero ancora aperti:
i 3 dell'editor di pianeti (mare non allaga / trasparenza acqua / bake fa sparire — fix-candidato non verificato), e
l'errore console benigno "Screen position out of view frustum". Tutto in `AUDIT4_CODICE.md §6`.

## 4. Il Gran Consiglio (`CONSIGLIO_TECNICO.md`, `CONSIGLIO_CREATIVO.md`) — la parte che ti interessa di più

Ho convocato i 35 a 6 tavoli (per sensibilità), ognuno ha **esplorato il codice davvero** e parlato con la sua voce.
Ce l'ho fatta a sentirli tutti. La cosa sorprendente: **una convergenza fortissima**, da Carmack a Knizia, da Ueda a Tajiri.

### Hanno trovato il tuo VERBO, all'unanimità:
> **OSSERVARE → RICONOSCERE → CATALOGARE → ANDARE → RICORDARE.** Il *naturalista dello spazio*. Non un ripiego
> contemplativo: l'asse su cui il gioco è **già allineato** (eclittica compresa).

Tajiri (Pokémon): *"Avete costruito il retino più grande mai fatto — un telescopio su 119.000 stelle vere — ma non
avete il Pokédex. Catturate gli insetti e li lasciate volare via."* Il gioco è **un Pokédex del cielo vero**, e
*insegna l'astronomia reale*. Spector lo chiama l'immersive sim del cielo; Blow "la conoscenza come verbo"; Wright
"l'editor di pianeti DOVREBBE essere il gioco".

### La frase che ha attraversato tutti i tavoli:
> ***"La stella che ammiri è la stella dove vai."*** È il cuore tematico (Levine), abolisce la differenza tra desiderio
> e meta (Kojima), è il "vedi quella montagna" reso cosmico (Aonuma). **È la spina dorsale. Coltivala.**

### La cosa singola da costruire (la indicano TUTTI, ed è economica):
**Un catalogo/diario persistente** che registra cosa hai scoperto (dove, quando), **lega ogni voce alla TUA foto**
(la sonda fotografa già — oggi la foto muore in una cartella), usa i **nomi veri** già nei dati, vive sopra il ciclo
sleep/wake (è dato del giocatore), e dà **il numero che sale** col giusto *juice* (Brode: in un gioco senza nemici, il
feedback È il gameplay).

### Il rischio terminale, unanime — il GATE da scrivere:
> **Niente più meraviglie di motore (giganti gassosi, guscio d'acqua, scala galattica) finché non esiste UN loop
> giocabile su 2-3 corpi.** Molyneux, *contro sé stesso*: *"È la MIA malattia — sognare la feature successiva invece di
> chiudere il loop presente. Il «wow» che avete vale dieci «wow» che promettete. Io, di tutte le persone, vi supplico
> di scriverlo in TODO."* (Lo raccomanda anche `AUDIT.md` da tempo.)

### L'omissione più grave secondo loro NON è il verbo: è l'AUDIO.
Sakaguchi (Final Fantasy): nessuna riga sul suono in tutto il progetto. Un sistema di **musica adattiva** + un
**leitmotiv di casa che si vela con la distanza** è lo strumento a più alto rapporto emozione/costo, vive *dentro* il
seamless, ed è il momento (lo sbucare nello spazio) che il giocatore racconterà. **Da fare prima del verbo.**

### Il tono resta una TUA scelta (tre poli, il consiglio non concorda apposta):
- **A — Contemplativo / silenzio / conoscenza** (Ueda, Yokoi, Blow, Kojima): un luogo dove *stare*.
- **B — Viaggio / cuore / un finale** (Druckmann "non perderti / ritrova casa", Sakaguchi "datevi un culmine + audio",
  Cage "una voce minima").
- **C — Sublime-terribile / orrore cosmico** (Petersen: *"avete messo un uomo grande quanto un sasso di 300 m sotto un
  cielo grande quanto l'universo VERO — è il terrore della scala di Lovecraft, e lo vendete come «carino»"*; Yoko Taro:
  *"il vostro cielo «vero» è una bugia bellissima — rompetela una volta sola e fate male"*; Miyazaki: *"il vuoto deve
  poterti perdere"*).

**Il punto chiave (sollievo):** le cose unanimi (verbo-naturalista, catalogo+foto, audio, valle-prima-del-picco,
finire il cielo) **servono in TUTTI e tre i toni** → si fanno subito, senza decidere il tono. La scelta di tono può
aspettare di *sentire* il gioco.

## 5. La mia raccomandazione (sintesi di audit + consiglio), in ordine

1. **Finire il cielo** (i 4 bug, insieme — sono diagnosticati, è poco). Prima: reimport shader pulito.
2. **Caricare di senso la foto della sonda** (timestamp, coordinate, nome stella) — il primo mattone del catalogo, costo quasi nullo.
3. **Mostrare "la stella che guardi è dove vai"** (nome/evidenziatore nel binocolo) — la spina dorsale, resa esplicita.
4. **Il catalogo/diario persistente** (il verbo) — su 2-3 corpi, con una risorsa scarsa (delta-v) e le finestre temporali (eclissi/transiti, che il `SimTime` calcola già gratis).
5. **Un sistema di musica adattiva + leitmotiv di casa.**
6. **Scrivere il gate anti-scope-creep in TODO** (l'hanno chiesto in coro).
7. *Poi*, quando il loop si sente, scegliere il tono e affrontare i pezzi grossi (giunto volumetrico, atmosfera/HDR, ecc.).

## 6. Dove leggere cosa
- **Bug del cielo + retest:** `TODO.md` (sezione 🌌 in cima).
- **Audit tecnici:** `AUDIT4_ARCHITETTURA.md`, `AUDIT4_CODICE.md`.
- **Il consiglio:** `CONSIGLIO_CREATIVO.md` (le idee, da leggere con calma e un caffè — c'è dentro tanta roba bella) e
  `CONSIGLIO_TECNICO.md` (le considerazioni di motore/sistemi). Il pitch che ho dato loro: `PITCH_CONSIGLIO.md`.
- **Album:** `~/Desktop/wanderer-history/README.md`.

*Niente commit fatti stanotte (come da richiesta). Tutto è nel working tree, pronto per quando vuoi committare tu.
Buon risveglio. È stata una bella notte di lavoro: il gioco ha un cielo vero, e adesso — a sentire 35 leggende —
ha anche un verbo che lo aspettava già sotto.*

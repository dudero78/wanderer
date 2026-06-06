# Prompt per la prossima sessione (copia-incolla)

> **Prima di incollarlo:** per farmi fare in sicurezza ANCHE il backlog shader, **chiudi Unity** prima di iniziare
> (così posso compilare-verificare tutto in batchmode). Se lo lasci aperto, faccio solo la parte C#. Poi sostituisci
> `[CHIUSO]` / `[APERTO]` nel prompt qui sotto secondo cosa hai fatto.

---

Ciao! Riprendiamo **Wanderer** (la cartella `Wanderer`). Lavora in **AUTONOMIA TOTALE** su tutto il backlog tecnico,
**senza fermarti e senza aspettare mie conferme** (io non ci sono / dormo). Abbiamo un checkpoint git stabile: alla
peggio torniamo indietro, quindi vai tranquillo.

**1) Contesto — leggi prima:** `CLAUDE.md`, la tua memoria, `TODO.md` (la sezione **"🚀 PROSSIMA SESSIONE"** è il
backlog prioritizzato), `AUDIT3.md`, `STARSYSTEM_DESIGN.md`, `REPORT_NOTTE_6giu.md`. Lo stato stabile è la cima di `git log`.

**2) Verifica shader (FONDAMENTALE):** ho lasciato Unity **[CHIUSO / APERTO]**.
- Se **CHIUSO**: compila script **E shader** in batchmode e verifica tutto →
  `"/Applications/Unity/Hub/Editor/6000.4.9f1/Unity.app/Contents/MacOS/Unity" -batchmode -quit -projectPath . -logFile - 2>&1 | grep -iE "error|Shader error"`
  (niente errori = pulito). Con questo **fai anche il backlog shader in sicurezza**.
- Se **APERTO**: ricrea il gate di compilazione C# offline (istruzioni in `TODO.md` → "Ricreare il gate C# offline")
  e fai i punti **C#**; per gli shader leggi `~/Library/Logs/Unity/Editor.log` (cerca `Shader error`), e se non li vedi
  compilare lascia stare gli shader.

**3) Fai, in ordine di priorità** (`TODO.md` → "🚀 PROSSIMA SESSIONE"): **colore per-vertice → PBR**, **uint region-stamp**
(togli il limite corpi), **STARSYSTEM Tappe 3-5 + sonda alla Outer Wilds**, `_HAS_SEA`, occupancy. Per ognuno: leggi i
file citati, implementa con cura, verifica (compile-gate C# sempre; shader se posso). **Lascia stare solo:** la parte
**ARTE/Prodotto** (cielo, atmosfera, bloom, sole-sfera: è direzione artistica MIA) e il **#17 transpiler** (bassa urgenza,
la parità è già protetta dal gate).

**4) Regole:**
- **Committa i checkpoint logici man mano, ma NON fermarti MAI ad aspettare una mia conferma** — i commit non hanno
  bisogno di approvazione, vai avanti senza pause. Committa **solo i tuoi file** (codice + doc), lascia fuori le mie
  ricette `*.json` e le scene `*.unity`. Messaggi in italiano, marca chiaramente i commit con **shader non verificati**.
- Tieni **sempre** un working tree che compila. Se un cambio shader rischia di rompere il default, mettilo dietro una
  keyword default-off o committalo isolato (revertibile da solo).
- Aggiorna `TODO.md`/`CLAUDE.md`/memoria man mano.
- Alla fine: **un report finale dettagliato** (cosa fatto/verificato, cosa lasciato e perché).

Vai, e non fermarti finché non hai esaurito il backlog (o il budget). Grazie!! 🚀

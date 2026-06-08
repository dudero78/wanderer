---
description: Chiusura sessione Wanderer — igiene git + aggiornamento memoria e documentazione per ripartire senza perdere nulla
---

Esegui la procedura di **fine sessione** per il progetto Wanderer. Obiettivo: lasciare tutto committato e la memoria/documentazione aggiornate, così la prossima sessione riparte senza perdere contesto. Lavora con cura, non di fretta.

## 1. Igiene git
- `git status` e identifica le modifiche **non committate**, ESCLUDENDO sempre:
  - i **4 file vietati** (MAI committare): `Assets/Scenes/Game.unity`, `Assets/Scenes/PlanetEditor.unity`, `Assets/Resources/Planets/Valentina2.json`, `Assets/Resources/Planets/terra-test3.json`;
  - i **blob pesanti gitignored** (`dso_atlas.png`, `MilkyWay*.png`, `deepstars.bytes`, `BakedPlanet*`, `/StarData/`) — sono rigenerabili, restano fuori.
- Committa il **resto** (codice, shader, blob piccoli tracciati come `stars.bytes`/`dso.bytes`/`constellations`) con messaggi chiari in italiano. Se sei su un branch di lavoro e c'è da mergiare, chiedi prima a Dario. Termina i commit con il `Co-Authored-By` del modello.
- Verifica alla fine che `git status` sia pulito (a parte i file esclusi sopra).

## 2. Memoria (`/Users/darioleone/.claude/projects/-Users-darioleone-Desktop-Projects-games/memory/`)
- Ripensa a **cosa è cambiato in questa sessione** (decisioni, architettura, trappole risolte, comandi nuovi).
- Aggiorna i file di memoria rilevanti (tipicamente `wanderer-*`). **Stile (istruzioni globali di Dario):** riscrivi PULITO, come prima versione — niente note difensive, niente "prima era X / ora Y", niente caveat. Ogni informazione c'è perché serve.
- Registra solo ciò che è **non-ovvio e durevole**: NON ciò che il codice/git già dicono. Le trappole e i "perché" hanno priorità.
- Controlla **duplicati/obsoleti**: se una memoria è superata, aggiornala o segnala a Dario di cancellarla.
- Aggiorna la **riga indice** corrispondente in `MEMORY.md` (una riga, hook breve).

## 3. Documentazione in repo
- **`CHANGELOG.md`**: aggiungi una voce sintetica con il lavoro della sessione (le cose degne di nota).
- **`CLAUDE.md`** (sezione "Stato attuale"): se il blocco in cima è ormai datato rispetto a quello che è successo, **riscrivilo pulito** (stesso stile delle istruzioni globali) così riflette lo stato reale. Non patcharlo con parentesi.
- **`TODO.md`**: spunta ciò che è stato fatto, aggiungi gli eventuali punti rimasti/rimandati (con il motivo).
- Committa anche questi aggiornamenti (non sono tra i file vietati).

## 4. Report finale
Riassumi a Dario, in poche righe: branch e stato git (pulito?), quanti commit, quali memorie/doc aggiornate, ed eventuali **punti rimandati** o cose da non dimenticare alla ripresa. Concludi confermando che si può ripartire senza perdere nulla.

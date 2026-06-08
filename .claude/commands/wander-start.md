---
description: Ripresa sessione Wanderer — recupera tutto il contesto da dove avevamo lasciato e chiede da dove ripartire
---

Sto riprendendo a lavorare su Wanderer. Recupera **in autonomia** tutto il contesto necessario per ripartire da dove avevamo lasciato, **senza che io debba spiegarti niente** e senza perdere nulla della sessione precedente (chiusa con `/wander-end`). Poi **chiedimi da dove voglio ripartire**. Sii rapido e concreto: l'obiettivo è essere operativo, non un riassunto enciclopedico.

## 1. Raccogli il contesto (FAI questi passi, non a memoria)
- Le **memorie** `[[wanderer-*]]` sono già caricate in questa sessione: tienine conto per lo STATO e le DECISIONI prese (project, performance/architettura, terreno, cielo, navigazione, fisica, "come lavorare con Dario"). Rispetta le **"Lezioni dure"** del `CLAUDE.md` (non ripeterle: evita di rifare quegli errori).
- Esegui **`git log --oneline -15`** e **`git status`**: cosa è stato fatto di recente, su quale **branch**, e se il working tree è **pulito** (la sessione precedente dovrebbe essere stata chiusa con `/wander-end` → dovrebbe esserlo, a parte i 4 file vietati e i blob gitignored).
- Leggi le **ultime voci** del `CHANGELOG.md` e le sezioni **"prossima sessione" / punti aperti / rimandati** del `TODO.md` — è lì che sta la coda di lavoro e ciò che è stato rimandato (con motivo).
- Se esiste `NEXT_SESSION_PROMPT.md`, leggilo.

## 2. Sintesi operativa (BREVE)
In poche righe: **dove siamo** (stato + branch), **cosa è stato fatto nell'ultima sessione**, **cosa è aperto/rimandato** (col motivo). Niente preamboli generici: solo ciò che serve per ripartire. Se il working tree NON è pulito o c'è qualcosa di sospeso/incoerente, **segnalalo subito**.

## 3. Chiedi da dove ripartire
Con **`AskUserQuestion`**, proponi le opzioni naturali ricavate dai punti aperti/rimandati e dalla "prossima sessione" del TODO (l'opzione "Altro" è automatica). **Non iniziare a lavorare** finché non ho scelto.

## Vincoli da ricordare (da memorie/CLAUDE.md)
- **Priorità di fase:** resa grafica + qualità + performance; il "VERBO"/mini-loop è in fondo alla lista (a meno che io non dica il contrario).
- **Non committare MAI** i 4 file: `Game.unity`, `PlanetEditor.unity`, `Valentina2.json`, `terra-test3.json`. Commit solo quando lo chiedo.
- **Parla divulgativo** (Dario impara il game-dev: ogni termine tecnico con la sua spiegazione). Direzione artistica e decisioni di design sono sue.
- Misura prima di concludere su performance (Profiler/build, non l'editor).

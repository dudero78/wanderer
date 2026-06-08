---
description: Audit del progetto Wanderer — due tavoli di massimi esperti (architettura/filosofia + codice/igiene/bug), con verifica avversariale e report finale
---

Convoca DUE tavoli di **massimi esperti** per scandagliare l'intero progetto Wanderer e dire se è **solido come una roccia** o dove conviene intervenire (debolezze, rischi, approcci sbagliati, bug). Lavora a fondo e con PROVE (riferimenti `file:riga` o comportamenti osservabili), mai a impressioni.

## Regole di metodo (valgono per entrambi i tavoli)
- **Prove, non opinioni.** Ogni rilievo cita file/righe o un comportamento concreto. Niente generico ("si potrebbe migliorare").
- **Verifica avversariale.** Prima di mettere un rilievo nel report, mettilo alla prova: un secondo esperto-scettico prova a smontarlo (è reale? è raggiungibile? è già gestito altrove?). Scarta i falsi positivi.
- **Niente regressioni inventate.** Confronta SEMPRE con le "Lezioni dure" del `CLAUDE.md` e le memorie `[[wanderer-*]]`: NON ri-segnalare scelte già decise/risolte (es. CDLOD puro, skybox-all'infinito, on-rails Keplero) a meno che siano davvero REGREDITE.
- **Misura prima (lezione del progetto).** Dove un giudizio di performance/calore NON è deducibile dal codice, NON darlo per certo: segnalalo come "DA MISURARE" (Unity Profiler / build standalone, non l'editor). Sul progetto è già successo che "la GPU non era il collo".
- **Severità + azione.** Ogni rilievo = **[BLOCCANTE / ALTO / MEDIO / BASSO]** + fix concreto + sforzo stimato (S/M/L).
- **Distingui** ciò che è VERIFICATO da ciò che richiede indagine/misura.

## Tavolo 1 — Architettura & filosofia di gioco
Domanda guida: i sistemi sono solidi e allineati agli **obiettivi ambiziosi** (seamless alla Outer Wilds → ampiezza No Man's Sky)? Un **MacBook M3 Pro Max** dovrebbe "mangiarselo a colazione": è così, e dove sono i veri margini/colli?
Esamina TUTTI i sistemi. Sedute consigliate (una seduta = un esperto, in PARALLELO):
- **Resa/GPU**: quadtree CDLOD su GPU, renderer autoritativo + fallback, cielo stellato (culling a celle, VRAM), RenderScaler, draw indirect, overdraw.
- **Fisica & precisione**: floating origin / doppia precisione, Kepler on-rails, baricentri, hand-off di gravità, riferimenti.
- **Streaming del mondo**: sveglia/dormienza dei sistemi, caricamenti graduali, multi-sistema, interesse/isteresi.
- **Cielo stellato**: architettura, campo profondo a celle, memoria, allineamento, scalabilità.
- **Game design / il loop & il "VERBO"**: cosa manca all'MVP, coerenza con la visione, rischio dei pezzi NON costruiti (gas/stelle volumetriche, il VERBO).
- **Performance & margine**: dove sono i colli VERI, cosa scalda (CPU main vs GPU), cosa va misurato, scalabilità (più corpi/sistemi/stelle).
Per ciascuna: punti di forza · debolezze/rischi · approcci sbagliati · **regge l'ambizione?**

## Tavolo 2 — Codice, igiene, bug
Sedute (in PARALLELO):
- **Correttezza & bug latenti**: edge case, null/aliasing, off-by-one, race/ordine di init, **divergenze CPU↔GPU/HLSL** (fonte altezza duplicata!), unità/frame.
- **Igiene**: duplicazione, god-object e file enormi (es. `MapMode`), dead code, naming, boilerplate ripetuto.
- **Robustezza & build-safety**: fallback e guardie, errori a runtime, **Always Included shaders**, scene nei Build Settings, varianti keyword strippate, gate di parità.
- **Shader/Metal**: trappole note (`Mathf.SmoothStep≠GLSL`, `/0` nei `?:`, point size), precisione, parità.
- **Igiene dati & git**: cosa NON deve essere committato (i 4 file vietati + blob pesanti gitignored), coerenza `.gitignore`.
Verifica avversariale di OGNI bug "trovato": è reale e raggiungibile in gioco, o teorico?

## Verifica degli audit precedenti
Leggi `AUDIT3.md`, `AUDIT4_*.md`, `CONSIGLIO_*.md`, `TODO.md` e le memorie. Per ogni punto rilevante stabilisci: **RISOLTO / APERTO / REGREDITO**. Riporta al tavolo i task mancanti o le raccomandazioni ancora valide (specie quelle "rimandate con motivo").

## Report finale
Scrivi **`AUDIT_<data>.md`** (data da `currentDate`) nella radice del repo, con:
1. **Sintesi esecutiva** — lo stato in poche righe + il verdetto: *roccia* o *dove sono le crepe*.
2. **Tavolo 1** — rilievi ordinati per severità (prove + fix + sforzo), e il giudizio "regge l'ambizione / l'M3 se lo mangia a colazione?".
3. **Tavolo 2** — idem.
4. **Stato audit precedenti** — risolti / aperti / nuovi.
5. **Lista d'azione PRIORITIZZATA** — cosa fare prima, cosa rimandare e perché.
Scrivi PULITO (stile delle istruzioni globali: niente note difensive). Alla fine, riassumi a Dario il verdetto in 5 righe e chiedi se vuole che apra subito i fix prioritari.

## Orchestrazione & scala
Spawna le sedute IN PARALLELO con il tool **Agent** (lettura/ricerca read-only) per coprire molto in fretta, poi fai la verifica avversariale e sintetizza tu. Tieni un numero ragionevole di agenti (≈5–6 per tavolo). **Per un audit ancora più esaustivo e avversariale** l'utente può aggiungere **"ultracode"** alla richiesta → allora usa un **Workflow** (fan-out per dimensione → verifica avversariale a più voti → sintesi con critico di completezza). Non spawnare decine di agenti se l'utente non ha chiesto quella scala.

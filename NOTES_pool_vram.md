# Note — VRAM del pool GPU (A/B fatti) e guardia NaN (C fatto)

A, B e C sono **implementati e committati**. Qui resta cosa VERIFICARE quando fai girare il gioco, e i dial che
puoi muovere. Il numero da guardare è il log all'avvio: `[GpuPlanet] pool VRAM CONDIVISO ~X MB ...`.

Promemoria numeri (da `GpuPlanetRenderer`): VRAM del pool = `maxSlabs × vertsPerSlab × 48 byte`, con
`vertsPerSlab = (nodeRes+1)² + 4·nodeRes`. L'osservazione chiave: **un solo corpo è "attivo" (si suddivide in
profondità) per volta**; gli altri stanno alle radici. Quindi un pool basta per tutti.

---

## A) Pool condiviso fra i corpi — FATTO (commit 96af374)

I 6 buffer geometria + la free-list + la cache LRU sono ora **statici e refcountati**: UNO per tutti i corpi
invece di uno ciascuno. Da `~843 MB × 6 ≈ 5 GB` a **~843 MB totali** (poi ridotti da B). I campi `posBuf…surfBuf`
restano come alias dei buffer condivisi, quindi il resto del codice è invariato. La chiave di cache include
`bodyId` così due corpi non collidono; `AcquirePool`/`ReleasePool` fanno il refcount.

**Da verificare (con te alla tastiera):**
- All'avvio il log `[GpuPlanet] pool VRAM CONDIVISO ~… MB` deve comparire **una volta sola** (il primo corpo
  alloca, gli altri riusano), non sei volte.
- **Tutti e 6 i corpi si disegnano.** Vola fra due asciutti e i due col mare (terra-test3 / Valentina2): niente
  forme sbagliate o lampeggi (= niente collisione di chiave nel pool condiviso).
- Avvicinandoti a un corpo il dettaglio si infittisce come prima; allontanandoti e andando su un altro, il nuovo
  prende dettaglio (il pool "presta" le fette al corpo attivo).

## B) Tetto di nodeRes 128 → 96 — FATTO (commit 96af374)

`gpuSurfaceRes=256` veniva clampato a 128; ora il clamp è a **96**. La VRAM del pool cala di ~1.8× per fetta
(con A: ~843 → ~460 MB totali) con un calo di dettaglio modesto (il quadtree si suddivide comunque per distanza).

**È un DIAL** (una riga in `GpuPlanetRenderer.Setup`, `Mathf.Clamp(res, 4, 96)`):
- **128** = dettaglio pieno, più VRAM (com'era prima);
- **96** = scelta attuale, compromesso;
- **64** = ~4× meno VRAM, più grossolano vicino ai piedi.
La direzione artistica (quanto fine vuoi il terreno calpestabile) è TUA: se 96 ti sembra meno nitido del dovuto,
rimettilo a 128; se la VRAM è ancora alta e il dettaglio regge, scendi a 64. Guarda il log per misurare l'effetto.

## C) Guardia NaN nel compute — FATTO (commit 95898f2)

I 4 `normalize(cross(...))` delle normali (superficie + fondo, kernel per-nodo e batch) ora hanno il fallback
radiale se la cella è degenere (`|cross|² ~ 0` → usa `dir`), così non producono NaN (shading nero). Cambia
l'output solo nel caso degenere.

**Da verificare:** i pianeti si illuminano come prima (nessuna macchia nera nuova). NB: il compute compila i
kernel al primo dispatch in Play — se ci fosse un refuso HLSL lo vedresti nella Console al primo avvio (la modifica
è banale, ma è l'unico pezzo non verificato a compilazione perché Unity era in background).

---

## Se vuoi spingere oltre la VRAM (opzionale, non fatto)

I buffer `bedNrm/depth/surf` servono SOLO ai corpi col mare, ma il compute li scrive sempre → per saltarli sui
corpi asciutti servirebbe guardare le scritture E le letture nel vertex su `_HasSea` (rischio lettura fuori-bounds
su Metal). Vale ~40% sui corpi asciutti, ma è un'altra sessione con te presente: non l'ho toccato.

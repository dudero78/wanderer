# Note — fix VRAM del pool GPU (#2) e altri pezzi da fare alla tastiera

Queste sono le cose che **non ho spedito alla cieca** mentre dormivi, perché il loro modo di rompersi è
"pianeti invisibili o corrotti" e non potresti diagnosticarlo da solo. Sono surgically descritte qui: con te
alla tastiera (così vedi subito se i pianeti si disegnano ancora) è lavoro corto.

Riferimento numeri, dal codice reale (`GpuPlanetRenderer.Setup`):
- VRAM del pool per corpo = `maxSlabs × vertsPerSlab × 48 byte`, con `vertsPerSlab = (nodeRes+1)² + 4·nodeRes`.
- In gioco `gpuSurfaceRes = 256` ma `Setup` lo **clampa a 128** (`Mathf.Clamp(res, 4, 128)`), quindi `nodeRes = 128`,
  `maxSlabs = 1024` → **~843 MB/corpo × 6 corpi ≈ 5 GB**, allocati TUTTI alla costruzione della scena.
- Ho aggiunto un log all'avvio: `[GpuPlanet <nome>] pool VRAM ~X MB (...)`. **Usa quel numero come metro**: qualunque
  cosa cambi qui sotto, riapri e leggi quanto scende.

L'osservazione chiave che rende tutto semplice: **un solo corpo è "attivo" (si suddivide in profondità) per volta.**
I corpi lontani restano alle radici (~6 fette ciascuno). Eppure oggi ogni corpo pre-alloca un pool intero da 843 MB.

---

## A) Fix vero: POOL CONDIVISO fra i corpi (5 GB → ~850 MB) — consigliato

Un solo pool serve il working-set del corpo attivo (~700 fette, vedi i log `visibili=`) + le poche radici degli altri
(~6 × 5 = 30). Totale ~730 < 1024 → **un pool unico basta per tutti e sei**.

Passi (in `GpuPlanetRenderer.cs`):
1. Sposta in una classe statica condivisa `SlabPool` (refcount): i 6 buffer geometria (`posBuf, nrmBuf, bedNrmBuf,
   depthBuf, fieldBuf, surfBuf`), `jobsBuf`, e le strutture di allocazione (`freeSlabs`, `cacheSlab`, `cacheClock`,
   `clock`). `idxBuf`, `slabOfInstance`, `argsBuf`, `roots`, `nodePool`, `visibleScratch`, `mat`, `cs` restano **per-corpo**.
2. `SlabPool.Acquire(maxSlabs, vertsPerSlab)`: alloca i buffer la PRIMA volta, incrementa un refcount; `SlabPool.Release()`
   decrementa e libera solo a refcount 0. Sostituisci le `new GraphicsBuffer(...)` del pool in `Setup` e i `Release()`
   in `OnDestroy` con queste.
3. Ogni corpo continua a fare `cs.SetBuffer(k, "_VPos", SlabPool.posBuf)` ecc. (il `cs` resta per-corpo, così niente
   clobber di uniform) e `mat.SetBuffer("_VPos", SlabPool.posBuf)` ecc.
4. **Chiave di cache per-corpo**: `Key(nd)` oggi non include l'identità del corpo → con un pool condiviso due corpi
   collidono. Aggiungi un `int bodyId` (assegnato in `Setup`, es. un contatore statico) e mettilo nei bit alti della
   chiave. (Controlla che `cacheSlab`/`cacheClock` siano ora condivisi → la collisione è reale senza questo.)
5. `VerifyBatchFill` e `VerifyParityRuntime` continuano a funzionare: ogni corpo pesca fette distinte dalla free-list
   condivisa, quindi i root di corpi diversi non si sovrascrivono.

Test (con te presente): avvia, **tutti e 6 i corpi si disegnano**? Vola fra due corpi col mare (terra-test3/Valentina2)
e due asciutti: niente lampeggi/forme sbagliate (= niente collisione di chiave). Il log VRAM ora deve stampare ~843 MB
**una volta** (il primo corpo alloca, gli altri riusano). Distruggi/ricrea la scena: nessun crash (refcount giusto).

Insidia: stato statico mutabile condiviso fra istanze è un piede di porco — assicurati che `Release` a refcount 0
azzeri anche le `Dictionary`/`Stack` statiche, o un secondo avvio nello stesso processo (editor) parte sporco.

---

## B) Leva rapida e parziale: abbassare `nodeRes` (la tua chiamata, cambia il DETTAGLIO)

`vertsPerSlab ∝ nodeRes²`. Portare `gpuSurfaceRes` da 256 (=128 dopo il clamp) a **96 o 64** taglia la VRAM di
~1.8× / ~4× **per corpo** (843 → ~470 / ~210 MB) **senza** la chirurgia del pool condiviso. NB: è anche un campo
serializzato in `Game.unity`, quindi cambialo nell'inspector del `GameBootstrap` (o ri-crea la scena), non basta
il sorgente.

Costo: ogni nodo-foglia diventa più grossolano → il dettaglio più fine cala, a meno di alzare `maxDepth` 6→7 (il
quadtree si suddivide un livello in più vicino alla camera, recuperando dettaglio con qualche nodo in più). **Questo
tocca il LOOK**, che è dominio tuo (liscio vs dettagliato): non l'ho deciso io. È la via veloce se vuoi un taglio
subito; A) è la via "giusta" che non tocca la resa. Si possono anche combinare.

---

## C) Guardia NaN nel compute (preventiva) — `PlanetHeight.compute`

Quattro `normalize(cross(...))` (righe ~30, 40, 105, 115): se due campioni vicini coincidono (cella degenere) il
`cross` è zero e `normalize` dà **NaN** → shading nero/sporco in quel punto. Oggi non si vede (gli step sono > 0),
è difesa preventiva. Fix per ciascuno:

```hlsl
float3 cr = cross(pYp - p, pXp - p);
float3 nrm = (dot(cr, cr) > 1e-20) ? normalize(cr) : dir;   // fallback radiale se degenere
```

(idem per la normale del fondo `bn`, fallback a `dir`). È un'edit di shader: la lascio a te perché un refuso HLSL
non compila → pianeta invisibile, e qui non posso compilare per verificare. Test: i pianeti si illuminano come prima.

---

## Stato di stanotte (già fatto e sicuro, in `git diff`)

- `PlanetRecipeUniforms.cs` (nuovo) + 4 siti unificati: colore in un posto solo. Comportamento **invariato**.
- `OnDestroy` che libera le Mesh in `PlanetQuadtree` e `SingleMeshPlanet` (leak chiusi).
- `VerifyParityRuntime()` in `GpuPlanetRenderer`: gate CPU↔GPU all'avvio (LogError se la mesh GPU diverge da
  `SampleHeight` oltre 0.5 m). Non bloccante.
- Log della VRAM del pool all'avvio (il metro per A/B).

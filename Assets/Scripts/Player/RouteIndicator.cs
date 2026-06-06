using UnityEngine;

/// <summary>
/// Reticolo di rotta stile Outer Wilds sul corpo SELEZIONATO (solar.Destination). Quando è in vista gli
/// disegna intorno un anello a parentesi con un chevron in cima, un pip al centro, e a lato distanza +
/// velocità di avvicinamento (col SEGNO). Il marker chiave è il VETTORE VELOCITÀ (⊕) piazzato nel punto
/// di fuga della velocità relativa: quando si sovrappone al bersaglio sei in rotta d'intercetto, è lo
/// strumento per pilotare verso un corpo. Quando la velocità relativa è ~0 il reticolo diventa VERDE
/// (sincronizzato: puoi puntare e andare dritto). Fuori vista, una freccia al bordo punta verso il corpo.
///
/// Tutto procedurale: le texture sono generate da codice una volta all'avvio (~KB, niente asset). Disegnato
/// in OnGUI (overlay), quindi sta sopra al RenderScaler. I bordi morbidi vengono da una smoothstep scritta
/// a mano — Mathf.SmoothStep di Unity interpola l'OUTPUT, non soglia l'input, e riempirebbe la texture.
/// </summary>
public class RouteIndicator : MonoBehaviour
{
    Camera cam;
    PlanetWalker walker;
    SolarSystem solar;
    MapMode map;     // per disegnare il reticolo anche in mappa (sul corpo selezionato), con la sua camera
    Camera view;     // camera ATTIVA in questo frame: quella del giocatore, o quella della mappa se aperta

    Texture2D ringTex, chevronTex, discTex, progradeTex, retroTex, barTex, crossTex, triTex;
    GUIStyle label;
    Material invertMat;   // mirino a INVERSIONE del colore di sfondo (sempre visibile su chiaro e scuro)

    // SONDA: il tracker della sonda riusa la stessa logica robusta del reticolo (proiezione/edge/distanza), ma con
    // forma e colore DIVERSI per distinguerla a colpo d'occhio → triangolo AMBRA (il corpo selezionato è anello blu/verde).
    public Probe ProbeTarget;

    // Tavolozza: blu di riposo, verde quando sincronizzato o in intercetto.
    static readonly Color Blue = new Color(0.55f, 0.72f, 1f, 1f);
    static readonly Color White = new Color(0.9f, 0.96f, 1f, 1f);
    static readonly Color Green = new Color(0.42f, 1f, 0.55f, 1f);
    static readonly Color Cyan = new Color(0.5f, 0.95f, 1f, 1f);
    static readonly Color Amber = new Color(1f, 0.72f, 0.2f, 1f);   // "frena ora": ti avvicini al punto di non ritorno
    static readonly Color Red = new Color(1f, 0.36f, 0.3f, 1f);     // superato: non freni più in tempo

    const float SyncSpeed = 1.0f;        // |velocità relativa| sotto cui il reticolo è "sincronizzato"
    const float AirborneAlt = 3f;        // sopra questa quota mostri la velocità (a terra è l'orbita del pianeta)
    const float ReactionTime = 1.5f;     // margine di reazione umano (s) sommato allo spool del freno nella gauge di frenata
    const float WarnMinClosing = 50f;    // sotto questa velocità di avvicinamento (m/s) la gauge NON compare: è un avviso da viaggio interplanetario, non per volo radente / saltelli / manovra fine vicino al suolo (lì usi i motori, non il freno)

    public void Init(Camera playerCamera, PlanetWalker w, SolarSystem s, MapMode m)
    {
        cam = playerCamera;
        walker = w;
        solar = s;
        map = m;

        var invSh = Shader.Find("Wanderer/InvertGUI");   // materiale per il mirino a inversione del colore di sfondo
        if (invSh != null) invertMat = new Material(invSh);

        // Anello a PARENTESI stile Outer Wilds: due archi sottili a SINISTRA e DESTRA, con ampi varchi sopra
        // e sotto. Banda con smoothstep a mano + alone tenue; le estremità degli archi sfumano nel varco.
        // 512px + MIPMAP + trilinear + supersampling 4× → linea nitida e pulita a ogni distanza (da lontano,
        // rimpicciolita, i mipmap evitano l'aliasing che la faceva "sgranata"; da vicino regge l'ingrandimento).
        ringTex = Make(512, 512, (u, v) =>
        {
            float dx = u - 0.5f, dy = v - 0.5f;
            float r = 2f * Mathf.Sqrt(dx * dx + dy * dy);   // 0 al centro, ~1 al bordo
            float d = Mathf.Abs(r - 0.80f);
            float band = 1f - Smooth(0.012f, 0.020f, d);      // arco nitido e sottile
            float halo = (1f - Smooth(0f, 0.13f, d)) * 0.07f; // alone appena percettibile
            float a = Mathf.Max(band, halo);
            float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;  // 0 = destra, ±180 = sinistra, ±90 = alto/basso
            float gap = Mathf.Abs(Mathf.Abs(ang) - 90f);      // distanza dalla verticale (90 sui lati, 0 su/giù)
            return a * Smooth(40f, 54f, gap);                 // archi ~88° sui lati, varchi ~92° su e giù
        }, mip: true, ss: 4);

        // Marker in alto a "casetta" (pentagono che punta su), stile Outer Wilds: sta sopra il varco superiore.
        chevronTex = Make(64, 64, (u, v) =>
        {
            Vector2 P = new Vector2(u, v);
            Vector2 apex = new Vector2(0.5f, 0.95f), sl = new Vector2(0.15f, 0.5f), sr = new Vector2(0.85f, 0.5f);
            Vector2 bl = new Vector2(0.28f, 0.1f), br = new Vector2(0.72f, 0.1f);
            bool roof = InTri(P, apex, sl, sr);
            bool body = InTri(P, sl, bl, br) || InTri(P, sl, br, sr); // quad sl-bl-br-sr
            return (roof || body) ? 1f : 0f;
        }, mip: true, ss: 4);

        discTex = Make(32, 32, (u, v) =>
        {
            float dx = u - 0.5f, dy = v - 0.5f;
            return 1f - Smooth(0.34f, 0.42f, 2f * Mathf.Sqrt(dx * dx + dy * dy));
        }, mip: true, ss: 4);

        // Marker del vettore velocità (⊕): cerchietto + punto centrale + quattro tacche radiali esterne.
        progradeTex = Make(96, 96, (u, v) =>
        {
            float dx = u - 0.5f, dy = v - 0.5f;
            float r = 2f * Mathf.Sqrt(dx * dx + dy * dy);
            float circle = 1f - Smooth(0.045f, 0.065f, Mathf.Abs(r - 0.52f));
            float dot = 1f - Smooth(0.10f, 0.14f, r);
            float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
            float axis = Mathf.Min(Mathf.Abs(ang), Mathf.Min(Mathf.Abs(Mathf.Abs(ang) - 90f), Mathf.Abs(Mathf.Abs(ang) - 180f)));
            float tick = (1f - Smooth(5f, 9f, axis)) * Smooth(0.58f, 0.62f, r) * (1f - Smooth(0.92f, 0.97f, r));
            return Mathf.Max(circle, Mathf.Max(dot, tick));
        }, mip: true, ss: 4);

        // Marker RETROGRADE: solo un cerchietto vuoto (l'opposto del prograde — "spingi di là per annullare").
        retroTex = Make(64, 64, (u, v) =>
        {
            float dx = u - 0.5f, dy = v - 0.5f;
            float r = 2f * Mathf.Sqrt(dx * dx + dy * dy);
            return 1f - Smooth(0.05f, 0.07f, Mathf.Abs(r - 0.5f));
        }, mip: true, ss: 4);

        // rettangolo pieno: barra/fondino della gauge di frenata (tinta via GUI.color in resa).
        barTex = Make(8, 8, (u, v) => 1f);

        // SONDA: marker a TRIANGOLO pieno che punta in su (distinto dall'anello del corpo selezionato).
        triTex = Make(64, 64, (u, v) =>
        {
            Vector2 P = new Vector2(u, v);
            return InTri(P, new Vector2(0.5f, 0.92f), new Vector2(0.1f, 0.12f), new Vector2(0.9f, 0.12f)) ? 1f : 0f;
        }, mip: true, ss: 4);

        // MIRINO al centro schermo: puntino + 4 tacche con un GAP centrale (reticolo di puntamento, non invasivo).
        crossTex = Make(64, 64, (u, v) =>
        {
            float dx = u - 0.5f, dy = v - 0.5f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float dot = r < 0.05f ? 1f : 0f;                                       // puntino centrale
            float tick = (r > 0.13f && r < 0.34f &&                                 // 4 tacche oltre il gap
                          (Mathf.Abs(dx) < 0.018f || Mathf.Abs(dy) < 0.018f)) ? 1f : 0f;
            return Mathf.Max(dot, tick);
        }, mip: true, ss: 4);
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (cam == null || solar == null) return;

        // MIRINO centrale: sempre durante il gioco (tranne in mappa, dove il cursore è libero). Indipendente dal
        // target → disegnato PRIMA del return su Destination nullo.
        if (cam.enabled && !(map != null && map.Active))
        {
            float uic = Mathf.Max(1f, Screen.height / 1080f);
            float cs = 26f * uic;
            var cr = new Rect(Screen.width * 0.5f - cs * 0.5f, Screen.height * 0.5f - cs * 0.5f, cs, cs);
            // INVERSIONE del colore di sfondo → mirino sempre visibile (chiaro e scuro). Fallback: bianco semitrasparente.
            if (invertMat != null) Graphics.DrawTexture(cr, crossTex, invertMat);
            else DrawTex(crossTex, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), cs, cs, 0f, A(White, 0.5f));
        }

        // TRACKER della SONDA (triangolo ambra): indipendente dal corpo selezionato → prima del return su Destination nullo.
        DrawProbeTracker(Mathf.Max(1f, Screen.height / 1080f));

        var target = solar.Destination;
        if (target == null) return;

        // Camera ATTIVA: in mappa il reticolo segue la camera della mappa → vedi quale corpo è selezionato.
        // Altrimenti la camera del giocatore (se spenta e non in mappa, niente da disegnare).
        bool mapActive = map != null && map.Active;
        view = mapActive ? map.ViewCamera : cam;
        if (view == null || !view.enabled) return;

        if (label == null)
            label = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        float ui = Mathf.Max(1f, Screen.height / 1080f);   // scala l'HUD con la risoluzione (Retina/4K)
        label.fontSize = Mathf.RoundToInt(14f * ui);

        Vector3 camPos = view.transform.position;
        Vector3 tp = target.transform.position;
        Vector2 gui = ToGui(tp, out bool behind, out bool onScreen);

        float dist = Vector3.Distance(camPos, tp);
        Vector3 relVel = RelativeVelocity(target);
        float relSpeed = relVel.magnitude;
        bool airborne = walker != null && walker.HasJetpack && walker.Altitude > AirborneAlt;
        bool synced = airborne && relSpeed < SyncSpeed;

        // GAUGE DI FRENATA (volo libero MANUALE): "ce la faccio a fermarmi prima del corpo?". Calcolata
        // ONESTAMENTE dai valori reali in gioco (così non va più ritoccata), distanza necessaria per fermarsi =
        //   d_react (continui ad avvicinarti mentre reagisci + i motori del freno spoolano) + d_brake (frenata vera).
        //  - d_react = closing · (brakeRampTime + ReactionTime): lo spool del freno X è reale, il tempo di
        //    reazione umano è un margine fisso → la barra arriva PRIMA, non all'ultimo istante.
        //  - d_brake = closing²/(2·aEff), con aEff = freno − g_superficie (la gravità erode la frenata reale
        //    tuffandoti su un corpo pesante, esattamente come per l'autopilota): conservativo, mai ottimista.
        // u = d_required / distanza-dalla-superficie. u=1 → ULTIMO momento per frenare; >1 → non ce la fai più.
        // Disegnata SEMPRE (anche quando il corpo riempie lo schermo e il reticolo svanisce): lì serve di più.
        // Sotto autopilota è nascosta (frena lui).
        if (!mapActive && airborne && walker != null && walker.IsNewtonian && !walker.Autopilot)
        {
            Vector3 toTb = (tp - camPos).normalized;
            float closingB = Vector3.Dot(relVel, toTb);          // + = ti avvicini
            float stopDist = dist - (float)target.Radius;        // distanza dalla SUPERFICIE del bersaglio
            if (closingB > WarnMinClosing && stopDist > 0f)
            {
                float gSurf = (float)target.SurfaceGravity;
                float aEff = Mathf.Max(walker.brakeAccel - gSurf, walker.brakeAccel * 0.3f);
                float tReact = walker.brakeRampTime + ReactionTime;
                float dReq = closingB * tReact + closingB * closingB / (2f * aEff);
                float u = dReq / stopDist;   // 1 = ULTIMO momento per frenare; >1 = troppo tardi
                if (u > 0.4f) DrawBrakeGauge(u, Mathf.Max(1f, Screen.height / 1080f));
            }
        }

        // raggio VERO a schermo (per la dissolvenza ravvicinata) e raggio CLAMPATO (per il disegno leggibile).
        float focal = Screen.height / (2f * Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad));
        float trueRad = focal * (float)target.Radius / Mathf.Max(dist, 1f);
        // quando il corpo riempie lo schermo svanisce: sei arrivato, non intralcia.
        float fade = 1f - Smooth(0.85f, 1.3f, trueRad / (Screen.height * 0.5f));
        if (fade <= 0.001f) return;

        Color baseCol = synced ? Green : Blue;

        if (onScreen)
        {
            // l'anello cresce col disco da vicino (niente tetto: la dissolvenza ravvicinata lo spegne quando
            // il corpo riempie lo schermo). La banda della texture sta a ~0.40 del raggio → moltiplicatore 3.4
            // mette l'anello a ~1.35× il raggio del disco: poco FUORI dai confini, non sopra.
            float rad = Mathf.Max(trueRad, 30f * ui);   // minimo per restare leggibile da lontano
            float ring = rad * 3.4f;

            DrawTex(ringTex, gui, ring, ring, 0f, baseCol, fade);
            DrawTex(discTex, gui, 6f * ui, 6f * ui, 0f, A(Cyan, fade));            // pip centrale ciano
            DrawTex(chevronTex, gui + new Vector2(0f, -ring * 0.5f - 14f * ui), 16f * ui, 16f * ui, 0f, baseCol, fade);  // casetta sopra il varco

            // VETTORE VELOCITÀ — due marker: PROGRADE (pieno ⊕, dove derivi di lato) e RETROGRADE (cerchietto
            // vuoto, l'opposto: spingi di là per annullare la deriva). L'offset è proporzionale alla velocità
            // LATERALE (componente perpendicolare alla rotta verso il bersaglio), NON alla direzione pura: così
            // vicino allo zero il marker resta al centro e non "sbanda" — la direzione di un vettore minuscolo è
            // instabile. Tratteggio su entrambi. Deriva laterale ~0 mentre ti avvicini = allineato (verde).
            if (!mapActive && airborne && relSpeed > SyncSpeed)
            {
                float ringEdge = ring * 0.5f;
                float maxLeash = Mathf.Clamp(ring * 1.3f, 130f * ui, 320f * ui);
                // SATURAZIONE MORBIDA (per asse): sensibile vicino allo zero ma arriva al bordo (maxLeash) solo a
                // deriva ALTA → il marker non schizza al minimo drift, ci arriva GRADUALMENTE. driftRef = m/s "di
                // scala": più alto, più lentamente raggiunge il massimo. (Era lineare a 6 px/(m/s): saturava a ~33 m/s.)
                const float driftRef = 240f;
                Vector3 toT = (tp - camPos).normalized;
                float closing = Vector3.Dot(relVel, toT);          // + = ti avvicini
                Vector3 latVel = relVel - toT * closing;           // deriva laterale (perpendicolare alla rotta)
                float lateral = latVel.magnitude;
                float dx = Vector3.Dot(latVel, view.transform.right) / driftRef;
                float dy = -Vector3.Dot(latVel, view.transform.up) / driftRef;
                // saturazione morbida + EASE-IN (pow > 1): il PRIMO tratto (dal centro, deriva piccola) è più lento,
                // il resto della corsa verso il bordo resta com'è (1^k = 1).
                float Soft(float t)
                {
                    float m = Mathf.Abs(t);
                    return Mathf.Sign(t) * Mathf.Pow(m / Mathf.Sqrt(1f + m * m), 1.7f);
                }
                Vector2 off = new Vector2(Soft(dx), Soft(dy)) * maxLeash;

                bool aligned = lateral < 2f && closing > 0.5f;     // poca deriva E ti avvicini
                Color pc = aligned ? Green : Blue;
                Vector2 mpos = gui + off, rpos = gui - off;

                float lead = off.magnitude;
                if (lead > ringEdge + 12f * ui)
                {
                    Vector2 d = off / lead;
                    DrawDots(gui + d * (ringEdge + 4f * ui), mpos - d * 12f * ui, pc, fade, ui);             // verso prograde
                    DrawDots(gui - d * (ringEdge + 4f * ui), rpos + d * 12f * ui, A(Blue, 0.5f), fade, ui);  // verso retrograde
                }
                DrawTex(retroTex, rpos, 18f * ui, 18f * ui, 0f, A(Blue, 0.55f), fade);    // retrograde: cerchietto vuoto
                DrawTex(progradeTex, mpos, 24f * ui, 24f * ui, 0f, pc, fade);            // prograde: ⊕ pieno
                if (aligned && !synced)
                    Shadowed(new Rect(gui.x - 70f * ui, gui.y + ringEdge + 6f * ui, 140f * ui, 20f * ui), "ALLINEATO", Green, fade, TextAnchor.UpperCenter);
            }

            // testo accanto al corpo: appena FUORI dall'anello, clampato al bordo schermo SOLO quando serve
            // (da vicino l'anello è enorme e altrimenti i numeri uscivano dalla visuale). Così finché c'è spazio
            // fuori dal reticolo i numeri restano lì, e si avvicinano al corpo solo all'ultimo, non troppo presto.
            // In mappa mostra il NOME (la distanza dalla camera-mappa non significa nulla).
            float tx = Mathf.Min(gui.x + ring * 0.5f + 12f * ui, Screen.width - 150f * ui), ty = gui.y - 16f * ui;
            if (mapActive)
            {
                Shadowed(new Rect(tx, ty, 220f * ui, 22f * ui), target.gameObject.name, White, fade, TextAnchor.UpperLeft);
            }
            else
            {
                Shadowed(new Rect(tx, ty, 170f * ui, 22f * ui), FmtDist(dist), White, fade, TextAnchor.UpperLeft);
                if (synced)
                    Shadowed(new Rect(tx, ty + 18f * ui, 170f * ui, 22f * ui), "SINCRONIZZATO", Green, fade, TextAnchor.UpperLeft);
                else if (airborne)
                {
                    // velocità di avvicinamento col segno: − = ti ALLONTANI, + = ti avvicini.
                    Vector3 toT = (tp - camPos).normalized;
                    float closing = Vector3.Dot(relVel, toT);
                    Shadowed(new Rect(tx, ty + 18f * ui, 170f * ui, 22f * ui), closing.ToString("+0;-0;0") + " m/s", White, fade, TextAnchor.UpperLeft);
                }
            }
        }
        else
        {
            // fuori vista: freccia al bordo schermo verso il corpo + distanza.
            Vector2 ctr = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 dir = gui - ctr;
            if (dir.sqrMagnitude < 1e-4f) dir = new Vector2(0f, -1f);
            dir.Normalize();
            float m = 64f * ui;
            Vector2 edge = ClampToRect(ctr, dir, new Rect(m, m, Screen.width - 2f * m, Screen.height - 2f * m));
            float ang = Mathf.Atan2(dir.x, -dir.y) * Mathf.Rad2Deg;  // casetta apice in alto → ruota verso dir
            DrawTex(chevronTex, edge, 26f * ui, 26f * ui, ang, baseCol, fade);
            // in mappa la distanza dalla camera non significa nulla: solo la freccia, niente numero.
            Shadowed(new Rect(edge.x - 60f * ui, edge.y + 18f * ui, 120f * ui, 20f * ui),
                mapActive ? target.gameObject.name : FmtDist(dist), White, fade, TextAnchor.UpperCenter);
        }
    }

    // Tracker della SONDA: stessa logica del reticolo (proiezione robusta ToGui, freccia al bordo se fuori vista,
    // distanza), ma TRIANGOLO AMBRA → si distingue a colpo d'occhio dall'anello blu/verde del corpo selezionato.
    void DrawProbeTracker(float ui)
    {
        if (ProbeTarget == null || !ProbeTarget.gameObject.activeSelf) return;
        if (map != null && map.Active) return;          // in mappa no
        if (cam == null || !cam.enabled) return;        // es. stai guardando ATTRAVERSO la sonda → niente tracker
        view = cam;
        if (label == null) label = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        label.fontSize = Mathf.RoundToInt(14f * ui);

        Vector3 wp = ProbeTarget.transform.position;
        Vector2 g = ToGui(wp, out _, out bool onScreen);
        float dist = Vector3.Distance(cam.transform.position, wp);
        string txt = (ProbeTarget.Landed ? "SONDA · posata · " : "SONDA · ") + FmtDist(dist);
        Color pcol = ProbeTarget.Landed ? Green : Amber;   // VERDE da posata, ambra in volo

        if (onScreen)
        {
            // marker SOPRA la sonda (non sovrapposto): triangolo apice in GIÙ che la indica, etichetta sopra di esso.
            Vector2 mk = new Vector2(g.x, g.y - 40f * ui);
            DrawTex(triTex, mk, 20f * ui, 20f * ui, 180f, pcol);   // 180° → apice verso il basso, punta la sonda
            Shadowed(new Rect(mk.x - 90f * ui, mk.y - 22f * ui, 180f * ui, 20f * ui), txt, pcol, 1f, TextAnchor.LowerCenter);
        }
        else
        {
            Vector2 ctr = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 dir = g - ctr;
            if (dir.sqrMagnitude < 1e-4f) dir = new Vector2(0f, -1f);
            dir.Normalize();
            float m = 64f * ui;
            Vector2 edge = ClampToRect(ctr, dir, new Rect(m, m, Screen.width - 2f * m, Screen.height - 2f * m));
            float ang = Mathf.Atan2(dir.x, -dir.y) * Mathf.Rad2Deg;   // triangolo: apice in su → ruota verso dir
            DrawTex(triTex, edge, 24f * ui, 24f * ui, ang, pcol);
            Shadowed(new Rect(edge.x - 90f * ui, edge.y + 16f * ui, 180f * ui, 20f * ui), txt, pcol, 1f, TextAnchor.UpperCenter);
        }
    }

    // ---- geometria a schermo ----------------------------------------------------------------------

    // Proietta un punto-mondo in coordinate GUI (y giù), riportando da pixel-camera a pixel-schermo (RenderScaler).
    Vector2 ToGui(Vector3 world, out bool behind, out bool onScreen)
    {
        Vector3 sp = view.WorldToScreenPoint(world);
        if (view.pixelWidth > 0) sp.x *= (float)Screen.width / view.pixelWidth;
        if (view.pixelHeight > 0) sp.y *= (float)Screen.height / view.pixelHeight;
        behind = sp.z <= 0f;
        Vector2 ctr = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 scr = new Vector2(sp.x, sp.y);
        if (behind) scr = ctr - (scr - ctr);                        // dietro: rifletti, così la freccia punta giusto
        onScreen = !behind && scr.x >= 0f && scr.x <= Screen.width && scr.y >= 0f && scr.y <= Screen.height;
        return new Vector2(scr.x, Screen.height - scr.y);
    }

    // Velocità del giocatore RELATIVA al bersaglio (scena, per secondo reale). rb.linearVelocity è relativa
    // al corpo ANCORATO; al bersaglio si toglie la velocità-scena del bersaglio = (target − ancora) in
    // velocità-universo × TimeScale. Se il bersaglio È l'ancora (in viaggio) resta la velocità del giocatore.
    Vector3 RelativeVelocity(CelestialBody target)
    {
        Vector3 pv = walker != null ? walker.Velocity : Vector3.zero;
        var refb = solar.Reference;
        if (refb == null) return pv;
        Vector3 tvs = (target.UniverseVelocity - refb.UniverseVelocity).ToVector3() * (float)solar.TimeScale;   // cache per-Step
        return pv - tvs;
    }

    static string FmtDist(float d) => d > 1000f ? (d / 1000f).ToString("F1") + " km" : d.ToString("F0") + " m";

    // interseca il raggio (c + dir·t) col bordo interno del rettangolo: punto sul bordo nella direzione data.
    static Vector2 ClampToRect(Vector2 c, Vector2 dir, Rect r)
    {
        float tx = dir.x > 0f ? (r.xMax - c.x) / dir.x : dir.x < 0f ? (r.xMin - c.x) / dir.x : float.MaxValue;
        float ty = dir.y > 0f ? (r.yMax - c.y) / dir.y : dir.y < 0f ? (r.yMin - c.y) / dir.y : float.MaxValue;
        return c + dir * Mathf.Min(tx, ty);
    }

    // ---- disegno ----------------------------------------------------------------------------------

    static Color A(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

    // linea tratteggiata (puntini) tra due punti: collega il marker prograde al reticolo.
    void DrawDots(Vector2 a, Vector2 b, Color col, float fade, float ui)
    {
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < 1f) return;
        Vector2 step = d / len;
        float gap = 9f * ui;
        int n = Mathf.FloorToInt(len / gap);
        for (int i = 0; i <= n; i++)
            DrawTex(discTex, a + step * (i * gap), 3.5f * ui, 3.5f * ui, 0f, col, fade);
    }

    // Gauge di frenata in basso al centro: una barra che si riempie verso una tacca "ORA" (u=1). Sotto 0.85
    // è informativa (ciano "PUNTO DI FRENATA"), poi ambra "FRENA", e quando supera 1 diventa rossa "TROPPO
    // VELOCE": col freno X non ti fermi più prima della superficie. Resta finché la condizione persiste.
    void DrawBrakeGauge(float u, float ui)
    {
        float w = 240f * ui, h = 9f * ui;
        float x = (Screen.width - w) * 0.5f;
        float y = Screen.height - 96f * ui;
        Color c = u >= 1f ? Red : (u >= 0.85f ? Amber : Cyan);

        Color pc = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.45f);                                   // fondino
        GUI.DrawTexture(new Rect(x - 2f * ui, y - 2f * ui, w + 4f * ui, h + 4f * ui), barTex);
        GUI.color = c;                                                              // riempimento (clampato a fine barra)
        GUI.DrawTexture(new Rect(x, y, w * Mathf.Clamp01(u), h), barTex);
        GUI.color = White;                                                          // tacca "ORA" alla fine (u=1)
        GUI.DrawTexture(new Rect(x + w - 1.5f * ui, y - 3f * ui, 3f * ui, h + 6f * ui), barTex);
        GUI.color = pc;

        string t = u >= 1f ? "TROPPO VELOCE — non freni in tempo" : (u >= 0.85f ? "FRENA" : "punto di frenata");
        Shadowed(new Rect(x, y - 22f * ui, w, 20f * ui), t, c, 1f, TextAnchor.UpperCenter);
    }

    void DrawTex(Texture2D tex, Vector2 c, float w, float h, float ang, Color col, float fade = 1f)
    {
        if (tex == null) return;
        Matrix4x4 m = GUI.matrix;
        Color pc = GUI.color;
        if (Mathf.Abs(ang) > 0.01f) GUIUtility.RotateAroundPivot(ang, c);
        GUI.color = A(col, fade);
        GUI.DrawTexture(new Rect(c.x - w * 0.5f, c.y - h * 0.5f, w, h), tex);
        GUI.color = pc;
        GUI.matrix = m;
    }

    // testo con OMBRA (nero sfalsato 1px), leggibile anche sui corpi chiari — niente fondino scuro.
    void Shadowed(Rect r, string t, Color c, float fade, TextAnchor anchor)
    {
        label.alignment = anchor;
        label.normal.textColor = new Color(0f, 0f, 0f, 0.8f * fade);
        GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), t, label);
        label.normal.textColor = A(c, fade);
        GUI.Label(r, t, label);
    }

    // ---- generazione texture ----------------------------------------------------------------------

    // smoothstep di GLSL scritta a mano (Mathf.SmoothStep di Unity interpola l'output, non soglia l'input).
    static float Smooth(float e0, float e1, float x)
    {
        float t = Mathf.Clamp01((x - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }

    static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Edge(p, a, b), d2 = Edge(p, b, c), d3 = Edge(p, c, a);
        bool neg = d1 < 0f || d2 < 0f || d3 < 0f, pos = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(neg && pos);
    }

    static float Edge(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);

    // genera una texture bianca con alfa = campo float [0,1] (la forma porta già la sua AA via smoothstep);
    // supersampling ss×ss per rifinire i bordi sottili. mip=true genera i mipmap + filtro trilineare → la forma
    // resta NITIDA anche molto rimpicciolita a schermo (niente aliasing/granulosità da lontano, come l'anello).
    // La tinta è applicata in resa via GUI.color.
    delegate float FieldFn(float u, float v);
    static Texture2D Make(int w, int h, FieldFn f, bool mip = false, int ss = 2)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, mip)
        { wrapMode = TextureWrapMode.Clamp, filterMode = mip ? FilterMode.Trilinear : FilterMode.Bilinear };
        var px = new Color32[w * h];
        float inv = 1f / (ss * ss);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float a = 0f;
                for (int sy = 0; sy < ss; sy++)
                    for (int sx = 0; sx < ss; sx++)
                    {
                        float u = (x + (sx + 0.5f) / ss) / w;
                        float v = (y + (sy + 0.5f) / ss) / h;
                        a += Mathf.Clamp01(f(u, v));
                    }
                px[y * w + x] = new Color32(255, 255, 255, (byte)(a * inv * 255f));
            }
        t.SetPixels32(px);
        t.Apply(mip);   // genera i mipmap se richiesto
        return t;
    }
}

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

    Texture2D ringTex, chevronTex, discTex, progradeTex, retroTex;
    GUIStyle label;

    // Tavolozza: blu di riposo, verde quando sincronizzato o in intercetto.
    static readonly Color Blue = new Color(0.55f, 0.72f, 1f, 1f);
    static readonly Color White = new Color(0.9f, 0.96f, 1f, 1f);
    static readonly Color Green = new Color(0.42f, 1f, 0.55f, 1f);
    static readonly Color Cyan = new Color(0.5f, 0.95f, 1f, 1f);

    const float SyncSpeed = 1.0f;        // |velocità relativa| sotto cui il reticolo è "sincronizzato"
    const float AirborneAlt = 3f;        // sopra questa quota mostri la velocità (a terra è l'orbita del pianeta)

    public void Init(Camera playerCamera, PlanetWalker w, SolarSystem s)
    {
        cam = playerCamera;
        walker = w;
        solar = s;

        // Anello a PARENTESI stile Outer Wilds: due archi sottili a SINISTRA e DESTRA, con ampi varchi sopra
        // e sotto. Banda con smoothstep a mano + alone tenue; le estremità degli archi sfumano nel varco.
        ringTex = Make(256, 256, (u, v) =>
        {
            float dx = u - 0.5f, dy = v - 0.5f;
            float r = 2f * Mathf.Sqrt(dx * dx + dy * dy);   // 0 al centro, ~1 al bordo
            float d = Mathf.Abs(r - 0.80f);
            float band = 1f - Smooth(0.014f, 0.026f, d);      // arco nitido e sottile
            float halo = (1f - Smooth(0f, 0.14f, d)) * 0.08f; // alone appena percettibile
            float a = Mathf.Max(band, halo);
            float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;  // 0 = destra, ±180 = sinistra, ±90 = alto/basso
            float gap = Mathf.Abs(Mathf.Abs(ang) - 90f);      // distanza dalla verticale (90 sui lati, 0 su/giù)
            return a * Smooth(40f, 54f, gap);                 // archi ~88° sui lati, varchi ~92° su e giù
        });

        // Marker in alto a "casetta" (pentagono che punta su), stile Outer Wilds: sta sopra il varco superiore.
        chevronTex = Make(64, 64, (u, v) =>
        {
            Vector2 P = new Vector2(u, v);
            Vector2 apex = new Vector2(0.5f, 0.95f), sl = new Vector2(0.15f, 0.5f), sr = new Vector2(0.85f, 0.5f);
            Vector2 bl = new Vector2(0.28f, 0.1f), br = new Vector2(0.72f, 0.1f);
            bool roof = InTri(P, apex, sl, sr);
            bool body = InTri(P, sl, bl, br) || InTri(P, sl, br, sr); // quad sl-bl-br-sr
            return (roof || body) ? 1f : 0f;
        });

        discTex = Make(32, 32, (u, v) =>
        {
            float dx = u - 0.5f, dy = v - 0.5f;
            return 1f - Smooth(0.34f, 0.42f, 2f * Mathf.Sqrt(dx * dx + dy * dy));
        });

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
        });

        // Marker RETROGRADE: solo un cerchietto vuoto (l'opposto del prograde — "spingi di là per annullare").
        retroTex = Make(64, 64, (u, v) =>
        {
            float dx = u - 0.5f, dy = v - 0.5f;
            float r = 2f * Mathf.Sqrt(dx * dx + dy * dy);
            return 1f - Smooth(0.05f, 0.07f, Mathf.Abs(r - 0.5f));
        });
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (cam == null || solar == null || !cam.enabled) return;   // cam spenta = modalità mappa: niente reticolo
        var target = solar.Destination;
        if (target == null) return;
        if (label == null)
            label = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };

        Vector3 camPos = cam.transform.position;
        Vector3 tp = target.transform.position;
        Vector2 gui = ToGui(tp, out bool behind, out bool onScreen);

        float dist = Vector3.Distance(camPos, tp);
        Vector3 relVel = RelativeVelocity(target);
        float relSpeed = relVel.magnitude;
        bool airborne = walker != null && walker.HasJetpack && walker.Altitude > AirborneAlt;
        bool synced = airborne && relSpeed < SyncSpeed;

        // raggio VERO a schermo (per la dissolvenza ravvicinata) e raggio CLAMPATO (per il disegno leggibile).
        float focal = Screen.height / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
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
            float rad = Mathf.Max(trueRad, 30f);   // minimo per restare leggibile da lontano
            float ring = rad * 3.4f;

            DrawTex(ringTex, gui, ring, ring, 0f, baseCol, fade);
            DrawTex(discTex, gui, 6f, 6f, 0f, A(Cyan, fade));            // pip centrale ciano
            DrawTex(chevronTex, gui + new Vector2(0f, -ring * 0.5f - 14f), 16f, 16f, 0f, baseCol, fade);  // casetta sopra il varco

            // VETTORE VELOCITÀ — due marker: PROGRADE (pieno ⊕, dove derivi di lato) e RETROGRADE (cerchietto
            // vuoto, l'opposto: spingi di là per annullare la deriva). L'offset è proporzionale alla velocità
            // LATERALE (componente perpendicolare alla rotta verso il bersaglio), NON alla direzione pura: così
            // vicino allo zero il marker resta al centro e non "sbanda" — la direzione di un vettore minuscolo è
            // instabile. Tratteggio su entrambi. Deriva laterale ~0 mentre ti avvicini = allineato (verde).
            if (airborne && relSpeed > SyncSpeed)
            {
                const float pxPerMS = 6f;       // pixel di offset per m/s di deriva laterale
                float ringEdge = ring * 0.5f;
                float maxLeash = Mathf.Clamp(ring * 1.3f, 130f, 320f);

                Vector3 toT = (tp - camPos).normalized;
                float closing = Vector3.Dot(relVel, toT);          // + = ti avvicini
                Vector3 latVel = relVel - toT * closing;           // deriva laterale (perpendicolare alla rotta)
                float lateral = latVel.magnitude;
                Vector2 off = new Vector2(Vector3.Dot(latVel, cam.transform.right),
                                        -Vector3.Dot(latVel, cam.transform.up)) * pxPerMS;
                off.x = Mathf.Clamp(off.x, -maxLeash, maxLeash);   // clamp PER ASSE: deriva H e V leggibili separate
                off.y = Mathf.Clamp(off.y, -maxLeash, maxLeash);

                bool aligned = lateral < 2f && closing > 0.5f;     // poca deriva E ti avvicini
                Color pc = aligned ? Green : Blue;
                Vector2 mpos = gui + off, rpos = gui - off;

                float lead = off.magnitude;
                if (lead > ringEdge + 12f)
                {
                    Vector2 d = off / lead;
                    DrawDots(gui + d * (ringEdge + 4f), mpos - d * 12f, pc, fade);             // verso prograde
                    DrawDots(gui - d * (ringEdge + 4f), rpos + d * 12f, A(Blue, 0.5f), fade);  // verso retrograde
                }
                DrawTex(retroTex, rpos, 18f, 18f, 0f, A(Blue, 0.55f), fade);    // retrograde: cerchietto vuoto
                DrawTex(progradeTex, mpos, 24f, 24f, 0f, pc, fade);            // prograde: ⊕ pieno
                if (aligned && !synced)
                    Shadowed(new Rect(gui.x - 70f, gui.y + ringEdge + 6f, 140f, 20f), "ALLINEATO", Green, fade, TextAnchor.UpperCenter);
            }

            // testo a lato: distanza sempre; velocità (col SEGNO) solo in volo.
            float tx = gui.x + ring * 0.5f + 12f, ty = gui.y - 16f;
            Shadowed(new Rect(tx, ty, 170f, 22f), FmtDist(dist), White, fade, TextAnchor.UpperLeft);
            if (synced)
                Shadowed(new Rect(tx, ty + 18f, 170f, 22f), "SINCRONIZZATO", Green, fade, TextAnchor.UpperLeft);
            else if (airborne)
            {
                // velocità di avvicinamento col segno: − = ti ALLONTANI, + = ti avvicini.
                Vector3 toT = (tp - camPos).normalized;
                float closing = Vector3.Dot(relVel, toT);
                Shadowed(new Rect(tx, ty + 18f, 170f, 22f), closing.ToString("+0;-0;0") + " m/s", White, fade, TextAnchor.UpperLeft);
            }
        }
        else
        {
            // fuori vista: freccia al bordo schermo verso il corpo + distanza.
            Vector2 ctr = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 dir = gui - ctr;
            if (dir.sqrMagnitude < 1e-4f) dir = new Vector2(0f, -1f);
            dir.Normalize();
            Vector2 edge = ClampToRect(ctr, dir, new Rect(64f, 64f, Screen.width - 128f, Screen.height - 128f));
            float ang = Mathf.Atan2(dir.x, -dir.y) * Mathf.Rad2Deg;  // casetta apice in alto → ruota verso dir
            DrawTex(chevronTex, edge, 26f, 26f, ang, baseCol, fade);
            Shadowed(new Rect(edge.x - 60f, edge.y + 18f, 120f, 20f), FmtDist(dist), White, fade, TextAnchor.UpperCenter);
        }
    }

    // ---- geometria a schermo ----------------------------------------------------------------------

    // Proietta un punto-mondo in coordinate GUI (y giù), riportando da pixel-camera a pixel-schermo (RenderScaler).
    Vector2 ToGui(Vector3 world, out bool behind, out bool onScreen)
    {
        Vector3 sp = cam.WorldToScreenPoint(world);
        if (cam.pixelWidth > 0) sp.x *= (float)Screen.width / cam.pixelWidth;
        if (cam.pixelHeight > 0) sp.y *= (float)Screen.height / cam.pixelHeight;
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
        Vector3 tvs = (target.UniverseVelocityAt(solar.SimTime) - refb.UniverseVelocityAt(solar.SimTime)).ToVector3() * (float)solar.TimeScale;
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
    void DrawDots(Vector2 a, Vector2 b, Color col, float fade)
    {
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < 1f) return;
        Vector2 step = d / len;
        int n = Mathf.FloorToInt(len / 9f);
        for (int i = 0; i <= n; i++)
            DrawTex(discTex, a + step * (i * 9f), 3.5f, 3.5f, 0f, col, fade);
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
    // supersampling 2×2 per rifinire i bordi sottili. La tinta è applicata in resa via GUI.color.
    delegate float FieldFn(float u, float v);
    static Texture2D Make(int w, int h, FieldFn f)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float a = 0f;
                for (int sy = 0; sy < 2; sy++)
                    for (int sx = 0; sx < 2; sx++)
                    {
                        float u = (x + (sx + 0.5f) * 0.5f) / w;
                        float v = (y + (sy + 0.5f) * 0.5f) / h;
                        a += Mathf.Clamp01(f(u, v));
                    }
                px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 0.25f * 255f));
            }
        t.SetPixels32(px);
        t.Apply();
        return t;
    }
}

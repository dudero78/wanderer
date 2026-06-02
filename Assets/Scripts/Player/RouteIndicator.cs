using UnityEngine;

/// <summary>
/// Indicatore di rotta stile Outer Wilds sul corpo SELEZIONATO (solar.Destination). Quando è in vista
/// gli disegna intorno un anello a parentesi scalato alla sua dimensione a schermo, un chevron in cima,
/// un pip al centro, e a lato distanza + velocità relativa con una freccia "prograde" nella direzione in
/// cui derivi rispetto a lui. Quando è fuori vista, una freccia al bordo schermo punta verso di lui con
/// la distanza. Così il bersaglio non "sparisce" mai senza lasciare traccia: è la bussola del viaggio.
///
/// Tutto procedurale: le texture (anello, triangolo, disco) sono generate da codice all'avvio — niente
/// asset da importare. Disegnato in OnGUI (overlay a schermo), quindi sta sopra al RenderScaler.
/// NOTA: le forme usano test BOOLEANI supersamplati (Make), non Mathf.SmoothStep — che in Unity NON è
/// la smoothstep di GLSL (interpola l'output, non soglia l'input) e riempirebbe la texture.
/// </summary>
public class RouteIndicator : MonoBehaviour
{
    Camera cam;
    PlanetWalker walker;
    SolarSystem solar;

    Texture2D ringTex, triTex, discTex;
    GUIStyle label;

    readonly Color tint = new Color(0.55f, 0.7f, 1f, 1f);
    readonly Color markCol = new Color(0.85f, 0.95f, 1f, 1f);

    public void Init(Camera playerCamera, PlanetWalker w, SolarSystem s)
    {
        cam = playerCamera;
        walker = w;
        solar = s;

        // anello a PARENTESI: cerchio sottile con due varchi (in alto e in basso) → archi a sinistra e
        // destra, come l'aggancio di Outer Wilds. Il chevron va nel varco in alto.
        ringTex = Make(256, 256, (u, v) =>
        {
            float dx = u - 0.5f, dy = v - 0.5f;
            float r = 2f * Mathf.Sqrt(dx * dx + dy * dy);
            if (Mathf.Abs(r - 0.9f) >= 0.03f) return false;
            float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;   // top = +90, bottom = -90
            return Mathf.Abs(Mathf.Abs(ang) - 90f) > 22f;       // varco di ±22° in alto e in basso
        });

        // triangolo pieno con l'apice in ALTO.
        triTex = Make(64, 64, (u, v) =>
        {
            Vector2 P = new Vector2(u, v), A = new Vector2(0.5f, 0.92f), B = new Vector2(0.16f, 0.22f), C = new Vector2(0.84f, 0.22f);
            float d1 = Edge(P, A, B), d2 = Edge(P, B, C), d3 = Edge(P, C, A);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f, pos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(neg && pos);
        });

        discTex = Make(32, 32, (u, v) =>
        {
            float dx = u - 0.5f, dy = v - 0.5f;
            return 2f * Mathf.Sqrt(dx * dx + dy * dy) < 0.8f;
        });
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (cam == null || solar == null || !cam.enabled) return;   // cam spenta = modalità mappa: niente reticolo
        var target = solar.Destination;
        if (target == null) return;
        if (label == null)
            label = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = markCol } };

        Vector3 tp = target.transform.position;
        Vector3 sp = cam.WorldToScreenPoint(tp);
        // la cam rende su una RenderTexture (RenderScaler): riporta a pixel di SCHERMO (a 1.0 è identità).
        if (cam.pixelWidth > 0) sp.x *= (float)Screen.width / cam.pixelWidth;
        if (cam.pixelHeight > 0) sp.y *= (float)Screen.height / cam.pixelHeight;
        bool behind = sp.z <= 0f;
        Vector2 ctr = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 scr = new Vector2(sp.x, sp.y);
        if (behind) scr = ctr - (scr - ctr);                        // dietro: rifletti, così la freccia punta giusto
        Vector2 gui = new Vector2(scr.x, Screen.height - scr.y);     // schermo (y su) -> GUI (y giù)
        bool onScreen = !behind && scr.x >= 0f && scr.x <= Screen.width && scr.y >= 0f && scr.y <= Screen.height;

        float dist = Vector3.Distance(cam.transform.position, tp);
        Vector3 relVel = RelativeVelocity(target);
        float relSpeed = relVel.magnitude;

        if (onScreen)
        {
            // raggio a schermo del corpo dalla sua dimensione angolare; clamp per restare sempre leggibile.
            float focal = Screen.height / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            float rad = Mathf.Clamp(focal * (float)target.Radius / Mathf.Max(dist, 1f), 26f, 360f);
            float ring = rad * 2.2f;                                  // anello appena fuori dal corpo
            DrawTex(ringTex, gui, ring, ring, 0f, tint);
            DrawTex(discTex, gui, 7f, 7f, 0f, markCol);                                  // pip centrale
            DrawTex(triTex, gui + new Vector2(0f, -ring * 0.5f - 12f), 18f, 18f, 0f, markCol);   // chevron in cima

            // freccia PROGRADE: direzione della velocità relativa proiettata a schermo.
            if (relSpeed > 0.5f)
            {
                Vector2 d = ScreenDir(tp, relVel);
                if (d.sqrMagnitude > 1e-6f)
                {
                    d.Normalize();
                    float ang = Mathf.Atan2(d.x, -d.y) * Mathf.Rad2Deg;
                    DrawTex(triTex, gui + d * (ring * 0.5f + 22f), 15f, 15f, ang, tint);
                }
            }

            string txt = FmtDist(dist) + "\n" + relSpeed.ToString("F0") + " m/s";
            GUI.Label(new Rect(gui.x + ring * 0.5f + 12f, gui.y - 16f, 160f, 40f), txt, label);
        }
        else
        {
            // fuori vista: freccia al bordo schermo che punta verso il corpo + distanza.
            Vector2 dir = gui - ctr;
            if (dir.sqrMagnitude < 1e-4f) dir = new Vector2(0f, -1f);
            dir.Normalize();
            Vector2 edge = ClampToRect(ctr, dir, new Rect(64f, 64f, Screen.width - 128f, Screen.height - 128f));
            float ang = Mathf.Atan2(dir.x, -dir.y) * Mathf.Rad2Deg;
            DrawTex(triTex, edge, 26f, 26f, ang, tint);
            GUI.Label(new Rect(edge.x - 50f, edge.y + 18f, 100f, 24f), FmtDist(dist), label);
        }
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

    // Direzione a schermo (coord GUI, y giù) di un vettore-mondo applicato nel punto del bersaglio.
    Vector2 ScreenDir(Vector3 worldPos, Vector3 worldVec)
    {
        Vector3 a = cam.WorldToScreenPoint(worldPos);
        Vector3 b = cam.WorldToScreenPoint(worldPos + worldVec.normalized * Mathf.Max(1f, worldVec.magnitude));
        return new Vector2(b.x - a.x, a.y - b.y);   // y invertita per la GUI
    }

    static string FmtDist(float d) => d > 1000f ? (d / 1000f).ToString("F1") + " km" : d.ToString("F0") + " m";

    // interseca il raggio (c + dir·t) col bordo interno del rettangolo: punto sul bordo nella direzione data.
    static Vector2 ClampToRect(Vector2 c, Vector2 dir, Rect r)
    {
        float tx = dir.x > 0f ? (r.xMax - c.x) / dir.x : dir.x < 0f ? (r.xMin - c.x) / dir.x : float.MaxValue;
        float ty = dir.y > 0f ? (r.yMax - c.y) / dir.y : dir.y < 0f ? (r.yMin - c.y) / dir.y : float.MaxValue;
        return c + dir * Mathf.Min(tx, ty);
    }

    void DrawTex(Texture2D tex, Vector2 c, float w, float h, float ang, Color col)
    {
        if (tex == null) return;
        Matrix4x4 m = GUI.matrix;
        Color pc = GUI.color;
        if (Mathf.Abs(ang) > 0.01f) GUIUtility.RotateAroundPivot(ang, c);
        GUI.color = col;
        GUI.DrawTexture(new Rect(c.x - w * 0.5f, c.y - h * 0.5f, w, h), tex);
        GUI.color = pc;
        GUI.matrix = m;
    }

    // lato sinistro/destro del segmento AB per il punto P (test di appartenenza al triangolo).
    static float Edge(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);

    // genera una texture bianca con alfa = forma BOOLEANA, supersampling 3×3 per i bordi morbidi (la tinta in resa).
    delegate bool ShapeFn(float u, float v);
    static Texture2D Make(int w, int h, ShapeFn f)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int hit = 0;
                for (int sy = 0; sy < 3; sy++)
                    for (int sx = 0; sx < 3; sx++)
                    {
                        float u = (x + (sx + 0.5f) / 3f) / w;
                        float v = (y + (sy + 0.5f) / 3f) / h;
                        if (f(u, v)) hit++;
                    }
                px[y * w + x] = new Color32(255, 255, 255, (byte)(hit * 255 / 9));
            }
        t.SetPixels32(px);
        t.Apply();
        return t;
    }
}

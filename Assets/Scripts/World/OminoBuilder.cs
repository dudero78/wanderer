using UnityEngine;

/// <summary>
/// Costruttore dell'OMINO stilizzato condiviso da TUTA e GIOCATORE (e, dopo la raccolta, dal giocatore "in tuta").
/// Pezzi: TORSO (mesh a rivoluzione: fondo arrotondato, spalle, top troncato-arrotondato) + 2 GAMBE + 2 BRACCIA (a V,
/// agganciate alle spalle) + 2 BOMBOLE centrali che si toccano + UGELLI luminosi + TESTA (sfera luminosa O casco).
/// Tutto da primitivi/mesh procedurali → niente asset. Parametrizzato da <see cref="Style"/>: colore/materiale del
/// corpo, tipo di testa, colore d'accento (glow/visiera), magrezza (il giocatore è un filo più magro della tuta).
/// </summary>
public static class OminoBuilder
{
    public enum HeadKind { GlowSphere, Helmet }

    public struct Style
    {
        public Color bodyColor;   // colore dei pezzi del corpo
        public bool metallic;     // true = acciaio metallico (tuta) · false = materiale "liscio" del giocatore
        public HeadKind head;     // sfera luminosa (testa nuda) o casco
        public Color accent;      // colore glow: testa-sfera, visiera del casco, ugelli
        public float thin;        // raggio del CORPO (torso): 1 = pieno · <1 = più magro (giocatore nudo)
        public float limbBulk;    // raggio ASSOLUTO di braccia/gambe (indipendente da thin): tuta più cicciotta del corpo
        public bool tanks;        // zaino-BOMBONE + ugelli: SOLO chi può volare (tuta). Il giocatore nudo non le ha.
    }

    /// <summary>Costruisce l'omino come figli di <paramref name="parent"/> secondo lo stile dato.</summary>
    public static void Build(Transform parent, Style s)
    {
        float t = Mathf.Clamp(s.thin, 0.4f, 1.5f);
        float lb = s.limbBulk > 0.01f ? s.limbBulk : t;   // spessore arti (assoluto); default = thin se non impostato

        // TORSO (lathe): più corto delle versioni precedenti, spalle ~0.35 a y≈0.68, top troncato-arrotondato a ~0.83.
        // I raggi del profilo sono moltiplicati per 'thin' (giocatore più magro). Le altezze NO (stessa statura).
        Vector2[] torso = {
            new Vector2(0.00f, -0.10f), new Vector2(0.15f, -0.04f), new Vector2(0.22f, 0.06f),
            new Vector2(0.24f, 0.28f),  new Vector2(0.27f, 0.46f),  new Vector2(0.32f, 0.60f),
            new Vector2(0.35f, 0.68f),  new Vector2(0.34f, 0.73f),  new Vector2(0.28f, 0.77f),
            new Vector2(0.16f, 0.80f),  new Vector2(0.05f, 0.82f),  new Vector2(0.00f, 0.83f),
        };
        for (int i = 0; i < torso.Length; i++) torso[i].x *= t;
        Lathe(parent, "Torso", torso, s);

        // GAMBE lunghe (dai fianchi y≈0 ai piedi y≈−1.0). Spessore = limbBulk (la tuta è più cicciotta del corpo).
        Part(parent, PrimitiveType.Capsule, "GambaSx", new Vector3(-0.15f, -0.50f, 0.02f), new Vector3(0.20f * lb, 0.50f, 0.20f * lb), Vector3.zero, s);
        Part(parent, PrimitiveType.Capsule, "GambaDx", new Vector3(0.15f, -0.50f, 0.02f), new Vector3(0.20f * lb, 0.50f, 0.20f * lb), Vector3.zero, s);

        // BRACCIA lunghe (la mano arriva ~all'alta coscia), agganciate alle SPALLE e aperte a V (~12° per lato).
        Part(parent, PrimitiveType.Capsule, "BraccioSx", new Vector3(-0.46f, 0.16f, 0.02f), new Vector3(0.16f * lb, 0.54f, 0.16f * lb), new Vector3(0f, 0f, -12f), s);
        Part(parent, PrimitiveType.Capsule, "BraccioDx", new Vector3(0.46f, 0.16f, 0.02f), new Vector3(0.16f * lb, 0.54f, 0.16f * lb), new Vector3(0f, 0f, 12f), s);

        // ZAINO-BOMBOLE + UGELLI dei motori: SOLO sulla tuta (chi può volare). Centrali e spesse, fino a TOCCARSI.
        if (s.tanks)
        {
            Part(parent, PrimitiveType.Capsule, "BombolaSx", new Vector3(-0.16f, 0.45f, -0.28f), new Vector3(0.26f * lb, 0.42f, 0.26f * lb), Vector3.zero, s);
            Part(parent, PrimitiveType.Capsule, "BombolaDx", new Vector3(0.16f, 0.45f, -0.28f), new Vector3(0.26f * lb, 0.42f, 0.26f * lb), Vector3.zero, s);
            GlowBall(parent, "UgelloSx", new Vector3(-0.16f, 0.05f, -0.28f), 0.20f * lb, s.accent);
            GlowBall(parent, "UgelloDx", new Vector3(0.16f, 0.05f, -0.28f), 0.20f * lb, s.accent);
        }

        // TESTA: leggermente STACCATA dal torso (top ≈ 0.83). Sfera luminosa (testa nuda) o CASCO.
        if (s.head == HeadKind.Helmet) BuildHelmet(parent, new Vector3(0f, 1.05f, 0f), 0.50f, s);
        else GlowBall(parent, "Testa", new Vector3(0f, 1.06f, 0f), 0.46f, s.accent);
    }

    // CASCO stilizzato: GUSCIO (sfera, stesso materiale del corpo) + VISIERA (ellissoide schiacciato sul fronte +Z,
    // vetro scuro LUMINOSO d'accento) + bordo-visiera scuro. Niente asset: due primitivi.
    static void BuildHelmet(Transform parent, Vector3 pos, float diam, Style s)
    {
        Part(parent, PrimitiveType.Sphere, "CascoGuscio", pos, Vector3.one * diam, Vector3.zero, s);   // guscio = materiale corpo
        // visiera: sfera schiacciata, spostata in avanti (+Z) e un filo in basso, vetro luminoso d'accento.
        var visor = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visor.name = "CascoVisiera";
        var vc = visor.GetComponent<Collider>(); if (vc) Object.Destroy(vc);
        visor.transform.SetParent(parent, false);
        visor.transform.localPosition = pos + new Vector3(0f, -0.03f, diam * 0.30f);
        visor.transform.localScale = new Vector3(diam * 0.74f, diam * 0.5f, diam * 0.66f);
        SetUnlit(visor, s.accent * 0.85f);   // vetro luminoso (accent un filo più scuro)
    }

    // ---- helpers ----------------------------------------------------------------------------------

    static void Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Vector3 euler, Style s)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        var c = go.GetComponent<Collider>(); if (c) Object.Destroy(c);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;
        ApplyBody(go.GetComponent<Renderer>(), s);
    }

    static void Lathe(Transform parent, string name, Vector2[] profile, Style s)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = ProcMesh.RevolveY(profile, 48, name);
        ApplyBody(go.AddComponent<MeshRenderer>(), s);
    }

    static void GlowBall(Transform parent, string name, Vector3 pos, float diam, Color col)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        var c = go.GetComponent<Collider>(); if (c) Object.Destroy(c);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = Vector3.one * diam;
        SetUnlit(go, col);
    }

    // materiale del CORPO: acciaio metallico (tuta) o Standard liscio del colore dato (giocatore).
    static void ApplyBody(Renderer r, Style s)
    {
        if (r == null) return;
        var sh = Shader.Find("Standard");
        if (sh == null) return;
        var m = new Material(sh) { color = s.bodyColor };
        if (s.metallic) { m.SetFloat("_Metallic", 0.9f); m.SetFloat("_Glossiness", 0.6f); }
        else { m.SetFloat("_Metallic", 0.0f); m.SetFloat("_Glossiness", 0.3f); }
        r.material = m;
    }

    // materiale LUMINOSO (Unlit/Color → niente variante _EMISSION strippata in build).
    static void SetUnlit(GameObject go, Color col)
    {
        var r = go.GetComponent<Renderer>(); if (r == null) return;
        var sh = Shader.Find("Unlit/Color");
        if (sh != null) r.material = new Material(sh) { color = col };
    }
}

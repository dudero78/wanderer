using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mostra/nasconde (tasto O) le orbite dei corpi del sistema come FILI LUMINOSI nel mondo, anche in volo —
/// non solo in mappa. Look "alla Outer Wilds": filo sottile a spessore COSTANTE in pixel (l'espansione è in
/// spazio schermo nel vertex shader Wanderer/OrbitLine, non un nastro in unità-mondo), bagliore additivo, e la
/// luce è piena dove sta il pianeta ADESSO e sfuma a coda andando indietro → si legge subito direzione e moto.
///
/// L'ellisse è FISSA nel frame del genitore (Kepler senza perturbazioni): la costruiamo UNA volta come mesh in
/// spazio locale al genitore (con la tangente per-vertice per l'espansione a schermo), poi ogni frame trasliamo
/// solo il GameObject con la posizione-scena del genitore (floating origin). Nessun solve orbitale, nessuna
/// allocazione, nessun loop per-vertice nel runtime: solo transform.position + un uniform di fase.
/// </summary>
public class OrbitDisplay : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.O;

    [Tooltip("Spessore del filo in pixel (la strip; il nucleo brillante è più sottile dell'alone).")]
    public float pixelWidth = 6f;

    // oltre questa distanza-scena del genitore l'orbita è di un sistema LONTANO → non disegnarla (evita il glitch
    // dell'espansione a schermo coi vertici dietro la camera). Un sistema sta entro ~150 km; soglia ben sopra, ben sotto l'interstellare.
    const float MaxParentDist = 800000f;

    SolarSystem solar;
    readonly List<Transform> nodes = new List<Transform>();
    readonly List<CelestialBody> bodies = new List<CelestialBody>();
    readonly List<MeshRenderer> renderers = new List<MeshRenderer>();
    readonly List<Material> mats = new List<Material>();
    bool visible;
    int builtCount;   // n. corpi al momento del Build: se cambia (sistema svegliato/addormentato) si ricostruisce

    static readonly int IdPeakU = Shader.PropertyToID("_PeakU");

    // Tinte tenui per corpo (cicliche): pallide e luminose, stile carta stellare OW.
    static readonly Color[] Palette =
    {
        new Color(0.55f, 0.78f, 1.00f),   // azzurro pallido
        new Color(0.70f, 0.86f, 0.96f),   // ghiaccio
        new Color(0.96f, 0.80f, 0.62f),   // caldo
        new Color(0.72f, 0.92f, 0.80f),   // verde tenue
    };

    public void Init(SolarSystem s)
    {
        solar = s;
        Build();
        Show(false);
    }

    void Build()
    {
        var shader = Shader.Find("Wanderer/OrbitLine");
        if (shader == null) return;                       // guardia: niente shader → niente orbite (non crash)
        const int n = 200;

        for (int i = 0; i < solar.Bodies.Count; i++)
        {
            var b = solar.Bodies[i];
            if (b == null || b.Orbit == null || b.Parent == null) continue;

            // campiona l'ellisse una volta, relativa al genitore (float locale al genitore)
            var pts = new Vector3[n];
            for (int k = 0; k < n; k++)
                pts[k] = b.Orbit.GetRelativePosition(b.Orbit.Period * (k / (double)n)).ToVector3();

            var go = new GameObject("OrbitLine_" + b.gameObject.name);
            go.transform.SetParent(transform, false);

            var mat = new Material(shader);
            mat.SetColor("_Color", Palette[mats.Count % Palette.Length]);
            mat.SetFloat("_PixelWidth", pixelWidth);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = BuildRibbon(pts);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            nodes.Add(go.transform);
            bodies.Add(b);
            renderers.Add(mr);
            mats.Add(mat);
        }
        builtCount = solar.Bodies.Count;
    }

    // Ricostruisce quando l'insieme dei corpi cambia (un sistema distante svegliato porta nuovi pianeti con orbita).
    void Rebuild()
    {
        for (int i = 0; i < nodes.Count; i++) if (nodes[i] != null) Destroy(nodes[i].gameObject);
        nodes.Clear(); bodies.Clear(); renderers.Clear(); mats.Clear();
        Build();
        Show(visible);
    }

    // Nastro chiuso a 2 vertici per campione: posizione = punto dell'ellisse, NORMAL = tangente (per
    // l'espansione a schermo), uv = (lungo-anello 0..1, lato 0/1). L'AABB è gonfiato perché i vertici
    // veri si spostano in spazio schermo nel vertex shader (altrimenti il culling lo taglierebbe).
    static Mesh BuildRibbon(Vector3[] pts)
    {
        int n = pts.Length;
        var verts = new Vector3[n * 2];
        var norms = new Vector3[n * 2];
        var uvs = new Vector2[n * 2];
        var tris = new int[n * 6];

        for (int k = 0; k < n; k++)
        {
            Vector3 prev = pts[(k - 1 + n) % n];
            Vector3 next = pts[(k + 1) % n];
            Vector3 tan = (next - prev);
            tan = tan.sqrMagnitude > 1e-12f ? tan.normalized : Vector3.forward;
            float along = k / (float)n;

            int v0 = k * 2, v1 = k * 2 + 1;
            verts[v0] = pts[k]; norms[v0] = tan; uvs[v0] = new Vector2(along, 0f);
            verts[v1] = pts[k]; norms[v1] = tan; uvs[v1] = new Vector2(along, 1f);

            int kn = (k + 1) % n;
            int a = k * 2, c = kn * 2;
            int t = k * 6;
            tris[t + 0] = a; tris[t + 1] = a + 1; tris[t + 2] = c + 1;
            tris[t + 3] = a; tris[t + 4] = c + 1; tris[t + 5] = c;
        }

        var mesh = new Mesh { name = "OrbitRibbon" };
        if (n * 2 > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts;
        mesh.normals = norms;
        mesh.uv = uvs;
        mesh.SetTriangles(tris, 0);
        // bounds generosi: i vertici si allargano a schermo, e l'anello viene comunque traslato col genitore
        float r = 0f;
        for (int k = 0; k < n; k++) r = Mathf.Max(r, pts[k].magnitude);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (r * 2.2f));
        return mesh;
    }

    void Update()
    {
        if (solar == null) return;
        if (solar.Bodies.Count != builtCount) Rebuild();   // un sistema svegliato/addormentato → aggiorna le orbite
        if (Input.GetKeyDown(toggleKey)) { visible = !visible; Show(visible); }
        if (!visible) return;

        double simTime = SolarSystem.Instance != null ? SolarSystem.Instance.SimTime : 0.0;
        var curSys = solar.Reference != null ? solar.Reference.System : null;   // il sistema in cui ti trovi (ancora)
        for (int i = 0; i < nodes.Count; i++)
        {
            var b = bodies[i];
            if (b == null || b.Parent == null) { renderers[i].enabled = false; continue; }
            // SOLO il sistema in cui ti trovi: le orbite di un sistema lontano hanno vertici dietro la camera →
            // l'espansione a schermo glitcha. Gate per SISTEMA (agnostico) + per distanza del genitore (di sicurezza).
            if (curSys != null && b.System != curSys) { renderers[i].enabled = false; continue; }
            float parentDist = b.Parent.transform.position.magnitude;
            if (parentDist > MaxParentDist) { renderers[i].enabled = false; continue; }
            renderers[i].enabled = true;   // sistema vicino → mostrala (riacceso se prima era lontano)
            nodes[i].position = b.Parent.transform.position;   // segue la floating origin (solo traslazione)

            // fase del pianeta sull'anello: il campionamento mappa u = k/n all'anomalia media (lineare nel
            // tempo) → la posizione attuale è frac(SimTime/Period); l'M0 si cancella.
            double u = simTime / b.Orbit.Period;
            mats[i].SetFloat(IdPeakU, (float)(u - System.Math.Floor(u)));
        }
    }

    void Show(bool on)
    {
        for (int i = 0; i < renderers.Count; i++) if (renderers[i] != null) renderers[i].enabled = on;
    }
}

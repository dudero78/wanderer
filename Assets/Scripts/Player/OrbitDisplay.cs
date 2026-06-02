using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mostra/nasconde (tasto O) le orbite dei corpi del sistema come linee nel mondo, anche in volo — non
/// solo in mappa. Le linee seguono la floating origin: l'ellisse è FISSA nel sistema di riferimento del
/// genitore (Kepler senza perturbazioni), quindi la campioniamo UNA volta e ogni frame la trasliamo solo
/// con la posizione-scena del genitore → niente solve orbitale per frame. Aiuto di navigazione, tenue.
/// </summary>
public class OrbitDisplay : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.O;

    SolarSystem solar;
    readonly List<LineRenderer> lines = new List<LineRenderer>();
    readonly List<CelestialBody> bodies = new List<CelestialBody>();
    readonly List<Vector3[]> rel = new List<Vector3[]>();   // ellisse cacheata, relativa al genitore
    bool visible;

    public void Init(SolarSystem s)
    {
        solar = s;
        Build();
        Show(false);
    }

    void Build()
    {
        var lineShader = Shader.Find("Sprites/Default");
        if (lineShader == null) return;
        float width = SystemRadius() * 0.0015f;
        const int n = 128;

        for (int i = 0; i < solar.Bodies.Count; i++)
        {
            var b = solar.Bodies[i];
            if (b == null || b.Orbit == null || b.Parent == null) continue;

            var lgo = new GameObject("OrbitLine_" + b.gameObject.name);
            lgo.transform.SetParent(transform, false);
            var lr = lgo.AddComponent<LineRenderer>();
            lr.material = new Material(lineShader);
            lr.useWorldSpace = true;
            lr.loop = true;
            lr.widthMultiplier = width;
            lr.numCapVertices = 2;
            lr.positionCount = n;
            var col = new Color(0.6f, 0.72f, 0.95f, 0.35f);
            lr.startColor = col; lr.endColor = col;

            // campiona l'ellisse una volta, relativa al genitore (in spazio float locale al genitore)
            var pts = new Vector3[n];
            for (int k = 0; k < n; k++)
                pts[k] = b.Orbit.GetRelativePosition(b.Orbit.Period * (k / (double)n)).ToVector3();

            lines.Add(lr);
            bodies.Add(b);
            rel.Add(pts);
        }
    }

    float SystemRadius()
    {
        float r = 3000f;
        for (int i = 0; i < solar.Bodies.Count; i++)
        {
            var b = solar.Bodies[i];
            if (b != null && b.Orbit != null)
                r = Mathf.Max(r, (float)(b.Orbit.SemiMajorAxis * (1.0 + b.Orbit.Eccentricity)));
        }
        return r;
    }

    void Update()
    {
        if (solar == null) return;
        if (Input.GetKeyDown(toggleKey)) { visible = !visible; Show(visible); }
        if (!visible) return;

        for (int i = 0; i < lines.Count; i++)
        {
            var b = bodies[i];
            var lr = lines[i];
            if (b == null || b.Parent == null) { lr.enabled = false; continue; }
            Vector3 parentScene = b.Parent.transform.position;   // segue la floating origin
            var pts = rel[i];
            for (int k = 0; k < pts.Length; k++)
                lr.SetPosition(k, parentScene + pts[k]);
        }
    }

    void Show(bool on)
    {
        for (int i = 0; i < lines.Count; i++) if (lines[i] != null) lines[i].enabled = on;
    }
}

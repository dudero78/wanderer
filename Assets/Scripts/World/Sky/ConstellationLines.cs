using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Linee delle costellazioni + etichette (nomi delle stelle brillanti e delle figure). Dati CURATI a mano dalle
/// coordinate reali RA/Dec (fatti di catalogo, GPL-clean: NON derivati dai file di Stellarium), portati nel frame di
/// gioco con la STESSA rotazione delle stelle (<see cref="SkyData.StarDirection"/>) → le linee cadono sulle stelle.
/// Toggle col tasto <see cref="ToggleKey"/> (fade morbido). Le etichette compaiono col reticolo, in IMGUI.
/// Insieme che si può ampliare: aggiungere una figura = una voce in <see cref="Build"/>.
/// </summary>
public class ConstellationLines : MonoBehaviour
{
    public const KeyCode ToggleKey = KeyCode.C;
    const float Radius = 100f;   // = StarFieldRenderer.Radius

    class Figure { public string name; public string[] starName; public Vector2[] radec; public int[] seg; }

    Transform skyRoot;
    Camera playerCam;
    Material mat;
    float alpha, target;
    readonly List<(Vector3 dir, string name)> labels = new List<(Vector3, string)>();   // stelle + centroidi
    readonly List<Vector2> placed = new List<Vector2>();   // posizioni etichette già disegnate (anti-sovrapposizione)

    public void Build(Transform root, int layer, Camera cam)
    {
        skyRoot = root; playerCam = cam;
        var figs = Catalog();

        var sh = Shader.Find("Wanderer/ConstellationLine");
        if (sh == null) { Debug.LogError("[sky] shader Wanderer/ConstellationLine non trovato (Always Included?)."); return; }

        // raccogli i segmenti (coppie di direzioni) + le etichette
        var segs = new List<(Vector3 a, Vector3 b)>();
        foreach (var f in figs)
        {
            var dirs = new Vector3[f.radec.Length];
            for (int i = 0; i < f.radec.Length; i++)
            {
                dirs[i] = SkyData.StarDirection(f.radec[i].x, f.radec[i].y);
                if (f.starName != null && i < f.starName.Length && !string.IsNullOrEmpty(f.starName[i]))
                    labels.Add((dirs[i], f.starName[i]));
            }
            for (int s = 0; s + 1 < f.seg.Length; s += 2) segs.Add((dirs[f.seg[s]], dirs[f.seg[s + 1]]));
            var cen = Vector3.zero; foreach (var d in dirs) cen += d;
            if (cen.sqrMagnitude > 1e-6f) labels.Add((cen.normalized, f.name.ToUpperInvariant()));
        }

        // ogni segmento = un QUAD (espanso a spessore-px costante nel vertex shader): nucleo morbido = linea liscia.
        var verts = new List<Vector3>(segs.Count * 4);
        var norms = new List<Vector3>(segs.Count * 4);
        var uvs = new List<Vector2>(segs.Count * 4);
        var tris = new List<int>(segs.Count * 6);
        foreach (var (a, b) in segs)
        {
            Vector3 pa = a * Radius, pb = b * Radius, tan = (pb - pa).normalized;
            int bi = verts.Count;
            verts.Add(pa); verts.Add(pa); verts.Add(pb); verts.Add(pb);
            norms.Add(tan); norms.Add(tan); norms.Add(tan); norms.Add(tan);
            uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(1, 0));
            tris.Add(bi); tris.Add(bi + 1); tris.Add(bi + 2); tris.Add(bi); tris.Add(bi + 2); tris.Add(bi + 3);
        }

        var mesh = new Mesh { name = "Constellations", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0, false);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);

        var go = new GameObject("Constellations");
        go.transform.SetParent(root, false);
        if (layer >= 0) go.layer = layer;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mat = new Material(sh); mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false; mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
    }

    void Update()
    {
        bool active = !MapMode.IsOpen && playerCam != null && playerCam.isActiveAndEnabled;
        if (active && Input.GetKeyDown(ToggleKey)) target = target > 0.5f ? 0f : 1f;
        alpha = Mathf.MoveTowards(alpha, active ? target : 0f, Time.unscaledDeltaTime / 0.35f);
        if (mat != null) mat.SetFloat("_Alpha", alpha * 0.9f);
    }

    void OnGUI()
    {
        if (alpha < 0.05f || playerCam == null || !playerCam.isActiveAndEnabled || MapMode.IsOpen) return;
        if (Event.current.type != EventType.Repaint) return;

        var style = GUI.skin.label;
        var prev = GUI.color;
        Vector3 origin = skyRoot != null ? skyRoot.position : playerCam.transform.position;
        // WorldToScreenPoint dà coordinate nella RenderTexture della camera (ridotta dal RenderScaler): riscalale a
        // schermo pieno, altrimenti le etichette si ammucchiano verso un angolo (disallineate dalle stelle disegnate).
        float sx = (float)Screen.width / Mathf.Max(1, playerCam.pixelWidth);
        float sy = (float)Screen.height / Mathf.Max(1, playerCam.pixelHeight);
        placed.Clear();
        for (int i = 0; i < labels.Count; i++)
        {
            Vector3 world = origin + labels[i].dir * Radius;
            Vector3 sp = playerCam.WorldToScreenPoint(world);
            if (sp.z <= 0) continue;
            float x = sp.x * sx, y = Screen.height - sp.y * sy;
            if (x < 0 || x > Screen.width || y < 0 || y > Screen.height) continue;

            // anti-sovrapposizione: salta se troppo vicina a un'etichetta già messa
            bool clash = false;
            var p = new Vector2(x, y);
            for (int k = 0; k < placed.Count; k++)
                if ((placed[k] - p).sqrMagnitude < 34f * 34f) { clash = true; break; }
            if (clash) continue;
            placed.Add(p);

            bool isFig = labels[i].name == labels[i].name.ToUpperInvariant() && labels[i].name.Length > 2;
            GUI.color = isFig ? new Color(0.6f, 0.8f, 1f, alpha * 0.7f) : new Color(0.85f, 0.9f, 1f, alpha * 0.85f);
            GUI.Label(new Rect(x + 6, y - 8, 200, 20), labels[i].name, style);
        }
        GUI.color = prev;
    }

    // ---- dati curati (RA/Dec gradi J2000) -------------------------------------------------------------------------
    static Vector2 P(float ra, float dec) => new Vector2(ra, dec);

    static List<Figure> Catalog()
    {
        var L = new List<Figure>();

        // Orione
        L.Add(new Figure { name = "Orione",
            starName = new[] { "Betelgeuse", "Bellatrix", "Mintaka", "Alnilam", "Alnitak", "Saiph", "Rigel", "Meissa" },
            radec = new[] { P(88.793f,7.407f), P(81.283f,6.350f), P(83.002f,-0.299f), P(84.053f,-1.202f),
                            P(85.190f,-1.943f), P(86.939f,-9.670f), P(78.634f,-8.202f), P(83.784f,9.934f) },
            seg = new[] { 1,0, 0,4, 1,2, 2,3, 3,4, 4,5, 6,2, 7,0, 7,1 } });

        // Grande Carro (Orsa Maggiore)
        L.Add(new Figure { name = "Grande Carro",
            starName = new[] { "Dubhe", "Merak", "Phecda", "Megrez", "Alioth", "Mizar", "Alkaid" },
            radec = new[] { P(165.932f,61.751f), P(165.460f,56.383f), P(178.458f,53.695f), P(183.857f,57.033f),
                            P(193.507f,55.960f), P(200.981f,54.925f), P(206.885f,49.313f) },
            seg = new[] { 0,1, 1,2, 2,3, 3,0, 3,4, 4,5, 5,6 } });

        // Cassiopea
        L.Add(new Figure { name = "Cassiopea",
            starName = new[] { "Caph", "Schedar", "Gamma Cas", "Ruchbah", "Segin" },
            radec = new[] { P(2.295f,59.150f), P(10.127f,56.537f), P(14.177f,60.717f), P(21.454f,60.235f), P(28.599f,63.670f) },
            seg = new[] { 0,1, 1,2, 2,3, 3,4 } });

        // Cigno (Croce del Nord)
        L.Add(new Figure { name = "Cigno",
            starName = new[] { "Deneb", "Sadr", "Gienah", "Delta Cyg", "Albireo" },
            radec = new[] { P(310.358f,45.280f), P(305.557f,40.257f), P(311.553f,33.970f), P(296.243f,45.131f), P(292.680f,27.960f) },
            seg = new[] { 0,1, 1,4, 2,1, 1,3 } });

        // Lira
        L.Add(new Figure { name = "Lira",
            starName = new[] { "Vega", "Sheliak", "Sulafat", "Zeta Lyr" },
            radec = new[] { P(279.234f,38.784f), P(282.520f,33.363f), P(284.736f,32.690f), P(281.193f,37.605f) },
            seg = new[] { 0,3, 3,1, 1,2, 2,3 } });

        // Aquila
        L.Add(new Figure { name = "Aquila",
            starName = new[] { "Altair", "Tarazed", "Alshain" },
            radec = new[] { P(297.696f,8.868f), P(296.565f,10.613f), P(298.828f,6.407f) },
            seg = new[] { 1,0, 0,2 } });

        // Croce del Sud
        L.Add(new Figure { name = "Croce del Sud",
            starName = new[] { "Acrux", "Mimosa", "Gacrux", "Imai" },
            radec = new[] { P(186.650f,-63.099f), P(191.930f,-59.689f), P(187.791f,-57.113f), P(183.786f,-58.749f) },
            seg = new[] { 0,2, 1,3 } });

        // Leone
        L.Add(new Figure { name = "Leone",
            starName = new[] { "Regolo", "Algieba", "Zosma", "Denebola", "Chort", "Eta Leo" },
            radec = new[] { P(152.093f,11.967f), P(154.993f,19.842f), P(168.527f,20.524f), P(177.265f,14.572f),
                            P(168.560f,15.430f), P(151.833f,16.763f) },
            seg = new[] { 0,5, 5,1, 1,2, 2,4, 4,0, 2,3, 3,4 } });

        // Scorpione (testa + Antares + coda)
        L.Add(new Figure { name = "Scorpione",
            starName = new[] { "Antares", "Dschubba", "Pi Sco", "Graffias", "Shaula", "Sargas" },
            radec = new[] { P(247.352f,-26.432f), P(240.083f,-22.622f), P(239.713f,-26.114f), P(241.359f,-19.805f),
                            P(263.402f,-37.104f), P(264.330f,-42.998f) },
            seg = new[] { 3,1, 1,2, 2,0, 0,5, 5,4 } });

        // Gemelli
        L.Add(new Figure { name = "Gemelli",
            starName = new[] { "Castore", "Polluce", "Alhena" },
            radec = new[] { P(113.650f,31.888f), P(116.329f,28.026f), P(99.428f,16.399f) },
            seg = new[] { 0,1, 0,2 } });

        // Cane Maggiore
        L.Add(new Figure { name = "Cane Maggiore",
            starName = new[] { "Sirio", "Wezen", "Adhara", "Mirzam" },
            radec = new[] { P(101.287f,-16.716f), P(107.098f,-26.393f), P(104.656f,-28.972f), P(95.675f,-17.956f) },
            seg = new[] { 3,0, 0,1, 1,2 } });

        // Toro (Aldebaran–Elnath)
        L.Add(new Figure { name = "Toro",
            starName = new[] { "Aldebaran", "Elnath" },
            radec = new[] { P(68.980f,16.509f), P(81.573f,28.608f) },
            seg = new[] { 0,1 } });

        // Auriga (Capella–Menkalinan)
        L.Add(new Figure { name = "Auriga",
            starName = new[] { "Capella", "Menkalinan" },
            radec = new[] { P(79.172f,45.998f), P(89.882f,44.947f) },
            seg = new[] { 0,1 } });

        // Cane Minore
        L.Add(new Figure { name = "Cane Minore",
            starName = new[] { "Procione", "Gomeisa" },
            radec = new[] { P(114.825f,5.225f), P(111.788f,8.289f) },
            seg = new[] { 0,1 } });

        // Quadrato di Pegaso
        L.Add(new Figure { name = "Pegaso",
            starName = new[] { "Markab", "Scheat", "Algenib", "Alpheratz" },
            radec = new[] { P(346.190f,15.205f), P(345.944f,28.083f), P(3.309f,15.184f), P(2.097f,29.090f) },
            seg = new[] { 0,1, 1,3, 3,2, 2,0 } });

        // Boote (solo Arcturus, etichetta)
        L.Add(new Figure { name = "Boote",
            starName = new[] { "Arturo" },
            radec = new[] { P(213.915f,19.182f) }, seg = new int[0] });

        // Andromeda
        L.Add(new Figure { name = "Andromeda",
            starName = new[] { "Alpheratz", "Delta And", "Mirach", "Almach" },
            radec = new[] { P(2.097f,29.090f), P(11.510f,30.861f), P(17.433f,35.621f), P(30.975f,42.330f) },
            seg = new[] { 0,1, 1,2, 2,3 } });

        // Perseo
        L.Add(new Figure { name = "Perseo",
            starName = new[] { "Mirfak", "Algol", "Gamma Per", "Delta Per", "Epsilon Per" },
            radec = new[] { P(51.081f,49.861f), P(47.042f,40.956f), P(46.199f,53.506f), P(59.464f,47.788f), P(59.741f,40.010f) },
            seg = new[] { 1,0, 0,2, 0,3, 3,4 } });

        // Ercole (la Chiave di Volta)
        L.Add(new Figure { name = "Ercole",
            starName = new[] { "Zeta Her", "Eta Her", "Pi Her", "Epsilon Her" },
            radec = new[] { P(250.321f,31.603f), P(245.480f,38.922f), P(258.758f,36.809f), P(255.073f,30.926f) },
            seg = new[] { 0,1, 1,2, 2,3, 3,0 } });

        // Sagittario (la Teiera)
        L.Add(new Figure { name = "Sagittario",
            starName = new[] { "Kaus Australis", "Kaus Media", "Kaus Borealis", "Nunki", "Ascella", "Phi Sgr" },
            radec = new[] { P(276.043f,-34.385f), P(274.407f,-29.828f), P(276.993f,-25.421f),
                            P(283.816f,-26.297f), P(287.441f,-29.880f), P(281.414f,-26.990f) },
            seg = new[] { 0,1, 1,2, 2,5, 5,3, 3,4, 4,1, 4,0 } });

        // Vergine
        L.Add(new Figure { name = "Vergine",
            starName = new[] { "Spica", "Porrima", "Vindemiatrix" },
            radec = new[] { P(201.298f,-11.161f), P(190.415f,-1.449f), P(195.544f,10.959f) },
            seg = new[] { 0,1, 1,2 } });

        // Centauro (e i Puntatori verso la Croce del Sud)
        L.Add(new Figure { name = "Centauro",
            starName = new[] { "Rigil Kent.", "Hadar", "Gamma Cen", "Menkent" },
            radec = new[] { P(219.902f,-60.834f), P(210.956f,-60.373f), P(190.379f,-48.960f), P(211.671f,-36.370f) },
            seg = new[] { 0,1, 1,2, 2,3 } });

        // Stella Polare (etichetta, niente figura)
        L.Add(new Figure { name = "Orsa Minore",
            starName = new[] { "Polaris" },
            radec = new[] { P(37.954f,89.264f) }, seg = new int[0] });

        // Asterismo: Triangolo Estivo (Vega–Deneb–Altair) — lega la nostra Vega di gioco al cielo
        L.Add(new Figure { name = "Triangolo Estivo",
            starName = new string[] { null, null, null },
            radec = new[] { P(279.234f,38.784f), P(310.358f,45.280f), P(297.696f,8.868f) },
            seg = new[] { 0,1, 1,2, 2,0 } });

        return L;
    }
}

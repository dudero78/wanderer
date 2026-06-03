using UnityEngine;
using UnityEditor;

/// <summary>
/// Comando editor "Wanderer/Test parità altezza GPU↔CPU". Verifica che il compute shader
/// PlanetHeight.compute produca le STESSE altezze del walker (PlanetTerrain.SampleHeight). È il
/// gate di sicurezza della strada "geometria su GPU": se GPU e CPU divergono, in gioco il giocatore
/// galleggerebbe/sprofonderebbe sui nodi calcolati dalla GPU.
///
/// Confronta su molte direzioni casuali (uniformi sulla sfera) per pianeta + Cetra. Stampa max/avg
/// differenza in METRI. Tolleranza ~1 cm: l'hash è intero (gradienti identici bit-per-bit), resta
/// solo il rounding float → deve essere sub-centimetrico.
/// </summary>
public static class PlanetGpuParityTest
{
    const int Samples = 4096;
    const float TolMeters = 0.01f;

    [MenuItem("Wanderer/Test parità altezza GPU↔CPU")]
    public static void Run()
    {
        bool okAll = true;
        okAll &= TestBody("Pianeta", PlanetPresets.ConfigureDemoPlanet);
        okAll &= TestBody("Cetra", t => GameBootstrap.ApplyCetraRecipe(t));
        // copre i parametri crateri ESTESI sul path GPU (pesi per taglia + distribuzione): se divergessero,
        // l'anteprima GPU dell'editor mostrerebbe crateri diversi dalla CPU.
        okAll &= TestBody("Crateri pesati+distrib", t =>
        {
            var rec = PlanetRecipe.SmoothSphere();
            rec.processes.Add(new ProcessStep
            {
                type = ProcessType.Crateri, seed = 4242, octaves = 6, largestRadius = 90f,
                density = 0.5f, depthRatio = 0.2f, rimRatio = 0.3f, rimSharpness = 2.5f,
                wLarge = 0.3f, wMedium = 1f, wSmall = 0.7f, distribution = 0.6f
            });
            t.ApplyRecipe(rec);
        });
        Debug.Log(okAll ? "<color=green>Parità GPU↔CPU: OK su tutti i corpi.</color>"
                        : "<color=red>Parità GPU↔CPU: FALLITA — NON integrare finché non combaciano.</color>");
    }

    static bool TestBody(string label, System.Action<PlanetTerrain> configure)
    {
        var go = new GameObject("__parityTemp");
        try
        {
            var terrain = go.AddComponent<PlanetTerrain>();
            configure(terrain);
            terrain.RebuildLayers();

            var baker = new GpuHeightBaker(terrain, 32);
            if (!baker.Supported) { Debug.LogError($"{label}: GPU baker non supportato (compute mancante?)."); return false; }

            // direzioni uniformi sulla sfera (spirale di Fibonacci: copertura regolare, niente cluster)
            var dirs = new Vector3[Samples];
            float ga = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < Samples; i++)
            {
                float z = 1f - 2f * (i + 0.5f) / Samples;
                float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
                float a = ga * i;
                dirs[i] = new Vector3(Mathf.Cos(a) * r, z, Mathf.Sin(a) * r).normalized;
            }

            float[] gpu = baker.SampleHeightsBlocking(dirs);

            float maxDiff = 0f, sumDiff = 0f, worstH = 0f;
            for (int i = 0; i < Samples; i++)
            {
                float cpu = terrain.SampleHeight(dirs[i]);
                float d = Mathf.Abs(cpu - gpu[i]);
                sumDiff += d;
                if (d > maxDiff) { maxDiff = d; worstH = cpu; }
            }
            float avg = sumDiff / Samples;
            bool okH = maxDiff <= TolMeters;
            string colH = okH ? "green" : "red";
            Debug.Log($"<color={colH}>{label} [altezza]: max Δ = {maxDiff * 1000f:F3} mm, avg = {avg * 1000f:F4} mm "
                    + $"(su h≈{worstH:F1} m, {Samples} campioni) → {(okH ? "OK" : "FUORI TOLLERANZA")}</color>");

            // PARITÀ DELLA GRIGLIA DI UN NODO: è il percorso che il quadtree usa davvero (float3→ora float
            // piatto). Confronta la griglia GPU con quella calcolata su CPU per un nodo campione.
            bool okN = TestNodeGrid(label, terrain, baker);
            baker.Dispose();
            return okH && okN;
        }
        finally { Object.DestroyImmediate(go); }
    }

    /// <summary>Confronta la griglia estesa di un nodo campione calcolata su GPU (kernel CSNodeGrid) con
    /// quella su CPU (stessa formula del quadtree). Verifica il percorso buffer/ParamToDir, non solo la
    /// matematica del rumore. Δ in METRI come distanza fra i due punti 3D.</summary>
    static bool TestNodeGrid(string label, PlanetTerrain terrain, GpuHeightBaker baker)
    {
        // nodo campione: faccia 0, patch a profondità 3, non sul bordo
        Vector3 up = PlanetMeshBuilder.FaceNormals[0];
        PlanetMeshBuilder.FaceAxes(up, out var axisA, out var axisB);
        float size = 1f / 8f, u0 = 3f / 8f, v0 = 4f / 8f;
        int nodeRes = 32;
        int ne = baker.Ne;                  // nodeRes+3
        float step = size / nodeRes;

        Vector3[] gpu = baker.NodeGridBlocking(up, axisA, axisB, u0, v0, step);

        float maxDiff = 0f;
        for (int y = 0; y < ne; y++)
            for (int x = 0; x < ne; x++)
            {
                float tx = u0 + (x - 1) * step;
                float ty = v0 + (y - 1) * step;
                Vector3 dir = PlanetMeshBuilder.ParamToDir(up, axisA, axisB, tx, ty);
                Vector3 cpu = dir * terrain.SampleHeight(dir);
                float d = (cpu - gpu[x + y * ne]).magnitude;
                if (d > maxDiff) maxDiff = d;
            }
        bool ok = maxDiff <= TolMeters;
        string col = ok ? "green" : "red";
        Debug.Log($"<color={col}>{label} [griglia nodo]: max Δ = {maxDiff * 1000f:F3} mm ({ne}×{ne} vertici) "
                + $"→ {(ok ? "OK" : "GRIGLIA CORROTTA")}</color>");
        return ok;
    }
}

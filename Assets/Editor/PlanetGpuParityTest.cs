using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

/// <summary>
/// Verifica che il compute shader (PlanetHeightCore.hlsl → SampleHeightD) produca le STESSE altezze del walker
/// (PlanetTerrain.SampleHeight). È il gate di sicurezza del #17 ("fonte unica altezza"): la forma del terreno è
/// duplicata a mano in C# (TerrainLayer) e in HLSL, e mantenere la parità a mano è error-prone — un cambio su un
/// lato non rispecchiato sull'altro fa GALLEGGIARE/SPROFONDARE il giocatore. Finché non c'è un vero single-source
/// (transpiler C#→HLSL), questo gate rende la duplicazione SICURA: la divergenza viene colta SUBITO.
///
/// Copre: le ricette UFFICIALI (Resources/Planets/*.json — i corpi che spedisci) + casi-limite costruiti a mano
/// (crateri pesati/distribuzione, mare dopo crateri = ordine pipeline, tettonica). Confronta su direzioni uniformi
/// (spirale di Fibonacci) + la griglia di un nodo. Tolleranza ~1 cm (hash interi = gradienti identici bit-per-bit,
/// resta solo il rounding float).
///
/// Due modi: il MENU "Wanderer/Test parità altezza GPU↔CPU" (verboso, completo) e il GATE AUTOMATICO che gira a ogni
/// ricompila (<see cref="PlanetParityGate"/>, leggero) → la divergenza non aspetta più il Play.
/// </summary>
public static class PlanetGpuParityTest
{
    const float TolMeters = 0.01f;

    struct BodyCase { public string label; public Action<PlanetTerrain> configure; }

    /// <summary>I corpi su cui verificare la parità: prima le ricette UFFICIALI (quelle che spedisci), poi i
    /// casi-limite a mano che stressano i percorsi delicati (ordine pipeline, pesi crateri, tettonica).</summary>
    static List<BodyCase> Cases()
    {
        var cases = new List<BodyCase>();

        // RICETTE UFFICIALI: ogni .json in Resources/Planets. Sono i corpi reali del gioco → se una la modifichi
        // (editor) e l'HLSL non rispecchia, QUI si vede subito, senza entrare in Play.
        string[] official = { "Cetra", "Luna6", "Luna7", "terra-test3", "Valentina2" };
        foreach (var name in official)
        {
            string n = name;   // cattura per-iterazione
            var rec = PlanetRecipe.LoadResource(n);
            if (rec == null) continue;   // mancante: già loggato da LoadResource; non far fallire il gate
            cases.Add(new BodyCase { label = "ricetta:" + n, configure = t => t.ApplyRecipe(rec) });
        }
        // pianeta-casa (preset di codice, non da JSON)
        cases.Add(new BodyCase { label = "Pianeta-casa", configure = PlanetPresets.ConfigureDemoPlanet });

        // CASI-LIMITE a mano (stress dei percorsi delicati, indipendenti dalle ricette spedite):
        cases.Add(new BodyCase
        {
            label = "Crateri pesati+distrib",
            configure = t =>
            {
                var rec = PlanetRecipe.SmoothSphere();
                rec.processes.Add(new ProcessStep
                {
                    type = ProcessType.Crateri, seed = 4242, octaves = 6, largestRadius = 90f,
                    density = 0.5f, depthRatio = 0.2f, rimRatio = 0.3f, rimSharpness = 2.5f,
                    wLarge = 0.3f, wMedium = 1f, wSmall = 0.7f, distribution = 0.6f
                });
                t.ApplyRecipe(rec);
            }
        });
        cases.Add(new BodyCase
        {
            label = "Crateri+Mare (ordine)",
            configure = t =>
            {
                var rec = PlanetRecipe.SmoothSphere();
                rec.processes.Add(new ProcessStep { type = ProcessType.Crateri, seed = 11, octaves = 5, largestRadius = 80f, density = 0.6f, depthRatio = 0.22f, rimRatio = 0.3f, rimSharpness = 2f });
                rec.processes.Add(new ProcessStep { type = ProcessType.Mare, seed = 22, seaLevel = 6f, seaRoughness = 4f, seaRoughScale = 3.5f, seaForma = -0.4f });
                t.ApplyRecipe(rec);
            }
        });
        cases.Add(new BodyCase
        {
            label = "Tettonica",
            configure = t =>
            {
                var rec = PlanetRecipe.SmoothSphere();
                rec.processes.Add(new ProcessStep { type = ProcessType.Tettonica, seed = 33, plateCount = 14, continentalFraction = 0.45f, elevationContrast = 60f, boundaryUplift = 40f, boundaryWidth = 0.08f, tectonicWarp = 0.5f, coastSlope = 0.5f });
                t.ApplyRecipe(rec);
            }
        });
        return cases;
    }

    [MenuItem("Wanderer/Test parità altezza GPU↔CPU")]
    public static void Run() => RunAll(verbose: true, samples: 4096, nodeGrid: true);

    /// <summary>Esegue la parità su tutti i corpi. verbose = log per-corpo (menu); altrimenti solo un riassunto e,
    /// in caso di divergenza, l'ERRORE rosso (gate automatico). Ritorna true se TUTTO combacia entro tolleranza.</summary>
    public static bool RunAll(bool verbose, int samples, bool nodeGrid)
    {
        var cases = Cases();
        bool okAll = true;
        int diverged = 0;
        float worstAll = 0f; string worstLabel = "";
        foreach (var c in cases)
        {
            bool ok = TestBody(c.label, c.configure, samples, nodeGrid, verbose, out float worst);
            okAll &= ok;
            if (!ok) diverged++;
            if (worst > worstAll) { worstAll = worst; worstLabel = c.label; }
        }
        if (verbose)
            Debug.Log(okAll ? $"<color=green>Parità GPU↔CPU: OK su tutti i {cases.Count} corpi (Δ max {worstAll * 1000f:F2} mm su {worstLabel}).</color>"
                            : "<color=red>Parità GPU↔CPU: FALLITA — NON integrare finché non combaciano.</color>");
        else if (okAll)
            Debug.Log($"[parità GPU↔CPU] gate: OK ({cases.Count} corpi, Δ max {worstAll * 1000f:F2} mm su {worstLabel}).");
        else
            Debug.LogError($"[parità GPU↔CPU] DIVERGENZA su {diverged}/{cases.Count} corpi (peggiore: {worstLabel}, Δ {worstAll * 1000f:F1} mm > {TolMeters * 1000f:F0} mm). " +
                           "La forma C# (TerrainLayer) e quella HLSL (PlanetHeightCore.SampleHeightD) NON combaciano: in gioco il giocatore galleggerà/sprofonderà. " +
                           "Un cambio su un lato non è stato rispecchiato sull'altro → allinea PlanetHeightCore.hlsl ai TerrainLayer C#. Dettagli col menu 'Wanderer/Test parità altezza GPU↔CPU'.");
        return okAll;
    }

    static bool TestBody(string label, Action<PlanetTerrain> configure, int samples, bool nodeGrid, bool verbose, out float worstDiff)
    {
        worstDiff = 0f;
        var go = new GameObject("__parityTemp");
        try
        {
            var terrain = go.AddComponent<PlanetTerrain>();
            configure(terrain);
            terrain.RebuildLayers();

            var baker = new GpuHeightBaker(terrain, 32);
            if (!baker.Supported) { if (verbose) Debug.LogError($"{label}: GPU baker non supportato (compute mancante?)."); return false; }

            // direzioni uniformi sulla sfera (spirale di Fibonacci: copertura regolare, niente cluster)
            var dirs = new Vector3[samples];
            float ga = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < samples; i++)
            {
                float z = 1f - 2f * (i + 0.5f) / samples;
                float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
                float a = ga * i;
                dirs[i] = new Vector3(Mathf.Cos(a) * r, z, Mathf.Sin(a) * r).normalized;
            }

            float[] gpu = baker.SampleHeightsBlocking(dirs);

            float maxDiff = 0f, sumDiff = 0f, worstH = 0f; Vector3 worstDir = Vector3.up;
            for (int i = 0; i < samples; i++)
            {
                float cpu = terrain.SampleHeight(dirs[i]);
                float d = Mathf.Abs(cpu - gpu[i]);
                sumDiff += d;
                if (d > maxDiff) { maxDiff = d; worstH = cpu; worstDir = dirs[i]; }
            }
            float avg = sumDiff / samples;
            bool okH = maxDiff <= TolMeters;
            worstDiff = maxDiff;
            if (verbose)
            {
                string colH = okH ? "green" : "red";
                Debug.Log($"<color={colH}>{label} [altezza]: max Δ = {maxDiff * 1000f:F3} mm, avg = {avg * 1000f:F4} mm "
                        + $"(su h≈{worstH:F1} m, dir≈{worstDir:F2}, {samples} campioni) → {(okH ? "OK" : "FUORI TOLLERANZA")}</color>");
            }

            bool okN = true;
            if (nodeGrid)
            {
                // PARITÀ DELLA GRIGLIA DI UN NODO: è il percorso che il quadtree usa davvero (float3→float piatto).
                okN = TestNodeGrid(label, terrain, baker, verbose, ref worstDiff);
            }
            baker.Dispose();
            return okH && okN;
        }
        catch (Exception e)
        {
            if (verbose) Debug.LogError($"{label}: parità saltata per eccezione ({e.GetType().Name}: {e.Message}).");
            return false;
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    /// <summary>Confronta la griglia estesa di un nodo campione calcolata su GPU (kernel CSNodeGrid) con
    /// quella su CPU (stessa formula del quadtree). Verifica il percorso buffer/ParamToDir, non solo la matematica.</summary>
    static bool TestNodeGrid(string label, PlanetTerrain terrain, GpuHeightBaker baker, bool verbose, ref float worstDiff)
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
        if (maxDiff > worstDiff) worstDiff = maxDiff;
        bool ok = maxDiff <= TolMeters;
        if (verbose)
        {
            string col = ok ? "green" : "red";
            Debug.Log($"<color={col}>{label} [griglia nodo]: max Δ = {maxDiff * 1000f:F3} mm ({ne}×{ne} vertici) "
                    + $"→ {(ok ? "OK" : "GRIGLIA CORROTTA")}</color>");
        }
        return ok;
    }
}

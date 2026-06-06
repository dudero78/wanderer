using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// GATE AUTOMATICO di parità altezza GPU↔CPU (#17 — "fonte unica altezza", parte di robustezza).
///
/// PROBLEMA: la forma del terreno è scritta DUE volte — in C# (TerrainLayer, usata dal walker) e in HLSL
/// (PlanetHeightCore.hlsl, usata dalla mesh GPU). Tenere le due allineate a mano è error-prone: un cambio su un
/// lato non rispecchiato sull'altro fa galleggiare/sprofondare il giocatore, e finora ce ne si accorgeva solo
/// entrando in Play e notando l'artefatto. Ha "morso tutta la sessione".
///
/// SOLUZIONE finché non c'è un vero transpiler C#→HLSL: dopo OGNI ricompila (di script O di compute), in edit mode,
/// gira una verifica leggera di parità su tutte le ricette ufficiali. Se C# e HLSL divergono, lo dice SUBITO con un
/// LogError rosso — la divergenza non aspetta più il Play. È un test: NON tocca l'algoritmo dell'altezza, quindi non
/// può rompere il gioco; al più logga. Si può spegnere dal menu (utile se rallenta l'iterazione).
/// </summary>
[InitializeOnLoad]
public static class PlanetParityGate
{
    const string PrefKey = "wanderer.parityGate.enabled";
    const string MenuPath = "Wanderer/Gate parità automatico (a ogni ricompila)";
    const int AutoSamples = 2048;   // leggero ma denso: una divergenza sistematica copre regioni ampie → si vede

    static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefKey, true);   // ON di default: la sicurezza è il punto
        set => EditorPrefs.SetBool(PrefKey, value);
    }

    static PlanetParityGate()
    {
        // afterAssemblyReload scatta dopo OGNI domain reload (ricompila script, e i transizioni Play). Il guardia
        // edit-mode + delayCall evita di girare entrando in Play (dove un dispatch interferirebbe col boot della scena).
        AssemblyReloadEvents.afterAssemblyReload += OnReload;
    }

    static void OnReload()
    {
        if (!Enabled) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;   // non durante le transizioni di Play
        EditorApplication.delayCall += RunOnce;                        // al prossimo tick: il render context è pronto
    }

    static void RunOnce()
    {
        if (!Enabled) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying) return;
        try
        {
            // leggero: niente griglia-nodo, meno campioni → costo trascurabile a ogni ricompila. Il menu fa il check completo.
            PlanetGpuParityTest.RunAll(verbose: false, samples: AutoSamples, nodeGrid: false);
        }
        catch (Exception e)
        {
            // un test non deve MAI buttare un'eccezione nell'editor: al più un warning.
            Debug.LogWarning($"[parità GPU↔CPU] gate automatico saltato ({e.GetType().Name}: {e.Message}).");
        }
    }

    [MenuItem(MenuPath)]
    static void Toggle() => Enabled = !Enabled;

    [MenuItem(MenuPath, true)]
    static bool ToggleValidate() { Menu.SetChecked(MenuPath, Enabled); return true; }
}

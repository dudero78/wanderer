using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Marca gli shader creati a RUNTIME (via Shader.Find, quindi senza riferimento d'asset) come "Always Included
/// Shaders": senza, la BUILD li SCARTA → pianeta magenta/invisibile (lezione dura nel CLAUDE.md). Gira da solo al
/// caricamento dell'editor (InitializeOnLoad) → Dario non deve aprire alcun menu. Idempotente: aggiunge solo i
/// mancanti. Riguarda SOLO gli shader usati a runtime nel gioco (non quelli dell'editor di pianeti).
/// </summary>
[InitializeOnLoad]
public static class EnsureIncludedShaders
{
    static readonly string[] Needed =
    {
        "Wanderer/PlanetSurfaceGPU",   // superficie dei pianeti IN GIOCO (percorso GPU/B1) — il più critico
        "Wanderer/PlanetBaked",        // quadtree (fallback) + proxy dei corpi nella mappa
        "Wanderer/Planet",             // fallback mesh uniforme
        "Wanderer/OrbitLine",          // orbite a schermo (O)
        "Wanderer/InvertGUI",          // mirino a inversione del colore di sfondo
        "Wanderer/AdditiveGlow",       // bagliore additivo attorno alla sonda
        "Wanderer/StarGlow",           // alone di luce attorno alle stelle
        "Unlit/Color",                 // disco della stella + scanalatura luminosa della sonda
        "Standard",                    // corpo METALLICO della sonda e dell'omino-tuta (creato a runtime via Shader.Find)
    };

    static EnsureIncludedShaders()
    {
        var so = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
        var arr = so.FindProperty("m_AlwaysIncludedShaders");
        if (arr == null) return;

        bool changed = false;
        foreach (var name in Needed)
        {
            var sh = Shader.Find(name);
            if (sh == null) continue;   // shader non ancora compilato/presente → salta (riproverà al prossimo load)

            bool present = false;
            for (int i = 0; i < arr.arraySize; i++)
                if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) { present = true; break; }
            if (present) continue;

            arr.arraySize++;
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
            changed = true;
        }

        if (changed)
        {
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log("[build] Shader del gioco aggiunti agli Always Included Shaders → la build non li scarterà.");
        }
    }
}

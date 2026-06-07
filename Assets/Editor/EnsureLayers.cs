using UnityEditor;
using UnityEngine;

/// <summary>
/// Assicura che esista il LAYER NOMINATO usato dal gioco (evita l'indice "grezzo" hardcodato). Gira al caricamento
/// dell'editor (InitializeOnLoad), idempotente: se il layer manca lo scrive nel primo user-layer libero del
/// TagManager. <see cref="AvatarLayerName"/> è il layer su cui sta il modello del GIOCATORE, nascosto alla sua camera.
/// </summary>
[InitializeOnLoad]
public static class EnsureLayers
{
    public static string AvatarLayerName => ModelHost.AvatarLayer;   // unica fonte del nome (runtime)

    // I layer nominati che il gioco si aspetta. "Sky" = la bolla del cielo stellato; "MapView" = ciò che la camera-mappa
    // renderizza. DEVONO essere DISTINTI: se "MapView" manca, MapMode cade sul fallback 9 = proprio "Sky" → la camera
    // del giocatore esclude il layer 9 → CIELO NERO (bug reale). Creiamo entrambi, in slot distinti (8/9/10).
    static readonly string[] Needed = { ModelHost.AvatarLayer, SkyController.SkyLayerName, "MapView" };

    static EnsureLayers()
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0) return;
        var tagManager = new SerializedObject(assets[0]);
        var layers = tagManager.FindProperty("layers");
        if (layers == null) return;

        bool changed = false;
        foreach (var name in Needed) changed |= EnsureOne(layers, name);
        if (changed) tagManager.ApplyModifiedProperties();
    }

    static bool EnsureOne(SerializedProperty layers, string name)
    {
        for (int i = 0; i < layers.arraySize; i++)
            if (layers.GetArrayElementAtIndex(i).stringValue == name) return false;   // già presente

        for (int i = 8; i < layers.arraySize; i++)   // user layer 8..31
        {
            var el = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(el.stringValue))
            {
                el.stringValue = name;
                Debug.Log($"[layers] aggiunto layer '{name}' allo slot {i}.");
                return true;
            }
        }
        Debug.LogWarning($"[layers] nessuno user-layer libero per '{name}'.");
        return false;
    }
}

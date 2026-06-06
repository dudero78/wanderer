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

    static EnsureLayers()
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0) return;
        var tagManager = new SerializedObject(assets[0]);
        var layers = tagManager.FindProperty("layers");
        if (layers == null) return;

        for (int i = 0; i < layers.arraySize; i++)
            if (layers.GetArrayElementAtIndex(i).stringValue == AvatarLayerName) return;   // già presente

        for (int i = 8; i < layers.arraySize; i++)   // user layer 8..31
        {
            var el = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(el.stringValue))
            {
                el.stringValue = AvatarLayerName;
                tagManager.ApplyModifiedProperties();
                Debug.Log($"[layers] aggiunto layer '{AvatarLayerName}' allo slot {i}.");
                return;
            }
        }
        Debug.LogWarning($"[layers] nessuno user-layer libero per '{AvatarLayerName}': il modello del giocatore userà il fallback.");
    }
}

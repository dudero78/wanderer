using UnityEngine;

/// <summary>
/// Modello da PREFAB AUTORATO (mesh di un artista): l'altra implementazione di <see cref="CharacterModel"/>. Quando
/// avrai i modelli veri, crei un asset (menù "Wanderer/Modello da prefab"), gli assegni il prefab e lo metti
/// nell'host al posto del procedurale — niente cambia nel resto del gioco. Lo spawn/scambio resta identico.
/// </summary>
[CreateAssetMenu(menuName = "Wanderer/Modello da prefab", fileName = "PrefabModel")]
public class PrefabCharacterModel : CharacterModel
{
    public GameObject prefab;
    public Vector3 localPosition;
    public Vector3 localEuler;
    public float scale = 1f;

    public override void Build(Transform parent)
    {
        if (prefab == null) { Debug.LogWarning($"{name}: prefab non assegnato."); return; }
        var go = Instantiate(prefab, parent);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.Euler(localEuler);
        go.transform.localScale = Vector3.one * scale;
    }
}

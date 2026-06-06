using UnityEngine;

/// <summary>
/// DEFINIZIONE DI UN MODELLO 3D di un personaggio/oggetto, come DATO (ScriptableObject → può essere un asset
/// AUTORATO nel progetto, o creato a runtime). È l'astrazione che rende i modelli INTERCAMBIABILI a runtime:
/// chi mostra un modello (vedi <see cref="ModelHost"/>) non sa COM'È fatto — chiede solo "costruisciti qui".
///
/// Implementazioni: <see cref="ProceduralOminoModel"/> (omino generato da codice) e <see cref="PrefabCharacterModel"/>
/// (prefab autorato da un artista). Domani basta assegnare un modello diverso all'host e si scambia, senza toccare
/// il resto (giocatore, tuta, ecc.). I pezzi costruiti restano figli dell'host → animabili/estendibili (es. una
/// torretta-camera sulla sonda che segue lo sguardo).
/// </summary>
public abstract class CharacterModel : ScriptableObject
{
    /// <summary>Costruisce il modello come FIGLI di <paramref name="parent"/> (in coordinate locali). L'host pensa a
    /// svuotare i figli vecchi prima di chiamarlo, così la sostituzione a runtime è pulita.</summary>
    public abstract void Build(Transform parent);
}

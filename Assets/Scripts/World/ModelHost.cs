using UnityEngine;

/// <summary>
/// Contenitore di un <see cref="CharacterModel"/> in scena: lo costruisce come propri figli e lo può SOSTITUIRE a
/// runtime (<see cref="SetModel"/>) in modo pulito (distrugge il vecchio, costruisce il nuovo). È il punto unico
/// dove i modelli diventano INTERCAMBIABILI: giocatore, tuta (e in futuro qualunque cosa) montano un ModelHost e
/// gli assegnano il modello che vogliono — procedurale oggi, prefab autorato domani, senza altre modifiche.
///
/// <see cref="hideLayer"/> ≥ 0 mette il modello (e i suoi pezzi) su quel layer: serve a NASCONDERE l'avatar del
/// giocatore alla SUA camera (prima persona pulita) lasciandolo visibile alle altre (sonda). −1 = nessun vincolo.
/// </summary>
public class ModelHost : MonoBehaviour
{
    /// <summary>Nome del layer su cui sta il modello del GIOCATORE (nascosto alla sua camera). Definito qui (runtime)
    /// così sia il bootstrap sia l'editor (EnsureLayers) lo riferiscono dallo stesso posto, senza indici grezzi.</summary>
    public const string AvatarLayer = "PlayerAvatar";

    [SerializeField] CharacterModel model;
    [SerializeField] int hideLayer = -1;

    public CharacterModel Model => model;
    public int HideLayer { get => hideLayer; set => hideLayer = value; }

    // Se il modello è ASSEGNATO da editor e non ci sono ancora figli, costruiscilo all'avvio. Il percorso da CODICE
    // chiama SetModel esplicitamente (così l'ordine di costruzione è controllato).
    void Start() { if (model != null && transform.childCount == 0) Rebuild(); }

    /// <summary>Sostituisce il modello a RUNTIME: distrugge il precedente, costruisce il nuovo, applica il layer.</summary>
    public void SetModel(CharacterModel m) { model = m; Rebuild(); }

    void Rebuild()
    {
        for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
        if (model == null) return;
        model.Build(transform);
        if (hideLayer >= 0) SetLayer(transform, hideLayer);
    }

    static void SetLayer(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
    }
}

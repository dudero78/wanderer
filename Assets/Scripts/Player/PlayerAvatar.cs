using UnityEngine;

/// <summary>
/// Glue del MODELLO del giocatore: tiene i due modelli (NUDO e IN TUTA) e li scambia sull'<see cref="ModelHost"/>
/// alla raccolta della tuta. Tutta la logica di costruzione/scambio sta nel sistema generico (ModelHost +
/// CharacterModel): qui c'è solo "quale modello quando". Espandibile (altri stati/equipaggiamenti = altri modelli).
/// </summary>
public class PlayerAvatar : MonoBehaviour
{
    ModelHost host;
    CharacterModel naked, suited;

    public void Init(ModelHost host, CharacterModel naked, CharacterModel suited)
    {
        this.host = host; this.naked = naked; this.suited = suited;
        host.SetModel(naked);
    }

    /// <summary>Raccolta la tuta: il giocatore "la indossa" → modello con casco/zaino (stesso COLORE del giocatore).</summary>
    public void OnSuitEquipped() { if (host != null && suited != null) host.SetModel(suited); }
}

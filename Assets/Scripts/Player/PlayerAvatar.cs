using UnityEngine;

/// <summary>
/// MODELLO 3D del GIOCATORE (omino), figlio del Player. Prima della tuta: testa-sfera luminosa, un filo più magro,
/// del COLORE del giocatore. Raccolta la tuta: stesso modello ma con CASCO (resta del colore del giocatore).
/// Sta su un LAYER nascosto alla camera del giocatore (prima persona pulita) ma visibile alle altre (sonda, ecc.).
/// </summary>
public class PlayerAvatar : MonoBehaviour
{
    public const int HideLayer = 9;   // layer dell'avatar: escluso dalla culling mask della camera del giocatore

    Color bodyColor;
    static readonly Color Accent = new Color(0.4f, 0.95f, 1f);

    public void Init(Color color)
    {
        bodyColor = color;
        // NUDO: magro, arti sottili, testa-sfera luminosa, NIENTE bombole (non può ancora volare).
        Rebuild(new OminoBuilder.Style
        {
            bodyColor = bodyColor, metallic = false, head = OminoBuilder.HeadKind.GlowSphere,
            accent = Accent, thin = 0.80f, limbBulk = 0.78f, tanks = false,
        });
    }

    /// <summary>Raccolta la tuta: il modello "indossa la tuta" → CASCO, corpo pieno, arti CICCIOTTI e ZAINO-bombole,
    /// MA resta del COLORE del giocatore (non diventa metallico).</summary>
    public void OnSuitEquipped() => Rebuild(new OminoBuilder.Style
    {
        bodyColor = bodyColor, metallic = false, head = OminoBuilder.HeadKind.Helmet,
        accent = Accent, thin = 1.0f, limbBulk = 1.30f, tanks = true,
    });

    void Rebuild(OminoBuilder.Style style)
    {
        for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
        OminoBuilder.Build(transform, style);
        SetLayer(transform, HideLayer);
    }

    static void SetLayer(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
    }
}

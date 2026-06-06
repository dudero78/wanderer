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
        Rebuild(OminoBuilder.HeadKind.GlowSphere, 0.85f);   // testa nuda luminosa, più magro
    }

    /// <summary>Raccolta la tuta: il modello "indossa la tuta" → casco e proporzioni piene, MA resta del colore del
    /// giocatore (non diventa metallico).</summary>
    public void OnSuitEquipped() => Rebuild(OminoBuilder.HeadKind.Helmet, 1.0f);

    void Rebuild(OminoBuilder.HeadKind head, float thin)
    {
        for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
        OminoBuilder.Build(transform, new OminoBuilder.Style
        {
            bodyColor = bodyColor, metallic = false, head = head, accent = Accent, thin = thin
        });
        SetLayer(transform, HideLayer);
    }

    static void SetLayer(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
    }
}

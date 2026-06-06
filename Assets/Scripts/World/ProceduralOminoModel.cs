using UnityEngine;

/// <summary>
/// Modello "omino" GENERATO da codice (vedi <see cref="OminoBuilder"/>): è una implementazione di
/// <see cref="CharacterModel"/>. Tutti i parametri della forma sono campi SERIALIZZATI → si possono creare ASSET
/// autorati (menù "Wanderer/Modello omino procedurale") e tararli dall'Inspector senza ricompilare, oppure creare
/// istanze a runtime con <see cref="Create"/>. Quando avrai modelli di artista, usa <see cref="PrefabCharacterModel"/>
/// al posto di questo: l'host non cambia.
/// </summary>
[CreateAssetMenu(menuName = "Wanderer/Modello omino procedurale", fileName = "OminoModel")]
public class ProceduralOminoModel : CharacterModel
{
    public Color bodyColor = new Color(0.55f, 0.58f, 0.62f);
    public bool metallic = true;
    public OminoBuilder.HeadKind head = OminoBuilder.HeadKind.Helmet;
    public Color accent = new Color(0.4f, 0.95f, 1f);
    [Range(0.4f, 1.5f)] public float thin = 1f;        // raggio del corpo
    [Range(0.4f, 1.8f)] public float limbBulk = 1f;    // spessore arti (assoluto)
    public bool tanks = true;                          // zaino-bombole (solo chi vola)

    public override void Build(Transform parent) => OminoBuilder.Build(parent, new OminoBuilder.Style
    {
        bodyColor = bodyColor, metallic = metallic, head = head, accent = accent,
        thin = thin, limbBulk = limbBulk, tanks = tanks,
    });

    /// <summary>Crea un'istanza a RUNTIME (finché non è un asset autorato). Usata dal bootstrap per giocatore/tuta.</summary>
    public static ProceduralOminoModel Create(Color bodyColor, bool metallic, OminoBuilder.HeadKind head,
                                              Color accent, float thin, float limbBulk, bool tanks)
    {
        var m = CreateInstance<ProceduralOminoModel>();
        m.bodyColor = bodyColor; m.metallic = metallic; m.head = head; m.accent = accent;
        m.thin = thin; m.limbBulk = limbBulk; m.tanks = tanks;
        return m;
    }
}

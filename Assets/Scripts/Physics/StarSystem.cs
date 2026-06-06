using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Un SISTEMA STELLARE: contenitore-dato di una stella + i suoi corpi, con la sua posizione nello spazio-galassia.
/// È la Tappa 1 del layer multi-sistema (#16, vedi STARSYSTEM_DESIGN.md): con UN solo sistema il comportamento è
/// identico a prima — `SystemOrigin = Vector3d.Zero` è il caso degenere, e la posizione della stella si propaga già
/// giù per la catena dei genitori (pianeti = stella + orbita, lune = pianeta + orbita), quindi non serve toccare la
/// matematica delle coordinate.
///
/// NON è un MonoBehaviour: è un oggetto-dato che <see cref="SolarSystem"/> possiede. Lo scoping (sonno/risveglio,
/// transizione interstellare) si scrive SOLO quando esisterà davvero un secondo sistema da accendere — prima quel
/// codice non sarebbe nemmeno collaudabile (il ramo "dormiente" non gira mai). Principio del progetto: "astrai il
/// nodo-sistema solo quando serve".
/// </summary>
public class StarSystem
{
    public string Name;
    public Vector3d SystemOrigin = Vector3d.Zero;   // posizione della stella nello spazio-galassia (double). Oggi Zero.
    public CelestialBody Star;
    // i corpi del sistema. A N=1 è la STESSA istanza di SolarSystem.Bodies (riferimento condiviso, NON una copia):
    // Register continua a popolare quell'unica lista e nessun consumatore può vederne una versione divergente.
    public List<CelestialBody> Bodies;
    public bool Active;                             // un solo sistema Active per volta (con N=1 sempre true)
    public bool Waking;                             // risveglio in corso (build su più frame): evita di ri-triggerare la sveglia o di addormentarlo a metà

    // --- Tappa 3+: dati per costruire/distruggere il sistema su richiesta (sleep/wake interstellare) ---
    // Recipe: la composizione del sistema (stella + corpi) come DATO → BuildSystem la sa costruire (Tappa 4). I sistemi
    // DORMIENTI esistono solo come (Name + SystemOrigin + Recipe + colore stella per la mappa), zero corpi/fette/BodyId.
    public SolarSystemSetup.SystemRecipe Recipe;
    public Color StarColor = new Color(1f, 0.88f, 0.55f);   // per il billboard nella mappa galattica (Tappa 5)
    public float StarRadius = 2000f;
    // GameObject creati quando il sistema è ATTIVO (stella + corpi): si distruggono alla retrocessione (Tappa 4).
    // null quando dormiente. La stella vive qui (NON è in Bodies del SolarSystem se dormiente).
    public System.Collections.Generic.List<GameObject> SceneObjects;
    public Transform StarTransform;                 // per SunLight.Retarget alla promozione
    public double WakeRadius = 400000;              // distanza-galassia entro cui il sistema si SVEGLIA (isteresi ×1.4 per dormire)
}

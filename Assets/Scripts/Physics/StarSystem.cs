using System.Collections.Generic;

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
}

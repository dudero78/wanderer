using UnityEngine;

/// <summary>
/// Orienta la luce direzionale in modo che punti dalla stella verso il pianeta.
/// Mentre il pianeta orbita, la direzione del sole cambia: dal suolo vedi il
/// sole sorgere e tramontare. È il ciclo giorno/notte che emerge dall'orbita,
/// non da un'animazione finta.
/// </summary>
public class SunLight : MonoBehaviour
{
    /// <summary>Riferimento deterministico al sole, per chi deve illuminare a mano (es. GpuPlanetRenderer):
    /// FindAnyObjectByType non è affidabile con più luci in scena. Impostato in Awake, prima di ogni Update.</summary>
    public static SunLight Instance { get; private set; }

    public Transform star;
    public Transform planet;
    // posizioni VERE (double): la direzione della luce si calcola da qui, NON dai transform. Il transform della stella è
    // spostato da StarRenderClamp vicino alla camera (per tenerla visibile oltre il far-clip) → usarlo illuminava i corpi
    // ~dalla direzione della camera invece che dal sole vero, e il disco visibile divergeva dall'illuminazione.
    CelestialBody starBody, planetBody;

    /// <summary>Ri-punta la direzionale a una NUOVA stella/pianeta (cambio di sistema stellare). Resta singleton perché
    /// la stella che ti illumina è sempre UNA.</summary>
    public void Retarget(Transform newStar, Transform newPlanet)
    {
        star = newStar; planet = newPlanet;
        starBody = newStar != null ? newStar.GetComponent<CelestialBody>() : null;
        planetBody = newPlanet != null ? newPlanet.GetComponent<CelestialBody>() : null;
    }

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }   // dopo un domain reload evita un Instance stale → RefreshLighting cadrebbe su luce di default

    void LateUpdate()
    {
        if (!star || !planet) return;
        // direzione dalle POSIZIONI VERE (immune al clamp del transform della stella); fallback ai transform se mancano i corpi
        Vector3 dir = (starBody != null && planetBody != null)
            ? (planetBody.UniversePosition - starBody.UniversePosition).ToVector3()
            : planet.position - star.position;
        if (dir.sqrMagnitude > 1e-6f)
            transform.rotation = Quaternion.LookRotation(dir.normalized);
    }
}

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

    void Awake() { Instance = this; }

    void LateUpdate()
    {
        if (!star || !planet) return;
        Vector3 dir = planet.position - star.position;
        if (dir.sqrMagnitude > 1e-6f)
            transform.rotation = Quaternion.LookRotation(dir.normalized);
    }
}

using UnityEngine;

/// <summary>
/// Tiene una STELLA sempre VISIBILE oltre il far-clip della camera (prima spariva di colpo allontanandosi): se è più
/// lontana di <see cref="clampDist"/>, ne riposiziona il transform a quella distanza lungo la stessa direzione e ne
/// riduce la scala in proporzione → dimensione APPARENTE invariata, ma sempre dentro il frustum (come una stella di
/// sfondo). Tocca SOLO la resa: gravità/fisica usano <see cref="CelestialBody.UniversePosition"/> (double), non il transform.
/// </summary>
public class StarRenderClamp : MonoBehaviour
{
    public CelestialBody body;
    public float clampDist = 150000f;   // entro il far-clip del giocatore (300 km): oltre, la stella viene "avvicinata" otticamente

    float baseScale;

    void Start() => baseScale = transform.localScale.x;

    void LateUpdate()
    {
        if (body == null) return;
        Vector3 truePos = (body.UniversePosition - FloatingOrigin.SceneOrigin).ToVector3();

        var main = Camera.main;
        // in vista sonda (camera giocatore spenta) lascio la stella alla posizione vera: il clamp è tarato sulla camera
        // del giocatore e da un'altra camera sarebbe fuori posto.
        if (main == null || !main.isActiveAndEnabled)
        {
            transform.position = truePos;
            transform.localScale = Vector3.one * baseScale;
            return;
        }

        Vector3 toStar = truePos - main.transform.position;
        float dist = toStar.magnitude;
        if (dist <= clampDist)
        {
            transform.position = truePos;
            transform.localScale = Vector3.one * baseScale;
        }
        else
        {
            transform.position = main.transform.position + toStar / dist * clampDist;
            transform.localScale = Vector3.one * (baseScale * clampDist / dist);   // dimensione apparente invariata
        }
    }
}

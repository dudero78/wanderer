using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Le stelle dei sistemi DISTANTI come PUNTI sempre visibili nel cielo di gioco (come le stelle vere): finché un sistema
/// dorme non esiste come corpo in scena, quindi senza questo non vedresti la sua stella mentre ci viaggi. Per ogni
/// sistema dormiente disegna un dischetto unlit del colore della stella, "avvicinato" otticamente entro il far-clip
/// (stessa tecnica di <see cref="StarRenderClamp"/>: direzione vera, distanza clampata, taglia apparente costante).
///
/// Quando il sistema si SVEGLIA (o è in risveglio), la sua stella VERA entra in scena → il punto si nasconde, niente
/// doppione. Tocca SOLO la resa; la fisica usa le posizioni-universo.
/// </summary>
public class DistantStars : MonoBehaviour
{
    public float clampDist = 150000f;   // entro il far-clip del giocatore (300 km): oltre, la stella si "avvicina" otticamente

    sealed class Point { public StarSystem sys; public GameObject go; public float baseScale; }
    readonly List<Point> points = new List<Point>();

    public void Init(SolarSystem solar)
    {
        if (solar == null || solar.Systems == null) return;
        var unlit = Shader.Find("Unlit/Color");
        foreach (var sys in solar.Systems)
        {
            if (sys == null || sys.SystemOrigin.sqrMagnitude < 1.0) continue;   // salta la casa (stella vera residente)
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "DistantStar_" + sys.Name;
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
            if (unlit != null) go.GetComponent<Renderer>().material = new Material(unlit) { color = sys.StarColor };
            go.transform.SetParent(transform, false);
            go.SetActive(false);
            points.Add(new Point { sys = sys, go = go, baseScale = Mathf.Max(1f, sys.StarRadius) * 2f });
        }
    }

    void LateUpdate()
    {
        var main = Camera.main;
        // tarato sulla camera del giocatore; in mappa / vista sonda lascia perdere (la mappa ha i suoi billboard)
        bool ok = !MapMode.IsOpen && main != null && main.isActiveAndEnabled;
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            // svegliato o in risveglio → la stella VERA è (o sta per essere) in scena: nascondi il punto, niente doppione
            if (!ok || p.sys.Active || p.sys.Waking) { if (p.go.activeSelf) p.go.SetActive(false); continue; }

            Vector3 truePos = (p.sys.SystemOrigin - FloatingOrigin.SceneOrigin).ToVector3();
            Vector3 toStar = truePos - main.transform.position;
            float dist = toStar.magnitude;
            if (!p.go.activeSelf) p.go.SetActive(true);
            if (dist <= clampDist)
            {
                p.go.transform.position = truePos;
                p.go.transform.localScale = Vector3.one * p.baseScale;
            }
            else
            {
                p.go.transform.position = main.transform.position + toStar / dist * clampDist;
                p.go.transform.localScale = Vector3.one * Mathf.Max(p.baseScale * clampDist / dist, clampDist * 0.01f);
            }
        }
    }
}

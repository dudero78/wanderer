using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Le stelle dei sistemi DISTANTI come PUNTI sempre visibili nel cielo di gioco (le tue mete: Vega, Antares...). Finché
/// un sistema dorme non esiste come corpo in scena, quindi senza questo non vedresti la sua stella. Resa come un
/// BILLBOARD additivo morbido (shader <c>Wanderer/StarGlow</c>) — NON una sfera opaca dura — così si fonde col resto
/// del cielo (le stelle del catalogo sono anch'esse additive morbide). "Avvicinata" otticamente entro il far-clip
/// (come <see cref="StarRenderClamp"/>: direzione vera, distanza clampata, taglia apparente ~costante).
///
/// Quando il sistema si SVEGLIA (o è in risveglio) la sua stella VERA entra in scena → il punto si nasconde (niente
/// doppione). Tocca SOLO la resa; la fisica usa le posizioni-universo.
/// </summary>
public class DistantStars : MonoBehaviour
{
    public float clampDist = 150000f;

    sealed class Point { public StarSystem sys; public GameObject go; public Transform t; public float baseScale; }
    readonly List<Point> points = new List<Point>();

    public void Init(SolarSystem solar)
    {
        if (solar == null || solar.Systems == null) return;
        var glow = Shader.Find("Wanderer/StarGlow");
        foreach (var sys in solar.Systems)
        {
            if (sys == null || sys.SystemOrigin.sqrMagnitude < 1.0) continue;   // salta la casa (stella vera residente)
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "DistantStar_" + sys.Name;
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
            var mr = go.GetComponent<MeshRenderer>();
            if (glow != null)
            {
                var m = new Material(glow);
                m.SetColor("_Color", sys.StarColor);
                m.SetFloat("_Strength", 1.4f);   // additivo morbido: centro brillante + alone (come una stella vera)
                m.SetFloat("_ZTest", 4f);        // LEqual: il terreno la occlude (non brilla attraverso il pianeta)
                mr.sharedMaterial = m;
            }
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            go.transform.SetParent(transform, false);
            go.SetActive(false);
            // glow morbido: un po' più grande della vecchia sfera, la caduta (1−r)³ concentra il centro
            points.Add(new Point { sys = sys, go = go, t = go.transform, baseScale = Mathf.Max(1f, sys.StarRadius) * 3f });
        }
    }

    void LateUpdate()
    {
        var main = Camera.main;
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

            Vector3 pos; float scale;
            if (dist <= clampDist) { pos = truePos; scale = p.baseScale; }
            else { pos = main.transform.position + toStar / dist * clampDist; scale = Mathf.Max(p.baseScale * clampDist / dist, clampDist * 0.012f); }

            p.t.position = pos;
            p.t.localScale = Vector3.one * scale;
            p.t.rotation = Quaternion.LookRotation(pos - main.transform.position, main.transform.up);   // billboard verso la camera
        }
    }
}

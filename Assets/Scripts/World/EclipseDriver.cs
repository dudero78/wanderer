using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ombre di ECLISSI analitiche: quando un corpo passa fra il sole e un altro, gli proietta un'ombra reale
/// sulla superficie. Il calcolo vero sta nello shader (Wanderer/PlanetBaked: raggio-sfera in spazio oggetto):
/// niente shadow map → ZERO acne sul terreno a luce radente e nessun limite di shadow distance (era la ragione
/// per cui le ombre proiettate erano spente). Qui, per frame, per ogni corpo roccioso si sceglie l'OCCLUSORE
/// più probabile — l'altro corpo più allineato col sole — e si passano gli uniform ai suoi materiali bakeati.
///
/// Gli uniform sono in SPAZIO OGGETTO del corpo, condiviso dalla mesh in gioco E dal proxy della mappa (stessa
/// geometria, centro all'origine) → l'eclissi appare correttamente in entrambi senza lavoro extra.
/// </summary>
public class EclipseDriver : MonoBehaviour
{
    struct Rocky { public CelestialBody body; public PlanetTerrain terrain; public GpuPlanetRenderer gpu; }
    readonly List<Rocky> rocky = new List<Rocky>();
    Transform sun;
    CelestialBody star;
    SolarSystem solarRef;
    // CADENZA: l'ombra di eclissi si muove a velocità ORBITALE (lentissima) → aggiornare gli uniform a ~10 Hz invece
    // di 60 è visivamente identico e taglia ~6× il costo O(n²) di questo LateUpdate. Parte > intervallo → primo frame subito.
    float eclTimer = 1f;
    const float EclInterval = 0.1f;

    static readonly int OccPos = Shader.PropertyToID("_EclipseOccluderPos");
    static readonly int OccRad = Shader.PropertyToID("_EclipseOccluderRadius");
    static readonly int SunDir = Shader.PropertyToID("_EclipseSunDir");
    static readonly int SunAng = Shader.PropertyToID("_EclipseSunAngular");

    public void Init(SolarSystem solar, Transform sunLight)
    {
        sun = sunLight;
        solarRef = solar;
        Rebuild();
    }

    /// <summary>Ri-scansiona i corpi rocciosi e la stella (per il futuro cambio di sistema: i corpi attivi cambiano).
    /// Oggi chiamata una volta dal bootstrap; l'effetto è identico a prima.</summary>
    public void Rebuild()
    {
        rocky.Clear();
        star = null;
        if (solarRef == null) return;
        foreach (var b in solarRef.Bodies)
        {
            if (b == null) continue;
            if (b.Orbit == null) star = b;   // la stella: corpo senza orbita (sorgente di luce)
            var t = b.GetComponent<PlanetTerrain>();
            if (t != null && t.Recipe != null && t.FaceMaterials != null)
                rocky.Add(new Rocky { body = b, terrain = t, gpu = b.GetComponent<GpuPlanetRenderer>() });
        }
    }

    void LateUpdate()
    {
        if (sun == null) return;
        eclTimer += Time.deltaTime;
        if (eclTimer < EclInterval) return;   // ~10 Hz: gli uniform restano sull'ultimo valore fra un update e l'altro
        eclTimer = 0f;
        Vector3 toSun = -sun.forward;   // verso il sole = stessa direzione della luce direzionale (Unity)

        for (int i = 0; i < rocky.Count; i++)
        {
            var B = rocky[i];

            // occlusore = fra gli altri corpi, quello più allineato col sole (davanti a B, verso il sole).
            // Anche se l'allineamento è scarso lo shader calcola ombra 0: qui basta passare il candidato giusto.
            CelestialBody occ = null;
            float best = 0f;
            for (int j = 0; j < rocky.Count; j++)
            {
                if (j == i) continue;
                Vector3 v = rocky[j].body.transform.position - B.body.transform.position;
                float d = v.magnitude;
                if (d < 1e-3f) continue;
                float align = Vector3.Dot(v / d, toSun);
                if (align > best) { best = align; occ = rocky[j].body; }
            }

            Vector3 occObj = Vector3.zero;
            float radius = 0f;
            Vector3 sunObj = B.body.transform.InverseTransformDirection(toSun).normalized;
            float scale = B.body.transform.lossyScale.x; if (scale < 1e-4f) scale = 1f;
            if (occ != null)
            {
                occObj = B.body.transform.InverseTransformPoint(occ.transform.position);
                radius = (float)occ.Radius / scale;
            }

            // raggio angolare del sole visto da B: raggio stella / distanza → governa penombra e durata umbra
            float sunAng = 0.033f;
            if (star != null)
            {
                float dStar = Vector3.Distance(B.body.transform.position, star.transform.position) / scale;
                if (dStar > 1e-3f) sunAng = (float)star.Radius / scale / dStar;
            }

            var mats = B.terrain.FaceMaterials;
            for (int f = 0; f < mats.Length; f++)
            {
                var m = mats[f];
                if (m == null) continue;
                m.SetVector(OccPos, occObj);
                m.SetFloat(OccRad, radius);
                m.SetVector(SunDir, sunObj);
                m.SetFloat(SunAng, sunAng);
            }
            // stesso set sul renderer GPU autoritativo (non solo i materiali bakeati del fallback/proxy)
            if (B.gpu != null) B.gpu.SetEclipse(occObj, radius, sunObj, sunAng);
        }
    }
}

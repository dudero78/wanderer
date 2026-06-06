using UnityEngine;

/// <summary>
/// ILLUMINAZIONE della scena, isolata (come SolarSystemSetup / PlayerSpawn): luce direzionale del sole +
/// ombre di ECLISSI analitiche fra corpi + luce ambiente. Niente shadow map (a luce radente danno acne sul
/// terreno e "schiarimento" oltre la shadow distance): il rilievo emerge dalle normali analitiche, e le ombre
/// geometriche (eclissi) si calcolano nello shader. I componenti che devono vivere oltre il bootstrap
/// (EclipseDriver) vanno sull'host passato.
/// </summary>
public static class LightingSetup
{
    public static void Setup(GameObject host, SolarSystem solar, Transform star, Transform planet)
    {
        // --- Luce stellare (direzionale) ---
        var lightGo = new GameObject("SunLight");
        var dl = lightGo.AddComponent<Light>();
        dl.type = LightType.Directional;
        dl.intensity = 2.0f;
        dl.color = new Color(1f, 0.96f, 0.9f);
        dl.shadows = LightShadows.None;   // niente shadow map: acne a luce radente + schiarimento oltre la distanza
        var sun = lightGo.AddComponent<SunLight>();
        sun.Retarget(star, planet);

        // Ombre di ECLISSI (analitiche, nello shader): un corpo fra il sole e un altro lo oscura. Niente shadow map.
        var eclipse = host.AddComponent<EclipseDriver>();
        eclipse.Init(solar, lightGo.transform);

        // notte quasi nera: il terminatore (giorno/notte) diventa netto, look lunare. Con l'atmosfera, più avanti,
        // sarà lo scattering a rialzare la luce sul lato in ombra.
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.05f, 0.054f, 0.065f);
    }
}

using UnityEngine;

/// <summary>
/// INTERFACCIA della scena, isolata (come SolarSystemSetup / PlayerSpawn): mappa (M), indicatore di rotta,
/// orbite a schermo (O), HUD di debug, schermata impostazioni (à). Tutti componenti sull'host (il GameObject del
/// bootstrap). Prende i riferimenti dal rig del giocatore e dal sistema solare già costruiti.
/// </summary>
public static class UiSetup
{
    public static void Setup(GameObject host, SolarSystem solar, PlayerSpawn.Built rig, SolarSystemSetup.Built sys)
    {
        // Mappa (M): zoom-out sul sistema, orbite disegnate, click per selezionare un corpo destinazione.
        var map = host.AddComponent<MapMode>();
        map.Init(rig.Cam, rig.Walker, solar);

        // Indicatore di rotta: reticolo stile Outer Wilds sul corpo selezionato (bussola del viaggio).
        var route = host.AddComponent<RouteIndicator>();
        route.Init(rig.Cam, rig.Walker, solar, map);

        // Orbite a schermo (O): linee delle orbite del sistema, anche in volo.
        var orbitDisplay = host.AddComponent<OrbitDisplay>();
        orbitDisplay.Init(solar);

        // HUD di debug (FPS+picco/sec con toggle "è", floating origin, volo, tuta, rotta).
        var hud = host.AddComponent<DebugHud>();
        hud.Init(rig.PlayerGo.transform, sys.HomePlanet, sys.Star, solar, rig.Walker, rig.Flashlight,
                 rig.SuitTransform, rig.CamTransform);

        // Schermata impostazioni (à): facilitazioni opzionali (es. autopilota stazionario), tarature live.
        var settings = host.AddComponent<SettingsMenu>();
        settings.Init(rig.Walker, rig.Cam);

        // Menu di PAUSA (ESC): Riprendi/Opzioni/Comandi/Esci. Disattivabile da debug (PauseMenu.Enabled).
        var pause = host.AddComponent<PauseMenu>();
        pause.Init(rig.Walker, settings, map);

        // Effetto "velocità della luce": righe radiali quando vai fortissimo (overlay, zero impatto fisica).
        var speedLines = host.AddComponent<SpeedLines>();
        speedLines.walker = rig.Walker; speedLines.cam = rig.Cam;

        // Sonda alla Outer Wilds (P lancia · V guarda attraverso · K richiama · G foto): oggetto fisico veloce con
        // gravità sommata + collisione analitica, registrato in Loose + ExtraViewpoints (il renderer le dà dettaglio).
        var probe = host.AddComponent<ProbeController>();
        probe.Init(rig.Cam, rig.CamTransform, rig.Walker, solar);
        route.ProbeTarget = probe.Probe;   // il reticolo segue la sonda con un triangolo ambra (tracker HUD)
    }
}

using UnityEngine;

/// <summary>
/// Impostazioni di gioco regolabili a runtime dalla schermata opzioni (tasto à). Statiche: i sistemi
/// (es. l'autopilota nel PlanetWalker) le leggono senza doverle cablare. Persistono tra le sessioni via
/// PlayerPrefs. I default rappresentano l'esperienza "base"; le facilitazioni sono OFF finché non le accendi.
/// </summary>
public static class GameSettings
{
    // Autopilota: a fine viaggio TIENE la stazione (hover contro gravità) finché non dai un comando, invece
    // di mollarti a distanza di sicurezza. È una FACILITAZIONE → di default OFF (arrivi e manovri da te).
    public static bool AutopilotStationKeeping;

    const string KStation = "wanderer.autopilot.stationKeeping";

    public static void Load()
    {
        AutopilotStationKeeping = PlayerPrefs.GetInt(KStation, 0) != 0;   // default 0 = OFF
    }

    public static void Save()
    {
        PlayerPrefs.SetInt(KStation, AutopilotStationKeeping ? 1 : 0);
        PlayerPrefs.Save();
    }
}

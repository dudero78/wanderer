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

    // Interrompendo l'autopilota (T) la nave FRENA da sola fino a fermarsi, invece di restare alla deriva.
    // È il comportamento desiderato → di default ON (lo si può spegnere per il drift newtoniano puro).
    public static bool AutopilotSoftStop;

    // GRAFICA — risoluzione della texture della Via Lattea: 0 = 4k, 1 = 8k, 2 = 16k. Default 16k (la più bella);
    // chi ha una macchina meno potente può scendere (meno VRAM). MilkyWayBand carica la variante corrispondente.
    public static int SkyTextureRes = 2;

    const string KStation = "wanderer.autopilot.stationKeeping";
    const string KSoftStop = "wanderer.autopilot.softStop";
    const string KSkyRes = "wanderer.graphics.skyTextureRes";

    public static void Load()
    {
        AutopilotStationKeeping = PlayerPrefs.GetInt(KStation, 0) != 0;   // default 0 = OFF
        AutopilotSoftStop = PlayerPrefs.GetInt(KSoftStop, 1) != 0;        // default 1 = ON
        SkyTextureRes = Mathf.Clamp(PlayerPrefs.GetInt(KSkyRes, 2), 0, 2);
    }

    public static void Save()
    {
        PlayerPrefs.SetInt(KStation, AutopilotStationKeeping ? 1 : 0);
        PlayerPrefs.SetInt(KSoftStop, AutopilotSoftStop ? 1 : 0);
        PlayerPrefs.SetInt(KSkyRes, SkyTextureRes);
        PlayerPrefs.Save();
    }
}

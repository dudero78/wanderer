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

    // FOTO — tieni l'HUD (mirino, scritte d'aiuto, etichette dello strumento) nelle foto. Di default OFF → foto PULITE
    // (l'HUD si nasconde sul frame dello scatto). Accendilo se vuoi catturare anche l'interfaccia.
    public static bool HudInPhotos;

    const string KStation = "wanderer.autopilot.stationKeeping";
    const string KSoftStop = "wanderer.autopilot.softStop";
    const string KSkyRes = "wanderer.graphics.skyTextureRes";
    const string KHudPhotos = "wanderer.photo.hudInPhotos";

    public static void Load()
    {
        AutopilotStationKeeping = PlayerPrefs.GetInt(KStation, 0) != 0;   // default 0 = OFF
        AutopilotSoftStop = PlayerPrefs.GetInt(KSoftStop, 1) != 0;        // default 1 = ON
        SkyTextureRes = Mathf.Clamp(PlayerPrefs.GetInt(KSkyRes, 2), 0, 2);
        HudInPhotos = PlayerPrefs.GetInt(KHudPhotos, 0) != 0;             // default 0 = OFF (foto pulite)
    }

    public static void Save()
    {
        PlayerPrefs.SetInt(KStation, AutopilotStationKeeping ? 1 : 0);
        PlayerPrefs.SetInt(KSoftStop, AutopilotSoftStop ? 1 : 0);
        PlayerPrefs.SetInt(KSkyRes, SkyTextureRes);
        PlayerPrefs.SetInt(KHudPhotos, HudInPhotos ? 1 : 0);
        PlayerPrefs.Save();
    }

    // Soppressione dell'HUD per uno scatto (runtime, non persistito): chi scatta chiama SuppressHudForPhoto() nel frame
    // della cattura; gli OnGUI dell'HUD controllano HudHiddenForPhoto e saltano il disegno SOLO in quel frame (così
    // l'immagine catturata a fine frame è pulita), a meno che HudInPhotos sia ON.
    static int hudSuppressFrame = -1;
    public static bool HudHiddenForPhoto => !HudInPhotos && Time.frameCount == hudSuppressFrame;
    public static void SuppressHudForPhoto() => hudSuppressFrame = Time.frameCount;
}

using UnityEngine;

/// <summary>
/// Regìa del CIELO. Crea una "bolla cielo" (un GameObject a parte, NON figlio del bootstrap) che ogni frame si
/// ricentra sulla camera attiva mantenendo orientamento FISSO (frame equatoriale): così le stelle sono di fatto
/// all'infinito (parallasse nullo) e le costellazioni restano ferme nel cielo mentre il giocatore si muove e si gira.
///
/// Il cielo è disegnato DALLA camera stessa (geometria in render-queue Background, additiva, ZWrite Off): niente
/// seconda camera → compatibile con il RenderScaler (che fa renderizzare la camera in una RenderTexture) e con la
/// vista sonda. La camera della MAPPA renderizza solo il layer "MapView" → il cielo (layer "Sky") ne è escluso da sé.
/// La bolla segue la camera che effettivamente disegna il cielo (quella il cui cullingMask include il layer Sky:
/// giocatore o sonda), così funziona anche guardando attraverso la sonda.
/// </summary>
public class SkyController : MonoBehaviour
{
    public const string SkyLayerName = "Sky";

    /// <summary>La camera che sta disegnando il cielo ADESSO (giocatore o sonda), o null in mappa. È la "verità"
    /// dell'osservatore in prima persona: la leggono lo strumento ottico (telescopio) e le costellazioni per seguire
    /// la vista attiva — così funzionano sia dal giocatore sia dalla sonda. Aggiornata ogni LateUpdate.</summary>
    public static Camera ActiveCamera { get; private set; }

    Transform skyRoot;
    Camera playerCam;
    RenderScaler playerScaler;
    MilkyWayBand milkyWay;
    StarFieldRenderer starField;   // per accendere il campo profondo solo zoomando
    int skyLayer;
    int skyMask;
    float baseFov = 52f;   // FOV "occhio nudo" di riferimento: zoom = (tan(base/2)/tan(fov/2))²

    public void Init(Camera playerCamera)
    {
        playerCam = playerCamera;
        ActiveCamera = playerCamera;   // finché non parte il primo LateUpdate
        playerScaler = playerCamera != null ? playerCamera.GetComponent<RenderScaler>() : null;
        if (playerCamera != null) baseFov = playerCamera.fieldOfView;
        skyLayer = LayerMask.NameToLayer(SkyLayerName);   // -1 se il layer non esiste: il cielo resta su Default (la mappa lo esclude comunque)
        skyMask = skyLayer >= 0 ? (1 << skyLayer) : ~0;

        var rootGo = new GameObject("SkyRoot");
        skyRoot = rootGo.transform;
        skyRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        Shader.SetGlobalFloat("_SkyZoom", 1f);   // occhio nudo (lo strumento ottico lo alzerà)

        // Ogni elemento del cielo è ISOLATO in un try/catch: se uno fallisce (shader, blob, texture), NON deve portare
        // giù gli altri (prima un errore nella Via Lattea spegneva anche le stelle, perché costruita per prima).
        // Le STELLE per prime: sono il cuore del cielo, costruiamole comunque vada il resto.
        try { starField = rootGo.AddComponent<StarFieldRenderer>();
              if (!starField.Build(skyRoot, skyLayer)) Debug.LogWarning("[sky] campo stellare non costruito (blob mancante?)."); }
        catch (System.Exception e) { Debug.LogError("[sky] stelle: " + e); }

        try { milkyWay = rootGo.AddComponent<MilkyWayBand>(); milkyWay.Build(skyRoot, skyLayer); }
        catch (System.Exception e) { Debug.LogError("[sky] Via Lattea: " + e); }

        try { rootGo.AddComponent<DeepSkyRenderer>().Build(skyRoot, skyLayer); }
        catch (System.Exception e) { Debug.LogError("[sky] deep-sky: " + e); }

        try { rootGo.AddComponent<ConstellationLines>().Build(skyRoot, skyLayer, playerCam); }
        catch (System.Exception e) { Debug.LogError("[sky] costellazioni: " + e); }
    }

    void LateUpdate()
    {
        var cam = ActiveSkyCamera();
        ActiveCamera = cam;   // pubblicata per telescopio/costellazioni (null in mappa → si nascondono)
        if (cam == null || skyRoot == null) return;
        skyRoot.SetPositionAndRotation(cam.transform.position, Quaternion.identity);

        // compensa la risoluzione dinamica: la camera del giocatore renderizza in una RenderTexture ridotta (RenderScaler);
        // senza compensare, le stelle si gonfiano/sfuocano quando la scala scende e il disegno additivo in più fa
        // scendere ancora la scala → pulsare. Passando la scala allo shader l'apparenza resta costante e il ciclo si spezza.
        float pxScale = (cam == playerCam && playerScaler != null) ? playerScaler.scale : 1f;
        Shader.SetGlobalFloat("_SkyPxScale", pxScale);

        // ZOOM = magnificazione² rispetto alla FOV occhio-nudo: restringendo il campo (binocolo/telescopio, o anche lo
        // slider FOV) le stelle DEBOLI attraversano la soglia di visibilità → "emergono", come davanti a un vero
        // strumento che concentra più luce nello stesso schermo. A campo largo (sonda) resta ≤1 (clampato nello shader).
        float t0 = Mathf.Tan(baseFov * 0.5f * Mathf.Deg2Rad);
        float t = Mathf.Tan(Mathf.Max(cam.fieldOfView, 0.05f) * 0.5f * Mathf.Deg2Rad);
        float mag = t0 / Mathf.Max(t, 1e-4f);
        // _SkyZoom guida la luminosità: restringendo il campo le stelle deboli EMERGONO. Uso mag^1.3 (più del lineare,
        // meno del quadrato): a 7× ≈12× (binocolo bello luminoso), a 80× ≈295× ma con campo strettissimo (poche stelle
        // sovrapposte → niente bianco). Il quadrato (×49 a 7×) saturava a bianco perché il campo largo ha tante stelle.
        Shader.SetGlobalFloat("_SkyZoom", Mathf.Max(Mathf.Pow(mag, 1.3f), 1f));
        Shader.SetGlobalFloat("_SkyTanHalfFov", t);   // raggio angolare dei deep-sky → pixel (dimensione cresce con lo zoom)

        // CAMPO PROFONDO: acceso solo quando c'è uno zoom reale (binocolo/telescopio o slider FOV stretto). A occhio
        // nudo è spento → ~840k vertici in meno da processare mentre voli (le stelle deboli lì sono comunque invisibili).
        if (starField != null) starField.SetDeepEnabled(mag > 1.5f);
    }

    /// <summary>La camera che disegna il cielo ADESSO: quella attiva il cui cullingMask include il layer Sky
    /// (giocatore o sonda). In mappa nessuna → il cielo non si muove (non è disegnato). Esclude la presentatrice del
    /// RenderScaler (cullingMask vuoto) e la camera-mappa (solo MapView).</summary>
    Camera ActiveSkyCamera()
    {
        if (MapMode.IsOpen) return null;
        var main = Camera.main;
        if (main != null && main.isActiveAndEnabled && (main.cullingMask & skyMask) != 0) return main;

        Camera best = null;
        var cams = Camera.allCameras;   // solo le camere ATTIVE
        for (int i = 0; i < cams.Length; i++)
        {
            var c = cams[i];
            if (c == null || (c.cullingMask & skyMask) == 0) continue;   // non disegna il cielo → ignora
            if (best == null || c.depth > best.depth) best = c;
        }
        return best != null ? best : playerCam;
    }
}

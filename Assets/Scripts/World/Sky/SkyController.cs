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

    Transform skyRoot;
    Camera playerCam;
    RenderScaler playerScaler;
    int skyLayer;
    int skyMask;

    public void Init(Camera playerCamera)
    {
        playerCam = playerCamera;
        playerScaler = playerCamera != null ? playerCamera.GetComponent<RenderScaler>() : null;
        skyLayer = LayerMask.NameToLayer(SkyLayerName);   // -1 se il layer non esiste: il cielo resta su Default (la mappa lo esclude comunque)
        skyMask = skyLayer >= 0 ? (1 << skyLayer) : ~0;

        var rootGo = new GameObject("SkyRoot");
        skyRoot = rootGo.transform;
        skyRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        Shader.SetGlobalFloat("_SkyZoom", 1f);   // occhio nudo (lo strumento ottico lo alzerà)

        var stars = rootGo.AddComponent<StarFieldRenderer>();
        if (!stars.Build(skyRoot, skyLayer))
            Debug.LogWarning("[sky] campo stellare non costruito (blob mancante?).");
    }

    void LateUpdate()
    {
        var cam = ActiveSkyCamera();
        if (cam == null || skyRoot == null) return;
        skyRoot.SetPositionAndRotation(cam.transform.position, Quaternion.identity);

        // compensa la risoluzione dinamica: la camera del giocatore renderizza in una RenderTexture ridotta (RenderScaler);
        // senza compensare, le stelle si gonfiano/sfuocano quando la scala scende e il disegno additivo in più fa
        // scendere ancora la scala → pulsare. Passando la scala allo shader l'apparenza resta costante e il ciclo si spezza.
        float pxScale = (cam == playerCam && playerScaler != null) ? playerScaler.scale : 1f;
        Shader.SetGlobalFloat("_SkyPxScale", pxScale);
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

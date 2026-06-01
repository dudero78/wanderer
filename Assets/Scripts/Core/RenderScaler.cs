using UnityEngine;

/// <summary>
/// Rende la scena a una frazione della risoluzione dello schermo e la riscala a pieno schermo.
/// È la leva più potente sul calore residuo: lo shader del pianeta gira PER PIXEL, quindi
/// dimezzando i pixel dimezzi il suo costo. Su Retina Unity renderizza a 2× (4× i pixel di un
/// 1080p): con scale 0.7 torni vicino a quella densità tagliando ~2× il lavoro per-pixel.
///
/// Architettura a DUE CAMERE (per non lasciare lo schermo senza camera → niente overlay
/// "No cameras rendering"):
///   - la camera di gioco renderizza la scena in una RenderTexture più piccola;
///   - una camera "presentatrice" non disegna nulla della scena (cullingMask vuoto) ma esiste
///     su Display 1, e nel suo OnRenderImage stende la RenderTexture a schermo intero.
/// Così c'è sempre una camera che presenta su Display 1, ma il lavoro pesante è a bassa res.
///
/// Compromesso onesto: sotto 1.0 l'immagine è più morbida. La nitidezza fine del pianeta viene
/// dalle ottave procedurali dello shader (indipendenti dalla risoluzione di render), quindi qui
/// si perde soprattutto sui bordi, non sul dettaglio del terreno.
/// </summary>
[RequireComponent(typeof(Camera))]
public class RenderScaler : MonoBehaviour
{
    [Range(0.4f, 1f)] public float scale = 0.7f;   // 1.0 = nativo; 0.7 ≈ densità 1080p su Retina

    Camera gameCam;
    Camera presentCam;
    RenderScalerPresenter presenter;
    RenderTexture rt;
    int lastW, lastH;
    float lastScale = -1f;

    void OnEnable()
    {
        gameCam = GetComponent<Camera>();

        // camera presentatrice: figlia, non renderizza geometria (cullingMask = Nothing),
        // sta su Display 1 e fa solo il blit finale. depth alto = disegnata per ultima.
        var go = new GameObject("RenderScalerPresenter");
        go.transform.SetParent(transform, false);
        presentCam = go.AddComponent<Camera>();
        presentCam.clearFlags = CameraClearFlags.Nothing;
        presentCam.cullingMask = 0;
        presentCam.depth = gameCam.depth + 1;
        presentCam.targetDisplay = gameCam.targetDisplay;
        presentCam.useOcclusionCulling = false;
        presentCam.allowMSAA = false;
        presenter = go.AddComponent<RenderScalerPresenter>();
    }

    void OnDisable()
    {
        if (gameCam) gameCam.targetTexture = null;
        if (presentCam) Destroy(presentCam.gameObject);
        Release();
    }

    // Update gira prima del rendering: garantiamo la RT giusta prima che la camera disegni.
    void Update() { Ensure(); }

    void Ensure()
    {
        if (gameCam == null) return;
        int w = Mathf.Max(8, Mathf.RoundToInt(Screen.width * scale));
        int h = Mathf.Max(8, Mathf.RoundToInt(Screen.height * scale));
        if (rt != null && w == lastW && h == lastH && scale == lastScale) return;

        Release();
        rt = new RenderTexture(w, h, 24, RenderTextureFormat.Default)
        {
            filterMode = FilterMode.Bilinear,   // upscale morbido
            name = "RenderScalerRT"
        };
        rt.Create();
        gameCam.targetTexture = rt;
        if (presenter) presenter.source = rt;
        lastW = w; lastH = h; lastScale = scale;
    }

    void Release()
    {
        if (rt == null) return;
        if (gameCam) gameCam.targetTexture = null;
        if (presenter) presenter.source = null;
        rt.Release();
        Destroy(rt);
        rt = null;
    }
}

/// <summary>
/// Sta sulla camera presentatrice: ignora ciò che la camera (non) ha disegnato e stende a
/// schermo la RenderTexture a bassa risoluzione prodotta dalla camera di gioco.
/// </summary>
[RequireComponent(typeof(Camera))]
public class RenderScalerPresenter : MonoBehaviour
{
    [System.NonSerialized] public RenderTexture source;

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (source != null) Graphics.Blit(source, dest);
        else Graphics.Blit(src, dest);   // fallback: passa attraverso, mai schermo nero
    }
}

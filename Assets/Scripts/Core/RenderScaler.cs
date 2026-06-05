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
    [Range(0.4f, 1f)] public float scale = 1f;     // 1.0 = nativo (nitido all'avvio); il dinamico scende SOLO su affanno GPU vero
    // RISOLUZIONE DINAMICA (tecnica AAA per restare fluidi): quando la GPU non tiene i ~60 fps, abbasso la
    // risoluzione di render (meno pixel = shader del mare più economico) per non scattare; quando c'è margine,
    // rialzo piano verso il nitido. Maschera il costo GPU senza toccare geometria/shader. minScale = quanto
    // morbido al massimo nei momenti peggiori (volo radente veloce sul mare).
    public bool dynamic = true;
    [Range(0.35f, 1f)] public float minScale = 0.4f;
    float smoothDt;
    float dynScale = 1f;

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
    void Update()
    {
        if (dynamic) UpdateDynamic();
        Ensure();
    }

    // Feedback semplice: se non teniamo ~57 fps (il cap è 60) la GPU è in affanno → abbassa in fretta; se siamo al
    // cap c'è margine → rialza piano (creep). 'scale' è a gradini di 0.05 → la RenderTexture si rialloca di rado.
    void UpdateDynamic()
    {
        float dt = Time.unscaledDeltaTime;
        smoothDt = smoothDt <= 0f ? dt : Mathf.Lerp(smoothDt, dt, 0.1f);
        // Quando il Governor sta capando a idleFps (sei fermo), il frame è lento PER IL CAP (~33 ms a 30 fps), non
        // perché la GPU è satura: quel dt è "avvelenato". Letto come affanno, abbasserebbe la risoluzione da fermo →
        // al primo movimento (torna a 60) lo schermo sarebbe morbido. Da idle-capped: TIENI la scala, non scendere.
        if (PerformanceGovernor.IdleCapped)
        {
            dynScale = Mathf.Clamp(dynScale, minScale, 1f);
            scale = Mathf.Round(dynScale / 0.05f) * 0.05f;
            return;
        }
        if (smoothDt > 1f / 57f) dynScale -= 0.8f * dt;   // affanno VERO (puntiamo a 60) → meno pixel, subito
        else dynScale += 0.12f * dt;                       // margine → più nitido, piano
        dynScale = Mathf.Clamp(dynScale, minScale, 1f);
        scale = Mathf.Round(dynScale / 0.05f) * 0.05f;
    }

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

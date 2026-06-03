using UnityEngine;

/// <summary>
/// Bootstrap della SCENA EDITOR (separata dal gioco): costruisce da codice un pianeta centrato all'origine,
/// una camera orbitale e l'UI dell'editor. Si parte da una sfera liscia e si compone la ricetta dal vivo.
/// Lanciata dal menu "Wanderer/Apri editor pianeti" (crea la scena PlanetEditor.unity → Play).
/// </summary>
public class PlanetEditorBootstrap : MonoBehaviour
{
    public int meshRes = 256;   // risoluzione mesh CPU dell'editor (l'anteprima live usa una res più bassa)
    public int gpuRes = 512;    // risoluzione anteprima GPU: alta perché sulla GPU costa quasi nulla (è il vantaggio della Tappa 1)

    void Start()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // --- camera orbitale ---
        var camGo = new GameObject("EditorCamera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 50000f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.01f, 0.01f, 0.03f);
        cam.fieldOfView = 50f;
        camGo.AddComponent<EditorOrbitCam>();

        // --- luce stellare ---
        var lightGo = new GameObject("Sun");
        var dl = lightGo.AddComponent<Light>();
        dl.type = LightType.Directional;
        dl.intensity = 1.7f;
        dl.color = new Color(1f, 0.96f, 0.9f);
        dl.shadows = LightShadows.None;
        lightGo.transform.rotation = Quaternion.Euler(32f, -35f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.06f, 0.06f, 0.075f);

        // --- pianeta all'origine, da ricetta "sfera liscia" ---
        var planetGo = new GameObject("Pianeta");
        var terrain = planetGo.AddComponent<PlanetTerrain>();
        terrain.ApplyRecipe(PlanetRecipe.SmoothSphere());

        // I materiali bakeati servono alla mesh CPU (la GPU usa il proprio shader procedurale). Il bake è anche il
        // test di capacità: se fallisce, shader/bake non disponibili.
        SingleMeshPlanet smp = null;
        GpuPlanetSurface gpu = null;
        var mats = PlanetBaker.BakeFaceMaterials(terrain, 64);
        if (mats != null)
        {
            // La mesh CPU NON viene costruita qui: l'editor parte in GPU e la crea pigra al primo passaggio a CPU
            // (apertura più veloce — niente campionamento del rumore sul main thread all'avvio). La normale-crateri
            // bakeata segue la RICETTA; l'editor la ri-bakea quando un edit si assesta.
            smp = planetGo.AddComponent<SingleMeshPlanet>();
            // --- anteprima GPU (render-dai-buffer senza readback): è il path di default, completo (geometria+colore+
            //     normali+acqua). Convive con la mesh CPU; il tasto G commuta per il confronto A/B. ---
            gpu = planetGo.AddComponent<GpuPlanetSurface>();
            gpu.Setup(terrain, gpuRes);
        }
        else
        {
            Debug.LogError("PlanetEditor: shader/bake non disponibili — impossibile mostrare il pianeta.");
        }

        // --- modo luce (L): ancorata (default) / libera (sole agganciato alla vista, da destra) ---
        var lm = camGo.AddComponent<EditorLightMode>();
        lm.sun = dl; lm.cam = camGo.transform; lm.gpu = gpu;

        // --- UI editor --- (Init prima di SetGpuSurface: la build pigra/fallback CPU usa mats+meshRes)
        var ed = gameObject.AddComponent<PlanetEditor>();
        ed.Init(terrain, smp, mats, meshRes);
        ed.SetGpuSurface(gpu);
    }
}

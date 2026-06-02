using UnityEngine;

/// <summary>
/// Bootstrap della SCENA EDITOR (separata dal gioco): costruisce da codice un pianeta centrato all'origine,
/// una camera orbitale e l'UI dell'editor. Si parte da una sfera liscia e si compone la ricetta dal vivo.
/// Lanciata dal menu "Wanderer/Apri editor pianeti" (crea la scena PlanetEditor.unity → Play).
/// </summary>
public class PlanetEditorBootstrap : MonoBehaviour
{
    public int meshRes = 256;   // risoluzione mesh dell'editor (l'anteprima live usa una res più bassa)

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

        SingleMeshPlanet smp = null;
        var mats = PlanetBaker.BakeFaceMaterials(terrain, 64);
        if (mats != null)
        {
            // NELL'EDITOR la luce dei crateri viene dalle NORMALI DELLA MESH (che riflettono la ricetta) →
            // WYSIWYG. La normale-crateri bakeata è decorrelata dalla ricetta (usa altri campi): la spengo.
            foreach (var m in mats) if (m != null) m.SetFloat("_CraterNormalApply", 0f);
            smp = planetGo.AddComponent<SingleMeshPlanet>();
            smp.Build(terrain, mats, meshRes, 48);
        }
        else
        {
            Debug.LogError("PlanetEditor: shader/bake non disponibili — impossibile mostrare il pianeta.");
        }

        // --- UI editor ---
        var ed = gameObject.AddComponent<PlanetEditor>();
        ed.Init(terrain, smp);
    }
}

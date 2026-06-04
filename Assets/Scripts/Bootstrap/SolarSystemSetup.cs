using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// COMPOSIZIONE DEL SISTEMA SOLARE, isolata dal resto della scena. Costruisce la stella, il pianeta-CASA
/// (dove nasce il giocatore) e i corpi in orbita, e li registra nel <see cref="SolarSystem"/>. GameBootstrap
/// chiama <see cref="Build"/> e riceve i riferimenti che gli servono (stella + casa) per piazzare giocatore,
/// luce, mappa ecc. — non sa nulla di QUALI corpi esistono.
///
/// Aggiungere un corpo in orbita = UNA voce in <see cref="Orbiting"/> (nome, raggio, gravità, attorno a chi,
/// orbita, cartella del bake, ricetta). Niente nuovi metodi. Le ricette/bake sono la stessa fonte di verità
/// del comando "Bake planet assets" (PlanetBakeTool chiama gli stessi Apply*Recipe).
/// </summary>
public static class SolarSystemSetup
{
    // --- Cetra: piccola luna craterizzata in orbita attorno al PIANETA (ricetta creata nell'editor) ---
    public const float CetraRadius = 300f;
    public const string CetraBakedDir = "BakedPlanet_Cetra";
    // --- Luna6: corpo creato nell'editor, in orbita attorno al SOLE. Raggio 500 m (dalla ricetta) ---
    public const float Luna6Radius = 500f;
    public const string Luna6BakedDir = "BakedPlanet_Luna6";
    // --- terra-test3: corpo dell'editor (con mare), bakeato su disco, in orbita attorno al SOLE. Raggio 700 m ---
    public const float TerraTest3Radius = 700f;
    public const string TerraTest3BakedDir = "BakedPlanet_terra-test3";
    // --- Luna7: piccola luna in orbita attorno a TERRA-TEST3 (non alla stella). Raggio 300 m ---
    public const float Luna7Radius = 300f;
    public const string Luna7BakedDir = "BakedPlanet_Luna7";

    /// <summary>Applica la ricetta di Cetra (Resources/Planets/Cetra.json), scalata al raggio. Una sola fonte di
    /// verità per gioco e bake offline. Ritorna false se la ricetta manca.</summary>
    public static bool ApplyCetraRecipe(PlanetTerrain terrain) => ApplyRecipe(terrain, "Cetra", CetraRadius);
    /// <summary>Applica la ricetta di Luna6 (Resources/Planets/Luna6.json), scalata al raggio.</summary>
    public static bool ApplyLuna6Recipe(PlanetTerrain terrain) => ApplyRecipe(terrain, "Luna6", Luna6Radius);
    /// <summary>Applica la ricetta di terra-test3 (Resources/Planets/terra-test3.json), scalata al raggio.</summary>
    public static bool ApplyTerraTest3Recipe(PlanetTerrain terrain) => ApplyRecipe(terrain, "terra-test3", TerraTest3Radius);
    /// <summary>Applica la ricetta di Luna7 (Resources/Planets/Luna7.json), scalata al raggio.</summary>
    public static bool ApplyLuna7Recipe(PlanetTerrain terrain) => ApplyRecipe(terrain, "Luna7", Luna7Radius);

    static bool ApplyRecipe(PlanetTerrain terrain, string resourceName, float radius)
    {
        var recipe = PlanetRecipe.LoadResource(resourceName);
        if (recipe == null) return false;
        terrain.ApplyRecipe(recipe.ScaledTo(radius));   // baseRadius della ricetta → raggio target: mesh e gravità in scala
        terrain.RebuildLayers();
        return true;
    }

    /// <summary>Descrizione DATA-DRIVEN di un corpo in orbita: tutto ciò che lo distingue dagli altri.</summary>
    struct OrbitBody
    {
        public string Name;
        public float Radius;
        public double Gravity;
        public bool AroundStar;     // true = orbita la stella; false = orbita il pianeta-casa
        public string ParentName;   // se valorizzato, orbita QUESTO corpo (per nome, dev'essere costruito PRIMA) → lune di lune
        public KeplerOrbit Orbit;
        public string BakedDir;
        public int ProxyRes;        // risoluzione del proxy nella mappa
        public System.Func<PlanetTerrain, bool> Apply;   // applica la ricetta (false se assente → il corpo si salta)
    }

    // I corpi in orbita, in un solo posto. L'ordine non conta. Per aggiungerne uno: una riga qui.
    static readonly OrbitBody[] Orbiting =
    {
        new OrbitBody {
            Name = "Cetra", Radius = CetraRadius, Gravity = 3.0, AroundStar = false, ProxyRes = 24,
            BakedDir = CetraBakedDir, Apply = ApplyCetraRecipe,
            Orbit = new KeplerOrbit { SemiMajorAxis = 4000, Eccentricity = 0.05, Period = 240, Inclination = 0.4 },
        },
        new OrbitBody {
            Name = "Luna6", Radius = Luna6Radius, Gravity = 9.81, AroundStar = true, ProxyRes = 32,
            BakedDir = Luna6BakedDir, Apply = ApplyLuna6Recipe,
            Orbit = new KeplerOrbit { SemiMajorAxis = 95000, Eccentricity = 0.08, Period = 1150, Inclination = 0.25 },
        },
        new OrbitBody {
            Name = "terra-test3", Radius = TerraTest3Radius, Gravity = 9.81, AroundStar = true, ProxyRes = 32,
            BakedDir = TerraTest3BakedDir, Apply = ApplyTerraTest3Recipe,
            Orbit = new KeplerOrbit { SemiMajorAxis = 130000, Eccentricity = 0.05, Period = 1840, Inclination = 0.15 },
        },
        new OrbitBody {
            Name = "Luna7", Radius = Luna7Radius, Gravity = 3.0, ParentName = "terra-test3", ProxyRes = 24,
            BakedDir = Luna7BakedDir, Apply = ApplyLuna7Recipe,
            Orbit = new KeplerOrbit { SemiMajorAxis = 4500, Eccentricity = 0.05, Period = 280, Inclination = 0.3 },
        },
    };

    /// <summary>Per "Bake planet assets": i corpi in orbita da bakeare (cartella + applicatore di ricetta), nello
    /// STESSO insieme del gioco → il comando segue automaticamente la lista. Aggiungere un corpo a 'Orbiting' lo
    /// include nel bake senza toccare il tool.</summary>
    public static IEnumerable<(string bakedDir, System.Func<PlanetTerrain, bool> apply)> BodyBakeTargets()
    {
        foreach (var d in Orbiting) yield return (d.BakedDir, d.Apply);
    }

    /// <summary>Riferimenti che il resto della scena (giocatore, luce, mappa, HUD) deve conoscere.</summary>
    public struct Built
    {
        public CelestialBody Star;
        public Transform StarTransform;
        public CelestialBody HomePlanet;
        public GameObject HomePlanetGo;
        public PlanetTerrain HomeTerrain;
    }

    /// <summary>Costruisce stella + pianeta-casa + corpi in orbita, registra tutto nel SolarSystem, ancora
    /// l'origine alla casa e posiziona i corpi al tempo 0. Ritorna i riferimenti chiave.</summary>
    public static Built Build(SolarSystem solar, bool useQuadtree, int singleMeshRes, bool useGpuSurface = false, int gpuSurfaceRes = 256, string spawnOnBody = "")
    {
        // --- Stella (corpo centrale, fisso all'origine dell'universo) ---
        var starGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        starGo.name = "Stella";
        var starCol = starGo.GetComponent<Collider>();
        if (starCol) Object.Destroy(starCol);
        var star = starGo.AddComponent<CelestialBody>();
        star.Radius = 2000;
        star.SurfaceGravity = 100;
        star.UniversePosition = Vector3d.Zero;
        starGo.transform.localScale = Vector3.one * (float)(star.Radius * 2);
        // disco pieno emissivo (Unlit/Color: niente ombreggiatura — un sole non va in ombra — e niente variante
        // _EMISSION strippata in build).
        var unlit = Shader.Find("Unlit/Color");
        if (unlit != null) starGo.GetComponent<Renderer>().material = new Material(unlit) { color = new Color(1f, 0.88f, 0.55f) };
        solar.Register(star);

        // --- Pianeta-CASA (orbita la stella; è dove nasce il giocatore e a cui si ancora l'origine) ---
        var planetGo = new GameObject("Pianeta");
        var planet = planetGo.AddComponent<CelestialBody>();
        planet.Radius = 500;
        planet.SurfaceGravity = 9.81;
        planet.Parent = star;
        planet.Orbit = new KeplerOrbit { SemiMajorAxis = 60000, Eccentricity = 0.1, Period = 600, Inclination = 0.15 };

        // terreno procedurale: il noise definisce la forma, la mesh la mostra, il PlanetWalker ci cammina sopra.
        // Una sola fonte di verità. Parametri in PlanetPresets (condiviso col bake offline).
        var terrain = planetGo.AddComponent<PlanetTerrain>();
        PlanetPresets.ConfigureDemoPlanet(terrain);
        terrain.RebuildLayers();   // pipeline pronta sul main thread: i thread di build la leggono già fatta

        // PRIMA gli asset bakeati offline (Resources/BakedPlanet); altrimenti bake a runtime (fallback).
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var faceMats = PlanetBaker.TryLoadBakedMaterials(terrain) ?? PlanetBaker.BakeFaceMaterials(terrain, 64);
        Debug.Log($"[load] materiali pianeta: {sw.ElapsedMilliseconds} ms");
        if (faceMats != null)
        {
            sw.Restart();
            AddSurface(planetGo, terrain, faceMats, useQuadtree, singleMeshRes, 40, useGpuSurface, gpuSurfaceRes);
            Debug.Log($"[load] superficie pianeta ({(useGpuSurface ? "GPU" : useQuadtree ? "quadtree" : "mesh singola")}): {sw.ElapsedMilliseconds} ms");
        }
        else
        {
            // fallback robusto: mesh uniforme + materiale procedurale.
            var planetSh = Shader.Find("Wanderer/Planet");
            var planetMat = planetSh != null ? new Material(planetSh) : null;
            if (planetMat != null)
            {
                planetMat.SetFloat("_BaseRadius", terrain.BaseRadius);
                planetMat.SetFloat("_Amplitude", terrain.Amplitude);
            }
            else Debug.LogError("Shader 'Wanderer/Planet' non trovato nella build (Always Included Shaders).");
            PlanetMeshBuilder.Build(planetGo.transform, terrain, 300, planetMat);
            Debug.Log("Pianeta: bake non riuscito, mesh uniforme procedurale (fallback).");
        }
        solar.Register(planet);

        // --- Corpi in orbita (data-driven). Genitore risolto per nome (lune di lune): ParentName dev'essere costruito
        //     PRIMA nell'array. Senza ParentName → stella o pianeta-casa. Cattura il corpo su cui nascere (test). ---
        CelestialBody spawnBody = planet; GameObject spawnGo = planetGo; PlanetTerrain spawnTerrain = terrain;
        var byName = new Dictionary<string, CelestialBody> { { "Pianeta", planet } };
        foreach (var def in Orbiting)
        {
            CelestialBody parent = !string.IsNullOrEmpty(def.ParentName) && byName.TryGetValue(def.ParentName, out var p)
                ? p : (def.AroundStar ? star : planet);
            var b = BuildOrbitBody(def, solar, parent, useQuadtree, useGpuSurface, gpuSurfaceRes);
            if (b == null) continue;
            byName[def.Name] = b;
            if (!string.IsNullOrEmpty(spawnOnBody) && def.Name == spawnOnBody)
            {
                spawnBody = b; spawnGo = b.gameObject; spawnTerrain = b.GetComponent<PlanetTerrain>();
            }
        }

        // origine ancorata al corpo di SPAWN (la casa, o quello scelto per il test): resta a ~(0,0,0). Posiziona i
        // corpi al tempo 0 PRIMA che GameBootstrap legga la posizione del corpo per lo spawn del giocatore.
        solar.Anchor = spawnBody;
        spawnBody.UpdatePosition(0);
        solar.Step();

        return new Built {
            Star = star, StarTransform = starGo.transform,
            HomePlanet = spawnBody, HomePlanetGo = spawnGo, HomeTerrain = spawnTerrain,
        };
    }

    /// <summary>Costruisce un corpo in orbita dalla sua descrizione. Walker/mappa/viaggio lo gestiscono "gratis"
    /// (leggono PlanetTerrain/CelestialBody). Se la ricetta manca, salta il corpo (il resto del gioco parte).</summary>
    static CelestialBody BuildOrbitBody(OrbitBody def, SolarSystem solar, CelestialBody parent, bool useQuadtree, bool useGpuSurface, int gpuSurfaceRes)
    {
        var go = new GameObject(def.Name);
        var terrain = go.AddComponent<PlanetTerrain>();
        if (!def.Apply(terrain)) { Object.Destroy(go); return null; }

        var body = go.AddComponent<CelestialBody>();
        body.Radius = def.Radius;
        body.SurfaceGravity = def.Gravity;
        body.Parent = parent;
        body.Orbit = def.Orbit;

        // PRIMA gli asset bakeati offline (cartella dedicata), poi bake a runtime.
        var faceMats = PlanetBaker.TryLoadBakedMaterials(terrain, def.BakedDir) ?? PlanetBaker.BakeFaceMaterials(terrain, 64);
        if (faceMats != null)
            AddSurface(go, terrain, faceMats, useQuadtree, 256, def.ProxyRes, useGpuSurface, gpuSurfaceRes);
        else Debug.LogWarning($"{def.Name}: bake non riuscito, niente superficie (corpo comunque presente per gravità/mappa).");

        solar.Register(body);
        return body;
    }

    /// <summary>Aggiunge la superficie renderizzata a un corpo roccioso: quadtree CDLOD (geometria view-dependent
    /// → crateri nitidi calpestabili) oppure mesh singola a risoluzione fissa (fallback). Walker/gravità/collisione
    /// NON dipendono da questa scelta (leggono PlanetTerrain.SampleHeight).</summary>
    static void AddSurface(GameObject go, PlanetTerrain terrain, Material[] faceMats, bool quadtree, int singleRes, int proxyRes,
                           bool gpuSurface = false, int gpuRes = 256)
    {
        terrain.FaceMaterials = faceMats;   // li riusa il proxy del corpo reale in mappa (stesso aspetto, niente ri-bake)
        if (gpuSurface)
        {
            // percorso B1: geometria+colore sulla GPU, 1 draw indirect. Se la GPU non regge i compute, Ready
            // resta false → si butta il componente e si ripiega sul quadtree (niente pianeta invisibile).
            var gpu = go.AddComponent<GpuPlanetRenderer>();
            gpu.Setup(terrain, gpuRes);
            if (gpu.Ready) return;
            Object.Destroy(gpu);
            Debug.LogWarning("Superficie GPU non disponibile → ripiego sul quadtree.");
        }
        if (quadtree)
        {
            var qt = go.AddComponent<PlanetQuadtree>();
            qt.Init(terrain, faceMats, null);   // la camera la prende da Camera.main quando esiste
        }
        else
        {
            var smp = go.AddComponent<SingleMeshPlanet>();
            smp.Build(terrain, faceMats, singleRes, proxyRes);
        }
    }
}

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
    // --- terra-test3: corpo dell'editor (con mare). Gemello binario di Valentina2 (orbitano un baricentro comune). 700 m ---
    public const float TerraTest3Radius = 700f;
    public const string TerraTest3BakedDir = "BakedPlanet_terra-test3";
    // --- Valentina2: gemello binario di terra-test3, ma RICETTA PROPRIA (Valentina2.json) → editabile a parte. 700 m ---
    public const float Valentina2Radius = 700f;
    public const string Valentina2BakedDir = "BakedPlanet_Valentina2";
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
    /// <summary>Applica la ricetta di Valentina2 (Resources/Planets/Valentina2.json), scalata al raggio.</summary>
    public static bool ApplyValentina2Recipe(PlanetTerrain terrain) => ApplyRecipe(terrain, "Valentina2", Valentina2Radius);
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
    public struct OrbitBody
    {
        public string Name;
        public float Radius;
        public double Gravity;
        public bool AroundStar;     // true = orbita la stella (del SUO sistema); false = orbita il pianeta-casa
        public string ParentName;   // se valorizzato, orbita QUESTO corpo (per nome, dev'essere costruito PRIMA) → lune di lune
        public KeplerOrbit Orbit;
        public string BakedDir;
        public int ProxyRes;        // risoluzione del proxy nella mappa
        public System.Func<PlanetTerrain, bool> Apply;   // applica la ricetta (false se assente → il corpo si salta)
    }

    /// <summary>
    /// RICETTA DI UN SISTEMA STELLARE (Tappa 3 multi-sistema): la composizione di QUALUNQUE sistema come DATO —
    /// stella (raggio/gravità/colore) + posizione nello spazio-galassia (`SystemOrigin`, double) + i suoi corpi.
    /// Il sistema-CASA resta costruito dal percorso bespoke di <see cref="Build"/> (PlanetPresets + binario), identico
    /// a prima (rischio zero a N=1); i sistemi DISTANTI sono interamente data-driven e li costruisce
    /// <see cref="BuildSystem"/> SOLO quando vengono "svegliati" (Tappa 4). Finché dormono esistono solo come questo
    /// dato (kB) → zero corpi, zero fette del pool, zero BodyId.
    /// </summary>
    public class SystemRecipe
    {
        public string Name;
        public Vector3d SystemOrigin = Vector3d.Zero;
        public float StarRadius = 1500f;
        public double StarGravity = 100;
        public Color StarColor = new Color(1f, 0.88f, 0.55f);
        public OrbitBody[] Bodies;   // i corpi del sistema (orbitano la SUA stella). Senza binario/baricentro per i distanti.
    }

    /// <summary>
    /// La GALASSIA: i sistemi stellari come DATO (modo Carmack — a mano, non un generatore). L'indice 0 è il sistema
    /// CASA (SystemOrigin = Zero, costruito da Build nel suo percorso bespoke); gli altri sono DISTANTI e dormienti,
    /// costruibili da BuildSystem alla sveglia (Tappa 4) e mostrati come stelle nella mappa galattica (Tappa 5).
    /// Riusano le ricette ufficiali esistenti (Luna6/Cetra/…) → corpi veri quando svegliati, niente arte nuova.
    /// Galassia STATICA (i SystemOrigin non derivano nel tempo): coerente con le orbite on-rails (vedi STARSYSTEM_DESIGN).
    /// </summary>
    public static readonly SystemRecipe[] Galaxy =
    {
        new SystemRecipe { Name = "Casa", SystemOrigin = Vector3d.Zero, StarRadius = 2000f, StarGravity = 100,
                           StarColor = new Color(1f, 0.88f, 0.55f), Bodies = null },   // Bodies=null: la casa la costruisce Build (bespoke)
        // Sistema distante "Helios" (~6 Mm sull'asse X): stella rossastra + 2 corpi (riusano ricette esistenti).
        new SystemRecipe {
            Name = "Helios", SystemOrigin = new Vector3d(6_000_000, 0, 0), StarRadius = 1600f, StarGravity = 90,
            StarColor = new Color(1f, 0.62f, 0.42f),
            Bodies = new[] {
                new OrbitBody { Name = "Helios-I", Radius = Luna6Radius, Gravity = 9.81, AroundStar = true, ProxyRes = 32,
                    BakedDir = Luna6BakedDir, Apply = ApplyLuna6Recipe,
                    Orbit = new KeplerOrbit { SemiMajorAxis = 70000, Eccentricity = 0.06, Period = 900, Inclination = 0.2 } },
                new OrbitBody { Name = "Helios-II", Radius = CetraRadius, Gravity = 3.0, AroundStar = true, ProxyRes = 24,
                    BakedDir = CetraBakedDir, Apply = ApplyCetraRecipe,
                    Orbit = new KeplerOrbit { SemiMajorAxis = 120000, Eccentricity = 0.1, Period = 1500, Inclination = 0.35 } },
            },
        },
        // Sistema distante "Vega" (~ -5 Mm X, +4 Mm Z): stella azzurra + 2 corpi.
        new SystemRecipe {
            Name = "Vega", SystemOrigin = new Vector3d(-5_000_000, 0, 4_000_000), StarRadius = 2400f, StarGravity = 120,
            StarColor = new Color(0.72f, 0.82f, 1f),
            Bodies = new[] {
                new OrbitBody { Name = "Vega-I", Radius = Valentina2Radius, Gravity = 9.81, AroundStar = true, ProxyRes = 32,
                    BakedDir = Valentina2BakedDir, Apply = ApplyValentina2Recipe,
                    Orbit = new KeplerOrbit { SemiMajorAxis = 90000, Eccentricity = 0.04, Period = 1100, Inclination = 0.12 } },
                new OrbitBody { Name = "Vega-II", Radius = Luna7Radius, Gravity = 3.0, AroundStar = true, ProxyRes = 24,
                    BakedDir = Luna7BakedDir, Apply = ApplyLuna7Recipe,
                    Orbit = new KeplerOrbit { SemiMajorAxis = 150000, Eccentricity = 0.08, Period = 1900, Inclination = 0.28 } },
            },
        },
    };

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
        // BINARIO: terra-test3 e Valentina2 orbitano un BARICENTRO comune (creato in Build come "Baricentro"), a
        // 180° l'uno dall'altro (stessa orbita, MeanAnomalyAtEpoch 0 e π) → restano sempre opposti, separazione
        // costante 2·1500 = 3000 m fra i centri (~1600 m fra le superfici: un salto breve). Il baricentro orbita la
        // stella → la coppia gira INSIEME attorno al sole. Le orbite ("O"/mappa) mostrano tutto correttamente.
        new OrbitBody {
            Name = "terra-test3", Radius = TerraTest3Radius, Gravity = 9.81, ParentName = "Baricentro", ProxyRes = 32,
            BakedDir = TerraTest3BakedDir, Apply = ApplyTerraTest3Recipe,
            Orbit = new KeplerOrbit { SemiMajorAxis = 1500, Eccentricity = 0.0, Period = 320, Inclination = 0.15, MeanAnomalyAtEpoch = 0.0 },   // = inclinazione del baricentro: il binario gira nel PIANO del suo moto attorno al sole (coerente in mappa)
        },
        // Gemello: RICETTA PROPRIA (Valentina2.json) → puoi editarlo a parte. Orbita opposta sul baricentro (M0 = π).
        new OrbitBody {
            Name = "Valentina2", Radius = Valentina2Radius, Gravity = 9.81, ParentName = "Baricentro", ProxyRes = 32,
            BakedDir = Valentina2BakedDir, Apply = ApplyValentina2Recipe,
            Orbit = new KeplerOrbit { SemiMajorAxis = 1500, Eccentricity = 0.0, Period = 320, Inclination = 0.15, MeanAnomalyAtEpoch = 3.14159265 },   // stessa inclinazione del gemello/baricentro → sempre opposti (M0=π), coplanari col moto comune
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
        // dedup per cartella: corpi che condividono il bake (es. i gemelli binari) lo producono UNA volta sola.
        var seen = new HashSet<string>();
        foreach (var d in Orbiting)
            if (seen.Add(d.BakedDir)) yield return (d.BakedDir, d.Apply);
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

        // SISTEMA stellare (Tappa 1 multi-sistema): contenitore della stella + i suoi corpi. A N=1 SystemOrigin=Zero
        // (= posizione della stella, che si propaga giù per la catena dei genitori → niente cambio di coordinate);
        // Bodies riferisce la STESSA lista di solar.Bodies (che Register popola) → nessuna vista divergente.
        var system = new StarSystem { Name = "Casa", SystemOrigin = Vector3d.Zero, Star = star, Bodies = solar.Bodies, Active = true };
        system.Recipe = Galaxy[0]; system.StarTransform = starGo.transform;
        system.StarColor = Galaxy[0].StarColor; system.StarRadius = Galaxy[0].StarRadius;
        solar.Systems.Add(system);
        solar.Active = system;

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

        // BARICENTRO del binario terra-test3/Valentina2: punto SENZA MASSA che orbita la stella. I due gemelli gli
        // orbitano attorno (vedi Orbiting) → coppia legata che gira insieme attorno al sole. Massless = niente
        // gravità/ancora/marker, ma la sua orbita viene disegnata. Registrato PRIMA dei gemelli (il genitore va
        // aggiornato prima dei figli in SolarSystem.Step). Niente terreno/superficie: è invisibile.
        var baryGo = new GameObject("Baricentro");
        var bary = baryGo.AddComponent<CelestialBody>();
        bary.Radius = 0; bary.SurfaceGravity = 0; bary.Massless = true;
        bary.Parent = star;
        bary.Orbit = new KeplerOrbit { SemiMajorAxis = 130000, Eccentricity = 0.05, Period = 1840, Inclination = 0.15 };
        solar.Register(bary);

        var byName = new Dictionary<string, CelestialBody> { { "Pianeta", planet }, { "Baricentro", bary } };
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
        foreach (var b in solar.Bodies) if (b != null) b.System = system;   // Tappa 1: ogni corpo conosce il suo sistema (a N=1 = "Casa")

        // SISTEMI DISTANTI (Tappa 3): registrati DORMIENTI — solo dato (Name+SystemOrigin+Recipe+colore stella), zero
        // corpi/fette/BodyId. Li sveglia BuildSystem alla promozione (Tappa 4); la mappa galattica li mostra (Tappa 5).
        for (int gi = 1; gi < Galaxy.Length; gi++)
        {
            var rec = Galaxy[gi];
            solar.Systems.Add(new StarSystem {
                Name = rec.Name, SystemOrigin = rec.SystemOrigin, Recipe = rec,
                StarColor = rec.StarColor, StarRadius = rec.StarRadius,
                Bodies = null, Star = null, Active = false, SceneObjects = null,
            });
        }

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

    /// <summary>TAPPA 4 — SVEGLIA un sistema DORMIENTE: costruisce la sua stella + i suoi corpi (data-driven dalla
    /// SystemRecipe), li registra nel SolarSystem e li posiziona al tempo corrente. Riusa BuildOrbitBody (stesso
    /// percorso del sistema-casa) → renderer GPU + walker + mappa "gratis". Il limite di corpi vivi non c'è più
    /// (region-stamp uint), quindi il sistema-casa può restare residente mentre un sistema distante si sveglia.</summary>
    public static bool BuildSystem(SolarSystem solar, StarSystem sys, bool useQuadtree, bool useGpuSurface, int gpuSurfaceRes)
    {
        if (sys == null || sys.Recipe == null || sys.Recipe.Bodies == null || sys.Active) return false;
        var rec = sys.Recipe;

        // stella del sistema: corpo SENZA orbita, fissa al proprio SystemOrigin (frame del sistema nello spazio-galassia)
        var starGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        starGo.name = rec.Name + "-Stella";
        var starCol = starGo.GetComponent<Collider>(); if (starCol) Object.Destroy(starCol);
        var star = starGo.AddComponent<CelestialBody>();
        star.Radius = rec.StarRadius; star.SurfaceGravity = rec.StarGravity;
        star.UniversePosition = rec.SystemOrigin;
        starGo.transform.localScale = Vector3.one * (float)(star.Radius * 2);
        var unlit = Shader.Find("Unlit/Color");
        if (unlit != null) starGo.GetComponent<Renderer>().material = new Material(unlit) { color = rec.StarColor };
        star.System = sys;
        solar.Register(star);

        var objs = new List<GameObject> { starGo };
        var bodies = new List<CelestialBody>();
        var byName = new Dictionary<string, CelestialBody>();
        foreach (var def in rec.Bodies)
        {
            CelestialBody parent = !string.IsNullOrEmpty(def.ParentName) && byName.TryGetValue(def.ParentName, out var p) ? p : star;
            var b = BuildOrbitBody(def, solar, parent, useQuadtree, useGpuSurface, gpuSurfaceRes);
            if (b == null) continue;
            b.System = sys; byName[def.Name] = b; bodies.Add(b); objs.Add(b.gameObject);
        }
        sys.Star = star; sys.StarTransform = starGo.transform; sys.Bodies = bodies; sys.SceneObjects = objs; sys.Active = true;

        star.UpdatePosition(solar.SimTime);
        foreach (var b in bodies) b.UpdatePosition(solar.SimTime);
        Debug.Log($"[multi-sistema] svegliato '{rec.Name}' a SystemOrigin {rec.SystemOrigin} ({bodies.Count} corpi).");
        return true;
    }

    /// <summary>TAPPA 4 — ADDORMENTA un sistema attivo: distrugge stella + corpi (i GpuPlanetRenderer.OnDestroy
    /// rendono fette e BodyId al pool) e li toglie dal SolarSystem. Torna a sola DATO (Recipe + SystemOrigin).</summary>
    public static void DestroySystem(SolarSystem solar, StarSystem sys)
    {
        if (sys == null || !sys.Active || sys.SceneObjects == null) return;
        if (sys.Bodies != null) foreach (var b in sys.Bodies) if (b != null) solar.Unregister(b);
        if (sys.Star != null) solar.Unregister(sys.Star);
        foreach (var go in sys.SceneObjects) if (go != null) Object.Destroy(go);   // OnDestroy del renderer GPU restituisce fette+BodyId
        Debug.Log($"[multi-sistema] addormentato '{sys.Name}'.");
        sys.SceneObjects = null; sys.Bodies = null; sys.Star = null; sys.StarTransform = null; sys.Active = false;
    }

    /// <summary>Aggiunge la superficie renderizzata a un corpo roccioso. GERARCHIA DEI RENDERER (decisa — audit #2):
    /// 1) <see cref="GpuPlanetRenderer"/> = renderer AUTORITATIVO in gioco (quadtree CDLOD su GPU + geomorph + 1 draw
    ///    indirect + pool VRAM condiviso); 2) <see cref="PlanetQuadtree"/> = FALLBACK ESPLICITO (stesso LOD su CPU) se
    ///    la GPU non regge i compute — NON è morto, è la garanzia "niente pianeta invisibile"; 3)
    ///    <see cref="SingleMeshPlanet"/> = fallback finale + proxy a bassa res della mappa.
    /// DISCIPLINA (contro il debito dei "renderer paralleli"): le feature di RESA nuove (geomorph, materiali PBR,
    /// eclissi GPU...) vanno SOLO sul renderer autoritativo; i fallback restano CONGELATI, così un fix non va
    /// replicato in N posti né diverge. Walker/gravità/collisione NON dipendono dal renderer (leggono SampleHeight).</summary>
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

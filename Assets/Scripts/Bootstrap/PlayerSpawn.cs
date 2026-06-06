using UnityEngine;

/// <summary>
/// SPAWN DEL GIOCATORE, isolato dal resto della scena (come <see cref="SolarSystemSetup"/> per il sistema solare).
/// Dato un CORPO (quello restituito da SolarSystemSetup come spawn), crea e piazza il "rig" del giocatore:
/// giocatore + tuta-jetpack + camera + torcia, all'alba sull'equatore rivolto al sole. Restituisce i riferimenti
/// che servono al resto del bootstrap (camera/walker/torcia/tuta) per luce, mappa, HUD.
///
/// Niente logica di scena qui dentro che non sia il giocatore: la composizione del sistema sta in
/// SolarSystemSetup, la luce/UI restano in GameBootstrap. Così ogni pezzo è separato e configurabile.
/// </summary>
public static class PlayerSpawn
{
    /// <summary>Riferimenti del rig del giocatore che il resto della scena (luce, mappa, HUD) deve conoscere.</summary>
    public struct Built
    {
        public GameObject PlayerGo;
        public PlanetWalker Walker;
        public Rigidbody Rb;
        public Camera Cam;
        public Transform CamTransform;
        public Flashlight Flashlight;
        public Transform SuitTransform;
    }

    /// <summary>Crea il giocatore (+ tuta + camera + torcia) a terra sul corpo dato, all'alba sull'equatore.</summary>
    public static Built Spawn(SolarSystem solar, GameObject bodyGo, PlanetTerrain terrain, Transform starTransform)
    {
        // Nasce a terra all'ALBA sull'EQUATORE, rivolto verso il sole (sole all'orizzonte davanti). Direzione del
        // sole = dal corpo verso la stella. Il polo è Vector3.up → l'equatore è il piano y=0. Il terminatore
        // (alba/tramonto) è il cerchio perpendicolare al sole. Il loro incrocio = cross(sole, polo).
        Vector3 sunDir = (starTransform.position - bodyGo.transform.position).normalized;
        Vector3 pole = Vector3.up;
        Vector3 spawnDir = Vector3.Cross(sunDir, pole);
        if (spawnDir.sqrMagnitude < 1e-4f) spawnDir = Vector3.Cross(sunDir, Vector3.right);   // sole sul polo: ripiego
        spawnDir.Normalize();
        Quaternion spawnRot = Quaternion.LookRotation(sunDir, spawnDir);   // forward=sole, up=radiale

        Vector3 playerSpawnPos = bodyGo.transform.position + spawnDir * (terrain.SampleHeight(spawnDir) + 1f);
        var playerGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        playerGo.name = "Player";
        var prb = playerGo.AddComponent<Rigidbody>();
        var walker = playerGo.AddComponent<PlanetWalker>();
        playerGo.transform.SetPositionAndRotation(playerSpawnPos, spawnRot);
        prb.position = playerSpawnPos;   // allinea subito lo stato fisico: niente teletrasporto a (0,0,0) al frame 0
        prb.rotation = spawnRot;
        solar.PlayerBody = prb;          // da ora l'origine ancora al corpo PIÙ VICINO al giocatore (viaggi tra corpi)
        var playerCol = playerGo.GetComponent<Collider>();
        if (playerCol) playerCol.enabled = false;   // a terra col vincolo analitico: il collider fisico non serve
        SetColor(playerGo, new Color(0.85f, 0.35f, 0.3f));

        // Tuta-jetpack: faro-pilastro, ~8 m DAVANTI al giocatore (= verso il sole, già tangente al suolo), sul terreno.
        Vector3 suitDir = (playerSpawnPos + sunDir * 8f - bodyGo.transform.position).normalized;
        Vector3 suitGround = bodyGo.transform.position + suitDir * terrain.SampleHeight(suitDir);

        // TUTA = OMINO stilizzato METALLICO LUMINOSO. Pezzi (capsule = estremità ARROTONDATE per mani/piedi/bombole):
        // TORSO + 2 GAMBE + 2 BRACCIA + 2 BOMBOLE/MOTORI sullo zaino (dietro), con UGELLI luminosi in fondo. TESTA =
        // sfera che brilla. Parent vuoto (SuitPickup lo fa ondeggiare e lo gira verso il giocatore). +Y in alto, +Z
        // davanti (verso di te); le bombole stanno dietro (-Z). Capsule → niente spigoli vivi alle estremità.
        var suitGo = new GameObject("Tuta");
        // TORSO a profilo (lathe): più MAGRO, più BASSO e più piccolo. Fondo arrotondato → si allarga alle SPALLE
        // (~0.36 a y≈0.82) → top TRONCATO con bordi ARROTONDATI (rientra dolce a 0). Niente pillola.
        Vector2[] torso = {
            new Vector2(0.00f, -0.10f), new Vector2(0.16f, -0.04f), new Vector2(0.23f, 0.08f),
            new Vector2(0.25f, 0.35f), new Vector2(0.28f, 0.58f), new Vector2(0.33f, 0.74f),
            new Vector2(0.36f, 0.82f), new Vector2(0.35f, 0.88f), new Vector2(0.29f, 0.93f),
            new Vector2(0.17f, 0.96f), new Vector2(0.06f, 0.98f), new Vector2(0.00f, 0.99f),
        };
        MetalLathe(suitGo.transform, "Torso", torso);
        // GAMBE/BRACCIA cicciotte (pillola spessa). BOMBOLE decisamente cicciotte.
        MetalPart(suitGo.transform, PrimitiveType.Capsule, "GambaSx", new Vector3(-0.16f, -0.40f, 0.02f), new Vector3(0.20f, 0.40f, 0.20f));
        MetalPart(suitGo.transform, PrimitiveType.Capsule, "GambaDx", new Vector3(0.16f, -0.40f, 0.02f), new Vector3(0.20f, 0.40f, 0.20f));
        // BRACCIA attaccate alle SPALLE che si ALLARGANO a V (~20° per lato → ~40° fra loro), cicciotte.
        MetalPart(suitGo.transform, PrimitiveType.Capsule, "BraccioSx", new Vector3(-0.40f, 0.50f, 0.02f), new Vector3(0.16f, 0.36f, 0.16f), new Vector3(0f, 0f, -20f));
        MetalPart(suitGo.transform, PrimitiveType.Capsule, "BraccioDx", new Vector3(0.40f, 0.50f, 0.02f), new Vector3(0.16f, 0.36f, 0.16f), new Vector3(0f, 0f, 20f));
        MetalPart(suitGo.transform, PrimitiveType.Capsule, "BombolaSx", new Vector3(-0.22f, 0.55f, -0.30f), new Vector3(0.24f, 0.40f, 0.24f));
        MetalPart(suitGo.transform, PrimitiveType.Capsule, "BombolaDx", new Vector3(0.22f, 0.55f, -0.30f), new Vector3(0.24f, 0.40f, 0.24f));

        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Testa";
        var headCol = head.GetComponent<Collider>(); if (headCol) Object.Destroy(headCol);
        head.transform.SetParent(suitGo.transform, false);
        head.transform.localPosition = new Vector3(0f, 1.30f, 0f);   // leggermente STACCATA dal corpo (torso top ≈ 0.99)
        head.transform.localScale = Vector3.one * 0.5f;
        SetColor(head, new Color(0.4f, 0.95f, 1f), emissive: true);   // testa che BRILLA (Unlit, sopravvive al build)

        // UGELLI dei MOTORI: piccole sfere LUMINOSE in fondo alle bombole (l'"accensione" dei motori).
        GlowBall(suitGo.transform, "UgelloSx", new Vector3(-0.22f, 0.14f, -0.30f), 0.20f, new Color(0.4f, 0.9f, 1f));
        GlowBall(suitGo.transform, "UgelloDx", new Vector3(0.22f, 0.14f, -0.30f), 0.20f, new Color(0.4f, 0.9f, 1f));

        var glowGo = new GameObject("Glow");
        glowGo.transform.SetParent(suitGo.transform, false);
        glowGo.transform.localPosition = new Vector3(0f, 0.6f, 0f);
        var glow = glowGo.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(0.3f, 0.95f, 1f);
        glow.range = 6f;
        glow.intensity = 1.4f;

        var pickup = suitGo.AddComponent<SuitPickup>();
        pickup.surfaceClearance = 0.78f;   // i piedi (gambe a y≈−0.80) sfiorano il suolo
        pickup.pickupRadius = 3.5f;
        pickup.Init(playerGo.transform, walker, suitGround, suitDir);
        solar.Loose.Add(suitGo.transform);   // oggetto sciolto: va traslato allo switch di corpo

        // --- Camera (figlia del giocatore) ---
        var camGo = new GameObject("PlayerCamera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 300000f;
        cam.fieldOfView = 52f;   // contenuto: a campo largo le sfere ai bordi si deformano. Regolabile dal menù à.
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.01f, 0.01f, 0.03f);
        camGo.AddComponent<RenderScaler>();   // risoluzione dinamica (tiene gli fps abbassando i pixel quando serve)
        camGo.transform.SetParent(playerGo.transform, false);
        camGo.transform.localPosition = new Vector3(0f, 0.6f, 0f);
        walker.cameraPivot = camGo.transform;

        // --- Torcia (inclusa nella tuta): spotlight che segue lo sguardo, toggle con F ---
        var flashGo = new GameObject("Flashlight");
        flashGo.transform.SetParent(camGo.transform, false);
        flashGo.transform.localPosition = new Vector3(0f, -0.15f, 0f);   // lampada al mento: angolo dal basso
        var lamp = flashGo.AddComponent<Light>();
        lamp.type = LightType.Spot;
        lamp.range = 110f;
        lamp.spotAngle = 68f;
        lamp.color = new Color(1f, 0.95f, 0.85f);
        lamp.shadows = LightShadows.None;   // a luce radente le ombre proiettate danno acne
        lamp.enabled = true;                // sempre acceso: la torcia si commuta via intensità
        lamp.intensity = 0f;
        var flashlight = flashGo.AddComponent<Flashlight>();
        flashlight.walker = walker;
        flashlight.lamp = lamp;
        flashlight.onIntensity = 2.2f;
        flashlight.baseRange = 110f;

        return new Built
        {
            PlayerGo = playerGo, Walker = walker, Rb = prb,
            Cam = cam, CamTransform = camGo.transform, Flashlight = flashlight, SuitTransform = suitGo.transform,
        };
    }

    /// <summary>Un pezzo METALLICO dell'omino-tuta: primitivo (cilindro/sfera) acciaio scuro lucido (Standard,
    /// illuminato dal sole), senza collider, figlio del parent dato. (Niente keyword: metallic/smoothness sono
    /// float dello Standard, sopravvivono al build.)</summary>
    static void MetalPart(Transform parent, PrimitiveType type, string name, Vector3 localPos, Vector3 localScale)
        => MetalPart(parent, type, name, localPos, localScale, Vector3.zero);

    static void MetalPart(Transform parent, PrimitiveType type, string name, Vector3 localPos, Vector3 localScale, Vector3 localEuler)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        var col = go.GetComponent<Collider>(); if (col) Object.Destroy(col);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(localEuler);
        go.transform.localScale = localScale;
        var sh = Shader.Find("Standard");
        if (sh != null)
        {
            var m = new Material(sh) { color = new Color(0.24f, 0.26f, 0.30f) };
            m.SetFloat("_Metallic", 0.9f);
            m.SetFloat("_Glossiness", 0.6f);
            go.GetComponent<Renderer>().material = m;
        }
    }

    /// <summary>Un pezzo METALLICO a forma libera (mesh a rivoluzione da un profilo): per il TORSO con le spalle.</summary>
    static void MetalLathe(Transform parent, string name, Vector2[] profile)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = ProcMesh.RevolveY(profile, 48, name);
        var mr = go.AddComponent<MeshRenderer>();
        var sh = Shader.Find("Standard");
        if (sh != null)
        {
            var m = new Material(sh) { color = new Color(0.24f, 0.26f, 0.30f) };
            m.SetFloat("_Metallic", 0.9f);
            m.SetFloat("_Glossiness", 0.6f);
            mr.material = m;
        }
    }

    /// <summary>Una pallina LUMINOSA (Unlit, sopravvive al build) figlia del parent: ugello di motore, dettaglio glow.</summary>
    static void GlowBall(Transform parent, string name, Vector3 localPos, float diameter, Color col)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        var c = go.GetComponent<Collider>(); if (c) Object.Destroy(c);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one * diameter;
        SetColor(go, col, emissive: true);
    }

    static void SetColor(GameObject go, Color c, bool emissive = false)
    {
        var r = go.GetComponent<Renderer>();
        if (!r) return;
        if (emissive)
        {
            // oggetti "che brillano" (tuta-beacon): disco pieno, NON ombreggiato. Unlit/Color evita lo stripping
            // della variante _EMISSION dello Standard in build.
            var us = Shader.Find("Unlit/Color");
            if (us != null) { r.material = new Material(us) { color = c }; return; }
        }
        var sh = Shader.Find("Standard");
        if (sh == null)
        {
            Debug.LogError("Shader 'Standard' non trovato nella build: aggiungilo agli Always Included Shaders.");
            return;
        }
        r.material = new Material(sh) { color = c };
    }
}

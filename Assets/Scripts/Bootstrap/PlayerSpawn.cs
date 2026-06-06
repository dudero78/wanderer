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

        // TUTA = OMINO stilizzato METALLICO LUMINOSO: TORSO + 2 GAMBE (3 cilindri acciaio) + TESTA (sfera che brilla).
        // Parent vuoto (lo SuitPickup lo fa ondeggiare/ruotare); i pezzi sono figli, costruiti col +Y in alto → stanno
        // dritti lungo axisUp (radiale). La testa glow + la luce point danno il "luminoso".
        var suitGo = new GameObject("Tuta");
        MetalPart(suitGo.transform, PrimitiveType.Cylinder, "Torso", new Vector3(0f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0.5f));
        MetalPart(suitGo.transform, PrimitiveType.Cylinder, "GambaSx", new Vector3(-0.17f, -0.45f, 0f), new Vector3(0.17f, 0.45f, 0.17f));
        MetalPart(suitGo.transform, PrimitiveType.Cylinder, "GambaDx", new Vector3(0.17f, -0.45f, 0f), new Vector3(0.17f, 0.45f, 0.17f));
        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Testa";
        var headCol = head.GetComponent<Collider>(); if (headCol) Object.Destroy(headCol);
        head.transform.SetParent(suitGo.transform, false);
        head.transform.localPosition = new Vector3(0f, 1.28f, 0f);
        head.transform.localScale = Vector3.one * 0.55f;
        SetColor(head, new Color(0.4f, 0.95f, 1f), emissive: true);   // testa che BRILLA (Unlit, sopravvive al build)

        var glowGo = new GameObject("Glow");
        glowGo.transform.SetParent(suitGo.transform, false);
        glowGo.transform.localPosition = new Vector3(0f, 1.28f, 0f);   // alla testa
        var glow = glowGo.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(0.3f, 0.95f, 1f);
        glow.range = 6f;
        glow.intensity = 1.4f;

        var pickup = suitGo.AddComponent<SuitPickup>();
        pickup.surfaceClearance = 0.95f;   // i piedi (gambe a y≈−0.9) sfiorano il suolo
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
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        var col = go.GetComponent<Collider>(); if (col) Object.Destroy(col);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
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

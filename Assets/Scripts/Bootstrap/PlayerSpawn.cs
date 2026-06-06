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
        var playerMr = playerGo.GetComponent<MeshRenderer>(); if (playerMr) playerMr.enabled = false;   // niente capsula: il modello è l'avatar (omino)

        // MODELLO del giocatore via il sistema intercambiabile (ModelHost + CharacterModel): omino del COLORE del
        // giocatore, su un LAYER NOMINATO nascosto alla SUA camera (prima persona pulita), visibile alle altre (sonda).
        // NUDO = testa-sfera, magro, niente bombole. Raccolta la tuta → modello col CASCO + zaino (stesso colore).
        int avatarLayer = LayerMask.NameToLayer(ModelHost.AvatarLayer);
        if (avatarLayer < 0) avatarLayer = 31;   // fallback se il layer nominato non c'è (EnsureLayers lo crea in editor)
        var avatarHostGo = new GameObject("Avatar");
        avatarHostGo.transform.SetParent(playerGo.transform, false);   // piedi dell'omino (y≈−1) ai piedi del giocatore (capsula r=1)
        var avatarHost = avatarHostGo.AddComponent<ModelHost>();
        avatarHost.HideLayer = avatarLayer;
        var playerColor = new Color(0.85f, 0.35f, 0.3f);
        var accent = new Color(0.4f, 0.95f, 1f);
        var nakedModel = ProceduralOminoModel.Create(playerColor, false, OminoBuilder.HeadKind.GlowSphere, accent, 0.80f, 0.78f, false);
        var suitedModel = ProceduralOminoModel.Create(playerColor, false, OminoBuilder.HeadKind.Helmet, accent, 1.0f, 1.30f, true);
        playerGo.AddComponent<PlayerAvatar>().Init(avatarHost, nakedModel, suitedModel);

        // Tuta-jetpack: faro-pilastro, ~8 m DAVANTI al giocatore (= verso il sole, già tangente al suolo), sul terreno.
        Vector3 suitDir = (playerSpawnPos + sunDir * 8f - bodyGo.transform.position).normalized;
        Vector3 suitGround = bodyGo.transform.position + suitDir * terrain.SampleHeight(suitDir);

        // TUTA = OMINO metallico col CASCO, via il sistema intercambiabile (ModelHost figlio, separato dalla luce
        // glow). Parent suitGo: lo SuitPickup lo fa ondeggiare e lo orienta verso il giocatore. +Z davanti, bombole dietro.
        var suitGo = new GameObject("Tuta");
        var suitModelGo = new GameObject("Model");
        suitModelGo.transform.SetParent(suitGo.transform, false);
        suitModelGo.AddComponent<ModelHost>().SetModel(ProceduralOminoModel.Create(
            new Color(0.55f, 0.58f, 0.62f), true, OminoBuilder.HeadKind.Helmet, new Color(0.4f, 0.95f, 1f),
            1.0f, 1.30f, true));   // tuta pesante: metallica, arti cicciotti + zaino-bombole

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
        cam.cullingMask &= ~(1 << avatarLayer);   // la camera del giocatore NON vede il proprio corpo (prima persona pulita)
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

}

using UnityEngine;

/// <summary>
/// Comandi della SONDA (vedi <see cref="Probe"/>): LANCIO dal muso della camera, VISTA in prima persona attraverso la
/// sua camera (grandangolo + free-look col mouse, per la foto), RICHIAMO, FOTO. Una sola sonda viva per volta. Un
/// TRACKER nell'HUD segue la sonda (marker + distanza + freccia ai bordi), come il reticolo del corpo selezionato.
///   P = lancia · V = guarda attraverso la sonda · K = richiama · G (in vista) = foto
/// </summary>
public class ProbeController : MonoBehaviour
{
    public KeyCode launchKey = KeyCode.P;
    public KeyCode viewKey = KeyCode.V;
    public KeyCode recallKey = KeyCode.K;
    public KeyCode photoKey = KeyCode.G;
    public float launchSpeed = 120f;   // spinta iniziale lungo il muso (sommata alla velocità del giocatore)
    public float lookSensitivity = 2.2f;

    Camera playerCam; Transform camT; PlanetWalker walker; SolarSystem solar; RenderScaler playerScaler;
    Probe probe; bool viewing; float camYaw, camPitch;
    string lastPhoto; float photoFlash;
    GUIStyle center;
    Texture2D dot;

    public Probe Probe => probe;   // il tracker HUD (RouteIndicator) la segue

    public void Init(Camera cam, Transform camTransform, PlanetWalker w, SolarSystem s)
    {
        playerCam = cam; camT = camTransform; walker = w; solar = s;
        playerScaler = cam != null ? cam.GetComponent<RenderScaler>() : null;
        probe = Probe.Spawn(s);
    }

    void Update()
    {
        if (probe == null) return;
        bool flying = probe.gameObject.activeSelf;
        bool freeToAct = walker == null || walker.ControlsActive;   // non in mappa/menu

        if (Input.GetKeyDown(launchKey) && freeToAct && !viewing) LaunchFromCamera();
        if (Input.GetKeyDown(viewKey) && flying && (freeToAct || viewing)) ToggleView();
        if (Input.GetKeyDown(recallKey)) RecallProbe();
        if (viewing)
        {
            if (Input.GetKeyDown(photoKey)) TakePhoto();
            FreeLook();   // guardati intorno dalla sonda col mouse
        }
        if (photoFlash > 0f) photoFlash -= Time.deltaTime;
    }

    void LaunchFromCamera()
    {
        if (camT == null) return;
        Vector3 pos = camT.position + camT.forward * 2f;
        Vector3 baseVel = walker != null ? walker.Velocity : Vector3.zero;   // eredita lo slancio del giocatore
        probe.Launch(pos, baseVel + camT.forward * launchSpeed, camT.forward);
    }

    void ToggleView()
    {
        viewing = !viewing;
        if (viewing)
        {
            if (playerScaler != null) playerScaler.enabled = false;   // il presentatore della RT coprirebbe la camera-sonda
            if (playerCam != null) playerCam.enabled = false;
            if (probe.Cam != null) probe.Cam.enabled = true;
            if (probe.Visual != null) probe.Visual.SetActive(false);   // prima persona: non vedi la tua stessa sonda
            probe.FreezeOrient = true;                                  // frame fermo → il free-look non combatte
            camYaw = 0f; camPitch = 0f;
            if (walker != null) walker.ControlsActive = false;                    // congela il giocatore (l'input va alla sonda)
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        }
        else ExitView();
    }

    void ExitView()
    {
        viewing = false;
        if (probe != null)
        {
            if (probe.Cam != null) probe.Cam.enabled = false;
            if (probe.Visual != null) probe.Visual.SetActive(true);
            probe.FreezeOrient = false;
        }
        if (playerCam != null) playerCam.enabled = true;
        if (playerScaler != null) playerScaler.enabled = true;
        if (walker != null) walker.ControlsActive = true;
    }

    void FreeLook()
    {
        if (probe == null || probe.Cam == null) return;
        camYaw += Input.GetAxisRaw("Mouse X") * lookSensitivity;
        camPitch = Mathf.Clamp(camPitch - Input.GetAxisRaw("Mouse Y") * lookSensitivity, -85f, 85f);
        probe.Cam.transform.localRotation = Quaternion.Euler(camPitch, camYaw, 0f);   // relativo al frame (fermo) della sonda
    }

    void RecallProbe()
    {
        if (viewing) ExitView();
        if (probe != null) probe.Recall();
    }

    void TakePhoto()
    {
        lastPhoto = $"{Application.persistentDataPath}/sonda_{Time.frameCount}.png";
        ScreenCapture.CaptureScreenshot(lastPhoto);
        Debug.Log($"[sonda] foto salvata: {lastPhoto}");   // così la trovi al volo (cartella persistentDataPath, nascosta)
        photoFlash = 0.8f;
    }

    // La VISTA dalla sonda (flash foto + suggerimenti). Il TRACKER HUD della sonda lo disegna RouteIndicator (triangolo
    // ambra), riusando la stessa logica robusta del reticolo del corpo selezionato.
    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || !viewing) return;
        float ui = Mathf.Max(1f, Screen.height / 1080f);
        if (center == null) center = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        center.fontSize = Mathf.RoundToInt(22f * ui);
        if (dot == null) { dot = new Texture2D(1, 1); dot.SetPixel(0, 0, Color.white); dot.Apply(); }

        // FLASH bianco + conferma foto AL CENTRO (non si sovrappone all'HUD in alto a sinistra)
        if (photoFlash > 0f)
        {
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(photoFlash) * 0.6f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), dot);
            GUI.color = Color.white;
            GUI.Label(new Rect(0, Screen.height * 0.5f - 20f * ui, Screen.width, 40f * ui), "📸 FOTO SALVATA", center);
        }
        // suggerimenti in BASSO al centro (lontano dall'HUD)
        GUI.Label(new Rect(0, Screen.height - 40f * ui, Screen.width, 24f * ui),
            "VISTA SONDA   ·   mouse = guarda   ·   G = foto   ·   V = esci   ·   K = richiama", center);
    }
}

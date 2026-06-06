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
    GUIStyle style, center;
    Texture2D dot, edge;

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
            if (probe.BodyRenderer != null) probe.BodyRenderer.enabled = false;   // prima persona: non vedi la tua stessa sonda
            probe.FreezeOrient = true;                                            // frame fermo → il free-look non combatte
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
            if (probe.BodyRenderer != null) probe.BodyRenderer.enabled = true;
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
        photoFlash = 0.8f;
    }

    void EnsureTextures()
    {
        if (dot != null) return;
        dot = new Texture2D(1, 1); dot.SetPixel(0, 0, Color.white); dot.Apply();
        edge = dot;
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || probe == null) return;
        float ui = Mathf.Max(1f, Screen.height / 1080f);
        if (style == null) style = new GUIStyle(GUI.skin.label) { normal = { textColor = Color.white } };
        if (center == null) center = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        style.fontSize = Mathf.RoundToInt(14f * ui);
        center.fontSize = Mathf.RoundToInt(22f * ui);
        EnsureTextures();

        if (viewing)
        {
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
            return;
        }

        // TRACKER HUD della sonda (quando vola e NON la stai guardando): marker + distanza, freccia ai bordi se fuori vista.
        if (probe.gameObject.activeSelf && playerCam != null && camT != null)
        {
            Vector3 wp = probe.transform.position;
            Vector3 sp = playerCam.WorldToScreenPoint(wp);
            float dist = Vector3.Distance(camT.position, wp);
            string label = probe.Landed ? $"SONDA · posata · {dist:F0} m" : $"SONDA · {dist:F0} m";
            var col = probe.Landed ? new Color(0.6f, 1f, 0.7f) : new Color(0.6f, 0.9f, 1f);
            GUI.color = col;
            if (sp.z > 0f && sp.x >= 0f && sp.x <= Screen.width && sp.y >= 0f && sp.y <= Screen.height)
            {
                float y = Screen.height - sp.y;
                GUI.DrawTexture(new Rect(sp.x - 5f * ui, y - 5f * ui, 10f * ui, 10f * ui), dot);   // marker
                style.normal.textColor = col;
                GUI.Label(new Rect(sp.x + 10f * ui, y - 10f * ui, 260f * ui, 20f * ui), label, style);
            }
            else
            {
                // fuori vista (o dietro): freccia clampata al bordo verso la sonda
                Vector3 dir = sp.z > 0f ? sp : new Vector3(Screen.width - sp.x, Screen.height - sp.y, 1f);
                Vector2 c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                Vector2 d2 = new Vector2(dir.x, dir.y) - c;
                if (d2.sqrMagnitude < 1f) d2 = Vector2.up;
                d2.Normalize();
                Vector2 e = c + d2 * (Mathf.Min(Screen.width, Screen.height) * 0.42f);
                GUI.DrawTexture(new Rect(e.x - 7f * ui, (Screen.height - e.y) - 7f * ui, 14f * ui, 14f * ui), dot);
                style.normal.textColor = col;
                GUI.Label(new Rect(e.x - 130f * ui, (Screen.height - e.y) + 8f * ui, 260f * ui, 20f * ui), label, style);
            }
            GUI.color = Color.white;
            style.normal.textColor = Color.white;
        }
    }
}

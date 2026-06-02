using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Modalità mappa stile Outer Wilds. Premendo M la camera fa uno zoom-out veloce dal personaggio
/// fino a inquadrare l'intero sistema in prospettiva, con le ORBITE dei corpi disegnate. Si clicca
/// un corpo per SELEZIONARLO (base per la futura navigazione "vai verso"). Premi M (o Esc) per tornare.
///
/// Usa una camera dedicata: in mappa la camera del giocatore viene spenta e accesa quella della mappa
/// (entrambe taggate MainCamera, una sola attiva → Camera.main resta valido). I comandi del walker
/// sono congelati (ControlsActive=false) ma gravità e suolo restano attivi: il giocatore aspetta a terra.
/// </summary>
public class MapMode : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.M;
    [Tooltip("Durata dello zoom-out/in in secondi.")]
    public float transitionTime = 0.7f;
    [Tooltip("Dimensione dei marker come frazione della distanza camera (≈ costante a schermo).")]
    public float markerScreenSize = 0.02f;

    Camera playerCam;
    Transform playerCamT;
    PlanetWalker walker;
    SolarSystem solar;
    RenderScaler playerScaler;   // la camera giocatore renderizza in una RT presentata a parte: va spento in mappa

    Camera mapCam;
    enum State { Off, Entering, On, Exiting }
    State state = State.Off;
    float t;
    Vector3 fromPos; Quaternion fromRot;

    readonly List<GameObject> markers = new List<GameObject>();
    readonly Dictionary<GameObject, CelestialBody> markerBody = new Dictionary<GameObject, CelestialBody>();
    readonly List<LineRenderer> orbits = new List<LineRenderer>();
    readonly List<CelestialBody> orbitBody = new List<CelestialBody>();
    CelestialBody selected;

    public CelestialBody Selected => selected;
    public bool Active => state != State.Off;

    public void Init(Camera playerCamera, PlanetWalker w, SolarSystem s)
    {
        playerCam = playerCamera;
        playerCamT = playerCamera.transform;
        walker = w;
        solar = s;
        playerScaler = playerCamera.GetComponent<RenderScaler>();

        var go = new GameObject("MapCamera");
        go.tag = "MainCamera";
        mapCam = go.AddComponent<Camera>();
        mapCam.nearClipPlane = 1f;
        mapCam.farClipPlane = 500000f;
        mapCam.clearFlags = CameraClearFlags.SolidColor;
        mapCam.backgroundColor = new Color(0.01f, 0.01f, 0.03f);
        mapCam.fieldOfView = 55f;
        mapCam.enabled = false;

        BuildVisuals();
        ShowVisuals(false);
    }

    void BuildVisuals()
    {
        var markerShader = Shader.Find("Unlit/Color");
        var lineShader = Shader.Find("Sprites/Default");
        float orbitWidth = SystemRadius() * 0.004f;

        for (int i = 0; i < solar.Bodies.Count; i++)
        {
            var b = solar.Bodies[i];
            if (b == null) continue;
            bool isStar = b.Orbit == null;
            Color col = isStar ? new Color(1f, 0.85f, 0.45f) : new Color(0.7f, 0.78f, 0.9f);

            // marker cliccabile (sfera unlit), posizionato e scalato ogni frame
            var mk = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mk.name = "Marker_" + b.gameObject.name;
            if (markerShader != null)
            {
                var m = new Material(markerShader);
                m.SetColor("_Color", col);
                mk.GetComponent<MeshRenderer>().sharedMaterial = m;
            }
            mk.transform.SetParent(transform, false);
            markers.Add(mk);
            markerBody[mk] = b;

            // orbita (solo per i corpi che orbitano)
            if (b.Orbit != null && b.Parent != null && lineShader != null)
            {
                var lgo = new GameObject("Orbit_" + b.gameObject.name);
                lgo.transform.SetParent(transform, false);
                var lr = lgo.AddComponent<LineRenderer>();
                lr.material = new Material(lineShader);
                lr.useWorldSpace = true;
                lr.loop = true;
                lr.widthMultiplier = orbitWidth;
                lr.numCapVertices = 2;
                int n = 96;
                lr.positionCount = n;
                var faint = new Color(col.r, col.g, col.b, 0.5f);
                lr.startColor = faint; lr.endColor = faint;
                orbits.Add(lr);
                orbitBody.Add(b);
            }
        }
    }

    // Centro del sistema = la stella (corpo senza orbita); fallback: media dei corpi.
    Vector3 SystemCenter()
    {
        for (int i = 0; i < solar.Bodies.Count; i++)
            if (solar.Bodies[i] != null && solar.Bodies[i].Orbit == null)
                return solar.Bodies[i].transform.position;
        Vector3 sum = Vector3.zero; int c = 0;
        for (int i = 0; i < solar.Bodies.Count; i++)
            if (solar.Bodies[i] != null) { sum += solar.Bodies[i].transform.position; c++; }
        return c > 0 ? sum / c : Vector3.zero;
    }

    // Raggio del sistema = l'orbita più esterna (apoapsis); così l'inquadratura contiene tutto.
    float SystemRadius()
    {
        float r = 3000f;
        for (int i = 0; i < solar.Bodies.Count; i++)
        {
            var b = solar.Bodies[i];
            if (b != null && b.Orbit != null)
                r = Mathf.Max(r, (float)(b.Orbit.SemiMajorAxis * (1.0 + b.Orbit.Eccentricity)));
        }
        return r;
    }

    void ComputeOverview(out Vector3 pos, out Quaternion rot)
    {
        Vector3 center = SystemCenter();
        float radius = SystemRadius();
        float dist = radius / Mathf.Tan(mapCam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.25f;
        Vector3 dir = (Vector3.up * 0.55f + Vector3.back * 0.83f).normalized;   // angolo prospettico (dall'alto/dietro)
        pos = center + dir * dist;
        rot = Quaternion.LookRotation(center - pos, Vector3.up);
    }

    void Update()
    {
        if (state == State.Off)
        {
            if (walker != null && Input.GetKeyDown(toggleKey)) EnterMap();
            return;
        }

        if (state == State.On && (Input.GetKeyDown(toggleKey) || Input.GetKeyDown(KeyCode.Escape)))
            ExitMap();

        if (state == State.Entering || state == State.Exiting)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, transitionTime);
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            ComputeOverview(out var ovPos, out var ovRot);
            Vector3 tgtPos = state == State.Entering ? ovPos : playerCamT.position;
            Quaternion tgtRot = state == State.Entering ? ovRot : playerCamT.rotation;
            mapCam.transform.SetPositionAndRotation(Vector3.Lerp(fromPos, tgtPos, s), Quaternion.Slerp(fromRot, tgtRot, s));
            if (t >= 1f)
            {
                if (state == State.Entering) state = State.On;
                else FinishExit();
            }
        }
        else if (state == State.On)
        {
            ComputeOverview(out var ovPos, out var ovRot);
            mapCam.transform.SetPositionAndRotation(ovPos, ovRot);
            HandleClick();
        }

        if (state != State.Off) UpdateVisuals();   // NON dopo FinishExit (lasciava le orbite a schermo)
    }

    void EnterMap()
    {
        fromPos = playerCamT.position; fromRot = playerCamT.rotation;
        mapCam.transform.SetPositionAndRotation(fromPos, fromRot);
        // spegni il RenderScaler: renderizza la camera giocatore in una RT che una camera
        // presentatrice stende a schermo ogni frame → senza spegnerlo coprirebbe la camera-mappa.
        if (playerScaler != null) playerScaler.enabled = false;
        playerCam.enabled = false;
        mapCam.enabled = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (walker != null) walker.ControlsActive = false;
        ShowVisuals(true);
        state = State.Entering; t = 0f;
    }

    void ExitMap()
    {
        fromPos = mapCam.transform.position; fromRot = mapCam.transform.rotation;
        state = State.Exiting; t = 0f;
    }

    void FinishExit()
    {
        mapCam.enabled = false;
        playerCam.enabled = true;
        if (playerScaler != null) playerScaler.enabled = true;   // ripristina il render scalato
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        if (walker != null) walker.ControlsActive = true;
        ShowVisuals(false);
        state = State.Off;
    }

    void HandleClick()
    {
        if (!Input.GetMouseButtonDown(0)) return;
        var ray = mapCam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out var hit, 1e7f) && markerBody.TryGetValue(hit.collider.gameObject, out var b))
        {
            selected = b;
            solar.Destination = b;   // in volo l'origine si ancora a lei → ferma e centrata, il freno X la sincronizza
        }
    }

    void UpdateVisuals()
    {
        Vector3 camPos = mapCam.transform.position;
        for (int i = 0; i < markers.Count; i++)
        {
            var mk = markers[i];
            var b = markerBody[mk];
            if (b == null) { mk.SetActive(false); continue; }
            mk.transform.position = b.transform.position;
            float sz = Vector3.Distance(camPos, b.transform.position) * markerScreenSize;
            if (b.Orbit == null) sz *= 1.6f;            // la stella un po' più grande
            if (b == selected) sz *= 1.5f;              // evidenzia il selezionato
            mk.transform.localScale = Vector3.one * sz;
        }

        for (int i = 0; i < orbits.Count; i++)
        {
            var b = orbitBody[i];
            var lr = orbits[i];
            if (b == null || b.Orbit == null || b.Parent == null) { lr.enabled = false; continue; }
            lr.enabled = true;
            Vector3 parentScene = b.Parent.transform.position;
            int n = lr.positionCount;
            for (int k = 0; k < n; k++)
            {
                double tt = b.Orbit.Period * (k / (double)n);
                lr.SetPosition(k, parentScene + b.Orbit.GetRelativePosition(tt).ToVector3());
            }
        }
    }

    void ShowVisuals(bool on)
    {
        for (int i = 0; i < markers.Count; i++) markers[i].SetActive(on);
        for (int i = 0; i < orbits.Count; i++) orbits[i].enabled = on;
    }

    void OnGUI()
    {
        GUI.color = Color.white;
        if (state != State.Off)
            GUI.Label(new Rect(20, Screen.height - 60, 640, 24),
                selected != null ? "Selezionato: " + selected.gameObject.name + "   ·   M / Esc per uscire"
                                 : "Clicca un corpo per selezionarlo   ·   M / Esc per uscire");
    }
}

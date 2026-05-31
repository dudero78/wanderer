using UnityEngine;

/// <summary>
/// HUD di debug disegnato via IMGUI (nessun canvas da configurare). Mostra il
/// punto chiave dell'architettura: la posizione del player nello spazio scena
/// resta piccola mentre SceneOrigin — la tua distanza reale nell'universo —
/// cresce senza limiti. È la floating origin che preserva la precisione.
/// </summary>
public class DebugHud : MonoBehaviour
{
    Transform player;
    CelestialBody planet;
    CelestialBody star;
    SolarSystem solar;
    PlanetWalker walker;
    Transform suit;
    Transform cam;
    GUIStyle style;
    GUIStyle banner;

    public void Init(Transform p, CelestialBody pl, CelestialBody st, SolarSystem s, PlanetWalker w, Transform suit, Transform cam)
    {
        player = p; planet = pl; star = st; solar = s; walker = w; this.suit = suit; this.cam = cam;
    }

    void OnGUI()
    {
        if (!player || planet == null) return;
        if (style == null)
            style = new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = Color.white } };

        Vector3 toCenter = player.position - planet.transform.position;
        float r = toCenter.magnitude;
        float surface = (float)planet.Radius;
        if (r > 0.001f && planet.TryGetComponent<PlanetTerrain>(out var terr))
            surface = terr.SampleHeight(toCenter / r);
        double sceneOrigin = FloatingOrigin.SceneOrigin.magnitude;
        float starDist = star != null ? star.transform.position.magnitude : 0f;

        bool jetpack = walker != null && walker.HasJetpack;
        string controls = jetpack
            ? "A terra: WASD cammina.  In volo: WASD spinge · Space sale · Shift scende.  Mouse guarda · Esc cursore"
            : "WASD muovi  ·  Space salta  ·  Mouse guarda  ·  Esc libera il cursore";

        string suitLine;
        if (suit != null && cam != null)
        {
            Vector3 off = suit.position - cam.position;
            float fwd = Vector3.Dot(off, cam.forward);
            float rgt = Vector3.Dot(off, cam.right);
            float upp = Vector3.Dot(off, cam.up);
            suitLine = $"TUTA  dist {off.magnitude:F0} m  ·  avanti {fwd:F0}  destra {rgt:F0}  alto {upp:F0}";
        }
        else suitLine = "TUTA: raccolta";

        string t =
            $"Tempo simulazione  : {solar.SimTime:F0} s   (TimeScale {solar.TimeScale})\n" +
            $"Altitudine         : {r - surface:F1} m\n" +
            $"Player |pos scena| : {player.position.magnitude:F1}   <- resta piccolo: precisione ok\n" +
            $"SceneOrigin |pos|  : {sceneOrigin:F0}   <- distanza reale nell'universo: cresce\n" +
            $"Stella |pos scena| : {starDist:F0}\n" +
            $"{suitLine}\n" +
            $"\n{controls}";

        GUI.Label(new Rect(14, 12, 820, 240), t, style);

        // banner alla raccolta della tuta
        if (jetpack && Time.time - walker.EquipTime < 5f)
        {
            if (banner == null)
                banner = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 26,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.4f, 0.95f, 1f) }
                };
            GUI.Label(new Rect(0, Screen.height * 0.32f, Screen.width, 60),
                "TUTA EQUIPAGGIATA — tieni premuto Space per volare", banner);
        }
    }
}

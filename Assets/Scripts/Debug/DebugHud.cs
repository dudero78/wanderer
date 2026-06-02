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
    Flashlight flash;
    Transform suit;
    Transform cam;
    GUIStyle style;
    GUIStyle banner;
    string cachedText;
    float nextRebuild;

    public void Init(Transform p, CelestialBody pl, CelestialBody st, SolarSystem s, PlanetWalker w, Flashlight fl, Transform suit, Transform cam)
    {
        player = p; planet = pl; star = st; solar = s; walker = w; flash = fl; this.suit = suit; this.cam = cam;
    }

    void OnGUI()
    {
        // SOLO sul Repaint: senza questo guard OnGUI gira anche sul Layout (e altri eventi) → la stringa
        // si ricostruisce 2-4 volte per frame, allocando. È un overlay di debug: ricostruirla a ~10Hz basta.
        if (Event.current.type != EventType.Repaint) return;
        if (!player || planet == null) return;
        if (style == null)
            style = new GUIStyle(GUI.skin.label) { normal = { textColor = Color.white } };

        // scala l'HUD con la risoluzione: a pixel fissi, su uno schermo Retina/4K il testo è minuscolo.
        // Riferimento 1080p; non scendere mai sotto 1× (schermi piccoli restano leggibili).
        float ui = Mathf.Max(1f, Screen.height / 1080f);
        style.fontSize = Mathf.RoundToInt(15f * ui);

        if (cachedText == null || Time.unscaledTime >= nextRebuild)
        {
            cachedText = BuildText();
            nextRebuild = Time.unscaledTime + 0.1f;
        }
        GUI.Label(new Rect(14f * ui, 12f * ui, 820f * ui, 240f * ui), cachedText, style);

        if (banner == null)
            banner = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.4f, 0.95f, 1f) }
            };

        // banner alla raccolta della tuta (valutato ogni repaint: è solo un confronto di tempo, niente alloc)
        if (walker != null && walker.HasJetpack && Time.time - walker.EquipTime < 5f)
        {
            banner.fontSize = Mathf.RoundToInt(26f * ui);
            GUI.Label(new Rect(0, Screen.height * 0.32f, Screen.width, 60f * ui),
                "TUTA EQUIPAGGIATA — tieni premuto Space per volare", banner);
        }

        // arrivato a destinazione: l'autopilota tiene la stazione, avvisa che un comando riprende il controllo.
        if (walker != null && walker.AutoHolding)
        {
            banner.fontSize = Mathf.RoundToInt(22f * ui);
            GUI.Label(new Rect(0, Screen.height * 0.30f, Screen.width, 50f * ui),
                "ARRIVATO — in stazione · un comando qualsiasi per riprendere il controllo", banner);
        }
    }

    // Costruisce il testo dell'HUD. Chiamato a bassa cadenza (~10Hz), non per frame.
    string BuildText()
    {
        // Due riferimenti distinti, perché rispondono a due domande diverse:
        //  - GRAVITÀ (il corpo più vicino): per l'ALTITUDINE — "quanto sono alto sul suolo che potrei
        //    toccare". Finché sei nell'influenza di un pianeta resta lui.
        //  - ANCORATO (fermo in scena, = destinazione in viaggio): per VELOCITÀ e RADIALE — "quanto
        //    velocemente mi avvicino al corpo verso cui vado".
        var grav = walker != null && walker.GravityBody != null ? walker.GravityBody : planet;
        var refBody = solar != null && solar.Reference != null ? solar.Reference : grav;
        float altitude = walker != null ? walker.Altitude : 0f;

        Vector3 toRef = player.position - refBody.transform.position;
        float rRef = toRef.magnitude;
        Vector3 up = rRef > 0.001f ? toRef / rRef : Vector3.up;   // verso "fuori" dal riferimento ancorato
        double sceneOrigin = FloatingOrigin.SceneOrigin.magnitude;
        float starDist = star != null ? star.transform.position.magnitude : 0f;

        bool jetpack = walker != null && walker.HasJetpack;
        string controls = jetpack
            ? "A terra: WASD cammina.  In volo: WASD spinge · Space sale · Shift scende · Q/E rollio · N cambia volo · X freno · T autopilota.  F torcia · M mappa · O orbite · à impostazioni · Mouse guarda · Esc cursore"
            : "WASD muovi  ·  Space salta  ·  Mouse guarda  ·  Esc libera il cursore";

        // velocità/radiale relative al RIFERIMENTO: "ti avvicini" parla del corpo verso cui viaggi.
        float spd = walker != null ? walker.Speed : 0f;
        float rad = walker != null ? Vector3.Dot(walker.Velocity, up) : 0f;   // >0 ti allontani dal riferimento
        float tan = Mathf.Sqrt(Mathf.Max(0f, spd * spd - rad * rad));   // velocità di traverso = orbitale
        string radWord = rad > 0.5f ? "ti allontani" : rad < -0.5f ? "ti AVVICINI" : "stazionario";
        string model = walker != null && walker.IsNewtonian ? $"NEWTONIANO (spinta {walker.ThrustSpool01 * 100f:F0}%)" : $"Crociera ({(walker != null ? walker.Boost01 * 100f : 0f):F0}%)";
        string brake = walker != null && walker.Braking ? "   ·   FRENO" : "";
        string auto = walker != null && walker.Autopilot ? (walker.AutoHolding ? "   ·   AUTOPILOTA (IN STAZIONE)" : "   ·   AUTOPILOTA") : "";
        string torch = flash != null && flash.IsOn ? "ACCESA" : "spenta";
        string flightLine = jetpack
            ? $"Velocità           : {spd:F0} m/s   ·   radiale {rad:+0;-0} ({radWord})   ·   tangenz. {tan:F0} (orbita)   ·   Volo: {model}{brake}{auto}\n" +
              $"Torcia             : {torch}\n"
            : "";

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

        // distanza dal corpo SELEZIONATO sulla mappa (può essere uno qualunque del sistema): riga a sé,
        // diversa dall'altitudine (che è il corpo di gravità sotto di te).
        string destLine = "";
        if (solar != null && solar.Destination != null)
        {
            float dd = (solar.Destination.transform.position - player.position).magnitude;
            string ds = dd > 1000f ? (dd / 1000f).ToString("F1") + " km" : dd.ToString("F0") + " m";
            destLine = $"Distanza ({solar.Destination.gameObject.name}) : {ds}\n";
        }

        return
            $"Tempo simulazione  : {solar.SimTime:F0} s   (TimeScale {solar.TimeScale})\n" +
            $"Altitudine ({grav.gameObject.name}) : {altitude:F1} m\n" +
            destLine +
            flightLine +
            $"Player |pos scena| : {player.position.magnitude:F1}   <- resta piccolo: precisione ok\n" +
            $"SceneOrigin |pos|  : {sceneOrigin:F0}   <- distanza reale nell'universo: cresce\n" +
            $"Stella |pos scena| : {starDist:F0}\n" +
            $"{suitLine}\n" +
            $"\n{controls}";
    }
}

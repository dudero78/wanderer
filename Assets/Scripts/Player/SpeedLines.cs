using UnityEngine;

/// <summary>
/// Effetto "salto a velocità della luce": righe radiali che sfrecciano verso l'esterno DALLA DIREZIONE DI MOTO
/// (non dal centro-vista: se giri la testa l'effetto resta ancorato a dove stai andando). Più vai veloce, più è
/// intenso e — oltre soglia — i raggi partono dal centro e diventano spessi/lunghi. Le righe vanno SEMPRE verso
/// l'esterno a ritmo costante: rallentando l'effetto si attenua (non si inverte). Solo overlay, zero fisica.
/// </summary>
public class SpeedLines : MonoBehaviour
{
    public PlanetWalker walker;
    public Camera cam;
    public float startSpeed = 13000f;    // sotto: niente effetto (parte da 13 km/s)
    public float fullSpeed = 30000f;     // pieno già a 30 km/s → ben visibile da subito sopra i 13 km/s
    public float thickSpeed = 80000f;    // oltre: raggi più spessi e lunghi (regime "iperveloce")

    const int N = 120;
    const float Rate = 0.85f;            // ritmo COSTANTE verso l'esterno (le righe non invertono mai)
    float[] ang, off;
    Texture2D tex;

    void Awake()
    {
        ang = new float[N]; off = new float[N];
        for (int i = 0; i < N; i++) { ang[i] = Random.value * Mathf.PI * 2f; off[i] = Random.value; }
        tex = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[64];
        for (int x = 0; x < 64; x++) { float u = x / 63f; px[x] = new Color32(255, 255, 255, (byte)(Mathf.Sin(u * Mathf.PI) * 255f)); }
        tex.SetPixels32(px); tex.Apply();
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || walker == null || cam == null) return;
        if (!cam.enabled) return;   // SOLO dalla camera del giocatore: guardando attraverso la sonda non si vede
        if (PauseMenu.Showing || SettingsMenu.AnyOpen) return;
        float speed = walker.Speed;   // magnitudo (≥0): rallentando scende, non diventa negativa
        float intensity = Mathf.InverseLerp(startSpeed, fullSpeed, speed);
        if (intensity <= 0.001f) return;
        float hot = Mathf.InverseLerp(fullSpeed, thickSpeed, speed);   // regime iperveloce (0..1)

        float ui = Mathf.Max(1f, Screen.height / 1080f);
        Vector2 screenC = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector3 vd = walker.Velocity.sqrMagnitude > 1f ? walker.Velocity.normalized : cam.transform.forward;
        float facing = Vector3.Dot(cam.transform.forward, vd);

        // UNA SOLA passata (com'era — pulita). AVANTI: le righe EMANANO dal prograde; INDIETRO: CONVERGONO verso il
        // retrograde (davanti a te). Vicino a 90° il punto di fuga schizza al bordo (buco nero): lo riporto verso il
        // CENTRO schermo man mano che |facing|→0 → le righe riempiono lo schermo e le due animazioni si toccano (no gap).
        bool inward = facing < 0f;
        Vector2 vanish = ProjectVanish(inward ? -vd : vd, screenC);
        float gap = 1f - Mathf.Clamp01(Mathf.Abs(facing) / 0.5f);   // 1 a 90°, 0 oltre ±30°
        Vector2 center = Vector2.Lerp(vanish, screenC, gap);

        float maxR = Mathf.Sqrt((float)Screen.width * Screen.width + (float)Screen.height * Screen.height);
        float innerFrac = Mathf.Lerp(0.5f, 0.04f, intensity);
        float baseLen = Mathf.Lerp(40f, 190f, intensity) + 120f * hot;
        float thick = (2.4f + 3.2f * hot) * ui;
        float t = Time.unscaledTime;
        Matrix4x4 m0 = GUI.matrix;
        Color prev = GUI.color;
        for (int i = 0; i < N; i++)
        {
            float phase = Mathf.Repeat(t * Rate + off[i], 1f);
            float r = inward ? Mathf.Lerp(maxR * 1.05f, maxR * innerFrac, phase)
                             : Mathf.Lerp(maxR * innerFrac, maxR * 1.05f, phase);
            float len = baseLen * (0.35f + phase) * ui;
            float a = intensity * Mathf.Sin(phase * Mathf.PI) * 0.9f;
            if (a <= 0.004f) continue;
            Vector2 dir = new Vector2(Mathf.Cos(ang[i]), Mathf.Sin(ang[i]));
            Vector2 p = center + dir * r;
            GUI.matrix = m0;
            GUIUtility.RotateAroundPivot(ang[i] * Mathf.Rad2Deg, p);
            GUI.color = new Color(0.82f, 0.9f, 1f, a);
            GUI.DrawTexture(new Rect(p.x - len * 0.5f, p.y - thick * 0.5f, len, thick), tex);
        }
        GUI.matrix = m0; GUI.color = prev;
    }

    // Proietta una direzione-mondo (da camPos) in coordinate GUI; se è dietro la camera, ripiega sul centro schermo.
    Vector2 ProjectVanish(Vector3 dir, Vector2 fallback)
    {
        Vector3 sp = cam.WorldToScreenPoint(cam.transform.position + dir * 1000f);
        if (sp.z <= 0f) return fallback;
        float sx = cam.pixelWidth > 0 ? sp.x * Screen.width / cam.pixelWidth : sp.x;
        float sy = cam.pixelHeight > 0 ? sp.y * Screen.height / cam.pixelHeight : sp.y;
        return new Vector2(sx, Screen.height - sy);
    }
}

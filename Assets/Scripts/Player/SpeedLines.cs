using UnityEngine;

/// <summary>
/// Effetto "salto a velocità della luce": righe radiali che sfrecciano dal centro verso i bordi quando vai molto
/// veloce (sopra <see cref="startSpeed"/>), sempre più intense fino a <see cref="fullSpeed"/>. Solo presentazione
/// (overlay IMGUI), zero impatto sulla fisica. Si nasconde coi menu aperti.
/// </summary>
public class SpeedLines : MonoBehaviour
{
    public PlanetWalker walker;
    public float startSpeed = 1500f;    // sotto: niente effetto (volo normale interplanetario lento)
    public float fullSpeed = 16000f;    // sopra: effetto pieno

    const int N = 110;
    float[] ang, off;
    Texture2D tex;

    void Awake()
    {
        ang = new float[N]; off = new float[N];
        for (int i = 0; i < N; i++) { ang[i] = Random.value * Mathf.PI * 2f; off[i] = Random.value; }
        // striscia con capi sfumati (alfa a campana lungo X) → le righe non hanno tagli netti
        tex = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[64];
        for (int x = 0; x < 64; x++) { float u = x / 63f; byte a = (byte)(Mathf.Sin(u * Mathf.PI) * 255f); px[x] = new Color32(255, 255, 255, a); }
        tex.SetPixels32(px); tex.Apply();
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || walker == null) return;
        if (PauseMenu.Showing || SettingsMenu.AnyOpen) return;
        float intensity = Mathf.InverseLerp(startSpeed, fullSpeed, walker.Speed);
        if (intensity <= 0.001f) return;

        float ui = Mathf.Max(1f, Screen.height / 1080f);
        float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
        float maxR = Mathf.Sqrt(cx * cx + cy * cy);
        float t = Time.unscaledTime;
        Matrix4x4 m0 = GUI.matrix;
        Color prev = GUI.color;

        for (int i = 0; i < N; i++)
        {
            float phase = Mathf.Repeat(t * (0.5f + intensity * 2.8f) + off[i], 1f);   // vola dal centro verso il bordo
            float r = Mathf.Lerp(maxR * 0.12f, maxR * 1.05f, phase);
            float len = Mathf.Lerp(24f, 200f, intensity) * (0.35f + phase) * ui;       // più lunga avvicinandosi al bordo
            float a = intensity * Mathf.Sin(phase * Mathf.PI) * 0.9f;                  // appare e svanisce ai capi
            if (a <= 0.004f) continue;

            Vector2 p = new Vector2(cx + Mathf.Cos(ang[i]) * r, cy + Mathf.Sin(ang[i]) * r);
            GUI.matrix = m0;
            GUIUtility.RotateAroundPivot(ang[i] * Mathf.Rad2Deg, p);   // allinea la riga lungo il raggio
            GUI.color = new Color(0.8f, 0.9f, 1f, a);
            GUI.DrawTexture(new Rect(p.x - len * 0.5f, p.y - 1.3f * ui, len, 2.6f * ui), tex);
        }
        GUI.matrix = m0; GUI.color = prev;
    }
}

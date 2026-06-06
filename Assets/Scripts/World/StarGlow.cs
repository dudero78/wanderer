using UnityEngine;

/// <summary>
/// Alone di luce attorno a una stella: un quad billboard additivo (shader Wanderer/StarGlow) figlio della stella, così
/// EREDITA la sua scala (incluso il clamp di <see cref="StarRenderClamp"/>) → l'alone resta proporzionato alla taglia
/// APPARENTE della stella a ogni distanza. Sempre rivolto alla camera. Solo resa, niente fisica.
/// </summary>
public class StarGlow : MonoBehaviour
{
    public float haloFactor = 3.2f;   // raggio dell'alone in multipli del raggio della stella
    public Color color = new Color(1f, 0.9f, 0.6f);
    public float strength = 0.9f;

    Transform quad;

    /// <summary>Imposta colore/taglia prima dello Start (es. dal colore della stella del sistema).</summary>
    public void Configure(Color c, float factor = 3.2f, float str = 0.9f) { color = c; haloFactor = factor; strength = str; }

    void Start()
    {
        var sh = Shader.Find("Wanderer/StarGlow");
        if (sh == null) { Debug.LogWarning("Shader 'Wanderer/StarGlow' non trovato (Always Included?)."); return; }
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "Glow";
        var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * haloFactor;   // eredita la scala-stella → alone ∝ taglia apparente
        var mr = go.GetComponent<MeshRenderer>();
        var m = new Material(sh); m.SetColor("_Color", color); m.SetFloat("_Strength", strength);
        mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        quad = go.transform;
    }

    void LateUpdate()
    {
        if (quad == null) return;
        var cam = Camera.main;
        if (cam == null) return;
        quad.rotation = Quaternion.LookRotation(quad.position - cam.transform.position, cam.transform.up);   // billboard
    }
}

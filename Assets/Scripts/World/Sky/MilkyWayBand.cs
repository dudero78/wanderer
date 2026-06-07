using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// La VIA LATTEA (+ tenue alone di stelle non risolte) come PASS A SCHERMO INTERO. Un quad che copre lo schermo;
/// la CPU calcola ogni frame i 4 raggi-di-vista d'angolo (dalla camera attiva) e lo shader <c>Wanderer/MilkyWay</c>
/// ricostruisce per pixel la direzione, la converte in coordinate equatoriali e campiona la texture equirettangolare
/// equatoriale (NASA Deep Star Map 2020). Niente sfera → nessun rischio di orientamento/culling. Additiva, fioca,
/// disegnata PRIMA dei punti-stella (queue Background+5). <see cref="SkyController"/> chiama <see cref="UpdateRays"/>.
/// </summary>
public class MilkyWayBand : MonoBehaviour
{
    Mesh mesh;
    readonly List<Vector4> rays = new List<Vector4>(4);

    public bool Build(Transform root, int layer)
    {
        var tex = Resources.Load<Texture2D>("Sky/MilkyWay");
        if (tex == null) { Debug.LogWarning("[sky] Resources/Sky/MilkyWay non trovata: niente Via Lattea."); return false; }
        tex.wrapModeU = TextureWrapMode.Repeat;
        tex.wrapModeV = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Trilinear;

        var sh = Shader.Find("Wanderer/MilkyWay");
        if (sh == null) { Debug.LogError("[sky] shader Wanderer/MilkyWay non trovato (Always Included?)."); return false; }

        // quad a schermo intero: vertici negli angoli del clip space (lo shader li emette diretti)
        mesh = new Mesh { name = "MilkyWayQuad" };
        mesh.SetVertices(new List<Vector3> {
            new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) });
        rays.AddRange(new[] { Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero });
        mesh.SetUVs(0, rays);
        mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0, false);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);   // mai cullato (lo shader ignora il transform)

        var go = new GameObject("MilkyWay");
        go.transform.SetParent(root, false);
        if (layer >= 0) go.layer = layer;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = new Material(sh) { mainTexture = tex };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return true;
    }

    /// <summary>Aggiorna i 4 raggi-di-vista d'angolo (in coordinate MONDO) dalla camera attiva → il fragment interpola
    /// e ottiene la direzione per pixel. Chiamato ogni frame da <see cref="SkyController"/>.</summary>
    public void UpdateRays(Camera cam)
    {
        if (mesh == null || cam == null) return;
        float tanY = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float tanX = tanY * cam.aspect;
        var rot = cam.transform.rotation;
        SetRay(0, rot * new Vector3(-tanX, -tanY, 1f));
        SetRay(1, rot * new Vector3( tanX, -tanY, 1f));
        SetRay(2, rot * new Vector3( tanX,  tanY, 1f));
        SetRay(3, rot * new Vector3(-tanX,  tanY, 1f));
        mesh.SetUVs(0, rays);
    }

    // NON normalizzare: i 4 raggi d'angolo (cx·tanX, cy·tanY, 1) ruotati sono LINEARI in (x,y) sullo schermo, quindi
    // l'interpolazione lineare nel quad dà la direzione ESATTA del pixel (la normalizzazione la fa il fragment). Se
    // normalizzassi qui, l'interpolazione fra angoli normalizzati sarebbe sbagliata → la banda "nuota" rispetto alle stelle.
    void SetRay(int i, Vector3 dir) { rays[i] = new Vector4(dir.x, dir.y, dir.z, 0f); }
}

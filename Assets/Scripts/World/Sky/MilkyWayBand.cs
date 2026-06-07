using UnityEngine;

/// <summary>
/// La VIA LATTEA (+ tenue alone di stelle non risolte) come texture equirettangolare EQUATORIALE mappata sull'interno
/// di una sfera. La sfera è generata orientando ogni vertice col frame equatoriale (<see cref="SkyData.EquatorialToGame"/>):
/// così la banda è ALLINEATA con le stelle del catalogo (stessa rotazione). Additiva e fioca, disegnata PRIMA dei punti
/// (queue Background+5 &lt; stelle +10) → le stelle nitide spiccano sopra il velo della galassia.
/// </summary>
public class MilkyWayBand : MonoBehaviour
{
    public const float Radius = 95f;   // poco dentro la sfera delle stelle (entrambe additive, l'ordine lo dà la queue)

    GameObject go;

    public bool Build(Transform root, int layer)
    {
        var tex = Resources.Load<Texture2D>("Sky/MilkyWay");
        if (tex == null) { Debug.LogWarning("[sky] Resources/Sky/MilkyWay non trovata: niente Via Lattea."); return false; }
        tex.wrapModeU = TextureWrapMode.Repeat;   // cucitura in Ascensione Retta
        tex.wrapModeV = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Trilinear;

        var sh = Shader.Find("Wanderer/MilkyWay");
        if (sh == null) { Debug.LogError("[sky] shader Wanderer/MilkyWay non trovato (Always Included?)."); return false; }

        var mesh = BuildEquatorialSphere(64, 128);

        go = new GameObject("MilkyWay");
        go.transform.SetParent(root, false);
        if (layer >= 0) go.layer = layer;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var mat = new Material(sh); mat.SetTexture("_MainTex", tex);
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return true;
    }

    /// <summary>Sfera UV i cui vertici sono direzioni EQUATORIALI portate nel frame di gioco; UV = (RA/360, (Dec+90)/180)
    /// → mappa diretta sulla texture equirettangolare equatoriale. Avvolgimento verso l'ESTERNO (normale = direzione):
    /// con Cull Front si vede l'interno. Cucitura in RA duplicata (lon 0..lonBands).</summary>
    static Mesh BuildEquatorialSphere(int latBands, int lonBands)
    {
        int vlat = latBands + 1, vlon = lonBands + 1;
        var verts = new Vector3[vlat * vlon];
        var uv = new Vector2[vlat * vlon];
        var tris = new int[latBands * lonBands * 6];

        for (int i = 0; i < vlat; i++)
        {
            float vT = i / (float)latBands;            // 0..1
            float dec = (vT * 180f - 90f) * Mathf.Deg2Rad;
            float cd = Mathf.Cos(dec), sd = Mathf.Sin(dec);
            for (int j = 0; j < vlon; j++)
            {
                float uT = j / (float)lonBands;         // 0..1
                float ra = uT * 360f * Mathf.Deg2Rad;
                var eq = new Vector3(cd * Mathf.Cos(ra), cd * Mathf.Sin(ra), sd);
                int idx = i * vlon + j;
                verts[idx] = SkyData.EquatorialToGame(eq) * Radius;
                uv[idx] = new Vector2(uT, vT);
            }
        }

        int t = 0;
        for (int i = 0; i < latBands; i++)
            for (int j = 0; j < lonBands; j++)
            {
                int v00 = i * vlon + j, v01 = v00 + 1, v10 = v00 + vlon, v11 = v10 + 1;
                // avvolgimento esterno (normale = direzione): base→+RA→diag, base→diag→+Dec
                tris[t++] = v00; tris[t++] = v01; tris[t++] = v11;
                tris[t++] = v00; tris[t++] = v11; tris[t++] = v10;
            }

        var mesh = new Mesh { name = "MilkyWaySphere", indexFormat = UnityEngine.Rendering.IndexFormat.UInt16 };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, new System.Collections.Generic.List<Vector2>(uv));
        mesh.SetTriangles(tris, 0, false);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);
        mesh.UploadMeshData(true);
        return mesh;
    }
}

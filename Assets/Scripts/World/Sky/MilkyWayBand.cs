using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// La VIA LATTEA (+ tenue alone di stelle non risolte) come texture equirettangolare EQUATORIALE su una SFERA. La
/// sfera è figlia della "bolla cielo" (<see cref="SkyController"/>), centrata sulla camera e con orientamento FISSO
/// (frame equatoriale): siccome è disegnata con la STESSA proiezione delle stelle (geometria vera, non un trucco a
/// schermo intero), la banda è **incollata alle stelle** e non "nuota" girandosi. Camera al CENTRO della sfera → ogni
/// raggio la attraversa in UN punto solo → si può usare <c>Cull Off</c> senza fantasmi (niente problema di winding).
/// Additiva e fioca, disegnata PRIMA dei punti (queue Background+5 &lt; stelle +10).
/// </summary>
public class MilkyWayBand : MonoBehaviour
{
    public const float Radius = 95f;

    MeshRenderer mr;   // per ricaricare la texture quando si cambia la risoluzione dalle opzioni

    // Risorsa della Via Lattea secondo l'impostazione grafica: 0=4k, 1=8k, 2=16k. Ripiego sulla 16k se la variante manca.
    static Texture2D LoadTex()
    {
        string name = GameSettings.SkyTextureRes == 0 ? "Sky/MilkyWay_4k"
                    : GameSettings.SkyTextureRes == 1 ? "Sky/MilkyWay_8k" : "Sky/MilkyWay";
        var t = Resources.Load<Texture2D>(name) ?? Resources.Load<Texture2D>("Sky/MilkyWay");
        if (t != null) { t.wrapModeU = TextureWrapMode.Repeat; t.wrapModeV = TextureWrapMode.Clamp; t.filterMode = FilterMode.Trilinear; }
        return t;
    }

    /// <summary>Cambia al volo la risoluzione della Via Lattea (dalle opzioni grafiche) senza ricostruire la sfera.</summary>
    public void ApplyResolution()
    {
        if (mr == null) return;
        var t = LoadTex();
        if (t != null) mr.sharedMaterial.mainTexture = t;
    }

    public bool Build(Transform root, int layer)
    {
        var tex = LoadTex();
        if (tex == null) { Debug.LogWarning("[sky] Resources/Sky/MilkyWay non trovata: niente Via Lattea."); return false; }
        tex.wrapModeU = TextureWrapMode.Repeat;   // cucitura in Ascensione Retta
        tex.wrapModeV = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Trilinear;

        var sh = Shader.Find("Wanderer/MilkyWay");
        if (sh == null) { Debug.LogError("[sky] shader Wanderer/MilkyWay non trovato (Always Included?)."); return false; }

        var mesh = BuildEquatorialSphere(96, 192);   // più fitta = niente "tagli" da triangoli grossi

        var go = new GameObject("MilkyWay");
        go.transform.SetParent(root, false);
        if (layer >= 0) go.layer = layer;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = new Material(sh) { mainTexture = tex };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return true;
    }

    /// <summary>Sfera WATERTIGHT (niente colonna di cucitura duplicata: il wrap in RA usa il MODULO → vertici condivisi,
    /// nessuna riga di pixel scoperti). I vertici sono direzioni equatoriali nel frame di gioco; le UV le calcola il
    /// FRAGMENT dalla direzione (vedi shader). vlon = lonBands (non +1).</summary>
    static Mesh BuildEquatorialSphere(int latBands, int lonBands)
    {
        int vlat = latBands + 1, vlon = lonBands;
        var verts = new Vector3[vlat * vlon];
        var tris = new int[latBands * lonBands * 6];

        for (int i = 0; i < vlat; i++)
        {
            float dec = (i / (float)latBands * 180f - 90f) * Mathf.Deg2Rad;
            float cd = Mathf.Cos(dec), sd = Mathf.Sin(dec);
            for (int j = 0; j < vlon; j++)
            {
                float ra = j / (float)lonBands * 360f * Mathf.Deg2Rad;
                var eq = new Vector3(cd * Mathf.Cos(ra), cd * Mathf.Sin(ra), sd);
                verts[i * vlon + j] = SkyData.EquatorialToGame(eq) * Radius;
            }
        }

        int t = 0;
        for (int i = 0; i < latBands; i++)
            for (int j = 0; j < lonBands; j++)
            {
                int jn = (j + 1) % lonBands;                 // wrap col modulo → vertici condivisi, niente cucitura
                int v00 = i * vlon + j, v01 = i * vlon + jn, v10 = (i + 1) * vlon + j, v11 = (i + 1) * vlon + jn;
                tris[t++] = v00; tris[t++] = v01; tris[t++] = v11;
                tris[t++] = v00; tris[t++] = v11; tris[t++] = v10;
            }

        var mesh = new Mesh { name = "MilkyWaySphere", indexFormat = UnityEngine.Rendering.IndexFormat.UInt16 };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0, false);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);   // mai cullata (la camera è al centro)
        mesh.UploadMeshData(true);
        return mesh;
    }
}

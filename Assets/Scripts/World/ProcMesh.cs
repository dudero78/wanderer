using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mesh procedurali a RIVOLUZIONE (lathe): si dà un PROFILO (raggio, altezza) dal basso verso l'alto e si ruota
/// attorno all'asse Y. Serve per forme lisce ad alta risoluzione che i primitivi non danno: la sonda con i SOLCHI
/// incisi (sfera ad alta risoluzione col raggio scavato a certe latitudini) e il TORSO dell'omino (profilo a spalle).
/// Normali ricalcolate; winding scelto per facce rivolte all'ESTERNO (verificato: triangoli (A,C,B)/(B,C,D)).
/// </summary>
public static class ProcMesh
{
    /// <summary>Ruota il profilo attorno a Y. profile[i] = (x=raggio, y=altezza), dal basso verso l'alto.
    /// lonSeg = segmenti di longitudine. Le estremità con raggio ~0 chiudono i poli (triangoli degeneri innocui).</summary>
    public static Mesh RevolveY(Vector2[] profile, int lonSeg, string name = "proc")
    {
        int rings = profile.Length;
        int cols = lonSeg + 1;   // colonna di cucitura duplicata → wrap pulito
        var verts = new Vector3[rings * cols];
        var uvs = new Vector2[rings * cols];
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < cols; j++)
            {
                float a = (j / (float)lonSeg) * Mathf.PI * 2f;
                float r = profile[i].x, y = profile[i].y;
                verts[i * cols + j] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                uvs[i * cols + j] = new Vector2(j / (float)lonSeg, i / (float)Mathf.Max(1, rings - 1));
            }
        var tris = new List<int>((rings - 1) * lonSeg * 6);
        for (int i = 0; i < rings - 1; i++)
            for (int j = 0; j < lonSeg; j++)
            {
                int A = i * cols + j, B = i * cols + j + 1, C = (i + 1) * cols + j, D = (i + 1) * cols + j + 1;
                tris.Add(A); tris.Add(C); tris.Add(B);   // facce verso l'esterno (verificato a mano)
                tris.Add(B); tris.Add(C); tris.Add(D);
            }
        var m = new Mesh { name = name };
        if (verts.Length > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.vertices = verts;
        m.uv = uvs;
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>Sfera (raggio 1) con SOLCHI incisi a certe latitudini: il raggio si scava verso l'interno nelle bande
    /// di latitudine date → canali reali (non bande dipinte), pareti che catturano la luce. latRings/lonSeg alti =
    /// liscia, niente faccette. grooveLatRad = latitudini dei solchi (rad, 0=equatore); halfWRad = mezza larghezza;
    /// depth = profondità (frazione del raggio).</summary>
    public static Mesh GroovedSphere(int latRings, int lonSeg, float[] grooveLatRad, float halfWRad, float depth)
    {
        var prof = new Vector2[latRings];
        for (int k = 0; k < latRings; k++)
        {
            float theta = Mathf.PI - Mathf.PI * k / (latRings - 1);   // k=0 polo basso (θ=π), k=fine polo alto (θ=0)
            float lat = Mathf.PI * 0.5f - theta;                      // -π/2 .. +π/2
            float p = 0f;
            for (int g = 0; g < grooveLatRad.Length; g++)
            {
                float d = Mathf.Abs(lat - grooveLatRad[g]);
                if (d < halfWRad)
                {
                    // canale a fondo PIATTO con pareti morbide ma decise: 1 per d<0.4·halfW, scende a 0 al bordo.
                    float t = Mathf.InverseLerp(halfWRad, halfWRad * 0.4f, d);   // 0 al bordo, 1 dentro
                    p = Mathf.Max(p, t * t * (3f - 2f * t));
                }
            }
            float rf = 1f - depth * p;
            prof[k] = new Vector2(Mathf.Sin(theta) * rf, Mathf.Cos(theta) * rf);
        }
        return RevolveY(prof, lonSeg, "groovedSphere");
    }
}

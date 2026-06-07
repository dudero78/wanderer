using System.IO;
using UnityEngine;

/// <summary>
/// Carica il BLOB binario delle stelle (Assets/Resources/Sky/stars.bytes, prodotto da <c>StarCatalogBakeTool</c>)
/// in array paralleli pronti per costruire la mesh. Nessun parsing CSV a runtime: solo una lettura sequenziale di
/// ~1.4 MB. Le stelle sono ORDINATE per luminosità decrescente; <see cref="NakedCount"/> = quante stanno nel tier a
/// occhio nudo (mag ≤ 6.5), così "occhio nudo" e "campo profondo" sono semplici range-prefisso.
/// </summary>
public static class SkyData
{
    public static bool Loaded { get; private set; }
    public static int Count { get; private set; }
    public static int NakedCount { get; private set; }
    public static Vector3[] Dir;      // direzione unitaria nel frame equatoriale fisso
    public static float[] Mag;        // magnitudine apparente
    public static Color32[] Color;    // RGB del B−V (alpha inutilizzato)
    public static byte[] Flags;       // bit0 = occhio-nudo · bit1 = showpiece (alone)

    public static bool Load()
    {
        if (Loaded) return true;
        var ta = Resources.Load<TextAsset>("Sky/stars");
        if (ta == null)
        {
            Debug.LogWarning("[sky] Resources/Sky/stars.bytes mancante: lancia 'Wanderer/Bake star catalog'. Cielo vuoto.");
            return false;
        }

        using var ms = new MemoryStream(ta.bytes);
        using var r = new BinaryReader(ms);
        // firma "WSKY"
        if (r.ReadByte() != 'W' || r.ReadByte() != 'S' || r.ReadByte() != 'K' || r.ReadByte() != 'Y')
        {
            Debug.LogError("[sky] stars.bytes: firma non valida.");
            return false;
        }
        r.ReadInt32();                 // versione (non usata ora)
        Count = r.ReadInt32();
        NakedCount = r.ReadInt32();

        Dir = new Vector3[Count];
        Mag = new float[Count];
        Color = new Color32[Count];
        Flags = new byte[Count];
        for (int i = 0; i < Count; i++)
        {
            float x = Mathf.HalfToFloat(r.ReadUInt16());
            float y = Mathf.HalfToFloat(r.ReadUInt16());
            float z = Mathf.HalfToFloat(r.ReadUInt16());
            Dir[i] = new Vector3(x, y, z);
            Mag[i] = Mathf.HalfToFloat(r.ReadUInt16());
            byte cr = r.ReadByte(), cg = r.ReadByte(), cb = r.ReadByte(), fl = r.ReadByte();
            Color[i] = new Color32(cr, cg, cb, 255);
            Flags[i] = fl;
        }
        Loaded = true;
        return true;
    }

    // ---- Frame del cielo ------------------------------------------------------------------------------------------

    public const float Obliquity = 23.4392811f;   // inclinazione dell'eclittica (gradi): angolo equatore↔eclittica

    /// <summary>
    /// Porta una direzione dal frame EQUATORIALE del catalogo (x→equinozio di primavera, z→polo nord celeste) al
    /// frame di GIOCO, dove il piano ORBITALE dei pianeti è y=0 e il polo è +y. Due passi: (1) equatoriale→eclittica
    /// (rotazione di Obliquity attorno all'asse x); (2) mappa il polo dell'eclittica su +y, così l'eclittica reale
    /// coincide col piano orbitale → da dentro il sistema lo ZODIACO poggia sull'eclittica e il Sole ci transita.
    /// Usato sia dal bake (ogni stella) sia dal piazzamento dei sistemi (Vega/Antares) → cielo e destinazioni coerenti.
    /// </summary>
    public static Vector3 EquatorialToGame(Vector3 eq)
    {
        float e = Obliquity * Mathf.Deg2Rad;
        float ce = Mathf.Cos(e), se = Mathf.Sin(e);
        float xe = eq.x;
        float ye = eq.y * ce + eq.z * se;     // equatoriale → eclittica (Rx(ε))
        float ze = -eq.y * se + eq.z * ce;    // ze = polo dell'eclittica
        return new Vector3(xe, ze, ye);       // piano eclittica (xe,ye) → piano x-z (y=0), polo → +y
    }

    /// <summary>Direzione di gioco di una stella da Ascensione Retta (gradi) e Declinazione (gradi).</summary>
    public static Vector3 StarDirection(float raDeg, float decDeg)
    {
        float ra = raDeg * Mathf.Deg2Rad, dec = decDeg * Mathf.Deg2Rad;
        var eq = new Vector3(Mathf.Cos(dec) * Mathf.Cos(ra), Mathf.Cos(dec) * Mathf.Sin(ra), Mathf.Sin(dec));
        return EquatorialToGame(eq).normalized;
    }
}

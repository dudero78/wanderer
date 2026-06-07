using System.IO;
using System.Text;
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

    // ---- Campo PROFONDO (ATHYG, ~mag 7-10): stelle che si vedono solo zoomando. Mesh separato, acceso col binocolo. --

    public static bool DeepLoaded { get; private set; }
    public static int DeepCount { get; private set; }
    public static Vector3[] DeepDir;
    public static float[] DeepMag;
    public static Color32[] DeepColor;

    public static bool LoadDeep()
    {
        if (DeepLoaded) return DeepDir != null;
        DeepLoaded = true;
        var ta = Resources.Load<TextAsset>("Sky/deepstars");
        if (ta == null) return false;   // opzionale: niente campo profondo, nessun errore
        using var ms = new MemoryStream(ta.bytes);
        using var r = new BinaryReader(ms);
        if (r.ReadByte() != 'W' || r.ReadByte() != 'S' || r.ReadByte() != 'K' || r.ReadByte() != 'Y') return false;
        r.ReadInt32(); DeepCount = r.ReadInt32(); r.ReadInt32();   // versione, conteggio, nakedCount(0)
        DeepDir = new Vector3[DeepCount]; DeepMag = new float[DeepCount]; DeepColor = new Color32[DeepCount];
        for (int i = 0; i < DeepCount; i++)
        {
            float x = Mathf.HalfToFloat(r.ReadUInt16()), y = Mathf.HalfToFloat(r.ReadUInt16()), z = Mathf.HalfToFloat(r.ReadUInt16());
            DeepDir[i] = new Vector3(x, y, z);
            DeepMag[i] = Mathf.HalfToFloat(r.ReadUInt16());
            byte cr = r.ReadByte(), cg = r.ReadByte(), cb = r.ReadByte(); r.ReadByte();
            DeepColor[i] = new Color32(cr, cg, cb, 255);
        }
        return true;
    }

    // ---- Deep-sky (galassie/nebulose/ammassi) ---------------------------------------------------------------------

    public static bool DsoLoaded { get; private set; }
    public static int DsoCount { get; private set; }
    public static Vector3[] DsoDir;       // direzione unitaria (frame di gioco)
    public static float[] DsoRadArcmin;   // raggio angolare apparente (arcmin)
    public static float[] DsoMag;         // magnitudine
    public static byte[] DsoType;         // 0 galassia · 1 ammasso aperto · 2 globulare · 3 nebulosa · 4 planetaria
    public static byte[] DsoFlags;        // bit0 Messier · bit1 con nome comune
    public static ushort[] DsoTile;       // indice nell'atlante delle foto vere
    public static string[] DsoName;       // etichetta (M 42, NGC 7000, nome comune…)

    public static bool LoadDso()
    {
        if (DsoLoaded) return true;
        var ta = Resources.Load<TextAsset>("Sky/dso");
        if (ta == null) return false;   // opzionale: niente deep-sky, nessun errore
        using var ms = new MemoryStream(ta.bytes);
        using var r = new BinaryReader(ms);
        if (r.ReadByte() != 'W' || r.ReadByte() != 'D' || r.ReadByte() != 'S' || r.ReadByte() != 'O') return false;
        r.ReadInt32();
        DsoCount = r.ReadInt32();
        DsoDir = new Vector3[DsoCount]; DsoRadArcmin = new float[DsoCount]; DsoMag = new float[DsoCount];
        DsoType = new byte[DsoCount]; DsoFlags = new byte[DsoCount]; DsoTile = new ushort[DsoCount]; DsoName = new string[DsoCount];
        for (int i = 0; i < DsoCount; i++)
        {
            float x = Mathf.HalfToFloat(r.ReadUInt16()), y = Mathf.HalfToFloat(r.ReadUInt16()), z = Mathf.HalfToFloat(r.ReadUInt16());
            DsoDir[i] = new Vector3(x, y, z);
            DsoRadArcmin[i] = Mathf.HalfToFloat(r.ReadUInt16());
            DsoMag[i] = Mathf.HalfToFloat(r.ReadUInt16());
            DsoType[i] = r.ReadByte(); DsoFlags[i] = r.ReadByte();
            DsoTile[i] = r.ReadUInt16();
            int nl = r.ReadByte();
            DsoName[i] = nl > 0 ? Encoding.UTF8.GetString(r.ReadBytes(nl)) : "";
        }
        DsoLoaded = true;
        return true;
    }

    // ---- Costellazioni (tutte le 88, da d3-celestial) + nomi delle stelle (da HYG) --------------------------------

    public sealed class Constellation
    {
        public string Name; public bool Zodiac; public bool North; public Vector3 Centroid;
        public Vector3[] A, B;   // estremi dei segmenti (direzioni di gioco)
    }
    public static Constellation[] Cons;
    public static bool ConsLoaded { get; private set; }

    public static bool LoadConstellations()
    {
        if (ConsLoaded) return Cons != null;
        ConsLoaded = true;
        var ta = Resources.Load<TextAsset>("Sky/constellations");
        if (ta == null) return false;
        using var r = new BinaryReader(new MemoryStream(ta.bytes));
        if (r.ReadByte() != 'W' || r.ReadByte() != 'C' || r.ReadByte() != 'O' || r.ReadByte() != 'N') return false;
        r.ReadInt32();
        int n = r.ReadInt32();
        Cons = new Constellation[n];
        for (int i = 0; i < n; i++)
        {
            int ln = r.ReadInt32();
            string name = Encoding.UTF8.GetString(r.ReadBytes(ln));
            bool zod = r.ReadByte() != 0;
            float avgDec = r.ReadSingle();
            int sc = r.ReadInt32();
            var A = new Vector3[sc]; var B = new Vector3[sc]; var cen = Vector3.zero;
            for (int s = 0; s < sc; s++)
            {
                float ra0 = r.ReadSingle(), dec0 = r.ReadSingle(), ra1 = r.ReadSingle(), dec1 = r.ReadSingle();
                A[s] = StarDirection(ra0, dec0); B[s] = StarDirection(ra1, dec1);
                cen += A[s] + B[s];
            }
            Cons[i] = new Constellation { Name = name, Zodiac = zod, North = avgDec >= 0f,
                Centroid = cen.sqrMagnitude > 1e-6f ? cen.normalized : Vector3.forward, A = A, B = B };
        }
        return true;
    }

    public static int StarNameCount { get; private set; }
    public static string[] StarNameStr;
    public static Vector3[] StarNameDir;
    public static float[] StarNameMag;
    public static bool NamesLoaded { get; private set; }

    public static bool LoadStarNames()
    {
        if (NamesLoaded) return StarNameStr != null;
        NamesLoaded = true;
        var ta = Resources.Load<TextAsset>("Sky/starnames");
        if (ta == null) return false;
        using var r = new BinaryReader(new MemoryStream(ta.bytes));
        if (r.ReadByte() != 'W' || r.ReadByte() != 'S' || r.ReadByte() != 'N' || r.ReadByte() != 'M') return false;
        r.ReadInt32();
        StarNameCount = r.ReadInt32();
        StarNameStr = new string[StarNameCount]; StarNameDir = new Vector3[StarNameCount]; StarNameMag = new float[StarNameCount];
        for (int i = 0; i < StarNameCount; i++)
        {
            int ln = r.ReadInt32();
            StarNameStr[i] = Encoding.UTF8.GetString(r.ReadBytes(ln));
            float ra = r.ReadSingle(), dec = r.ReadSingle();
            StarNameDir[i] = StarDirection(ra, dec);
            StarNameMag[i] = r.ReadSingle();
        }
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

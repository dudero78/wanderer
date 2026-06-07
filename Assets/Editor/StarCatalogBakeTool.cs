using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Comando editor "Wanderer/Bake star catalog": legge i cataloghi astronomici GREZZI (CSV) da Assets/StarData/ e
/// li converte in BLOB BINARI COMPATTI in Assets/Resources/Sky/, che il gioco carica all'avvio (niente parsing CSV
/// a runtime). È il gemello stellare di <see cref="PlanetBakeTool"/>: lavoro pesante OFFLINE, dato leggero in gioco.
///
/// Sorgente stelle = HYG database v4.2 (astronexus, CC BY-SA): ~119k stelle con posizione, magnitudine, indice di
/// colore B−V, nome proprio, ID Hipparcos. Per ogni stella scrivo:
///   - direzione UNITARIA nel frame equatoriale fisso (normalizzo le x,y,z già cartesiane del catalogo),
///   - magnitudine apparente (Vmag),
///   - colore RGB pre-risolto dal B−V (temperatura blackbody → sRGB), così a runtime niente LUT,
///   - flag (tier occhio-nudo / brillante).
/// Ordinato per luminosità DECRESCENTE → il tiering (occhio-nudo vs campo profondo) e "le prime N brillanti" sono
/// semplici range-prefisso. ~119k × 12 B ≈ 1.4 MB.
///
/// È OPT-IN: finché non lanci il comando la cartella Sky/ non esiste e il cielo resta vuoto. I CSV grezzi stanno in
/// Assets/StarData/ (FUORI da Resources → non finiscono nella build). ATTRIBUTION.txt cita le sorgenti (CC BY-SA
/// richiede credito; i dati NON vanno rilicenziati).
/// </summary>
public static class StarCatalogBakeTool
{
    const string Magic = "WSKY";          // firma del blob
    const int Version = 1;
    const float NakedEyeMag = 6.5f;       // soglia occhio nudo (tier A): più luminose di così = sempre visibili
    const float BrightHaloMag = 2.2f;     // sotto questa magnitudine = stella "showpiece" → flag alone (Sirio≈−1.4, Vega≈0.0)

    [MenuItem("Wanderer/Bake star catalog")]
    public static void BakeStarCatalog()
    {
        // CSV grezzi FUORI da Assets (Wanderer/StarData), così Unity non li importa: il bake li legge via File.IO.
        string srcDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "StarData"));
        string hygPath = Path.Combine(srcDir, "hyg_v42.csv");
        if (!File.Exists(hygPath))
        {
            Debug.LogError("Bake star catalog: manca " + hygPath + " (scarica il catalogo HYG v4.2 in <progetto>/StarData/).");
            return;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Sky")) AssetDatabase.CreateFolder("Assets/Resources", "Sky");

        var stars = ReadHyg(hygPath);
        if (stars.Count == 0) { Debug.LogError("Bake star catalog: nessuna stella letta da HYG."); return; }

        // ordina per luminosità decrescente (mag crescente = più luminoso prima)
        stars.Sort((a, b) => a.mag.CompareTo(b.mag));

        int nakedCount = 0;
        foreach (var s in stars) { if (s.mag <= NakedEyeMag) nakedCount++; else break; }

        WriteStarBlob("Assets/Resources/Sky/stars.bytes", stars, nakedCount);

        // CAMPO PROFONDO (opzionale): ATHYG ridotto a mag 10 — milioni... no, ~330k stelle, di cui teniamo solo quelle
        // NON già nell'HYG (colonna 'hyg' vuota) → niente doppioni. Sono le deboli (mag 7-10) che si vedono solo con
        // binocolo/telescopio. Mesh separato, acceso solo zoomando (vedi StarFieldRenderer/SkyController) → costo ZERO
        // a occhio nudo. File: StarData/athyg_m10.csv.gz.
        int deepCount = 0;
        string athygPath = Path.Combine(srcDir, "athyg_m11.csv.gz");                 // più denso (preferito)
        if (!File.Exists(athygPath)) athygPath = Path.Combine(srcDir, "athyg_m10.csv.gz");   // fallback
        if (File.Exists(athygPath))
        {
            try { var deep = ReadAthygDeep(athygPath); WriteStarBlob("Assets/Resources/Sky/deepstars.bytes", deep, 0); deepCount = deep.Count; }
            catch (System.Exception e) { Debug.LogError("Bake: campo profondo (ATHYG) saltato: " + e.Message); }
        }

        // Deep-sky (OpenNGC): galassie, nebulose, ammassi. Opzionale (se il CSV c'è).
        int dsoCount = 0;
        string ngcPath = Path.Combine(srcDir, "NGC.csv");
        if (File.Exists(ngcPath))
        {
            var dso = ReadOpenNgc(ngcPath, LoadTileMap(srcDir));
            WriteDsoBlob("Assets/Resources/Sky/dso.bytes", dso);
            dsoCount = dso.Count;
        }
        else Debug.LogWarning("Bake star catalog: NGC.csv non trovato in StarData/ → niente deep-sky.");

        WriteAttribution("Assets/Resources/Sky/ATTRIBUTION.txt");

        AssetDatabase.Refresh();
        Debug.Log($"Bake star catalog: FATTO. {stars.Count} stelle ({nakedCount} a occhio nudo, mag≤{NakedEyeMag}), "
                + $"{deepCount} stelle profonde (ATHYG, solo zoom), {dsoCount} deep-sky. Scritto stars.bytes/deepstars.bytes/dso.bytes.");
    }

    struct Star { public Vector3 dir; public float mag; public byte r, g, b, flags; }

    /// <summary>Campo profondo da ATHYG (gz): tiene SOLO le stelle non presenti nell'HYG (colonna 'hyg' vuota) → niente
    /// doppioni. Direzione da x0/y0/z0 (normalizzati, frame eclittica di gioco) come l'HYG; colore dal B−V (ci).</summary>
    static List<Star> ReadAthygDeep(string gzPath)
    {
        var list = new List<Star>(400000);
        using var sr = new StreamReader(new GZipStream(File.OpenRead(gzPath), CompressionMode.Decompress));
        string header = sr.ReadLine();
        if (header == null) return list;
        // mappa colonne PROPRIA (non MapColumns, che è specifica HYG e pretende x/y/z): ATHYG ha x0/y0/z0
        var hf = SplitCsv(header);
        var col = new Dictionary<string, int>();
        for (int i = 0; i < hf.Count; i++) col[hf[i].Trim().ToLowerInvariant()] = i;
        if (!col.ContainsKey("hyg") || !col.ContainsKey("x0") || !col.ContainsKey("mag")) { Debug.LogWarning("ATHYG: colonne inattese."); return list; }
        int iHyg = col["hyg"], iX = col["x0"], iY = col["y0"], iZ = col["z0"], iMag = col["mag"];
        int iCi = col.TryGetValue("ci", out int c0) ? c0 : -1;
        string line;
        while ((line = sr.ReadLine()) != null)
        {
            if (line.Length == 0) continue;
            var f = SplitCsv(line);
            if (f.Count <= iZ) continue;
            if (!string.IsNullOrEmpty(f[iHyg].Trim())) continue;   // già nell'HYG → salta (no doppioni)
            if (!TryF(f[iX], out float x) || !TryF(f[iY], out float y) || !TryF(f[iZ], out float z)) continue;
            var dir = new Vector3(x, y, z); float len = dir.magnitude;
            if (len < 1e-6f) continue;
            dir = SkyData.EquatorialToGame(dir / len);
            if (!TryF(f[iMag], out float mag)) continue;
            float bv = (iCi >= 0 && TryF(f[iCi], out float ci)) ? ci : 0.6f;
            BvToRgb(bv, out byte r, out byte g, out byte b);
            list.Add(new Star { dir = dir, mag = mag, r = r, g = g, b = b, flags = 0 });
        }
        return list;
    }

    // ---- Lettura HYG ----------------------------------------------------------------------------------------------

    static List<Star> ReadHyg(string path)
    {
        var list = new List<Star>(120000);
        using var sr = new StreamReader(path);
        string header = sr.ReadLine();
        if (header == null) return list;
        var col = MapColumns(header);
        int iId = col["id"], iX = col["x"], iY = col["y"], iZ = col["z"], iMag = col["mag"], iCi = col["ci"];

        string line;
        while ((line = sr.ReadLine()) != null)
        {
            if (line.Length == 0) continue;
            var f = SplitCsv(line);
            if (f.Count <= iZ) continue;

            // salta il Sole (id 0, a (0,0,0)): è il nostro centro, non una stella del cielo
            if (int.TryParse(f[iId], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && id == 0) continue;

            if (!TryF(f[iX], out float x) || !TryF(f[iY], out float y) || !TryF(f[iZ], out float z)) continue;
            var dir = new Vector3(x, y, z);
            float len = dir.magnitude;
            if (len < 1e-6f) continue;
            dir /= len;
            dir = SkyData.EquatorialToGame(dir);   // allinea l'eclittica reale al piano orbitale di gioco (y=0)

            if (!TryF(f[iMag], out float mag)) continue;
            float bv = TryF(f[iCi], out float ci) ? ci : 0.6f;   // B−V mancante → ~Sole (giallo-bianco)

            BvToRgb(bv, out byte r, out byte g, out byte b);
            byte flags = 0;
            if (mag <= NakedEyeMag) flags |= 1;     // tier occhio-nudo
            if (mag <= BrightHaloMag) flags |= 2;   // showpiece (alone)

            list.Add(new Star { dir = dir, mag = mag, r = r, g = g, b = b, flags = flags });
        }
        return list;
    }

    static Dictionary<string, int> MapColumns(string header)
    {
        var f = SplitCsv(header);
        var map = new Dictionary<string, int>();
        for (int i = 0; i < f.Count; i++) map[f[i].Trim().ToLowerInvariant()] = i;
        foreach (var need in new[] { "id", "x", "y", "z", "mag", "ci" })
            if (!map.ContainsKey(need)) throw new System.Exception("HYG: colonna mancante '" + need + "'");
        return map;
    }

    // splitter CSV minimale ma quote-aware (i campi HYG sono fra virgolette doppie)
    static List<string> SplitCsv(string line)
    {
        var outp = new List<string>(40);
        var sb = new System.Text.StringBuilder();
        bool q = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"') { q = !q; continue; }
            if (c == ',' && !q) { outp.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(c);
        }
        outp.Add(sb.ToString());
        return outp;
    }

    static bool TryF(string s, out float v) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    // ---- B−V → colore RGB -----------------------------------------------------------------------------------------

    /// <summary>
    /// Indice di colore B−V → RGB. Due passi standard (alla Stellarium): B−V → temperatura (formula di Ballesteros,
    /// da Planck) → colore di corpo nero in sRGB. Risultato: blu (B−V negativo, caldo) → bianco → giallo → arancio →
    /// rosso (B−V grande, freddo). Manteniamo il colore VERO (la desaturazione percettiva "le deboli sembrano bianche"
    /// la fa lo shader in base alla luminosità/zoom).
    /// </summary>
    static void BvToRgb(float bv, out byte r, out byte g, out byte b)
    {
        bv = Mathf.Clamp(bv, -0.4f, 2.0f);
        float t = 4600f * (1f / (0.92f * bv + 1.7f) + 1f / (0.92f * bv + 0.62f));   // Ballesteros
        BlackbodyToRgb(t, out float rf, out float gf, out float bf);
        r = (byte)Mathf.RoundToInt(Mathf.Clamp01(rf) * 255f);
        g = (byte)Mathf.RoundToInt(Mathf.Clamp01(gf) * 255f);
        b = (byte)Mathf.RoundToInt(Mathf.Clamp01(bf) * 255f);
    }

    /// <summary>Temperatura (K) → RGB ~sRGB (approssimazione di Tanner Helland, valida ~1000–40000 K).</summary>
    static void BlackbodyToRgb(float kelvin, out float r, out float g, out float b)
    {
        float t = Mathf.Clamp(kelvin, 1000f, 40000f) / 100f;
        // rosso
        r = t <= 66f ? 1f : Mathf.Clamp01(1.292936f * Mathf.Pow(t - 60f, -0.1332047f));
        // verde
        g = t <= 66f
            ? Mathf.Clamp01(0.3900816f * Mathf.Log(t) - 0.6318414f)
            : Mathf.Clamp01(1.129891f * Mathf.Pow(t - 60f, -0.0755148f));
        // blu
        b = t >= 66f ? 1f : (t <= 19f ? 0f : Mathf.Clamp01(0.5432068f * Mathf.Log(t - 10f) - 1.196254f));
    }

    // ---- Scrittura blob -------------------------------------------------------------------------------------------

    static void WriteStarBlob(string assetPath, List<Star> stars, int nakedCount)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)Magic[0]); w.Write((byte)Magic[1]); w.Write((byte)Magic[2]); w.Write((byte)Magic[3]);
        w.Write(Version);
        w.Write(stars.Count);
        w.Write(nakedCount);
        foreach (var s in stars)
        {
            w.Write(Mathf.FloatToHalf(s.dir.x));
            w.Write(Mathf.FloatToHalf(s.dir.y));
            w.Write(Mathf.FloatToHalf(s.dir.z));
            w.Write(Mathf.FloatToHalf(s.mag));
            w.Write(s.r); w.Write(s.g); w.Write(s.b); w.Write(s.flags);
        }
        File.WriteAllBytes(assetPath, ms.ToArray());
    }

    // ---- Deep-sky (OpenNGC) ---------------------------------------------------------------------------------------

    struct Dso { public Vector3 dir; public float radArcmin; public float mag; public byte type; public byte flags; public ushort tile; public string name; }

    /// <summary>Mappa identificatore ("M42"/"NGC7000"/"IC434") → indice tile nell'atlante foto, da StarData/dso_tiles.json
    /// (scritto dallo script che impacchetta le immagini). Parser semplice: il file è un dizionario piatto stringa→intero.</summary>
    static Dictionary<string, int> LoadTileMap(string srcDir)
    {
        var map = new Dictionary<string, int>();
        string path = Path.Combine(srcDir, "dso_tiles.json");
        if (!File.Exists(path)) return map;
        foreach (Match m in Regex.Matches(File.ReadAllText(path), "\"([^\"]+)\"\\s*:\\s*(\\d+)"))
            map[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
        return map;
    }

    // categorie (type byte): 0 galassia · 1 ammasso aperto · 2 ammasso globulare · 3 nebulosa · 4 nebulosa planetaria
    static int Category(string t)
    {
        switch (t)
        {
            case "G": case "GPair": case "GTrpl": case "GGroup": return 0;
            case "OCl": case "*Ass": return 1;
            case "GCl": return 2;
            case "Neb": case "HII": case "EmN": case "RfN": case "Cl+N": case "SNR": return 3;
            case "PN": return 4;
            default: return -1;   // Dup, *, **, NonEx, Nova, Other → scarta
        }
    }

    static List<Dso> ReadOpenNgc(string path, Dictionary<string, int> tiles)
    {
        bool useImages = tiles.Count > 0;   // se c'è l'atlante foto → teniamo SOLO gli oggetti con immagine vera
        var list = new List<Dso>(1024);
        using var sr = new StreamReader(path);
        sr.ReadLine();   // header (campi fissi, separatore ';')
        string line;
        while ((line = sr.ReadLine()) != null)
        {
            var f = line.Split(';');
            if (f.Length < 26) continue;
            int cat = Category(f[1]);
            if (cat < 0) continue;

            if (!TryHms(f[2], out float raDeg) || !TryDms(f[3], out float decDeg)) continue;

            float mag = TryF(f[9], out float v) ? v : (TryF(f[8], out float b) ? b : 11f);   // V, poi B, poi default

            // identificatori candidati: M (col 23), NGC/IC dal NOME (col 0, es. "NGC0224"→"NGC224")
            string name = f[0].Trim();
            string mId = !string.IsNullOrEmpty(f[23].Trim()) && int.TryParse(f[23].Trim(), out int mn) ? "M" + mn : null;
            string ngcId = name.StartsWith("NGC") && int.TryParse(name.Substring(3).TrimStart('0'), out int gn) ? "NGC" + gn : null;
            string icId = name.StartsWith("IC") && int.TryParse(name.Substring(2).TrimStart('0'), out int icn) ? "IC" + icn : null;

            int tile = -1;
            if (mId != null && tiles.TryGetValue(mId, out int t1)) tile = t1;
            else if (ngcId != null && tiles.TryGetValue(ngcId, out int t2)) tile = t2;
            else if (icId != null && tiles.TryGetValue(icId, out int t3)) tile = t3;

            bool messier = mId != null;
            bool named = f.Length > 28 && !string.IsNullOrEmpty(f[28].Trim());
            if (useImages)
            {
                if (tile < 0) continue;   // niente foto → scarta (solo oggetti veri da esplorare)
            }
            else if (!messier && !named && !(mag <= 10.5f)) continue;   // fallback senza atlante: vecchio criterio

            float major = TryF(f[5], out float maj) ? maj : 2f;   // asse maggiore in arcmin
            float minor = TryF(f[6], out float mnr) && mnr > 0 ? mnr : major;
            float radArcmin = Mathf.Max(major * 0.5f, 0.5f);

            // LUMINOSITÀ DI SUPERFICIE (mag/arcsec²): è ciò che determina QUANDO un oggetto si vede (un oggetto grande e
            // debole come le Pleiadi ha superficie fioca → serve ingrandimento; uno compatto e brillante si vede subito).
            // OpenNGC la dà (col 13) solo a volte → altrimenti la calcolo: SurfBr = V + 2.5·log10(area in arcsec²).
            float surfBr;
            if (TryF(f[13], out float sb) && sb > 1f) surfBr = sb;
            else
            {
                float areaArcsec2 = Mathf.PI * (major * minor / 4f) * 3600f;   // major,minor in arcmin → arcsec²
                surfBr = areaArcsec2 > 1f ? mag + 2.5f * Mathf.Log10(areaArcsec2) : mag + 6f;
            }
            // compressione dell'estremo BRILLANTE: le foto (Hubble, molto stirate) appaiono troppo vivide a basso
            // ingrandimento → avvicino a ~24 gli oggetti con superficie più brillante, lasciando intatti i deboli
            // (Pleiadi/galassie). Così Orione non è più "troppo luminoso" a 7×, il resto resta com'è.
            surfBr += Mathf.Max(0f, 24f - surfBr) * 0.5f;

            var eq = EqUnit(raDeg, decDeg);
            byte flags = (byte)((messier ? 1 : 0) | (named ? 2 : 0));
            // etichetta: preferisci la sigla Messier (la più riconoscibile), poi il nome comune, poi NGC/IC
            string common = f.Length > 28 ? f[28].Trim() : "";
            string label = mId != null ? "M " + mId.Substring(1)
                         : !string.IsNullOrEmpty(common) ? common
                         : ngcId != null ? "NGC " + ngcId.Substring(3)
                         : icId != null ? "IC " + icId.Substring(2) : name;
            // NB: nel campo 'mag' del blob salviamo la luminosità di SUPERFICIE (driver di visibilità realistico)
            list.Add(new Dso { dir = SkyData.EquatorialToGame(eq), radArcmin = radArcmin, mag = surfBr,
                               type = (byte)cat, flags = flags, tile = (ushort)Mathf.Max(0, tile), name = label });
        }

        // Oggetti famosi ASSENTI da OpenNGC (non sono NGC/IC): aggiunti a mano se c'è la loro immagine. Il caso chiave è
        // M45 (Pleiadi = Melotte 22) → senza questo la nebulosità non verrebbe MAI renderizzata (si vedono solo le stelle).
        if (useImages && tiles.TryGetValue("M45", out int t45))
            list.Add(new Dso { dir = SkyData.EquatorialToGame(EqUnit(56.871f, 24.105f)), radArcmin = 55f, mag = 24f,
                               type = 1, flags = 1, tile = (ushort)t45, name = "M 45" });   // surfBr 24 = nebulosità fioca
        // Nubi di Magellano: galassie satelliti ENORMI (~10°/5°), non in OpenNGC. surfBr bassa → fioche a basso zoom,
        // emergono ingrandendo (non più "luminosissime" a 7×). radArcmin tarato per la resa angolare (×_SizeScale 2.2).
        if (useImages && tiles.TryGetValue("LMC", out int tLmc))
            list.Add(new Dso { dir = SkyData.EquatorialToGame(EqUnit(80.894f, -69.756f)), radArcmin = 140f, mag = 22.5f,
                               type = 0, flags = 2, tile = (ushort)tLmc, name = "Grande Nube di Magellano" });
        if (useImages && tiles.TryGetValue("SMC", out int tSmc))
            list.Add(new Dso { dir = SkyData.EquatorialToGame(EqUnit(13.187f, -72.829f)), radArcmin = 75f, mag = 23f,
                               type = 0, flags = 2, tile = (ushort)tSmc, name = "Piccola Nube di Magellano" });

        list.Sort((a, b) => a.mag.CompareTo(b.mag));
        return list;
    }

    static Vector3 EqUnit(float raDeg, float decDeg)
    {
        float ra = raDeg * Mathf.Deg2Rad, dec = decDeg * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(dec) * Mathf.Cos(ra), Mathf.Cos(dec) * Mathf.Sin(ra), Mathf.Sin(dec));
    }

    // "HH:MM:SS.s" → gradi (×15)
    static bool TryHms(string s, out float deg)
    {
        deg = 0; var p = s.Split(':'); if (p.Length < 3) return false;
        if (!TryF(p[0], out float h) || !TryF(p[1], out float m) || !TryF(p[2], out float sec)) return false;
        deg = (h + m / 60f + sec / 3600f) * 15f; return true;
    }

    // "+DD:MM:SS.s" → gradi (con segno)
    static bool TryDms(string s, out float deg)
    {
        deg = 0; s = s.Trim(); if (s.Length == 0) return false;
        float sign = s[0] == '-' ? -1f : 1f;
        var p = s.TrimStart('+', '-').Split(':'); if (p.Length < 3) return false;
        if (!TryF(p[0], out float d) || !TryF(p[1], out float m) || !TryF(p[2], out float sec)) return false;
        deg = sign * (d + m / 60f + sec / 3600f); return true;
    }

    static void WriteDsoBlob(string assetPath, List<Dso> dso)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)'W'); w.Write((byte)'D'); w.Write((byte)'S'); w.Write((byte)'O');
        w.Write(Version);
        w.Write(dso.Count);
        foreach (var d in dso)
        {
            w.Write(Mathf.FloatToHalf(d.dir.x));
            w.Write(Mathf.FloatToHalf(d.dir.y));
            w.Write(Mathf.FloatToHalf(d.dir.z));
            w.Write(Mathf.FloatToHalf(d.radArcmin));
            w.Write(Mathf.FloatToHalf(d.mag));
            w.Write(d.type); w.Write(d.flags);
            w.Write(d.tile);   // ushort: indice nell'atlante foto
            var nb = System.Text.Encoding.UTF8.GetBytes(d.name ?? "");
            w.Write((byte)Mathf.Min(nb.Length, 255)); w.Write(nb, 0, Mathf.Min(nb.Length, 255));   // etichetta (lunghezza in 1 byte)
        }
        File.WriteAllBytes(assetPath, ms.ToArray());
    }

    static void WriteAttribution(string path)
    {
        File.WriteAllText(path,
            "Cielo stellato di Wanderer — sorgenti dei dati (CC BY-SA: credito richiesto, dati non rilicenziati)\n" +
            "\n" +
            "Stelle: HYG Database v4.2 — astronexus (David Nash), CC BY-SA 4.0.\n" +
            "  https://www.astronexus.com/projects/hyg  ·  https://codeberg.org/astronexus/hyg\n" +
            "  (fonde Hipparcos, Yale Bright Star Catalog e Gliese.)\n" +
            "\n" +
            "Deep-sky: OpenNGC — Mattia Verga, CC BY-SA 4.0.  https://github.com/mattiaverga/OpenNGC\n" +
            "\n" +
            "Linee di costellazione: generate da noi dagli ID Hipparcos (non derivate dai file GPL di Stellarium).\n");
    }
}

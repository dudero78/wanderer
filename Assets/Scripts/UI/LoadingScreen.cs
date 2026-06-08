using UnityEngine;

/// <summary>
/// Schermata di CARICAMENTO: overlay scuro con uno SPINNER che gira e una serie di messaggi BUFFI che scorrono
/// ("stiamo generando/caricando…"). La crea GameBootstrap PRIMA del lavoro pesante e la chiude con <see cref="Done"/>
/// (dissolvenza). Spinner e messaggi animano sui tempi REALI (unscaled), quando il main thread non è bloccato dal build.
/// </summary>
public class LoadingScreen : MonoBehaviour
{
    static readonly string[] Messages =
    {
        "Generando crateri a mano, uno per uno…",
        "Convincendo i pianeti a restare in orbita…",
        "Lucidando le bombole della tuta…",
        "Calibrando la gravità (di nuovo)…",
        "Insegnando alla sonda a non sprofondare…",
        "Allineando i tropici delle sfere…",
        "Accendendo le stelle lontane…",
        "Pre-scaldando i compute shader…",
        "Verificando che il giocatore non fluttui via…",
        "Spolverando la regolite…",
        "Spolverando gli anelli dei giganti gassosi…",
        "Chiedendo scusa agli asteroidi…",
        "Negoziando con la seconda legge di Keplero…",
        "Riavvolgendo il nastro delle comete…",
        "Pettinando la chioma delle stelle…",
        "Cercando le chiavi della navetta…",
        "Gonfiando il jetpack…",
        "Tarando l'altimetro al millimetro…",
        "Contando i crateri (ne mancava uno)…",
        "Convincendo la luna a non scappare…",
        "Sturando gli ugelli dei motori…",
        "Riscaldando il caffè dell'astronauta…",
        "Allacciando le cinture invisibili…",
        "Misurando il diametro a occhio…",
        "Spingendo i pianeti più piccoli da parte…",
        "Limando gli spigoli del cubo-sfera…",
        "Dando la cera allo scafo…",
        "Sussurrando alle particelle di polvere…",
        "Raddrizzando l'asse di rotazione…",
        "Cercando il nord magnetico (non c'è)…",
        "Riempiendo i serbatoi di delta-v…",
        "Ordinando le stelle per luminosità…",
        "Lucidando le lenti del telescopio…",
        "Soffiando via la regolite dalle suole…",
        "Controllando che la gravità tiri in giù…",
        "Insegnando ai crateri a stare in fila…",
        "Calcolando rotte che nessuno percorrerà…",
        "Mescolando l'atmosfera (poca)…",
        "Accendendo il sole di riserva…",
        "Verificando i nodi delle orbite…",
        "Spostando l'origine fluttuante con discrezione…",
        "Convertendo i double in float (con cura)…",
        "Tappando le crepe nella tettonica…",
        "Cucendo le facce del pianeta…",
        "Distribuendo i sassi uno a uno…",
        "Chiamando l'autopilota (è in pausa)…",
        "Sincronizzando gli orologi orbitali…",
        "Riparando il reticolo di rotta…",
        "Riempiendo i mari (con il righello)…",
        "Aggiungendo zolfo dove serve il giallo…",
        "Spegnendo le stelle che disturbano…",
        "Calibrando il freno X…",
        "Dando un nome alle lune anonime…",
        "Spazzando la Via Lattea…",
        "Allineando l'eclittica con pazienza…",
        "Disegnando le costellazioni a mano libera…",
        "Contando fino a 2,37 milioni di stelle…",
        "Inseguendo un fotone in ritardo…",
        "Pulendo l'oculare dalle ditate…",
        "Aggiornando il manuale della tuta…",
        "Convincendo Vega a brillare di più…",
        "Misurando la pendenza con la livella…",
        "Posando le zampe della sonda con delicatezza…",
        "Riavviando la luce ambientale…",
        "Lucidando il puntino del mirino…",
        "Sigillando il casco (di nuovo)…",
        "Controllando l'ossigeno (per scaramanzia)…",
        "Calcolando dove cadrà il giocatore…",
        "Stirando le orbite ellittiche…",
        "Riportando a casa una cometa smarrita…",
        "Riempiendo i crateri di ombra…",
        "Allineando i tre raggi di diffrazione…",
        "Asciugando i mari liquidi…",
        "Spostando un continente di un pelo…",
        "Tarando il jetpack anti-galleggiamento…",
        "Avvisando i pianeti che si parte…",
        "Contando le ottave del rumore…",
        "Negoziando l'attrito con il vuoto…",
        "Riordinando la free-list della VRAM…",
        "Convincendo la GPU a collaborare…",
        "Stappando una nuova faccia del cubo…",
        "Riscaldando il compute su Metal…",
        "Distribuendo i crateri per taglia…",
        "Verificando la parità GPU↔CPU…",
        "Spazzolando i bordi dei crateri…",
        "Lustrando i raggi del sole…",
        "Riempiendo i secchielli di delta-v…",
        "Disegnando un'orbita che brilli…",
        "Spruzzando un po' di Fresnel sul mare…",
        "Centrando il baricentro del binario…",
        "Aspettando che la regolite si posi…",
        "Sussurrando coordinate alla sonda…",
        "Lucidando la croce… anzi, il cerchietto del mirino…",
        "Salvando una foto del cielo…",
        "Misurando l'abitabilità con ottimismo…",
        "Aggiungendo elio-3 alle scorte…",
        "Bilanciando le placche tettoniche…",
        "Caricando la curiosità del giocatore…",
        "Spolverando la lente rossa della sonda…",
        "Riallineando il muso al bersaglio…",
        "Tenendo ferma la destinazione che orbita…",
        "Insaponando i pannelli solari…",
        "Riempiendo lo spazio profondo di stelle…",
        "Mettendo i puntini sulle costellazioni…",
        "Convincendo la mappa a non tremare…",
        "Calcolando un sorvolo perfetto…",
        "Riportando l'autopilota alla calma…",
        "Tarando la rotella del telescopio…",
        "Dando un'occhiata oltre il far-clip…",
        "Contando le facce del cubo (sono sei)…",
        "Spianando una pista d'atterraggio…",
        "Riempiendo il vuoto, ma non troppo…",
        "Annodando la scia della cometa…",
        "Sgrassando i finestrini del casco…",
        "Convincendo un cratere a essere tondo…",
        "Calibrando il senso di vertigine…",
        "Riscaldando i motori a freddo…",
        "Cercando un pianeta dove parcheggiare…",
        "Lucidando l'anello del mirino (poco)…",
        "Pronti? Quasi. Ancora un cratere…",
    };

    bool done;
    float alpha = 1f;
    int msgIdx;
    float nextMsg;
    int[] order;        // ordine CASUALE di presentazione (playlist mescolata)
    int orderPos;
    Texture2D spinnerTex, white;
    GUIStyle msgStyle, tipStyle;

    public void Done() => done = true;

    // Prossimo messaggio in ORDINE CASUALE: scorre una playlist mescolata (Fisher-Yates) e la rimescola a fine giro →
    // ordine imprevedibile a ogni caricamento, ma senza ripetere finché non le ha mostrate tutte.
    int NextMessageIndex()
    {
        if (order == null || order.Length != Messages.Length)
        {
            order = new int[Messages.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Shuffle();
            orderPos = 0;
        }
        int idx = order[orderPos++];
        if (orderPos >= order.Length) { Shuffle(); orderPos = 0; }
        return idx;
    }

    void Shuffle()
    {
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
    }

    void Update()
    {
        if (done)
        {
            alpha = Mathf.MoveTowards(alpha, 0f, Time.unscaledDeltaTime * 1.8f);
            if (alpha <= 0f) Destroy(gameObject);
        }
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        float ui = Mathf.Max(1f, Screen.height / 1080f);
        EnsureAssets(ui);

        float rt = Time.realtimeSinceStartup;
        if (rt >= nextMsg) { msgIdx = NextMessageIndex(); nextMsg = rt + 0.9f; }

        Color prev = GUI.color;
        // fondo scuro (dissolve in uscita)
        GUI.color = new Color(0.02f, 0.02f, 0.04f, alpha);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), white);

        // SPINNER: arco che ruota attorno al centro (sui tempi reali → gira quando il thread è libero)
        float cx = Screen.width * 0.5f, cy = Screen.height * 0.46f, r = 34f * ui;
        GUI.color = new Color(0.55f, 0.9f, 1f, alpha);
        Matrix4x4 m = GUI.matrix;
        GUIUtility.RotateAroundPivot(rt * 220f, new Vector2(cx, cy));
        GUI.DrawTexture(new Rect(cx - r, cy - r, r * 2f, r * 2f), spinnerTex);
        GUI.matrix = m;

        // messaggio buffo + TITOLO (grande, FAUX-BOLD spinto + spaziatura, appena trasparente)
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.Label(new Rect(0, cy + r + 22f * ui, Screen.width, 30f * ui), Messages[msgIdx], msgStyle);
        Rect tr = new Rect(0, cy - r - 104f * ui, Screen.width, 80f * ui);
        DrawFauxBold(tr, "W A N D E R E R", tipStyle, new Color(0.72f, 0.86f, 1f, alpha * 0.82f), 1.6f * ui);
        GUI.color = prev;
    }

    // "grassetto finto": disegna il testo più volte a piccoli scostamenti → glifi più spessi di Arial Bold (≈ Arial Black).
    static void DrawFauxBold(Rect r, string text, GUIStyle style, Color col, float o)
    {
        GUI.color = col;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                GUI.Label(new Rect(r.x + dx * o, r.y + dy * o, r.width, r.height), text, style);
    }

    void EnsureAssets(float ui)
    {
        if (white == null) white = Texture2D.whiteTexture;
        if (spinnerTex == null)
        {
            // arco (ring con coda che sfuma): alfa = banda sottile attorno a r≈0.8, modulata sull'angolo (coda)
            int n = 128; spinnerTex = new Texture2D(n, n, TextureFormat.RGBA32, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float rr = 2f * Mathf.Sqrt(dx * dx + dy * dy);
                    float band = 1f - Mathf.Clamp01(Mathf.Abs(rr - 0.8f) / 0.12f);
                    float ang = Mathf.Atan2(dy, dx) / (2f * Mathf.PI) + 0.5f;   // 0..1 attorno
                    float tail = ang;                                          // coda: sfuma da 1 a 0 lungo il giro
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(band * tail) * 255f));
                }
            spinnerTex.SetPixels32(px); spinnerTex.Apply(true);
        }
        if (msgStyle == null)
        {
            msgStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            tipStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.55f, 0.85f, 1f) } };
        }
        msgStyle.fontSize = Mathf.RoundToInt(19f * ui);
        tipStyle.fontSize = Mathf.RoundToInt(60f * ui);   // titolo grande
        tipStyle.fontStyle = FontStyle.Bold;
    }
}

using UnityEngine;

/// <summary>
/// SONDA alla Outer Wilds (entro un sistema). Un piccolo oggetto fisico che LANCI: vola sotto la gravità RADIALE
/// SOMMATA di tutti i corpi (stessa contabilità del <see cref="PlanetWalker"/>) e COLLIDE in modo ANALITICO col
/// terreno (quota della sonda vs <see cref="PlanetTerrain.SampleHeight"/> nella sua direzione, ogni FixedUpdate —
/// niente collider mesh). Entro un sistema (~130 km) la doppia precisione + floating origin reggono benissimo.
///
/// È ADDITIVA, non tocca le fondamenta:
///  - si registra in <see cref="SolarSystem.Loose"/> → trasla con l'origine al cambio d'ancora (niente salti);
///  - si registra in <see cref="GpuPlanetRenderer.ExtraViewpoints"/> → il renderer dà DETTAGLIO LOD ai corpi che la
///    sonda guarda da vicino e NON li culla (la "foto da lontano" mostra terreno vero, non una sfera liscia);
///  - ha una camera propria per la FOTO.
/// La gestione input (lancio/vista/richiamo/foto) sta in <see cref="ProbeController"/>.
/// </summary>
public class Probe : MonoBehaviour
{
    Rigidbody rb;
    SolarSystem solar;
    Camera cam;
    GameObject visual;   // corpo metallico + scanalatura luminosa + luce: spento mentre guardi in prima persona
    Transform camGadget; // piccola telecamera sul guscio: scivola sulla sfera puntando dove guardi (solo COSMETICA, vista da fuori)
    Light lamp;          // la lampada della sonda: registrata come luce ausiliaria del terreno mentre è in volo/posata
    bool landed;
    CelestialBody landedOn;
    Vector3 landedLocal;   // posizione di posa in coordinate LOCALI del corpo → resta incollata alla superficie anche
                           // mentre il corpo orbita / la floating origin ri-centra (era la causa del lento sprofondamento)
    Quaternion landedLocalRot = Quaternion.identity;   // ORIENTAMENTO di posa nel frame del corpo → i piedi restano
                                                       // poggiati alla stessa pendenza anche se il pianeta ruota/orbita

    public static Probe Instance { get; private set; }   // l'unica sonda (per la mappa: marker "SONDA")

    public Camera Cam => cam;
    public bool Landed => landed;
    public CelestialBody LandedOn => landedOn;
    public GameObject Visual => visual;
    // FreezeOrient: mentre guardi ATTRAVERSO la sonda, NON la riorientare lungo la velocità → il frame resta fermo e
    // il free-look del mouse (che ruota la camera-figlia) non "combatte" con la rotazione della sonda.
    public bool FreezeOrient;

    const float ProbeRadius = 0.6f;   // mezza taglia della sonda (per la collisione e la mesh visiva)
    const float LegSpread = ProbeRadius * 1.7f;   // raggio orizzontale dei piedi (le zampe si allargano oltre il corpo, come il LM)
    const float LegDrop   = ProbeRadius * 1.2f;   // quanto i piedi scendono sotto il centro del corpo
    const float LandClearance = LegDrop;          // a riposo il centro corpo sta LegDrop sopra il punto di contatto del piede

    public static Probe Spawn(SolarSystem s)
    {
        var go = new GameObject("Sonda");
        var p = go.AddComponent<Probe>();
        p.solar = s;
        Instance = p;

        p.rb = go.AddComponent<Rigidbody>();
        p.rb.useGravity = false;                 // gravità a mano (radiale sommata), come il walker
        p.rb.mass = 1f;
        p.rb.interpolation = RigidbodyInterpolation.Interpolate;
        p.rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

        // VISUALE (look hi-tech / "sfera dei Pokémon"): corpo metallico opaco + scanalatura equatoriale LUMINOSA +
        // luce emessa. Sotto un nodo "Visual" che si spegne mentre guardi in prima persona (non vedi la tua sonda).
        var vis = new GameObject("Visual");
        vis.transform.SetParent(go.transform, false);

        // CORPO: sfera METALLICA ad alta risoluzione con SOLCHI VERI INCISI (ProcMesh.GroovedSphere): il raggio è
        // SCAVATO verso l'interno alle 3 latitudini (equatore + tropici) → canali reali con pareti che catturano la
        // luce ("groove" netti, Image #14), non bande dipinte. Mesh raggio 1 → scala = ProbeRadius. Standard acciaio,
        // illuminata dal sole direzionale di Unity (il terreno GPU ignora le luci Unity, una mesh Standard no).
        var body = new GameObject("Corpo");
        body.transform.SetParent(vis.transform, false);
        body.transform.localScale = Vector3.one * ProbeRadius;
        var mf = body.AddComponent<MeshFilter>();
        float[] gLat = { 0f, 23.4f * Mathf.Deg2Rad, -23.4f * Mathf.Deg2Rad };
        mf.sharedMesh = ProcMesh.GroovedSphere(160, 96, gLat, 6f * Mathf.Deg2Rad, 0.13f);
        var mr = body.AddComponent<MeshRenderer>();
        var std = Shader.Find("Standard");
        if (std != null)
        {
            var bm = new Material(std) { color = new Color(0.5f, 0.53f, 0.58f) };   // acciaio chiaro
            bm.SetFloat("_Metallic", 0.9f);
            bm.SetFloat("_Glossiness", 0.7f);
            mr.material = bm;
        }

        // Linea luminosa RECESSA nel fondo di ogni solco (Unlit ciano → glow dentro il canale; widthFactor<1 = sta
        // sotto la superficie, nel canale, non sporge). Unlit/Color = niente variante _EMISSION strippata in build.
        var unlit = Shader.Find("Unlit/Color");
        Color glow = new Color(0.55f, 0.95f, 1f);
        MakeGroove(vis.transform, unlit, ProbeRadius, 0f, 0.90f, 0.03f, glow);     // equatore
        MakeGroove(vis.transform, unlit, ProbeRadius, 23.4f, 0.90f, 0.03f, glow);  // tropico nord
        MakeGroove(vis.transform, unlit, ProbeRadius, -23.4f, 0.90f, 0.03f, glow); // tropico sud

        // TELECAMERA sul guscio (COSMETICA): un modellino di macchina fotografica montato sulla superficie della sfera.
        // Scivola lungo il guscio puntando dove guardi (lo guida ProbeController via SetGaze, dalla direzione di sguardo).
        // Figlia di Visual → visibile da fuori (terza persona / mappa / volo), sparisce in prima persona col resto della sfera.
        p.camGadget = BuildCameraGadget(vis.transform, ProbeRadius, std, unlit).transform;

        // ZAMPE D'ATTERRAGGIO stile modulo lunare Apollo (figlie di Visual). Fisse nel frame della sonda: è il CORPO che
        // si orienta alla normale della superficie all'atterraggio → i piedi poggiano a qualunque pendenza.
        BuildLegs(vis.transform, ProbeRadius, std);

        // LUCE EMESSA: point light. Illumina gli oggetti Standard vicini E il TERRENO GPU (registrata come luce
        // ausiliaria in GpuPlanetRenderer.AuxPointLight da Launch → il terreno la calcola, come la torcia).
        // Figlia di GO (NON di Visual) → resta accesa anche in VISTA SONDA (Visual si spegne, ma la luce no) →
        // guardando attraverso la sonda vedi l'ambiente illuminato.
        var lampGo = new GameObject("SondaLuce");
        lampGo.transform.SetParent(go.transform, false);
        p.lamp = lampGo.AddComponent<Light>();
        p.lamp.type = LightType.Point;
        p.lamp.color = new Color(0.85f, 0.92f, 1f);   // meno blu (quasi bianco, appena freddo)
        p.lamp.intensity = 2.8f;   // profilo morbido (plateau nel terrain shader): niente hotspot accecante
        p.lamp.range = 130f;       // ILLUMINA LONTANO: una sonda in una stanza la illumina tutta (orientarsi al buio)

        // BAGLIORE (halo): quad additivo billboard attorno alla sonda → si vede che è luminosa e proietta luce.
        // Sotto Visual (sparisce in prima persona). Cull Off nello shader: l'orientamento serve solo a tenerlo piatto a schermo.
        var glowSh = Shader.Find("Wanderer/AdditiveGlow");
        if (glowSh != null)
        {
            var halo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var hc = halo.GetComponent<Collider>(); if (hc != null) Destroy(hc);
            halo.name = "Bagliore";
            halo.transform.SetParent(vis.transform, false);
            halo.transform.localScale = Vector3.one * (ProbeRadius * 7f);
            halo.GetComponent<Renderer>().material = new Material(glowSh) { color = new Color(0.45f, 0.85f, 1f, 1f) };
            halo.AddComponent<Billboard>();
        }

        p.visual = vis;

        // camera della sonda (per la foto): spenta finché non guardi attraverso di lei. NON taggata MainCamera, così
        // Camera.main e il LOD del renderer continuano a usare la camera del giocatore (la sonda dà dettaglio via
        // ExtraViewpoints). PRIMA PERSONA: al CENTRO della sonda, un filo verso l'alto-locale (resta sopra il suolo
        // anche da posata → niente clipping sottoterra). La mesh si spegne mentre guardi (non vedi la tua stessa sonda).
        // GRANDANGOLO spinto (FOV alto) → vista d'insieme, capisci dove sei. Il free-look (mouse) lo guida ProbeController.
        var camGo = new GameObject("SondaCam");
        camGo.transform.SetParent(go.transform, false);
        camGo.transform.localPosition = new Vector3(0f, 0.35f, 0f);
        camGo.transform.localRotation = Quaternion.identity;
        p.cam = camGo.AddComponent<Camera>();
        p.cam.nearClipPlane = 0.2f;
        p.cam.farClipPlane = 200000f;
        p.cam.clearFlags = CameraClearFlags.SolidColor;
        p.cam.backgroundColor = new Color(0.01f, 0.01f, 0.03f);
        p.cam.fieldOfView = 95f;   // grandangolo spinto
        p.cam.enabled = false;

        go.SetActive(false);   // dormiente finché non viene lanciata
        return p;
    }

    /// <summary>Un SOLCO luminoso a una latitudine data: disco sottile (sfera schiacciata su Y) che sporge appena dal
    /// corpo (widthFactor>1 → "buca il metallo") all'altezza lat. ringR=cos(lat) = raggio del cerchio a quella
    /// latitudine; offset y=sin(lat)·R. thick = spessore (frazione di R).</summary>
    static void MakeGroove(Transform parent, Shader unlit, float R, float latDeg, float widthFactor, float thick, Color col)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var c = g.GetComponent<Collider>(); if (c != null) Destroy(c);
        g.transform.SetParent(parent, false);
        float lat = latDeg * Mathf.Deg2Rad;
        float ringR = Mathf.Cos(lat);
        g.transform.localPosition = new Vector3(0f, Mathf.Sin(lat) * R, 0f);
        g.transform.localScale = new Vector3(R * 2f * ringR * widthFactor, R * thick, R * 2f * ringR * widthFactor);
        if (unlit != null) g.GetComponent<Renderer>().material = new Material(unlit) { color = col };
    }

    /// <summary>Costruisce il modellino di telecamera (corpo scuro + barile + lente luminosa ciano) assemblato da
    /// primitive, orientato a guardare lungo il +Z LOCALE (= direzione di sguardo). Il nodo restituito si MONTA poi sul
    /// guscio via <see cref="SetGaze"/>: localPosition = dir·R (sulla superficie), localRotation = LookRotation(dir).
    /// `cs` = taglia base della telecamera (frazione del raggio sonda → "piccola" ma leggibile).</summary>
    static GameObject BuildCameraGadget(Transform parent, float R, Shader std, Shader unlit)
    {
        var node = new GameObject("Telecamera");
        node.transform.SetParent(parent, false);
        node.transform.localPosition = Vector3.forward * R;   // default: sul muso (sovrascritto da SetGaze)
        float cs = R * 0.15f;   // taglia della telecamerina = 15% del raggio sonda

        Material body = null;
        if (std != null)
        {
            body = new Material(std) { color = new Color(0.12f, 0.13f, 0.16f) };   // corpo scuro tipo macchina fotografica
            body.SetFloat("_Metallic", 0.6f);
            body.SetFloat("_Glossiness", 0.5f);
        }
        Material lens = unlit != null ? new Material(unlit) { color = new Color(1f, 0.12f, 0.06f) } : null;   // vetro lente: glow ROSSO (effetto HAL 9000)

        var fwd = Quaternion.identity;
        var axisZ = Quaternion.Euler(90f, 0f, 0f);   // i Cylinder hanno l'asse su Y → ruoto perché vada lungo Z (uscente)

        // base/torretta appoggiata al guscio · corpo macchina · mirino in cima · barile della lente · vetro luminoso davanti
        Prim(PrimitiveType.Cylinder, node.transform, new Vector3(0f, 0f, cs * 0.15f), axisZ, new Vector3(cs * 0.95f, cs * 0.12f, cs * 0.95f), body);
        Prim(PrimitiveType.Cube,     node.transform, new Vector3(0f, 0f, cs * 0.75f), fwd,   new Vector3(cs * 1.10f, cs * 0.95f, cs * 1.25f), body);
        Prim(PrimitiveType.Cube,     node.transform, new Vector3(0f, cs * 0.62f, cs * 0.55f), fwd, new Vector3(cs * 0.50f, cs * 0.35f, cs * 0.55f), body);
        Prim(PrimitiveType.Cylinder, node.transform, new Vector3(0f, 0f, cs * 1.50f), axisZ, new Vector3(cs * 0.60f, cs * 0.50f, cs * 0.60f), body);
        Prim(PrimitiveType.Sphere,   node.transform, new Vector3(0f, 0f, cs * 1.95f), fwd,   new Vector3(cs * 0.42f, cs * 0.42f, cs * 0.25f), lens);
        return node;
    }

    /// <summary>Zampe d'atterraggio stile modulo lunare Apollo: una basetta/hub sul fondo + 4 gambe a raggiera con
    /// controvento ("A-frame" del LM) e piede a disco dorato. Sono FISSE nel frame della sonda (a differenza della
    /// telecamera che scivola): all'atterraggio è il CORPO a orientarsi alla normale della superficie, così i piedi
    /// poggiano sempre. Figlie di Visual → visibili da fuori, via in prima persona.</summary>
    static void BuildLegs(Transform parent, float R, Shader std)
    {
        Material strut = null, pad = null;
        if (std != null)
        {
            strut = new Material(std) { color = new Color(0.20f, 0.21f, 0.24f) };   // struttura scura
            strut.SetFloat("_Metallic", 0.7f); strut.SetFloat("_Glossiness", 0.45f);
            pad = new Material(std) { color = new Color(0.80f, 0.62f, 0.26f) };     // piede dorato (foglio Apollo)
            pad.SetFloat("_Metallic", 0.85f); pad.SetFloat("_Glossiness", 0.55f);
        }

        // basetta/hub sul fondo del corpo (disco basso = "la basetta con le zampette")
        Prim(PrimitiveType.Cylinder, parent, new Vector3(0f, -R * 0.92f, 0f), Quaternion.identity,
             new Vector3(R * 0.5f, R * 0.12f, R * 0.5f), strut);

        for (int i = 0; i < 4; i++)
        {
            float a = (45f + 90f * i) * Mathf.Deg2Rad;
            Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));      // direzione orizzontale della gamba
            Vector3 top  = d * (R * 0.50f) + Vector3.up * (-R * 0.35f);   // attacco al corpo (basso, sul fianco)
            Vector3 foot = d * LegSpread   + Vector3.up * (-LegDrop);     // piede (largo e in basso)

            Strut(parent, top, foot, R * 0.07f, strut);                  // gamba principale inclinata
            Vector3 brace = d * (R * 0.18f) + Vector3.up * (R * 0.05f);   // controvento secondario (look a "A" del LM)
            Strut(parent, brace, Vector3.Lerp(top, foot, 0.55f), R * 0.045f, strut);
            Prim(PrimitiveType.Cylinder, parent, foot, Quaternion.identity,   // piede a disco, piatto (asse Y → giace orizzontale)
                 new Vector3(R * 0.50f, R * 0.05f, R * 0.50f), pad);
        }
    }

    /// <summary>Un puntone cilindrico fra due punti A e B (lunghezza lungo l'asse Y del cilindro), spessore dato.</summary>
    static void Strut(Transform parent, Vector3 a, Vector3 b, float thick, Material mat)
    {
        Vector3 axis = b - a; float len = axis.magnitude;
        if (len < 1e-5f) return;
        Quaternion rot = Quaternion.FromToRotation(Vector3.up, axis / len);
        Prim(PrimitiveType.Cylinder, parent, (a + b) * 0.5f, rot, new Vector3(thick, len * 0.5f, thick), mat);
    }

    /// <summary>Crea una primitiva figlia senza collider, con trasform e materiale dati (helper per il modellino).</summary>
    static void Prim(PrimitiveType t, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, Material mat)
    {
        var g = GameObject.CreatePrimitive(t);
        var c = g.GetComponent<Collider>(); if (c != null) Destroy(c);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localRotation = rot;
        g.transform.localScale = scale;
        if (mat != null) g.GetComponent<Renderer>().sharedMaterial = mat;
    }

    /// <summary>Posiziona/orienta la telecamera cosmetica sul guscio data la direzione di sguardo in coordinate LOCALI
    /// della sonda (= forward della camera in prima persona). Scivola sulla superficie (localPos = dir·ProbeRadius) e
    /// punta in fuori lungo dir. La chiama <see cref="ProbeController"/> quando muovi la visuale.</summary>
    public void SetGaze(Vector3 localDir)
    {
        if (camGadget == null) return;
        if (localDir.sqrMagnitude < 1e-6f) localDir = Vector3.forward;
        localDir.Normalize();
        camGadget.localPosition = localDir * ProbeRadius;
        // up di riferimento stabile: 'su' locale, tranne quando guardi quasi a picco (dir ∥ up) → uso 'destra' per non degenerare
        Vector3 up = Mathf.Abs(Vector3.Dot(localDir, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
        camGadget.localRotation = Quaternion.LookRotation(localDir, up);
    }

    /// <summary>Lancia la sonda da una posizione con una velocità iniziale (di solito = velocità del giocatore +
    /// muso×spinta). Si registra come oggetto sciolto e come viewpoint extra del renderer.</summary>
    public void Launch(Vector3 pos, Vector3 vel, Vector3 lookDir)
    {
        gameObject.SetActive(true);
        rb.isKinematic = false;   // torna dinamica per il volo (a terra diventa kinematica e si incolla al corpo)
        transform.position = pos;
        if (lookDir.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
        rb.position = pos;
        rb.linearVelocity = vel;
        rb.angularVelocity = Vector3.zero;
        landed = false; landedOn = null;

        if (solar != null && !solar.Loose.Contains(transform)) solar.Loose.Add(transform);
        if (!GpuPlanetRenderer.ExtraViewpoints.Contains(transform)) GpuPlanetRenderer.ExtraViewpoints.Add(transform);
        GpuPlanetRenderer.AuxPointLight = lamp;   // la sua lampada illumina il terreno GPU attorno
    }

    /// <summary>Richiama la sonda: la toglie dalla scena e dai registri (Loose + viewpoint). Riusabile con Launch.</summary>
    public void Recall()
    {
        if (solar != null) solar.Loose.Remove(transform);
        GpuPlanetRenderer.ExtraViewpoints.Remove(transform);
        if (GpuPlanetRenderer.AuxPointLight == lamp) GpuPlanetRenderer.AuxPointLight = null;
        landed = false; landedOn = null;
        if (cam != null) cam.enabled = false;
        gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (solar != null) solar.Loose.Remove(transform);
        GpuPlanetRenderer.ExtraViewpoints.Remove(transform);
        if (GpuPlanetRenderer.AuxPointLight == lamp) GpuPlanetRenderer.AuxPointLight = null;
        if (Instance == this) Instance = null;
    }

    void FixedUpdate()
    {
        if (rb == null) return;
        // POSATA: incollata al corpo. Ri-derivo la posizione dal transform del pianeta OGNI tick (in coord locali del
        // corpo) → segue la superficie mentre il corpo orbita e mentre la floating origin ri-centra la scena. Senza
        // questo la sonda restava ferma in scena e il pianeta le scorreva sopra → sprofondava lentamente sottoterra.
        if (landed)
        {
            if (landedOn != null)
            {
                Vector3 p = landedOn.transform.TransformPoint(landedLocal);
                rb.position = p; transform.position = p;
                Quaternion q = landedOn.transform.rotation * landedLocalRot;   // ri-applico anche l'orientamento di posa
                rb.rotation = q; transform.rotation = q;
            }
            return;
        }

        // corpo più vicino (riferimento di gravità/collisione): argmin sulla distanza, niente baricentri.
        CelestialBody planet = Nearest(out Vector3 toCenter, out float r);
        if (planet == null) return;

        // GRAVITÀ = somma vettoriale 1/r² di tutti i corpi (come il walker): in un binario niente salto di "giù".
        Vector3 up = r > 1e-3f ? -toCenter / r : transform.up;   // up = via dal centro (toCenter punta al centro)
        float rEff = Mathf.Max(r, (float)planet.Radius);
        Vector3 gravAccel = -up * (float)(planet.Mu / ((double)rEff * rEff));
        for (int i = 0; i < solar.Bodies.Count; i++)
        {
            var b = solar.Bodies[i];
            if (b == null || b == planet || b.Massless) continue;
            Vector3 toB = b.transform.position - rb.position;
            float rB = toB.magnitude;
            if (rB < 1e-3f) continue;
            float rEffB = Mathf.Max(rB, (float)b.Radius);
            gravAccel += (toB / rB) * (float)(b.Mu / ((double)rEffB * rEffB));
        }
        rb.AddForce(gravAccel, ForceMode.Acceleration);

        // orienta il muso lungo la velocità (per la camera) finché vola — MA non mentre la guardi (free-look fermo)
        Vector3 v = rb.linearVelocity;
        if (!FreezeOrient && v.sqrMagnitude > 1f) transform.rotation = Quaternion.LookRotation(v.normalized, up);

        // COLLISIONE ANALITICA: quota della sonda vs altezza del terreno nella sua direzione (come il walker, ma per
        // sapere SE ha toccato, non per camminarci). Campiono il rilievo solo vicino alla superficie (altrove i bump
        // sono irrilevanti e la pipeline crateri girerebbe per niente).
        float surface = (float)planet.Radius;
        Vector3 normal = up;   // normale della superficie: radiale di default (corpo liscio / fuori dalla banda di rilievo)
        double band = System.Math.Max(60.0, planet.Radius * 0.5);
        if (r < planet.Radius + band && planet.TryGetComponent<PlanetTerrain>(out var terr))
        {
            float s = terr.SampleHeight(up);
            if (!float.IsNaN(s) && !float.IsInfinity(s)) surface = s;
            // NORMALE locale dalla pendenza: l'eps angolare campiona il rilievo a ~1.5 m laterali (taglia del lander),
            // così l'orientamento segue la PENDENZA reale e non i sassolini. Stesso frame di SampleHeight(up).
            float eps = Mathf.Max(1e-4f, 1.5f / (float)planet.Radius);
            Vector3 sn = terr.SurfaceNormal(up, eps);
            if (sn.sqrMagnitude > 1e-6f && !float.IsNaN(sn.x)) normal = sn.normalized;
        }
        if (r <= surface + LandClearance)
        {
            // impatto/aggancio: i PIEDI poggiano sul suolo e il moto si ferma. Il corpo si solleva di LandClearance
            // LUNGO LA NORMALE (non lungo il raggio) → su una pendenza il lander è inclinato come la superficie, su uno
            // strapiombo/soffitto si rovescia: la basetta con le zampe guarda sempre verso la superficie.
            Vector3 contact = planet.transform.position + up * surface;
            Vector3 rest = contact + normal * LandClearance;
            rb.position = rest; transform.position = rest;
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            if (!FreezeOrient)   // mentre la guardi, non scattare l'orientamento di atterraggio (il free-look resta stabile)
            {
                // +Y del corpo = normale (zampe verso il suolo); il muso = forward proiettato sul piano della superficie.
                Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, normal);
                if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.ProjectOnPlane(transform.right, normal);
                if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.ProjectOnPlane(Vector3.forward, normal);
                transform.rotation = Quaternion.LookRotation(fwd.normalized, normal);
            }
            landed = true; landedOn = planet;
            landedLocal = planet.transform.InverseTransformPoint(rest);                       // posa NEL frame del corpo (vi resta incollata)
            landedLocalRot = Quaternion.Inverse(planet.transform.rotation) * transform.rotation;   // anche l'orientamento, nel frame del corpo
            rb.isKinematic = true;   // niente più simulazione fisica: posizione+orientamento li guida l'aggancio al corpo
        }
    }

    CelestialBody Nearest(out Vector3 toCenter, out float r)
    {
        CelestialBody best = null; float bd = float.MaxValue; toCenter = Vector3.zero; r = 0f;
        for (int i = 0; i < solar.Bodies.Count; i++)
        {
            var b = solar.Bodies[i];
            if (b == null || b.Massless) continue;
            Vector3 tc = b.transform.position - rb.position;
            float d2 = tc.sqrMagnitude;
            if (d2 < bd) { bd = d2; best = b; toCenter = tc; }
        }
        if (best != null) r = toCenter.magnitude;
        return best;
    }
}

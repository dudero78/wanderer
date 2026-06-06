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
    bool landed;
    CelestialBody landedOn;
    Vector3 landedLocal;   // posizione di posa in coordinate LOCALI del corpo → resta incollata alla superficie anche
                           // mentre il corpo orbita / la floating origin ri-centra (era la causa del lento sprofondamento)

    public Camera Cam => cam;
    public bool Landed => landed;
    public CelestialBody LandedOn => landedOn;
    public GameObject Visual => visual;
    // FreezeOrient: mentre guardi ATTRAVERSO la sonda, NON la riorientare lungo la velocità → il frame resta fermo e
    // il free-look del mouse (che ruota la camera-figlia) non "combatte" con la rotazione della sonda.
    public bool FreezeOrient;

    const float ProbeRadius = 0.6f;   // mezza taglia della sonda (per la collisione e la mesh visiva)

    public static Probe Spawn(SolarSystem s)
    {
        var go = new GameObject("Sonda");
        var p = go.AddComponent<Probe>();
        p.solar = s;

        p.rb = go.AddComponent<Rigidbody>();
        p.rb.useGravity = false;                 // gravità a mano (radiale sommata), come il walker
        p.rb.mass = 1f;
        p.rb.interpolation = RigidbodyInterpolation.Interpolate;
        p.rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

        // VISUALE (look hi-tech / "sfera dei Pokémon"): corpo metallico opaco + scanalatura equatoriale LUMINOSA +
        // luce emessa. Sotto un nodo "Visual" che si spegne mentre guardi in prima persona (non vedi la tua sonda).
        var vis = new GameObject("Visual");
        vis.transform.SetParent(go.transform, false);

        // CORPO: sfera Standard METALLICA opaca (grigio acciaio), illuminata dal sole direzionale di Unity → riflessi
        // metallici veri. (Il terreno GPU ignora le luci Unity, ma una mesh Standard no: il sole la illumina.)
        var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var bcol = body.GetComponent<Collider>(); if (bcol != null) Destroy(bcol);   // collisione analitica, niente collider
        body.transform.SetParent(vis.transform, false);
        body.transform.localScale = Vector3.one * (ProbeRadius * 2f);
        var std = Shader.Find("Standard");
        if (std != null)
        {
            var bm = new Material(std);
            bm.color = new Color(0.22f, 0.24f, 0.27f);   // acciaio scuro
            bm.SetFloat("_Metallic", 0.9f);
            bm.SetFloat("_Glossiness", 0.62f);
            body.GetComponent<Renderer>().material = bm;
        }

        // SCANALATURA: disco sottile equatoriale (sfera schiacciata sull'asse Y), Unlit CIANO brillante → linea che
        // GLOWa attorno al centro (look pokeball/hi-tech). Unlit/Color = niente variante _EMISSION strippata in build.
        var groove = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var gcol = groove.GetComponent<Collider>(); if (gcol != null) Destroy(gcol);
        groove.transform.SetParent(vis.transform, false);
        groove.transform.localScale = new Vector3(ProbeRadius * 2.08f, ProbeRadius * 0.18f, ProbeRadius * 2.08f);
        var unlit = Shader.Find("Unlit/Color");
        if (unlit != null) groove.GetComponent<Renderer>().material = new Material(unlit) { color = new Color(0.55f, 0.95f, 1f) };

        // LUCE EMESSA: point light ciano → la sonda è una piccola lampada (illumina oggetti Standard vicini e si
        // legge come sorgente luminosa). NB: il terreno GPU usa luce MANUALE (sole+torcia) e non la cattura ancora.
        var lampGo = new GameObject("SondaLuce");
        lampGo.transform.SetParent(vis.transform, false);
        var lamp = lampGo.AddComponent<Light>();
        lamp.type = LightType.Point;
        lamp.color = new Color(0.6f, 0.95f, 1f);
        lamp.intensity = 2.5f;
        lamp.range = 14f;

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
    }

    /// <summary>Richiama la sonda: la toglie dalla scena e dai registri (Loose + viewpoint). Riusabile con Launch.</summary>
    public void Recall()
    {
        if (solar != null) solar.Loose.Remove(transform);
        GpuPlanetRenderer.ExtraViewpoints.Remove(transform);
        landed = false; landedOn = null;
        if (cam != null) cam.enabled = false;
        gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (solar != null) solar.Loose.Remove(transform);
        GpuPlanetRenderer.ExtraViewpoints.Remove(transform);
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
        double band = System.Math.Max(60.0, planet.Radius * 0.5);
        if (r < planet.Radius + band && planet.TryGetComponent<PlanetTerrain>(out var terr))
        {
            float s = terr.SampleHeight(up);
            if (!float.IsNaN(s) && !float.IsInfinity(s)) surface = s;
        }
        if (r <= surface + ProbeRadius)
        {
            // impatto/aggancio: posa la sonda sul suolo e ferma il moto (stilizzato: si pianta dove tocca).
            Vector3 rest = planet.transform.position + up * (surface + ProbeRadius);
            rb.position = rest; transform.position = rest;
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            if (!FreezeOrient)   // mentre la guardi, non scattare l'orientamento di atterraggio (il free-look resta stabile)
            {
                Vector3 flat = Vector3.ProjectOnPlane(transform.forward, up);
                if (flat.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(flat.normalized, up);
            }
            landed = true; landedOn = planet;
            landedLocal = planet.transform.InverseTransformPoint(rest);   // memorizza la posa NEL frame del corpo (vi resta incollata)
            rb.isKinematic = true;   // niente più simulazione fisica: la posizione la guida l'aggancio al corpo
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

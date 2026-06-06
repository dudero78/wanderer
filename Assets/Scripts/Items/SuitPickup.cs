using UnityEngine;

/// <summary>
/// La tuta-jetpack, resa come pilastro-faro luminoso. La sua posizione viene calcolata
/// e passata dal bootstrap al caricamento (da dati noti e stabili: posizione di spawn +
/// altezza del terreno). Qui ci limitiamo a ondeggiare, ruotare e controllare la raccolta:
/// nessuna lettura di transform fisici al frame 0, nessuna race condition.
/// </summary>
public class SuitPickup : MonoBehaviour
{
    public Transform player;
    public PlanetWalker walker;
    public Vector3 groundPoint;        // base del pilastro, sul terreno
    public Vector3 axisUp = Vector3.up;
    public float surfaceClearance = 12f;   // metà altezza del pilastro: la base tocca il suolo
    public float pickupRadius = 5f;
    public float spinSpeed = 30f;
    public float bobAmplitude = 0.4f;
    public float bobSpeed = 2f;

    Vector3 basePos;

    public void Init(Transform player, PlanetWalker walker, Vector3 groundPoint, Vector3 axisUp)
    {
        this.player = player;
        this.walker = walker;
        this.groundPoint = groundPoint;
        this.axisUp = axisUp.normalized;
        basePos = groundPoint + this.axisUp * surfaceClearance;
        transform.position = basePos;
        // RIVOLTO AL GIOCATORE SOLO ALLO SPAWN: il "davanti" (+Z) punta verso di te, proiettato sul piano tangente
        // (resta in piedi). Poi NON si ri-orienta più → ci puoi girare intorno liberamente.
        Vector3 fwd = player != null ? Vector3.ProjectOnPlane(player.position - basePos, this.axisUp) : Vector3.zero;
        if (fwd.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(fwd.normalized, this.axisUp);
        else transform.up = this.axisUp;
    }

    void Update()
    {
        // solo ONDEGGIO (l'orientamento è fissato allo spawn in Init → ci puoi girare intorno).
        transform.position = basePos + axisUp * (Mathf.Sin(Time.time * bobSpeed) * bobAmplitude);

        if (player == null || walker == null) return;
        if ((player.position - groundPoint).sqrMagnitude <= pickupRadius * pickupRadius)
        {
            walker.EquipJetpack();
            // il modello del giocatore "indossa la tuta": omino col CASCO (resta del colore del giocatore).
            var avatar = player.GetComponentInChildren<PlayerAvatar>();
            if (avatar != null) avatar.OnSuitEquipped();
            // Nascondi SUBITO: Destroy è differito a fine frame, senza questo la capsula ciano
            // emissiva (e la sua luce) renderizzerebbero un frame a distanza ravvicinata,
            // inondando lo schermo di ciano (il "rettangolo" alla raccolta).
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
            foreach (var l in GetComponentsInChildren<Light>()) l.enabled = false;
            Destroy(gameObject);
        }
    }
}

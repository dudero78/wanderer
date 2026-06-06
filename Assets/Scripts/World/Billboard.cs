using UnityEngine;

/// <summary>
/// Tiene il piano dell'oggetto sempre RIVOLTO alla camera (per un alone/bagliore additivo che deve apparire come un
/// disco a schermo da qualunque angolo). Usa la camera principale (quella del giocatore); se serve, si aggancia
/// alla camera che sta renderizzando.
/// </summary>
public class Billboard : MonoBehaviour
{
    void LateUpdate()
    {
        var c = Camera.main;
        if (c == null) return;
        transform.rotation = Quaternion.LookRotation(transform.position - c.transform.position, c.transform.up);
    }
}

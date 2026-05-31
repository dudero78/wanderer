using UnityEngine;

/// <summary>
/// Torcia inclusa nella tuta: uno spotlight che segue lo sguardo (è figlio della camera).
/// Si accende/spegne con F, ma solo dopo aver raccolto la tuta. Prima resta spenta.
/// </summary>
public class Flashlight : MonoBehaviour
{
    public PlanetWalker walker;
    public Light lamp;
    public float onIntensity = 2.2f;
    public float baseRange = 110f;
    public KeyCode toggleKey = KeyCode.F;

    bool on;

    void Update()
    {
        if (lamp == null) return;

        // La luce resta SEMPRE enabled: commutiamo solo l'intensità.
        bool available = walker != null && walker.HasJetpack;
        if (!available) { on = false; lamp.intensity = 0f; return; }

        if (Input.GetKeyDown(toggleKey)) on = !on;

        // comportamento monotòno e fisico, niente picco-e-crollo:
        // - il range cresce con la quota, così il fascio raggiunge sempre il suolo (niente buio improvviso);
        // - un boost dolce con TETTO compensa l'inverso del quadrato salendo, poi si appiattisce.
        float alt = Mathf.Max(0f, walker.Altitude);
        // il range cresce con la quota ma con un TETTO: oltre ~150-200 m il suolo esce dalla
        // portata e la torcia si spegne da sola (irrealistico illuminarlo da centinaia di metri).
        lamp.range = Mathf.Min(baseRange + alt * 1.2f, 260f);
        float boost = 1f + Mathf.Min(alt, 120f) / 70f;   // ~1x a terra -> ~2.7x da 120 m in su (cap)
        lamp.intensity = on ? onIntensity * boost : 0f;
    }
}

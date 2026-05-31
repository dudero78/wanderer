using UnityEngine;

/// <summary>
/// Torcia inclusa nella tuta: uno spotlight che segue lo sguardo (è figlio della camera).
/// Si accende/spegne con F, ma solo dopo aver raccolto la tuta. Prima resta spenta.
/// </summary>
public class Flashlight : MonoBehaviour
{
    public PlanetWalker walker;
    public Light lamp;
    public KeyCode toggleKey = KeyCode.F;

    bool on;

    void Update()
    {
        if (lamp == null) return;

        // disponibile solo con la tuta equipaggiata
        if (walker == null || !walker.HasJetpack)
        {
            if (lamp.enabled) lamp.enabled = false;
            on = false;
            return;
        }

        if (Input.GetKeyDown(toggleKey))
        {
            on = !on;
            lamp.enabled = on;
        }
    }
}

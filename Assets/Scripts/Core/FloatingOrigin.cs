/// <summary>
/// L'origine fluttuante. Tiene il giocatore (e il corpo su cui sta) sempre
/// vicino a (0,0,0) di Unity, dove il float è preciso. Invece di muovere il
/// giocatore attraverso un universo enorme, spostiamo l'universo attorno a lui.
///
/// In questa demo l'origine è "ancorata" al pianeta: il pianeta resta fermo a
/// circa (0,0,0) e tutto il resto (la stella, le altre orbite) si muove. Così
/// camminare su un pianeta che orbita è stabile e non perdi mai precisione,
/// anche se in termini reali sei a decine di migliaia di unità dalla stella —
/// o, in futuro, a miliardi dal centro della galassia.
///
/// SceneOrigin è il punto dell'universo (in double) che attualmente corrisponde
/// all'origine di Unity. È l'unico stato globale di cui il rendering ha bisogno.
/// </summary>
public static class FloatingOrigin
{
    public static Vector3d SceneOrigin = Vector3d.Zero;

    /// <summary>Azzerata a ogni avvio in Play (i campi static sopravvivono ai replay nell'editor).</summary>
    public static void Reset() => SceneOrigin = Vector3d.Zero;
}

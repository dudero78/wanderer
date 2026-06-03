using UnityEditor;

/// <summary>
/// Collega il pulsante "Bake su disco" dell'editor di pianeti (PlanetEditor, assembly runtime) alla logica di
/// bake che vive nell'assembly Editor (PlanetBakeTool). Il runtime non può referenziare UnityEditor, quindi
/// l'Editor INIETTA la capacità via hook all'avvio. In una build il file Editor non c'è → l'hook resta null e
/// il pulsante avvisa (il bake è uno strumento di authoring, non una feature di gioco).
/// </summary>
[InitializeOnLoad]
public static class PlanetEditorBakeHook
{
    static PlanetEditorBakeHook()
    {
        PlanetEditor.BakeToDiskHook = PlanetBakeTool.BakeTerrainFromEditor;
    }
}

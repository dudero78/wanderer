using UnityEditor;

/// <summary>
/// Inietta nelle capacità dell'editor di pianeti (PlanetEditor, assembly runtime) le funzioni che vivono
/// nell'assembly Editor (UnityEditor): il bake su disco e il selettore file. Il runtime non può referenziare
/// UnityEditor, quindi l'Editor le INIETTA via hook all'avvio. In una build i file Editor non ci sono → gli hook
/// restano null e i pulsanti ripiegano (bake avvisa; "Carica" usa il nome digitato).
/// </summary>
[InitializeOnLoad]
public static class PlanetEditorBakeHook
{
    static PlanetEditorBakeHook()
    {
        PlanetEditor.BakeToDiskHook = PlanetBakeTool.BakeTerrainFromEditor;
        // "Carica" apre un selettore file nativo, di default sulla cartella dove l'editor salva i pianeti.
        PlanetEditor.PickFileHook = dir => EditorUtility.OpenFilePanel("Carica pianeta", dir, "json");
    }
}

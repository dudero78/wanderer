using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Genera la scena demo da menu, senza setup manuale: crea una scena vuota con
/// un solo GameObject "GameBootstrap", la salva e la apre. Poi basta premere Play.
/// Menu: Wanderer > Crea scena demo.
/// </summary>
public static class SceneSetup
{
    [MenuItem("Wanderer/Crea scena demo")]
    public static void CreateDemoScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var go = new GameObject("GameBootstrap");
        go.AddComponent<GameBootstrap>();

        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Demo.unity");

        Debug.Log("Scena demo creata in Assets/Scenes/Demo.unity — premi Play.");
    }

    [MenuItem("Wanderer/Apri editor pianeti")]
    public static void CreatePlanetEditorScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var go = new GameObject("PlanetEditorBootstrap");
        go.AddComponent<PlanetEditorBootstrap>();

        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/PlanetEditor.unity");

        Debug.Log("Editor pianeti creato in Assets/Scenes/PlanetEditor.unity — premi Play.");
    }
}

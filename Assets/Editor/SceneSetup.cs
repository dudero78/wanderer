using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Genera la scena di gioco da menu, senza setup manuale: crea una scena vuota con
/// un solo GameObject "GameBootstrap", la salva, la apre e la registra nei Build
/// Settings. Poi basta premere Play. Menu: Wanderer > Crea scena di gioco.
/// </summary>
public static class SceneSetup
{
    [MenuItem("Wanderer/Crea scena di gioco")]
    public static void CreateGameScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var go = new GameObject("GameBootstrap");
        go.AddComponent<GameBootstrap>();

        System.IO.Directory.CreateDirectory("Assets/Scenes");
        const string path = "Assets/Scenes/Game.unity";
        EditorSceneManager.SaveScene(scene, path);

        // È LA scena della build standalone: registrarla nei Build Settings evita la "build nera"
        // (una scena non elencata → la build apre una scena vuota). Vedi lezioni in CLAUDE.md.
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };

        Debug.Log("Scena di gioco creata in " + path + " e aggiunta ai Build Settings — premi Play.");
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

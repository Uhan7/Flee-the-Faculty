using UnityEngine;
using UnityEngine.SceneManagement;

public static class VisualPolishBootstrap
{
    private const string PrefabResourcePath = "Visual Polish Rig";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForActiveScene()
    {
        TryCreate(SceneManager.GetActiveScene());
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode _)
    {
        TryCreate(scene);
    }

    private static void TryCreate(Scene scene)
    {
        if (!IsPrimaryScene(scene.name))
        {
            return;
        }

        SceneVisualPolish[] existingRigs = Object.FindObjectsByType<SceneVisualPolish>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int index = 0; index < existingRigs.Length; index++)
        {
            if (existingRigs[index].gameObject.scene == scene)
            {
                return;
            }
        }

        GameObject prefab = Resources.Load<GameObject>(PrefabResourcePath);
        if (prefab == null)
        {
            Debug.LogError("Resources/Visual Polish Rig.prefab is missing.");
            return;
        }

        GameObject instance = Object.Instantiate(prefab);
        instance.name = "Visual Polish Rig (Runtime)";
        SceneManager.MoveGameObjectToScene(instance, scene);
    }

    private static bool IsPrimaryScene(string sceneName)
    {
        return sceneName == "Main Menu"
            || sceneName == "Classroom and Movement"
            || sceneName == "Teacher Evaluation";
    }
}

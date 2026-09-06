using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Adds the reusable classroom picker to the existing main menu without
/// embedding another copy of the UI in the scene.
/// </summary>
public static class ClassroomSelectionBootstrap
{
    private const string MainMenuSceneName = "Main Menu";
    private const string PrefabResourcePath = "Classroom Selection";

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
        if (!string.Equals(scene.name, MainMenuSceneName))
        {
            return;
        }

        MainMenuController mainMenu = Object.FindFirstObjectByType<MainMenuController>();
        EnsureFor(mainMenu);
    }

    public static void EnsureFor(MainMenuController mainMenu)
    {
        if (mainMenu == null || mainMenu.GetComponentInChildren<ClassroomSelectionController>(true) != null)
        {
            return;
        }

        GameObject prefab = Resources.Load<GameObject>(PrefabResourcePath);
        if (prefab == null)
        {
            Debug.LogError("Resources/Classroom Selection.prefab is missing.");
            return;
        }

        GameObject instance = Object.Instantiate(prefab, mainMenu.transform);
        instance.name = PrefabResourcePath;
        ClassroomSelectionController controller = instance.GetComponent<ClassroomSelectionController>();
        if (controller == null)
        {
            Debug.LogError("The classroom selection prefab has no controller.", instance);
            Object.Destroy(instance);
            return;
        }

        controller.Configure(mainMenu.transform as RectTransform);
    }
}

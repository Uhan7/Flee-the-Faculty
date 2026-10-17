using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds <c>Assets/Scenes/Learning.unity</c>, the scene the Learning phase
/// plays in.
///
/// It starts from a copy of <c>Teacher Evaluation.unity</c>, because that scene
/// already has the classroom wall, the dialogue box and the camera the Learning
/// mockup shows, and then takes out what only the Evaluation phase should run:
/// the Teacher Evaluation Sequence (which would call the service to finish the
/// run), its progress HUD, and the Classroom Session (which would spawn the
/// Pupils; Learning builds the Classroom itself and shows none). It adds a
/// "Learning Sequence" object that carries the controller and the Teacher's
/// <see cref="DialogueActor"/>, copied from the one the Evaluation scene used so
/// the Teacher keeps the same name, dialogue style and voice ticks.
///
/// Nothing existing is edited: the copy is a new asset, and the only other file
/// touched is the build settings, which gain the scene at the end of the list if
/// it is missing. No entry is ever removed or reordered.
///
/// It refuses to overwrite a Learning.unity that is already there. The second
/// menu item replaces it after asking.
///
/// What it leaves to a person: the Teacher's artwork and its place on the wall
/// (the mockup puts the sprite to the right of the whiteboard), and pointing the
/// game's Play flow at this scene. See the checklist in the pull request.
/// </summary>
public static class LearningSceneBuilder
{
    private const string SourceScenePath = "Assets/Scenes/Teacher Evaluation.unity";
    private const string LearningScenePath = LearningPhaseController.LearningScenePath;
    private const string BuildMenuPath = "Flee/Build Learning Scene";
    private const string RebuildMenuPath = "Flee/Rebuild Learning Scene (replaces Learning.unity)";
    private const string SequenceObjectName = "Learning Sequence";

    [MenuItem(BuildMenuPath)]
    public static void BuildLearningScene()
    {
        if (File.Exists(LearningScenePath))
        {
            string message = LearningScenePath + " already exists, so nothing was changed. "
                + "To throw it away and build it again, use " + RebuildMenuPath + ".";
            Debug.LogWarning(message);
            EditorUtility.DisplayDialog("Learning scene already exists", message, "OK");
            return;
        }

        BuildScene(replaceExisting: false);
    }

    [MenuItem(RebuildMenuPath)]
    public static void RebuildLearningScene()
    {
        if (!File.Exists(LearningScenePath))
        {
            BuildScene(replaceExisting: false);
            return;
        }

        bool confirmed = EditorUtility.DisplayDialog(
            "Replace the Learning scene?",
            LearningScenePath + " will be deleted and built again from "
                + SourceScenePath + ". Anything you added to it by hand is lost.",
            "Replace it",
            "Cancel");
        if (!confirmed)
        {
            return;
        }

        BuildScene(replaceExisting: true);
    }

    private static void BuildScene(bool replaceExisting)
    {
        if (!File.Exists(SourceScenePath))
        {
            Debug.LogError("The Learning scene is built from " + SourceScenePath + ", which is missing.");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        if (replaceExisting)
        {
            // The scene being deleted must not be the one that is open.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!AssetDatabase.DeleteAsset(LearningScenePath))
            {
                Debug.LogError("Could not delete " + LearningScenePath + ", so it was left as it was.");
                return;
            }
        }

        if (!AssetDatabase.CopyAsset(SourceScenePath, LearningScenePath))
        {
            Debug.LogError("Could not copy " + SourceScenePath + " to " + LearningScenePath + ".");
            return;
        }

        AssetDatabase.Refresh();
        Scene scene = EditorSceneManager.OpenScene(LearningScenePath, OpenSceneMode.Single);
        GameObject sequence = PrepareScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        bool addedToBuild = EnsureInBuildSettings();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (sequence != null)
        {
            Selection.activeGameObject = sequence;
        }

        Debug.Log(
            "Learning scene built at " + LearningScenePath + ". "
            + (addedToBuild
                ? "It was added to the build settings. "
                : "It was already in the build settings. ")
            + "Next: give the Teacher a sprite on the wall, and point the Play flow at this scene.");
    }

    /// <summary>
    /// Edits the copy that is now open. Order matters: the Teacher's actor is
    /// copied before the Evaluation object that holds it is deleted.
    /// </summary>
    private static GameObject PrepareScene()
    {
        TeacherEvaluationController evaluation = FindFirst<TeacherEvaluationController>();
        DialogueActor teacherSource = evaluation != null
            ? evaluation.GetComponent<DialogueActor>()
            : null;

        GameObject sequence = new GameObject(SequenceObjectName);
        DialogueActor teacher = sequence.AddComponent<DialogueActor>();
        if (teacherSource != null)
        {
            EditorUtility.CopySerialized(teacherSource, teacher);
        }
        else
        {
            SerializedObject actorObject = new SerializedObject(teacher);
            actorObject.FindProperty("displayName").stringValue = "Teacher";
            actorObject.FindProperty("useBrownDialogueStyle").boolValue = true;
            actorObject.ApplyModifiedPropertiesWithoutUndo();
        }

        LearningPhaseController controller = sequence.AddComponent<LearningPhaseController>();
        SerializedObject controllerObject = new SerializedObject(controller);
        controllerObject.FindProperty("teacherActor").objectReferenceValue = teacher;
        controllerObject.ApplyModifiedPropertiesWithoutUndo();

        int removed = 0;
        removed += RemoveAll<TeacherEvaluationController>();
        removed += RemoveAll<EvaluationProgressView>();
        removed += RemoveAll<ClassroomSessionController>();
        Debug.Log(
            "Learning scene: removed " + removed + " object(s) that belong to other phases "
            + "(Teacher Evaluation Sequence, Evaluation Progress HUD, Classroom Session).");
        return sequence;
    }

    /// <summary>
    /// Delete every object carrying <typeparamref name="T"/>. When it sits in a
    /// prefab instance, the whole instance goes, because a scene cannot hold a
    /// prefab instance minus its root.
    /// </summary>
    private static int RemoveAll<T>() where T : Component
    {
        T[] found = FindAll<T>();
        int removed = 0;
        for (int index = 0; index < found.Length; index++)
        {
            T component = found[index];
            if (component == null)
            {
                continue;
            }

            GameObject target = component.gameObject;
            if (PrefabUtility.IsPartOfPrefabInstance(target))
            {
                GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(target);
                if (root != null)
                {
                    target = root;
                }
            }

            Object.DestroyImmediate(target);
            removed++;
        }

        return removed;
    }

    /// <summary>
    /// Adds the scene at the end of the list if it is not there. Entries that
    /// exist are left exactly as they are, in the order they are in.
    /// </summary>
    private static bool EnsureInBuildSettings()
    {
        EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;
        for (int index = 0; index < existing.Length; index++)
        {
            if (existing[index].path == LearningScenePath)
            {
                if (!existing[index].enabled)
                {
                    Debug.LogWarning(
                        LearningScenePath + " is in the build settings but unticked. "
                        + "It was left that way; tick it for Learning to load in a build.");
                }

                return false;
            }
        }

        EditorBuildSettingsScene[] updated = new EditorBuildSettingsScene[existing.Length + 1];
        for (int index = 0; index < existing.Length; index++)
        {
            updated[index] = existing[index];
        }

        updated[existing.Length] = new EditorBuildSettingsScene(LearningScenePath, true);
        EditorBuildSettings.scenes = updated;
        return true;
    }

    private static T FindFirst<T>() where T : Object
    {
        T[] found = FindAll<T>();
        return found.Length > 0 ? found[0] : null;
    }

    private static T[] FindAll<T>() where T : Object
    {
#if UNITY_2023_1_OR_NEWER
        return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        return Object.FindObjectsOfType<T>(true);
#endif
    }
}

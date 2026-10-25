using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class DialogueSpeakerTemplateBuilder
{
    private const string Folder = "Assets/Resources/Dialogue Speaker Templates";

    [MenuItem("Flee the Faculty/Dialogue/Create Missing Speaker Templates")]
    public static void CreateMissingTemplates()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
        {
            AssetDatabase.CreateFolder("Assets/Resources", "Dialogue Speaker Templates");
        }

        const string scenePath = "Assets/Scenes/Classroom and Movement.unity";
        var scene = SceneManager.GetSceneByPath(scenePath);
        bool openedScene = !scene.isLoaded;
        if (openedScene)
        {
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        }
        var previewScene = EditorSceneManager.NewPreviewScene();
        try
        {
            SceneDialogueView view = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                view = root.GetComponentInChildren<SceneDialogueView>(true);
                if (view != null)
                {
                    break;
                }
            }

            if (view == null)
            {
                throw new InvalidOperationException("The classroom scene has no dialogue view.");
            }

            SerializedObject source = new SerializedObject(view);
            GameObject container = (GameObject)source.FindProperty("dialogueContainer").objectReferenceValue;
            foreach (string role in new[] { "Character", "Teacher", "Student", "AraBOT" })
            {
                string path = Folder + "/" + role + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                {
                    continue;
                }

                GameObject preview = UnityEngine.Object.Instantiate(container);
                SceneManager.MoveGameObjectToScene(preview, previewScene);
                preview.name = role;
                preview.SetActive(true);
                TMP_Text speaker = Map<TMP_Text>(source, "speakerText", container, preview);
                TMP_Text body = Map<TMP_Text>(source, "bodyText", container, preview);
                Image box = Map<Image>(source, "dialogueBoxImage", container, preview);
                Image nameBox = Map<Image>(source, "nameBoxImage", container, preview);
                Image arrow = Map<Image>(source, "pointingArrowImage", container, preview);
                string fontField = role == "AraBOT" ? "araBotFont" : char.ToLowerInvariant(role[0]) + role.Substring(1) + "Font";
                TMP_FontAsset font = source.FindProperty(fontField).objectReferenceValue as TMP_FontAsset;
                speaker.text = role;
                body.text = DialogueSpeakerTemplate.PreviewText;
                if (font != null)
                {
                    speaker.font = body.font = font;
                }

                bool brown = role == "Student" || role == "Teacher";
                SetSprite(source, box, brown ? "studentDialogueBoxSprite" : "defaultDialogueBoxSprite");
                SetSprite(source, nameBox, brown ? "studentNameBoxSprite" : "defaultNameBoxSprite");
                SetSprite(source, arrow, brown ? "studentPointingArrowSprite" : "defaultPointingArrowSprite");
                preview.AddComponent<DialogueSpeakerTemplate>().Configure(speaker, body, box, nameBox, arrow);
                PrefabUtility.SaveAsPrefabAsset(preview, path);
                UnityEngine.Object.DestroyImmediate(preview);
            }

            AssetDatabase.SaveAssets();
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(previewScene);
            if (openedScene)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }

    private static T Map<T>(SerializedObject source, string field, GameObject original, GameObject clone) where T : Component
    {
        T component = source.FindProperty(field).objectReferenceValue as T;
        if (component == null)
        {
            return null;
        }

        string path = AnimationUtility.CalculateTransformPath(component.transform, original.transform);
        Transform target = clone.transform.Find(path);
        return target != null ? target.GetComponent<T>() : null;
    }

    private static void SetSprite(SerializedObject source, Image image, string field)
    {
        Sprite sprite = source.FindProperty(field).objectReferenceValue as Sprite;
        if (image != null && sprite != null)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
        }
    }
}

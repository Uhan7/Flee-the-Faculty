using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem.UI;

namespace BrowserSpeechToTextPrototypeSceneBuilderInternal
{
    internal static class SpeechToTextPrototypeSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Speech To Text Prototype.unity";
        private const string FontAssetPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        [MenuItem("Tools/Flee the Faculty/Create Speech To Text Prototype Scene")]
        public static void CreateScene()
        {
            Directory.CreateDirectory("Assets/Scenes");

            SceneSetupUtility.CreatePrototypeScene(ScenePath, LoadFontAsset());
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(ScenePath);

            Debug.Log("Speech-to-text prototype scene created at " + ScenePath + ".");
        }

        private static TMP_FontAsset LoadFontAsset()
        {
            TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (fontAsset != null)
            {
                return fontAsset;
            }

            return TMP_Settings.defaultFontAsset;
        }
    }

    internal static class SceneSetupUtility
    {
        public static void CreatePrototypeScene(string scenePath, TMP_FontAsset fontAsset)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera();
            CreateEventSystem();
            CreateCanvas(fontAsset);

            EditorSceneManager.SaveScene(scene, scenePath);
        }

        private static void CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";

            var cameraComponent = cameraObject.AddComponent<Camera>();
            cameraComponent.clearFlags = CameraClearFlags.SolidColor;
            cameraComponent.backgroundColor = new Color(0.06f, 0.08f, 0.13f);
            cameraComponent.orthographic = true;

            cameraObject.AddComponent<AudioListener>();
        }

        private static void CreateEventSystem()
        {
            var eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
        }

        private static void CreateCanvas(TMP_FontAsset fontAsset)
        {
            var canvasObject = new GameObject("Speech To Text Canvas");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GraphicRaycaster>();

            var rootTransform = canvasObject.GetComponent<RectTransform>();
            rootTransform.anchorMin = Vector2.zero;
            rootTransform.anchorMax = Vector2.one;
            rootTransform.offsetMin = Vector2.zero;
            rootTransform.offsetMax = Vector2.zero;

            CreateFullscreenImage("Background", rootTransform, new Color(0.09f, 0.12f, 0.18f));

            RectTransform panel = CreatePanel(
                "Main Panel",
                rootTransform,
                new Color(0.12f, 0.16f, 0.24f, 0.96f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 0f),
                new Vector2(1120f, 700f));

            TMP_Text titleText = CreateText(
                "Title",
                panel,
                fontAsset,
                "Speech To Text Prototype",
                42,
                FontStyles.Bold,
                new Color(0.96f, 0.98f, 1f),
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -58f),
                new Vector2(900f, 60f));

            TMP_Text instructionsText = CreateText(
                "Instructions",
                panel,
                fontAsset,
                "1. Click Start Listening. 2. In a WebGL build, speak into your microphone. 3. In the Unity Editor, type into the fallback field to simulate speech. 4. Click Submit to print what you said.",
                22,
                FontStyles.Normal,
                new Color(0.82f, 0.9f, 0.98f),
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -108f),
                new Vector2(950f, 78f));

            TMP_Text supportText = CreateText(
                "Support Text",
                panel,
                fontAsset,
                string.Empty,
                20,
                FontStyles.Italic,
                new Color(0.98f, 0.85f, 0.52f),
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -162f),
                new Vector2(940f, 50f));

            Button startButton = CreateButton(
                "Start Listening Button",
                panel,
                fontAsset,
                "Start Listening",
                new Color(0.24f, 0.68f, 0.49f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(-145f, -235f),
                new Vector2(260f, 74f));

            Button submitButton = CreateButton(
                "Submit Button",
                panel,
                fontAsset,
                "Submit",
                new Color(0.94f, 0.56f, 0.2f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(145f, -235f),
                new Vector2(220f, 74f));

            RectTransform statusPanel = CreatePanel(
                "Status Panel",
                panel,
                new Color(0.16f, 0.2f, 0.3f, 0.98f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -320f),
                new Vector2(980f, 86f));

            TMP_Text statusText = CreateText(
                "Status Text",
                statusPanel,
                fontAsset,
                string.Empty,
                24,
                FontStyles.Bold,
                Color.white,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(920f, 54f));

            TMP_InputField fallbackInputField = CreateInputFieldSection(
                panel,
                fontAsset,
                "Fallback Input Section",
                "Fallback Typed Input",
                "Type here in the Unity Editor or when speech is unavailable on this device.",
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -438f));

            CreateTranscriptSection(
                panel,
                fontAsset,
                "Live Transcript Section",
                "Live Transcript",
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -578f),
                out TMP_Text liveTranscriptText);

            CreateTranscriptSection(
                panel,
                fontAsset,
                "Submitted Transcript Section",
                "Submitted Transcript",
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 118f),
                out TMP_Text submittedTranscriptText);

            var controllerObject = new GameObject("Speech To Text Prototype");
            controllerObject.transform.SetParent(panel, false);

            var controller = controllerObject.AddComponent<BrowserSpeechToTextPrototype>();
            controller.SetReferences(
                startButton,
                submitButton,
                statusText,
                supportText,
                fallbackInputField,
                liveTranscriptText,
                submittedTranscriptText);

            UnityEventTools.AddPersistentListener(startButton.onClick, controller.StartListening);
            UnityEventTools.AddPersistentListener(submitButton.onClick, controller.SubmitTranscript);
        }

        private static TMP_InputField CreateInputFieldSection(
            RectTransform parent,
            TMP_FontAsset fontAsset,
            string sectionName,
            string heading,
            string placeholderText,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPosition)
        {
            RectTransform sectionPanel = CreatePanel(
                sectionName,
                parent,
                new Color(0.11f, 0.14f, 0.21f, 0.98f),
                anchorMin,
                anchorMax,
                anchoredPosition,
                new Vector2(980f, 112f));

            CreateText(
                heading + " Heading",
                sectionPanel,
                fontAsset,
                heading,
                22,
                FontStyles.Bold,
                new Color(0.74f, 0.88f, 1f),
                TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -22f),
                new Vector2(-40f, 28f));

            return CreateInputField(
                "Fallback Input Field",
                sectionPanel,
                fontAsset,
                placeholderText,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, -54f),
                new Vector2(-40f, -60f));
        }

        private static void CreateTranscriptSection(
            RectTransform parent,
            TMP_FontAsset fontAsset,
            string sectionName,
            string heading,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPosition,
            out TMP_Text bodyText)
        {
            RectTransform sectionPanel = CreatePanel(
                sectionName,
                parent,
                new Color(0.11f, 0.14f, 0.21f, 0.98f),
                anchorMin,
                anchorMax,
                anchoredPosition,
                new Vector2(980f, 180f));

            CreateText(
                heading + " Heading",
                sectionPanel,
                fontAsset,
                heading,
                24,
                FontStyles.Bold,
                new Color(0.74f, 0.88f, 1f),
                TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -28f),
                new Vector2(-40f, 34f));

            bodyText = CreateText(
                heading + " Body",
                sectionPanel,
                fontAsset,
                string.Empty,
                26,
                FontStyles.Normal,
                Color.white,
                TextAlignmentOptions.TopLeft,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, -74f),
                new Vector2(-40f, -96f));

            bodyText.enableWordWrapping = true;
        }

        private static RectTransform CreatePanel(
            string name,
            Transform parent,
            Color color,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            var panelObject = new GameObject(name);
            panelObject.transform.SetParent(parent, false);

            var rectTransform = panelObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.sizeDelta = sizeDelta;

            var image = panelObject.AddComponent<Image>();
            image.color = color;

            return rectTransform;
        }

        private static void CreateFullscreenImage(string name, Transform parent, Color color)
        {
            RectTransform rectTransform = CreatePanel(
                name,
                parent,
                color,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);

            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }

        private static TMP_Text CreateText(
            string name,
            Transform parent,
            TMP_FontAsset fontAsset,
            string text,
            float fontSize,
            FontStyles fontStyle,
            Color color,
            TextAlignmentOptions alignment,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            var textObject = new GameObject(name);
            textObject.transform.SetParent(parent, false);

            var rectTransform = textObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.sizeDelta = sizeDelta;

            var textComponent = textObject.AddComponent<TextMeshProUGUI>();
            textComponent.font = fontAsset;
            textComponent.text = text;
            textComponent.fontSize = fontSize;
            textComponent.fontStyle = fontStyle;
            textComponent.color = color;
            textComponent.alignment = alignment;

            return textComponent;
        }

        private static TMP_InputField CreateInputField(
            string name,
            Transform parent,
            TMP_FontAsset fontAsset,
            string placeholderText,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            var inputObject = new GameObject(name);
            inputObject.transform.SetParent(parent, false);

            var rootRect = inputObject.AddComponent<RectTransform>();
            rootRect.anchorMin = anchorMin;
            rootRect.anchorMax = anchorMax;
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = anchoredPosition;
            rootRect.sizeDelta = sizeDelta;

            var background = inputObject.AddComponent<Image>();
            background.color = new Color(0.2f, 0.24f, 0.34f, 1f);

            var inputField = inputObject.AddComponent<TMP_InputField>();
            inputField.lineType = TMP_InputField.LineType.MultiLineNewline;

            var textAreaObject = new GameObject("Text Area");
            textAreaObject.transform.SetParent(inputObject.transform, false);

            var textAreaRect = textAreaObject.AddComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(16f, 10f);
            textAreaRect.offsetMax = new Vector2(-16f, -10f);
            textAreaObject.AddComponent<RectMask2D>();

            TextMeshProUGUI textComponent = CreateText(
                "Text",
                textAreaRect,
                fontAsset,
                string.Empty,
                22,
                FontStyles.Normal,
                Color.white,
                TextAlignmentOptions.TopLeft,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero) as TextMeshProUGUI;

            textComponent.enableWordWrapping = true;

            TextMeshProUGUI placeholderComponent = CreateText(
                "Placeholder",
                textAreaRect,
                fontAsset,
                placeholderText,
                22,
                FontStyles.Italic,
                new Color(1f, 1f, 1f, 0.4f),
                TextAlignmentOptions.TopLeft,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero) as TextMeshProUGUI;

            placeholderComponent.enableWordWrapping = true;

            inputField.textViewport = textAreaRect;
            inputField.textComponent = textComponent;
            inputField.placeholder = placeholderComponent;

            return inputField;
        }

        private static Button CreateButton(
            string name,
            Transform parent,
            TMP_FontAsset fontAsset,
            string label,
            Color backgroundColor,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            var buttonObject = new GameObject(name);
            buttonObject.transform.SetParent(parent, false);

            var rectTransform = buttonObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.sizeDelta = sizeDelta;

            var image = buttonObject.AddComponent<Image>();
            image.color = backgroundColor;

            var button = buttonObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = backgroundColor;
            colors.highlightedColor = backgroundColor * 1.1f;
            colors.pressedColor = backgroundColor * 0.85f;
            colors.selectedColor = backgroundColor;
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.75f);
            button.colors = colors;

            CreateText(
                "Label",
                rectTransform,
                fontAsset,
                label,
                26,
                FontStyles.Bold,
                Color.white,
                TextAlignmentOptions.Center,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);

            return button;
        }
    }
}

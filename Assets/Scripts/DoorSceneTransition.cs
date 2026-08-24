using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#if UNITY_EDITOR
using System.IO;
using UnityEditor.SceneManagement;
#endif

[DisallowMultipleComponent]
public sealed class DoorSceneTransition : MonoBehaviour
{
    // Singleton
    public static DoorSceneTransition Instance { get; private set; }

    // Destination
    [Header("Destination")]
    [SerializeField] private string targetSceneName = "AraBOT Walk Test";
    [SerializeField] private string targetScenePath = "Assets/Scenes/AraBOT Walk Test.unity";

    // Timing
    [Header("Timing")]
    [SerializeField, Min(0.01f)] private float closeDuration = 0.22f;
    [SerializeField, Min(0f)] private float holdClosedDuration = 0.05f;
    [SerializeField, Min(0.01f)] private float openDuration = 0.28f;
    [SerializeField, Min(0f)] private float postLoadDelay = 0f;

    // Startup
    [Header("Startup")]
    [SerializeField] private bool playOpenOnStart = true;
    [SerializeField, Min(0f)] private float startupHoldClosedDuration = 0.1f;

    // Look
    [Header("Look")]
    [SerializeField] private Sprite doorSprite;
    [SerializeField] private Color doorColor = new Color(0.08f, 0.11f, 0.15f, 1f);
    [SerializeField, Min(0f)] private float doorOvershoot = 28f;
    [SerializeField] private Vector2 referenceResolution = new Vector2(1600f, 900f);
    [SerializeField] private int sortingOrder = 5000;

    private Canvas canvas;
    private CanvasScaler canvasScaler;
    private GraphicRaycaster graphicRaycaster;
    private RectTransform rootRect;
    private Image blocker;
    private RectTransform topDoor;
    private RectTransform bottomDoor;
    private bool isTransitioning;
    private bool isWaitingForSceneLoad;
    private bool hasLoadedRequestedScene;
    private bool hasPlayedStartupOpen;
    private float currentCoverage;
    private Vector2 lastCanvasSize;

    public void TransitionToConfiguredScene()
    {
        BeginTransition(targetSceneName, targetScenePath);
    }

    public void TransitionToScene(string sceneName)
    {
        BeginTransition(sceneName, string.Empty);
    }

    public static void LoadScene(string sceneName, string scenePath = null)
    {
        DoorSceneTransition transition = EnsureInstance();
        if (transition != null)
        {
            transition.BeginTransition(sceneName, scenePath);
        }
    }

    private static DoorSceneTransition EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        DoorSceneTransition existingInstance = FindFirstObjectByType<DoorSceneTransition>();
        if (existingInstance != null)
        {
            existingInstance.InitializeVisuals();
            return existingInstance;
        }

        GameObject transitionObject = new GameObject("Door Scene Transition");
        DoorSceneTransition createdInstance = transitionObject.AddComponent<DoorSceneTransition>();
        createdInstance.InitializeVisuals();
        return createdInstance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        InitializeVisuals();
        SetDoorCoverage(1f);
        SetInputBlocked(false);
    }

    private void Start()
    {
        if (!playOpenOnStart || hasPlayedStartupOpen)
        {
            SetDoorCoverage(0f);
            SetInputBlocked(false);
            return;
        }

        StopAllCoroutines();
        StartCoroutine(PlayStartupOpenRoutine());
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void LateUpdate()
    {
        RefreshDoorLayoutIfNeeded();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void BeginTransition(string sceneName, string scenePath)
    {
        if (isTransitioning)
        {
            return;
        }

        StopAllCoroutines();
        StartCoroutine(RunTransition(sceneName, scenePath));
    }

    private IEnumerator PlayStartupOpenRoutine()
    {
        hasPlayedStartupOpen = true;
        SetInputBlocked(true);
        SetDoorCoverage(1f);

        if (startupHoldClosedDuration > 0f)
        {
            yield return WaitForSecondsRealtime(startupHoldClosedDuration);
        }

        yield return AnimateDoors(1f, 0f, openDuration);
        SetInputBlocked(false);
    }

    private IEnumerator RunTransition(string sceneName, string scenePath)
    {
        isTransitioning = true;
        SetInputBlocked(true);

        yield return AnimateDoors(0f, 1f, closeDuration);

        if (holdClosedDuration > 0f)
        {
            yield return WaitForSecondsRealtime(holdClosedDuration);
        }

        isWaitingForSceneLoad = true;
        hasLoadedRequestedScene = false;

        yield return LoadSceneRoutine(sceneName, scenePath);

        while (isWaitingForSceneLoad && !hasLoadedRequestedScene)
        {
            yield return null;
        }

        if (postLoadDelay > 0f)
        {
            yield return WaitForSecondsRealtime(postLoadDelay);
        }

        yield return AnimateDoors(1f, 0f, openDuration);

        SetInputBlocked(false);
        isTransitioning = false;
    }

    private IEnumerator AnimateDoors(float fromCoverage, float toCoverage, float duration)
    {
        if (duration <= 0f)
        {
            SetDoorCoverage(toCoverage);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float easedProgress = EaseInOutCubic(progress);
            float coverage = Mathf.Lerp(fromCoverage, toCoverage, easedProgress);

            SetDoorCoverage(coverage);
            yield return null;
        }

        SetDoorCoverage(toCoverage);
    }

    private IEnumerator LoadSceneRoutine(string sceneName, string scenePath)
    {
        AsyncOperation loadOperation = null;

#if UNITY_EDITOR
        if (Application.isPlaying &&
            !string.IsNullOrWhiteSpace(scenePath) &&
            File.Exists(scenePath))
        {
            loadOperation = EditorSceneManager.LoadSceneAsyncInPlayMode(
                scenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
        }
#endif

        if (loadOperation == null &&
            !string.IsNullOrWhiteSpace(sceneName) &&
            Application.CanStreamedLevelBeLoaded(sceneName))
        {
            loadOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        }

        if (loadOperation == null)
        {
            Debug.LogWarning(
                "DoorSceneTransition could not load the requested scene. " +
                "Pass a valid scene path for editor play mode or add the scene to Build Settings.");
            isWaitingForSceneLoad = false;
            yield break;
        }

        while (!loadOperation.isDone)
        {
            yield return null;
        }
    }

    private void InitializeVisuals()
    {
        if (canvas != null &&
            canvasScaler != null &&
            graphicRaycaster != null &&
            rootRect != null &&
            blocker != null &&
            topDoor != null &&
            bottomDoor != null)
        {
            return;
        }

        DontDestroyOnLoad(gameObject);

        canvas = GetOrAddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        canvasScaler = GetOrAddComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = referenceResolution;
        canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        canvasScaler.matchWidthOrHeight = 0.5f;

        graphicRaycaster = GetOrAddComponent<GraphicRaycaster>();
        rootRect = transform as RectTransform;
        if (rootRect == null)
        {
            rootRect = gameObject.AddComponent<RectTransform>();
        }

        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
        rootRect.anchoredPosition = Vector2.zero;

        blocker = GetOrCreateImage("Blocker", new Color(0f, 0f, 0f, 0f), Vector2.zero, Vector2.one, true);
        blocker.rectTransform.offsetMin = Vector2.zero;
        blocker.rectTransform.offsetMax = Vector2.zero;

        topDoor = GetOrCreateDoor("Top Door");
        bottomDoor = GetOrCreateDoor("Bottom Door");

        Canvas.ForceUpdateCanvases();
        lastCanvasSize = Vector2.zero;
        RefreshDoorLayoutIfNeeded();
        SetDoorCoverage(1f);
        SetInputBlocked(false);
    }

    private void SetDoorCoverage(float coverage)
    {
        if (rootRect == null || topDoor == null || bottomDoor == null)
        {
            return;
        }

        currentCoverage = Mathf.Clamp01(coverage);
        Vector2 canvasSize = GetCanvasSize();
        float closedTravel = canvasSize.y * 0.5f;

        ConfigureDoorRect(topDoor, canvasSize, true);
        ConfigureDoorRect(bottomDoor, canvasSize, false);

        topDoor.anchoredPosition = new Vector2(
            0f,
            Mathf.Lerp(doorOvershoot, -closedTravel, currentCoverage));

        bottomDoor.anchoredPosition = new Vector2(
            0f,
            Mathf.Lerp(-doorOvershoot, closedTravel, currentCoverage));
    }

    private void SetInputBlocked(bool isBlocked)
    {
        if (blocker == null)
        {
            return;
        }

        blocker.enabled = isBlocked;
        blocker.raycastTarget = isBlocked;
    }

    private Image GetOrCreateImage(
        string objectName,
        Color color,
        Vector2 anchorMin,
        Vector2 anchorMax,
        bool shouldBlockRaycasts)
    {
        Transform existingChild = transform.Find(objectName);
        Image image = existingChild != null ? existingChild.GetComponent<Image>() : null;
        RectTransform rectTransform;

        if (image == null)
        {
            GameObject imageObject = new GameObject(objectName);
            imageObject.transform.SetParent(transform, false);
            image = imageObject.AddComponent<Image>();
        }

        rectTransform = image.rectTransform;
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.localScale = Vector3.one;
        image.color = color;
        image.raycastTarget = shouldBlockRaycasts;

        return image;
    }

    private RectTransform GetOrCreateDoor(string objectName)
    {
        Image doorImage = GetOrCreateImage(objectName, doorColor, Vector2.zero, Vector2.one, false);
        ApplyDoorImageStyle(doorImage);
        doorImage.rectTransform.sizeDelta = Vector2.zero;
        return doorImage.rectTransform;
    }

    private void ApplyDoorImageStyle(Image doorImage)
    {
        if (doorImage == null)
        {
            return;
        }

        doorImage.sprite = doorSprite;
        doorImage.type = doorSprite != null ? Image.Type.Sliced : Image.Type.Simple;
        doorImage.preserveAspect = false;
        doorImage.color = doorColor;
    }

    private void RefreshDoorLayoutIfNeeded()
    {
        Vector2 canvasSize = GetCanvasSize();
        if ((canvasSize - lastCanvasSize).sqrMagnitude <= 0.01f)
        {
            return;
        }

        lastCanvasSize = canvasSize;
        SetDoorCoverage(currentCoverage);
    }

    private Vector2 GetCanvasSize()
    {
        if (rootRect != null)
        {
            Vector2 rectSize = rootRect.rect.size;
            if (rectSize.x > 1f && rectSize.y > 1f)
            {
                return rectSize;
            }
        }

        if (canvas != null)
        {
            Rect pixelRect = canvas.pixelRect;
            float scaleFactor = Mathf.Max(canvas.scaleFactor, 1f);
            Vector2 scaledSize = new Vector2(pixelRect.width / scaleFactor, pixelRect.height / scaleFactor);
            if (scaledSize.x > 1f && scaledSize.y > 1f)
            {
                return scaledSize;
            }
        }

        return referenceResolution;
    }

    private void ConfigureDoorRect(RectTransform doorRect, Vector2 canvasSize, bool isTopDoor)
    {
        if (doorRect == null)
        {
            return;
        }

        float width = canvasSize.x + (doorOvershoot * 2f);
        float height = (canvasSize.y * 0.5f) + (doorOvershoot * 2f);

        if (isTopDoor)
        {
            doorRect.anchorMin = new Vector2(0.5f, 1f);
            doorRect.anchorMax = new Vector2(0.5f, 1f);
            doorRect.pivot = new Vector2(0.5f, 0f);
        }
        else
        {
            doorRect.anchorMin = new Vector2(0.5f, 0f);
            doorRect.anchorMax = new Vector2(0.5f, 0f);
            doorRect.pivot = new Vector2(0.5f, 1f);
        }

        doorRect.sizeDelta = new Vector2(width, height);
        doorRect.localScale = Vector3.one;
    }

    private void HandleSceneLoaded(Scene _, LoadSceneMode __)
    {
        if (!isWaitingForSceneLoad)
        {
            return;
        }

        hasLoadedRequestedScene = true;
        isWaitingForSceneLoad = false;
    }

    private T GetOrAddComponent<T>() where T : Component
    {
        T component = GetComponent<T>();
        if (component == null)
        {
            component = gameObject.AddComponent<T>();
        }

        return component;
    }

    private static float EaseInOutCubic(float value)
    {
        if (value < 0.5f)
        {
            return 4f * value * value * value;
        }

        float inverted = (-2f * value) + 2f;
        return 1f - ((inverted * inverted * inverted) * 0.5f);
    }

    private static IEnumerator WaitForSecondsRealtime(float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }
}

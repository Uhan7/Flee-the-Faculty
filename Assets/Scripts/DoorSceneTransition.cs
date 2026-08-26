using System.Collections;
using System.Collections.Generic;
using TMPro;
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
    private const string TransitionPrefabResourcePath = "Door Scene Transition";

    // Singleton
    public static DoorSceneTransition Instance { get; private set; }

    [Header("Prefab References")]
    [SerializeField] private Canvas canvas;
    [SerializeField] private RectTransform rootRect;
    [SerializeField] private Image blocker;
    [SerializeField] private RectTransform topDoor;
    [SerializeField] private RectTransform bottomDoor;
    [SerializeField] private GameObject loadingContent;
    [SerializeField] private RectTransform loadingSpinner;
    [SerializeField] private TMP_Text loadingStatusText;
    [SerializeField] private Image loadingProgressFill;
    [SerializeField] private TMP_Text loadingPercentText;

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

    [Header("Animation Layout")]
    [SerializeField, Min(0f)] private float doorOvershoot = 28f;
    [SerializeField] private Vector2 referenceResolution = new Vector2(1600f, 900f);

    [Header("Loading")]
    [SerializeField, Min(0f)] private float deferredLoadRegistrationGraceSeconds = 0.08f;
    [SerializeField] private float loadingSpinnerDegreesPerSecond = -120f;

    private bool isTransitioning;
    private bool isWaitingForSceneLoad;
    private bool hasLoadedRequestedScene;
    private bool hasPlayedStartupOpen;
    private float currentCoverage;
    private Vector2 lastCanvasSize;
    private readonly Dictionary<string, DeferredLoadTask> deferredLoadTasks = new Dictionary<string, DeferredLoadTask>();

    public static bool TryRegisterLoadingTask(string taskId, string status, float progress = 0f, float weight = 1f)
    {
        DoorSceneTransition transition = EnsureInstance();
        return transition != null && transition.RegisterLoadingTask(taskId, status, progress, weight);
    }

    public static void UpdateLoadingTask(string taskId, float progress, string status = null)
    {
        Instance?.UpdateRegisteredTask(taskId, progress, status);
    }

    public static void CompleteLoadingTask(string taskId, string status = null)
    {
        Instance?.CompleteRegisteredTask(taskId, status);
    }

    public static DoorSceneTransition EnsureExists()
    {
        return EnsureInstance();
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

        DoorSceneTransition createdInstance = CreateInstanceFromPrefab();
        if (createdInstance == null)
        {
            Debug.LogError(
                "Door scene transition prefab is missing or has no DoorSceneTransition component. " +
                "Expected Resources/Door Scene Transition.prefab.");
            return null;
        }

        createdInstance.InitializeVisuals();
        return createdInstance;
    }

    private static DoorSceneTransition CreateInstanceFromPrefab()
    {
        GameObject transitionPrefab = Resources.Load<GameObject>(TransitionPrefabResourcePath);
        if (transitionPrefab == null)
        {
            return null;
        }

        GameObject transitionInstance = Instantiate(transitionPrefab);
        transitionInstance.name = transitionPrefab.name;
        return transitionInstance.GetComponent<DoorSceneTransition>();
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

    private void OnValidate()
    {
        ResolvePrefabReferences();
    }

    private void Reset()
    {
        ResolvePrefabReferences();
    }

    private void ResolvePrefabReferences()
    {
        if (canvas == null)
        {
            canvas = GetComponent<Canvas>();
        }

        if (rootRect == null)
        {
            rootRect = transform as RectTransform;
        }

        if (blocker == null)
        {
            Transform blockerTransform = transform.Find("Blocker");
            blocker = blockerTransform != null ? blockerTransform.GetComponent<Image>() : null;
        }

        if (topDoor == null)
        {
            Transform topDoorTransform = transform.Find("Top Door");
            topDoor = topDoorTransform as RectTransform;
        }

        if (bottomDoor == null)
        {
            Transform bottomDoorTransform = transform.Find("Bottom Door");
            bottomDoor = bottomDoorTransform as RectTransform;
        }

        Transform loadingContentTransform = loadingContent != null
            ? loadingContent.transform
            : transform.Find("Loading UI");
        if (loadingContent == null && loadingContentTransform != null)
        {
            loadingContent = loadingContentTransform.gameObject;
        }

        if (loadingContentTransform == null)
        {
            return;
        }

        if (loadingSpinner == null)
        {
            loadingSpinner = loadingContentTransform.Find("Top Content/AraBOT Spinner") as RectTransform;
        }

        if (loadingStatusText == null)
        {
            Transform statusTransform = loadingContentTransform.Find("Top Content/Loading Status");
            loadingStatusText = statusTransform != null ? statusTransform.GetComponent<TMP_Text>() : null;
        }

        if (loadingProgressFill == null)
        {
            Transform fillTransform = loadingContentTransform.Find("Bottom Content/Loading Bar/Fill");
            loadingProgressFill = fillTransform != null ? fillTransform.GetComponent<Image>() : null;
        }

        if (loadingPercentText == null)
        {
            Transform percentTransform = loadingContentTransform.Find("Bottom Content/Loading Percentage");
            loadingPercentText = percentTransform != null ? percentTransform.GetComponent<TMP_Text>() : null;
        }
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

    private void OnRectTransformDimensionsChange()
    {
        RefreshDoorLayout(forceRefresh: true);
    }

    private void LateUpdate()
    {
        RefreshDoorLayout();

        if (loadingSpinner != null && loadingContent != null && loadingContent.activeInHierarchy)
        {
            loadingSpinner.Rotate(0f, 0f, loadingSpinnerDegreesPerSecond * Time.unscaledDeltaTime);
        }
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
        UpdateLoadingText(0f, "Preparing classroom...");

        if (startupHoldClosedDuration > 0f)
        {
            yield return WaitForSecondsRealtime(startupHoldClosedDuration);
        }

        yield return WaitForDeferredLoadsAtStartup();

        yield return AnimateDoors(1f, 0f, openDuration);
        SetLoadingTextVisible(false);
        SetInputBlocked(false);
    }

    private IEnumerator RunTransition(string sceneName, string scenePath)
    {
        isTransitioning = true;
        SetInputBlocked(true);
        deferredLoadTasks.Clear();
        UpdateLoadingText(0f, "Closing doors...");

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

        yield return WaitForDeferredLoadsAfterSceneLoad();

        if (postLoadDelay > 0f)
        {
            yield return WaitForSecondsRealtime(postLoadDelay);
        }

        yield return AnimateDoors(1f, 0f, openDuration);

        SetLoadingTextVisible(false);
        SetInputBlocked(false);
        isTransitioning = false;
        deferredLoadTasks.Clear();
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
            SetLoadingTextVisible(false);
            yield break;
        }

        UpdateLoadingText(0f, "Loading scene...");

        while (!loadOperation.isDone)
        {
            float normalizedProgress = loadOperation.progress >= 0.9f
                ? 1f
                : Mathf.Clamp01(loadOperation.progress / 0.9f);
            UpdateLoadingText(normalizedProgress * 0.85f, "Loading scene...");
            yield return null;
        }

        UpdateLoadingText(0.85f, "Scene loaded.");
    }

    private void InitializeVisuals()
    {
        ResolvePrefabReferences();
        if (canvas == null ||
            rootRect == null ||
            blocker == null ||
            topDoor == null ||
            bottomDoor == null ||
            loadingContent == null ||
            loadingStatusText == null ||
            loadingProgressFill == null ||
            loadingPercentText == null)
        {
            Debug.LogError(
                "DoorSceneTransition is missing prefab references. " +
                "Assign the Canvas, doors, blocker, and loading UI in the transition prefab.",
                this);
            return;
        }

        DontDestroyOnLoad(gameObject);

        Canvas.ForceUpdateCanvases();
        lastCanvasSize = Vector2.zero;
        RefreshDoorLayout(forceRefresh: true);
        SetDoorCoverage(1f);
        SetInputBlocked(false);
        SetLoadingTextVisible(false);
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

    private void RefreshDoorLayout(bool forceRefresh = false)
    {
        Vector2 canvasSize = GetCanvasSize();
        if (!forceRefresh && (canvasSize - lastCanvasSize).sqrMagnitude <= 0.01f)
        {
            return;
        }

        lastCanvasSize = canvasSize;
        SetDoorCoverage(currentCoverage);
    }

    private Vector2 GetCanvasSize()
    {
        Canvas activeCanvas = canvas != null && canvas.rootCanvas != null
            ? canvas.rootCanvas
            : canvas;
        if (activeCanvas != null)
        {
            Rect pixelRect = activeCanvas.pixelRect;
            float scaleFactor = Mathf.Max(activeCanvas.scaleFactor, 0.0001f);
            Vector2 scaledSize = new Vector2(pixelRect.width / scaleFactor, pixelRect.height / scaleFactor);
            if (scaledSize.x > 1f && scaledSize.y > 1f)
            {
                return scaledSize;
            }
        }

        if (rootRect != null)
        {
            Vector2 rectSize = rootRect.rect.size;
            if (rectSize.x > 1f && rectSize.y > 1f)
            {
                return rectSize;
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

    private bool RegisterLoadingTask(string taskId, string status, float progress, float weight)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return false;
        }

        deferredLoadTasks[taskId] = new DeferredLoadTask(
            Mathf.Clamp01(progress),
            Mathf.Max(0.01f, weight),
            false,
            string.IsNullOrWhiteSpace(status) ? "Loading..." : status.Trim());
        return true;
    }

    private void UpdateRegisteredTask(string taskId, float progress, string status)
    {
        if (string.IsNullOrWhiteSpace(taskId) || !deferredLoadTasks.TryGetValue(taskId, out DeferredLoadTask task))
        {
            return;
        }

        task.Progress = Mathf.Clamp01(progress);
        if (!string.IsNullOrWhiteSpace(status))
        {
            task.Status = status.Trim();
        }

        deferredLoadTasks[taskId] = task;
    }

    private void CompleteRegisteredTask(string taskId, string status)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return;
        }

        if (!deferredLoadTasks.TryGetValue(taskId, out DeferredLoadTask task))
        {
            task = new DeferredLoadTask(1f, 1f, true, string.Empty);
        }

        task.Progress = 1f;
        task.IsComplete = true;
        if (!string.IsNullOrWhiteSpace(status))
        {
            task.Status = status.Trim();
        }

        deferredLoadTasks[taskId] = task;
    }

    private IEnumerator WaitForDeferredLoadsAtStartup()
    {
        float graceEndTime = Time.unscaledTime + deferredLoadRegistrationGraceSeconds;
        while (Time.unscaledTime < graceEndTime && deferredLoadTasks.Count == 0)
        {
            UpdateLoadingText(0f, "Preparing classroom...");
            yield return null;
        }

        if (deferredLoadTasks.Count == 0)
        {
            SetLoadingTextVisible(false);
            yield break;
        }

        while (HasPendingDeferredLoads())
        {
            UpdateLoadingText(CalculateDeferredLoadProgress(), GetPrimaryDeferredLoadStatus("Preparing classroom..."));
            yield return null;
        }
    }

    private IEnumerator WaitForDeferredLoadsAfterSceneLoad()
    {
        float graceEndTime = Time.unscaledTime + deferredLoadRegistrationGraceSeconds;
        while (Time.unscaledTime < graceEndTime && deferredLoadTasks.Count == 0)
        {
            UpdateLoadingText(0.85f, "Preparing classroom...");
            yield return null;
        }

        if (deferredLoadTasks.Count == 0)
        {
            SetLoadingTextVisible(false);
            yield break;
        }

        while (HasPendingDeferredLoads())
        {
            float overallProgress = Mathf.Lerp(0.85f, 1f, CalculateDeferredLoadProgress());
            UpdateLoadingText(overallProgress, GetPrimaryDeferredLoadStatus("Preparing classroom..."));
            yield return null;
        }

        UpdateLoadingText(1f, "Ready.");
    }

    private bool HasPendingDeferredLoads()
    {
        foreach (KeyValuePair<string, DeferredLoadTask> entry in deferredLoadTasks)
        {
            if (!entry.Value.IsComplete)
            {
                return true;
            }
        }

        return false;
    }

    private float CalculateDeferredLoadProgress()
    {
        if (deferredLoadTasks.Count == 0)
        {
            return 1f;
        }

        float weightedProgress = 0f;
        float totalWeight = 0f;
        foreach (KeyValuePair<string, DeferredLoadTask> entry in deferredLoadTasks)
        {
            weightedProgress += entry.Value.Progress * entry.Value.Weight;
            totalWeight += entry.Value.Weight;
        }

        if (totalWeight <= 0.0001f)
        {
            return 1f;
        }

        return Mathf.Clamp01(weightedProgress / totalWeight);
    }

    private string GetPrimaryDeferredLoadStatus(string fallbackStatus)
    {
        foreach (KeyValuePair<string, DeferredLoadTask> entry in deferredLoadTasks)
        {
            if (!entry.Value.IsComplete && !string.IsNullOrWhiteSpace(entry.Value.Status))
            {
                return entry.Value.Status;
            }
        }

        foreach (KeyValuePair<string, DeferredLoadTask> entry in deferredLoadTasks)
        {
            if (!string.IsNullOrWhiteSpace(entry.Value.Status))
            {
                return entry.Value.Status;
            }
        }

        return fallbackStatus;
    }

    private void UpdateLoadingText(float progress, string status)
    {
        if (loadingContent == null)
        {
            return;
        }

        SetLoadingTextVisible(true);

        float normalizedProgress = Mathf.Clamp01(progress);
        int percent = Mathf.RoundToInt(normalizedProgress * 100f);
        string safeStatus = string.IsNullOrWhiteSpace(status) ? "Loading..." : status.Trim();

        if (loadingStatusText != null)
        {
            loadingStatusText.text = safeStatus;
        }

        if (loadingProgressFill != null)
        {
            loadingProgressFill.fillAmount = normalizedProgress;
        }

        if (loadingPercentText != null)
        {
            loadingPercentText.text = percent + "%";
        }
    }

    private void SetLoadingTextVisible(bool visible)
    {
        if (loadingContent != null)
        {
            loadingContent.SetActive(visible);
        }
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

    private struct DeferredLoadTask
    {
        public DeferredLoadTask(float progress, float weight, bool isComplete, string status)
        {
            Progress = progress;
            Weight = weight;
            IsComplete = isComplete;
            Status = status;
        }

        public float Progress;
        public float Weight;
        public bool IsComplete;
        public string Status;
    }
}

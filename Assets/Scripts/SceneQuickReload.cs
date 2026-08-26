using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public sealed class SceneQuickReload : MonoBehaviour
{
    private static SceneQuickReload instance;

#if ENABLE_INPUT_SYSTEM
    [SerializeField] private Key reloadKey = Key.R;
#else
    [SerializeField] private KeyCode reloadKey = KeyCode.R;
#endif
    [SerializeField] private bool ignoreWhileTyping = true;

    private bool isReloading;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (instance != null)
        {
            return;
        }

#if UNITY_2023_1_OR_NEWER
        SceneQuickReload existing = FindFirstObjectByType<SceneQuickReload>(FindObjectsInactive.Include);
#else
        SceneQuickReload existing = FindObjectOfType<SceneQuickReload>(true);
#endif
        if (existing != null)
        {
            existing.RegisterInstance();
            return;
        }

        GameObject reloadObject = new GameObject("Scene Quick Reload");
        reloadObject.AddComponent<SceneQuickReload>();
    }

    private void Awake()
    {
        RegisterInstance();
    }

    private void Update()
    {
        if (isReloading || IsTextInputFocused() || !WasReloadPressed())
        {
            return;
        }

        ReloadActiveScene();
    }

    public void ReloadActiveScene()
    {
        if (isReloading)
        {
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid())
        {
            Debug.LogWarning("SceneQuickReload could not find a valid active scene.", this);
            return;
        }

        isReloading = true;
        Time.timeScale = 1f;

        AsyncOperation reloadOperation = null;

#if UNITY_EDITOR
        if (Application.isPlaying && !string.IsNullOrWhiteSpace(activeScene.path))
        {
            reloadOperation = EditorSceneManager.LoadSceneAsyncInPlayMode(
                activeScene.path,
                new LoadSceneParameters(LoadSceneMode.Single));
        }
#endif

        if (reloadOperation == null && activeScene.buildIndex >= 0)
        {
            reloadOperation = SceneManager.LoadSceneAsync(activeScene.buildIndex, LoadSceneMode.Single);
        }

        if (reloadOperation == null && Application.CanStreamedLevelBeLoaded(activeScene.name))
        {
            reloadOperation = SceneManager.LoadSceneAsync(activeScene.name, LoadSceneMode.Single);
        }

        if (reloadOperation == null)
        {
            isReloading = false;
            Debug.LogWarning(
                "SceneQuickReload could not reload the active scene. Save it or add it to Build Settings.",
                this);
            return;
        }

        reloadOperation.completed += _ => isReloading = false;
    }

    private void RegisterInstance()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private bool IsTextInputFocused()
    {
        if (!ignoreWhileTyping || EventSystem.current == null)
        {
            return false;
        }

        GameObject selectedObject = EventSystem.current.currentSelectedGameObject;
        return selectedObject != null
            && (selectedObject.GetComponentInParent<TMP_InputField>() != null
                || selectedObject.GetComponentInParent<InputField>() != null);
    }

    private bool WasReloadPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return reloadKey != Key.None
            && Keyboard.current != null
            && Keyboard.current[reloadKey].wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(reloadKey);
#else
        return false;
#endif
    }
}

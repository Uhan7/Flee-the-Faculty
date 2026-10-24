using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class ButtonJuiceRuntime : MonoBehaviour
{
    private const string AudioResourcePath = "Button Juice Audio";
    private const float RescanIntervalSeconds = 0.5f;

    private static ButtonJuiceRuntime instance;

    private AudioSource audioSource;
    private ButtonJuiceAudioLibrary audioLibrary;
    private Coroutine scanRoutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (instance != null)
        {
            return;
        }

        GameObject host = new GameObject("Button Juice Runtime");
        instance = host.AddComponent<ButtonJuiceRuntime>();
        DontDestroyOnLoad(host);
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        audioLibrary = Resources.Load<ButtonJuiceAudioLibrary>(AudioResourcePath);
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.ignoreListenerPause = true;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        ScanButtons();
        scanRoutine = StartCoroutine(ScanForRuntimeButtons());
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        if (scanRoutine != null)
        {
            StopCoroutine(scanRoutine);
            scanRoutine = null;
        }
    }

    public static void PlayHover()
    {
        instance?.Play(instance.audioLibrary != null ? instance.audioLibrary.HoverClip : null, 0.62f);
    }

    public static void PlayClick()
    {
        instance?.Play(instance.audioLibrary != null ? instance.audioLibrary.ClickClip : null, 0.78f);
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ScanButtons();
    }

    private IEnumerator ScanForRuntimeButtons()
    {
        WaitForSecondsRealtime wait = new WaitForSecondsRealtime(RescanIntervalSeconds);
        while (true)
        {
            yield return wait;
            ScanButtons();
        }
    }

    private static void ScanButtons()
    {
#if UNITY_2023_1_OR_NEWER
        Button[] buttons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        Button[] buttons = FindObjectsOfType<Button>(true);
#endif
        for (int index = 0; index < buttons.Length; index++)
        {
            Button button = buttons[index];
            if (button == null
                || button.GetComponent<ButtonJuice>() != null
                || button.GetComponent<QuestionButtonAnimator>() != null
                || button.targetGraphic is TMP_Text)
            {
                continue;
            }

            button.gameObject.AddComponent<ButtonJuice>();
        }
    }

    private void Play(AudioClip clip, float volume)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.PlayOneShot(clip, volume * AudioSettingsStore.SfxVolume);
        }
    }
}

using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Persistent music and sound-effect preferences shared by every scene.
/// </summary>
public static class AudioSettingsStore
{
    private const string MusicKey = "Flee.MusicVolume";
    private const string SfxKey = "Flee.SfxVolume";

    public static event System.Action MusicVolumeChanged;

    public static float MusicVolume => PlayerPrefs.GetFloat(MusicKey, 1f);
    public static float SfxVolume => PlayerPrefs.GetFloat(SfxKey, 1f);

    public static void SetMusicVolume(float value)
    {
        PlayerPrefs.SetFloat(MusicKey, Mathf.Clamp01(value));
        PlayerPrefs.Save();
        MusicVolumeChanged?.Invoke();
    }

    public static void SetSfxVolume(float value)
    {
        PlayerPrefs.SetFloat(SfxKey, Mathf.Clamp01(value));
        PlayerPrefs.Save();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapMusicSources()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        AttachMusicVolumeSources();
    }

    private static void HandleSceneLoaded(Scene _, LoadSceneMode __)
    {
        AttachMusicVolumeSources();
    }

    private static void AttachMusicVolumeSources()
    {
#if UNITY_2023_1_OR_NEWER
        AudioSource[] sources = Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        AudioSource[] sources = Object.FindObjectsOfType<AudioSource>(true);
#endif
        foreach (AudioSource source in sources)
        {
            if (source != null
                && source.gameObject.name == "BGM Source"
                && source.GetComponent<MusicVolumeSource>() == null)
            {
                source.gameObject.AddComponent<MusicVolumeSource>();
            }
        }
    }
}

/// <summary>Preserves an authored music source's base volume and scales it.</summary>
[DisallowMultipleComponent]
public sealed class MusicVolumeSource : MonoBehaviour
{
    private AudioSource source;
    private float baseVolume = 1f;

    private void Awake()
    {
        source = GetComponent<AudioSource>();
        if (source != null)
        {
            baseVolume = source.volume;
        }
    }

    private void OnEnable()
    {
        AudioSettingsStore.MusicVolumeChanged += Apply;
        Apply();
    }

    private void OnDisable()
    {
        AudioSettingsStore.MusicVolumeChanged -= Apply;
    }

    private void Apply()
    {
        if (source != null)
        {
            source.volume = baseVolume * AudioSettingsStore.MusicVolume;
        }
    }
}

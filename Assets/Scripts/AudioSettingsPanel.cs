using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Wires the Settings window's sliders to the persistent audio values.</summary>
[DisallowMultipleComponent]
public sealed class AudioSettingsPanel : MonoBehaviour
{
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private TMP_Text musicValue;
    [SerializeField] private TMP_Text sfxValue;

    private void OnEnable()
    {
        Configure(musicSlider, AudioSettingsStore.MusicVolume, HandleMusicChanged);
        Configure(sfxSlider, AudioSettingsStore.SfxVolume, HandleSfxChanged);
        RefreshLabels();
    }

    private void OnDisable()
    {
        if (musicSlider != null)
        {
            musicSlider.onValueChanged.RemoveListener(HandleMusicChanged);
        }

        if (sfxSlider != null)
        {
            sfxSlider.onValueChanged.RemoveListener(HandleSfxChanged);
        }
    }

    private static void Configure(Slider slider, float value, UnityEngine.Events.UnityAction<float> callback)
    {
        if (slider == null)
        {
            return;
        }

        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.SetValueWithoutNotify(value);
        slider.onValueChanged.RemoveListener(callback);
        slider.onValueChanged.AddListener(callback);
    }

    private void HandleMusicChanged(float value)
    {
        AudioSettingsStore.SetMusicVolume(value);
        RefreshLabels();
    }

    private void HandleSfxChanged(float value)
    {
        AudioSettingsStore.SetSfxVolume(value);
        RefreshLabels();
    }

    private void RefreshLabels()
    {
        if (musicValue != null)
        {
            musicValue.text = Mathf.RoundToInt(AudioSettingsStore.MusicVolume * 100f) + "%";
        }

        if (sfxValue != null)
        {
            sfxValue.text = Mathf.RoundToInt(AudioSettingsStore.SfxVolume * 100f) + "%";
        }
    }
}

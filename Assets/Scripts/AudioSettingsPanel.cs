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
    [SerializeField] private Toggle textToSpeechToggle;
    [SerializeField] private TMP_Text textToSpeechValue;

    private void OnEnable()
    {
        EnsureTextToSpeechToggle();
        Configure(musicSlider, AudioSettingsStore.MusicVolume, HandleMusicChanged);
        Configure(sfxSlider, AudioSettingsStore.SfxVolume, HandleSfxChanged);
        Configure(textToSpeechToggle, AudioSettingsStore.TextToSpeechEnabled, HandleTextToSpeechChanged);
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

        if (textToSpeechToggle != null)
        {
            textToSpeechToggle.onValueChanged.RemoveListener(HandleTextToSpeechChanged);
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

    private static void Configure(
        Toggle toggle,
        bool value,
        UnityEngine.Events.UnityAction<bool> callback)
    {
        if (toggle == null)
        {
            return;
        }

        toggle.SetIsOnWithoutNotify(value);
        toggle.onValueChanged.RemoveListener(callback);
        toggle.onValueChanged.AddListener(callback);
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

    private void HandleTextToSpeechChanged(bool enabled)
    {
        AudioSettingsStore.SetTextToSpeechEnabled(enabled);
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

        if (textToSpeechValue != null)
        {
            textToSpeechValue.text = AudioSettingsStore.TextToSpeechEnabled ? "On" : "Off";
        }
    }

    private void EnsureTextToSpeechToggle()
    {
        if (textToSpeechToggle != null)
        {
            return;
        }

        Transform existingRow = transform.Find("Spoken Dialogue Row");
        if (existingRow != null)
        {
            textToSpeechToggle = existingRow.GetComponentInChildren<Toggle>(true);
            TMP_Text[] existingLabels = existingRow.GetComponentsInChildren<TMP_Text>(true);
            for (int index = 0; index < existingLabels.Length; index++)
            {
                if (existingLabels[index].name == "Value")
                {
                    textToSpeechValue = existingLabels[index];
                    break;
                }
            }

            return;
        }

        TMP_FontAsset font = musicValue != null && musicValue.font != null
            ? musicValue.font
            : TMP_Settings.defaultFontAsset;
        GameObject row = new GameObject("Spoken Dialogue Row", typeof(RectTransform), typeof(LayoutElement));
        row.layer = gameObject.layer;
        RectTransform rowRect = row.GetComponent<RectTransform>();
        rowRect.SetParent(transform, false);
        rowRect.sizeDelta = new Vector2(0f, 64f);
        row.GetComponent<LayoutElement>().preferredHeight = 64f;
        row.transform.SetSiblingIndex(Mathf.Max(0, transform.childCount - 2));

        TMP_Text label = CreateRuntimeText("Label", rowRect, font, "Spoken Dialogue", 25f);
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = new Vector2(0.62f, 1f);
        labelRect.offsetMin = new Vector2(18f, 0f);
        labelRect.offsetMax = Vector2.zero;
        label.alignment = TextAlignmentOptions.MidlineLeft;

        GameObject toggleObject = new GameObject(
            "Spoken Dialogue Toggle",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Toggle));
        toggleObject.layer = gameObject.layer;
        RectTransform toggleRect = toggleObject.GetComponent<RectTransform>();
        toggleRect.SetParent(rowRect, false);
        toggleRect.anchorMin = new Vector2(1f, 0.5f);
        toggleRect.anchorMax = new Vector2(1f, 0.5f);
        toggleRect.pivot = new Vector2(1f, 0.5f);
        toggleRect.anchoredPosition = new Vector2(-18f, 0f);
        toggleRect.sizeDelta = new Vector2(150f, 44f);
        Image toggleBackground = toggleObject.GetComponent<Image>();
        toggleBackground.color = new Color(0.12f, 0.16f, 0.2f, 0.2f);

        GameObject checkObject = new GameObject(
            "Checkmark",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        checkObject.layer = gameObject.layer;
        RectTransform checkRect = checkObject.GetComponent<RectTransform>();
        checkRect.SetParent(toggleRect, false);
        checkRect.anchorMin = new Vector2(0f, 0.5f);
        checkRect.anchorMax = new Vector2(0f, 0.5f);
        checkRect.pivot = new Vector2(0f, 0.5f);
        checkRect.anchoredPosition = new Vector2(8f, 0f);
        checkRect.sizeDelta = new Vector2(28f, 28f);
        Image checkmark = checkObject.GetComponent<Image>();
        checkmark.color = new Color(0.25f, 0.72f, 0.8f, 1f);

        textToSpeechValue = CreateRuntimeText("Value", toggleRect, font, string.Empty, 23f);
        RectTransform valueRect = textToSpeechValue.rectTransform;
        valueRect.anchorMin = new Vector2(0f, 0f);
        valueRect.anchorMax = Vector2.one;
        valueRect.offsetMin = new Vector2(44f, 0f);
        valueRect.offsetMax = new Vector2(-8f, 0f);
        textToSpeechValue.alignment = TextAlignmentOptions.Center;

        textToSpeechToggle = toggleObject.GetComponent<Toggle>();
        textToSpeechToggle.targetGraphic = toggleBackground;
        textToSpeechToggle.graphic = checkmark;
    }

    private static TMP_Text CreateRuntimeText(
        string objectName,
        Transform parent,
        TMP_FontAsset font,
        string value,
        float fontSize)
    {
        GameObject textObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        TMP_Text text = textObject.GetComponent<TMP_Text>();
        text.font = font;
        text.text = value;
        text.fontSize = fontSize;
        text.color = new Color(0.12f, 0.16f, 0.2f, 1f);
        text.raycastTarget = false;
        return text;
    }
}

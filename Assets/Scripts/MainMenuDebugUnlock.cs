using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// A deliberately hidden title-screen gesture for development builds: click
/// the title logo five times in quick succession to enable debug mode.
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuDebugUnlock : MonoBehaviour, IPointerClickHandler
{
    private const int RequiredClicks = 5;
    private const float ClickWindowSeconds = 2.5f;

    private RectTransform menuCanvas;
    private GameObject statusButtonObject;
    private int clickCount;
    private float firstClickTime;

    public void Configure(RectTransform canvas)
    {
        menuCanvas = canvas;
        RefreshStatusButton();
    }

    private void OnEnable()
    {
        DebugModeStore.Changed += HandleDebugModeChanged;
    }

    private void OnDisable()
    {
        DebugModeStore.Changed -= HandleDebugModeChanged;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (DebugModeStore.IsEnabled)
        {
            return;
        }

        if (clickCount == 0 || Time.unscaledTime - firstClickTime > ClickWindowSeconds)
        {
            clickCount = 0;
            firstClickTime = Time.unscaledTime;
        }

        clickCount++;
        if (clickCount < RequiredClicks)
        {
            return;
        }

        clickCount = 0;
        DebugModeStore.SetEnabled(true);
    }

    private void HandleDebugModeChanged(bool _)
    {
        RefreshStatusButton();
    }

    private void RefreshStatusButton()
    {
        if (!DebugModeStore.IsEnabled)
        {
            if (statusButtonObject != null)
            {
                Destroy(statusButtonObject);
                statusButtonObject = null;
            }

            return;
        }

        if (statusButtonObject != null || menuCanvas == null)
        {
            return;
        }

        statusButtonObject = new GameObject(
            "Debug Mode Status",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button));
        statusButtonObject.layer = 5;

        RectTransform buttonRect = statusButtonObject.GetComponent<RectTransform>();
        buttonRect.SetParent(menuCanvas, false);
        buttonRect.anchorMin = Vector2.zero;
        buttonRect.anchorMax = Vector2.zero;
        buttonRect.pivot = Vector2.zero;
        buttonRect.anchoredPosition = new Vector2(24f, 24f);
        buttonRect.sizeDelta = new Vector2(310f, 54f);

        Image image = statusButtonObject.GetComponent<Image>();
        image.color = new Color(0.34f, 0.12f, 0.17f, 0.96f);

        Button button = statusButtonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(DisableDebugMode);

        GameObject labelObject = new GameObject(
            "Debug Mode Status Text",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        labelObject.layer = 5;

        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.SetParent(buttonRect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(12f, 4f);
        labelRect.offsetMax = new Vector2(-12f, -4f);

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.text = "DEBUG MODE  |  CLICK TO DISABLE";
        label.font = TMP_Settings.defaultFontAsset;
        label.fontSize = 18f;
        label.enableAutoSizing = true;
        label.fontSizeMin = 13f;
        label.fontSizeMax = 18f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
    }

    private void DisableDebugMode()
    {
        DebugModeStore.SetEnabled(false);
    }
}

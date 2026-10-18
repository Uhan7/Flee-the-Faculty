using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The three buttons of the Learning phase: Continue Lesson, Back and the mic.
///
/// This view only reports clicks. What each one means belongs to the
/// controller: Continue Lesson moves the cursor to the next step, Back moves it
/// to the previous one, and the mic starts a Learner Interjection. The
/// controller tells the view what to show: <see cref="SetContinueVisible"/> at
/// the end of a step, <see cref="SetBackAvailable"/> off on the first step, and
/// <see cref="SetMicAvailable"/> only when the browser can listen. The mic is
/// hidden until it is told it is available.
///
/// Built from code, with no scene dependency, so the scene builder can add it
/// with <see cref="Create"/>. Move <see cref="ContinueRect"/>,
/// <see cref="BackRect"/> and <see cref="MicRect"/> to place them.
///
/// A click that lands on one of these must not also advance the dialogue.
/// <see cref="DialogueManager"/> advances on any left click that is not over
/// the dialogue box's own buttons (<c>SceneDialogueView.IsPointerOverControlButton</c>),
/// and it reads the mouse in its own Update, which runs before the UI sends the
/// click to a button. So a check inside a button's click handler, such as
/// <c>EventSystem.current.IsPointerOverGameObject()</c>, is too late: the
/// dialogue has already moved on. Two things here deal with that instead.
/// <see cref="IsPointerOverControls"/> answers the same question
/// <c>IsPointerOverControlButton</c> does, for these buttons, using their
/// rectangles rather than <c>IsPointerOverGameObject</c>, which would also be
/// true over the dialogue box and stop clicking the box to advance. And this
/// component runs before <see cref="DialogueManager"/> (execution order -100
/// against its -50) and, on a frame where the mouse went down over one of its
/// buttons, switches the manager off until that frame's LateUpdate so it never
/// sees the click. The cleaner fix is one line in
/// <c>DialogueManager.IsPointerOverControlButton</c> that also returns
/// <see cref="IsPointerOverControls"/>; this class does not depend on it.
/// </summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public sealed class LessonControlsView : MonoBehaviour
{
    private const string ContinueLabelText = "Continue Lesson";
    private const string BackLabelText = "Back";
    private const string MicLabelText = "Mic";

    private static readonly List<LessonControlsView> ActiveViews = new List<LessonControlsView>();

    [SerializeField] private Button continueButton;
    [SerializeField] private Button backButton;
    [SerializeField] private Button micButton;

    private DialogueManager suppressedManager;
    private bool wired;

    public event Action ContinueClicked;
    public event Action BackClicked;
    public event Action MicClicked;

    public RectTransform ContinueRect => RectOf(continueButton);
    public RectTransform BackRect => RectOf(backButton);
    public RectTransform MicRect => RectOf(micButton);

    public bool IsContinueVisible => continueButton != null && continueButton.gameObject.activeSelf;
    public bool IsBackInteractable => backButton != null && backButton.interactable;
    public bool IsMicVisible => micButton != null && micButton.gameObject.activeSelf;

    /// <summary>
    /// Build the controls under <paramref name="parent"/>, which should be a
    /// canvas or a child of one. <paramref name="styleSource"/> is optional: a
    /// button whose look the new ones copy, such as the one on
    /// <see cref="DialogueExitConversationButton"/>.
    /// </summary>
    public static LessonControlsView Create(
        Transform parent,
        TMP_FontAsset font = null,
        Button styleSource = null)
    {
        GameObject rootObject = new GameObject("Lesson Controls", typeof(RectTransform));
        rootObject.layer = 5;
        RectTransform rootRect = rootObject.GetComponent<RectTransform>();
        rootRect.SetParent(parent, false);
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        LessonControlsView view = rootObject.AddComponent<LessonControlsView>();
        view.continueButton = CreateButton(
            rootRect,
            "Continue Lesson Button",
            ContinueLabelText,
            null,
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0f, 250f),
            new Vector2(340f, 76f),
            font,
            styleSource);
        DialogueUiIconLibrary icons = DialogueUiIconLibrary.Load();
        view.backButton = CreateButton(
            rootRect,
            "Lesson Back Button",
            BackLabelText,
            icons != null ? icons.BackIcon : null,
            new Vector2(0f, 0f),
            new Vector2(0f, 0f),
            new Vector2(28f, 120f),
            new Vector2(88f, 88f),
            font,
            styleSource);
        view.micButton = CreateButton(
            rootRect,
            "Lesson Mic Button",
            MicLabelText,
            null,
            new Vector2(1f, 0f),
            new Vector2(1f, 0f),
            new Vector2(-28f, 120f),
            new Vector2(88f, 88f),
            font,
            styleSource);
        view.Wire();
        view.SetContinueVisible(false);
        view.SetBackAvailable(false);
        view.SetMicAvailable(false);
        return view;
    }

    /// <summary>Continue Lesson is shown at the end of a step and hidden while one plays.</summary>
    public void SetContinueVisible(bool visible)
    {
        SetButtonVisible(continueButton, visible);
    }

    /// <summary>
    /// Back is always on screen and greyed out when there is no previous step,
    /// which is the first step and during a replay.
    /// </summary>
    public void SetBackAvailable(bool available)
    {
        if (backButton != null)
        {
            backButton.interactable = available;
        }
    }

    /// <summary>
    /// The mic is hidden unless the browser can listen, so a Learner on a
    /// browser without speech recognition is never offered a button that does
    /// nothing.
    /// </summary>
    public void SetMicAvailable(bool available)
    {
        SetButtonVisible(micButton, available);
    }

    /// <summary>
    /// Relabel the mic button, for the stretch where it means Cancel or Skip
    /// instead of "ask the Teacher". Null or blank puts the usual label back.
    /// </summary>
    public void SetMicLabel(string text)
    {
        TMP_Text label = micButton != null ? micButton.GetComponentInChildren<TMP_Text>(true) : null;
        if (label != null)
        {
            label.text = string.IsNullOrWhiteSpace(text) ? MicLabelText : text;
        }
    }

    /// <summary>
    /// Hide or show all three, for the stretches where the controls would get
    /// in the way, such as while she is answering aloud.
    /// </summary>
    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }

    /// <summary>
    /// True when the pointer is over any visible button of any
    /// <see cref="LessonControlsView"/>. The same question as
    /// <c>SceneDialogueView.IsPointerOverControlButton</c>, for these buttons.
    /// </summary>
    public static bool IsPointerOverControls()
    {
        if (ActiveViews.Count == 0 || !TryGetPointerPosition(out Vector2 pointerPosition))
        {
            return false;
        }

        for (int index = 0; index < ActiveViews.Count; index++)
        {
            if (ActiveViews[index] != null && ActiveViews[index].ContainsPoint(pointerPosition))
            {
                return true;
            }
        }

        return false;
    }

    private void Awake()
    {
        Wire();
    }

    private void OnEnable()
    {
        if (!ActiveViews.Contains(this))
        {
            ActiveViews.Add(this);
        }
    }

    private void OnDisable()
    {
        ActiveViews.Remove(this);
        ReleaseDialogueManager();
    }

    private void OnDestroy()
    {
        Unwire();
    }

    private void Update()
    {
        if (suppressedManager != null
            || !WasPointerPressedThisFrame()
            || !TryGetPointerPosition(out Vector2 pointerPosition)
            || !ContainsPoint(pointerPosition))
        {
            return;
        }

        DialogueManager manager = DialogueManager.Instance;
        if (manager == null || !manager.enabled)
        {
            return;
        }

        manager.enabled = false;
        suppressedManager = manager;
    }

    private void LateUpdate()
    {
        ReleaseDialogueManager();
    }

    private void ReleaseDialogueManager()
    {
        if (suppressedManager == null)
        {
            return;
        }

        suppressedManager.enabled = true;
        suppressedManager = null;
    }

    private void Wire()
    {
        // Awake runs as Create adds the component, before Create has handed it
        // the buttons, so there is nothing to wire yet and Create wires them
        // itself once they are in place.
        if (wired || (continueButton == null && backButton == null && micButton == null))
        {
            return;
        }

        wired = true;
        AddListener(continueButton, HandleContinue);
        AddListener(backButton, HandleBack);
        AddListener(micButton, HandleMic);
    }

    private void Unwire()
    {
        if (!wired)
        {
            return;
        }

        wired = false;
        RemoveListener(continueButton, HandleContinue);
        RemoveListener(backButton, HandleBack);
        RemoveListener(micButton, HandleMic);
    }

    private void HandleContinue()
    {
        ContinueClicked?.Invoke();
    }

    private void HandleBack()
    {
        BackClicked?.Invoke();
    }

    private void HandleMic()
    {
        MicClicked?.Invoke();
    }

    private bool ContainsPoint(Vector2 screenPoint)
    {
        return ButtonContains(continueButton, screenPoint)
            || ButtonContains(backButton, screenPoint)
            || ButtonContains(micButton, screenPoint);
    }

    private static bool ButtonContains(Button button, Vector2 screenPoint)
    {
        if (button == null || !button.gameObject.activeInHierarchy)
        {
            return false;
        }

        RectTransform buttonRect = button.transform as RectTransform;
        if (buttonRect == null)
        {
            return false;
        }

        Canvas canvas = button.GetComponentInParent<Canvas>();
        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
        return RectTransformUtility.RectangleContainsScreenPoint(buttonRect, screenPoint, eventCamera);
    }

    private static void SetButtonVisible(Button button, bool visible)
    {
        if (button != null && button.gameObject.activeSelf != visible)
        {
            button.gameObject.SetActive(visible);
        }
    }

    private static void AddListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null)
        {
            button.onClick.AddListener(action);
        }
    }

    private static void RemoveListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null)
        {
            button.onClick.RemoveListener(action);
        }
    }

    private static RectTransform RectOf(Button button)
    {
        return button != null ? button.transform as RectTransform : null;
    }

    private static bool WasPointerPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(0))
        {
            return true;
        }
#endif

        return false;
    }

    private static bool TryGetPointerPosition(out Vector2 pointerPosition)
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            pointerPosition = Mouse.current.position.ReadValue();
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        pointerPosition = Input.mousePosition;
        return true;
#else
        pointerPosition = Vector2.zero;
        return false;
#endif
    }

    private static Button CreateButton(
        Transform parent,
        string objectName,
        string labelText,
        Sprite icon,
        Vector2 anchor,
        Vector2 pivot,
        Vector2 position,
        Vector2 size,
        TMP_FontAsset font,
        Button styleSource)
    {
        Image sourceImage = styleSource != null ? styleSource.GetComponent<Image>() : null;
        TMP_Text sourceLabel = styleSource != null
            ? styleSource.GetComponentInChildren<TMP_Text>(true)
            : null;

        GameObject buttonObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button));
        buttonObject.layer = 5;
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.SetParent(parent, false);
        buttonRect.anchorMin = anchor;
        buttonRect.anchorMax = anchor;
        buttonRect.pivot = pivot;
        buttonRect.anchoredPosition = position;
        buttonRect.sizeDelta = size;

        Image image = buttonObject.GetComponent<Image>();
        if (sourceImage != null)
        {
            image.sprite = sourceImage.sprite;
            image.type = sourceImage.type;
            image.color = sourceImage.color;
            image.material = sourceImage.material;
        }
        else
        {
            image.color = new Color(0.18f, 0.68f, 0.76f, 1f);
        }

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        if (styleSource != null)
        {
            button.transition = styleSource.transition;
            button.colors = styleSource.colors;
            button.spriteState = styleSource.spriteState;
        }

        if (icon != null)
        {
            GameObject iconObject = new GameObject(
                objectName + " Icon",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            iconObject.layer = 5;
            RectTransform iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.SetParent(buttonRect, false);
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(14f, 14f);
            iconRect.offsetMax = new Vector2(-14f, -14f);
            Image iconImage = iconObject.GetComponent<Image>();
            iconImage.sprite = icon;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            return button;
        }

        GameObject labelObject = new GameObject(
            objectName + " Text",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        labelObject.layer = 5;
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.SetParent(buttonRect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(12f, 6f);
        labelRect.offsetMax = new Vector2(-12f, -6f);

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.text = labelText;
        label.font = sourceLabel != null && sourceLabel.font != null
            ? sourceLabel.font
            : font != null ? font : TMP_Settings.defaultFontAsset;
        label.fontStyle = sourceLabel != null ? sourceLabel.fontStyle : FontStyles.Normal;
        label.fontSize = sourceLabel != null ? sourceLabel.fontSize : 26f;
        label.enableAutoSizing = true;
        label.fontSizeMin = 16f;
        label.fontSizeMax = Mathf.Max(22f, label.fontSize);
        label.alignment = TextAlignmentOptions.Center;
        label.color = sourceLabel != null
            ? sourceLabel.color
            : new Color(0.22f, 0.12f, 0.09f, 1f);
        label.raycastTarget = false;
        return button;
    }
}

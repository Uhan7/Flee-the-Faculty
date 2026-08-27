using System;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class AraBotPromptButton : MonoBehaviour
{
    public enum PromptRole
    {
        Default,
        Mic,
        Redo,
        Keyboard,
        Thinking
    }

    [Header("References")]
    [SerializeField] private Button button;
    [SerializeField] private Image bubbleImage;
    [SerializeField] private Image iconImage;

    [Header("Behavior")]
    [SerializeField] private PromptRole role = PromptRole.Default;

    [Header("Look")]
    [SerializeField] private Sprite bubbleSprite;
    [SerializeField] private Color bubbleColor = Color.white;
    [SerializeField] private Color iconColor = new Color(0.1f, 0.53f, 0.68f, 1f);

    private Action clickAction;
    public PromptRole Role => role;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnDisable()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(HandleButtonClicked);
        }
    }

    public void Show(Action onClick)
    {
        ResolveReferences();
        clickAction = onClick;

        if (bubbleImage != null)
        {
            if (bubbleImage.sprite == null && bubbleSprite != null)
            {
                bubbleImage.sprite = bubbleSprite;
            }

            bubbleImage.type = Image.Type.Simple;
            bubbleImage.preserveAspect = true;
            bubbleImage.color = bubbleColor;
            bubbleImage.raycastTarget = true;
        }

        if (iconImage != null)
        {
            iconImage.color = iconColor;
            iconImage.raycastTarget = true;
            iconImage.gameObject.SetActive(false);
        }

        if (button != null)
        {
            button.onClick.RemoveListener(HandleButtonClicked);
            button.onClick.AddListener(HandleButtonClicked);
            button.interactable = onClick != null;
        }

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
    }

    public void Hide()
    {
        ResolveReferences();
        clickAction = null;

        if (button != null)
        {
            button.onClick.RemoveListener(HandleButtonClicked);
            button.interactable = false;
        }

        if (iconImage != null)
        {
            iconImage.gameObject.SetActive(false);
        }

        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }
    }

    public void ShowVisualOnly()
    {
        Show(null);

        Graphic[] graphics = GetComponentsInChildren<Graphic>(true);
        for (int index = 0; index < graphics.Length; index++)
        {
            if (graphics[index] != null)
            {
                graphics[index].raycastTarget = false;
            }
        }
    }

    private void ResolveReferences()
    {
        if (button == null)
        {
            button = GetComponent<Button>();
        }

        if (bubbleImage == null)
        {
            bubbleImage = GetComponent<Image>();
        }

        if (iconImage == null)
        {
            iconImage = GetComponentInChildren<Image>(true);
            if (iconImage == bubbleImage)
            {
                iconImage = null;
            }
        }
    }

    private void HandleButtonClicked()
    {
        clickAction?.Invoke();
    }
}

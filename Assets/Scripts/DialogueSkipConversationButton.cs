using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class DialogueSkipConversationButton : MonoBehaviour, IPointerDownHandler
{
    private Button skipButton;
    private int lastRequestFrame = -1;

    private void Awake()
    {
        skipButton = GetComponent<Button>();
        if (skipButton != null)
        {
            skipButton.onClick.AddListener(RequestSkip);
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData != null && eventData.button == PointerEventData.InputButton.Left)
        {
            RequestSkip();
        }
    }

    private void RequestSkip()
    {
        if (lastRequestFrame == Time.frameCount)
        {
            return;
        }

        lastRequestFrame = Time.frameCount;
        StudentDialogueInteraction.SkipActiveConversation();
    }

    private void OnDestroy()
    {
        if (skipButton != null)
        {
            skipButton.onClick.RemoveListener(RequestSkip);
        }
    }
}

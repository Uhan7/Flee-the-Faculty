using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class DialogueSkipConversationButton : MonoBehaviour
{
    private Button skipButton;
    private GameObject skipRemainingButtonObject;
    private int lastRequestFrame = -1;

    private void Awake()
    {
        if (!DebugModeStore.IsEnabled)
        {
            gameObject.SetActive(false);
            return;
        }

        skipButton = GetComponent<Button>();
        if (skipButton != null)
        {
            skipButton.onClick.AddListener(RequestSkip);
        }

        FindOptionalSkipRemainingButton();
    }

    private void OnEnable()
    {
        StudentDialogueInteraction.ConversationStarted += HandleConversationStarted;
        StudentDialogueInteraction.ConversationEnded += HandleConversationEnded;
        SetSkipRemainingVisible(false);
    }

    private void OnDisable()
    {
        StudentDialogueInteraction.ConversationStarted -= HandleConversationStarted;
        StudentDialogueInteraction.ConversationEnded -= HandleConversationEnded;
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

    private void HandleConversationStarted(DialogueActor _)
    {
        SetSkipRemainingVisible(true);
    }

    private void HandleConversationEnded()
    {
        SetSkipRemainingVisible(false);
    }

    private void SetSkipRemainingVisible(bool visible)
    {
        if (skipRemainingButtonObject != null)
        {
            skipRemainingButtonObject.SetActive(visible);
        }
    }

    private void FindOptionalSkipRemainingButton()
    {
        Transform parent = transform.parent;
        if (parent == null)
        {
            return;
        }

        Transform existing = parent.Find("Skip To Classroom Scene Button");
        if (existing != null)
        {
            skipRemainingButtonObject = existing.gameObject;
            SetSkipRemainingVisible(false);
        }
    }
}

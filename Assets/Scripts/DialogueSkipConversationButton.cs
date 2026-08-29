using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class DialogueSkipConversationButton : MonoBehaviour
{
    private Button skipButton;

    private void Awake()
    {
        skipButton = GetComponent<Button>();
        if (skipButton != null)
        {
            skipButton.onClick.AddListener(StudentDialogueInteraction.SkipActiveConversation);
        }
    }

    private void OnDestroy()
    {
        if (skipButton != null)
        {
            skipButton.onClick.RemoveListener(StudentDialogueInteraction.SkipActiveConversation);
        }
    }
}

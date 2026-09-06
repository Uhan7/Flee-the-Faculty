using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class DialogueExitConversationButton : MonoBehaviour
{
    private Button exitButton;

    private void Awake()
    {
        exitButton = GetComponent<Button>();
        exitButton.onClick.AddListener(RequestExit);
    }

    private void RequestExit()
    {
        if (!StudentDialogueInteraction.HasActiveConversation)
        {
            return;
        }

        StudentDialogueInteraction.ExitActiveConversation();
    }

    private void OnDestroy()
    {
        if (exitButton != null)
        {
            exitButton.onClick.RemoveListener(RequestExit);
        }
    }
}

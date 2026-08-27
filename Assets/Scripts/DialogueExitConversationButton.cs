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
        exitButton.onClick.AddListener(StudentDialogueInteraction.ExitActiveConversation);
    }

    private void OnDestroy()
    {
        if (exitButton != null)
        {
            exitButton.onClick.RemoveListener(StudentDialogueInteraction.ExitActiveConversation);
        }
    }
}

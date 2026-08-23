using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SceneDialogueView : MonoBehaviour, IDialogueView
{
    [SerializeField] private GameObject dialogueContainer;
    [SerializeField] private TMP_Text speakerText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private GameObject continueIndicator;

    private void Awake()
    {
        SetVisible(false);
    }

    public void SetVisible(bool visible)
    {
        if (dialogueContainer != null)
        {
            dialogueContainer.SetActive(visible);
        }

        if (!visible)
        {
            if (speakerText != null)
            {
                speakerText.text = string.Empty;
            }

            if (bodyText != null)
            {
                bodyText.text = string.Empty;
            }

            SetContinueIndicator(false);
        }
    }

    public void DisplayLine(DialogueLine line, string visibleText, bool canAdvance)
    {
        if (dialogueContainer != null && !dialogueContainer.activeSelf)
        {
            dialogueContainer.SetActive(true);
        }

        if (speakerText != null)
        {
            speakerText.text = line != null ? line.Speaker : string.Empty;
        }

        if (bodyText != null)
        {
            bodyText.text = visibleText ?? string.Empty;
        }

        SetContinueIndicator(canAdvance);
    }

    private void SetContinueIndicator(bool visible)
    {
        if (continueIndicator != null)
        {
            continueIndicator.SetActive(visible);
        }
    }
}

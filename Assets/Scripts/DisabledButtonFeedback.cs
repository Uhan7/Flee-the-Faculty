using UnityEngine;
using UnityEngine.UI;

/// <summary>Shows the Button's actual interactable state without changing Image colours.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Button), typeof(CanvasGroup))]
public sealed class DisabledButtonFeedback : MonoBehaviour
{
    public static void SetInteractable(Button target, bool interactable)
    {
        if (target == null)
        {
            return;
        }
        target.interactable = interactable;
        CanvasGroup group = target.GetComponent<CanvasGroup>();
        if (group == null)
        {
            group = target.gameObject.AddComponent<CanvasGroup>();
        }
        group.alpha = interactable ? 1f : 0.45f;
    }

    private Button button;
    private CanvasGroup appearance;

    private void Awake()
    {
        button = GetComponent<Button>();
        appearance = GetComponent<CanvasGroup>();
        if (appearance == null)
        {
            appearance = gameObject.AddComponent<CanvasGroup>();
        }
    }

    private void LateUpdate()
    {
        bool enabled = button.IsInteractable();
        appearance.alpha = enabled ? 1f : 0.45f;
        // Keep raycasts blocked so a disabled control cannot click through to a control below.
        appearance.blocksRaycasts = true;
    }
}

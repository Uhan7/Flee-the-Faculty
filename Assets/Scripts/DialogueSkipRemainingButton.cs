using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class DialogueSkipRemainingButton : MonoBehaviour
{
    private Button button;
    private int lastRequestFrame = -1;

    private void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(RequestSkipRemaining);
    }

    private void RequestSkipRemaining()
    {
        if (lastRequestFrame == Time.frameCount)
        {
            return;
        }

        lastRequestFrame = Time.frameCount;
#if UNITY_2023_1_OR_NEWER
        ClassroomSessionController classroom = FindFirstObjectByType<ClassroomSessionController>();
#else
        ClassroomSessionController classroom = FindObjectOfType<ClassroomSessionController>();
#endif
        classroom?.SkipRemainingConversations();
    }

    private void OnDestroy()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(RequestSkipRemaining);
        }
    }
}

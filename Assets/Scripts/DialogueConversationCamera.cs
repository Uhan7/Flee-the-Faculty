using Unity.Cinemachine;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CinemachineCamera))]
public sealed class DialogueConversationCamera : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CinemachineCamera conversationCamera;
    [SerializeField] private Transform araBotTarget;

    [Header("Priority")]
    [SerializeField] private int activePriority = 20;
    [SerializeField] private int inactivePriority = -10;

    private DialogueManager dialogueManager;
    private bool conversationActive;

    private void Reset()
    {
        conversationCamera = GetComponent<CinemachineCamera>();
    }

    private void Awake()
    {
        if (conversationCamera == null)
        {
            conversationCamera = GetComponent<CinemachineCamera>();
        }

        SetCameraPriority(inactivePriority);
    }

    private void OnEnable()
    {
        StudentDialogueInteraction.ConversationStarted += HandleConversationStarted;
        StudentDialogueInteraction.AraBotResponseRequested += HandleAraBotResponseRequested;
        StudentDialogueInteraction.ConversationEnded += HandleConversationEnded;

        dialogueManager = DialogueManager.GetOrCreate();
        if (dialogueManager != null)
        {
            dialogueManager.LineChanged += HandleLineChanged;
        }
    }

    private void OnDisable()
    {
        StudentDialogueInteraction.ConversationStarted -= HandleConversationStarted;
        StudentDialogueInteraction.AraBotResponseRequested -= HandleAraBotResponseRequested;
        StudentDialogueInteraction.ConversationEnded -= HandleConversationEnded;

        if (dialogueManager != null)
        {
            dialogueManager.LineChanged -= HandleLineChanged;
        }

        conversationActive = false;
        FocusOn(araBotTarget);
        SetCameraPriority(inactivePriority);
    }

    private void HandleConversationStarted(DialogueActor studentActor)
    {
        BeginExternalFocus(studentActor != null ? studentActor.transform : araBotTarget);
    }

    private void HandleLineChanged(IDialogueLine line, int _)
    {
        if (!conversationActive)
        {
            return;
        }

        Transform speakerTarget = ResolveSpeakerTarget(line);
        if (speakerTarget != null)
        {
            FocusOn(speakerTarget);
        }
    }

    private void HandleAraBotResponseRequested()
    {
        if (conversationActive)
        {
            FocusOn(araBotTarget);
        }
    }

    private void HandleConversationEnded()
    {
        EndExternalFocus();
    }

    public void BeginExternalFocus(Transform target)
    {
        conversationActive = true;
        FocusOn(target != null ? target : araBotTarget);
        SetCameraPriority(activePriority);
    }

    public void EndExternalFocus()
    {
        conversationActive = false;
        FocusOn(araBotTarget);
        SetCameraPriority(inactivePriority);
    }

    private void FocusOn(Transform target)
    {
        if (conversationCamera != null && target != null)
        {
            conversationCamera.Follow = target;
        }
    }

    private void SetCameraPriority(int priority)
    {
        if (conversationCamera != null)
        {
            conversationCamera.Priority = priority;
        }
    }

    private static Transform ResolveSpeakerTarget(IDialogueLine line)
    {
        if (line == null || line.SpeakerReference == null)
        {
            return null;
        }

        if (line.SpeakerReference is DialogueActor actor)
        {
            return actor.transform;
        }

        if (line.SpeakerReference is Component component)
        {
            DialogueActor componentActor = component.GetComponent<DialogueActor>();
            return componentActor != null ? componentActor.transform : component.transform;
        }

        if (line.SpeakerReference is GameObject speakerObject)
        {
            return speakerObject.transform;
        }

        return null;
    }
}

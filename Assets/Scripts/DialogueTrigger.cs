using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public sealed class DialogueTrigger : MonoBehaviour
{
    [SerializeField] private Dialogue dialogue;
    [Tooltip("Optional. The shared manager is found or created when this is empty.")]
    [SerializeField] private DialogueManager dialogueManager;
    [Tooltip("Optional. When empty, any moving Rigidbody2D can activate this trigger.")]
    [SerializeField] private Transform activator;

    [Header("Behavior")]
    [SerializeField] private bool startOnEnable;
    [SerializeField] private bool playOnce = true;
    [SerializeField] private bool closeOnExit;

    private bool hasPlayed;

    public Dialogue Dialogue => dialogue;
    public bool HasPlayed => hasPlayed;

    public event Action<DialogueTrigger> Triggered;
    public event Action<DialogueTrigger> Completed;

    private void Awake()
    {
        GetComponent<Collider2D>().isTrigger = true;
    }

    private void OnEnable()
    {
        if (startOnEnable)
        {
            TriggerDialogue();
        }
    }

    private void OnDisable()
    {
        UnsubscribeFromManager();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (CanBeActivatedBy(other))
        {
            TriggerDialogue();
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!closeOnExit || !CanBeActivatedBy(other) || dialogueManager == null)
        {
            return;
        }

        if (dialogueManager.ActiveDialogue == dialogue)
        {
            dialogueManager.EndDialogue();
        }
    }

    public void TriggerDialogue()
    {
        if (dialogue == null || (playOnce && hasPlayed))
        {
            return;
        }

        dialogueManager = dialogueManager != null ? dialogueManager : DialogueManager.GetOrCreate();
        UnsubscribeFromManager();
        dialogueManager.DialogueEnded += HandleDialogueEnded;

        if (!dialogueManager.Play(dialogue))
        {
            UnsubscribeFromManager();
            return;
        }

        hasPlayed = true;
        Triggered?.Invoke(this);
    }

    public void ResetTrigger()
    {
        hasPlayed = false;
    }

    private bool CanBeActivatedBy(Collider2D other)
    {
        if (other == null || other.attachedRigidbody == null)
        {
            return false;
        }

        if (activator == null)
        {
            return true;
        }

        Transform movingRoot = other.attachedRigidbody.transform;
        return movingRoot == activator || movingRoot.IsChildOf(activator) || activator.IsChildOf(movingRoot);
    }

    private void HandleDialogueEnded(Dialogue finishedDialogue)
    {
        if (finishedDialogue != dialogue)
        {
            return;
        }

        UnsubscribeFromManager();
        Completed?.Invoke(this);
    }

    private void UnsubscribeFromManager()
    {
        if (dialogueManager != null)
        {
            dialogueManager.DialogueEnded -= HandleDialogueEnded;
        }
    }
}

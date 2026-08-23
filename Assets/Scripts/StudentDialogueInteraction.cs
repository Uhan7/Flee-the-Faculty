using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public sealed class StudentDialogueInteraction : MonoBehaviour
{
    [SerializeField] private Dialogue dialogue;
    [SerializeField] private Transform activator;
    [SerializeField] private Button questionButton;
    [SerializeField] private GameObject questionButtonRoot;

    private Coroutine beginDialogueRoutine;
    private Collider2D interactionZone;
    private DialogueManager dialogueManager;
    private bool hasCompletedDialogue;
    private bool isActivatorInside;

    private void Reset()
    {
        interactionZone = GetComponent<Collider2D>();
        if (interactionZone != null)
        {
            interactionZone.isTrigger = true;
        }

        if (questionButton == null)
        {
            questionButton = GetComponentInChildren<Button>(true);
        }

        if (questionButtonRoot == null && questionButton != null)
        {
            questionButtonRoot = questionButton.gameObject;
        }
    }

    private void Awake()
    {
        interactionZone = GetComponent<Collider2D>();
        interactionZone.isTrigger = true;

        if (questionButtonRoot == null && questionButton != null)
        {
            questionButtonRoot = questionButton.gameObject;
        }

        SetQuestionButtonVisible(false);
    }

    private void OnEnable()
    {
        if (questionButton != null)
        {
            questionButton.onClick.AddListener(OnQuestionButtonPressed);
        }

        dialogueManager = DialogueManager.GetOrCreate();
        if (dialogueManager != null)
        {
            dialogueManager.DialogueEnded += HandleDialogueEnded;
        }

        RefreshQuestionButton();
    }

    private void OnDisable()
    {
        if (questionButton != null)
        {
            questionButton.onClick.RemoveListener(OnQuestionButtonPressed);
        }

        if (dialogueManager != null)
        {
            dialogueManager.DialogueEnded -= HandleDialogueEnded;
        }

        if (beginDialogueRoutine != null)
        {
            StopCoroutine(beginDialogueRoutine);
            beginDialogueRoutine = null;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!CanBeActivatedBy(other) || hasCompletedDialogue)
        {
            return;
        }

        isActivatorInside = true;
        RefreshQuestionButton();
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!CanBeActivatedBy(other))
        {
            return;
        }

        isActivatorInside = false;
        RefreshQuestionButton();
    }

    public void OnQuestionButtonPressed()
    {
        if (!CanStartDialogue())
        {
            return;
        }

        SetQuestionButtonVisible(false);

        if (beginDialogueRoutine != null)
        {
            StopCoroutine(beginDialogueRoutine);
        }

        beginDialogueRoutine = StartCoroutine(BeginDialogueNextFrame());
    }

    private IEnumerator BeginDialogueNextFrame()
    {
        yield return null;
        beginDialogueRoutine = null;

        if (!CanStartDialogue())
        {
            RefreshQuestionButton();
            yield break;
        }

        if (dialogueManager == null)
        {
            dialogueManager = DialogueManager.GetOrCreate();
        }

        if (dialogueManager == null || !dialogueManager.Play(dialogue))
        {
            RefreshQuestionButton();
        }
    }

    private void HandleDialogueEnded(Dialogue finishedDialogue)
    {
        if (finishedDialogue != dialogue)
        {
            return;
        }

        hasCompletedDialogue = true;
        isActivatorInside = false;
        SetQuestionButtonVisible(false);
    }

    private bool CanStartDialogue()
    {
        return dialogue != null
            && !hasCompletedDialogue
            && isActivatorInside
            && dialogueManager != null
            && !dialogueManager.IsPlaying;
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

    private void RefreshQuestionButton()
    {
        bool shouldShowButton = !hasCompletedDialogue
            && isActivatorInside
            && dialogueManager != null
            && !dialogueManager.IsPlaying;

        SetQuestionButtonVisible(shouldShowButton);
    }

    private void SetQuestionButtonVisible(bool visible)
    {
        if (questionButton != null)
        {
            questionButton.interactable = visible;
        }

        if (questionButtonRoot != null)
        {
            questionButtonRoot.SetActive(visible);
        }
    }
}

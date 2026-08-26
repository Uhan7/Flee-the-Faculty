using System;
using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public sealed class DialogueManager : MonoBehaviour
{
    private const float MinimumLineDisplaySeconds = 0.35f;
    private const float RevealSkipDebounceSeconds = 0.45f;

    [Header("Presentation")]
    [Tooltip("Optional component implementing IDialogueView. A temporary view is created when empty.")]
    [SerializeField] private MonoBehaviour viewProvider;

    [Header("Input")]
    [SerializeField] private bool advanceWithKeyboard = true;
    [SerializeField] private bool advanceWithLeftClick = true;

    [Header("Voice Ticks")]
    [Tooltip("Number of revealed letters between gibberish voice clips for every speaker.")]
    [SerializeField, Min(1)] private int lettersPerVoiceTick = 3;

    private IDialogueView view;
    private IDialogueSequence activeDialogue;
    private IDialogueLine activeLine;
    private DialogueActor activeSpeakerActor;
    private int activeLineIndex = -1;
    private int lettersSinceVoiceTick;
    private float advanceAllowedAt;

    public static DialogueManager Instance { get; private set; }
    public IDialogueSequence ActiveDialogue => activeDialogue;
    public IDialogueLine ActiveLine => activeLine;
    public bool IsPlaying => activeDialogue != null;
    public bool IsTyping => view != null && !view.IsRevealComplete;
    public int LettersPerVoiceTick => Mathf.Max(1, lettersPerVoiceTick);

    public event Action<IDialogueSequence> DialogueStarted;
    public event Action<IDialogueLine, int> LineChanged;
    public event Action<IDialogueSequence> DialogueEnded;

    public static DialogueManager GetOrCreate()
    {
        if (Instance != null)
        {
            return Instance;
        }

#if UNITY_2023_1_OR_NEWER
        DialogueManager existingManager = FindFirstObjectByType<DialogueManager>();
#else
        DialogueManager existingManager = FindObjectOfType<DialogueManager>();
#endif
        if (existingManager != null)
        {
            return existingManager;
        }

        GameObject dialogueSystem = new GameObject("Dialogue System");
        return dialogueSystem.AddComponent<DialogueManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            return;
        }

        Instance = this;
        ResolveView();
        view.SetVisible(false);
    }

    private void Update()
    {
        if (IsPlaying && WasAdvancePressed())
        {
            Advance();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public bool Play(Dialogue dialogue)
    {
        return PlayInternal(dialogue);
    }

    public bool Play(SceneDialogueConversation dialogue)
    {
        return PlayInternal(dialogue);
    }

    public bool Play(IDialogueSequence dialogue)
    {
        return PlayInternal(dialogue);
    }

    private bool PlayInternal(IDialogueSequence dialogue)
    {
        if (dialogue == null || !dialogue.HasLines)
        {
            Debug.LogWarning("Cannot play an empty dialogue.");
            return false;
        }

        if (IsPlaying)
        {
            EndDialogue();
        }

        activeDialogue = dialogue;
        activeLine = null;
        activeLineIndex = -1;
        view.SetVisible(true);
        DialogueStarted?.Invoke(activeDialogue);
        ShowNextLine();
        return true;
    }

    public void Advance()
    {
        if (!IsPlaying || Time.unscaledTime < advanceAllowedAt)
        {
            return;
        }

        if (!view.IsRevealComplete)
        {
            view.CompleteReveal();
            advanceAllowedAt = Time.unscaledTime + RevealSkipDebounceSeconds;
            return;
        }

        ShowNextLine();
    }

    public void EndDialogue()
    {
        if (!IsPlaying)
        {
            return;
        }

        IDialogueSequence finishedDialogue = activeDialogue;
        activeDialogue = null;
        activeLine = null;
        activeLineIndex = -1;
        activeSpeakerActor?.StopVoice();
        activeSpeakerActor = null;
        lettersSinceVoiceTick = 0;
        view.SetVisible(false);
        DialogueEnded?.Invoke(finishedDialogue);
    }

    public void NotifyCharacterRevealed(char revealedCharacter)
    {
        if (activeSpeakerActor == null || !char.IsLetter(revealedCharacter))
        {
            return;
        }

        lettersSinceVoiceTick++;
        if (lettersSinceVoiceTick < LettersPerVoiceTick)
        {
            return;
        }

        lettersSinceVoiceTick = 0;
        activeSpeakerActor.PlayVoiceTick();
    }

    private void ShowNextLine()
    {
        IReadOnlyList<IDialogueLine> lines = activeDialogue.Lines;
        do
        {
            activeLineIndex++;
        }
        while (activeLineIndex < lines.Count && lines[activeLineIndex] == null);

        if (activeLineIndex >= lines.Count)
        {
            EndDialogue();
            return;
        }

        activeSpeakerActor?.StopVoice();
        activeLine = lines[activeLineIndex];
        activeSpeakerActor = ResolveDialogueActor(activeLine);
        lettersSinceVoiceTick = 0;
        advanceAllowedAt = Time.unscaledTime + MinimumLineDisplaySeconds;
        LineChanged?.Invoke(activeLine, activeLineIndex);
        view.DisplayLine(activeLine, activeLine.Text, true);
    }

    private static DialogueActor ResolveDialogueActor(IDialogueLine line)
    {
        if (line == null || line.SpeakerReference == null)
        {
            return null;
        }

        if (line.SpeakerReference is DialogueActor actor)
        {
            return actor;
        }

        if (line.SpeakerReference is StudentPersonality personality)
        {
            return personality.Actor;
        }

        if (line.SpeakerReference is Component component)
        {
            return component.GetComponent<DialogueActor>();
        }

        if (line.SpeakerReference is GameObject gameObject)
        {
            return gameObject.GetComponent<DialogueActor>();
        }

        return null;
    }

    private void ResolveView()
    {
        view = viewProvider as IDialogueView;
        if (view != null)
        {
            return;
        }

        MonoBehaviour sceneViewProvider = FindSceneViewProvider();
        if (sceneViewProvider != null)
        {
            viewProvider = sceneViewProvider;
            view = (IDialogueView)sceneViewProvider;
            return;
        }

        DialogueView fallbackView = GetComponent<DialogueView>();
        if (fallbackView == null)
        {
            fallbackView = gameObject.AddComponent<DialogueView>();
        }

        viewProvider = fallbackView;
        view = fallbackView;
    }

    private MonoBehaviour FindSceneViewProvider()
    {
        MonoBehaviour dialogueViewFallback = null;

#if UNITY_2023_1_OR_NEWER
        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
#else
        MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>();
#endif
        for (int index = 0; index < behaviours.Length; index++)
        {
            MonoBehaviour behaviour = behaviours[index];
            if (behaviour == null || behaviour == this || behaviour is not IDialogueView)
            {
                continue;
            }

            if (behaviour is DialogueView)
            {
                dialogueViewFallback ??= behaviour;
                continue;
            }

            return behaviour;
        }

        return dialogueViewFallback;
    }

    private bool WasAdvancePressed()
    {
        bool pressed = false;

#if ENABLE_INPUT_SYSTEM
        if (advanceWithKeyboard && Keyboard.current != null)
        {
            pressed |= Keyboard.current.spaceKey.wasPressedThisFrame;
            pressed |= Keyboard.current.enterKey.wasPressedThisFrame;
        }

        if (advanceWithLeftClick && Mouse.current != null)
        {
            pressed |= Mouse.current.leftButton.wasPressedThisFrame;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (advanceWithKeyboard)
        {
            pressed |= Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
        }

        if (advanceWithLeftClick)
        {
            pressed |= Input.GetMouseButtonDown(0);
        }
#endif

        return pressed;
    }
}

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

    [Header("Spoken Dialogue")]
    [Tooltip("Use recorded/backend speech when available. Turn off to use only voice bleeps.")]
    [SerializeField] private bool useTextToSpeech = true;

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
    public bool CanGoBack => IsPlaying && FindPreviousLineIndex(activeLineIndex) >= 0;
    public bool HasNextLine => IsPlaying && FindNextLineIndex(activeLineIndex) >= 0;
    public bool UseTextToSpeech => useTextToSpeech;
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

    public bool PlayLastLine(IDialogueSequence dialogue)
    {
        return PlayInternal(dialogue, true);
    }

    private bool PlayInternal(IDialogueSequence dialogue, bool startAtLastLine = false)
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

        if (startAtLastLine)
        {
            int lastLineIndex = FindLastLineIndex();
            if (lastLineIndex < 0)
            {
                EndDialogue();
                return false;
            }

            ShowLine(lastLineIndex);
        }
        else
        {
            ShowNextLine();
        }

        return true;
    }

    public void Advance()
    {
        if (!IsPlaying)
        {
            return;
        }

        if (!view.IsRevealComplete)
        {
            DialogueVoicePlayer voicePlayer = DialogueVoicePlayer.Instance;
            if (voicePlayer != null && voicePlayer.IsPreparingLine(activeLine))
            {
                return;
            }

            view.CompleteReveal();
            advanceAllowedAt = Time.unscaledTime + RevealSkipDebounceSeconds;
            return;
        }

        if (Time.unscaledTime < advanceAllowedAt)
        {
            return;
        }

        ShowNextLine();
    }

    public void GoBack()
    {
        if (!IsPlaying)
        {
            return;
        }

        int previousLineIndex = FindPreviousLineIndex(activeLineIndex);
        if (previousLineIndex < 0)
        {
            return;
        }

        ShowLine(previousLineIndex);
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
        int nextLineIndex = FindNextLineIndex(activeLineIndex);
        if (nextLineIndex < 0)
        {
            EndDialogue();
            return;
        }

        ShowLine(nextLineIndex);
    }

    private void ShowLine(int lineIndex)
    {
        if (!IsPlaying || lineIndex < 0 || lineIndex >= activeDialogue.Lines.Count)
        {
            return;
        }

        activeSpeakerActor?.StopVoice();
        activeLineIndex = lineIndex;
        IReadOnlyList<IDialogueLine> lines = activeDialogue.Lines;
        activeLine = lines[activeLineIndex];
        activeSpeakerActor = ResolveDialogueActor(activeLine);
        lettersSinceVoiceTick = 0;
        advanceAllowedAt = Time.unscaledTime + MinimumLineDisplaySeconds;
        LineChanged?.Invoke(activeLine, activeLineIndex);
        view.DisplayLine(activeLine, activeLine.Text, true);
    }

    private int FindNextLineIndex(int fromLineIndex)
    {
        if (!IsPlaying)
        {
            return -1;
        }

        IReadOnlyList<IDialogueLine> lines = activeDialogue.Lines;
        for (int index = Mathf.Max(0, fromLineIndex + 1); index < lines.Count; index++)
        {
            if (lines[index] != null)
            {
                return index;
            }
        }

        return -1;
    }

    private int FindPreviousLineIndex(int fromLineIndex)
    {
        if (!IsPlaying)
        {
            return -1;
        }

        IReadOnlyList<IDialogueLine> lines = activeDialogue.Lines;
        for (int index = Mathf.Min(fromLineIndex - 1, lines.Count - 1); index >= 0; index--)
        {
            if (lines[index] != null)
            {
                return index;
            }
        }

        return -1;
    }

    private int FindLastLineIndex()
    {
        if (!IsPlaying)
        {
            return -1;
        }

        IReadOnlyList<IDialogueLine> lines = activeDialogue.Lines;
        for (int index = lines.Count - 1; index >= 0; index--)
        {
            if (lines[index] != null)
            {
                return index;
            }
        }

        return -1;
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
            pressed |= Mouse.current.leftButton.wasPressedThisFrame && !IsPointerOverControlButton();
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (advanceWithKeyboard)
        {
            pressed |= Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
        }

        if (advanceWithLeftClick)
        {
            pressed |= Input.GetMouseButtonDown(0) && !IsPointerOverControlButton();
        }
#endif

        return pressed;
    }

    private static bool IsPointerOverControlButton()
    {
        SceneDialogueView sceneView = SceneDialogueView.ActiveInstance;
        return sceneView != null && sceneView.IsPointerOverControlButton();
    }
}

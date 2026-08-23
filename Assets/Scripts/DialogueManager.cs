using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public sealed class DialogueManager : MonoBehaviour
{
    [Header("Presentation")]
    [Tooltip("Optional component implementing IDialogueView. A temporary view is created when empty.")]
    [SerializeField] private MonoBehaviour viewProvider;
    [SerializeField] private AudioSource voiceAudioSource;

    [Header("Typing")]
    [SerializeField, Min(0f)] private float defaultCharactersPerSecond = 40f;
    [SerializeField, Min(0f)] private float punctuationPause = 0.08f;

    [Header("Input")]
    [SerializeField] private bool advanceWithKeyboard = true;
    [SerializeField] private bool advanceWithLeftClick = true;

    private IDialogueView view;
    private Coroutine typingRoutine;
    private Dialogue activeDialogue;
    private DialogueLine activeLine;
    private int activeLineIndex = -1;
    private bool isTyping;

    public static DialogueManager Instance { get; private set; }
    public Dialogue ActiveDialogue => activeDialogue;
    public bool IsPlaying => activeDialogue != null;
    public bool IsTyping => isTyping;

    public event Action<Dialogue> DialogueStarted;
    public event Action<DialogueLine, int> LineChanged;
    public event Action<Dialogue> DialogueEnded;

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
        if (dialogue == null || !dialogue.HasLines)
        {
            Debug.LogWarning("Cannot play an empty dialogue.", dialogue);
            return false;
        }

        if (IsPlaying)
        {
            EndDialogue();
        }

        activeDialogue = dialogue;
        activeLineIndex = -1;
        view.SetVisible(true);
        DialogueStarted?.Invoke(activeDialogue);
        ShowNextLine();
        return true;
    }

    public void Advance()
    {
        if (!IsPlaying)
        {
            return;
        }

        if (isTyping)
        {
            RevealActiveLine();
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

        Dialogue finishedDialogue = activeDialogue;
        StopTyping();
        activeDialogue = null;
        activeLine = null;
        activeLineIndex = -1;
        view.SetVisible(false);
        DialogueEnded?.Invoke(finishedDialogue);
    }

    private void ShowNextLine()
    {
        IReadOnlyList<DialogueLine> lines = activeDialogue.Lines;
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

        activeLine = lines[activeLineIndex];
        LineChanged?.Invoke(activeLine, activeLineIndex);

        if (activeLine.VoiceClip != null)
        {
            if (voiceAudioSource == null)
            {
                voiceAudioSource = GetComponent<AudioSource>();
                if (voiceAudioSource == null)
                {
                    voiceAudioSource = gameObject.AddComponent<AudioSource>();
                }
            }

            voiceAudioSource.Stop();
            voiceAudioSource.PlayOneShot(activeLine.VoiceClip);
        }

        float charactersPerSecond = activeLine.CharactersPerSecond >= 0f
            ? activeLine.CharactersPerSecond
            : defaultCharactersPerSecond;

        if (charactersPerSecond <= 0f || string.IsNullOrEmpty(activeLine.Text))
        {
            isTyping = false;
            view.DisplayLine(activeLine, activeLine.Text, true);
            return;
        }

        StopTyping();
        typingRoutine = StartCoroutine(TypeLine(activeLine, charactersPerSecond));
    }

    private IEnumerator TypeLine(DialogueLine line, float charactersPerSecond)
    {
        isTyping = true;
        view.DisplayLine(line, string.Empty, false);

        string fullText = line.Text;
        float characterDelay = 1f / charactersPerSecond;
        for (int characterIndex = 1; characterIndex <= fullText.Length; characterIndex++)
        {
            view.DisplayLine(line, fullText.Substring(0, characterIndex), false);

            char character = fullText[characterIndex - 1];
            float delay = IsPunctuation(character)
                ? characterDelay + punctuationPause
                : characterDelay;
            yield return new WaitForSecondsRealtime(delay);
        }

        typingRoutine = null;
        isTyping = false;
        view.DisplayLine(line, fullText, true);
    }

    private void RevealActiveLine()
    {
        StopTyping();
        view.DisplayLine(activeLine, activeLine.Text, true);
    }

    private void StopTyping()
    {
        if (typingRoutine != null)
        {
            StopCoroutine(typingRoutine);
            typingRoutine = null;
        }

        isTyping = false;
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

    private static bool IsPunctuation(char character)
    {
        return character == '.' || character == ',' || character == '!' || character == '?' || character == ';' || character == ':';
    }
}

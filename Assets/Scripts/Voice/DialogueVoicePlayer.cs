using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Speaks the line the DialogueManager is showing, in the speaker's own voice.
///
/// The subscription pattern is the one <c>DialogueSpeakerMouth</c> already uses:
/// listen to <c>LineChanged</c> and <c>DialogueEnded</c>, and decide from the
/// line's speaker. Nothing in the dialogue runner knows this component exists,
/// so a scene without it plays silently and behaves exactly as before.
///
/// Clips come from a <see cref="VoiceClipLibrary"/> baked ahead of time by
/// <c>Tools/voicelab</c>. Lines the model writes at run time cannot be baked, so
/// ADR-0011 has the service render those and send them with the text; when that
/// lands, <see cref="Speak"/> is the one method that grows a download step, and
/// it is already a coroutine so that it can.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-40)]
public sealed class DialogueVoicePlayer : MonoBehaviour
{
    /// <summary>
    /// Where the library is looked up when the field is empty. A scene needs no
    /// wiring at all if the asset sits at <c>Assets/Resources/Voice Clip
    /// Library.asset</c>, which is where the editor rebuild puts it.
    /// </summary>
    public const string DefaultLibraryResourcePath = "Voice Clip Library";

    [Header("Clips")]
    [Tooltip("Leave empty to load 'Voice Clip Library' from Resources.")]
    [SerializeField] private VoiceClipLibrary library;

    [Header("Playback")]
    [SerializeField] private AudioSource source;
    [SerializeField, Range(0f, 1f)] private float volume = 1f;

    [Tooltip("Stop the current line when the Learner clicks through to the next one.")]
    [SerializeField] private bool interruptOnAdvance = true;

    [Header("Diagnostics")]
    [Tooltip("Log lines that have a voice but no baked clip. Turn off once every line is baked.")]
    [SerializeField] private bool logMissingClips = true;

    private DialogueManager dialogueManager;
    private Coroutine speaking;

    public static DialogueVoicePlayer Instance { get; private set; }

    /// <summary>The speaker whose clip is playing right now, or null.</summary>
    public UnityEngine.Object SpeakingActor { get; private set; }

    /// <summary>The line being spoken right now, or null.</summary>
    public IDialogueLine SpeakingLine { get; private set; }

    /// <summary>
    /// Create the player once a scene is up, and again after every scene load.
    ///
    /// A scene nobody has wired still gets voices, which the classroom relies on:
    /// there is no Dialogue System object saved in it, and DialogueManager
    /// bootstraps itself the same way.
    ///
    /// The per-scene part is not optional. `DoorSceneTransition` loads with
    /// `LoadSceneMode.Single`, which destroys this object along with the rest of
    /// the scene, and `RuntimeInitializeOnLoadMethod` runs once per play session
    /// rather than once per scene. Without the `sceneLoaded` hook, voices work
    /// when you press play inside the classroom and go silent when you walk into
    /// it from the main menu.
    ///
    /// Recreating rather than surviving is deliberate. DialogueManager is
    /// per-scene, so a player that outlived the scene would hold a subscription
    /// to a destroyed manager and never hear another line.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Resources.Load<VoiceClipLibrary>(DefaultLibraryResourcePath) == null)
        {
            Debug.LogWarning(
                $"No voice library at Resources/{DefaultLibraryResourcePath}, so Pupils "
                + "will not speak. Run Flee the Faculty > Voices > Rebuild Voice Library.");
            return;
        }

        // Subtracting first keeps this to one subscription when the editor is
        // set to skip the domain reload and these statics survive play mode.
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        GetOrCreate();
    }

    private static void HandleSceneLoaded(Scene _, LoadSceneMode __)
    {
        GetOrCreate();
    }

    public static DialogueVoicePlayer GetOrCreate()
    {
        if (Instance != null)
        {
            return Instance;
        }

#if UNITY_2023_1_OR_NEWER
        DialogueVoicePlayer existing = FindFirstObjectByType<DialogueVoicePlayer>();
#else
        DialogueVoicePlayer existing = FindObjectOfType<DialogueVoicePlayer>();
#endif
        if (existing != null)
        {
            return existing;
        }

        GameObject host = new GameObject("Dialogue Voice Player");
        return host.AddComponent<DialogueVoicePlayer>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            return;
        }

        Instance = this;

        if (library == null)
        {
            library = Resources.Load<VoiceClipLibrary>(DefaultLibraryResourcePath);
        }

        if (source == null)
        {
            source = GetComponent<AudioSource>();
        }

        if (source == null)
        {
            source = gameObject.AddComponent<AudioSource>();
        }

        source.playOnAwake = false;
        source.loop = false;
        // A Pupil speaks to the Learner, not from a desk. Panning a voice by
        // where its sprite sits would move it every time the camera does.
        source.spatialBlend = 0f;
    }

    private void OnEnable()
    {
        dialogueManager = DialogueManager.GetOrCreate();
        if (dialogueManager == null)
        {
            return;
        }

        dialogueManager.LineChanged += HandleLineChanged;
        dialogueManager.DialogueEnded += HandleDialogueEnded;

        if (dialogueManager.IsPlaying && dialogueManager.ActiveLine != null)
        {
            HandleLineChanged(dialogueManager.ActiveLine, -1);
        }
    }

    private void OnDisable()
    {
        if (dialogueManager != null)
        {
            dialogueManager.LineChanged -= HandleLineChanged;
            dialogueManager.DialogueEnded -= HandleDialogueEnded;
        }

        Stop();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>True while this speaker's own clip is playing.</summary>
    public bool IsSpeaking(UnityEngine.Object speaker)
    {
        return speaker != null && SpeakingActor == speaker;
    }

    /// <summary>Stop whatever is playing and tell anyone listening.</summary>
    public void Stop()
    {
        if (speaking != null)
        {
            StopCoroutine(speaking);
            speaking = null;
        }

        if (source != null && source.isPlaying)
        {
            source.Stop();
        }

        SpeakingActor = null;
        SpeakingLine = null;
    }

    private void HandleLineChanged(IDialogueLine line, int _)
    {
        if (interruptOnAdvance || SpeakingActor != null)
        {
            Stop();
        }

        if (line == null || !isActiveAndEnabled)
        {
            return;
        }

        VoiceId voice = VoiceCatalog.VoiceOf(line.SpeakerReference);
        if (voice == VoiceId.None)
        {
            return;
        }

        speaking = StartCoroutine(Speak(line, voice));
    }

    private void HandleDialogueEnded(IDialogueSequence _)
    {
        Stop();
    }

    /// <summary>
    /// Find this line's audio and play it.
    ///
    /// A coroutine because the clip does not always exist yet. Today the only
    /// source is the baked library and the lookup returns immediately; the
    /// service path from ADR-0011 adds a wait here for
    /// <c>UnityWebRequestMultimedia.GetAudioClip</c> and changes nothing else.
    /// </summary>
    private IEnumerator Speak(IDialogueLine line, VoiceId voice)
    {
        AudioClip clip = library != null ? library.Find(voice, line.Text) : null;

        if (clip == null)
        {
            if (logMissingClips)
            {
                // Editing a line changes its fingerprint, so the clip that was
                // baked for the old wording stops matching and the Pupil falls
                // back to the syllable ticks. That is the common case here, and
                // it is quiet unless this says so.
                Debug.LogWarning(
                    $"No {VoiceCatalog.ToKey(voice)} clip for \"{Preview(line.Text)}\", "
                    + $"so this line falls back to voice ticks. Key "
                    + $"{VoiceKey.For(voice, line.Text)}. Re-run Flee the Faculty > "
                    + "Voices > Export Dialogue Lines, then voicelab bake-lines, then "
                    + "Rebuild Voice Library.",
                    this);
            }

            speaking = null;
            yield break;
        }

        SpeakingActor = line.SpeakerReference;
        SpeakingLine = line;
        source.clip = clip;
        source.volume = volume;
        source.Play();

        while (source.isPlaying)
        {
            yield return null;
        }

        speaking = null;
        SpeakingActor = null;
        SpeakingLine = null;
    }

    private static string Preview(string text)
    {
        string normalised = VoiceKey.Normalise(text);
        return normalised.Length <= 40 ? normalised : normalised.Substring(0, 40) + "...";
    }
}

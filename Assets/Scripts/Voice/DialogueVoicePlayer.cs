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
/// Clips come from two places. Authored dialogue is baked ahead of time into a
/// <see cref="VoiceClipLibrary"/> and answers on the frame it is asked for.
/// Lines the model writes at turn time cannot be baked by anyone, so
/// <see cref="ServiceVoiceSynthesizer"/> fetches those from the service; this
/// player starts them as soon as a conversation opens rather than as each line
/// comes up, so only the first line of a reply ever waits.
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

    [Tooltip("Longest the text and syllable ticks wait for fetched speech. Past this, the "
        + "text starts with ticks and late audio is not played over it.")]
    [SerializeField, Min(0f)] private float tickHoldSeconds = 12f;

    [Header("Diagnostics")]
    [Tooltip("Log lines that have a voice but no baked clip. Turn off once every line is baked.")]
    [SerializeField] private bool logMissingClips = true;

    private DialogueManager dialogueManager;
    private Coroutine speaking;
    private UnityEngine.Object pendingActor;
    private IDialogueLine pendingLine;
    private float pendingUntil;

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
        // A missing library costs the authored lines their recordings, and
        // nothing else. Everything a Pupil says during an Encounter is written
        // at turn time and synthesised, so the player still has work to do.
        if (Resources.Load<VoiceClipLibrary>(DefaultLibraryResourcePath) == null)
        {
            Debug.LogWarning(
                $"No voice library at Resources/{DefaultLibraryResourcePath}, so authored "
                + "lines fall back to ticks. Run Flee the Faculty > Voices > Rebuild Voice "
                + "Library. Lines written during an Encounter are unaffected.");
        }

        // Subtracting first keeps this to one subscription when the editor is
        // set to skip the domain reload and these statics survive play mode.
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        GetOrCreate();
    }

    /// <summary>
    /// Make sure the synthesiser exists, so the service is woken at the first
    /// scene rather than at the first unbaked line.
    ///
    /// It gets its own object and outlives every scene, so this asks for the
    /// session's one instance rather than adding a component here. Adding it
    /// here is what broke voices for anyone who walked in from the main menu:
    /// this player is rebuilt per scene, and the synthesiser was being rebuilt
    /// with it, losing the readiness its health call establishes only once.
    /// </summary>
    private void EnsureSynthesizer()
    {
        ServiceVoiceSynthesizer.GetOrCreate();
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

        if (dialogueManager.UseTextToSpeech)
        {
            EnsureSynthesizer();
        }

        dialogueManager.DialogueStarted += HandlePrefetch;
        dialogueManager.LineChanged += HandleLineChanged;
        dialogueManager.DialogueEnded += HandleDialogueEnded;

        if (dialogueManager.IsPlaying && dialogueManager.ActiveLine != null)
        {
            HandleLineChanged(dialogueManager.ActiveLine, -1);
        }
    }

    private void Update()
    {
        if (dialogueManager != null
            && !dialogueManager.UseTextToSpeech
            && (speaking != null || SpeakingActor != null || pendingLine != null))
        {
            Stop();
        }
    }

    private void OnDisable()
    {
        if (dialogueManager != null)
        {
            dialogueManager.DialogueStarted -= HandlePrefetch;
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
        return dialogueManager != null
            && dialogueManager.UseTextToSpeech
            && speaker != null
            && SpeakingActor == speaker;
    }

    /// <summary>
    /// True while this speaker's line is playing or about to.
    ///
    /// The syllable ticks use this rather than <see cref="IsSpeaking"/>. A line
    /// the model wrote takes a round trip to arrive, and without the wider check
    /// the ticks fill that round trip and are still going when the real voice
    /// starts underneath them.
    ///
    /// The wait is capped, so a line that never arrives gets its ticks back
    /// instead of playing out in silence.
    /// </summary>
    public bool IsVoicing(UnityEngine.Object speaker)
    {
        if (dialogueManager == null || !dialogueManager.UseTextToSpeech || speaker == null)
        {
            return false;
        }

        return SpeakingActor == speaker
            || (pendingActor == speaker && Time.unscaledTime < pendingUntil);
    }

    /// <summary>
    /// True while this exact line is waiting for service audio.
    ///
    /// The dialogue view uses this to keep its first letter hidden until the
    /// voice starts. The wait is bounded by <see cref="tickHoldSeconds"/>, so a
    /// service hiccup never leaves an empty speech bubble on screen.
    /// </summary>
    public bool IsPreparingLine(IDialogueLine line)
    {
        return dialogueManager != null
            && dialogueManager.UseTextToSpeech
            && line != null
            && ReferenceEquals(pendingLine, line);
    }

    /// <summary>
    /// Start making every line of this conversation that is not already baked.
    ///
    /// A Pupil's reply is a restatement and a follow-up, delivered together, so
    /// the follow-up is fetched while the restatement is still playing and costs
    /// the Learner no wait at all. Only the first line of a reply is ever
    /// exposed, which is the one latency GDD 16.2 cares about.
    /// </summary>
    private void HandlePrefetch(IDialogueSequence sequence)
    {
        if (dialogueManager == null || !dialogueManager.UseTextToSpeech)
        {
            return;
        }

        EnsureSynthesizer();
        ServiceVoiceSynthesizer synthesizer = ServiceVoiceSynthesizer.Instance;
        if (sequence == null || !sequence.HasLines || synthesizer == null || !synthesizer.IsReady)
        {
            return;
        }

        for (int index = 0; index < sequence.Lines.Count; index++)
        {
            IDialogueLine line = sequence.Lines[index];
            if (line == null || string.IsNullOrWhiteSpace(line.Text))
            {
                continue;
            }

            VoiceId voice = VoiceCatalog.VoiceOf(line.SpeakerReference);
            if (voice == VoiceId.None)
            {
                continue;
            }

            if (library != null && library.Find(voice, line.Text) != null)
            {
                continue;
            }

            synthesizer.Prepare(VoiceCatalog.SlotOf(line.SpeakerReference), voice, line.Text);
        }
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
        pendingActor = null;
        pendingLine = null;
    }

    private void HandleLineChanged(IDialogueLine line, int _)
    {
        if (interruptOnAdvance || SpeakingActor != null)
        {
            Stop();
        }

        if (line == null
            || !isActiveAndEnabled
            || dialogueManager == null
            || !dialogueManager.UseTextToSpeech)
        {
            return;
        }

        EnsureSynthesizer();
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

        // A conversation the Learner walked out of may have left lines queued.
        // Dropping them keeps the next Pupil's first line from waiting behind
        // audio nobody will hear.
        if (ServiceVoiceSynthesizer.Instance != null)
        {
            ServiceVoiceSynthesizer.Instance.CancelPending();
        }
    }

    /// <summary>
    /// Find this line's audio and play it.
    ///
    /// Two sources, in order. The baked library holds authored dialogue and
    /// answers instantly. Anything else was written by the model at turn time
    /// and has to be fetched, which is one request to the service, or nothing at
    /// all when <see cref="HandlePrefetch"/> already started it.
    ///
    /// A line neither can supply plays silent, and the syllable ticks in
    /// <c>DialogueActor</c> carry it instead.
    /// </summary>
    private IEnumerator Speak(IDialogueLine line, VoiceId voice)
    {
        AudioClip clip = library != null ? library.Find(voice, line.Text) : null;

        if (clip == null)
        {
            ServiceVoiceSynthesizer synthesizer = ServiceVoiceSynthesizer.Instance;
            if (synthesizer != null)
            {
                pendingActor = line.SpeakerReference;
                pendingLine = line;
                float deadline = Time.unscaledTime + tickHoldSeconds;
                pendingUntil = deadline;

                AudioClip synthesized = null;
                while (!synthesizer.IsReady && Time.unscaledTime < deadline)
                {
                    yield return null;
                }

                if (synthesizer.IsReady)
                {
                    float remainingWait = Mathf.Max(0f, deadline - Time.unscaledTime);
                    yield return synthesizer.Speak(
                        VoiceCatalog.SlotOf(line.SpeakerReference),
                        voice,
                        line.Text,
                        remainingWait,
                        result => synthesized = result);
                }

                clip = synthesized;
                pendingActor = null;
                pendingLine = null;
            }
        }

        if (clip == null)
        {
            if (logMissingClips)
            {
                // Two ways to get here. An authored line whose text changed no
                // longer matches its baked clip, which is fixed by rebaking. A
                // model-written line has no clip by design and needs the
                // synthesiser, which is still downloading or unavailable.
                bool canSynthesize = ServiceVoiceSynthesizer.Instance != null
                    && ServiceVoiceSynthesizer.Instance.IsReady;
                Debug.LogWarning(
                    $"No {VoiceCatalog.ToKey(voice)} audio for \"{Preview(line.Text)}\", "
                    + "so this line falls back to voice ticks. "
                    + (canSynthesize
                        ? $"Speech was not ready within {tickHoldSeconds:0.0}s."
                        : "The service has not answered its health call yet.")
                    + $" Baked key would be {VoiceKey.For(voice, line.Text)}.",
                    this);
            }

            speaking = null;
            yield break;
        }

        SpeakingActor = line.SpeakerReference;
        SpeakingLine = line;
        source.clip = clip;
        source.volume = volume * AudioSettingsStore.SfxVolume;
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

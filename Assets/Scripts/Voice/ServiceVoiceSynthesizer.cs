using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Speaks a line that was never baked, by asking the service for it.
///
/// Baked clips cover authored dialogue. Everything a Pupil says in a real
/// Encounter is written by the model at turn time, so it cannot be baked by
/// anyone, on any machine. This is the other half: one call to
/// <c>POST /v1/speech</c> per line, answered with a WAV.
///
/// This replaced a WebAssembly build of Pocket TTS that ran the same job in the
/// browser. ADR-0013 in the service repository has the whole argument; the two
/// numbers that decided it are that the browser runtime took 1.50 seconds for a
/// five-second line <em>on a development MacBook</em> with no way to compensate
/// on a slower one, and that it ran an 8-bit quantised model because the
/// full-precision weights were too large to hand a player. The service has
/// neither constraint: synthesis there is about 0.1 seconds and the same
/// everywhere.
///
/// <h3>Everything here is still fail-soft</h3>
///
/// Before <c>/health</c> answers, after any error, and outside a build with a
/// service to talk to, <see cref="IsReady"/> stays false and the caller gets no
/// clip. A line with no clip falls back to the syllable ticks in
/// <c>DialogueActor</c>, so the game is playable throughout. That contract is
/// unchanged from the runtime this replaced, and every caller already honours it.
/// </summary>
[DisallowMultipleComponent]
public sealed class ServiceVoiceSynthesizer : MonoBehaviour
{
    /// <summary>
    /// How many finished clips to hold on to.
    ///
    /// Enough that a Pupil re-reading a line, or repeating one across turns,
    /// answers instantly and costs no second request. A clip is a few seconds of
    /// mono PCM, so the whole cache is a couple of megabytes, and the references
    /// are simply dropped when it fills.
    ///
    /// The service caches rendered lines too. This one saves the round trip; that
    /// one saves the render, and saves it across every player rather than per
    /// machine.
    /// </summary>
    private const int MaxCachedClips = 24;

    [Tooltip("Seconds to wait for one line before giving up on it and letting the ticks carry "
        + "it. Synthesis is about 0.1s and a cold instance is the rest.")]
    [SerializeField, Min(1f)] private float timeoutSeconds = 30f;

    [Tooltip("Tries at the health call before the game accepts that it will be quiet. Each one "
        + "may be paying a cold start, which measured 19.3 seconds.")]
    [SerializeField, Min(1)] private int healthAttempts = 3;

    [SerializeField] private bool logProgress = true;

    /// <summary>One line's trip through the service, shared by everyone who asks for it.</summary>
    private sealed class Job
    {
        public bool IsDone;
        public bool Cancelled;
        public AudioClip Clip;
    }

    /// <summary>Jobs by voice key, so the same line is never fetched twice.</summary>
    private readonly Dictionary<string, Job> jobs = new Dictionary<string, Job>();

    /// <summary>Finished keys, oldest first, so the cache can be trimmed in order.</summary>
    private readonly List<string> finishedKeys = new List<string>();

    private bool started;

    public static ServiceVoiceSynthesizer Instance { get; private set; }

    /// <summary>True once the service has answered and said it has voices loaded.</summary>
    public bool IsReady { get; private set; }

    /// <summary>
    /// Find the one synthesizer for this session, creating it if it is the first
    /// ask.
    ///
    /// It gets its own object rather than sharing the player's, because the two
    /// have opposite lifetimes: the player is per-scene and this is not. A
    /// per-scene component looked harmless and was not, before: walking from the
    /// main menu into the Classroom destroyed the one that knew the voices were
    /// ready and built a fresh one that did not, and every Pupil fell back to
    /// ticks, only when reached the way a player reaches them.
    /// </summary>
    public static ServiceVoiceSynthesizer GetOrCreate()
    {
        if (Instance != null)
        {
            return Instance;
        }

#if UNITY_2023_1_OR_NEWER
        ServiceVoiceSynthesizer existing = FindFirstObjectByType<ServiceVoiceSynthesizer>();
#else
        ServiceVoiceSynthesizer existing = FindObjectOfType<ServiceVoiceSynthesizer>();
#endif
        if (existing != null)
        {
            return existing;
        }

        return new GameObject("Voice Synthesizer").AddComponent<ServiceVoiceSynthesizer>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (transform.parent == null)
        {
            DontDestroyOnLoad(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnEnable()
    {
        Begin();
    }

    /// <summary>
    /// Wake the service up and find out whether it can speak. Safe to call more
    /// than once.
    ///
    /// Worth calling from the main menu, and that is the point of it. A first
    /// request to a scaled-to-zero Cloud Run instance measured 19.3 seconds
    /// against 0.3 warm, and loading the two voices is about 0.8 seconds on top.
    /// Paying that while she reads the menu is the whole reason this exists as a
    /// separate step rather than as part of the first line.
    /// </summary>
    public void Begin()
    {
        if (started)
        {
            return;
        }

        started = true;
        StartCoroutine(Warm());
    }

    private IEnumerator Warm()
    {
        FleeApiClient api = FleeApiClient.GetOrCreate();
        for (int attempt = 1; attempt <= Mathf.Max(1, healthAttempts); attempt++)
        {
            bool answered = false;
            bool hasVoices = false;
            yield return api.CheckHealth(
                voices =>
                {
                    answered = true;
                    hasVoices = voices != null && voices.Length > 0;
                },
                _ => answered = false);

            if (answered && hasVoices)
            {
                IsReady = true;
                if (logProgress)
                {
                    Debug.Log("The service has its voices. Unbaked lines will be spoken.", this);
                }

                yield break;
            }

            if (answered)
            {
                // The service is up and has no voices, which is a deployment
                // fact rather than a slow one. Retrying cannot change it.
                Debug.LogWarning(
                    "The service is up but loaded no voices, so every Pupil will use "
                    + "syllable ticks. Check SPEECH_ENABLED and the voices directory "
                    + "on the service.",
                    this);
                yield break;
            }

            if (attempt < healthAttempts)
            {
                yield return new WaitForSecondsRealtime(2f);
            }
        }

        Debug.LogWarning(
            $"The service did not answer {healthAttempts} health calls, so Pupils will "
            + "use syllable ticks until something else reaches it.",
            this);
    }

    /// <summary>
    /// Start fetching this line now, without waiting for it.
    ///
    /// Call it for the lines a Pupil is about to say. A reply is a restatement
    /// and a follow-up delivered together, and the follow-up is finished long
    /// before the restatement has played out, so only the first line of a reply
    /// ever costs the Learner a wait.
    /// </summary>
    public void Prepare(VoiceId voice, string text)
    {
        RequestJob(VoiceCatalog.ToKey(voice), voice, text);
    }

    /// <summary>Start fetching this line in one of the six slots. GDD 8.3.</summary>
    public void Prepare(string slot, VoiceId voice, string text)
    {
        RequestJob(slot, voice, text);
    }

    /// <summary>
    /// Fetch one line and hand back the clip.
    ///
    /// Yields until the audio is ready or the line is given up on. The result is
    /// null on any failure, which the caller should treat as "no voice for this
    /// line" rather than as an error worth stopping for.
    /// </summary>
    public IEnumerator Speak(VoiceId voice, string text, Action<AudioClip> onDone)
    {
        yield return Speak(VoiceCatalog.ToKey(voice), voice, text, onDone);
    }

    /// <summary>Fetch one line in one of the six slots and hand back the clip.</summary>
    public IEnumerator Speak(string slot, VoiceId voice, string text, Action<AudioClip> onDone)
    {
        Job job = RequestJob(slot, voice, text);
        if (job == null)
        {
            onDone?.Invoke(null);
            yield break;
        }

        while (!job.IsDone)
        {
            yield return null;
        }

        onDone?.Invoke(job.Clip);
    }

    /// <summary>
    /// Give up on any line still in flight.
    ///
    /// Called when a conversation ends, so a reply nobody will hear does not
    /// delay the next Pupil's first line. The request itself is left to finish;
    /// it costs one response body nobody reads, and the service has already done
    /// the work by the time this is called.
    /// </summary>
    public void CancelPending()
    {
        foreach (KeyValuePair<string, Job> entry in jobs)
        {
            if (!entry.Value.IsDone)
            {
                entry.Value.Cancelled = true;
            }
        }
    }

    /// <summary>
    /// Find the job for this line, starting one if nobody has asked yet.
    ///
    /// Sharing by key is what makes <see cref="Prepare"/> worth calling: a later
    /// <see cref="Speak"/> for the same text joins the request already in flight
    /// instead of making a second one.
    ///
    /// The key carries the slot, not the base voice, because V1 and V2 are two
    /// different renders of the same words and caching them together would give
    /// one Pupil another Pupil's voice.
    /// </summary>
    private Job RequestJob(string slot, VoiceId voice, string text)
    {
        string safeSlot = string.IsNullOrWhiteSpace(slot)
            ? VoiceCatalog.ToKey(voice)
            : slot.Trim();
        string fingerprint = VoiceKey.For(voice, text);
        if (!IsReady || string.IsNullOrEmpty(safeSlot) || string.IsNullOrEmpty(fingerprint))
        {
            return null;
        }

        string key = string.Concat(safeSlot, "|", fingerprint);
        if (jobs.TryGetValue(key, out Job existing))
        {
            return existing;
        }

        Job job = new Job();
        jobs[key] = job;
        StartCoroutine(Run(job, key, safeSlot, text));
        return job;
    }

    private IEnumerator Run(Job job, string key, string slot, string text)
    {
        float askedAt = Time.unscaledTime;
        AudioClip clip = null;
        FleeApiFailure failure = null;

        yield return FleeApiClient.GetOrCreate().SpeakLine(
            slot,
            text,
            Mathf.Max(1f, timeoutSeconds),
            result => clip = result,
            error => failure = error);

        job.Clip = job.Cancelled ? null : clip;
        job.IsDone = true;

        if (job.Clip != null)
        {
            if (logProgress)
            {
                Debug.Log(
                    $"Spoke a {slot} line of {job.Clip.length:0.0}s in "
                    + $"{Time.unscaledTime - askedAt:0.00}s.",
                    this);
            }

            Remember(key);
            yield break;
        }

        if (failure != null && !job.Cancelled)
        {
            // Not an error for the game: the ticks carry any line this cannot
            // speak. A 429 is the one worth seeing, because it means the Learner
            // is about to hear a silent Classroom for a minute.
            Debug.LogWarning(
                $"Could not speak one {slot} line ({failure.StatusCode}): {failure.Message}",
                this);
        }

        // Forget a line that came back with nothing, so asking again tries again.
        // A cached failure would silence that line for the whole session, and the
        // usual cause is a cancel or a hiccup rather than anything about the text.
        jobs.Remove(key);
    }

    /// <summary>
    /// Keep this line's clip, and drop the oldest once the cache is full.
    ///
    /// Dropping the reference is the whole of it. The clip is a managed object
    /// rather than a loaded asset, so it goes when nothing holds it, and
    /// destroying it here could pull it out from under an AudioSource that is
    /// still playing it.
    /// </summary>
    private void Remember(string key)
    {
        finishedKeys.Remove(key);
        finishedKeys.Add(key);

        while (finishedKeys.Count > MaxCachedClips)
        {
            jobs.Remove(finishedKeys[0]);
            finishedKeys.RemoveAt(0);
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Speaks a line that was never baked, using the cloned voices in the browser.
///
/// Baked clips cover authored dialogue. Everything a Pupil says in a real
/// Encounter is written by the model at turn time, so it cannot be baked by
/// anyone, on any machine. This is the other half: the same two voices, in a
/// WebAssembly runtime, generating whatever text arrives.
///
/// Measured in that runtime on a 107-character line, single-threaded:
/// 5.04 seconds of audio in 1.50 seconds, with first audio at 0.15 seconds.
/// The model is 146MB and is fetched once, then cached by the browser.
///
/// Lines are worked one at a time, because the runtime holds a single model
/// whose state each generation resets. <see cref="Prepare"/> exists so the rest
/// of a Pupil's reply is made while its first line is playing: a Pupil answers
/// with a restatement and a follow-up, and only the restatement should ever
/// cost the caller a wait.
///
/// Everything here is fail-soft. Outside WebGL, before the model has finished
/// downloading, or after any error, <see cref="IsReady"/> stays false and the
/// caller gets no clip. A line with no clip falls back to the syllable ticks in
/// <c>DialogueActor</c>, so the game is playable throughout.
/// </summary>
[DisallowMultipleComponent]
public sealed class BrowserVoiceSynthesizer : MonoBehaviour
{
    /// <summary>
    /// How many finished clips to hold on to.
    ///
    /// Enough that a Pupil re-reading a line, or repeating one across turns,
    /// answers instantly. A clip is a few seconds of 24kHz mono, so the whole
    /// cache is under 2MB and the references are simply dropped when it fills.
    /// </summary>
    private const int MaxCachedClips = 24;

    /// <summary>
    /// Where the weights come from when the build has its own copy.
    ///
    /// The file is 146MB, which GitHub rejects, so it is not in the project. It
    /// does not have to be: <c>VoiceModelPostBuild</c> copies it into the build
    /// output after every WebGL build, and the build output is not the
    /// repository. Loading it from the game's own origin means no CORS and no
    /// dependence on anyone else's hosting.
    /// </summary>
    [SerializeField] private string modelFileName = "model.gguf";

    [SerializeField] private string tokenizerFileName = "tokenizer.model";

    /// <summary>
    /// Used only when the build has no copy of its own, which happens when
    /// somebody builds without running the model download first.
    /// </summary>
    [SerializeField]
    private string fallbackModelUrl =
        "https://huggingface.co/lmz/pocket-tts-without-voice-cloning-q8/resolve/main/tts_b6369a24.gguf";

    [SerializeField]
    private string fallbackTokenizerUrl =
        "https://huggingface.co/kyutai/pocket-tts-without-voice-cloning/resolve/main/tokenizer.model";

    [Tooltip("Seconds to wait for one line, counted from when the runtime starts it rather "
        + "than from when it was asked for, so a queue behind it does not count against it.")]
    [SerializeField, Min(1f)] private float timeoutSeconds = 20f;

    [Tooltip("Seconds a line may sit in the queue before it is given up on.")]
    [SerializeField, Min(1f)] private float queueTimeoutSeconds = 45f;

    [SerializeField] private bool logProgress = true;

    /// <summary>One line's trip through the runtime, shared by everyone who asks for it.</summary>
    private sealed class Job
    {
        public string RequestId;
        public bool IsDone;
        public AudioClip Clip;
    }

    /// <summary>Jobs by voice key, so the same line is never generated twice.</summary>
    private readonly Dictionary<string, Job> jobs = new Dictionary<string, Job>();

    /// <summary>Finished keys, oldest first, so the cache can be trimmed in order.</summary>
    private readonly List<string> finishedKeys = new List<string>();

    private readonly Dictionary<string, AudioClip> readyClips =
        new Dictionary<string, AudioClip>();
    private readonly HashSet<string> failedRequests = new HashSet<string>();
    private readonly HashSet<string> startedRequests = new HashSet<string>();
    private int nextRequestId;
    private bool started;

    public static BrowserVoiceSynthesizer Instance { get; private set; }

    /// <summary>True once the model and both voices have finished loading.</summary>
    public bool IsReady { get; private set; }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void VoiceSynthesis_Begin(string targetName, string configJson);

    [DllImport("__Internal")]
    private static extern int VoiceSynthesis_IsReady();

    [DllImport("__Internal")]
    private static extern void VoiceSynthesis_Speak(string requestId, string voice, string text);

    [DllImport("__Internal")]
    private static extern void VoiceSynthesis_Cancel(string requestId);

    [DllImport("__Internal")]
    private static extern int VoiceSynthesis_ReadSamples(
        string requestId, float[] destination, int capacity);
#endif

    /// <summary>
    /// Find the one synthesizer for this session, creating it if it is the
    /// first ask.
    ///
    /// It gets its own object rather than sharing the player's, because the two
    /// have opposite lifetimes: the player is per-scene and this is not.
    /// </summary>
    public static BrowserVoiceSynthesizer GetOrCreate()
    {
        if (Instance != null)
        {
            return Instance;
        }

#if UNITY_2023_1_OR_NEWER
        BrowserVoiceSynthesizer existing = FindFirstObjectByType<BrowserVoiceSynthesizer>();
#else
        BrowserVoiceSynthesizer existing = FindObjectOfType<BrowserVoiceSynthesizer>();
#endif
        if (existing != null)
        {
            return existing;
        }

        return new GameObject("Voice Synthesizer").AddComponent<BrowserVoiceSynthesizer>();
    }

    /// <summary>
    /// Live for the whole session, not for one scene.
    ///
    /// What this wraps is a Web Worker holding 146MB of weights, which belongs
    /// to the page rather than to any scene. A per-scene component looked
    /// harmless and was not: walking from the main menu into the Classroom
    /// destroyed the one that had been told the model was ready and built a
    /// fresh one that never would be, because the bridge only announces
    /// readiness when it loads. Every Pupil then fell back to the ticks, and
    /// only when reached the way a player reaches them.
    /// </summary>
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
    /// Start downloading the model and voices. Safe to call more than once.
    ///
    /// Worth calling from the main menu rather than from the classroom: the
    /// download is 146MB on a first visit and nothing else waits on it.
    /// </summary>
    public void Begin()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (started)
        {
            return;
        }

        started = true;
        string root = Application.streamingAssetsPath + "/Voices";
        string config = "{"
            + "\"workerUrl\":\"" + root + "/runtime/voice-worker.js\","
            + "\"voicesBase\":\"" + root + "\","
            + "\"voices\":[\"" + VoiceCatalog.GirlKey + "\",\"" + VoiceCatalog.BoyKey + "\"],"
            + "\"modelUrl\":\"" + root + "/runtime/" + modelFileName + "\","
            + "\"tokenizerUrl\":\"" + root + "/runtime/" + tokenizerFileName + "\","
            + "\"fallbackModelUrl\":\"" + fallbackModelUrl + "\","
            + "\"fallbackTokenizerUrl\":\"" + fallbackTokenizerUrl + "\","
            + "\"quant\":\"q8\"}";

        VoiceSynthesis_Begin(gameObject.name, config);
#else
        started = true;
#endif
    }

    /// <summary>
    /// Start making this line now, without waiting for it.
    ///
    /// Call it for the lines a Pupil is about to say. Generation is about a
    /// third of real time, so a reply's second line is finished well before the
    /// first has played out, and <see cref="Speak"/> then answers immediately.
    /// </summary>
    public void Prepare(VoiceId voice, string text)
    {
        RequestJob(voice, text);
    }

    /// <summary>
    /// Synthesize one line and hand back the clip.
    ///
    /// Yields until the audio is ready or the line is given up on. The result
    /// is null on any failure, which the caller should treat as "no voice for
    /// this line" rather than as an error worth stopping for.
    /// </summary>
    public IEnumerator Speak(VoiceId voice, string text, Action<AudioClip> onDone)
    {
        Job job = RequestJob(voice, text);
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
    /// Give up on any line that has not started yet.
    ///
    /// Called when a conversation ends, so a reply nobody will hear does not
    /// delay the next Pupil's first line. The line already in the runtime is
    /// left alone: generation is a synchronous loop with no way in.
    /// </summary>
    public void CancelPending()
    {
        foreach (KeyValuePair<string, Job> entry in jobs)
        {
            Job job = entry.Value;
            if (job.IsDone || job.RequestId == null || startedRequests.Contains(job.RequestId))
            {
                continue;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            VoiceSynthesis_Cancel(job.RequestId);
#endif
            failedRequests.Add(job.RequestId);
        }
    }

    /// <summary>
    /// Find the job for this line, starting one if nobody has asked yet.
    ///
    /// Sharing by key is what makes <see cref="Prepare"/> worth calling: a
    /// later <see cref="Speak"/> for the same text joins the work already in
    /// flight instead of queueing a second copy of it behind the first.
    /// </summary>
    private Job RequestJob(VoiceId voice, string text)
    {
        string voiceKey = VoiceCatalog.ToKey(voice);
        string key = VoiceKey.For(voice, text);
        if (!IsReady || string.IsNullOrEmpty(voiceKey) || string.IsNullOrEmpty(key))
        {
            return null;
        }

        if (jobs.TryGetValue(key, out Job existing))
        {
            return existing;
        }

        Job job = new Job();
        jobs[key] = job;
        StartCoroutine(Run(job, key, voiceKey, text));
        return job;
    }

    private IEnumerator Run(Job job, string key, string voiceKey, string text)
    {
        string requestId = (++nextRequestId).ToString();
        job.RequestId = requestId;

#if UNITY_WEBGL && !UNITY_EDITOR
        VoiceSynthesis_Speak(requestId, voiceKey, text);
#else
        // Nothing synthesizes outside a browser build, so fail immediately
        // rather than making every caller wait out the timeout in the editor.
        failedRequests.Add(requestId);
#endif

        // Two clocks, and only one of them runs at a time. Until the runtime
        // picks this line up it is waiting on the lines ahead of it, which is
        // the queue clock. Once it starts, the queue no longer has anything to
        // do with it, so the work clock replaces it rather than joining it.
        bool hasStarted = false;
        float askedAt = Time.unscaledTime;
        float deadline = askedAt + queueTimeoutSeconds;

        while (!readyClips.ContainsKey(requestId) && !failedRequests.Contains(requestId))
        {
            if (!hasStarted && startedRequests.Contains(requestId))
            {
                hasStarted = true;
                deadline = Time.unscaledTime + timeoutSeconds;
            }

            if (Time.unscaledTime > deadline)
            {
                failedRequests.Add(requestId);
                Debug.LogWarning(
                    $"Voice synthesis gave up on a {voiceKey} line after "
                    + $"{(hasStarted ? timeoutSeconds : queueTimeoutSeconds):0}s"
                    + (hasStarted ? " of work." : " in the queue."),
                    this);
                break;
            }

            yield return null;
        }

        startedRequests.Remove(requestId);

        if (readyClips.TryGetValue(requestId, out AudioClip clip))
        {
            readyClips.Remove(requestId);
            job.Clip = clip;
        }

        failedRequests.Remove(requestId);
        job.IsDone = true;

        if (logProgress && job.Clip != null)
        {
            // Worth having in the console during a real Encounter. A line that
            // was prepared shows a total larger than its own work, and the
            // Learner still waits none of it, because it finished while the
            // line before it was playing.
            Debug.Log(
                $"Made a {voiceKey} line of {job.Clip.length:0.0}s in "
                + $"{Time.unscaledTime - askedAt:0.00}s.",
                this);
        }

        if (job.Clip != null)
        {
            Remember(key);
            yield break;
        }

        // Forget a line that came back with nothing, so asking again tries
        // again. A cached failure would silence that line for the whole
        // session, and the usual cause is a cancel or a hiccup rather than
        // anything about the text.
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

    // Called from VoiceSynthesisBridge.jslib by name. Do not rename.

    public void HandleVoiceReady(string sampleRate)
    {
        IsReady = true;
        if (logProgress)
        {
            Debug.Log($"Voices ready at {sampleRate}Hz. Unbaked lines will now be spoken.", this);
        }
    }

    /// <summary>The runtime has picked this line up, so its own clock can start.</summary>
    public void HandleVoiceStarted(string requestId)
    {
        startedRequests.Add(requestId);
    }

    /// <summary>
    /// One line is finished. Copy its samples across and make the clip.
    ///
    /// The payload is the request id, the sample count, and the rate, because
    /// audio cannot travel through SendMessage. The samples themselves are
    /// copied straight from the runtime into an array this side owns, which is
    /// the only way to get a playable clip in WebGL: one built from a file, of
    /// any format, stays unloaded and plays silence.
    /// </summary>
    public void HandleVoiceSamples(string payload)
    {
        string[] parts = payload.Split('|');
        if (parts.Length != 3
            || !int.TryParse(parts[1], out int count)
            || !int.TryParse(parts[2], out int sampleRate)
            || count <= 0
            || sampleRate <= 0)
        {
            Debug.LogWarning($"Could not read a synthesized line: {payload}", this);
            return;
        }

        float[] samples = new float[count];

#if UNITY_WEBGL && !UNITY_EDITOR
        int copied = VoiceSynthesis_ReadSamples(parts[0], samples, count);
#else
        int copied = 0;
#endif

        if (copied <= 0)
        {
            Debug.LogWarning($"A synthesized line arrived empty: {payload}", this);
            return;
        }

        AudioClip clip = AudioClip.Create($"Voice {parts[0]}", count, 1, sampleRate, false);
        clip.SetData(samples, 0);
        readyClips[parts[0]] = clip;
    }

    /// <summary>
    /// One line failed. The rest of the queue is unaffected, so this stays a
    /// warning and only that line falls back to ticks.
    /// </summary>
    public void HandleVoiceFailed(string payload)
    {
        int split = payload.IndexOf('|');
        string requestId = split > 0 ? payload.Substring(0, split) : payload;
        string message = split > 0 ? payload.Substring(split + 1) : "voice worker failed";

        failedRequests.Add(requestId);
        Debug.LogWarning($"Could not speak one line: {message}", this);
    }

    public void HandleVoiceError(string message)
    {
        // Not an error for the game: the ticks carry any line this cannot speak.
        Debug.LogWarning($"Voice synthesis unavailable: {message}", this);
        IsReady = false;

        // Fail everything in flight. Without this every waiting line sits out
        // its full timeout after the runtime has already said it is gone.
        foreach (KeyValuePair<string, Job> entry in jobs)
        {
            if (!entry.Value.IsDone && entry.Value.RequestId != null)
            {
                failedRequests.Add(entry.Value.RequestId);
            }
        }
    }
}

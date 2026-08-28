using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;

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
/// Everything here is fail-soft. Outside WebGL, before the model has finished
/// downloading, or after any error, <see cref="IsReady"/> stays false and the
/// caller gets no clip. A line with no clip falls back to the syllable ticks in
/// <c>DialogueActor</c>, so the game is playable throughout.
/// </summary>
[DisallowMultipleComponent]
public sealed class BrowserVoiceSynthesizer : MonoBehaviour
{
    /// <summary>
    /// Where the weights come from. Not in the repository: the file is 146MB and
    /// GitHub rejects anything past 100MB. Point this at your own host once the
    /// game has one, so the game does not depend on a third party staying up.
    /// </summary>
    [SerializeField]
    private string modelUrl =
        "https://huggingface.co/lmz/pocket-tts-without-voice-cloning-q8/resolve/main/tts_b6369a24.gguf";

    [SerializeField]
    private string tokenizerUrl =
        "https://huggingface.co/kyutai/pocket-tts-without-voice-cloning/resolve/main/tokenizer.model";

    [Tooltip("Seconds to wait for one line before giving up and letting it tick.")]
    [SerializeField, Min(1f)] private float timeoutSeconds = 20f;

    [SerializeField] private bool logProgress = true;

    private readonly Dictionary<string, string> readyClips = new Dictionary<string, string>();
    private readonly HashSet<string> failedRequests = new HashSet<string>();
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
    private static extern void VoiceSynthesis_ReleaseClip(string url);
#endif

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            return;
        }

        Instance = this;
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
            + "\"modelUrl\":\"" + modelUrl + "\","
            + "\"tokenizerUrl\":\"" + tokenizerUrl + "\","
            + "\"quant\":\"q8\"}";

        VoiceSynthesis_Begin(gameObject.name, config);
#else
        started = true;
#endif
    }

    /// <summary>
    /// Synthesize one line and hand back the clip.
    ///
    /// Yields until the audio is ready or the timeout passes. The result is null
    /// on any failure, which the caller should treat as "no voice for this line"
    /// rather than as an error worth stopping for.
    /// </summary>
    public IEnumerator Speak(VoiceId voice, string text, Action<AudioClip> onDone)
    {
        string key = VoiceCatalog.ToKey(voice);
        if (!IsReady || string.IsNullOrEmpty(key) || string.IsNullOrWhiteSpace(text))
        {
            onDone?.Invoke(null);
            yield break;
        }

        string requestId = (++nextRequestId).ToString();

#if UNITY_WEBGL && !UNITY_EDITOR
        VoiceSynthesis_Speak(requestId, key, text);
#else
        // Nothing synthesizes outside a browser build, so fail immediately
        // rather than making every caller wait out the timeout in the editor.
        failedRequests.Add(requestId);
#endif

        float deadline = Time.unscaledTime + timeoutSeconds;
        while (!readyClips.ContainsKey(requestId) && !failedRequests.Contains(requestId))
        {
            if (Time.unscaledTime > deadline)
            {
                failedRequests.Add(requestId);
                Debug.LogWarning($"Voice synthesis timed out after {timeoutSeconds:0}s.", this);
                break;
            }

            yield return null;
        }

        if (!readyClips.TryGetValue(requestId, out string url))
        {
            failedRequests.Remove(requestId);
            onDone?.Invoke(null);
            yield break;
        }

        readyClips.Remove(requestId);

        using (UnityWebRequest request =
            UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV))
        {
            yield return request.SendWebRequest();

            AudioClip clip = request.result == UnityWebRequest.Result.Success
                ? DownloadHandlerAudioClip.GetContent(request)
                : null;

            if (clip == null)
            {
                Debug.LogWarning($"Could not read the synthesized clip: {request.error}", this);
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            VoiceSynthesis_ReleaseClip(url);
#endif
            onDone?.Invoke(clip);
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

    public void HandleVoiceClip(string payload)
    {
        int split = payload.IndexOf('|');
        if (split <= 0)
        {
            return;
        }

        readyClips[payload.Substring(0, split)] = payload.Substring(split + 1);
    }

    public void HandleVoiceError(string message)
    {
        // Not an error for the game: the ticks carry any line this cannot speak.
        Debug.LogWarning($"Voice synthesis unavailable: {message}", this);
        IsReady = false;
        foreach (string pending in new List<string>(readyClips.Keys))
        {
            failedRequests.Add(pending);
        }
    }
}

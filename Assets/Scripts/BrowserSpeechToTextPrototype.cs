using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR_OSX
using System.Diagnostics;
using System.IO;
#endif

[DisallowMultipleComponent]
public sealed class BrowserSpeechToTextPrototype : MonoBehaviour
{
    // UI
    [Header("UI")]
    [SerializeField] private Button startListeningButton;
    [SerializeField] private Button submitButton;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text supportText;
    [SerializeField] private TMP_InputField fallbackInputField;
    [SerializeField] private TMP_Text liveTranscriptText;
    [SerializeField] private TMP_Text submittedTranscriptText;

    // Copy
    [Header("Copy")]
    [SerializeField] private string unsupportedMessage = "Speech recognition works in a WebGL build inside a supported browser such as Chrome or Edge.";

    private string currentTranscript = string.Empty;
    private bool isListening;
    private bool isPendingSubmit;

#if UNITY_EDITOR_OSX
    private string macEditorEventFilePath = string.Empty;
    private string macEditorStopFilePath = string.Empty;
    private int macEditorProcessedLineCount;
    private bool isMacEditorSpeechSessionActive;
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern int SpeechRecognition_IsSupported();

    [DllImport("__Internal")]
    private static extern void SpeechRecognition_StartListening(string targetName);

    [DllImport("__Internal")]
    private static extern void SpeechRecognition_StopListening();
#endif

    private void Awake()
    {
        UpdateSupportText();
        UpdateStatus("Ready. Click Start Listening to begin.");
        UpdateLiveTranscript(string.Empty);
        UpdateSubmittedTranscript("Nothing submitted yet.");
        RefreshButtons();
    }

    private void Update()
    {
#if UNITY_EDITOR_OSX
        DrainMacEditorEventFile();
#endif
    }

    private void OnEnable()
    {
        RegisterFallbackInputCallbacks();
    }

    private void OnDisable()
    {
        UnregisterFallbackInputCallbacks();

#if UNITY_EDITOR_OSX
        StopMacEditorSpeechSession();
#endif
    }

    private void OnDestroy()
    {
#if UNITY_EDITOR_OSX
        StopMacEditorSpeechSession();
#endif
    }

    public void SetReferences(
        Button startButtonReference,
        Button submitButtonReference,
        TMP_Text statusTextReference,
        TMP_Text supportTextReference,
        TMP_InputField fallbackInputFieldReference,
        TMP_Text liveTranscriptTextReference,
        TMP_Text submittedTranscriptTextReference)
    {
        startListeningButton = startButtonReference;
        submitButton = submitButtonReference;
        statusText = statusTextReference;
        supportText = supportTextReference;
        fallbackInputField = fallbackInputFieldReference;
        liveTranscriptText = liveTranscriptTextReference;
        submittedTranscriptText = submittedTranscriptTextReference;
    }

    public void StartListening()
    {
#if UNITY_EDITOR_OSX
        if (CanUseMacEditorSpeech())
        {
            BeginMacEditorSpeechCapture();
            return;
        }
#endif

        if (IsEditorSimulationMode())
        {
            BeginEditorSimulation();
            return;
        }

        if (!IsSpeechRecognitionSupported())
        {
            UpdateStatus("Speech is not available here. Use the fallback text field, then click Submit.");
            FocusFallbackInput();
            RefreshButtons();
            return;
        }

        currentTranscript = string.Empty;
        isPendingSubmit = false;
        isListening = true;

        UpdateStatus("Listening. Speak into your mic, then click Submit when you are done.");
        UpdateLiveTranscript(string.Empty);
        RefreshButtons();
        BeginSpeechRecognition();
    }

    public void SubmitTranscript()
    {
#if UNITY_EDITOR_OSX
        if (IsMacEditorSpeechActive())
        {
            isPendingSubmit = true;
            UpdateStatus("Stopping Mac speech capture and submitting your transcript...");
            RefreshButtons();
            RequestMacEditorSpeechStop();
            return;
        }
#endif

        if (isListening && IsSpeechRecognitionSupported())
        {
            isPendingSubmit = true;
            UpdateStatus("Stopping capture and submitting your transcript...");
            RefreshButtons();
            EndSpeechRecognition();
            return;
        }

        FinalizeSubmission();
    }

    public void HandleTranscriptUpdated(string transcript)
    {
        currentTranscript = transcript == null ? string.Empty : transcript.Trim();
        UpdateLiveTranscript(currentTranscript);
        RefreshButtons();
    }

    public void HandleSpeechStatus(string status)
    {
        string normalizedStatus = string.IsNullOrWhiteSpace(status)
            ? string.Empty
            : status.Trim().ToLowerInvariant();

        switch (normalizedStatus)
        {
            case "started":
                isListening = true;
                UpdateStatus("Listening. Speak into your mic, then click Submit when you are done.");
                break;

            case "already-listening":
                isListening = true;
                UpdateStatus("Speech recognition is already running.");
                break;

            case "stopped":
                isListening = false;
                if (isPendingSubmit)
                {
                    FinalizeSubmission();
                    return;
                }

                UpdateStatus("Speech capture stopped. You can start again or submit the current transcript.");
                break;

            case "ended":
                isListening = false;
                if (isPendingSubmit)
                {
                    FinalizeSubmission();
                    return;
                }

                UpdateStatus("Speech capture ended. You can start again or submit the current transcript.");
                break;
        }

        RefreshButtons();
    }

    public void HandleSpeechError(string error)
    {
        isListening = false;
        isPendingSubmit = false;

        string safeError = string.IsNullOrWhiteSpace(error) ? "unknown error" : error.Trim();
        UpdateStatus("Speech recognition error: " + safeError);
        RefreshButtons();
    }

    public void HandleFallbackInputChanged(string value)
    {
        string normalizedValue = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

        if (IsEditorSimulationMode() && !IsMacEditorSpeechEnabled() && isListening)
        {
            currentTranscript = normalizedValue;
            UpdateLiveTranscript(currentTranscript);
            RefreshButtons();
            return;
        }

        if (!IsSpeechRecognitionSupported() && !IsMacEditorSpeechEnabled())
        {
            UpdateLiveTranscript(normalizedValue);
            RefreshButtons();
        }
    }

    private void FinalizeSubmission()
    {
        isListening = false;
        isPendingSubmit = false;

        string submittedTranscript = GetSubmissionText();
        bool hasSubmittedWords = !string.IsNullOrWhiteSpace(submittedTranscript)
            && submittedTranscript != "No speech or fallback text was submitted.";

        UpdateSubmittedTranscript(submittedTranscript);
        UpdateStatus("Submitted. Check the transcript below and the Console output.");
        RefreshButtons();

        if (!hasSubmittedWords)
        {
            UnityEngine.Debug.Log("Speech-to-text submitted with no captured words.", this);
            return;
        }

        UnityEngine.Debug.Log("Speech-to-text submitted: " + submittedTranscript, this);
    }

    private void UpdateSupportText()
    {
        if (supportText == null)
        {
            return;
        }

#if UNITY_EDITOR_OSX
        if (CanUseMacEditorSpeech())
        {
            supportText.text = "Mac editor speech mode is available. The first run may ask for microphone and speech-recognition permission.";
            return;
        }
#endif

        if (IsEditorSimulationMode())
        {
            supportText.text = "Unity Editor test mode is active. Type in the fallback field to simulate speech input.";
            return;
        }

        supportText.text = IsSpeechRecognitionSupported()
            ? "Browser speech recognition is available in this build."
            : unsupportedMessage;
    }

    private void UpdateStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message ?? string.Empty;
        }
    }

    private void UpdateLiveTranscript(string transcript)
    {
        if (liveTranscriptText == null)
        {
            return;
        }

        liveTranscriptText.text = string.IsNullOrWhiteSpace(transcript)
            ? "Waiting for speech..."
            : transcript;
    }

    private void UpdateSubmittedTranscript(string transcript)
    {
        if (submittedTranscriptText != null)
        {
            submittedTranscriptText.text = transcript ?? string.Empty;
        }
    }

    private void RefreshButtons()
    {
        if (startListeningButton != null)
        {
            startListeningButton.interactable = !isListening;
        }

        if (submitButton != null)
        {
            submitButton.interactable = isListening
                || !string.IsNullOrWhiteSpace(currentTranscript)
                || !string.IsNullOrWhiteSpace(GetFallbackText());
        }
    }

    private string GetSubmissionText()
    {
        if (!string.IsNullOrWhiteSpace(currentTranscript))
        {
            return currentTranscript.Trim();
        }

        string fallbackText = GetFallbackText();
        if (!string.IsNullOrWhiteSpace(fallbackText))
        {
            return fallbackText;
        }

        return "No speech or fallback text was submitted.";
    }

    private string GetFallbackText()
    {
        if (fallbackInputField == null || string.IsNullOrWhiteSpace(fallbackInputField.text))
        {
            return string.Empty;
        }

        return fallbackInputField.text.Trim();
    }

    private bool IsSpeechRecognitionSupported()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return SpeechRecognition_IsSupported() != 0;
#else
        return false;
#endif
    }

    private void BeginSpeechRecognition()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        SpeechRecognition_StartListening(gameObject.name);
#endif
    }

    private void EndSpeechRecognition()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        SpeechRecognition_StopListening();
#endif
    }

    private void BeginEditorSimulation()
    {
        currentTranscript = string.Empty;
        isPendingSubmit = false;
        isListening = true;

        if (fallbackInputField != null)
        {
            fallbackInputField.text = string.Empty;
        }

        UpdateStatus("Editor test mode: type your pretend speech into the fallback box, then click Submit.");
        UpdateLiveTranscript(string.Empty);
        RefreshButtons();
        FocusFallbackInput();
    }

    private void FocusFallbackInput()
    {
        if (fallbackInputField == null)
        {
            return;
        }

        fallbackInputField.ActivateInputField();
        fallbackInputField.Select();
    }

    private void RegisterFallbackInputCallbacks()
    {
        if (fallbackInputField == null)
        {
            return;
        }

        fallbackInputField.onValueChanged.RemoveListener(HandleFallbackInputChanged);
        fallbackInputField.onValueChanged.AddListener(HandleFallbackInputChanged);
    }

    private void UnregisterFallbackInputCallbacks()
    {
        if (fallbackInputField == null)
        {
            return;
        }

        fallbackInputField.onValueChanged.RemoveListener(HandleFallbackInputChanged);
    }

    private bool IsMacEditorSpeechEnabled()
    {
#if UNITY_EDITOR_OSX
        return CanUseMacEditorSpeech();
#else
        return false;
#endif
    }

#if UNITY_EDITOR_OSX
    private static string GetMacEditorSpeechAppPath()
    {
        return Path.Combine(
            Application.dataPath,
            "Plugins",
            "macOS",
            "SpeechCaptureHelper.app");
    }

    private static string GetMacEditorSpeechHelperPath()
    {
        return Path.Combine(
            GetMacEditorSpeechAppPath(),
            "Contents",
            "MacOS",
            "SpeechCaptureHelper");
    }

    private bool CanUseMacEditorSpeech()
    {
        return Directory.Exists(GetMacEditorSpeechAppPath()) && File.Exists(GetMacEditorSpeechHelperPath());
    }

    private bool IsMacEditorSpeechActive()
    {
        return isMacEditorSpeechSessionActive;
    }

    private void BeginMacEditorSpeechCapture()
    {
        if (!CanUseMacEditorSpeech())
        {
            BeginEditorSimulation();
            return;
        }

        StopMacEditorSpeechSession();

        currentTranscript = string.Empty;
        isPendingSubmit = false;
        isListening = true;
        UpdateLiveTranscript(string.Empty);
        UpdateStatus("Preparing Mac speech capture. macOS may ask for microphone and speech permission the first time.");
        RefreshButtons();

        try
        {
            string sessionDirectory = Path.Combine(Application.temporaryCachePath, "SpeechCaptureHelper");
            Directory.CreateDirectory(sessionDirectory);

            string sessionId = System.Guid.NewGuid().ToString("N");
            macEditorEventFilePath = Path.Combine(sessionDirectory, sessionId + ".events.txt");
            macEditorStopFilePath = Path.Combine(sessionDirectory, sessionId + ".stop");
            macEditorProcessedLineCount = 0;
            isMacEditorSpeechSessionActive = true;

            string helperAppPath = GetMacEditorSpeechAppPath();
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/open",
                UseShellExecute = false,
                CreateNoWindow = true,
                Arguments = "-n \"" + helperAppPath + "\" --args --event-file \"" + macEditorEventFilePath + "\" --stop-file \"" + macEditorStopFilePath + "\""
            };

            Process launchProcess = Process.Start(startInfo);
            if (launchProcess == null)
            {
                throw new System.InvalidOperationException("macOS could not launch the SpeechCaptureHelper app.");
            }

            launchProcess.Dispose();
        }
        catch (System.Exception exception)
        {
            ClearMacEditorSpeechSessionFiles();
            isListening = false;
            UpdateStatus("Couldn't launch the Mac speech helper: " + exception.Message);
            RefreshButtons();
        }
    }

    private void RequestMacEditorSpeechStop()
    {
        if (!IsMacEditorSpeechActive())
        {
            FinalizeSubmission();
            return;
        }

        try
        {
            File.WriteAllText(macEditorStopFilePath, "stop");
        }
        catch (System.Exception exception)
        {
            UpdateStatus("Couldn't stop the Mac speech helper cleanly: " + exception.Message);
            FinalizeSubmission();
        }
    }

    private void StopMacEditorSpeechSession()
    {
        if (!isMacEditorSpeechSessionActive && string.IsNullOrEmpty(macEditorEventFilePath) && string.IsNullOrEmpty(macEditorStopFilePath))
        {
            return;
        }

        try
        {
            if (!string.IsNullOrEmpty(macEditorStopFilePath))
            {
                File.WriteAllText(macEditorStopFilePath, "stop");
            }
        }
        catch
        {
            // Ignore cleanup failures during editor shutdown.
        }
        finally
        {
            ClearMacEditorSpeechSessionFiles();
        }
    }

    private void ClearMacEditorSpeechSessionFiles()
    {
        try
        {
            if (!string.IsNullOrEmpty(macEditorEventFilePath) && File.Exists(macEditorEventFilePath))
            {
                File.Delete(macEditorEventFilePath);
            }
        }
        catch
        {
        }

        try
        {
            if (!string.IsNullOrEmpty(macEditorStopFilePath) && File.Exists(macEditorStopFilePath))
            {
                File.Delete(macEditorStopFilePath);
            }
        }
        catch
        {
        }

        macEditorEventFilePath = string.Empty;
        macEditorStopFilePath = string.Empty;
        macEditorProcessedLineCount = 0;
        isMacEditorSpeechSessionActive = false;
    }

    private void DrainMacEditorEventFile()
    {
        if (!isMacEditorSpeechSessionActive || string.IsNullOrEmpty(macEditorEventFilePath) || !File.Exists(macEditorEventFilePath))
        {
            return;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(macEditorEventFilePath);
        }
        catch (IOException)
        {
            return;
        }

        for (int index = macEditorProcessedLineCount; index < lines.Length; index++)
        {
            string message = lines[index];
            if (!string.IsNullOrWhiteSpace(message))
            {
                ProcessMacEditorMessage(message);
            }
        }

        macEditorProcessedLineCount = lines.Length;
    }

    private void ProcessMacEditorMessage(string message)
    {
        int separatorIndex = message.IndexOf('|');
        if (separatorIndex < 0)
        {
            return;
        }

        string kind = message.Substring(0, separatorIndex);
        string payload = message.Substring(separatorIndex + 1);

        switch (kind)
        {
            case "STATUS":
                ProcessMacEditorStatus(payload);
                break;

            case "TRANSCRIPT":
                HandleTranscriptUpdated(payload);
                break;

            case "FINAL":
                HandleTranscriptUpdated(payload);
                if (isPendingSubmit)
                {
                    FinalizeSubmission();
                }
                break;

            case "ERROR":
                HandleSpeechError(payload);
                break;

            case "EXIT":
                if (isPendingSubmit)
                {
                    FinalizeSubmission();
                }
                else if (isListening)
                {
                    isListening = false;
                    UpdateStatus("Mac speech capture ended.");
                    RefreshButtons();
                }

                ClearMacEditorSpeechSessionFiles();
                break;
        }
    }

    private void ProcessMacEditorStatus(string status)
    {
        string normalizedStatus = string.IsNullOrWhiteSpace(status)
            ? string.Empty
            : status.Trim().ToLowerInvariant();

        switch (normalizedStatus)
        {
            case "authorizing":
                UpdateStatus("Checking Mac speech permissions...");
                break;

            case "requesting-microphone-access":
                UpdateStatus("macOS is requesting microphone access for the helper.");
                break;

            case "requesting-speech-access":
                UpdateStatus("macOS is requesting speech-recognition access for the helper.");
                break;

            case "starting":
                UpdateStatus("Starting Mac microphone capture...");
                break;

            case "listening":
                isListening = true;
                UpdateStatus("Listening through your Mac microphone. Speak, then click Submit when you are done.");
                break;

            case "stopping":
                UpdateStatus("Finishing your Mac speech transcription...");
                break;

            case "stopped":
                UpdateStatus("Mac speech capture stopped.");
                break;
        }

        RefreshButtons();
    }
#endif

    private static bool IsEditorSimulationMode()
    {
#if UNITY_EDITOR
        return true;
#else
        return false;
#endif
    }
}

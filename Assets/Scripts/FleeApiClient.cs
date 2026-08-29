using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[DisallowMultipleComponent]
public sealed class FleeApiClient : MonoBehaviour
{
    private const string DefaultBaseUrl =
        "https://flee-the-faculty-747438214074.us-central1.run.app";
    private const string TokenResourceName = "FleeApiClientToken";
    private const string ActiveClassroomIdKey = "Flee.ActiveClassroomId";

    [SerializeField] private string baseUrl = DefaultBaseUrl;
    [SerializeField] private string presetId = "photosynthesis";
    [SerializeField, Min(10)] private int requestTimeoutSeconds = 120;

    private static FleeApiClient instance;
    private FleeClassroomResponse activeClassroom;

    public FleeClassroomSession ActiveClassroom => ToClassroomSession(activeClassroom);

    public static void ResetClassroomSession()
    {
        if (instance != null)
        {
            instance.activeClassroom = null;
        }

        PlayerPrefs.DeleteKey(ActiveClassroomIdKey);
        PlayerPrefs.Save();
    }

    public static FleeApiClient GetOrCreate()
    {
        if (instance != null)
        {
            return instance;
        }

#if UNITY_2023_1_OR_NEWER
        instance = FindFirstObjectByType<FleeApiClient>();
#else
        instance = FindObjectOfType<FleeApiClient>();
#endif
        if (instance != null)
        {
            return instance;
        }

        GameObject apiObject = new GameObject("Flee API Client");
        instance = apiObject.AddComponent<FleeApiClient>();
        DontDestroyOnLoad(apiObject);
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    public IEnumerator PreparePresetClassroom(
        Action<FleeClassroomSession> onSuccess,
        Action<FleeApiFailure> onFailure,
        Action<float, string> onProgress = null)
    {
        if (activeClassroom != null)
        {
            onProgress?.Invoke(1f, "Classroom is ready.");
            onSuccess?.Invoke(ToClassroomSession(activeClassroom));
            yield break;
        }

        string savedClassroomId = PlayerPrefs.GetString(ActiveClassroomIdKey, string.Empty).Trim();
        if (!string.IsNullOrEmpty(savedClassroomId))
        {
            onProgress?.Invoke(0.08f, "Rejoining classroom...");
            FleeClassroomResponse resumedClassroom = null;
            FleeApiFailure resumeFailure = null;
            yield return GetJson<FleeClassroomResponse>(
                "/v1/classrooms/" + UnityWebRequest.EscapeURL(savedClassroomId),
                response => resumedClassroom = response,
                error => resumeFailure = error);

            if (resumeFailure == null && IsUsableClassroom(resumedClassroom))
            {
                SetActiveClassroom(resumedClassroom);
                onProgress?.Invoke(1f, "Classroom restored.");
                onSuccess?.Invoke(ToClassroomSession(activeClassroom));
                yield break;
            }

            if (resumeFailure != null && resumeFailure.StatusCode != 404)
            {
                onFailure?.Invoke(resumeFailure);
                yield break;
            }

            ResetClassroomSession();
        }

        onProgress?.Invoke(0.08f, "Loading classroom...");
        FleeClassroomResponse classroom = null;
        FleeApiFailure failure = null;
        yield return PostJson<FleeClassroomRequest, FleeClassroomResponse>(
            "/v1/classrooms",
            new FleeClassroomRequest
            {
                source = "preset",
                presetId = string.IsNullOrWhiteSpace(presetId) ? "photosynthesis" : presetId.Trim()
            },
            response => classroom = response,
            error => failure = error);

        if (failure != null)
        {
            onFailure?.Invoke(failure);
            yield break;
        }

        if (!IsUsableClassroom(classroom))
        {
            onFailure?.Invoke(new FleeApiFailure(0, "The classroom did not include any Pupils."));
            yield break;
        }

        SetActiveClassroom(classroom);
        onProgress?.Invoke(1f, "Classroom is ready.");
        onSuccess?.Invoke(ToClassroomSession(activeClassroom));
    }

    public IEnumerator BeginEncounter(
        string requestedPupilId,
        string requestedPupilName,
        Action<FleeEncounterSession> onSuccess,
        Action<FleeApiFailure> onFailure,
        Action<float, string> onProgress = null)
    {
        string safePupilName = SafePupilName(requestedPupilName);
        FleePupilResponse pupil = FindPupil(activeClassroom, requestedPupilId, requestedPupilName);

        if (activeClassroom == null || pupil == null)
        {
            onFailure?.Invoke(new FleeApiFailure(
                0,
                activeClassroom == null
                    ? "There is no active Classroom."
                    : "The active Classroom did not include " + safePupilName + "."));
            yield break;
        }

        FleeEncounterOpening opening = null;
        FleeApiFailure failure = null;
        onProgress?.Invoke(0.72f, "Generating " + safePupilName + "'s question...");
        yield return PostJson<FleeEncounterRequest, FleeEncounterOpening>(
            "/v1/encounters",
            new FleeEncounterRequest
            {
                classroomId = activeClassroom.classroomId,
                pupilId = pupil.pupilId
            },
            response => opening = response,
            error => failure = error);

        if (failure != null)
        {
            onFailure?.Invoke(failure);
            yield break;
        }

        if (opening == null || string.IsNullOrWhiteSpace(opening.line))
        {
            onFailure?.Invoke(new FleeApiFailure(0, "The classroom returned an empty opening line."));
            yield break;
        }

        onProgress?.Invoke(1f, safePupilName + " is ready.");
        onSuccess?.Invoke(new FleeEncounterSession(
            activeClassroom.classroomId,
            pupil.pupilId,
            pupil.name,
            opening.line.Trim(),
            opening.turnsRemaining,
            opening.satisfied,
            opening.firstApproach,
            pupil.turnBudget,
            pupil.turnsUsed));
    }

    public IEnumerator SubmitTurn(
        FleeEncounterSession encounter,
        string explanation,
        Action<FleeTurnResult> onSuccess,
        Action<FleeApiFailure> onFailure)
    {
        if (encounter == null)
        {
            onFailure?.Invoke(new FleeApiFailure(0, "There is no active encounter."));
            yield break;
        }

        if (!encounter.CanAcceptExplanation)
        {
            onFailure?.Invoke(new FleeApiFailure(409, "This Pupil cannot take another explanation."));
            yield break;
        }

        if (string.IsNullOrWhiteSpace(explanation))
        {
            onFailure?.Invoke(new FleeApiFailure(422, "AraBOT's explanation was empty."));
            yield break;
        }

        string normalizedExplanation = explanation.Trim();
        string attemptId = Guid.NewGuid().ToString("N");
        FleeTurnRequest requestPayload = new FleeTurnRequest
        {
            classroomId = encounter.ClassroomId,
            pupilId = encounter.PupilId,
            explanation = normalizedExplanation,
            attemptId = attemptId
        };

        FleeTurnResponse response = null;
        FleeApiFailure failure = null;
        yield return PostJson<FleeTurnRequest, FleeTurnResponse>(
            "/v1/turns",
            requestPayload,
            result => response = result,
            error => failure = error);

        // A connection can drop after the server spends the turn. The same attempt id
        // makes this one automatic retry return the original result without charging twice.
        if (failure != null && failure.StatusCode == 0)
        {
            response = null;
            failure = null;
            yield return PostJson<FleeTurnRequest, FleeTurnResponse>(
                "/v1/turns",
                requestPayload,
                result => response = result,
                error => failure = error);
        }

        if (failure != null)
        {
            onFailure?.Invoke(failure);
            yield break;
        }

        if (response == null || string.IsNullOrWhiteSpace(response.restatement))
        {
            onFailure?.Invoke(new FleeApiFailure(0, "The classroom returned an empty reply."));
            yield break;
        }

        encounter.ApplyTurnResponse(
            response.turnsRemaining,
            response.turnsUsed,
            response.turnBudget,
            response.satisfied);
        UpdateCachedPupil(encounter, response);
        onSuccess?.Invoke(new FleeTurnResult(
            response.restatement.Trim(),
            string.IsNullOrWhiteSpace(response.followUp) ? string.Empty : response.followUp.Trim(),
            response.satisfied,
            response.score,
            response.turnsUsed,
            response.turnBudget,
            response.turnsRemaining,
            response.encounterEnded,
            ToFindings(response.findings)));
    }

    private IEnumerator PostJson<TRequest, TResponse>(
        string path,
        TRequest payload,
        Action<TResponse> onSuccess,
        Action<FleeApiFailure> onFailure)
    {
        string json = JsonUtility.ToJson(payload);
        string url = BuildUrl(path);
        byte[] body = Encoding.UTF8.GetBytes(json);

        UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
        {
            uploadHandler = new UploadHandlerRaw(body),
            downloadHandler = new DownloadHandlerBuffer(),
            timeout = Mathf.Max(10, requestTimeoutSeconds)
        };
        ApplyRequestHeaders(request, true);

        yield return request.SendWebRequest();

        HandleResponse(path, request, onSuccess, onFailure);
    }

    private IEnumerator GetJson<TResponse>(
        string path,
        Action<TResponse> onSuccess,
        Action<FleeApiFailure> onFailure)
    {
        UnityWebRequest request = UnityWebRequest.Get(BuildUrl(path));
        request.timeout = Mathf.Max(10, requestTimeoutSeconds);
        ApplyRequestHeaders(request, false);

        yield return request.SendWebRequest();

        HandleResponse(path, request, onSuccess, onFailure);
    }

    private void HandleResponse<TResponse>(
        string path,
        UnityWebRequest request,
        Action<TResponse> onSuccess,
        Action<FleeApiFailure> onFailure)
    {
        if (request == null)
        {
            onFailure?.Invoke(new FleeApiFailure(0, "The classroom request could not be created."));
            return;
        }

        bool succeeded = request.responseCode >= 200
            && request.responseCode < 300
            && request.result == UnityWebRequest.Result.Success;

        if (!succeeded)
        {
            FleeApiFailure failure = BuildFailure(request);
            Debug.LogError(
                "Flee API " + path + " failed (" + failure.StatusCode + "): " + failure.Message,
                this);
            onFailure?.Invoke(failure);
            request.Dispose();
            return;
        }

        try
        {
            TResponse response = JsonUtility.FromJson<TResponse>(request.downloadHandler.text);
            Debug.Log("Flee API " + path + " succeeded.", this);
            onSuccess?.Invoke(response);
        }
        catch (Exception exception)
        {
            onFailure?.Invoke(new FleeApiFailure(
                request.responseCode,
                "The classroom reply could not be read: " + exception.Message));
        }
        finally
        {
            request.Dispose();
        }
    }

    private static void ApplyRequestHeaders(UnityWebRequest request, bool hasJsonBody)
    {
        if (hasJsonBody)
        {
            request.SetRequestHeader("Content-Type", "application/json");
        }

        string clientToken = ResolveClientToken();
        if (!string.IsNullOrEmpty(clientToken))
        {
            request.SetRequestHeader("X-Client-Token", clientToken);
        }
    }

    private string BuildUrl(string path)
    {
        string safeBaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim();
        return safeBaseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
    }

    private static string ResolveClientToken()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        string environmentToken = Environment.GetEnvironmentVariable("FLEE_CLIENT_TOKEN");
        if (!string.IsNullOrWhiteSpace(environmentToken))
        {
            return environmentToken.Trim();
        }
#endif

        TextAsset tokenAsset = Resources.Load<TextAsset>(TokenResourceName);
        return tokenAsset == null ? string.Empty : tokenAsset.text.Trim();
    }

    private static FleePupilResponse FindPupil(
        FleeClassroomResponse classroom,
        string requestedPupilId,
        string requestedPupilName)
    {
        if (classroom == null || classroom.pupils == null)
        {
            return null;
        }

        string safeName = SafePupilName(requestedPupilName);
        for (int index = 0; index < classroom.pupils.Length; index++)
        {
            FleePupilResponse pupil = classroom.pupils[index];
            if (pupil == null)
            {
                continue;
            }

            bool idMatches = !string.IsNullOrWhiteSpace(requestedPupilId)
                && string.Equals(pupil.pupilId, requestedPupilId.Trim(), StringComparison.Ordinal);
            bool nameMatches = string.Equals(pupil.name, safeName, StringComparison.OrdinalIgnoreCase);
            if (idMatches || nameMatches)
            {
                return pupil;
            }
        }

        return null;
    }

    private static bool IsUsableClassroom(FleeClassroomResponse classroom)
    {
        return classroom != null
            && !string.IsNullOrWhiteSpace(classroom.classroomId)
            && classroom.pupils != null
            && classroom.pupils.Length > 0;
    }

    private void SetActiveClassroom(FleeClassroomResponse classroom)
    {
        activeClassroom = classroom;
        PlayerPrefs.SetString(ActiveClassroomIdKey, classroom.classroomId.Trim());
        PlayerPrefs.Save();
    }

    private void UpdateCachedPupil(FleeEncounterSession encounter, FleeTurnResponse response)
    {
        FleePupilResponse pupil = FindPupil(activeClassroom, encounter.PupilId, encounter.PupilName);
        if (pupil == null)
        {
            return;
        }

        pupil.turnsUsed = response.turnsUsed;
        pupil.turnBudget = response.turnBudget > 0 ? response.turnBudget : pupil.turnBudget;
        pupil.satisfied = response.satisfied;
        pupil.learnedAnswer = string.IsNullOrWhiteSpace(response.restatement)
            ? pupil.learnedAnswer
            : response.restatement.Trim();
        pupil.lastScore = response.score;
    }

    private static FleeTurnFindings ToFindings(FleeFindingsResponse findings)
    {
        return findings == null
            ? null
            : new FleeTurnFindings(
                findings.addressedMisconception,
                findings.namedKeyMechanism,
                findings.reachedThisPupil,
                findings.containedFalseClaim);
    }

    private static FleeClassroomSession ToClassroomSession(FleeClassroomResponse classroom)
    {
        if (classroom == null || classroom.pupils == null)
        {
            return null;
        }

        FleePupilSession[] pupils = new FleePupilSession[classroom.pupils.Length];
        for (int index = 0; index < classroom.pupils.Length; index++)
        {
            FleePupilResponse pupil = classroom.pupils[index];
            pupils[index] = pupil == null
                ? null
                : new FleePupilSession(
                    pupil.pupilId,
                    pupil.name,
                    pupil.personality,
                    pupil.quirk,
                    pupil.voice,
                    pupil.misconception,
                    pupil.turnBudget,
                    pupil.turnsUsed,
                    pupil.satisfied,
                    pupil.learnedAnswer,
                    pupil.lastScore);
        }

        return new FleeClassroomSession(
            classroom.classroomId,
            classroom.topic,
            classroom.rescueQuota,
            pupils);
    }

    private static string SafePupilName(string pupilName)
    {
        return string.IsNullOrWhiteSpace(pupilName) ? "Mary" : pupilName.Trim();
    }

    private static FleeApiFailure BuildFailure(UnityWebRequest request)
    {
        string message = request.error;
        string responseText = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;

        if (!string.IsNullOrWhiteSpace(responseText))
        {
            try
            {
                FleeApiErrorBody errorBody = JsonUtility.FromJson<FleeApiErrorBody>(responseText);
                if (errorBody != null && !string.IsNullOrWhiteSpace(errorBody.detail))
                {
                    message = errorBody.detail.Trim();
                }
            }
            catch
            {
                message = responseText.Trim();
            }
        }

        int retryAfterSeconds = 0;
        string retryAfter = request.GetResponseHeader("Retry-After");
        if (!string.IsNullOrWhiteSpace(retryAfter))
        {
            int.TryParse(retryAfter, out retryAfterSeconds);
        }

        return new FleeApiFailure(request.responseCode, message, retryAfterSeconds);
    }

    [Serializable]
    private sealed class FleeClassroomRequest
    {
        public string source;
        public string presetId;
    }

    [Serializable]
    private sealed class FleeClassroomResponse
    {
        public string classroomId;
        public string topic;
        public FleePupilResponse[] pupils;
        public int rescueQuota;
    }

    [Serializable]
    private sealed class FleePupilResponse
    {
        public string pupilId;
        public string name;
        public string personality;
        public string quirk;
        public string voice;
        public string misconception;
        public int turnBudget;
        public int turnsUsed;
        public bool satisfied;
        public string learnedAnswer;
        public int lastScore;
    }

    [Serializable]
    private sealed class FleeEncounterRequest
    {
        public string classroomId;
        public string pupilId;
    }

    [Serializable]
    private sealed class FleeEncounterOpening
    {
        public string pupilId;
        public string line;
        public int turnsRemaining;
        public bool satisfied;
        public bool firstApproach;
    }

    [Serializable]
    private sealed class FleeTurnRequest
    {
        public string classroomId;
        public string pupilId;
        public string explanation;
        public string attemptId;
    }

    [Serializable]
    private sealed class FleeTurnResponse
    {
        public FleeFindingsResponse findings;
        public string restatement;
        public string followUp;
        public bool satisfied;
        public int score;
        public int turnsUsed;
        public int turnBudget;
        public int turnsRemaining;
        public bool encounterEnded;
    }

    [Serializable]
    private sealed class FleeFindingsResponse
    {
        public bool addressedMisconception;
        public bool namedKeyMechanism;
        public bool reachedThisPupil;
        public string containedFalseClaim;
    }

    [Serializable]
    private sealed class FleeApiErrorBody
    {
        public string detail;
    }
}

public sealed class FleeEncounterSession
{
    public FleeEncounterSession(
        string classroomId,
        string pupilId,
        string pupilName,
        string openingLine,
        int turnsRemaining,
        bool satisfied,
        bool firstApproach,
        int turnBudget,
        int turnsUsed)
    {
        ClassroomId = classroomId;
        PupilId = pupilId;
        PupilName = pupilName;
        OpeningLine = openingLine;
        TurnsRemaining = turnsRemaining;
        Satisfied = satisfied;
        FirstApproach = firstApproach;
        TurnBudget = turnBudget;
        TurnsUsed = turnsUsed;
    }

    public string ClassroomId { get; }
    public string PupilId { get; }
    public string PupilName { get; }
    public string OpeningLine { get; }
    public int TurnsRemaining { get; internal set; }
    public bool Satisfied { get; private set; }
    public bool FirstApproach { get; }
    public int TurnBudget { get; private set; }
    public int TurnsUsed { get; private set; }
    public bool CanAcceptExplanation => !Satisfied && TurnsRemaining > 0;

    internal void ApplyTurnResponse(
        int turnsRemaining,
        int turnsUsed,
        int turnBudget,
        bool satisfied)
    {
        TurnsRemaining = Mathf.Max(0, turnsRemaining);
        TurnsUsed = Mathf.Max(0, turnsUsed);
        if (turnBudget > 0)
        {
            TurnBudget = turnBudget;
        }

        Satisfied = satisfied;
    }
}

public sealed class FleeClassroomSession
{
    public FleeClassroomSession(
        string classroomId,
        string topic,
        int rescueQuota,
        FleePupilSession[] pupils)
    {
        ClassroomId = classroomId;
        Topic = topic;
        RescueQuota = rescueQuota;
        Pupils = pupils ?? Array.Empty<FleePupilSession>();
    }

    public string ClassroomId { get; }
    public string Topic { get; }
    public int RescueQuota { get; }
    public FleePupilSession[] Pupils { get; }
}

public sealed class FleePupilSession
{
    public FleePupilSession(
        string pupilId,
        string name,
        string personality,
        string quirk,
        string voice,
        string misconception,
        int turnBudget,
        int turnsUsed,
        bool satisfied,
        string learnedAnswer = null,
        int lastScore = 0)
    {
        PupilId = pupilId;
        Name = name;
        Personality = personality;
        Quirk = quirk;
        Voice = voice;
        Misconception = misconception;
        TurnBudget = turnBudget;
        TurnsUsed = turnsUsed;
        Satisfied = satisfied;
        LearnedAnswer = learnedAnswer ?? string.Empty;
        LastScore = lastScore;
    }

    public string PupilId { get; }
    public string Name { get; }
    public string Personality { get; }
    public string Quirk { get; }
    public string Voice { get; }
    public string Misconception { get; }
    public int TurnBudget { get; }
    public int TurnsUsed { get; }
    public bool Satisfied { get; }
    public string LearnedAnswer { get; }
    public int LastScore { get; }
}

public sealed class FleeTurnResult
{
    public FleeTurnResult(
        string restatement,
        string followUp,
        bool satisfied,
        int score,
        int turnsUsed,
        int turnBudget,
        int turnsRemaining,
        bool encounterEnded,
        FleeTurnFindings findings)
    {
        Restatement = restatement;
        FollowUp = followUp;
        Satisfied = satisfied;
        Score = score;
        TurnsUsed = turnsUsed;
        TurnBudget = turnBudget;
        TurnsRemaining = turnsRemaining;
        EncounterEnded = encounterEnded;
        Findings = findings;
    }

    public string Restatement { get; }
    public string FollowUp { get; }
    public bool Satisfied { get; }
    public int Score { get; }
    public int TurnsUsed { get; }
    public int TurnBudget { get; }
    public int TurnsRemaining { get; }
    public bool EncounterEnded { get; }
    public FleeTurnFindings Findings { get; }
}

public sealed class FleeTurnFindings
{
    public FleeTurnFindings(
        bool addressedMisconception,
        bool namedKeyMechanism,
        bool reachedThisPupil,
        string containedFalseClaim)
    {
        AddressedMisconception = addressedMisconception;
        NamedKeyMechanism = namedKeyMechanism;
        ReachedThisPupil = reachedThisPupil;
        ContainedFalseClaim = containedFalseClaim ?? string.Empty;
    }

    public bool AddressedMisconception { get; }
    public bool NamedKeyMechanism { get; }
    public bool ReachedThisPupil { get; }
    public string ContainedFalseClaim { get; }
}

public sealed class FleeApiFailure
{
    public FleeApiFailure(long statusCode, string message, int retryAfterSeconds = 0)
    {
        StatusCode = statusCode;
        Message = string.IsNullOrWhiteSpace(message) ? "The classroom service did not respond." : message;
        RetryAfterSeconds = retryAfterSeconds;
    }

    public long StatusCode { get; }
    public string Message { get; }
    public int RetryAfterSeconds { get; }

    public string ToDialogueLine()
    {
        switch (StatusCode)
        {
            case 401:
                return "I couldn't connect to our science classroom. The client token is missing or incorrect.";
            case 429:
                return "Sorry, I got distracted for a moment. Could you say that again?";
            case 502:
            case 503:
                return "I couldn't think that through just now. Can we try again in a moment?";
            case 422:
                return "I didn't catch an explanation. Could you say it again?";
            case 409:
                return "I don't have another question right now.";
            default:
                return "I couldn't reach the science classroom just now. Can we try again in a moment?";
        }
    }
}

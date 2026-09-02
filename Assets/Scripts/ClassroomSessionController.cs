using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class ClassroomSessionController : MonoBehaviour
{
    [Serializable]
    private sealed class PupilPrefabBinding
    {
        public string pupilName;
        public GameObject prefab;
    }

    [Header("Pupil Prefabs")]
    [SerializeField] private PupilPrefabBinding[] pupilPrefabs = Array.Empty<PupilPrefabBinding>();

    [Header("Classroom Layout")]
    [Tooltip("Fallback chair centers used when the scene does not contain ArmchairSeat components.")]
    [SerializeField] private Vector2[] spawnPositions =
    {
        new Vector2(-3.8f, 1.65f),
        new Vector2(-1.35f, 1.65f),
        new Vector2(1.35f, 1.65f),
        new Vector2(3.8f, 1.65f),
        new Vector2(-3.8f, -0.6f),
        new Vector2(-1.35f, -0.6f),
        new Vector2(1.35f, -0.6f),
        new Vector2(3.8f, -0.6f)
    };
    [SerializeField] private bool randomizeBesideSpawnAnchors;
    [SerializeField, Min(0f)] private float minimumSpawnDistance = 1.75f;
    [SerializeField, Min(0f)] private float maximumSpawnDistance = 2.35f;
    [SerializeField, Min(0f)] private float verticalSpawnJitter = 0.35f;
    [SerializeField, Min(0f)] private float navMeshSampleDistance = 1.25f;
    [SerializeField, Min(0f)] private float minimumPupilSpacing = 1.8f;
    [SerializeField, Min(1)] private int spawnPositionAttempts = 12;

    [Header("Seated Pupils")]
    [SerializeField] private bool keepPupilsSeated = true;
    [SerializeField] private Vector2 seatedVisualOffset = new Vector2(0f, 0.25f);

    [Header("Conversation Progress")]
    [SerializeField] private bool showConversationCounter = true;
    [SerializeField] private Sprite studentIconHead;
    [SerializeField] private Sprite studentIconBody;
    [SerializeField] private TMP_FontAsset progressFont;

    [Header("Teacher Evaluation")]
    [SerializeField] private bool transitionWhenAllStudentsTalkedTo = true;
    [SerializeField] private string teacherEvaluationSceneName = "Teacher Evaluation";
    [SerializeField] private string teacherEvaluationScenePath = "Assets/Scenes/Teacher Evaluation.unity";

    private const string LoadingTaskId = "classroom-session";
    private const string GameplayClassroomSceneName = "Classroom and Movement";
    private const string LegacyBackendPupilName = "Liam";
    private const string JairusPupilName = "Jairus";

    private FleeApiClient apiClient;
    private FleeClassroomSession classroom;
    private readonly HashSet<int> talkedToStudentIds = new HashSet<int>();
    private int spawnedStudentCount;
    private SessionProgressHud conversationHud;
    private bool isEvaluationTransitionQueued;

    public FleeClassroomSession Classroom => classroom;
    public int TalkedToStudentCount => talkedToStudentIds.Count;
    public int SpawnedStudentCount => spawnedStudentCount;

    public void SetConversationCounterVisible(bool visible)
    {
        showConversationCounter = visible;
        conversationHud?.SetVisible(visible);
    }

    private void OnEnable()
    {
        StudentDialogueInteraction.ConversationCompleted += HandleConversationCompleted;
    }

    private void OnDisable()
    {
        StudentDialogueInteraction.ConversationCompleted -= HandleConversationCompleted;
    }

    private IEnumerator Start()
    {
        conversationHud = SessionProgressHud.CreateConversation(
            transform,
            studentIconHead,
            studentIconBody,
            progressFont);
        conversationHud.SetVisible(false);

        if (string.Equals(
                SceneManager.GetActiveScene().name,
                GameplayClassroomSceneName,
                StringComparison.Ordinal))
        {
            FleeApiClient.ResetClassroomSession();
        }

        DoorSceneTransition.TryRegisterLoadingTask(
            LoadingTaskId,
            "Preparing the Classroom...",
            0f,
            2f);

        apiClient = FleeApiClient.GetOrCreate();
        FleeApiFailure failure = null;
        yield return apiClient.PrepareClassroom(
            value => classroom = NormalizeClassroom(value),
            error => failure = error,
            (progress, status) => DoorSceneTransition.UpdateLoadingTask(
                LoadingTaskId,
                progress,
                status));

        if (failure != null || classroom == null)
        {
            string message = failure != null ? failure.Message : "The Classroom could not be prepared.";
            Debug.LogError(message, this);
            DoorSceneTransition.CompleteLoadingTask(LoadingTaskId, "Classroom unavailable.");
            yield break;
        }

        SpawnPupils(classroom.Pupils);
        conversationHud.SetConversationCounts(TalkedToStudentCount, spawnedStudentCount);
        conversationHud.SetVisible(showConversationCounter && spawnedStudentCount > 0);
        DoorSceneTransition.CompleteLoadingTask(
            LoadingTaskId,
            classroom.Topic + " is ready.");
    }

    private void SpawnPupils(FleePupilSession[] pupils)
    {
        if (pupils == null || pupils.Length == 0)
        {
            return;
        }

        GameObject rosterRoot = new GameObject("Classroom Pupils");
        rosterRoot.transform.SetParent(transform, false);
        rosterRoot.SetActive(false);
        spawnedStudentCount = 0;
        talkedToStudentIds.Clear();
        List<Vector2> occupiedSpawnPositions = new List<Vector2>(pupils.Length);
        ArmchairSeat[] classroomSeats = keepPupilsSeated ? FindOrderedClassroomSeats() : Array.Empty<ArmchairSeat>();

        for (int index = 0; index < pupils.Length; index++)
        {
            FleePupilSession pupil = pupils[index];
            GameObject prefab = FindPupilPrefab(pupil != null ? pupil.Name : string.Empty);
            if (pupil == null || prefab == null)
            {
                Debug.LogWarning(
                    "No Unity Pupil prefab is assigned for " + (pupil != null ? pupil.Name : "an unknown Pupil") + ".",
                    this);
                continue;
            }

            ArmchairSeat assignedSeat = index < classroomSeats.Length ? classroomSeats[index] : null;
            Vector2 position = assignedSeat != null
                ? assignedSeat.WorldSeatPosition + seatedVisualOffset
                : GetSpawnPosition(index, prefab, occupiedSpawnPositions)
                    + (keepPupilsSeated ? seatedVisualOffset : Vector2.zero);

            occupiedSpawnPositions.Add(position);
            GameObject pupilObject = Instantiate(
                prefab,
                new Vector3(position.x, position.y, 0f),
                Quaternion.identity,
                rosterRoot.transform);
            pupilObject.name = pupil.Name;

            if (keepPupilsSeated)
            {
                ConfigureSeatedPupil(pupilObject);
                assignedSeat?.SeatStudent(pupilObject);
            }

            StudentDialogueInteraction interaction = pupilObject.GetComponentInChildren<StudentDialogueInteraction>(true);
            if (interaction != null)
            {
                interaction.ConfigureBackendPupil(pupil);
                spawnedStudentCount++;
            }
        }

        rosterRoot.SetActive(true);
    }

    private static ArmchairSeat[] FindOrderedClassroomSeats()
    {
#if UNITY_2023_1_OR_NEWER
        ArmchairSeat[] seats = FindObjectsByType<ArmchairSeat>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
#else
        ArmchairSeat[] seats = FindObjectsOfType<ArmchairSeat>();
#endif
        Array.Sort(seats, CompareSeatPositions);
        return seats;
    }

    private static int CompareSeatPositions(ArmchairSeat left, ArmchairSeat right)
    {
        float verticalDifference = right.transform.position.y - left.transform.position.y;
        if (Mathf.Abs(verticalDifference) > 0.01f)
        {
            return verticalDifference > 0f ? 1 : -1;
        }

        return left.transform.position.x.CompareTo(right.transform.position.x);
    }

    private static void ConfigureSeatedPupil(GameObject pupilObject)
    {
        StudentRoamingController[] roamingControllers =
            pupilObject.GetComponentsInChildren<StudentRoamingController>(true);
        for (int index = 0; index < roamingControllers.Length; index++)
        {
            roamingControllers[index].enabled = false;
        }

        Collider2D[] colliders = pupilObject.GetComponentsInChildren<Collider2D>(true);
        for (int index = 0; index < colliders.Length; index++)
        {
            if (!colliders[index].isTrigger)
            {
                colliders[index].enabled = false;
            }
        }
    }

    private void HandleConversationCompleted(DialogueActor studentActor)
    {
        if (studentActor == null
            || !studentActor.transform.IsChildOf(transform)
            || !talkedToStudentIds.Add(studentActor.GetInstanceID()))
        {
            return;
        }

        Debug.Log(
            "Students talked to: " + TalkedToStudentCount + " / " + spawnedStudentCount,
            this);
        conversationHud?.SetConversationCounts(TalkedToStudentCount, spawnedStudentCount);

        if (transitionWhenAllStudentsTalkedTo
            && !isEvaluationTransitionQueued
            && spawnedStudentCount > 0
            && TalkedToStudentCount >= spawnedStudentCount)
        {
            isEvaluationTransitionQueued = true;
            StartCoroutine(TransitionToTeacherEvaluationNextFrame());
        }
    }

    private IEnumerator TransitionToTeacherEvaluationNextFrame()
    {
        yield return null;
        DoorSceneTransition.LoadScene(teacherEvaluationSceneName, teacherEvaluationScenePath);
    }

    private GameObject FindPupilPrefab(string pupilName)
    {
        for (int index = 0; index < pupilPrefabs.Length; index++)
        {
            PupilPrefabBinding binding = pupilPrefabs[index];
            if (binding != null
                && binding.prefab != null
                && string.Equals(binding.pupilName, pupilName, StringComparison.OrdinalIgnoreCase))
            {
                return binding.prefab;
            }
        }

        return null;
    }

    private static FleeClassroomSession NormalizeClassroom(FleeClassroomSession source)
    {
        if (source == null)
        {
            return null;
        }

        FleePupilSession[] pupils = new FleePupilSession[source.Pupils.Length];
        for (int index = 0; index < source.Pupils.Length; index++)
        {
            FleePupilSession pupil = source.Pupils[index];
            pupils[index] = pupil != null
                && string.Equals(pupil.Name, LegacyBackendPupilName, StringComparison.OrdinalIgnoreCase)
                ? new FleePupilSession(
                    pupil.PupilId,
                    JairusPupilName,
                    pupil.Personality,
                    pupil.Quirk,
                    pupil.Voice,
                    pupil.Misconception,
                    pupil.TurnBudget,
                    pupil.TurnsUsed,
                    pupil.Satisfied,
                    pupil.LearnedAnswer,
                    pupil.LastScore)
                : pupil;
        }

        return new FleeClassroomSession(
            source.ClassroomId,
            source.Topic,
            source.RescueQuota,
            pupils);
    }

    private Vector2 GetSpawnPosition(
        int index,
        GameObject pupilPrefab,
        IReadOnlyList<Vector2> occupiedPositions)
    {
        if (spawnPositions == null || spawnPositions.Length == 0)
        {
            return Vector2.zero;
        }

        Vector2 anchor = spawnPositions[Mathf.Clamp(index, 0, spawnPositions.Length - 1)];
        if (!randomizeBesideSpawnAnchors)
        {
            return anchor;
        }

        Vector2 navigationOffset = GetPrefabNavigationOffset(pupilPrefab);
        float minimumDistance = Mathf.Min(minimumSpawnDistance, maximumSpawnDistance);
        float maximumDistance = Mathf.Max(minimumSpawnDistance, maximumSpawnDistance);
        Vector2 fallbackPosition = anchor;

        for (int attempt = 0; attempt < spawnPositionAttempts; attempt++)
        {
            float side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            float horizontalDistance = UnityEngine.Random.Range(minimumDistance, maximumDistance);
            float verticalOffset = UnityEngine.Random.Range(-verticalSpawnJitter, verticalSpawnJitter);
            Vector2 candidate = anchor + new Vector2(side * horizontalDistance, verticalOffset);
            Vector2 sampledPosition = SampleNavMeshPosition(candidate, navigationOffset);
            fallbackPosition = sampledPosition;

            if (HasMinimumSpacing(sampledPosition, occupiedPositions))
            {
                return sampledPosition;
            }
        }

        return fallbackPosition;
    }

    private Vector2 SampleNavMeshPosition(Vector2 candidate, Vector2 navigationOffset)
    {
        if (navMeshSampleDistance <= 0f)
        {
            return candidate;
        }

        Vector2 navigationCandidate = candidate + navigationOffset;
        Vector3 navMeshCandidate = new Vector3(navigationCandidate.x, 0f, navigationCandidate.y);
        return NavMesh.SamplePosition(
            navMeshCandidate,
            out NavMeshHit sampledPosition,
            navMeshSampleDistance,
            NavMesh.AllAreas)
            ? new Vector2(sampledPosition.position.x, sampledPosition.position.z) - navigationOffset
            : candidate;
    }

    private static Vector2 GetPrefabNavigationOffset(GameObject pupilPrefab)
    {
        if (pupilPrefab == null)
        {
            return Vector2.zero;
        }

        Collider2D[] colliders = pupilPrefab.GetComponents<Collider2D>();
        for (int index = 0; index < colliders.Length; index++)
        {
            Collider2D collider = colliders[index];
            if (collider == null || !collider.enabled || collider.isTrigger)
            {
                continue;
            }

            Vector2 colliderCenter = collider.transform.TransformPoint(collider.offset);
            return colliderCenter - (Vector2)pupilPrefab.transform.position;
        }

        return Vector2.zero;
    }

    private bool HasMinimumSpacing(Vector2 candidate, IReadOnlyList<Vector2> occupiedPositions)
    {
        if (occupiedPositions == null || minimumPupilSpacing <= 0f)
        {
            return true;
        }

        float minimumSpacingSquared = minimumPupilSpacing * minimumPupilSpacing;
        for (int index = 0; index < occupiedPositions.Count; index++)
        {
            if ((candidate - occupiedPositions[index]).sqrMagnitude < minimumSpacingSquared)
            {
                return false;
            }
        }

        return true;
    }

    private void OnValidate()
    {
        minimumSpawnDistance = Mathf.Max(0f, minimumSpawnDistance);
        maximumSpawnDistance = Mathf.Max(minimumSpawnDistance, maximumSpawnDistance);
        verticalSpawnJitter = Mathf.Max(0f, verticalSpawnJitter);
        navMeshSampleDistance = Mathf.Max(0f, navMeshSampleDistance);
        minimumPupilSpacing = Mathf.Max(0f, minimumPupilSpacing);
        spawnPositionAttempts = Mathf.Max(1, spawnPositionAttempts);
    }

    private void OnDrawGizmosSelected()
    {
        if (spawnPositions == null)
        {
            return;
        }

        Gizmos.color = new Color(0.2f, 0.9f, 0.65f, 0.9f);
        float minimumDistance = Mathf.Min(minimumSpawnDistance, maximumSpawnDistance);
        float maximumDistance = Mathf.Max(minimumSpawnDistance, maximumSpawnDistance);

        for (int index = 0; index < spawnPositions.Length; index++)
        {
            Vector3 anchor = new Vector3(spawnPositions[index].x, spawnPositions[index].y, transform.position.z);
            Gizmos.DrawWireSphere(anchor, 0.16f);
            Gizmos.DrawLine(anchor + Vector3.left * minimumDistance, anchor + Vector3.left * maximumDistance);
            Gizmos.DrawLine(anchor + Vector3.right * minimumDistance, anchor + Vector3.right * maximumDistance);
        }
    }
}

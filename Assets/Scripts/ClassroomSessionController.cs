using System;
using System.Collections;
using UnityEngine;

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
    [SerializeField] private Vector2[] spawnPositions =
    {
        new Vector2(-3.8f, 1.65f),
        new Vector2(-1.35f, 1.45f),
        new Vector2(1.35f, 1.45f),
        new Vector2(3.8f, 1.65f),
        new Vector2(-2.35f, -0.6f),
        new Vector2(2.35f, -0.6f)
    };

    private const string LoadingTaskId = "classroom-session";
    private const string LegacyBackendPupilName = "Liam";
    private const string JairusPupilName = "Jairus";

    private FleeApiClient apiClient;
    private FleeClassroomSession classroom;

    public FleeClassroomSession Classroom => classroom;

    private IEnumerator Start()
    {
        DoorSceneTransition.TryRegisterLoadingTask(
            LoadingTaskId,
            "Preparing the Classroom...",
            0f,
            2f);

        apiClient = FleeApiClient.GetOrCreate();
        FleeApiFailure failure = null;
        yield return apiClient.PreparePresetClassroom(
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

            Vector2 position = GetSpawnPosition(index);
            GameObject pupilObject = Instantiate(
                prefab,
                new Vector3(position.x, position.y, 0f),
                Quaternion.identity,
                rosterRoot.transform);
            pupilObject.name = pupil.Name;

            StudentDialogueInteraction interaction = pupilObject.GetComponentInChildren<StudentDialogueInteraction>(true);
            if (interaction != null)
            {
                interaction.ConfigureBackendPupil(pupil);
            }
        }

        rosterRoot.SetActive(true);
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
                    pupil.Satisfied)
                : pupil;
        }

        return new FleeClassroomSession(
            source.ClassroomId,
            source.Topic,
            source.RescueQuota,
            pupils);
    }

    private Vector2 GetSpawnPosition(int index)
    {
        if (spawnPositions == null || spawnPositions.Length == 0)
        {
            return Vector2.zero;
        }

        return spawnPositions[Mathf.Clamp(index, 0, spawnPositions.Length - 1)];
    }
}

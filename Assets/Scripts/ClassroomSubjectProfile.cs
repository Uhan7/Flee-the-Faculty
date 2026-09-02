using UnityEngine;

[DisallowMultipleComponent]
public sealed class ClassroomSubjectProfile : MonoBehaviour
{
    [SerializeField] private bool generateFromTopic;
    [SerializeField] private string topic = "photosynthesis";
    [SerializeField, Range(1, 12)] private int gradeLevel = 5;
    [SerializeField] private string preparedPresetId = "photosynthesis";

    private void Awake()
    {
        FleeApiClient.GetOrCreate().ConfigureClassroomSource(
            generateFromTopic,
            topic,
            gradeLevel,
            preparedPresetId);
    }
}

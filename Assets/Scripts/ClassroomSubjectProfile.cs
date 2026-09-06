using UnityEngine;

[DisallowMultipleComponent]
public sealed class ClassroomSubjectProfile : MonoBehaviour
{
    [SerializeField] private bool generateFromTopic;
    [SerializeField] private string topic = "scientific investigation";
    [SerializeField, Range(1, 12)] private int gradeLevel = 5;
    [SerializeField] private string preparedPresetId = "scientific-investigation";

    [Tooltip("What the Classroom is played in. Independent of the language of "
        + "the material, so a Filipino worksheet can build an English Classroom. "
        + "A prepared preset keeps the language it was written in.")]
    [SerializeField] private FleeClassroomLanguage language = FleeClassroomLanguage.English;

    [Tooltip("A note for whoever builds the Classroom: a topic to leave out, a "
        + "term her class uses, an angle to weight. Up to 500 characters. It "
        + "changes what the Classroom is about, never how a turn is judged.")]
    [SerializeField, TextArea(2, 4)] private string additionalNotes = string.Empty;

    private void Awake()
    {
        FleeApiClient client = FleeApiClient.GetOrCreate();
        client.ConfigureClassroomSource(
            generateFromTopic,
            topic,
            gradeLevel,
            preparedPresetId);
        client.ConfigureClassroomLanguage(language, additionalNotes);
    }
}

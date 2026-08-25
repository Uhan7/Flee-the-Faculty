using UnityEngine;

[DisallowMultipleComponent]
public sealed class DialogueActor : MonoBehaviour
{
    [SerializeField] private string displayName = "Speaker";

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? gameObject.name : displayName;

    public bool TryGetStudentPersonality(out StudentPersonality personality)
    {
        personality = GetComponent<StudentPersonality>();
        return personality != null;
    }

    public string GetStudentPromptContext()
    {
        StudentPersonality attachedPersonality = GetComponent<StudentPersonality>();
        if (attachedPersonality != null)
        {
            return attachedPersonality.BuildPromptContext();
        }

        return string.Empty;
    }
}

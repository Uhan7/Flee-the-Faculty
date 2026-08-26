using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class StudentPersonality : MonoBehaviour
{
    [SerializeField] private DialogueActor actor;
    [SerializeField, TextArea(2, 5)] private string explanationNeed;
    [SerializeField, TextArea(2, 6)] private string quirk;

    [Header("Character Voice")]
    [SerializeField, TextArea(1, 3)] private string generalVibe;
    [SerializeField, TextArea(1, 3)] private string speechStyle;
    [SerializeField, TextArea(2, 4)] private string commonPhrases;
    [SerializeField, TextArea(1, 3)] private string confusionLine;
    [SerializeField, TextArea(1, 3)] private string wrongAnswerLine;
    [SerializeField, TextArea(1, 3)] private string understandingLine;
    [SerializeField, TextArea(1, 3)] private string preferredExplanationStyle;
    [SerializeField, TextArea(1, 3)] private string recognizableHabit;

    public DialogueActor Actor => actor != null ? actor : GetComponent<DialogueActor>();
    public string StudentName => Actor != null && !string.IsNullOrWhiteSpace(Actor.DisplayName) ? Actor.DisplayName : gameObject.name;
    public string ExplanationNeed => explanationNeed ?? string.Empty;
    public string Quirk => quirk ?? string.Empty;
    public string GeneralVibe => generalVibe ?? string.Empty;
    public string SpeechStyle => speechStyle ?? string.Empty;
    public string CommonPhrases => commonPhrases ?? string.Empty;
    public string ConfusionLine => confusionLine ?? string.Empty;
    public string WrongAnswerLine => wrongAnswerLine ?? string.Empty;
    public string UnderstandingLine => understandingLine ?? string.Empty;
    public string PreferredExplanationStyle => preferredExplanationStyle ?? string.Empty;
    public string RecognizableHabit => recognizableHabit ?? string.Empty;
    public string BuildPromptContext()
    {
        StringBuilder context = new StringBuilder(StudentName);
        AppendPromptDetail(context, "Explanation need", ExplanationNeed);
        AppendPromptDetail(context, "Quirk", Quirk);
        AppendPromptDetail(context, "General vibe", GeneralVibe);
        AppendPromptDetail(context, "Speech style", SpeechStyle);
        AppendPromptDetail(context, "Common phrases", CommonPhrases);
        AppendPromptDetail(context, "When confused", ConfusionLine);
        AppendPromptDetail(context, "When AraBOT is wrong", WrongAnswerLine);
        AppendPromptDetail(context, "When they understand", UnderstandingLine);
        AppendPromptDetail(context, "Preferred explanation style", PreferredExplanationStyle);
        AppendPromptDetail(context, "Recognizable habit", RecognizableHabit);
        return context.ToString();
    }

    private void Reset()
    {
        actor = GetComponent<DialogueActor>();
    }

    private void OnValidate()
    {
        if (actor == null)
        {
            actor = GetComponent<DialogueActor>();
        }
    }

    private static void AppendPromptDetail(StringBuilder context, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            context.Append($". {label}: {value.Trim()}");
        }
    }
}

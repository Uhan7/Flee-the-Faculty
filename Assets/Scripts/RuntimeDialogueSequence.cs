using System.Collections.Generic;
using UnityEngine;

public sealed class RuntimeDialogueSequence : IDialogueSequence
{
    private readonly List<IDialogueLine> lines;

    public RuntimeDialogueSequence(string conversationId, IEnumerable<IDialogueLine> sourceLines)
    {
        ConversationId = conversationId ?? string.Empty;
        lines = sourceLines != null ? new List<IDialogueLine>(sourceLines) : new List<IDialogueLine>();
    }

    public string ConversationId { get; }
    public bool HasLines => lines.Count > 0;
    public IReadOnlyList<IDialogueLine> Lines => lines;
}

public sealed class RuntimeDialogueLine : IDialogueLine
{
    public RuntimeDialogueLine(Object speakerReference, string speakerName, string text)
    {
        SpeakerReference = speakerReference;
        SpeakerName = speakerName ?? string.Empty;
        Text = text ?? string.Empty;
    }

    public Object SpeakerReference { get; }
    public string SpeakerName { get; }
    public string Text { get; }
}

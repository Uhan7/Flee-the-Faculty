using System.Collections.Generic;
using UnityEngine;

public interface IDialogueLine
{
    Object SpeakerReference { get; }
    string SpeakerName { get; }
    string Text { get; }
}

public interface IDialogueSequence
{
    string ConversationId { get; }
    bool HasLines { get; }
    IReadOnlyList<IDialogueLine> Lines { get; }
}

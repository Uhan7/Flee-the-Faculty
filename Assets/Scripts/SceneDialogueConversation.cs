using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SceneDialogueConversation : MonoBehaviour, IDialogueSequence
{
    [SerializeField] private string conversationId;
    [SerializeField] private List<SceneDialogueLine> lines = new List<SceneDialogueLine>();

    public string ConversationId => conversationId;
    public IReadOnlyList<SceneDialogueLine> Lines => lines;
    public bool HasLines => lines != null && lines.Count > 0;

    IReadOnlyList<IDialogueLine> IDialogueSequence.Lines => lines;
}

[Serializable]
public sealed class SceneDialogueLine : IDialogueLine
{
    [SerializeField] private DialogueActor speaker;
    [SerializeField, TextArea(2, 8)] private string text;

    public UnityEngine.Object SpeakerReference => speaker;
    public string SpeakerName => speaker != null ? speaker.DisplayName : string.Empty;
    public string Text => text ?? string.Empty;
}

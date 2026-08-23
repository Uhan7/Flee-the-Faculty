using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Dialogue", menuName = "Dialogue/Conversation")]
public sealed class Dialogue : ScriptableObject
{
    [SerializeField] private string conversationId;
    [SerializeField] private List<DialogueLine> lines = new List<DialogueLine>();

    public string ConversationId => conversationId;
    public IReadOnlyList<DialogueLine> Lines => lines;
    public bool HasLines => lines != null && lines.Count > 0;
}

[Serializable]
public sealed class DialogueLine
{
    [SerializeField] private string speaker = "Speaker";
    [SerializeField, TextArea(2, 8)] private string text;
    [SerializeField] private Sprite portrait;
    [SerializeField] private AudioClip voiceClip;
    [SerializeField, Tooltip("Use -1 to use the manager's default typing speed.")]
    private float charactersPerSecond = -1f;

    public string Speaker => speaker;
    public string Text => text ?? string.Empty;
    public Sprite Portrait => portrait;
    public AudioClip VoiceClip => voiceClip;
    public float CharactersPerSecond => charactersPerSecond;
}

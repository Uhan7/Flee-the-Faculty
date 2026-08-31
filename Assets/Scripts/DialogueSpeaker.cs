using UnityEngine;

[CreateAssetMenu(fileName = "New Dialogue Speaker", menuName = "Dialogue/Speaker")]
public sealed class DialogueSpeaker : ScriptableObject, IVoicedSpeaker
{
    [SerializeField] private string displayName = "Speaker";

    [Tooltip("Which recorded voice this Character speaks in. cast.py in the service decides which side of the split they are on.")]
    [SerializeField] private VoiceId voice = VoiceId.None;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

    public VoiceId Voice => voice;

    /// <summary>
    /// Always empty. A speaker asset is authored dialogue, and authored dialogue
    /// is baked in the base voice rather than in one of the six slots. Only a
    /// Pupil in a live Encounter carries a slot; see <c>DialogueActor</c>.
    /// </summary>
    public string VoiceSlot => string.Empty;
}

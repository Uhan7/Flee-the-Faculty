using UnityEngine;

[CreateAssetMenu(fileName = "New Dialogue Speaker", menuName = "Dialogue/Speaker")]
public sealed class DialogueSpeaker : ScriptableObject
{
    [SerializeField] private string displayName = "Speaker";

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
}

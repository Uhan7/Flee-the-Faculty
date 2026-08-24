using UnityEngine;

[DisallowMultipleComponent]
public sealed class DialogueActor : MonoBehaviour
{
    [SerializeField] private string displayName = "Speaker";

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? gameObject.name : displayName;
}

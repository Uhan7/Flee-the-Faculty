using UnityEngine;

[CreateAssetMenu(fileName = "Button Juice Audio", menuName = "UI/Button Juice Audio")]
public sealed class ButtonJuiceAudioLibrary : ScriptableObject
{
    [SerializeField] private AudioClip hoverClip;
    [SerializeField] private AudioClip clickClip;

    public AudioClip HoverClip => hoverClip;
    public AudioClip ClickClip => clickClip;
}

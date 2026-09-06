using UnityEngine;

[CreateAssetMenu(fileName = "Dialogue UI Icons", menuName = "Dialogue/UI Icon Library")]
public sealed class DialogueUiIconLibrary : ScriptableObject
{
    private const string ResourcePath = "Dialogue UI Icons";

    [SerializeField] private Sprite backIcon;
    [SerializeField] private Sprite pauseIcon;
    [SerializeField] private Sprite exitIcon;

    private static DialogueUiIconLibrary instance;

    public Sprite BackIcon => backIcon;
    public Sprite PauseIcon => pauseIcon;
    public Sprite ExitIcon => exitIcon;

    public static DialogueUiIconLibrary Load()
    {
        if (instance == null)
        {
            instance = Resources.Load<DialogueUiIconLibrary>(ResourcePath);
        }

        return instance;
    }
}

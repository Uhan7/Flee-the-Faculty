using UnityEngine;

[DisallowMultipleComponent]
public sealed class DoorSceneTransitionBootstrap : MonoBehaviour
{
    private void Awake()
    {
        DoorSceneTransition.EnsureExists();
    }

    public void TransitionToConfiguredScene()
    {
        DoorSceneTransition.LoadConfiguredScene();
    }
}

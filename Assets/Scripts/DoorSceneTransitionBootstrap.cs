using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public sealed class DoorSceneTransitionBootstrap : MonoBehaviour
{
    [Header("Destination")]
#if UNITY_EDITOR
    [SerializeField] private SceneAsset targetSceneAsset;
#endif
    [SerializeField, HideInInspector] private string targetSceneName;
    [SerializeField, HideInInspector] private string targetScenePath;

    private void Awake()
    {
        DoorSceneTransition.EnsureExists();
    }

    public void TransitionToConfiguredScene()
    {
        if (string.IsNullOrWhiteSpace(targetSceneName))
        {
            Debug.LogWarning("No target scene is assigned to the door scene transition controller.", this);
            return;
        }

        DoorSceneTransition.LoadScene(targetSceneName, targetScenePath);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        SyncTargetSceneMetadata();
    }

    private void Reset()
    {
        SyncTargetSceneMetadata();
    }

    private void SyncTargetSceneMetadata()
    {
        if (targetSceneAsset == null)
        {
            targetSceneName = string.Empty;
            targetScenePath = string.Empty;
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(targetSceneAsset);
        targetScenePath = assetPath;
        targetSceneName = string.IsNullOrWhiteSpace(assetPath)
            ? string.Empty
            : Path.GetFileNameWithoutExtension(assetPath);
    }
#endif
}

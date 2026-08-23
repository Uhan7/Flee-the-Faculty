using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(SortingGroup))]
public sealed class YSortByPosition : MonoBehaviour
{
    [Tooltip("Usually a character's feet or the bottom-center of a decoration.")]
    [SerializeField] private Transform sortPoint;
    [Tooltip("Local adjustment from the selected sort point, such as (0, -0.5) for a sprite's bottom edge.")]
    [SerializeField] private Vector2 localSortPointOffset;
    [Tooltip("Fine adjustment after converting world Y into a sorting order.")]
    [SerializeField] private int orderOffset;
    [Tooltip("Higher values allow more precise sorting between nearby objects.")]
    [SerializeField, Min(1f)] private float ordersPerWorldUnit = 100f;
    [Tooltip("Disable for decorations that never move after spawning.")]
    [SerializeField] private bool updateEveryFrame = true;

    private SortingGroup sortingGroup;
    private int lastSortingOrder = int.MinValue;

    private void OnEnable()
    {
        sortingGroup = GetComponent<SortingGroup>();
        RefreshSorting();
    }

    private void LateUpdate()
    {
        if (updateEveryFrame || !Application.isPlaying)
        {
            RefreshSorting();
        }
    }

    private void OnValidate()
    {
        ordersPerWorldUnit = Mathf.Max(1f, ordersPerWorldUnit);
        sortingGroup = GetComponent<SortingGroup>();
        RefreshSorting();
    }

    public void RefreshSorting()
    {
        if (sortingGroup == null)
        {
            return;
        }

        Transform positionSource = sortPoint != null ? sortPoint : transform;
        Vector3 sortPosition = positionSource.TransformPoint(localSortPointOffset);
        int sortingOrder = orderOffset - Mathf.RoundToInt(sortPosition.y * ordersPerWorldUnit);
        sortingOrder = Mathf.Clamp(sortingOrder, short.MinValue, short.MaxValue);

        if (sortingOrder == lastSortingOrder)
        {
            return;
        }

        sortingGroup.sortingOrder = sortingOrder;
        lastSortingOrder = sortingOrder;
    }

    private void OnDrawGizmosSelected()
    {
        Transform positionSource = sortPoint != null ? sortPoint : transform;
        Vector3 sortPosition = positionSource.TransformPoint(localSortPointOffset);
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(sortPosition + Vector3.left * 0.15f, sortPosition + Vector3.right * 0.15f);
    }
}

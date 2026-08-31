using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class ArmchairSeat : MonoBehaviour
{
    [Header("Seat Placement")]
    [SerializeField] private Vector2 localStudentOffset;

    [Header("Separated Chair Art")]
    [SerializeField] private SpriteRenderer chairBack;
    [SerializeField] private SpriteRenderer deskFront;

    [Header("Layering")]
    [SerializeField, Min(1)] private int ordersPerWorldUnit = 100;
    [SerializeField] private int chairBackOrderOffset;
    [SerializeField] private int studentBodyOrderOffset = 10;
    [SerializeField] private int deskFrontOrderOffset = 20;
    [SerializeField] private int studentHeadOrderOffset = 30;
    [SerializeField] private int studentUiOrderOffset = 40;

    public Vector2 WorldSeatPosition =>
        transform.TransformPoint(new Vector3(localStudentOffset.x, localStudentOffset.y, 0f));

    private void Awake()
    {
        ApplyChairLayers();
    }

    public void SeatStudent(GameObject student)
    {
        if (student == null)
        {
            return;
        }

        ApplyChairLayers();

        SortingGroup studentRootGroup = student.GetComponent<SortingGroup>();
        if (studentRootGroup != null)
        {
            studentRootGroup.enabled = false;
        }

        YSortByPosition studentYSort = student.GetComponent<YSortByPosition>();
        if (studentYSort != null)
        {
            studentYSort.enabled = false;
        }

        int baseOrder = GetBaseOrder();
        int sortingLayerId = ResolveSortingLayerId();
        ConfigureStudentSection(
            student.transform.Find("Torso"),
            sortingLayerId,
            baseOrder + studentBodyOrderOffset);
        ConfigureStudentSection(
            student.transform.Find("Head"),
            sortingLayerId,
            baseOrder + studentHeadOrderOffset);

        Canvas[] canvases = student.GetComponentsInChildren<Canvas>(true);
        for (int index = 0; index < canvases.Length; index++)
        {
            canvases[index].overrideSorting = true;
            canvases[index].sortingLayerID = sortingLayerId;
            canvases[index].sortingOrder = baseOrder + studentUiOrderOffset;
        }
    }

    private void ApplyChairLayers()
    {
        ResolveReferences();

        SortingGroup chairGroup = GetComponent<SortingGroup>();
        if (chairGroup != null)
        {
            chairGroup.enabled = false;
        }

        YSortByPosition chairYSort = GetComponent<YSortByPosition>();
        if (chairYSort != null)
        {
            chairYSort.enabled = false;
        }

        int baseOrder = GetBaseOrder();
        int sortingLayerId = ResolveSortingLayerId();
        ConfigureRenderer(chairBack, sortingLayerId, baseOrder + chairBackOrderOffset);
        ConfigureRenderer(deskFront, sortingLayerId, baseOrder + deskFrontOrderOffset);
    }

    private int GetBaseOrder()
    {
        return -Mathf.RoundToInt(transform.position.y * ordersPerWorldUnit);
    }

    private int ResolveSortingLayerId()
    {
        if (chairBack != null)
        {
            return chairBack.sortingLayerID;
        }

        return deskFront != null ? deskFront.sortingLayerID : 0;
    }

    private static void ConfigureStudentSection(
        Transform section,
        int sortingLayerId,
        int sortingOrder)
    {
        if (section == null)
        {
            return;
        }

        SortingGroup sectionGroup = section.GetComponent<SortingGroup>();
        if (sectionGroup == null)
        {
            sectionGroup = section.gameObject.AddComponent<SortingGroup>();
        }

        sectionGroup.sortingLayerID = sortingLayerId;
        sectionGroup.sortingOrder = sortingOrder;
        sectionGroup.enabled = true;
    }

    private static void ConfigureRenderer(
        SpriteRenderer spriteRenderer,
        int sortingLayerId,
        int sortingOrder)
    {
        if (spriteRenderer == null)
        {
            return;
        }

        spriteRenderer.sortingLayerID = sortingLayerId;
        spriteRenderer.sortingOrder = sortingOrder;
    }

    private void ResolveReferences()
    {
        if (chairBack == null)
        {
            Transform chairTransform = transform.Find("Chair");
            chairBack = chairTransform != null ? chairTransform.GetComponent<SpriteRenderer>() : null;
        }

        if (deskFront == null)
        {
            Transform tableTransform = transform.Find("Table");
            deskFront = tableTransform != null ? tableTransform.GetComponent<SpriteRenderer>() : null;
        }
    }
}

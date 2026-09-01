using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class ClassroomDoorExitSequence : MonoBehaviour
{
    [Header("Door References")]
    [SerializeField] private Transform leftDoor;
    [SerializeField] private Transform rightDoor;
    [SerializeField] private SpriteRenderer leftDoorRenderer;
    [SerializeField] private SpriteRenderer rightDoorRenderer;
    [Tooltip("The wall or ceiling that should hide a student after they cross the doorway.")]
    [SerializeField] private SpriteRenderer doorwayForegroundRenderer;

    [Header("Exit Route")]
    [Tooltip("World-space offset from the doorway center where the student waits for it to open.")]
    [SerializeField] private Vector2 approachOffset = new Vector2(0f, -2.2f);
    [Tooltip("World-space offset from the doorway center where the student disappears beyond the wall.")]
    [SerializeField] private Vector2 beyondDoorOffset = new Vector2(0f, 2.4f);
    [SerializeField, Min(0.1f)] private float navMeshSampleDistance = 2.5f;
    [SerializeField, Min(0.1f)] private float studentMoveSpeed = 2.4f;
    [SerializeField, Min(0.1f)] private float studentAcceleration = 8f;
    [SerializeField, Min(0.001f)] private float waypointReachDistance = 0.03f;

    [Header("Door Animation")]
    [SerializeField, Range(0.01f, 1f)] private float openScaleX = 0.06f;
    [SerializeField, Min(0.01f)] private float openDuration = 0.28f;
    [SerializeField, Min(0f)] private float openHoldDuration = 0.08f;
    [SerializeField, Min(0.01f)] private float closeDuration = 0.24f;
    [SerializeField] private string foregroundSortingLayer = "Decor Front";
    [SerializeField] private int foregroundSortingOrder = 20;

    private Vector3 leftClosedScale;
    private Vector3 rightClosedScale;
    private Vector3 leftClosedPosition;
    private Vector3 rightClosedPosition;
    private Vector3 leftHingeWorldPosition;
    private Vector3 rightHingeWorldPosition;
    private RendererSortState leftClosedSort;
    private RendererSortState rightClosedSort;
    private RendererSortState doorwayForegroundClosedSort;
    private bool hasCapturedClosedState;

    public static ClassroomDoorExitSequence FindOrCreate()
    {
#if UNITY_2023_1_OR_NEWER
        ClassroomDoorExitSequence existing = FindFirstObjectByType<ClassroomDoorExitSequence>();
#else
        ClassroomDoorExitSequence existing = FindObjectOfType<ClassroomDoorExitSequence>();
#endif
        if (existing != null)
        {
            return existing;
        }

        Transform doorRoot = FindDoorRoot();
        return doorRoot != null
            ? doorRoot.gameObject.AddComponent<ClassroomDoorExitSequence>()
            : null;
    }

    private void Awake()
    {
        ResolveReferences();
        CaptureClosedState();
    }

    private void OnValidate()
    {
        navMeshSampleDistance = Mathf.Max(0.1f, navMeshSampleDistance);
        studentMoveSpeed = Mathf.Max(0.1f, studentMoveSpeed);
        studentAcceleration = Mathf.Max(0.1f, studentAcceleration);
        waypointReachDistance = Mathf.Max(0.001f, waypointReachDistance);
        openDuration = Mathf.Max(0.01f, openDuration);
        closeDuration = Mathf.Max(0.01f, closeDuration);
        ResolveReferences();
    }

    private void OnDrawGizmosSelected()
    {
        ResolveReferences();
        if (leftDoor == null || rightDoor == null)
        {
            return;
        }

        Vector2 doorwayCenter = GetDoorwayCenter();
        Vector2 approachPosition = doorwayCenter + approachOffset;
        Vector2 beyondPosition = doorwayCenter + beyondDoorOffset;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(approachPosition, beyondPosition);
        Gizmos.DrawWireSphere(approachPosition, 0.18f);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(beyondPosition, 0.18f);
    }

    public IEnumerator PlayStudentExit(GameObject student)
    {
        if (student == null)
        {
            yield break;
        }

        ResolveReferences();
        CaptureClosedState();
        if (leftDoor == null || rightDoor == null)
        {
            Debug.LogWarning("The classroom exit sequence could not find both door panels.", this);
            student.SetActive(false);
            yield break;
        }

        PrepareStudentForExit(student);
        SetDoorPanelsInFront();

        Vector2 doorwayCenter = GetDoorwayCenter();
        Vector2 approachPosition = doorwayCenter + approachOffset;
        Vector2 beyondPosition = doorwayCenter + beyondDoorOffset;
        List<Vector2> approachRoute = BuildApproachRoute(student.transform.position, approachPosition);

        yield return MoveStudentAlongRoute(student.transform, approachRoute);
        yield return AnimateDoorScale(1f, openScaleX, openDuration);

        if (openHoldDuration > 0f)
        {
            yield return WaitForSeconds(openHoldDuration);
        }

        yield return MoveStudentAlongRoute(
            student.transform,
            new List<Vector2> { beyondPosition });
        yield return AnimateDoorScale(openScaleX, 1f, closeDuration);

        RestoreDoorSorting();
        student.SetActive(false);
    }

    private List<Vector2> BuildApproachRoute(Vector2 start, Vector2 destination)
    {
        List<Vector2> route = new List<Vector2>();
        NavMeshPath path = new NavMeshPath();
        bool foundStart = NavMesh.SamplePosition(
            ToNavMeshPosition(start),
            out NavMeshHit sampledStart,
            navMeshSampleDistance,
            NavMesh.AllAreas);
        bool foundDestination = NavMesh.SamplePosition(
            ToNavMeshPosition(destination),
            out NavMeshHit sampledDestination,
            navMeshSampleDistance,
            NavMesh.AllAreas);

        if (foundStart
            && foundDestination
            && NavMesh.CalculatePath(sampledStart.position, sampledDestination.position, NavMesh.AllAreas, path)
            && path.status != NavMeshPathStatus.PathInvalid)
        {
            AddWaypointIfDistinct(route, ToWorldPosition(sampledStart.position));
            Vector3[] corners = path.corners;
            for (int index = 1; index < corners.Length; index++)
            {
                AddWaypointIfDistinct(route, ToWorldPosition(corners[index]));
            }
        }

        AddWaypointIfDistinct(route, destination);
        return route;
    }

    private IEnumerator MoveStudentAlongRoute(Transform student, IReadOnlyList<Vector2> route)
    {
        float currentSpeed = 0f;
        for (int waypointIndex = 0; waypointIndex < route.Count; waypointIndex++)
        {
            Vector2 waypoint = route[waypointIndex];
            while (((Vector2)student.position - waypoint).sqrMagnitude
                > waypointReachDistance * waypointReachDistance)
            {
                float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
                currentSpeed = Mathf.MoveTowards(
                    currentSpeed,
                    studentMoveSpeed,
                    studentAcceleration * deltaTime);
                Vector2 nextPosition = Vector2.MoveTowards(
                    student.position,
                    waypoint,
                    currentSpeed * deltaTime);
                student.position = new Vector3(nextPosition.x, nextPosition.y, student.position.z);
                yield return null;
            }

            student.position = new Vector3(waypoint.x, waypoint.y, student.position.z);
        }
    }

    private IEnumerator AnimateDoorScale(float fromScale, float toScale, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float easedProgress = progress * progress * (3f - (2f * progress));
            ApplyDoorScale(Mathf.Lerp(fromScale, toScale, easedProgress));
            yield return null;
        }

        ApplyDoorScale(toScale);
    }

    private void ApplyDoorScale(float scaleMultiplier)
    {
        Vector3 leftScale = leftClosedScale;
        leftScale.x *= scaleMultiplier;
        leftDoor.localScale = leftScale;
        KeepHingeAnchored(leftDoor, leftDoorRenderer, leftHingeWorldPosition, true);

        Vector3 rightScale = rightClosedScale;
        rightScale.x *= scaleMultiplier;
        rightDoor.localScale = rightScale;
        KeepHingeAnchored(rightDoor, rightDoorRenderer, rightHingeWorldPosition, false);
    }

    private void PrepareStudentForExit(GameObject student)
    {
        Transform torso = student.transform.Find("Torso");
        if (torso != null)
        {
            torso.gameObject.SetActive(true);
        }

        SortingGroup rootSortingGroup = student.GetComponent<SortingGroup>();
        if (rootSortingGroup != null)
        {
            rootSortingGroup.enabled = true;
        }

        YSortByPosition ySort = student.GetComponent<YSortByPosition>();
        if (ySort != null)
        {
            ySort.enabled = true;
            ySort.RefreshSorting();
        }

        Canvas[] canvases = student.GetComponentsInChildren<Canvas>(true);
        for (int index = 0; index < canvases.Length; index++)
        {
            canvases[index].overrideSorting = false;
        }
    }

    private void SetDoorPanelsInFront()
    {
        int layerId = SortingLayer.NameToID(foregroundSortingLayer);
        if (layerId == 0 && foregroundSortingLayer != "Default")
        {
            Debug.LogWarning(
                "Sorting layer '" + foregroundSortingLayer + "' was not found. " +
                "The classroom doors will keep their current layer.",
                this);
            return;
        }

        ConfigureDoorRenderer(leftDoorRenderer, layerId, foregroundSortingOrder);
        ConfigureDoorRenderer(rightDoorRenderer, layerId, foregroundSortingOrder + 1);
        ConfigureDoorRenderer(
            doorwayForegroundRenderer,
            layerId,
            foregroundSortingOrder - 1);
    }

    private void RestoreDoorSorting()
    {
        leftDoor.localScale = leftClosedScale;
        rightDoor.localScale = rightClosedScale;
        leftDoor.localPosition = leftClosedPosition;
        rightDoor.localPosition = rightClosedPosition;
        leftClosedSort.Apply(leftDoorRenderer);
        rightClosedSort.Apply(rightDoorRenderer);
        doorwayForegroundClosedSort.Apply(doorwayForegroundRenderer);
    }

    private void CaptureClosedState()
    {
        if (hasCapturedClosedState || leftDoor == null || rightDoor == null)
        {
            return;
        }

        leftClosedScale = leftDoor.localScale;
        rightClosedScale = rightDoor.localScale;
        leftClosedPosition = leftDoor.localPosition;
        rightClosedPosition = rightDoor.localPosition;
        leftHingeWorldPosition = GetDoorEdgeWorldPosition(leftDoorRenderer, true);
        rightHingeWorldPosition = GetDoorEdgeWorldPosition(rightDoorRenderer, false);
        leftClosedSort = RendererSortState.Capture(leftDoorRenderer);
        rightClosedSort = RendererSortState.Capture(rightDoorRenderer);
        doorwayForegroundClosedSort = RendererSortState.Capture(doorwayForegroundRenderer);
        hasCapturedClosedState = true;
    }

    private Vector2 GetDoorwayCenter()
    {
        return ((Vector2)leftDoor.position + (Vector2)rightDoor.position) * 0.5f;
    }

    private void ResolveReferences()
    {
        if (leftDoor == null)
        {
            leftDoor = transform.Find("Left Door");
        }

        if (rightDoor == null)
        {
            rightDoor = transform.Find("Right Door");
        }

        if (leftDoorRenderer == null && leftDoor != null)
        {
            leftDoorRenderer = leftDoor.GetComponent<SpriteRenderer>();
        }

        if (rightDoorRenderer == null && rightDoor != null)
        {
            rightDoorRenderer = rightDoor.GetComponent<SpriteRenderer>();
        }

        if (doorwayForegroundRenderer == null && transform.parent != null)
        {
            doorwayForegroundRenderer = transform.parent.GetComponent<SpriteRenderer>();
        }
    }

    private static Vector3 GetDoorEdgeWorldPosition(SpriteRenderer renderer, bool leftEdge)
    {
        if (renderer == null || renderer.sprite == null)
        {
            return renderer != null ? renderer.transform.position : Vector3.zero;
        }

        Bounds spriteBounds = renderer.sprite.bounds;
        float edgeX = leftEdge ? spriteBounds.min.x : spriteBounds.max.x;
        return renderer.transform.TransformPoint(new Vector3(edgeX, spriteBounds.center.y, 0f));
    }

    private static void KeepHingeAnchored(
        Transform door,
        SpriteRenderer renderer,
        Vector3 hingeWorldPosition,
        bool leftEdge)
    {
        if (door == null || renderer == null || renderer.sprite == null)
        {
            return;
        }

        Vector3 currentHingePosition = GetDoorEdgeWorldPosition(renderer, leftEdge);
        door.position += hingeWorldPosition - currentHingePosition;
    }

    private static Transform FindDoorRoot()
    {
#if UNITY_2023_1_OR_NEWER
        Transform[] sceneTransforms = FindObjectsByType<Transform>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
#else
        Transform[] sceneTransforms = FindObjectsOfType<Transform>();
#endif
        for (int index = 0; index < sceneTransforms.Length; index++)
        {
            Transform candidate = sceneTransforms[index];
            if (candidate.name == "Doors"
                && candidate.Find("Left Door") != null
                && candidate.Find("Right Door") != null)
            {
                return candidate;
            }
        }

        return null;
    }

    private static void AddWaypointIfDistinct(List<Vector2> route, Vector2 waypoint)
    {
        if (route.Count == 0 || (route[route.Count - 1] - waypoint).sqrMagnitude > 0.001f)
        {
            route.Add(waypoint);
        }
    }

    private static void ConfigureDoorRenderer(SpriteRenderer renderer, int layerId, int sortingOrder)
    {
        if (renderer == null)
        {
            return;
        }

        renderer.sortingLayerID = layerId;
        renderer.sortingOrder = sortingOrder;
    }

    private static Vector3 ToNavMeshPosition(Vector2 worldPosition)
    {
        return new Vector3(worldPosition.x, 0f, worldPosition.y);
    }

    private static Vector2 ToWorldPosition(Vector3 navMeshPosition)
    {
        return new Vector2(navMeshPosition.x, navMeshPosition.z);
    }

    private static IEnumerator WaitForSeconds(float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private readonly struct RendererSortState
    {
        private readonly int sortingLayerId;
        private readonly int sortingOrder;

        private RendererSortState(int sortingLayerId, int sortingOrder)
        {
            this.sortingLayerId = sortingLayerId;
            this.sortingOrder = sortingOrder;
        }

        public static RendererSortState Capture(SpriteRenderer renderer)
        {
            return renderer != null
                ? new RendererSortState(renderer.sortingLayerID, renderer.sortingOrder)
                : default;
        }

        public void Apply(SpriteRenderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.sortingLayerID = sortingLayerId;
            renderer.sortingOrder = sortingOrder;
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class AraBotClickToMove : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float acceleration = 14f;
    [SerializeField] private float deceleration = 18f;
    [SerializeField] private float slowDownRadius = 0.85f;
    [SerializeField] private float cornerReachDistance = 0.08f;
    [SerializeField] private float stoppingDistance = 0.05f;
    [SerializeField] private float destinationSampleDistance = 1f;
    [SerializeField] private bool flipSpriteWithMovement = true;

    private static readonly Vector3[] EmptyCorners = Array.Empty<Vector3>();

    private Camera targetCamera;
    private NavMeshPath currentPath;
    private Vector3[] currentCorners = EmptyCorners;
    private int currentCornerIndex;
    private Plane clickPlane;
    private Rigidbody2D body;
    private SpriteRenderer spriteRenderer;
    private Vector2 currentVelocity;
    private float cachedZ;

    public Vector2 CurrentVelocity => currentVelocity;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        currentPath = new NavMeshPath();
        cachedZ = transform.position.z;
        clickPlane = new Plane(Vector3.back, new Vector3(0f, 0f, cachedZ));

        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.gravityScale = 0f;
            body.simulated = false;
        }
    }

    private void Update()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera == null)
        {
            return;
        }

        HandleClick();
        UpdateMovement();
    }

    // Converts left-clicks into 2D world targets sampled against the runtime NavMesh.
    private void HandleClick()
    {
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
        {
            return;
        }

        Ray clickRay = targetCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!clickPlane.Raycast(clickRay, out float enter))
        {
            return;
        }

        Vector3 worldTarget = clickRay.GetPoint(enter);
        Vector3 navMeshTarget = ToNavMeshPosition(worldTarget);

        if (!NavMesh.SamplePosition(navMeshTarget, out NavMeshHit sampledDestination, destinationSampleDistance, NavMesh.AllAreas))
        {
            return;
        }

        Vector3 navMeshStart = ToNavMeshPosition(transform.position);
        if (!NavMesh.SamplePosition(navMeshStart, out NavMeshHit sampledStart, destinationSampleDistance, NavMesh.AllAreas))
        {
            return;
        }

        if (!NavMesh.CalculatePath(sampledStart.position, sampledDestination.position, NavMesh.AllAreas, currentPath))
        {
            return;
        }

        if (currentPath.status == NavMeshPathStatus.PathInvalid || currentPath.corners == null || currentPath.corners.Length < 2)
        {
            ClearPath();
            return;
        }

        currentCorners = currentPath.corners;
        currentCornerIndex = 1;
    }

    private void UpdateMovement()
    {
        Vector2 currentWorldPosition = transform.position;
        AdvanceCompletedCorners(currentWorldPosition);

        Vector2 desiredVelocity = Vector2.zero;
        if (TryGetTargetPosition(currentWorldPosition, out Vector2 targetWorldPosition))
        {
            Vector2 toTarget = targetWorldPosition - currentWorldPosition;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                float targetSpeed = moveSpeed;
                if (currentCornerIndex == currentCorners.Length - 1)
                {
                    float remainingDistance = GetRemainingPathDistance(currentWorldPosition);
                    float arrivalRatio = Mathf.Clamp01(remainingDistance / Mathf.Max(slowDownRadius, stoppingDistance));
                    targetSpeed *= arrivalRatio;
                }

                desiredVelocity = toTarget.normalized * targetSpeed;
            }
        }

        float velocityChange = desiredVelocity.sqrMagnitude > currentVelocity.sqrMagnitude ? acceleration : deceleration;
        currentVelocity = Vector2.MoveTowards(currentVelocity, desiredVelocity, velocityChange * Time.deltaTime);

        if (desiredVelocity == Vector2.zero && currentVelocity.sqrMagnitude <= 0.0001f)
        {
            currentVelocity = Vector2.zero;
        }

        Vector2 nextWorldPosition = currentWorldPosition + (currentVelocity * Time.deltaTime);
        Vector2 movement = nextWorldPosition - currentWorldPosition;
        transform.position = new Vector3(nextWorldPosition.x, nextWorldPosition.y, cachedZ);

        if (flipSpriteWithMovement && spriteRenderer != null && Mathf.Abs(movement.x) > 0.001f)
        {
            spriteRenderer.flipX = movement.x < 0f;
        }
    }

    private void AdvanceCompletedCorners(Vector2 currentWorldPosition)
    {
        while (currentCorners != null && currentCorners.Length > 0 && currentCornerIndex < currentCorners.Length)
        {
            Vector2 cornerPosition = ToWorldPosition(currentCorners[currentCornerIndex]);
            float reachDistance = currentCornerIndex == currentCorners.Length - 1 ? stoppingDistance : cornerReachDistance;

            if ((cornerPosition - currentWorldPosition).sqrMagnitude > reachDistance * reachDistance)
            {
                break;
            }

            currentCornerIndex++;
        }

        if (currentCorners != null && currentCornerIndex >= currentCorners.Length)
        {
            ClearPath();
        }
    }

    private bool TryGetTargetPosition(Vector2 currentWorldPosition, out Vector2 targetWorldPosition)
    {
        if (currentCorners == null || currentCorners.Length == 0 || currentCornerIndex >= currentCorners.Length)
        {
            targetWorldPosition = currentWorldPosition;
            return false;
        }

        targetWorldPosition = ToWorldPosition(currentCorners[currentCornerIndex]);
        return true;
    }

    private float GetRemainingPathDistance(Vector2 currentWorldPosition)
    {
        if (currentCorners == null || currentCorners.Length == 0 || currentCornerIndex >= currentCorners.Length)
        {
            return 0f;
        }

        float remainingDistance = 0f;
        Vector2 previousPosition = currentWorldPosition;

        for (int index = currentCornerIndex; index < currentCorners.Length; index++)
        {
            Vector2 cornerPosition = ToWorldPosition(currentCorners[index]);
            remainingDistance += Vector2.Distance(previousPosition, cornerPosition);
            previousPosition = cornerPosition;
        }

        return remainingDistance;
    }

    private void ClearPath()
    {
        currentCorners = EmptyCorners;
        currentCornerIndex = 0;
    }

    private static Vector3 ToNavMeshPosition(Vector3 worldPosition)
    {
        return new Vector3(worldPosition.x, 0f, worldPosition.y);
    }

    private static Vector2 ToWorldPosition(Vector3 navMeshPosition)
    {
        return new Vector2(navMeshPosition.x, navMeshPosition.z);
    }
}

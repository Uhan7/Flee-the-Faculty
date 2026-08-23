using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
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
    [SerializeField, Min(0f)] private float collisionSkin = 0.02f;
    [SerializeField, Range(0f, 1f)] private float wallSlideStrength = 1f;
    [SerializeField] private LayerMask collisionLayers = ~0;
    [SerializeField] private bool flipSpriteWithMovement = true;

    private static readonly Vector3[] EmptyCorners = Array.Empty<Vector3>();

    private Camera targetCamera;
    private NavMeshPath currentPath;
    private readonly List<RaycastHit2D> collisionHits = new List<RaycastHit2D>(8);
    private Vector3[] currentCorners = EmptyCorners;
    private int currentCornerIndex;
    private Plane clickPlane;
    private Rigidbody2D body;
    private Collider2D movementCollider;
    private ContactFilter2D movementContactFilter;
    private SpriteRenderer spriteRenderer;
    private Vector2 currentVelocity;
    private Vector2 navigationOffset;
    private float cachedZ;

    public Vector2 CurrentVelocity => currentVelocity;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        movementCollider = GetComponent<Collider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        currentPath = new NavMeshPath();
        cachedZ = transform.position.z;
        clickPlane = new Plane(Vector3.back, new Vector3(0f, 0f, cachedZ));

        if (movementCollider != null)
        {
            navigationOffset = (Vector2)movementCollider.bounds.center - (Vector2)transform.position;
        }

        movementContactFilter = new ContactFilter2D();
        movementContactFilter.SetLayerMask(collisionLayers);
        movementContactFilter.useTriggers = false;

        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.gravityScale = 0f;
            body.bodyType = RigidbodyType2D.Kinematic;
            body.useFullKinematicContacts = true;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.simulated = true;
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
    }

    private void FixedUpdate()
    {
        UpdateMovement(Time.fixedDeltaTime);
    }

    // Converts left-clicks into 2D world targets sampled against the runtime NavMesh.
    private void HandleClick()
    {
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
        {
            return;
        }

        if ((DialogueManager.Instance != null && DialogueManager.Instance.IsPlaying)
            || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
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

        Vector3 navMeshStart = ToNavMeshPosition(GetNavigationWorldPosition());
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

    private void UpdateMovement(float deltaTime)
    {
        Vector2 currentRootPosition = GetRootPosition();
        Vector2 currentNavigationPosition = currentRootPosition + navigationOffset;
        AdvanceCompletedCorners(currentNavigationPosition);

        Vector2 desiredVelocity = Vector2.zero;
        if (TryGetTargetPosition(currentNavigationPosition, out Vector2 targetWorldPosition))
        {
            Vector2 toTarget = targetWorldPosition - currentNavigationPosition;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                float targetSpeed = moveSpeed;
                if (currentCornerIndex == currentCorners.Length - 1)
                {
                    float remainingDistance = GetRemainingPathDistance(currentNavigationPosition);
                    float arrivalRatio = Mathf.Clamp01(remainingDistance / Mathf.Max(slowDownRadius, stoppingDistance));
                    targetSpeed *= arrivalRatio;
                }

                desiredVelocity = toTarget.normalized * targetSpeed;
            }
        }

        float velocityChange = desiredVelocity.sqrMagnitude > currentVelocity.sqrMagnitude ? acceleration : deceleration;
        currentVelocity = Vector2.MoveTowards(currentVelocity, desiredVelocity, velocityChange * deltaTime);

        if (desiredVelocity == Vector2.zero && currentVelocity.sqrMagnitude <= 0.0001f)
        {
            currentVelocity = Vector2.zero;
        }

        Vector2 requestedMovement = currentVelocity * deltaTime;
        Vector2 movement = LimitMovementByCollision(requestedMovement);
        Vector2 nextRootPosition = currentRootPosition + movement;

        if (body != null)
        {
            body.MovePosition(nextRootPosition);
        }
        else
        {
            transform.position = new Vector3(nextRootPosition.x, nextRootPosition.y, cachedZ);
        }

        if (movement != requestedMovement && deltaTime > 0f)
        {
            currentVelocity = movement / deltaTime;
        }

        if (flipSpriteWithMovement && spriteRenderer != null && Mathf.Abs(movement.x) > 0.001f)
        {
            spriteRenderer.flipX = movement.x < 0f;
        }
    }

    private Vector2 LimitMovementByCollision(Vector2 requestedMovement)
    {
        float requestedDistance = requestedMovement.magnitude;
        if (movementCollider == null || requestedDistance <= 0.00001f)
        {
            return requestedMovement;
        }

        Vector2 direction = requestedMovement / requestedDistance;
        if (!TryGetBlockingHit(direction, requestedDistance + collisionSkin, out RaycastHit2D blockingHit))
        {
            return requestedMovement;
        }

        float allowedDistance = Mathf.Clamp(blockingHit.distance - collisionSkin, 0f, requestedDistance);
        Vector2 forwardMovement = direction * allowedDistance;
        Vector2 remainingMovement = requestedMovement - forwardMovement;

        // Preserve motion along the obstacle instead of turning contact into a full stop.
        float movementIntoObstacle = Vector2.Dot(remainingMovement, blockingHit.normal);
        Vector2 slideMovement = movementIntoObstacle < 0f
            ? remainingMovement - blockingHit.normal * movementIntoObstacle
            : remainingMovement;
        slideMovement *= wallSlideStrength;

        float slideDistance = slideMovement.magnitude;
        if (slideDistance <= 0.00001f)
        {
            return forwardMovement;
        }

        Vector2 slideDirection = slideMovement / slideDistance;
        if (TryGetBlockingHit(slideDirection, slideDistance + collisionSkin, out RaycastHit2D slideHit))
        {
            slideDistance = Mathf.Clamp(slideHit.distance - collisionSkin, 0f, slideDistance);
        }

        return forwardMovement + slideDirection * slideDistance;
    }

    private bool TryGetBlockingHit(Vector2 direction, float distance, out RaycastHit2D blockingHit)
    {
        collisionHits.Clear();
        movementCollider.Cast(
            direction,
            movementContactFilter,
            collisionHits,
            distance,
            true);

        blockingHit = default;
        float closestDistance = float.PositiveInfinity;
        for (int index = 0; index < collisionHits.Count; index++)
        {
            RaycastHit2D hit = collisionHits[index];
            if (hit.collider == null || hit.collider.isTrigger || hit.distance <= 0f)
            {
                continue;
            }

            if (hit.distance < closestDistance)
            {
                blockingHit = hit;
                closestDistance = hit.distance;
            }
        }

        return closestDistance < float.PositiveInfinity;
    }

    private Vector2 GetNavigationWorldPosition()
    {
        return GetRootPosition() + navigationOffset;
    }

    private Vector2 GetRootPosition()
    {
        return body != null ? body.position : (Vector2)transform.position;
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

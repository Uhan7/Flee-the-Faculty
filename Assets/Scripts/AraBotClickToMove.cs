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
    [SerializeField, Min(0.05f)] private float repathIntervalSeconds = 0.35f;
    [SerializeField, Min(0f)] private float collisionSkin = 0.02f;
    [SerializeField, Range(0f, 1f)] private float wallSlideStrength = 1f;
    [SerializeField, Min(0f)] private float dynamicBlockerAvoidanceDistance = 0.9f;
    [SerializeField, Range(0f, 1f)] private float dynamicBlockerAvoidanceStrength = 0.7f;
    [SerializeField, Min(0f)] private float dynamicBlockerClearance = 0.12f;
    [SerializeField, Min(0f)] private float dynamicBlockerDetourDistance = 0.9f;
    [SerializeField, Min(0f)] private float dynamicBlockerDetourForwardBias = 0.35f;
    [SerializeField, Min(0f)] private float dynamicBlockerStopDistance = 0.6f;
    [SerializeField, Min(0)] private int maxDynamicBlockerRecoveryAttempts = 2;
    [SerializeField, Min(0f)] private float blockedMoveThreshold = 0.01f;
    [SerializeField, Min(0.1f)] private float blockedRepathDelay = 0.3f;
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
    private float repathTimer;
    private float blockedTimer;
    private int dynamicBlockerRecoveryAttempts;
    private RaycastHit2D latestDynamicBlockerHit;
    private Vector3 finalDestination;
    private Vector3 currentDestination;
    private bool hasFinalDestination;
    private bool hasDestination;
    private bool hitDynamicBlockerThisFrame;
    private bool isDetouringAroundDynamicBlocker;
    private bool isConversationMovementLocked;

    public Vector2 CurrentVelocity => currentVelocity;
    public bool IsConversationMovementLocked => isConversationMovementLocked;

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
        if (isConversationMovementLocked)
        {
            StopForConversation();
            return;
        }

        UpdateMovement(Time.fixedDeltaTime);
    }

    public void SetConversationMovementLocked(bool locked)
    {
        isConversationMovementLocked = locked;
        if (locked)
        {
            StopForConversation();
        }
    }

    // Converts left-clicks into 2D world targets sampled against the runtime NavMesh.
    private void HandleClick()
    {
        if (isConversationMovementLocked
            || Mouse.current == null
            || !Mouse.current.leftButton.wasPressedThisFrame)
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
        TrySetPathToDestination(
            ToNavMeshPosition(worldTarget),
            clearPathOnFailure: true,
            setAsFinalDestination: true);
    }

    private void StopForConversation()
    {
        currentVelocity = Vector2.zero;
        blockedTimer = 0f;
        ClearPath();

        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
    }

    private void UpdateMovement(float deltaTime)
    {
        RefreshPathIfNeeded(deltaTime);
        hitDynamicBlockerThisFrame = false;
        latestDynamicBlockerHit = default;

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

        desiredVelocity = ApplyDynamicBlockerAvoidance(desiredVelocity, deltaTime);

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

        if (hasDestination && desiredVelocity.sqrMagnitude > 0.0001f && movement.sqrMagnitude <= blockedMoveThreshold * blockedMoveThreshold)
        {
            blockedTimer += deltaTime;
            if (blockedTimer >= blockedRepathDelay)
            {
                if (!TryHandleDynamicBlockerStall(desiredVelocity, currentNavigationPosition))
                {
                    Vector3 repathDestination = hasFinalDestination ? finalDestination : currentDestination;
                    TrySetPathToDestination(
                        repathDestination,
                        clearPathOnFailure: false,
                        setAsFinalDestination: !isDetouringAroundDynamicBlocker);
                }

                blockedTimer = 0f;
            }
        }
        else
        {
            blockedTimer = 0f;
        }

        if (flipSpriteWithMovement && spriteRenderer != null && Mathf.Abs(movement.x) > 0.001f)
        {
            spriteRenderer.flipX = movement.x < 0f;
        }
    }

    private void RefreshPathIfNeeded(float deltaTime)
    {
        if (!hasDestination || repathIntervalSeconds <= 0f)
        {
            return;
        }

        repathTimer -= deltaTime;
        if (repathTimer > 0f)
        {
            return;
        }

        repathTimer = repathIntervalSeconds;
        TrySetPathToDestination(
            currentDestination,
            clearPathOnFailure: false,
            setAsFinalDestination: !isDetouringAroundDynamicBlocker);
    }

    private Vector2 ApplyDynamicBlockerAvoidance(Vector2 desiredVelocity, float deltaTime)
    {
        if (movementCollider == null
            || desiredVelocity.sqrMagnitude <= 0.0001f
            || dynamicBlockerAvoidanceDistance <= 0f
            || !TryGetBlockingHit(
                desiredVelocity.normalized,
                Mathf.Max(desiredVelocity.magnitude * deltaTime, dynamicBlockerAvoidanceDistance) + collisionSkin,
                out RaycastHit2D blockingHit)
            || !DynamicMovementBlockerUtility.IsDynamicMovementBlocker(blockingHit.collider, body))
        {
            return desiredVelocity;
        }

        Vector2 avoidanceDirection = DynamicMovementBlockerUtility.GetPreferredAvoidanceDirection(
            desiredVelocity.normalized,
            GetNavigationWorldPosition(),
            blockingHit.collider.bounds.center);
        Vector2 blendedDirection = Vector2.Lerp(
            desiredVelocity.normalized,
            avoidanceDirection,
            dynamicBlockerAvoidanceStrength);

        if (blendedDirection.sqrMagnitude <= 0.0001f)
        {
            return desiredVelocity;
        }

        return blendedDirection.normalized * desiredVelocity.magnitude;
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

        if (DynamicMovementBlockerUtility.IsDynamicMovementBlocker(blockingHit.collider, body))
        {
            hitDynamicBlockerThisFrame = true;
            latestDynamicBlockerHit = blockingHit;
            float allowedDynamicDistance = Mathf.Clamp(
                blockingHit.distance - Mathf.Max(collisionSkin, dynamicBlockerClearance),
                0f,
                requestedDistance);
            return direction * allowedDynamicDistance;
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
        return TryGetBlockingHit(direction, distance, null, out blockingHit);
    }

    private bool TryGetBlockingHit(
        Vector2 direction,
        float distance,
        Collider2D ignoredCollider,
        out RaycastHit2D blockingHit)
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
            if (hit.collider == null || hit.collider.isTrigger || hit.collider == ignoredCollider)
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
            HandleReachedPathEnd();
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

    private void HandleReachedPathEnd()
    {
        if (isDetouringAroundDynamicBlocker
            && hasFinalDestination
            && TrySetPathToDestination(
                finalDestination,
                clearPathOnFailure: false,
                setAsFinalDestination: true))
        {
            return;
        }

        ClearPath();
    }

    private void ClearPath(bool clearFinalDestination = true)
    {
        currentCorners = EmptyCorners;
        currentCornerIndex = 0;
        blockedTimer = 0f;
        dynamicBlockerRecoveryAttempts = 0;
        hitDynamicBlockerThisFrame = false;
        latestDynamicBlockerHit = default;
        currentDestination = default;
        hasDestination = false;
        isDetouringAroundDynamicBlocker = false;
        repathTimer = 0f;

        if (clearFinalDestination)
        {
            finalDestination = default;
            hasFinalDestination = false;
        }
    }

    private bool TrySetPathToDestination(
        Vector3 navMeshDestination,
        bool clearPathOnFailure,
        bool setAsFinalDestination)
    {
        if (!NavMesh.SamplePosition(navMeshDestination, out NavMeshHit sampledDestination, destinationSampleDistance, NavMesh.AllAreas))
        {
            return HandlePathFailure(clearPathOnFailure);
        }

        Vector3 navMeshStart = ToNavMeshPosition(GetNavigationWorldPosition());
        if (!NavMesh.SamplePosition(navMeshStart, out NavMeshHit sampledStart, destinationSampleDistance, NavMesh.AllAreas))
        {
            return HandlePathFailure(clearPathOnFailure);
        }

        if (!NavMesh.CalculatePath(sampledStart.position, sampledDestination.position, NavMesh.AllAreas, currentPath))
        {
            return HandlePathFailure(clearPathOnFailure);
        }

        if (currentPath.status == NavMeshPathStatus.PathInvalid || currentPath.corners == null || currentPath.corners.Length < 2)
        {
            return HandlePathFailure(clearPathOnFailure);
        }

        currentCorners = currentPath.corners;
        currentCornerIndex = 1;
        currentDestination = sampledDestination.position;
        hasDestination = true;
        isDetouringAroundDynamicBlocker = !setAsFinalDestination;
        repathTimer = repathIntervalSeconds;
        blockedTimer = 0f;

        if (setAsFinalDestination)
        {
            finalDestination = sampledDestination.position;
            hasFinalDestination = true;
            dynamicBlockerRecoveryAttempts = 0;
        }

        return true;
    }

    private bool TryHandleDynamicBlockerStall(Vector2 desiredVelocity, Vector2 currentNavigationPosition)
    {
        if (!TryGetDynamicBlockerHitForRecovery(desiredVelocity, currentNavigationPosition, out RaycastHit2D blockingHit))
        {
            return false;
        }

        if (ShouldStopForDynamicBlocker(currentNavigationPosition))
        {
            ClearPath();
            return true;
        }

        dynamicBlockerRecoveryAttempts++;
        if (maxDynamicBlockerRecoveryAttempts > 0
            && dynamicBlockerRecoveryAttempts > maxDynamicBlockerRecoveryAttempts)
        {
            ClearPath();
            return true;
        }

        if (TryStartDynamicBlockerDetour(desiredVelocity))
        {
            return true;
        }

        return false;
    }

    private bool TryStartDynamicBlockerDetour(Vector2 desiredVelocity)
    {
        if (!hasFinalDestination || movementCollider == null || dynamicBlockerDetourDistance <= 0f)
        {
            return false;
        }

        Vector2 currentNavigationPosition = GetNavigationWorldPosition();
        if (!TryGetDynamicBlockerHitForRecovery(desiredVelocity, currentNavigationPosition, out RaycastHit2D blockingHit))
        {
            return false;
        }

        Vector2 forwardDirection = GetDynamicBlockerForwardDirection(desiredVelocity, currentNavigationPosition);
        Vector2 blockerCenter = blockingHit.collider.bounds.center;
        Vector2 preferredSide = DynamicMovementBlockerUtility.GetPreferredAvoidanceDirection(
            forwardDirection,
            currentNavigationPosition,
            blockerCenter);

        return TrySetDynamicBlockerDetour(
                forwardDirection,
                preferredSide,
                blockerCenter,
                blockingHit.collider.bounds)
            || TrySetDynamicBlockerDetour(
                forwardDirection,
                -preferredSide,
                blockerCenter,
                blockingHit.collider.bounds);
    }

    private bool TrySetDynamicBlockerDetour(
        Vector2 forwardDirection,
        Vector2 sideDirection,
        Vector2 blockerCenter,
        Bounds blockerBounds)
    {
        if (sideDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        float blockerRadius = Mathf.Max(blockerBounds.extents.x, blockerBounds.extents.y);
        Vector2 detourWorldPosition = blockerCenter
            + sideDirection.normalized * (blockerRadius + dynamicBlockerDetourDistance)
            + forwardDirection.normalized * dynamicBlockerDetourForwardBias;

        return TrySetPathToDestination(
            ToNavMeshPosition(detourWorldPosition),
            clearPathOnFailure: false,
            setAsFinalDestination: false);
    }

    private bool TryGetDynamicBlockingHitAhead(
        Vector2 desiredVelocity,
        Vector2 currentNavigationPosition,
        out RaycastHit2D blockingHit)
    {
        Vector2 forwardDirection = GetDynamicBlockerForwardDirection(desiredVelocity, currentNavigationPosition);
        if (forwardDirection.sqrMagnitude <= 0.0001f)
        {
            blockingHit = default;
            return false;
        }

        if (!TryGetBlockingHit(
                forwardDirection,
                dynamicBlockerAvoidanceDistance + dynamicBlockerDetourDistance + collisionSkin,
                out blockingHit))
        {
            return false;
        }

        return DynamicMovementBlockerUtility.IsDynamicMovementBlocker(blockingHit.collider, body);
    }

    private bool TryGetDynamicBlockerHitForRecovery(
        Vector2 desiredVelocity,
        Vector2 currentNavigationPosition,
        out RaycastHit2D blockingHit)
    {
        if (hitDynamicBlockerThisFrame && latestDynamicBlockerHit.collider != null)
        {
            blockingHit = latestDynamicBlockerHit;
            return true;
        }

        return TryGetDynamicBlockingHitAhead(desiredVelocity, currentNavigationPosition, out blockingHit);
    }

    private Vector2 GetDynamicBlockerForwardDirection(Vector2 desiredVelocity, Vector2 currentNavigationPosition)
    {
        if (desiredVelocity.sqrMagnitude > 0.0001f)
        {
            return desiredVelocity.normalized;
        }

        return GetFallbackDetourDirection(currentNavigationPosition);
    }

    private bool ShouldStopForDynamicBlocker(Vector2 currentNavigationPosition)
    {
        if (!hasFinalDestination)
        {
            return false;
        }

        if (dynamicBlockerStopDistance > 0f)
        {
            Vector2 finalWorldDestination = ToWorldPosition(finalDestination);
            if ((finalWorldDestination - currentNavigationPosition).sqrMagnitude
                <= dynamicBlockerStopDistance * dynamicBlockerStopDistance)
            {
                return true;
            }
        }

        return maxDynamicBlockerRecoveryAttempts <= 0;
    }

    private Vector2 GetFallbackDetourDirection(Vector2 currentNavigationPosition)
    {
        if (currentCorners != null
            && currentCornerIndex < currentCorners.Length)
        {
            Vector2 toCorner = ToWorldPosition(currentCorners[currentCornerIndex]) - currentNavigationPosition;
            if (toCorner.sqrMagnitude > 0.0001f)
            {
                return toCorner.normalized;
            }
        }

        if (hasFinalDestination)
        {
            Vector2 toFinalDestination = ToWorldPosition(finalDestination) - currentNavigationPosition;
            if (toFinalDestination.sqrMagnitude > 0.0001f)
            {
                return toFinalDestination.normalized;
            }
        }

        return currentVelocity.sqrMagnitude > 0.0001f ? currentVelocity.normalized : Vector2.zero;
    }

    private bool HandlePathFailure(bool clearPathOnFailure)
    {
        if (clearPathOnFailure)
        {
            ClearPath();
        }

        return false;
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

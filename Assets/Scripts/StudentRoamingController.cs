using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public sealed class StudentRoamingController : MonoBehaviour
{
    // Area
    [SerializeField] private Collider2D roamArea;
    [SerializeField] private Transform manualBoundsCenter;
    [SerializeField] private Vector2 manualBoundsOffset = Vector2.zero;
    [SerializeField] private Vector2 manualBoundsSize = new Vector2(2f, 2f);
    [SerializeField] private bool anchorManualBoundsToSpawn = true;
    [SerializeField, Min(1)] private int destinationSampleAttempts = 12;
    [SerializeField, Min(0f)] private float boundaryPadding = 0.15f;

    // Timing
    [SerializeField] private Vector2 waitIntervalRange = new Vector2(0.75f, 2f);
    [SerializeField, Min(0.05f)] private float repathIntervalSeconds = 0.35f;

    // Pathing
    [SerializeField] private float slowDownRadius = 0.85f;
    [SerializeField] private float cornerReachDistance = 0.08f;
    [SerializeField] private float stoppingDistance = 0.05f;
    [SerializeField] private float destinationSampleDistance = 1f;
    [SerializeField, Min(0f)] private float partialPathErrorAllowance = 0.35f;
    [SerializeField, Min(0f)] private float destinationObstacleClearance = 0.12f;

    // Movement
    [SerializeField] private float moveSpeed = 1f;
    [SerializeField] private float acceleration = 6f;
    [SerializeField] private float deceleration = 8f;
    [SerializeField, Min(0f)] private float collisionSkin = 0.02f;
    [SerializeField, Range(0f, 1f)] private float wallSlideStrength = 1f;
    [SerializeField, Min(0f)] private float dynamicBlockerAvoidanceDistance = 0.9f;
    [SerializeField, Range(0f, 1f)] private float dynamicBlockerAvoidanceStrength = 0.7f;
    [SerializeField, Min(0f)] private float dynamicBlockerClearance = 0.12f;
    [SerializeField, Min(0f)] private float overlapRecoveryPadding = 0.04f;
    [SerializeField, Min(0f)] private float blockedMoveThreshold = 0.01f;
    [SerializeField, Min(0.1f)] private float blockedRepickDelay = 0.4f;
    [SerializeField] private LayerMask collisionLayers = ~0;
    [SerializeField] private bool flipSpriteWithMovement = true;
    [SerializeField] private bool pauseWhileInteracting = true;
    [SerializeField, Min(0f)] private float activatorYieldRadius = 0.85f;
    [SerializeField, Min(0f)] private float activatorResumeDelay = 0.35f;
    [SerializeField] private bool passThroughAraBot = true;

    private static readonly Vector3[] EmptyCorners = Array.Empty<Vector3>();

    private readonly List<RaycastHit2D> collisionHits = new List<RaycastHit2D>(8);
    private readonly List<Collider2D> overlapColliders = new List<Collider2D>(8);

    private Rigidbody2D body;
    private Collider2D movementCollider;
    private ContactFilter2D movementContactFilter;
    private SpriteRenderer spriteRenderer;
    private CharacterProceduralAnimation proceduralAnimation;
    private AraBotClickToMove activatorMovement;
    private NavMeshPath currentPath;
    private Vector3[] currentCorners = EmptyCorners;
    private Vector2 currentVelocity;
    private Vector2 navigationOffset;
    private Vector3 currentDestination;
    private Transform interactionTarget;
    private Vector2 anchoredManualBoundsCenter;
    private float waitTimer;
    private float repathTimer;
    private float blockedTimer;
    private float activatorYieldTimer;
    private int currentCornerIndex;
    private bool hasDestination;
    private bool hitStaticBlockerThisFrame;
    private bool isYieldingToActivator;

    public Vector2 CurrentVelocity => currentVelocity;
    public Collider2D MovementCollider => movementCollider;

    private void Reset()
    {
        body = GetComponent<Rigidbody2D>();
        movementCollider = FindMovementCollider();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (manualBoundsCenter == null)
        {
            manualBoundsCenter = transform;
        }

        if (body != null)
        {
            body.gravityScale = 0f;
            body.bodyType = RigidbodyType2D.Kinematic;
            body.useFullKinematicContacts = true;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        movementCollider = FindMovementCollider();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        proceduralAnimation = GetComponent<CharacterProceduralAnimation>();
        currentPath = new NavMeshPath();
        activatorMovement = FindFirstObjectByType<AraBotClickToMove>();

        if (manualBoundsCenter == null)
        {
            manualBoundsCenter = transform;
        }

        anchoredManualBoundsCenter = ResolveManualBoundsCenter();

        if (movementCollider != null)
        {
            navigationOffset = (Vector2)movementCollider.bounds.center - (Vector2)transform.position;
        }

        movementContactFilter = new ContactFilter2D();
        movementContactFilter.SetLayerMask(collisionLayers);
        movementContactFilter.useTriggers = false;
        IgnoreAraBotCollision();
    }

    private void OnEnable()
    {
        IgnoreAraBotCollision();
        anchoredManualBoundsCenter = ResolveManualBoundsCenter();
        currentVelocity = Vector2.zero;
        blockedTimer = 0f;
        repathTimer = 0f;
        activatorYieldTimer = 0f;
        isYieldingToActivator = false;
        BeginWaiting();
    }

    private void OnDisable()
    {
        currentVelocity = Vector2.zero;
        interactionTarget = null;

        if (proceduralAnimation != null)
        {
            proceduralAnimation.ClearLookTarget();
        }

        ClearPath();
    }

    private void FixedUpdate()
    {
        if (!HasRoamBounds())
        {
            currentVelocity = Vector2.zero;
            ClearPath();
            return;
        }

        if (TryResolveStaticOverlap())
        {
            currentVelocity = Vector2.zero;
            ClearPath();
            waitTimer = 0f;
            return;
        }

        if (interactionTarget != null)
        {
            UpdateInteractionFocus();
            return;
        }

        float deltaTime = Time.fixedDeltaTime;
        if (UpdateActivatorYield(deltaTime))
        {
            return;
        }

        if (!hasDestination)
        {
            UpdateWaiting(deltaTime);
            return;
        }

        UpdateMovement(deltaTime);
    }

    public void PickNewDestinationNow()
    {
        currentVelocity = Vector2.zero;
        blockedTimer = 0f;
        waitTimer = 0f;
        ClearPath();
    }

    public void SetInteractionTarget(Transform target)
    {
        interactionTarget = target;

        if (proceduralAnimation != null)
        {
            proceduralAnimation.SetLookTarget(target);
        }

        if (pauseWhileInteracting)
        {
            currentVelocity = Vector2.zero;
            blockedTimer = 0f;
            ClearPath();
        }
    }

    public void ClearInteractionTarget(Transform target = null)
    {
        if (target != null && interactionTarget != target)
        {
            return;
        }

        interactionTarget = null;

        if (proceduralAnimation != null)
        {
            proceduralAnimation.ClearLookTarget(target);
        }

        if (pauseWhileInteracting)
        {
            BeginWaiting();
        }
    }

    private void UpdateWaiting(float deltaTime)
    {
        waitTimer -= deltaTime;
        if (waitTimer > 0f)
        {
            return;
        }

        if (TryPickNewRoamPath())
        {
            return;
        }

        waitTimer = GetRandomWaitDuration();
    }

    private void UpdateMovement(float deltaTime)
    {
        RefreshPathIfNeeded(deltaTime);
        hitStaticBlockerThisFrame = false;

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
        Vector2 nextNavigationPosition = nextRootPosition + navigationOffset;

        if (!IsPointInsideRoamBounds(nextNavigationPosition))
        {
            BeginWaiting();
            return;
        }

        body.MovePosition(nextRootPosition);

        if (movement != requestedMovement && deltaTime > 0f)
        {
            currentVelocity = movement / deltaTime;
        }

        UpdateSpriteFacing(movement);

        if (movement.sqrMagnitude <= blockedMoveThreshold * blockedMoveThreshold)
        {
            blockedTimer += deltaTime;
            if (blockedTimer >= blockedRepickDelay)
            {
                if (hitStaticBlockerThisFrame)
                {
                    currentVelocity = Vector2.zero;
                    ClearPath();
                    waitTimer = 0f;
                    return;
                }

                if (!TrySetPathToDestination(currentDestination, clearPathOnFailure: false))
                {
                    BeginWaiting();
                    return;
                }

                blockedTimer = 0f;
            }

            return;
        }

        blockedTimer = 0f;
    }

    private void UpdateInteractionFocus()
    {
        currentVelocity = Vector2.zero;
        blockedTimer = 0f;

        if (interactionTarget == null)
        {
            return;
        }

        UpdateSpriteFacing((Vector2)(interactionTarget.position - transform.position));
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
        TrySetPathToDestination(currentDestination, clearPathOnFailure: false);
    }

    private bool TryPickNewRoamPath()
    {
        for (int attempt = 0; attempt < destinationSampleAttempts; attempt++)
        {
            Vector2 candidate = roamArea != null
                ? GetRandomPointInsideCollider(roamArea)
                : GetRandomPointInsideManualBounds();

            if (!IsPointInsideRoamBounds(candidate))
            {
                continue;
            }

            if (TrySetPathToDestination(ToNavMeshPosition(candidate), clearPathOnFailure: false))
            {
                blockedTimer = 0f;
                return true;
            }
        }

        return false;
    }

    private bool TrySetPathToDestination(Vector3 navMeshDestination, bool clearPathOnFailure)
    {
        if (!NavMesh.SamplePosition(navMeshDestination, out NavMeshHit sampledDestination, destinationSampleDistance, NavMesh.AllAreas))
        {
            return HandlePathFailure(clearPathOnFailure);
        }

        if (!IsPointInsideRoamBounds(ToWorldPosition(sampledDestination.position)))
        {
            return HandlePathFailure(clearPathOnFailure);
        }

        if (!HasPhysicalClearanceAt(ToWorldPosition(sampledDestination.position)))
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

        if (currentPath.status == NavMeshPathStatus.PathPartial)
        {
            Vector2 reachableEndPoint = ToWorldPosition(currentPath.corners[currentPath.corners.Length - 1]);
            Vector2 sampledWorldDestination = ToWorldPosition(sampledDestination.position);
            if ((reachableEndPoint - sampledWorldDestination).sqrMagnitude >
                partialPathErrorAllowance * partialPathErrorAllowance)
            {
                return HandlePathFailure(clearPathOnFailure);
            }
        }

        currentCorners = currentPath.corners;
        currentCornerIndex = 1;
        currentDestination = sampledDestination.position;
        hasDestination = true;
        repathTimer = repathIntervalSeconds;
        return true;
    }

    private bool HandlePathFailure(bool clearPathOnFailure)
    {
        if (clearPathOnFailure)
        {
            ClearPath();
        }

        return false;
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
            BeginWaiting();
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

    private void BeginWaiting()
    {
        currentVelocity = Vector2.zero;
        blockedTimer = 0f;
        waitTimer = GetRandomWaitDuration();
        ClearPath();
    }

    private void ClearPath()
    {
        currentCorners = EmptyCorners;
        currentCornerIndex = 0;
        currentDestination = default;
        hasDestination = false;
        repathTimer = 0f;
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
            float allowedDynamicDistance = Mathf.Clamp(
                blockingHit.distance - Mathf.Max(collisionSkin, dynamicBlockerClearance),
                0f,
                requestedDistance);
            return direction * allowedDynamicDistance;
        }

        hitStaticBlockerThisFrame = true;

        float allowedDistance = Mathf.Clamp(blockingHit.distance - collisionSkin, 0f, requestedDistance);
        Vector2 forwardMovement = direction * allowedDistance;
        Vector2 remainingMovement = requestedMovement - forwardMovement;

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

    private bool HasPhysicalClearanceAt(Vector2 navigationPosition)
    {
        if (movementCollider == null)
        {
            return true;
        }

        Vector2 clearance = Vector2.one * destinationObstacleClearance * 2f;
        Vector2 testSize = (Vector2)movementCollider.bounds.size + clearance;
        overlapColliders.Clear();
        Physics2D.OverlapBox(
            navigationPosition,
            testSize,
            movementCollider.transform.eulerAngles.z,
            movementContactFilter,
            overlapColliders);

        for (int index = 0; index < overlapColliders.Count; index++)
        {
            Collider2D otherCollider = overlapColliders[index];
            if (otherCollider == null
                || otherCollider == movementCollider
                || otherCollider.isTrigger
                || otherCollider.attachedRigidbody == body
                || DynamicMovementBlockerUtility.IsDynamicMovementBlocker(otherCollider, body))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private bool TryResolveStaticOverlap()
    {
        if (movementCollider == null)
        {
            return false;
        }

        overlapColliders.Clear();
        movementCollider.Overlap(movementContactFilter, overlapColliders);

        Vector2 strongestCorrection = Vector2.zero;
        float deepestOverlap = 0f;

        for (int index = 0; index < overlapColliders.Count; index++)
        {
            Collider2D otherCollider = overlapColliders[index];
            if (otherCollider == null
                || otherCollider == movementCollider
                || otherCollider.isTrigger
                || otherCollider.attachedRigidbody == body
                || DynamicMovementBlockerUtility.IsDynamicMovementBlocker(otherCollider, body))
            {
                continue;
            }

            ColliderDistance2D separation = movementCollider.Distance(otherCollider);
            if (!separation.isValid || !separation.isOverlapped)
            {
                continue;
            }

            float overlapDepth = -separation.distance;
            if (overlapDepth <= deepestOverlap)
            {
                continue;
            }

            deepestOverlap = overlapDepth;
            strongestCorrection = separation.normal
                * (overlapDepth + Mathf.Max(collisionSkin, overlapRecoveryPadding));
        }

        if (strongestCorrection.sqrMagnitude <= 0.000001f)
        {
            return false;
        }

        Vector2 correctedRootPosition = GetRootPosition() + strongestCorrection;
        Vector2 correctedNavigationPosition = correctedRootPosition + navigationOffset;
        if ((!IsPointInsideRoamBounds(correctedNavigationPosition)
                || !HasPhysicalClearanceAt(correctedNavigationPosition))
            && TryFindSafeRecoveryPosition(out Vector2 safeRootPosition))
        {
            correctedRootPosition = safeRootPosition;
        }

        body.position = correctedRootPosition;
        body.linearVelocity = Vector2.zero;
        blockedTimer = 0f;
        repathTimer = 0f;
        return true;
    }

    private bool TryFindSafeRecoveryPosition(out Vector2 safeRootPosition)
    {
        safeRootPosition = GetRootPosition();
        Vector2 currentNavigationPosition = GetNavigationWorldPosition();
        float closestDistanceSquared = float.PositiveInfinity;
        bool foundPosition = false;
        int attempts = Mathf.Max(4, destinationSampleAttempts);

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            Vector2 candidate = roamArea != null
                ? GetRandomPointInsideCollider(roamArea)
                : GetRandomPointInsideManualBounds();
            Vector3 navMeshCandidate = ToNavMeshPosition(candidate);
            if (!NavMesh.SamplePosition(
                    navMeshCandidate,
                    out NavMeshHit sampledPosition,
                    destinationSampleDistance,
                    NavMesh.AllAreas))
            {
                continue;
            }

            Vector2 sampledNavigationPosition = ToWorldPosition(sampledPosition.position);
            if (!IsPointInsideRoamBounds(sampledNavigationPosition)
                || !HasPhysicalClearanceAt(sampledNavigationPosition))
            {
                continue;
            }

            float distanceSquared = (sampledNavigationPosition - currentNavigationPosition).sqrMagnitude;
            if (distanceSquared >= closestDistanceSquared)
            {
                continue;
            }

            closestDistanceSquared = distanceSquared;
            safeRootPosition = sampledNavigationPosition - navigationOffset;
            foundPosition = true;
        }

        return foundPosition;
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
        if (movementCollider == null)
        {
            blockingHit = default;
            return false;
        }

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
            if (hit.collider == null
                || hit.collider.isTrigger
                || hit.collider == ignoredCollider
                || ShouldPassThrough(hit.collider))
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

    private bool ShouldPassThrough(Collider2D otherCollider)
    {
        return passThroughAraBot
            && DynamicMovementBlockerUtility.IsAraBot(otherCollider, body);
    }

    private void IgnoreAraBotCollision()
    {
        if (!passThroughAraBot || movementCollider == null)
        {
            return;
        }

        if (activatorMovement == null)
        {
            activatorMovement = FindFirstObjectByType<AraBotClickToMove>();
        }

        Collider2D activatorCollider = activatorMovement != null
            ? activatorMovement.GetComponent<Collider2D>()
            : null;
        if (activatorCollider != null)
        {
            Physics2D.IgnoreCollision(movementCollider, activatorCollider, true);
        }
    }
    private bool UpdateActivatorYield(float deltaTime)
    {
        bool shouldYield = IsActivatorWithinYieldRadius();
        if (shouldYield)
        {
            if (!isYieldingToActivator)
            {
                currentVelocity = Vector2.zero;
                blockedTimer = 0f;
                ClearPath();
            }

            isYieldingToActivator = true;
            activatorYieldTimer = activatorResumeDelay;
            return true;
        }

        if (!isYieldingToActivator)
        {
            return false;
        }

        currentVelocity = Vector2.zero;
        blockedTimer = 0f;
        activatorYieldTimer -= deltaTime;
        if (activatorYieldTimer > 0f)
        {
            return true;
        }

        isYieldingToActivator = false;
        BeginWaiting();
        return false;
    }

    private bool IsActivatorWithinYieldRadius()
    {
        if (activatorYieldRadius <= 0f)
        {
            return false;
        }

        if (activatorMovement == null || !activatorMovement.isActiveAndEnabled)
        {
            activatorMovement = FindFirstObjectByType<AraBotClickToMove>();
            if (activatorMovement == null || !activatorMovement.isActiveAndEnabled)
            {
                return false;
            }
        }

        Vector2 toActivator = (Vector2)activatorMovement.transform.position - GetRootPosition();
        return toActivator.sqrMagnitude <= activatorYieldRadius * activatorYieldRadius;
    }

    private Vector2 GetRandomPointInsideCollider(Collider2D area)
    {
        Bounds bounds = area.bounds;
        Vector2 min = new Vector2(bounds.min.x, bounds.min.y);
        Vector2 max = new Vector2(bounds.max.x, bounds.max.y);

        if (boundaryPadding > 0f)
        {
            min += Vector2.one * boundaryPadding;
            max -= Vector2.one * boundaryPadding;
        }

        if (max.x < min.x)
        {
            float midpointX = bounds.center.x;
            min.x = midpointX;
            max.x = midpointX;
        }

        if (max.y < min.y)
        {
            float midpointY = bounds.center.y;
            min.y = midpointY;
            max.y = midpointY;
        }

        return new Vector2(
            UnityEngine.Random.Range(min.x, max.x),
            UnityEngine.Random.Range(min.y, max.y));
    }

    private Vector2 GetRandomPointInsideManualBounds()
    {
        Rect bounds = GetManualBounds();
        float minX = bounds.xMin + boundaryPadding;
        float maxX = bounds.xMax - boundaryPadding;
        float minY = bounds.yMin + boundaryPadding;
        float maxY = bounds.yMax - boundaryPadding;

        if (maxX < minX)
        {
            float midpointX = bounds.center.x;
            minX = midpointX;
            maxX = midpointX;
        }

        if (maxY < minY)
        {
            float midpointY = bounds.center.y;
            minY = midpointY;
            maxY = midpointY;
        }

        return new Vector2(
            UnityEngine.Random.Range(minX, maxX),
            UnityEngine.Random.Range(minY, maxY));
    }

    private bool IsPointInsideRoamBounds(Vector2 point)
    {
        if (roamArea != null)
        {
            return roamArea.OverlapPoint(point);
        }

        return GetManualBounds().Contains(point);
    }

    private bool HasRoamBounds()
    {
        return roamArea != null || manualBoundsSize.x > 0f && manualBoundsSize.y > 0f;
    }

    private Rect GetManualBounds()
    {
        Vector2 center = anchorManualBoundsToSpawn
            ? anchoredManualBoundsCenter
            : ResolveManualBoundsCenter();

        Vector2 size = new Vector2(
            Mathf.Max(0.01f, manualBoundsSize.x),
            Mathf.Max(0.01f, manualBoundsSize.y));

        return new Rect(center - size * 0.5f, size);
    }

    private Vector2 GetRootPosition()
    {
        return body != null ? body.position : (Vector2)transform.position;
    }

    private Vector2 GetNavigationWorldPosition()
    {
        return GetRootPosition() + navigationOffset;
    }

    private Collider2D FindMovementCollider()
    {
        Collider2D[] colliders = GetComponents<Collider2D>();
        for (int index = 0; index < colliders.Length; index++)
        {
            Collider2D collider = colliders[index];
            if (collider != null && collider.enabled && !collider.isTrigger)
            {
                return collider;
            }
        }

        return GetComponent<Collider2D>();
    }

    private void UpdateSpriteFacing(Vector2 referenceDirection)
    {
        if (!flipSpriteWithMovement || spriteRenderer == null || Mathf.Abs(referenceDirection.x) <= 0.001f)
        {
            return;
        }

        spriteRenderer.flipX = referenceDirection.x < 0f;
    }

    private float GetRandomWaitDuration()
    {
        float minWait = Mathf.Min(waitIntervalRange.x, waitIntervalRange.y);
        float maxWait = Mathf.Max(waitIntervalRange.x, waitIntervalRange.y);
        return UnityEngine.Random.Range(minWait, maxWait);
    }

    private Vector2 ResolveManualBoundsCenter()
    {
        Vector2 center = manualBoundsCenter != null
            ? (Vector2)manualBoundsCenter.position
            : (Vector2)transform.position;
        return center + manualBoundsOffset;
    }

    private void OnValidate()
    {
        manualBoundsSize = new Vector2(
            Mathf.Max(0f, manualBoundsSize.x),
            Mathf.Max(0f, manualBoundsSize.y));
        destinationObstacleClearance = Mathf.Max(0f, destinationObstacleClearance);
        overlapRecoveryPadding = Mathf.Max(0f, overlapRecoveryPadding);

        waitIntervalRange = new Vector2(
            Mathf.Max(0f, waitIntervalRange.x),
            Mathf.Max(0f, waitIntervalRange.y));
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.15f, 0.8f, 0.95f, 0.75f);

        if (roamArea != null)
        {
            Bounds bounds = roamArea.bounds;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
            return;
        }

        Rect manualBounds = GetManualBounds();
        Vector3 center = new Vector3(manualBounds.center.x, manualBounds.center.y, transform.position.z);
        Vector3 size = new Vector3(manualBounds.width, manualBounds.height, 0f);
        Gizmos.DrawWireCube(center, size);
    }

    private static Vector3 ToNavMeshPosition(Vector2 worldPosition)
    {
        return new Vector3(worldPosition.x, 0f, worldPosition.y);
    }

    private static Vector2 ToWorldPosition(Vector3 navMeshPosition)
    {
        return new Vector2(navMeshPosition.x, navMeshPosition.z);
    }
}

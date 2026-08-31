using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

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
    [SerializeField] private bool detourAroundNearbyDynamicBlockers = true;
    [SerializeField] private bool stopForNearbyDynamicBlockers = true;
    [SerializeField, Min(0f)] private float nearbyDynamicBlockerStopDistance = 0.3f;
    [SerializeField, Min(0f)] private float dynamicBlockerClearance = 0.12f;
    [SerializeField, Min(0f)] private float dynamicBlockerDetourDistance = 0.9f;
    [SerializeField, Min(0f)] private float dynamicBlockerDetourForwardBias = 0.35f;
    [SerializeField, Min(0f)] private float dynamicBlockerStopDistance = 0.6f;
    [SerializeField, Min(0)] private int maxDynamicBlockerRecoveryAttempts = 2;
    [SerializeField, Min(0f)] private float crowdedSpaceEscapeDistance = 1.2f;
    [SerializeField, Min(0f)] private float crowdedSpaceClearance = 0.08f;
    [SerializeField, Min(0)] private int maxCrowdedSpaceEscapeAttempts = 3;
    [SerializeField, Min(0)] private int maxStaticBlockerRecoveryAttempts = 2;
    [SerializeField, Min(0f)] private float overlapRecoveryPadding = 0.04f;
    [SerializeField, Min(0f)] private float blockedMoveThreshold = 0.01f;
    [SerializeField, Min(0.1f)] private float blockedRepathDelay = 0.3f;
    [SerializeField] private LayerMask collisionLayers = ~0;
    [SerializeField] private bool flipSpriteWithMovement = true;

    [Header("Students")]
    [SerializeField] private bool passThroughStudents = true;
    [SerializeField, Min(0f)] private float conversationSeparationPadding = 0.08f;
    [SerializeField, Min(0.1f)] private float conversationSeparationSpeed = 1.25f;
    [SerializeField, Min(0f)] private float conversationFreeSideProbeDistance = 0.5f;

    [Header("Spawn Safety")]
    [SerializeField, Min(0.1f)] private float safeSpawnSampleDistance = 3f;
    [SerializeField, Min(1)] private int safeSpawnOverlapPasses = 5;

    private static readonly Vector3[] EmptyCorners = Array.Empty<Vector3>();

    private Camera targetCamera;
    private NavMeshPath currentPath;
    private readonly List<RaycastHit2D> collisionHits = new List<RaycastHit2D>(8);
    private readonly List<Collider2D> overlapColliders = new List<Collider2D>(8);
    private readonly List<RaycastResult> uiRaycastResults = new List<RaycastResult>(8);
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
    private int crowdedSpaceEscapeAttempts;
    private int staticBlockerRecoveryAttempts;
    private RaycastHit2D latestDynamicBlockerHit;
    private RaycastHit2D latestStaticBlockerHit;
    private Vector3 finalDestination;
    private Vector3 currentDestination;
    private bool hasFinalDestination;
    private bool hasDestination;
    private bool hitDynamicBlockerThisFrame;
    private bool hitStaticBlockerThisFrame;
    private bool isDetouringAroundDynamicBlocker;
    private bool isConversationMovementLocked;
    private bool isSeparatingForConversation;
    private Vector2 conversationSeparationTarget;
    private Collider2D conversationPartner;

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

    private System.Collections.IEnumerator Start()
    {
        // The classroom NavMesh is built during scene startup. Wait one frame, then
        // place AraBOT on its nearest valid point and push it clear of furniture.
        yield return null;
        EnsureSafeSpawnPosition();
    }

    private void EnsureSafeSpawnPosition()
    {
        Vector3 navigationPosition = ToNavMeshPosition(GetNavigationWorldPosition());
        if (NavMesh.SamplePosition(
                navigationPosition,
                out NavMeshHit safePosition,
                safeSpawnSampleDistance,
                NavMesh.AllAreas))
        {
            Vector2 safeRootPosition = ToWorldPosition(safePosition.position) - navigationOffset;
            if (body != null)
            {
                body.position = safeRootPosition;
                body.linearVelocity = Vector2.zero;
            }
            else
            {
                transform.position = new Vector3(safeRootPosition.x, safeRootPosition.y, cachedZ);
            }
        }

        Physics2D.SyncTransforms();
        for (int pass = 0; pass < safeSpawnOverlapPasses; pass++)
        {
            if (!TryResolveStaticOverlap())
            {
                break;
            }

            Physics2D.SyncTransforms();
        }

        currentVelocity = Vector2.zero;
        ClearPath();
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
            UpdateConversationSeparation(Time.fixedDeltaTime);
            return;
        }

        UpdateMovement(Time.fixedDeltaTime);
    }

    public void SetConversationMovementLocked(bool locked, Collider2D conversationPartner = null)
    {
        isConversationMovementLocked = locked;
        if (locked)
        {
            StopForConversation();
            BeginConversationSeparation(conversationPartner);
        }
        else
        {
            isSeparatingForConversation = false;
            this.conversationPartner = null;
        }
    }

    // Converts desktop clicks and mobile taps into 2D world targets sampled against the runtime NavMesh.
    private void HandleClick()
    {
        if (isConversationMovementLocked || !TryGetDestinationPress(out Vector2 screenPosition))
        {
            return;
        }

        if ((DialogueManager.Instance != null && DialogueManager.Instance.IsPlaying)
            || IsPointerOverInteractiveUi(screenPosition))
        {
            return;
        }

        Ray clickRay = targetCamera.ScreenPointToRay(screenPosition);
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

    private static bool TryGetDestinationPress(out Vector2 screenPosition)
    {
#if ENABLE_INPUT_SYSTEM
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            screenPosition = Touchscreen.current.primaryTouch.position.ReadValue();
            return true;
        }

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            screenPosition = Mouse.current.position.ReadValue();
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.touchCount > 0 && Input.GetTouch(0).phase == UnityEngine.TouchPhase.Began)
        {
            screenPosition = Input.GetTouch(0).position;
            return true;
        }

        if (Input.GetMouseButtonDown(0))
        {
            screenPosition = Input.mousePosition;
            return true;
        }
#endif

        screenPosition = Vector2.zero;
        return false;
    }

    private bool IsPointerOverInteractiveUi(Vector2 screenPosition)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            return false;
        }

        uiRaycastResults.Clear();
        eventSystem.RaycastAll(
            new PointerEventData(eventSystem) { position = screenPosition },
            uiRaycastResults);

        for (int index = 0; index < uiRaycastResults.Count; index++)
        {
            GameObject hitObject = uiRaycastResults[index].gameObject;
            if (hitObject != null && hitObject.GetComponentInParent<Selectable>() != null)
            {
                return true;
            }
        }

        return false;
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
        hitStaticBlockerThisFrame = false;
        latestDynamicBlockerHit = default;
        latestStaticBlockerHit = default;

        if (TryResolveStaticOverlap())
        {
            return;
        }

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

        if (TryHandleNearbyDynamicBlocker(desiredVelocity, deltaTime))
        {
            return;
        }

        if (!stopForNearbyDynamicBlockers)
        {
            desiredVelocity = ApplyDynamicBlockerAvoidance(desiredVelocity, deltaTime);
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

        if (hasDestination && desiredVelocity.sqrMagnitude > 0.0001f && movement.sqrMagnitude <= blockedMoveThreshold * blockedMoveThreshold)
        {
            blockedTimer += deltaTime;
            if (blockedTimer >= blockedRepathDelay)
            {
                if (!TryStartCrowdedSpaceEscape(desiredVelocity, currentNavigationPosition)
                    && !TryHandleDynamicBlockerStall(desiredVelocity, currentNavigationPosition))
                {
                    if (!TryHandleStaticBlockerStall())
                    {
                        Vector3 repathDestination = hasFinalDestination ? finalDestination : currentDestination;
                        TrySetPathToDestination(
                            repathDestination,
                            clearPathOnFailure: false,
                            setAsFinalDestination: !isDetouringAroundDynamicBlocker,
                            resetRecoveryAttempts: false);
                    }
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
        if (!hasDestination || repathIntervalSeconds <= 0f || isDetouringAroundDynamicBlocker)
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
            setAsFinalDestination: !isDetouringAroundDynamicBlocker,
            resetRecoveryAttempts: false);
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

    private bool TryHandleNearbyDynamicBlocker(Vector2 desiredVelocity, float deltaTime)
    {
        if ((!detourAroundNearbyDynamicBlockers && !stopForNearbyDynamicBlockers)
            || movementCollider == null
            || desiredVelocity.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        float movementLookAhead = Mathf.Max(currentVelocity.magnitude, desiredVelocity.magnitude) * deltaTime;
        float castDistance = Mathf.Max(nearbyDynamicBlockerStopDistance, movementLookAhead) + collisionSkin;
        if (!TryGetBlockingHit(desiredVelocity.normalized, castDistance, out RaycastHit2D blockingHit)
            || !DynamicMovementBlockerUtility.IsDynamicMovementBlocker(blockingHit.collider, body))
        {
            return false;
        }

        if (detourAroundNearbyDynamicBlockers
            && !isDetouringAroundDynamicBlocker
            && maxDynamicBlockerRecoveryAttempts > 0
            && dynamicBlockerRecoveryAttempts < maxDynamicBlockerRecoveryAttempts
            && TryStartDynamicBlockerDetour(desiredVelocity))
        {
            dynamicBlockerRecoveryAttempts++;
            currentVelocity = Vector2.zero;
            blockedTimer = 0f;

            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
            }

            return true;
        }

        if (!stopForNearbyDynamicBlockers)
        {
            return false;
        }

        currentVelocity = Vector2.zero;
        ClearPath();

        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }

        return true;
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

        hitStaticBlockerThisFrame = true;
        latestStaticBlockerHit = blockingHit;

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

    private bool TryResolveStaticOverlap()
    {
        if (movementCollider == null)
        {
            return false;
        }

        overlapColliders.Clear();
        movementCollider.Overlap(movementContactFilter, overlapColliders);

        Vector2 strongestCorrection = Vector2.zero;
        Vector2 combinedCorrection = Vector2.zero;
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
            Vector2 correction = separation.normal
                * (overlapDepth + Mathf.Max(collisionSkin, overlapRecoveryPadding));
            combinedCorrection += correction;

            if (overlapDepth <= deepestOverlap)
            {
                continue;
            }

            deepestOverlap = overlapDepth;
            strongestCorrection = correction;
        }

        Vector2 correctionToApply = combinedCorrection.sqrMagnitude > 0.000001f
            ? combinedCorrection
            : strongestCorrection;
        if (correctionToApply.sqrMagnitude <= 0.000001f)
        {
            return false;
        }

        Vector2 correctedPosition = GetRootPosition() + correctionToApply;
        currentVelocity = Vector2.zero;
        blockedTimer = 0f;
        repathTimer = 0f;

        if (body != null)
        {
            body.position = correctedPosition;
            body.linearVelocity = Vector2.zero;
        }
        else
        {
            transform.position = new Vector3(correctedPosition.x, correctedPosition.y, cachedZ);
        }

        return true;
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
        return passThroughStudents
            && DynamicMovementBlockerUtility.IsStudent(otherCollider, body);
    }

    private void BeginConversationSeparation(Collider2D partner)
    {
        isSeparatingForConversation = false;
        conversationPartner = partner;
        if (movementCollider == null || partner == null)
        {
            return;
        }

        Physics2D.SyncTransforms();
        ColliderDistance2D separation = movementCollider.Distance(partner);
        if (!separation.isValid || !separation.isOverlapped)
        {
            return;
        }

        Vector2 preferredDirection = (Vector2)movementCollider.bounds.center - (Vector2)partner.bounds.center;
        if (preferredDirection.sqrMagnitude <= 0.0001f)
        {
            preferredDirection = separation.normal.sqrMagnitude > 0.0001f
                ? -separation.normal
                : Vector2.down;
        }

        if (TryChooseConversationSeparationTarget(preferredDirection.normalized, partner, out Vector2 target))
        {
            conversationSeparationTarget = target;
            isSeparatingForConversation = true;
        }
    }

    private bool TryChooseConversationSeparationTarget(
        Vector2 preferredDirection,
        Collider2D partner,
        out Vector2 target)
    {
        Vector2 partnerCenter = partner.bounds.center;
        Vector2 selfCenterOffset = (Vector2)movementCollider.bounds.center - GetRootPosition();
        Vector2[] directions =
        {
            preferredDirection,
            new Vector2(-preferredDirection.y, preferredDirection.x),
            new Vector2(preferredDirection.y, -preferredDirection.x),
            -preferredDirection,
            (preferredDirection + new Vector2(-preferredDirection.y, preferredDirection.x)).normalized,
            (preferredDirection + new Vector2(preferredDirection.y, -preferredDirection.x)).normalized,
            (-preferredDirection + new Vector2(-preferredDirection.y, preferredDirection.x)).normalized,
            (-preferredDirection + new Vector2(preferredDirection.y, -preferredDirection.x)).normalized
        };

        target = GetRootPosition();
        float bestScore = float.NegativeInfinity;
        for (int index = 0; index < directions.Length; index++)
        {
            Vector2 direction = directions[index];
            float selfRadius = GetProjectedRadius(movementCollider.bounds.extents, direction);
            float partnerRadius = GetProjectedRadius(partner.bounds.extents, direction);
            Vector2 candidateCenter = partnerCenter
                + direction * (selfRadius + partnerRadius + conversationSeparationPadding);
            Vector2 candidateRoot = candidateCenter - selfCenterOffset;
            Vector2 movement = candidateRoot - GetRootPosition();
            float movementDistance = movement.magnitude;
            if (movementDistance <= 0.0001f)
            {
                continue;
            }

            float probeDistance = movementDistance + conversationFreeSideProbeDistance;
            float availableDistance = probeDistance;
            if (TryGetBlockingHit(
                    movement / movementDistance,
                    probeDistance + collisionSkin,
                    partner,
                    out RaycastHit2D blockingHit))
            {
                availableDistance = Mathf.Max(0f, blockingHit.distance - collisionSkin);
            }

            if (availableDistance + 0.0001f < movementDistance)
            {
                continue;
            }

            float freeSpace = availableDistance - movementDistance;
            float directionPreference = Vector2.Dot(direction, preferredDirection);
            float score = freeSpace + directionPreference * 0.15f - movementDistance * 0.05f;
            if (score > bestScore)
            {
                bestScore = score;
                target = candidateRoot;
            }
        }

        return bestScore > float.NegativeInfinity;
    }

    private void UpdateConversationSeparation(float deltaTime)
    {
        if (!isSeparatingForConversation || movementCollider == null || conversationPartner == null)
        {
            return;
        }

        Vector2 currentPosition = GetRootPosition();
        Vector2 toTarget = conversationSeparationTarget - currentPosition;
        float remainingDistance = toTarget.magnitude;
        if (remainingDistance <= 0.001f)
        {
            isSeparatingForConversation = false;
            return;
        }

        Vector2 direction = toTarget / remainingDistance;
        float requestedDistance = Mathf.Min(conversationSeparationSpeed * deltaTime, remainingDistance);
        if (TryGetBlockingHit(
                direction,
                requestedDistance + collisionSkin,
                conversationPartner,
                out RaycastHit2D blockingHit))
        {
            requestedDistance = Mathf.Clamp(blockingHit.distance - collisionSkin, 0f, requestedDistance);
        }

        if (requestedDistance <= 0.0001f)
        {
            isSeparatingForConversation = false;
            return;
        }

        Vector2 nextPosition = currentPosition + direction * requestedDistance;
        if (body != null)
        {
            body.position = nextPosition;
            body.linearVelocity = Vector2.zero;
        }
        else
        {
            transform.position = new Vector3(nextPosition.x, nextPosition.y, cachedZ);
        }
    }

    private static float GetProjectedRadius(Vector3 extents, Vector2 direction)
    {
        return Mathf.Abs(direction.x) * extents.x + Mathf.Abs(direction.y) * extents.y;
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
                setAsFinalDestination: true,
                resetRecoveryAttempts: false))
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
        crowdedSpaceEscapeAttempts = 0;
        staticBlockerRecoveryAttempts = 0;
        hitDynamicBlockerThisFrame = false;
        hitStaticBlockerThisFrame = false;
        latestDynamicBlockerHit = default;
        latestStaticBlockerHit = default;
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
        bool setAsFinalDestination,
        bool resetRecoveryAttempts = true)
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
            if (resetRecoveryAttempts)
            {
                dynamicBlockerRecoveryAttempts = 0;
                crowdedSpaceEscapeAttempts = 0;
                staticBlockerRecoveryAttempts = 0;
            }
        }

        return true;
    }

    private bool TryHandleStaticBlockerStall()
    {
        if (!hitStaticBlockerThisFrame || latestStaticBlockerHit.collider == null)
        {
            return false;
        }

        staticBlockerRecoveryAttempts++;
        if (maxStaticBlockerRecoveryAttempts <= 0
            || staticBlockerRecoveryAttempts > maxStaticBlockerRecoveryAttempts)
        {
            ClearPath();
            currentVelocity = Vector2.zero;
            return true;
        }

        Vector3 repathDestination = hasFinalDestination ? finalDestination : currentDestination;
        TrySetPathToDestination(
            repathDestination,
            clearPathOnFailure: false,
            setAsFinalDestination: !isDetouringAroundDynamicBlocker,
            resetRecoveryAttempts: false);
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

    private bool TryStartCrowdedSpaceEscape(Vector2 desiredVelocity, Vector2 currentNavigationPosition)
    {
        if (!hasFinalDestination
            || crowdedSpaceEscapeDistance <= 0f
            || maxCrowdedSpaceEscapeAttempts <= 0
            || crowdedSpaceEscapeAttempts >= maxCrowdedSpaceEscapeAttempts)
        {
            return false;
        }

        Vector2 forwardDirection = GetDynamicBlockerForwardDirection(desiredVelocity, currentNavigationPosition);
        if (forwardDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        crowdedSpaceEscapeAttempts++;

        Vector2 preferredSide = GetCrowdedSpacePreferredSide(forwardDirection, currentNavigationPosition);
        if (TrySetCrowdedSpaceEscapeRoute(currentNavigationPosition, preferredSide)
            || TrySetCrowdedSpaceEscapeRoute(currentNavigationPosition, -preferredSide)
            || TrySetCrowdedSpaceEscapeRoute(currentNavigationPosition, -forwardDirection))
        {
            return true;
        }

        return false;
    }

    private Vector2 GetCrowdedSpacePreferredSide(Vector2 forwardDirection, Vector2 currentNavigationPosition)
    {
        if (TryGetBlockingHit(
                forwardDirection,
                dynamicBlockerAvoidanceDistance + collisionSkin,
                out RaycastHit2D blockingHit)
            && blockingHit.collider != null)
        {
            return DynamicMovementBlockerUtility.GetPreferredAvoidanceDirection(
                forwardDirection,
                currentNavigationPosition,
                blockingHit.collider.bounds.center);
        }

        return new Vector2(-forwardDirection.y, forwardDirection.x);
    }

    private bool TrySetCrowdedSpaceEscapeRoute(Vector2 currentNavigationPosition, Vector2 escapeDirection)
    {
        if (escapeDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        Vector2 escapePosition = currentNavigationPosition
            + escapeDirection.normalized * crowdedSpaceEscapeDistance;
        if (!HasPhysicalClearanceAt(escapePosition))
        {
            return false;
        }

        return TrySetPathToDestination(
            ToNavMeshPosition(escapePosition),
            clearPathOnFailure: false,
            setAsFinalDestination: false,
            resetRecoveryAttempts: false);
    }

    private bool HasPhysicalClearanceAt(Vector2 navigationPosition)
    {
        if (movementCollider == null)
        {
            return true;
        }

        Vector2 testSize = (Vector2)movementCollider.bounds.size
            + (Vector2.one * crowdedSpaceClearance * 2f);
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
                || otherCollider.attachedRigidbody == body)
            {
                continue;
            }

            return false;
        }

        return true;
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
        Vector2 normalizedForward = forwardDirection.normalized;
        Vector2 normalizedSide = sideDirection.normalized;
        float lateralDistance = blockerRadius + dynamicBlockerDetourDistance;
        float approachDistance = blockerRadius + Mathf.Max(dynamicBlockerClearance, collisionSkin);
        float exitDistance = blockerRadius + Mathf.Max(dynamicBlockerDetourForwardBias, dynamicBlockerClearance);
        float currentForwardOffset = Vector2.Dot(
            GetNavigationWorldPosition() - blockerCenter,
            normalizedForward);
        float entryForwardOffset = Mathf.Min(currentForwardOffset, -approachDistance);
        Vector2 entryWorldPosition = blockerCenter
            + normalizedForward * entryForwardOffset
            + normalizedSide * lateralDistance;
        Vector2 exitWorldPosition = blockerCenter
            + normalizedForward * exitDistance
            + normalizedSide * lateralDistance;

        if (!TrySampleClearDetourPoint(entryWorldPosition, out NavMeshHit sampledEntry)
            || !TrySampleClearDetourPoint(exitWorldPosition, out NavMeshHit sampledExit)
            || !NavMesh.SamplePosition(
                ToNavMeshPosition(GetNavigationWorldPosition()),
                out NavMeshHit sampledStart,
                destinationSampleDistance,
                NavMesh.AllAreas)
            || !TryCalculateClearPath(sampledStart.position, sampledEntry.position, out NavMeshPath entryPath)
            || !TryCalculateClearPath(sampledEntry.position, sampledExit.position, out NavMeshPath exitPath))
        {
            return false;
        }

        List<Vector3> detourCorners = new List<Vector3>(entryPath.corners.Length + exitPath.corners.Length - 1);
        detourCorners.AddRange(entryPath.corners);
        for (int cornerIndex = 1; cornerIndex < exitPath.corners.Length; cornerIndex++)
        {
            detourCorners.Add(exitPath.corners[cornerIndex]);
        }

        currentCorners = detourCorners.ToArray();
        currentCornerIndex = 1;
        currentDestination = sampledExit.position;
        hasDestination = true;
        isDetouringAroundDynamicBlocker = true;
        repathTimer = repathIntervalSeconds;
        blockedTimer = 0f;
        return true;
    }

    private bool TrySampleClearDetourPoint(Vector2 worldPosition, out NavMeshHit sampledPosition)
    {
        return NavMesh.SamplePosition(
                ToNavMeshPosition(worldPosition),
                out sampledPosition,
                destinationSampleDistance,
                NavMesh.AllAreas)
            && HasPhysicalClearanceAt(ToWorldPosition(sampledPosition.position));
    }

    private bool TryCalculateClearPath(Vector3 start, Vector3 destination, out NavMeshPath path)
    {
        path = new NavMeshPath();
        return NavMesh.CalculatePath(start, destination, NavMesh.AllAreas, path)
            && path.status == NavMeshPathStatus.PathComplete
            && path.corners != null
            && path.corners.Length >= 2
            && HasPhysicalClearanceAlongPath(path);
    }

    private bool HasPhysicalClearanceAlongPath(NavMeshPath path)
    {
        Vector2 previousPosition = ToWorldPosition(path.corners[0]);
        Vector2 testSize = (Vector2)movementCollider.bounds.size
            + Vector2.one * dynamicBlockerClearance * 2f;
        float testAngle = movementCollider.transform.eulerAngles.z;

        for (int cornerIndex = 1; cornerIndex < path.corners.Length; cornerIndex++)
        {
            Vector2 cornerPosition = ToWorldPosition(path.corners[cornerIndex]);
            Vector2 segment = cornerPosition - previousPosition;
            float segmentDistance = segment.magnitude;
            if (segmentDistance <= 0.0001f)
            {
                continue;
            }

            collisionHits.Clear();
            Physics2D.BoxCast(
                previousPosition,
                testSize,
                testAngle,
                segment / segmentDistance,
                movementContactFilter,
                collisionHits,
                segmentDistance);

            for (int hitIndex = 0; hitIndex < collisionHits.Count; hitIndex++)
            {
                Collider2D hitCollider = collisionHits[hitIndex].collider;
                if (hitCollider == null
                    || hitCollider == movementCollider
                    || hitCollider.isTrigger
                    || hitCollider.attachedRigidbody == body)
                {
                    continue;
                }

                return false;
            }

            previousPosition = cornerPosition;
        }

        return true;
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

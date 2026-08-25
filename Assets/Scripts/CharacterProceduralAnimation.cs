using UnityEngine;

[AddComponentMenu("Animation/Character Procedural Animation")]
[DisallowMultipleComponent]
public class CharacterProceduralAnimation : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform head;
    [SerializeField] private Transform antennaRoot;
    [SerializeField] private Transform torso;
    [SerializeField] private Transform leftEye;
    [SerializeField] private Transform rightEye;
    [Tooltip("Optional. Leave wheel references empty for characters without wheels.")]
    [SerializeField] private Transform leftWheel;
    [SerializeField] private Transform rightWheel;

    [Header("Blink")]
    [Tooltip("Random time range, in seconds, between blinks.")]
    [SerializeField] private Vector2 blinkInterval = new Vector2(2.5f, 5f);
    [Tooltip("Total time for the eyes to close and reopen.")]
    [SerializeField, Min(0.02f)] private float blinkDuration = 0.14f;
    [Tooltip("Vertical eye scale at the most closed point. Keep this above zero for a visible line.")]
    [SerializeField, Range(0.05f, 1f)] private float closedEyeScale = 0.22f;
    [Tooltip("Makes the closed eyes wider for a cartoony horizontal-line blink.")]
    [SerializeField, Range(1f, 1.5f)] private float blinkHorizontalStretch = 1.15f;

    [Header("Eye Look")]
    [Tooltip("How far the eyes move toward the character's travel direction.")]
    [SerializeField, Min(0f)] private float eyeLookDistance = 0.075f;
    [Tooltip("How softly the eyes move and return to center.")]
    [SerializeField, Min(0.01f)] private float eyeLookSmoothTime = 0.08f;

    [Header("Head Bob")]
    [Tooltip("Vertical distance the head moves while idle.")]
    [SerializeField, Min(0f)] private float idleBobAmount = 0.035f;
    [Tooltip("Vertical distance the head moves at full movement speed.")]
    [SerializeField, Min(0f)] private float movingBobAmount = 0.06f;
    [Tooltip("Idle bob cycles per second.")]
    [SerializeField, Min(0f)] private float idleBobSpeed = 1.2f;
    [Tooltip("Moving bob cycles per second.")]
    [SerializeField, Min(0f)] private float movingBobSpeed = 3f;

    [Header("Torso Motion")]
    [Tooltip("Small idle movement so the body does not feel frozen.")]
    [SerializeField, Min(0f)] private float idleTorsoBobAmount = 0.01f;
    [Tooltip("Torso movement at full travel speed.")]
    [SerializeField, Min(0f)] private float movingTorsoBobAmount = 0.028f;
    [Tooltip("Maximum torso lean opposite horizontal travel.")]
    [SerializeField, Min(0f)] private float movingTorsoLeanAngle = 2f;
    [Tooltip("How softly the torso enters and exits its lean.")]
    [SerializeField, Min(0.01f)] private float torsoLeanSmoothTime = 0.12f;

    [Header("Motion Response")]
    [Tooltip("Travel speed where moving animation reaches full strength.")]
    [SerializeField, Min(0.01f)] private float speedForFullMovementAnimation = 3.5f;
    [Tooltip("How quickly idle and moving animation settings blend together.")]
    [SerializeField, Min(0f)] private float movementBlendSpeed = 5f;
    [Tooltip("Smooths tiny frame-to-frame changes in detected movement.")]
    [SerializeField, Min(0f)] private float detectedVelocitySmoothing = 16f;

    [Header("Wheels (Optional)")]
    [Tooltip("Wheel rotation for each world unit traveled. About 345 matches AraBOT's wheel size.")]
    [SerializeField, Min(0f)] private float wheelDegreesPerUnit = 345f;

    private Vector3 headBaseLocalPosition;
    private Vector3 antennaBaseLocalPosition;
    private Vector3 torsoBaseLocalPosition;
    private Vector3 torsoBaseLocalEulerAngles;
    private Vector3 leftEyeBaseLocalPosition;
    private Vector3 rightEyeBaseLocalPosition;
    private Vector3 leftEyeBaseLocalScale;
    private Vector3 rightEyeBaseLocalScale;
    private Quaternion leftWheelBaseLocalRotation;
    private Quaternion rightWheelBaseLocalRotation;

    private Vector3 previousWorldPosition;
    private Vector2 detectedVelocity;
    private Vector2 eyeLookOffset;
    private Vector2 eyeLookSmoothVelocity;
    private float torsoLeanVelocity;
    private float blinkTimer;
    private float blinkElapsed;
    private float bobPhase;
    private float movementBlend;
    private float wheelSpinAngle;
    private float wheelDirection = 1f;
    private bool isBlinking;
    private Transform lookTargetOverride;

    private void Awake()
    {
        if (head != null)
        {
            headBaseLocalPosition = head.localPosition;
        }

        if (antennaRoot != null)
        {
            antennaBaseLocalPosition = antennaRoot.localPosition;
        }

        if (torso != null)
        {
            torsoBaseLocalPosition = torso.localPosition;
            torsoBaseLocalEulerAngles = torso.localEulerAngles;
        }

        if (leftEye != null)
        {
            leftEyeBaseLocalPosition = leftEye.localPosition;
            leftEyeBaseLocalScale = leftEye.localScale;
        }

        if (rightEye != null)
        {
            rightEyeBaseLocalPosition = rightEye.localPosition;
            rightEyeBaseLocalScale = rightEye.localScale;
        }

        if (leftWheel != null)
        {
            leftWheelBaseLocalRotation = leftWheel.localRotation;
        }

        if (rightWheel != null)
        {
            rightWheelBaseLocalRotation = rightWheel.localRotation;
        }

        previousWorldPosition = transform.position;
        ScheduleNextBlink();
    }

    private void OnEnable()
    {
        previousWorldPosition = transform.position;
    }

    private void LateUpdate()
    {
        float deltaTime = Time.deltaTime;
        Vector3 currentWorldPosition = transform.position;
        Vector2 frameTravel = currentWorldPosition - previousWorldPosition;
        previousWorldPosition = currentWorldPosition;

        Vector2 frameVelocity = deltaTime > 0f ? frameTravel / deltaTime : Vector2.zero;
        float velocityBlend = detectedVelocitySmoothing > 0f
            ? 1f - Mathf.Exp(-detectedVelocitySmoothing * deltaTime)
            : 1f;
        detectedVelocity = Vector2.Lerp(detectedVelocity, frameVelocity, velocityBlend);

        float movementSpeed = detectedVelocity.magnitude;
        UpdateMovementBlend(movementSpeed, deltaTime);
        UpdateBlink(deltaTime);
        UpdateEyeLook(movementSpeed, deltaTime);
        UpdateBodyMotion(deltaTime);
        UpdateWheels(frameTravel);
    }

    private void OnDisable()
    {
        ApplyEyeBlink(1f);
        ResetAnimatedTransforms();
    }

    private void UpdateMovementBlend(float movementSpeed, float deltaTime)
    {
        float targetBlend = Mathf.Clamp01(movementSpeed / speedForFullMovementAnimation);
        movementBlend = Mathf.MoveTowards(movementBlend, targetBlend, movementBlendSpeed * deltaTime);
    }

    private void UpdateBlink(float deltaTime)
    {
        if (!isBlinking)
        {
            blinkTimer -= deltaTime;
            if (blinkTimer <= 0f)
            {
                isBlinking = true;
                blinkElapsed = 0f;
            }

            return;
        }

        blinkElapsed += deltaTime;
        float blinkProgress = Mathf.Clamp01(blinkElapsed / blinkDuration);
        float eyeOpenness = Mathf.Abs((blinkProgress * 2f) - 1f);
        ApplyEyeBlink(Mathf.SmoothStep(0f, 1f, eyeOpenness));

        if (blinkProgress >= 1f)
        {
            isBlinking = false;
            ApplyEyeBlink(1f);
            ScheduleNextBlink();
        }
    }

    private void ApplyEyeBlink(float openness)
    {
        float verticalScale = Mathf.Lerp(closedEyeScale, 1f, openness);
        float horizontalScale = Mathf.Lerp(blinkHorizontalStretch, 1f, openness);

        if (leftEye != null)
        {
            leftEye.localScale = new Vector3(
                leftEyeBaseLocalScale.x * horizontalScale,
                leftEyeBaseLocalScale.y * verticalScale,
                leftEyeBaseLocalScale.z);
        }

        if (rightEye != null)
        {
            rightEye.localScale = new Vector3(
                rightEyeBaseLocalScale.x * horizontalScale,
                rightEyeBaseLocalScale.y * verticalScale,
                rightEyeBaseLocalScale.z);
        }
    }

    private void ScheduleNextBlink()
    {
        float minimumInterval = Mathf.Min(blinkInterval.x, blinkInterval.y);
        float maximumInterval = Mathf.Max(blinkInterval.x, blinkInterval.y);
        blinkTimer = Random.Range(minimumInterval, maximumInterval);
    }

    private void UpdateEyeLook(float movementSpeed, float deltaTime)
    {
        Vector2 targetOffset = Vector2.zero;
        if (lookTargetOverride != null)
        {
            Vector2 toLookTarget = (Vector2)(lookTargetOverride.position - transform.position);
            if (toLookTarget.sqrMagnitude > 0.0001f)
            {
                Vector3 localDirection = transform.InverseTransformDirection(toLookTarget.normalized);
                targetOffset = new Vector2(localDirection.x, localDirection.y) * eyeLookDistance;
            }
        }
        else if (detectedVelocity.sqrMagnitude > 0.0025f)
        {
            Vector3 localDirection = transform.InverseTransformDirection(detectedVelocity.normalized);
            float lookStrength = Mathf.Clamp01(movementSpeed / speedForFullMovementAnimation);
            targetOffset = new Vector2(localDirection.x, localDirection.y) * eyeLookDistance * lookStrength;
        }

        eyeLookOffset = Vector2.SmoothDamp(
            eyeLookOffset,
            targetOffset,
            ref eyeLookSmoothVelocity,
            eyeLookSmoothTime,
            Mathf.Infinity,
            deltaTime);

        if (leftEye != null)
        {
            leftEye.localPosition = leftEyeBaseLocalPosition + (Vector3)eyeLookOffset;
        }

        if (rightEye != null)
        {
            rightEye.localPosition = rightEyeBaseLocalPosition + (Vector3)eyeLookOffset;
        }
    }

    private void UpdateBodyMotion(float deltaTime)
    {
        float bobAmount = Mathf.Lerp(idleBobAmount, movingBobAmount, movementBlend);
        float bobSpeed = Mathf.Lerp(idleBobSpeed, movingBobSpeed, movementBlend);
        bobPhase += bobSpeed * Mathf.PI * 2f * deltaTime;

        float headBobOffset = Mathf.Sin(bobPhase) * bobAmount;
        if (head != null)
        {
            head.localPosition = headBaseLocalPosition + (Vector3.up * headBobOffset);
        }

        if (antennaRoot != null)
        {
            antennaRoot.localPosition = antennaBaseLocalPosition + (Vector3.up * headBobOffset);
        }

        if (torso == null)
        {
            return;
        }

        float torsoBobAmount = Mathf.Lerp(idleTorsoBobAmount, movingTorsoBobAmount, movementBlend);
        float torsoBobOffset = Mathf.Sin(bobPhase - 0.3f) * torsoBobAmount;
        torso.localPosition = torsoBaseLocalPosition + (Vector3.up * torsoBobOffset);

        Vector3 localMovementDirection = detectedVelocity.sqrMagnitude > 0.0025f
            ? transform.InverseTransformDirection(detectedVelocity.normalized)
            : Vector3.zero;
        float horizontalDirection = localMovementDirection.x;
        float targetLean = torsoBaseLocalEulerAngles.z -
            (horizontalDirection * movingTorsoLeanAngle * movementBlend);
        float currentLean = torso.localEulerAngles.z;
        float nextLean = Mathf.SmoothDampAngle(
            currentLean,
            targetLean,
            ref torsoLeanVelocity,
            torsoLeanSmoothTime,
            Mathf.Infinity,
            deltaTime);
        torso.localRotation = Quaternion.Euler(
            torsoBaseLocalEulerAngles.x,
            torsoBaseLocalEulerAngles.y,
            nextLean);
    }

    private void UpdateWheels(Vector2 frameTravel)
    {
        float travelDistance = frameTravel.magnitude;
        if (travelDistance <= 0.00001f)
        {
            return;
        }

        // Moving right rolls clockwise; moving left rolls counter-clockwise.
        if (Mathf.Abs(frameTravel.x) > travelDistance * 0.1f)
        {
            wheelDirection = Mathf.Sign(frameTravel.x);
        }

        wheelSpinAngle = Mathf.Repeat(
            wheelSpinAngle - (travelDistance * wheelDegreesPerUnit * wheelDirection),
            360f);
        Quaternion spinRotation = Quaternion.AngleAxis(wheelSpinAngle, Vector3.forward);

        if (leftWheel != null)
        {
            leftWheel.localRotation = leftWheelBaseLocalRotation * spinRotation;
        }

        if (rightWheel != null)
        {
            rightWheel.localRotation = rightWheelBaseLocalRotation * spinRotation;
        }
    }

    private void ResetAnimatedTransforms()
    {
        if (head != null)
        {
            head.localPosition = headBaseLocalPosition;
        }

        if (antennaRoot != null)
        {
            antennaRoot.localPosition = antennaBaseLocalPosition;
        }

        if (torso != null)
        {
            torso.localPosition = torsoBaseLocalPosition;
            torso.localRotation = Quaternion.Euler(torsoBaseLocalEulerAngles);
        }

        if (leftEye != null)
        {
            leftEye.localPosition = leftEyeBaseLocalPosition;
        }

        if (rightEye != null)
        {
            rightEye.localPosition = rightEyeBaseLocalPosition;
        }

        if (leftWheel != null)
        {
            leftWheel.localRotation = leftWheelBaseLocalRotation;
        }

        if (rightWheel != null)
        {
            rightWheel.localRotation = rightWheelBaseLocalRotation;
        }
    }

    public void SetLookTarget(Transform lookTarget)
    {
        lookTargetOverride = lookTarget;
    }

    public void ClearLookTarget(Transform lookTarget = null)
    {
        if (lookTarget != null && lookTargetOverride != lookTarget)
        {
            return;
        }

        lookTargetOverride = null;
    }
}

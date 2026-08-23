using UnityEngine;

[DisallowMultipleComponent]
public sealed class AntennaSway : MonoBehaviour
{
    [SerializeField] private AraBotClickToMove movementSource;
    [SerializeField] private float maxSwayAngle = 14f;
    [SerializeField] private float speedForMaxSway = 3.5f;
    [SerializeField] private float swaySmoothTime = 0.12f;

    private Vector3 baseLocalEulerAngles;
    private float swayVelocity;

    private void Awake()
    {
        if (movementSource == null)
        {
            movementSource = GetComponentInParent<AraBotClickToMove>();
        }

        baseLocalEulerAngles = transform.localEulerAngles;
    }

    private void Update()
    {
        float horizontalSpeed = movementSource != null ? movementSource.CurrentVelocity.x : 0f;
        float normalizedSpeed = Mathf.Clamp(horizontalSpeed / Mathf.Max(speedForMaxSway, 0.01f), -1f, 1f);
        float targetZAngle = baseLocalEulerAngles.z - (normalizedSpeed * maxSwayAngle);
        float currentZAngle = transform.localEulerAngles.z;
        float nextZAngle = Mathf.SmoothDampAngle(currentZAngle, targetZAngle, ref swayVelocity, swaySmoothTime);

        transform.localRotation = Quaternion.Euler(baseLocalEulerAngles.x, baseLocalEulerAngles.y, nextZAngle);
    }
}

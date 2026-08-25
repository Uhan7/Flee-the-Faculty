using UnityEngine;

public static class DynamicMovementBlockerUtility
{
    public static bool IsDynamicMovementBlocker(Collider2D collider, Rigidbody2D selfBody = null)
    {
        if (collider == null)
        {
            return false;
        }

        Rigidbody2D attachedBody = collider.attachedRigidbody;
        if (attachedBody == null || attachedBody == selfBody)
        {
            return false;
        }

        return attachedBody.TryGetComponent(out AraBotClickToMove _)
            || attachedBody.TryGetComponent(out StudentRoamingController _);
    }

    public static Vector2 GetPreferredAvoidanceDirection(
        Vector2 desiredDirection,
        Vector2 selfPosition,
        Vector2 blockerPosition)
    {
        if (desiredDirection.sqrMagnitude <= 0.0001f)
        {
            return Vector2.zero;
        }

        Vector2 normalizedDesiredDirection = desiredDirection.normalized;
        Vector2 perpendicular = new Vector2(-normalizedDesiredDirection.y, normalizedDesiredDirection.x);
        Vector2 toBlocker = blockerPosition - selfPosition;
        float blockerSide = (normalizedDesiredDirection.x * toBlocker.y) - (normalizedDesiredDirection.y * toBlocker.x);

        if (Mathf.Abs(blockerSide) <= 0.0001f)
        {
            return perpendicular;
        }

        return blockerSide > 0f ? -perpendicular : perpendicular;
    }
}

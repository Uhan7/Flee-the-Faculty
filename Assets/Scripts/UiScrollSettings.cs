using UnityEngine.UI;

/// <summary>Shared wheel speed in UI units, independent of the number of rows.</summary>
public static class UiScrollSettings
{
    public const float WheelSensitivity = 2f;

    public static void Apply(ScrollRect scroll)
    {
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = WheelSensitivity;
        scroll.inertia = false;
    }
}

using System;
using UnityEngine;

/// <summary>
/// Keeps development shortcuts out of normal play unless they have been
/// deliberately unlocked from the title screen.
/// </summary>
public static class DebugModeStore
{
    private const string PreferenceKey = "Flee.DebugModeEnabled";

    public static event Action<bool> Changed;

    public static bool IsEnabled => PlayerPrefs.GetInt(PreferenceKey, 0) == 1;

    public static void SetEnabled(bool enabled)
    {
        if (IsEnabled == enabled)
        {
            return;
        }

        PlayerPrefs.SetInt(PreferenceKey, enabled ? 1 : 0);
        PlayerPrefs.Save();
        Changed?.Invoke(enabled);
    }
}

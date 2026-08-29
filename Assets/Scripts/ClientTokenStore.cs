using UnityEngine;

/// <summary>
/// Holds the service's access code, typed in by whoever is playing.
///
/// The code used to be a text file compiled into the build. That works and it
/// is readable: a WebGL build is a download, and everything in it belongs to
/// whoever downloaded it. Anyone who took a copy of the deployed game took the
/// code with it and could spend the Classroom budget.
///
/// Typing it instead keeps it out of the build entirely. It is still a shared
/// code rather than an account: everyone given it has the same access, sharing
/// it hands that access on, and changing it locks out everyone at once. What it
/// buys is that a copy of the game is no longer a copy of the code.
///
/// It is kept per browser, in <c>PlayerPrefs</c>, so it is typed once and not
/// once per session. Nothing here writes it to a log.
/// </summary>
public static class ClientTokenStore
{
    private const string PreferenceKey = "Flee.ClientToken";

    /// <summary>Fires when the code changes, so any open screen can catch up.</summary>
    public static event System.Action Changed;

    /// <summary>The stored code, or empty when nobody has entered one.</summary>
    public static string Token => PlayerPrefs.GetString(PreferenceKey, string.Empty).Trim();

    /// <summary>True once a code has been entered on this browser.</summary>
    public static bool HasToken => !string.IsNullOrEmpty(Token);

    /// <summary>
    /// Save a code, or clear it when the text is blank.
    ///
    /// Whitespace is trimmed because the usual way to get one of these is to
    /// copy it, and a copied line often brings a space or a newline with it.
    /// </summary>
    public static void Save(string token)
    {
        string trimmed = token == null ? string.Empty : token.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            PlayerPrefs.DeleteKey(PreferenceKey);
        }
        else
        {
            PlayerPrefs.SetString(PreferenceKey, trimmed);
        }

        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// A version safe to show on screen: the length and the last four
    /// characters, which is enough to tell two codes apart without putting one
    /// where a screenshot or a shoulder can reach it.
    /// </summary>
    public static string Describe()
    {
        string token = Token;
        if (string.IsNullOrEmpty(token))
        {
            return "No code saved yet.";
        }

        string tail = token.Length <= 4 ? token : token.Substring(token.Length - 4);
        return $"Saved: {token.Length} characters ending {tail}.";
    }
}

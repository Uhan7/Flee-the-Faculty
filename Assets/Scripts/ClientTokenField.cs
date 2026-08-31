using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Settings box where the access code is typed.
///
/// Wired to a <see cref="ClientTokenStore"/> rather than to the API client, so
/// this knows nothing about requests and the client knows nothing about UI.
///
/// The field starts empty even when a code is saved. Showing the saved one
/// would put it on screen for anyone watching, and would be a fresh copy of it
/// on every screenshot; <see cref="ClientTokenStore.Describe"/> says enough to
/// tell whether the right code is in without showing it.
/// </summary>
[DisallowMultipleComponent]
public sealed class ClientTokenField : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private TMP_InputField input;
    [SerializeField] private Button saveButton;
    [SerializeField] private Button clearButton;
    [SerializeField] private TMP_Text status;

    private void Reset()
    {
        input = GetComponentInChildren<TMP_InputField>(true);
    }

    private void OnEnable()
    {
        if (input != null)
        {
            input.text = string.Empty;
            input.contentType = TMP_InputField.ContentType.Password;
            input.onSubmit.AddListener(SaveText);
        }

        if (saveButton != null)
        {
            saveButton.onClick.AddListener(SaveFromField);
        }

        if (clearButton != null)
        {
            clearButton.onClick.AddListener(Clear);
        }

        ClientTokenStore.Changed += RefreshStatus;
        RefreshStatus();
    }

    private void OnDisable()
    {
        if (input != null)
        {
            input.onSubmit.RemoveListener(SaveText);
            // Do not leave a typed code sitting in the field for the next
            // person to open Settings.
            input.text = string.Empty;
        }

        if (saveButton != null)
        {
            saveButton.onClick.RemoveListener(SaveFromField);
        }

        if (clearButton != null)
        {
            clearButton.onClick.RemoveListener(Clear);
        }

        ClientTokenStore.Changed -= RefreshStatus;
    }

    /// <summary>Save whatever is in the box. Bound to the button and to Enter.</summary>
    public void SaveFromField()
    {
        SaveText(input != null ? input.text : string.Empty);
    }

    /// <summary>Forget the saved code on this browser.</summary>
    public void Clear()
    {
        ClientTokenStore.Save(string.Empty);
        if (input != null)
        {
            input.text = string.Empty;
        }
    }

    private void SaveText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            RefreshStatus();
            return;
        }

        ClientTokenStore.Save(text);
        if (input != null)
        {
            input.text = string.Empty;
        }
    }

    private void RefreshStatus()
    {
        if (status != null)
        {
            status.text = ClientTokenStore.Describe();
        }
    }
}

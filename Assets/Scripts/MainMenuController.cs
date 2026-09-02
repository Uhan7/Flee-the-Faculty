using UnityEngine;

[DisallowMultipleComponent]
public sealed class MainMenuController : MonoBehaviour
{
    // UI
    [Header("UI")]
    [SerializeField] private GameObject accessPanel;
    [SerializeField] private GameObject settingsPanel;

    // Face references
    [Header("Face")]
    [SerializeField] private RectTransform head;
    [SerializeField] private RectTransform leftEye;
    [SerializeField] private RectTransform rightEye;
    [SerializeField] private RectTransform mouth;

    // Eye motion
    [Header("Eye Follow")]
    [SerializeField, Min(0f)] private float eyeHorizontalRange = 6f;
    [SerializeField, Min(0f)] private float eyeVerticalRange = 4f;
    [SerializeField, Min(0.01f)] private float eyeFollowSmoothing = 14f;
    [SerializeField, Min(0f)] private float eyeDistanceForFullTravel = 140f;

    // Mouth hover hold
    [Header("Mouth Hover Hold")]
    [SerializeField] private float mouthLeanAngle = 10f;
    [SerializeField, Min(0.1f)] private float closedScaleY = 1f;
    [SerializeField, Min(0.1f)] private float openScaleY = 1.75f;
    [SerializeField, Min(0.1f)] private float wideScaleX = 1.02f;
    [SerializeField, Min(0.1f)] private float narrowScaleX = 0.95f;
    [SerializeField, Min(1f)] private float mouthSmoothing = 18f;

    private Vector2 leftEyeBasePosition;
    private Vector2 rightEyeBasePosition;
    private Vector3 mouthBaseScale;
    private Quaternion mouthBaseRotation;
    private int hoverSourceCount;

    private void Awake()
    {
        ResolveReferences();

        if (leftEye != null)
        {
            leftEyeBasePosition = leftEye.anchoredPosition;
        }

        if (rightEye != null)
        {
            rightEyeBasePosition = rightEye.anchoredPosition;
        }

        if (mouth != null)
        {
            mouthBaseScale = mouth.localScale;
            mouthBaseRotation = mouth.localRotation;
        }

        ClosePanels();
    }

    private void Update()
    {
        UpdateEyes();
        UpdateMouth();
    }

    public void ToggleSettingsPanel()
    {
        if (settingsPanel == null)
        {
            Debug.Log("MainMenuController settings button clicked.");
            return;
        }

        bool shouldOpen = !settingsPanel.activeSelf;
        ClosePanels();
        settingsPanel.SetActive(shouldOpen);
    }

    public void CloseSettingsPanel()
    {
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
        }
    }

    public void ToggleAccessPanel()
    {
        if (accessPanel == null)
        {
            Debug.Log("MainMenuController access code button clicked.");
            return;
        }

        bool shouldOpen = !accessPanel.activeSelf;
        ClosePanels();
        accessPanel.SetActive(shouldOpen);
    }

    public void CloseAccessPanel()
    {
        if (accessPanel != null)
        {
            accessPanel.SetActive(false);
        }
    }

    public void ClosePanels()
    {
        CloseAccessPanel();
        CloseSettingsPanel();
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void SetHoverReaction(bool isHovering)
    {
        if (isHovering)
        {
            hoverSourceCount++;
            return;
        }

        hoverSourceCount = Mathf.Max(0, hoverSourceCount - 1);
    }

    private void UpdateEyes()
    {
        if (head == null || leftEye == null || rightEye == null)
        {
            return;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                head,
                Input.mousePosition,
                null,
                out Vector2 localCursorPosition))
        {
            return;
        }

        Vector2 leftTarget = leftEyeBasePosition + GetEyeOffset(localCursorPosition - leftEyeBasePosition);
        Vector2 rightTarget = rightEyeBasePosition + GetEyeOffset(localCursorPosition - rightEyeBasePosition);

        float lerpFactor = GetLerpFactor(eyeFollowSmoothing, Time.unscaledDeltaTime);
        leftEye.anchoredPosition = Vector2.Lerp(leftEye.anchoredPosition, leftTarget, lerpFactor);
        rightEye.anchoredPosition = Vector2.Lerp(rightEye.anchoredPosition, rightTarget, lerpFactor);
    }

    private void UpdateMouth()
    {
        if (mouth == null)
        {
            return;
        }

        float mouthBlend = hoverSourceCount > 0 ? 1f : 0f;

        Vector3 targetScale = new Vector3(
            mouthBaseScale.x * Mathf.Lerp(wideScaleX, narrowScaleX, mouthBlend),
            mouthBaseScale.y * Mathf.Lerp(closedScaleY, openScaleY, mouthBlend),
            mouthBaseScale.z);

        Quaternion targetRotation = mouthBaseRotation * Quaternion.Euler(0f, 0f, mouthBlend * mouthLeanAngle);
        float lerpFactor = GetLerpFactor(mouthSmoothing, Time.unscaledDeltaTime);

        mouth.localScale = Vector3.Lerp(mouth.localScale, targetScale, lerpFactor);
        mouth.localRotation = Quaternion.Lerp(mouth.localRotation, targetRotation, lerpFactor);
    }

    private Vector2 GetEyeOffset(Vector2 cursorDelta)
    {
        float distance = cursorDelta.magnitude;
        if (distance <= 0.001f)
        {
            return Vector2.zero;
        }

        float fullTravelDistance = Mathf.Max(eyeDistanceForFullTravel, 0.001f);
        float distanceBlend = Mathf.Clamp01(distance / fullTravelDistance);
        Vector2 direction = cursorDelta / distance;

        return new Vector2(
            direction.x * eyeHorizontalRange * distanceBlend,
            direction.y * eyeVerticalRange * distanceBlend);
    }

    private void ResolveReferences()
    {
        if (accessPanel == null)
        {
            Transform accessPanelTransform = transform.Find("Access Panel");
            if (accessPanelTransform != null)
            {
                accessPanel = accessPanelTransform.gameObject;
            }
        }

        if (settingsPanel == null)
        {
            Transform settingsPanelTransform = transform.Find("Settings Panel");
            if (settingsPanelTransform != null)
            {
                settingsPanel = settingsPanelTransform.gameObject;
            }
        }

        if (head == null)
        {
            head = FindRectTransform("Robot Head");
        }

        if (leftEye == null)
        {
            leftEye = FindRectTransform("Robot Head/Eye L");
        }

        if (rightEye == null)
        {
            rightEye = FindRectTransform("Robot Head/Eye R");
        }

        if (mouth == null)
        {
            mouth = FindRectTransform("Robot Head/Mouth");
        }
    }

    private RectTransform FindRectTransform(string path)
    {
        Transform target = transform.Find(path);
        return target as RectTransform;
    }

    private static float GetLerpFactor(float speed, float deltaTime)
    {
        return 1f - Mathf.Exp(-speed * deltaTime);
    }
}

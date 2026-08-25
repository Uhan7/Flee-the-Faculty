using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum DialogueRevealMode
{
    Instant = 0,
    PerWord = 1,
    PerLetter = 2
}

[DisallowMultipleComponent]
public sealed class SceneDialogueView : MonoBehaviour, IDialogueView
{
    private const string DefaultExternalInputPlaceholder = "Type AraBOT's reply here...";

    [Header("References")]
    [SerializeField] private GameObject dialogueContainer;
    [SerializeField] private TMP_Text speakerText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private GameObject continueIndicator;

    [Header("Reveal")]
    [SerializeField] private DialogueRevealMode revealMode = DialogueRevealMode.PerLetter;
    [SerializeField, Min(1f)] private float wordsPerSecond = 5f;
    [SerializeField, Min(1f)] private float lettersPerSecond = 24f;
    [SerializeField, Min(0f)] private float punctuationPauseSeconds = 0.18f;
    [SerializeField, Min(0f)] private float whitespacePauseSeconds = 0.1f;

    [Header("Letter Juice")]
    [SerializeField, Min(0.01f)] private float letterSpawnDuration = 0.16f;
    [SerializeField, Min(0f)] private float letterSpawnRiseDistance = 10f;
    [SerializeField, Min(0f)] private float letterSpawnOvershootHeight = 2.5f;
    [SerializeField, Min(0f)] private float letterSpawnScaleBoost = 0.08f;

    [Header("Next Symbol")]
    [SerializeField, Min(0f)] private float continueBounceDistance = 18f;
    [SerializeField, Min(0.1f)] private float continueBounceCyclesPerSecond = 2.4f;

    private Coroutine revealRoutine;
    private RectTransform continueIndicatorRect;
    private Vector2 continueIndicatorBasePosition;
    private string currentFullText = string.Empty;
    private bool canAdvance;
    private TMP_InputField externalInputField;
    private TMP_Text externalInputText;
    private TMP_Text externalInputPlaceholderText;
    private TMP_MeshInfo[] cachedBodyMeshInfo;
    private readonly List<ActiveGlyphAnimation> activeGlyphAnimations = new List<ActiveGlyphAnimation>();

    public static SceneDialogueView ActiveInstance { get; private set; }
    public bool IsRevealComplete { get; private set; } = true;
    public TMP_InputField ExternalInputField
    {
        get
        {
            EnsureExternalInputField();
            return externalInputField;
        }
    }

    private void Awake()
    {
        ActiveInstance = this;

        if (continueIndicator != null)
        {
            continueIndicatorRect = continueIndicator.transform as RectTransform;
            if (continueIndicatorRect != null)
            {
                continueIndicatorBasePosition = continueIndicatorRect.anchoredPosition;
            }
        }

        SetVisible(false);
    }

    private void Update()
    {
        UpdateContinueIndicator();
        UpdateActiveGlyphAnimations();
    }

    private void OnDestroy()
    {
        if (ActiveInstance == this)
        {
            ActiveInstance = null;
        }
    }

    private void UpdateContinueIndicator()
    {
        if (continueIndicatorRect == null || continueIndicator == null || !continueIndicator.activeSelf)
        {
            return;
        }

        float horizontalBounce = Mathf.Abs(
            Mathf.Sin(Time.unscaledTime * continueBounceCyclesPerSecond * Mathf.PI * 2f)) * continueBounceDistance;
        continueIndicatorRect.anchoredPosition = continueIndicatorBasePosition + (Vector2.left * horizontalBounce);
    }

    private void UpdateActiveGlyphAnimations()
    {
        if (bodyText == null || activeGlyphAnimations.Count == 0 || cachedBodyMeshInfo == null)
        {
            return;
        }

        TMP_TextInfo textInfo = bodyText.textInfo;
        if (textInfo.meshInfo == null || textInfo.meshInfo.Length == 0)
        {
            return;
        }

        RestoreBodyTextVertices(textInfo);

        bool hasActiveAnimations = false;
        float now = Time.unscaledTime;
        for (int index = activeGlyphAnimations.Count - 1; index >= 0; index--)
        {
            ActiveGlyphAnimation animation = activeGlyphAnimations[index];
            float progress = Mathf.Clamp01((now - animation.StartTime) / Mathf.Max(letterSpawnDuration, 0.01f));

            ApplyGlyphAnimation(textInfo, animation.CharacterIndex, progress);

            if (progress >= 1f)
            {
                activeGlyphAnimations.RemoveAt(index);
            }
            else
            {
                hasActiveAnimations = true;
            }
        }

        PushBodyTextVertices(textInfo);

        if (!hasActiveAnimations)
        {
            RestoreBodyTextVertices(textInfo);
            PushBodyTextVertices(textInfo);
        }
    }

    public void SetVisible(bool visible)
    {
        if (dialogueContainer != null)
        {
            dialogueContainer.SetActive(visible);
        }

        if (!visible)
        {
            StopRevealRoutine();
            ClearActiveGlyphAnimations();
            IsRevealComplete = true;
            currentFullText = string.Empty;
            canAdvance = false;
            SetExternalInputVisible(false);

            if (speakerText != null)
            {
                speakerText.text = string.Empty;
            }

            if (bodyText != null)
            {
                bodyText.text = string.Empty;
                bodyText.maxVisibleCharacters = 0;
            }

            SetContinueIndicator(false);
        }
    }

    public void DisplayLine(IDialogueLine line, string visibleText, bool lineCanAdvance)
    {
        if (dialogueContainer != null && !dialogueContainer.activeSelf)
        {
            dialogueContainer.SetActive(true);
        }

        SetExternalInputVisible(false);

        if (speakerText != null)
        {
            speakerText.text = line != null ? line.SpeakerName : string.Empty;
        }

        currentFullText = visibleText ?? string.Empty;
        canAdvance = lineCanAdvance;
        SetContinueIndicator(false);
        StopRevealRoutine();
        ClearActiveGlyphAnimations();

        if (bodyText == null)
        {
            IsRevealComplete = true;
            return;
        }

        if (string.IsNullOrEmpty(currentFullText) || revealMode == DialogueRevealMode.Instant)
        {
            bodyText.text = currentFullText;
            bodyText.maxVisibleCharacters = int.MaxValue;
            CacheBodyTextMesh();
            IsRevealComplete = true;
            SetContinueIndicator(canAdvance);
            return;
        }

        bodyText.text = currentFullText;
        bodyText.maxVisibleCharacters = 0;
        CacheBodyTextMesh();
        IsRevealComplete = false;
        revealRoutine = StartCoroutine(RevealRoutine());
    }

    public void ShowExternalContent(string speaker, string body, bool lineCanAdvance)
    {
        if (dialogueContainer != null && !dialogueContainer.activeSelf)
        {
            dialogueContainer.SetActive(true);
        }

        StopRevealRoutine();
        ClearActiveGlyphAnimations();
        SetExternalInputVisible(false);

        currentFullText = body ?? string.Empty;
        canAdvance = lineCanAdvance;
        IsRevealComplete = true;

        if (speakerText != null)
        {
            speakerText.text = speaker ?? string.Empty;
        }

        if (bodyText != null)
        {
            bodyText.gameObject.SetActive(true);
            bodyText.text = currentFullText;
            bodyText.maxVisibleCharacters = int.MaxValue;
            CacheBodyTextMesh();
        }

        SetContinueIndicator(canAdvance);
    }

    public void SetExternalBodyText(string body, bool lineCanAdvance)
    {
        ShowExternalContent(speakerText != null ? speakerText.text : string.Empty, body, lineCanAdvance);
    }

    public void SetExternalInputVisible(bool visible, string placeholder = null, string currentValue = null)
    {
        EnsureExternalInputField();

        if (bodyText != null)
        {
            bodyText.gameObject.SetActive(!visible);
        }

        if (externalInputField == null)
        {
            return;
        }

        externalInputField.gameObject.SetActive(visible);
        if (!visible)
        {
            return;
        }

        if (externalInputPlaceholderText != null)
        {
            externalInputPlaceholderText.text = string.IsNullOrWhiteSpace(placeholder)
                ? DefaultExternalInputPlaceholder
                : placeholder;
        }

        if (currentValue != null)
        {
            externalInputField.text = currentValue;
        }
    }

    public void FocusExternalInputField()
    {
        if (ExternalInputField == null)
        {
            return;
        }

        externalInputField.ActivateInputField();
        externalInputField.Select();
    }

    public void CompleteReveal()
    {
        StopRevealRoutine();
        ClearActiveGlyphAnimations();
        IsRevealComplete = true;

        if (bodyText != null)
        {
            bodyText.text = currentFullText;
            bodyText.maxVisibleCharacters = int.MaxValue;
            CacheBodyTextMesh();
        }

        SetContinueIndicator(canAdvance);
    }

    private IEnumerator RevealRoutine()
    {
        List<RevealChunk> chunks = BuildRevealChunks(bodyText.textInfo, revealMode);
        if (chunks.Count == 0)
        {
            CompleteReveal();
            yield break;
        }

        int visibleCharacterCount = 0;

        for (int index = 0; index < chunks.Count; index++)
        {
            RevealChunk chunk = chunks[index];
            int chunkStartCharacterIndex = visibleCharacterCount;
            visibleCharacterCount += chunk.CharacterCount;
            bodyText.maxVisibleCharacters = visibleCharacterCount;
            CacheBodyTextMesh();
            QueueGlyphAnimations(chunk, chunkStartCharacterIndex);
            NotifyVoiceTicks(chunkStartCharacterIndex, chunk.CharacterCount);
            UpdateActiveGlyphAnimations();

            if (index < chunks.Count - 1)
            {
                yield return WaitForSecondsRealtime(GetChunkDelaySeconds(chunk));
            }
        }

        revealRoutine = null;
        IsRevealComplete = true;
        bodyText.maxVisibleCharacters = int.MaxValue;
        CacheBodyTextMesh();
        SetContinueIndicator(canAdvance);
    }

    private void SetContinueIndicator(bool visible)
    {
        if (continueIndicator == null)
        {
            return;
        }

        continueIndicator.SetActive(visible);
        if (visible && continueIndicatorRect != null)
        {
            continueIndicatorRect.anchoredPosition = continueIndicatorBasePosition;
        }
    }

    private void StopRevealRoutine()
    {
        if (revealRoutine == null)
        {
            return;
        }

        StopCoroutine(revealRoutine);
        revealRoutine = null;
    }

    private void EnsureExternalInputField()
    {
        if (externalInputField != null || bodyText == null)
        {
            return;
        }

        RectTransform bodyRect = bodyText.rectTransform;
        if (bodyRect == null || bodyRect.parent == null)
        {
            return;
        }

        GameObject inputRoot = new GameObject("Dialogue External Input");
        inputRoot.transform.SetParent(bodyRect.parent, false);

        RectTransform inputRect = inputRoot.AddComponent<RectTransform>();
        inputRect.anchorMin = bodyRect.anchorMin;
        inputRect.anchorMax = bodyRect.anchorMax;
        inputRect.pivot = bodyRect.pivot;
        inputRect.anchoredPosition = bodyRect.anchoredPosition;
        inputRect.sizeDelta = bodyRect.sizeDelta;

        Image inputBackground = inputRoot.AddComponent<Image>();
        inputBackground.color = new Color(0.91f, 0.97f, 1f, 0.18f);

        externalInputField = inputRoot.AddComponent<TMP_InputField>();
        externalInputField.lineType = TMP_InputField.LineType.MultiLineNewline;
        externalInputField.caretColor = bodyText.color;

        GameObject textAreaObject = new GameObject("Text Area");
        textAreaObject.transform.SetParent(inputRoot.transform, false);

        RectTransform textAreaRect = textAreaObject.AddComponent<RectTransform>();
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(14f, 12f);
        textAreaRect.offsetMax = new Vector2(-14f, -12f);
        textAreaObject.AddComponent<RectMask2D>();

        externalInputText = CreateRuntimeText(
            "Text",
            textAreaRect,
            bodyText.font,
            string.Empty,
            bodyText.fontSize,
            bodyText.fontStyle,
            bodyText.color,
            bodyText.alignment);

        externalInputPlaceholderText = CreateRuntimeText(
            "Placeholder",
            textAreaRect,
            bodyText.font,
            DefaultExternalInputPlaceholder,
            bodyText.fontSize,
            FontStyles.Italic,
            new Color(bodyText.color.r, bodyText.color.g, bodyText.color.b, 0.4f),
            bodyText.alignment);

        externalInputField.textViewport = textAreaRect;
        externalInputField.textComponent = externalInputText;
        externalInputField.placeholder = externalInputPlaceholderText;
        inputRoot.SetActive(false);
    }

    private static TMP_Text CreateRuntimeText(
        string objectName,
        Transform parent,
        TMP_FontAsset fontAsset,
        string text,
        float fontSize,
        FontStyles fontStyle,
        Color color,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(objectName);
        textObject.transform.SetParent(parent, false);

        RectTransform rectTransform = textObject.AddComponent<RectTransform>();
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        TextMeshProUGUI textComponent = textObject.AddComponent<TextMeshProUGUI>();
        textComponent.font = fontAsset;
        textComponent.text = text;
        textComponent.fontSize = fontSize;
        textComponent.fontStyle = fontStyle;
        textComponent.color = color;
        textComponent.alignment = alignment;
        textComponent.enableWordWrapping = true;
        return textComponent;
    }

    private void ClearActiveGlyphAnimations()
    {
        activeGlyphAnimations.Clear();
        cachedBodyMeshInfo = null;
    }

    private static List<RevealChunk> BuildRevealChunks(TMP_TextInfo textInfo, DialogueRevealMode mode)
    {
        List<RevealChunk> chunks = new List<RevealChunk>();
        if (textInfo == null || textInfo.characterCount <= 0)
        {
            return chunks;
        }

        int index = 0;
        while (index < textInfo.characterCount)
        {
            char current = textInfo.characterInfo[index].character;
            if (char.IsWhiteSpace(current))
            {
                chunks.Add(new RevealChunk(1, RevealChunkKind.Whitespace));
                index++;
                continue;
            }

            if (char.IsLetterOrDigit(current))
            {
                int start = index;
                index++;

                while (index < textInfo.characterCount && IsWordCharacter(textInfo, index))
                {
                    index++;
                }

                int wordCharacterCount = index - start;
                if (mode == DialogueRevealMode.PerLetter)
                {
                    for (int letterIndex = 0; letterIndex < wordCharacterCount; letterIndex++)
                    {
                        chunks.Add(new RevealChunk(1, RevealChunkKind.Text));
                    }
                }
                else
                {
                    chunks.Add(new RevealChunk(wordCharacterCount, RevealChunkKind.Text));
                }

                continue;
            }

            chunks.Add(new RevealChunk(1, RevealChunkKind.Punctuation));
            index++;
        }

        return chunks;
    }

    private static bool IsWordCharacter(TMP_TextInfo textInfo, int index)
    {
        char current = textInfo.characterInfo[index].character;
        if (char.IsLetterOrDigit(current))
        {
            return true;
        }

        if ((current == '\'' || current == '-') &&
            index > 0 &&
            index + 1 < textInfo.characterCount &&
            char.IsLetterOrDigit(textInfo.characterInfo[index - 1].character) &&
            char.IsLetterOrDigit(textInfo.characterInfo[index + 1].character))
        {
            return true;
        }

        return false;
    }

    private float GetChunkDelaySeconds(RevealChunk chunk)
    {
        switch (chunk.Kind)
        {
            case RevealChunkKind.Punctuation:
                return punctuationPauseSeconds;
            case RevealChunkKind.Whitespace:
                return whitespacePauseSeconds;
            default:
                return revealMode == DialogueRevealMode.PerLetter
                    ? 1f / Mathf.Max(lettersPerSecond, 1f)
                    : 1f / Mathf.Max(wordsPerSecond, 1f);
        }
    }

    private void QueueGlyphAnimations(RevealChunk chunk, int startCharacterIndex)
    {
        if (chunk.Kind == RevealChunkKind.Whitespace)
        {
            return;
        }

        float startTime = Time.unscaledTime;
        for (int offset = 0; offset < chunk.CharacterCount; offset++)
        {
            activeGlyphAnimations.Add(new ActiveGlyphAnimation(startCharacterIndex + offset, startTime));
        }
    }

    private void NotifyVoiceTicks(int startCharacterIndex, int characterCount)
    {
        DialogueManager dialogueManager = DialogueManager.Instance;
        if (dialogueManager == null || bodyText == null)
        {
            return;
        }

        TMP_TextInfo textInfo = bodyText.textInfo;
        if (textInfo == null || textInfo.characterCount <= 0)
        {
            return;
        }

        int maxCharacterIndex = Mathf.Min(startCharacterIndex + characterCount, textInfo.characterCount);
        for (int characterIndex = startCharacterIndex; characterIndex < maxCharacterIndex; characterIndex++)
        {
            dialogueManager.NotifyCharacterRevealed(textInfo.characterInfo[characterIndex].character);
        }
    }

    private void CacheBodyTextMesh()
    {
        if (bodyText == null)
        {
            cachedBodyMeshInfo = null;
            return;
        }

        bodyText.ForceMeshUpdate();
        TMP_TextInfo textInfo = bodyText.textInfo;
        if (textInfo == null || textInfo.characterCount <= 0 || textInfo.meshInfo == null || textInfo.meshInfo.Length == 0)
        {
            cachedBodyMeshInfo = null;
            return;
        }

        for (int meshIndex = 0; meshIndex < textInfo.meshInfo.Length; meshIndex++)
        {
            TMP_MeshInfo meshInfo = textInfo.meshInfo[meshIndex];
            if (meshInfo.vertices == null || meshInfo.mesh == null)
            {
                cachedBodyMeshInfo = null;
                return;
            }
        }

        cachedBodyMeshInfo = textInfo.CopyMeshInfoVertexData();
    }

    private void RestoreBodyTextVertices(TMP_TextInfo textInfo)
    {
        if (cachedBodyMeshInfo == null)
        {
            return;
        }

        int meshCount = Mathf.Min(textInfo.meshInfo.Length, cachedBodyMeshInfo.Length);
        for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
        {
            Vector3[] sourceVertices = cachedBodyMeshInfo[meshIndex].vertices;
            Vector3[] targetVertices = textInfo.meshInfo[meshIndex].vertices;

            if (sourceVertices == null || targetVertices == null)
            {
                continue;
            }

            Array.Copy(sourceVertices, targetVertices, Mathf.Min(sourceVertices.Length, targetVertices.Length));
        }
    }

    private void PushBodyTextVertices(TMP_TextInfo textInfo)
    {
        for (int meshIndex = 0; meshIndex < textInfo.meshInfo.Length; meshIndex++)
        {
            textInfo.meshInfo[meshIndex].mesh.vertices = textInfo.meshInfo[meshIndex].vertices;
            bodyText.UpdateGeometry(textInfo.meshInfo[meshIndex].mesh, meshIndex);
        }
    }

    private void ApplyGlyphAnimation(TMP_TextInfo textInfo, int characterIndex, float progress)
    {
        if (characterIndex < 0 || characterIndex >= textInfo.characterCount)
        {
            return;
        }

        TMP_CharacterInfo characterInfo = textInfo.characterInfo[characterIndex];
        if (!characterInfo.isVisible)
        {
            return;
        }

        int materialIndex = characterInfo.materialReferenceIndex;
        int vertexIndex = characterInfo.vertexIndex;
        Vector3[] vertices = textInfo.meshInfo[materialIndex].vertices;

        Vector3 bottomLeft = vertices[vertexIndex];
        Vector3 topRight = vertices[vertexIndex + 2];
        Vector3 midpoint = (bottomLeft + topRight) * 0.5f;

        float eased = 1f - Mathf.Pow(1f - progress, 3f);
        float verticalOffset = Mathf.Lerp(-letterSpawnRiseDistance, 0f, eased)
            + Mathf.Sin(progress * Mathf.PI) * letterSpawnOvershootHeight;
        float scale = Mathf.Lerp(0.94f, 1f, eased) + Mathf.Sin(progress * Mathf.PI) * letterSpawnScaleBoost;
        Vector3 offset = new Vector3(0f, verticalOffset, 0f);

        for (int vertexOffset = 0; vertexOffset < 4; vertexOffset++)
        {
            int currentVertexIndex = vertexIndex + vertexOffset;
            Vector3 vertex = vertices[currentVertexIndex] - midpoint;
            vertex *= scale;
            vertex += midpoint + offset;
            vertices[currentVertexIndex] = vertex;
        }
    }

    private static IEnumerator WaitForSecondsRealtime(float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private readonly struct RevealChunk
    {
        public RevealChunk(int characterCount, RevealChunkKind kind)
        {
            CharacterCount = characterCount;
            Kind = kind;
        }

        public int CharacterCount { get; }

        public RevealChunkKind Kind { get; }
    }

    private readonly struct ActiveGlyphAnimation
    {
        public ActiveGlyphAnimation(int characterIndex, float startTime)
        {
            CharacterIndex = characterIndex;
            StartTime = startTime;
        }

        public int CharacterIndex { get; }

        public float StartTime { get; }
    }

    private enum RevealChunkKind
    {
        Text,
        Punctuation,
        Whitespace
    }
}

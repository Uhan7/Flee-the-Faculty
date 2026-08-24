using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

public enum DialogueRevealMode
{
    Instant = 0,
    PerWord = 1,
    PerLetter = 2
}

[DisallowMultipleComponent]
public sealed class SceneDialogueView : MonoBehaviour, IDialogueView
{
    [Header("References")]
    [SerializeField] private GameObject dialogueContainer;
    [SerializeField] private TMP_Text speakerText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private GameObject continueIndicator;

    [Header("Reveal")]
    [SerializeField] private DialogueRevealMode revealMode = DialogueRevealMode.PerWord;
    [SerializeField, Min(1f)] private float wordsPerSecond = 5f;
    [SerializeField, Min(1f)] private float lettersPerSecond = 24f;

    [Header("Next Symbol")]
    [SerializeField, Min(0f)] private float continueBounceDistance = 18f;
    [SerializeField, Min(0.1f)] private float continueBounceCyclesPerSecond = 2.4f;

    private Coroutine revealRoutine;
    private RectTransform continueIndicatorRect;
    private Vector2 continueIndicatorBasePosition;
    private string currentFullText = string.Empty;
    private bool canAdvance;

    public bool IsRevealComplete { get; private set; } = true;

    private void Awake()
    {
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
        if (continueIndicatorRect == null || continueIndicator == null || !continueIndicator.activeSelf)
        {
            return;
        }

        float horizontalBounce = Mathf.Abs(
            Mathf.Sin(Time.unscaledTime * continueBounceCyclesPerSecond * Mathf.PI * 2f)) * continueBounceDistance;
        continueIndicatorRect.anchoredPosition = continueIndicatorBasePosition + (Vector2.left * horizontalBounce);
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
            IsRevealComplete = true;
            currentFullText = string.Empty;
            canAdvance = false;

            if (speakerText != null)
            {
                speakerText.text = string.Empty;
            }

            if (bodyText != null)
            {
                bodyText.text = string.Empty;
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

        if (speakerText != null)
        {
            speakerText.text = line != null ? line.SpeakerName : string.Empty;
        }

        currentFullText = visibleText ?? string.Empty;
        canAdvance = lineCanAdvance;
        SetContinueIndicator(false);
        StopRevealRoutine();

        if (bodyText == null)
        {
            IsRevealComplete = true;
            return;
        }

        if (string.IsNullOrEmpty(currentFullText) || revealMode == DialogueRevealMode.Instant)
        {
            bodyText.text = currentFullText;
            IsRevealComplete = true;
            SetContinueIndicator(canAdvance);
            return;
        }

        bodyText.text = string.Empty;
        IsRevealComplete = false;
        revealRoutine = StartCoroutine(RevealRoutine(currentFullText));
    }

    public void CompleteReveal()
    {
        StopRevealRoutine();
        IsRevealComplete = true;

        if (bodyText != null)
        {
            bodyText.text = currentFullText;
        }

        SetContinueIndicator(canAdvance);
    }

    private IEnumerator RevealRoutine(string fullText)
    {
        List<RevealChunk> chunks = BuildRevealChunks(fullText, revealMode);
        if (chunks.Count == 0)
        {
            CompleteReveal();
            yield break;
        }

        StringBuilder builder = new StringBuilder(fullText.Length);
        float secondsPerChunk = revealMode == DialogueRevealMode.PerLetter
            ? 1f / Mathf.Max(lettersPerSecond, 1f)
            : 1f / Mathf.Max(wordsPerSecond, 1f);

        for (int index = 0; index < chunks.Count; index++)
        {
            builder.Append(chunks[index].Prefix);
            builder.Append(chunks[index].Content);
            bodyText.text = builder.ToString();

            if (index < chunks.Count - 1)
            {
                yield return WaitForSecondsRealtime(secondsPerChunk);
            }
        }

        revealRoutine = null;
        IsRevealComplete = true;
        bodyText.text = fullText;
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

    private static List<RevealChunk> BuildRevealChunks(string text, DialogueRevealMode mode)
    {
        List<RevealChunk> chunks = new List<RevealChunk>();
        if (string.IsNullOrEmpty(text))
        {
            return chunks;
        }

        StringBuilder pendingWhitespace = new StringBuilder();
        int index = 0;
        while (index < text.Length)
        {
            char current = text[index];
            if (char.IsWhiteSpace(current))
            {
                pendingWhitespace.Append(current);
                index++;
                continue;
            }

            if (char.IsLetterOrDigit(current))
            {
                int start = index;
                index++;

                while (index < text.Length && IsWordCharacter(text, index))
                {
                    index++;
                }

                string word = text.Substring(start, index - start);
                if (mode == DialogueRevealMode.PerLetter)
                {
                    for (int letterIndex = 0; letterIndex < word.Length; letterIndex++)
                    {
                        chunks.Add(new RevealChunk(
                            letterIndex == 0 ? pendingWhitespace.ToString() : string.Empty,
                            word[letterIndex].ToString()));
                    }
                }
                else
                {
                    chunks.Add(new RevealChunk(pendingWhitespace.ToString(), word));
                }

                pendingWhitespace.Length = 0;
                continue;
            }

            chunks.Add(new RevealChunk(pendingWhitespace.ToString(), current.ToString()));
            pendingWhitespace.Length = 0;
            index++;
        }

        return chunks;
    }

    private static bool IsWordCharacter(string text, int index)
    {
        char current = text[index];
        if (char.IsLetterOrDigit(current))
        {
            return true;
        }

        if ((current == '\'' || current == '-') &&
            index > 0 &&
            index + 1 < text.Length &&
            char.IsLetterOrDigit(text[index - 1]) &&
            char.IsLetterOrDigit(text[index + 1]))
        {
            return true;
        }

        return false;
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
        public RevealChunk(string prefix, string content)
        {
            Prefix = prefix;
            Content = content;
        }

        public string Prefix { get; }

        public string Content { get; }
    }
}

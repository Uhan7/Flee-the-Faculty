using System;
using System.Text;

/// <summary>
/// The few lines the Learning phase says on its own, because no service call
/// can supply them: a closing from AraBOT, an opener before the first check,
/// and what each character says when a call fails.
///
/// Everything else the Teacher and AraBOT say comes from the Lesson or from the
/// service. These are fixed text on purpose: they have to work when the
/// service does not, and a failure line that needed the network would be no
/// failure line at all.
///
/// The Filipino lines are drafts for a native speaker to review before they
/// reach a classroom, the same as the service's Filipino template lines.
/// </summary>
public static class LessonLocalLines
{
    private const int MaxTopicLength = 60;

    public static bool IsFilipino(string language)
    {
        return string.Equals((language ?? string.Empty).Trim(), "fil", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// AraBOT's closing line, in the voice of the sample flow: "YIPPEE I
    /// UNDERSTAND THE WATER CYCLE NOW!!". The topic is trimmed, flattened to
    /// one line and cut at 60 characters, so a pasted paragraph never turns
    /// into a speech.
    /// </summary>
    public static string ClosingLine(string language, string topic)
    {
        string safeTopic = CleanTopic(topic);
        if (IsFilipino(language))
        {
            return safeTopic.Length > 0
                ? ("YEHEY! NAINTINDIHAN KO NA ANG " + safeTopic + " NGAYON!!").ToUpperInvariant()
                : "YEHEY! NAINTINDIHAN KO NA NGAYON!!";
        }

        return safeTopic.Length > 0
            ? ("YIPPEE! I UNDERSTAND " + safeTopic + " NOW!!").ToUpperInvariant()
            : "YIPPEE! I UNDERSTAND IT NOW!!";
    }

    /// <summary>The Teacher, before the first Comprehension Check of a section.</summary>
    public static string CheckIntro(string language)
    {
        return IsFilipino(language)
            ? "Sandali lang, may maikling tanong muna tayo!"
            : "Quick check time! Tell me what you think.";
    }

    /// <summary>The Teacher, when the service could not answer a Learner Interjection.</summary>
    public static string InterjectionFailed(string language)
    {
        return IsFilipino(language)
            ? "Hindi ko iyon narinig nang malinaw. Magpatuloy muna tayo!"
            : "Hmm, I could not catch that. Let us keep going, and ask me again in a moment!";
    }

    /// <summary>The Teacher, when the service could not answer a Comprehension Check.</summary>
    public static string CheckFailed(string language)
    {
        return IsFilipino(language)
            ? "Magaling! Magpatuloy na tayo."
            : "Nice thinking! Let us move on.";
    }

    /// <summary>AraBOT, when the service could not answer an AraBOT Question.</summary>
    public static string AraBotAnswerFailed(string language)
    {
        return IsFilipino(language)
            ? "Ahh, sige! Tuloy lang tayo."
            : "Ohh, okay! Let us keep going.";
    }

    /// <summary>What the capture box says under "Ask the Teacher".</summary>
    public static string AskInstructions(string language)
    {
        return IsFilipino(language)
            ? "I-click ang mic sa itaas ni AraBOT para itanong."
            : "Click the mic above AraBOT to ask your question.";
    }

    /// <summary>What the capture box says under "Ara" when she answers.</summary>
    public static string AnswerInstructions(string language)
    {
        return IsFilipino(language)
            ? "I-click ang mic sa itaas ni AraBOT para sumagot."
            : "Click the mic above AraBOT to answer.";
    }

    public static string CancelLabel(string language)
    {
        return IsFilipino(language) ? "Kansela" : "Cancel";
    }

    public static string SkipLabel(string language)
    {
        return IsFilipino(language) ? "Laktaw" : "Skip";
    }

    private static string CleanTopic(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return string.Empty;
        }

        string[] words = topic.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        StringBuilder builder = new StringBuilder();
        for (int index = 0; index < words.Length; index++)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(words[index]);
        }

        string flat = builder.ToString();
        return flat.Length <= MaxTopicLength ? flat : flat.Substring(0, MaxTopicLength).TrimEnd();
    }
}

/// <summary>
/// What she said, as the capture box hands it over. The box passes a stock
/// sentence through when she confirms with nothing heard, and that sentence is
/// not an answer.
/// </summary>
public static class LessonAnswerText
{
    private const string NoSpeechSubmitted = "No speech was submitted.";
    private const string NoSpeechOrFallbackSubmitted = "No speech or fallback text was submitted.";

    /// <summary>The trimmed answer, or an empty string when there was none.</summary>
    public static string Clean(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return string.Empty;
        }

        string trimmed = transcript.Trim();
        if (string.Equals(trimmed, NoSpeechSubmitted, StringComparison.Ordinal)
            || string.Equals(trimmed, NoSpeechOrFallbackSubmitted, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return trimmed;
    }
}

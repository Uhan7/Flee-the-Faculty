using System;
using System.Collections.Generic;

/// <summary>Who says a page of the Lesson. The Teacher teaches, AraBOT asks and reacts.</summary>
public enum LessonSpeaker
{
    Teacher = 0,
    AraBot = 1
}

/// <summary>
/// One box of dialogue: a stretch of one Teacher line (or one AraBOT line) that
/// fits the dialogue box, plus which entry of the step's `dialogue` it came from.
/// </summary>
public sealed class LessonSpeechPage
{
    public LessonSpeechPage(LessonSpeaker speaker, string text, int sourceLineIndex)
    {
        Speaker = speaker;
        Text = text ?? string.Empty;
        SourceLineIndex = sourceLineIndex;
    }

    public LessonSpeaker Speaker { get; }
    public string Text { get; }

    /// <summary>
    /// The index of the line in the step's `dialogue` this page was cut from, as
    /// <see cref="LessonCursor.SetLine"/> counts it. Several pages of one long
    /// line share an index.
    /// </summary>
    public int SourceLineIndex { get; }
}

/// <summary>
/// Turns Teacher lines into the pages the dialogue box shows. Plain C#, so the
/// cut can be tested without Unity: the paginator that does the actual
/// splitting lives in the game assembly and is handed in.
///
/// Why this exists: the cursor counts lines, the dialogue box counts pages, and
/// a Learner Interjection has to resume at the line she was on. Every page
/// remembers its line, so the controller can answer "where was she" from the
/// page the dialogue box reports.
/// </summary>
public static class LessonSpeechPlan
{
    /// <summary>
    /// Pages for the lines from <paramref name="startLineIndex"/> to the end.
    /// Blank lines are skipped without disturbing the numbering of the others.
    /// A null <paramref name="splitter"/> keeps each line as one page.
    /// </summary>
    public static List<LessonSpeechPage> Build(
        IReadOnlyList<string> lines,
        int startLineIndex,
        LessonSpeaker speaker,
        Func<string, IReadOnlyList<string>> splitter)
    {
        List<LessonSpeechPage> pages = new List<LessonSpeechPage>();
        if (lines == null)
        {
            return pages;
        }

        for (int index = Math.Max(0, startLineIndex); index < lines.Count; index++)
        {
            Append(pages, lines[index], speaker, index, splitter);
        }

        return pages;
    }

    /// <summary>One line, as pages, numbered as line 0.</summary>
    public static List<LessonSpeechPage> BuildSingle(
        string text,
        LessonSpeaker speaker,
        Func<string, IReadOnlyList<string>> splitter)
    {
        List<LessonSpeechPage> pages = new List<LessonSpeechPage>();
        Append(pages, text, speaker, 0, splitter);
        return pages;
    }

    /// <summary>Add one line's pages to an existing plan.</summary>
    public static void Append(
        List<LessonSpeechPage> pages,
        string text,
        LessonSpeaker speaker,
        int sourceLineIndex,
        Func<string, IReadOnlyList<string>> splitter)
    {
        if (pages == null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        string trimmed = text.Trim();
        IReadOnlyList<string> parts = splitter != null ? splitter(trimmed) : null;
        if (parts == null || parts.Count == 0)
        {
            pages.Add(new LessonSpeechPage(speaker, trimmed, sourceLineIndex));
            return;
        }

        for (int index = 0; index < parts.Count; index++)
        {
            if (!string.IsNullOrWhiteSpace(parts[index]))
            {
                pages.Add(new LessonSpeechPage(speaker, parts[index], sourceLineIndex));
            }
        }
    }

    /// <summary>
    /// The line a page index belongs to, or -1 when the index is outside the
    /// plan. This is what the controller hands to <see cref="LessonCursor.SetLine"/>.
    /// </summary>
    public static int SourceLineOf(IReadOnlyList<LessonSpeechPage> pages, int pageIndex)
    {
        if (pages == null || pageIndex < 0 || pageIndex >= pages.Count)
        {
            return -1;
        }

        return pages[pageIndex].SourceLineIndex;
    }
}

using System;
using System.Collections.Generic;

/// <summary>
/// What has already been asked in the Lesson she is in, so Back and a replay
/// can walk over a step again without asking its AraBOT Question a second time
/// or putting a section's Comprehension Checks to her twice. Each of those asks
/// costs a call to the service and an interruption, and neither is graded, so
/// asking once is the whole point.
///
/// Plain C#, with no Unity types, so it is tested directly.
/// </summary>
public sealed class LessonProgress
{
    private readonly HashSet<string> askedQuestions = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> checkedSections = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>True for an AraBOT Question with an id that has not been asked yet.</summary>
    public bool ShouldAskQuestion(string questionId)
    {
        string key = Normalize(questionId);
        return key.Length > 0 && !askedQuestions.Contains(key);
    }

    public void MarkQuestionAsked(string questionId)
    {
        string key = Normalize(questionId);
        if (key.Length > 0)
        {
            askedQuestions.Add(key);
        }
    }

    /// <summary>True for a section whose checks have not been put to her yet.</summary>
    public bool ShouldRunChecks(string sectionKey)
    {
        string key = Normalize(sectionKey);
        return key.Length > 0 && !checkedSections.Contains(key);
    }

    public void MarkChecksRun(string sectionKey)
    {
        string key = Normalize(sectionKey);
        if (key.Length > 0)
        {
            checkedSections.Add(key);
        }
    }

    /// <summary>
    /// A key for a section that works when the service left its id blank, which
    /// a hand-edited mock can: fall back to the section's position.
    /// </summary>
    public static string SectionKey(string sectionId, int sectionIndex)
    {
        string trimmed = Normalize(sectionId);
        return trimmed.Length > 0 ? trimmed : "section-" + sectionIndex;
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}

/// <summary>
/// Which Classrooms have already had their Learning phase this session. A
/// replay of a Classroom skips Learning, so the controller asks this before it
/// teaches and marks it when she leaves.
///
/// Held in memory only. The Lesson plays once per Classroom per visit, and a
/// reload of the page is a fresh start.
/// </summary>
public sealed class LearningRunLedger
{
    private readonly HashSet<string> finished = new HashSet<string>(StringComparer.Ordinal);

    public bool HasRun(string classroomId)
    {
        string key = Normalize(classroomId);
        return key.Length > 0 && finished.Contains(key);
    }

    public void MarkRun(string classroomId)
    {
        string key = Normalize(classroomId);
        if (key.Length > 0)
        {
            finished.Add(key);
        }
    }

    public void Clear()
    {
        finished.Clear();
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}

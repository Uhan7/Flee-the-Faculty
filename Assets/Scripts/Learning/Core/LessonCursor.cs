using System;
using System.Collections.Generic;

/// <summary>
/// What a call on <see cref="LessonCursor"/> did.
/// </summary>
public enum LessonMove
{
    /// <summary>Nothing moved: the move was not allowed from here.</summary>
    Stayed = 0,

    /// <summary>Moved to the next Teacher line of the same step.</summary>
    Line = 1,

    /// <summary>
    /// <see cref="LessonCursor.Next"/> was on the last line of the step, so it
    /// did not move. This is the end-of-step signal: offer Continue Lesson,
    /// ask the step's AraBOT Question, or run the section's checks.
    /// </summary>
    EndOfStep = 2,

    /// <summary>Moved to the first line of the next step in the same section.</summary>
    Step = 3,

    /// <summary>Moved to the first step of the next section.</summary>
    Section = 4,

    /// <summary>
    /// <see cref="LessonCursor.NextStep"/> was on the last step of the Lesson,
    /// so it did not move and <see cref="LessonCursor.IsFinished"/> is now true.
    /// </summary>
    Finished = 5
}

/// <summary>
/// Where she is in a Lesson. Plain C#, no UnityEngine, so it can be tested
/// without the editor and cannot be tangled up with a scene.
///
/// The Lesson is a list of sections, each a list of steps, each a list of
/// Teacher lines. The cursor walks that as one flat run of steps, because the
/// Back button is "the previous step" whichever section it sits in. It knows
/// nothing about dialogue boxes: a "line" here is one entry of a step's
/// `dialogue`, which the controller may paginate into several boxes. The
/// controller reports which line it is on with <see cref="SetLine"/>.
///
/// A Learner Interjection takes her to another step to hear it again. The
/// cursor keeps a return point for that: <see cref="BeginReplay"/> jumps and
/// remembers where she was, <see cref="EndReplay"/> comes back to the same
/// step and the same line. While a replay is running Back and NextStep do
/// nothing, so the way home is always EndReplay.
///
/// Everything tolerates the nulls JsonUtility leaves behind: null sections,
/// steps and arrays are skipped, and blank dialogue lines are dropped, so
/// line indexes always count lines worth showing.
/// </summary>
public sealed class LessonCursor
{
    private readonly FleeLessonView lesson;
    private readonly List<StepEntry> entries = new List<StepEntry>();
    private int sectionCount;
    private int stepIndex;
    private int lineIndex;
    private bool isFinished;
    private bool isReplaying;
    private int returnStepIndex;
    private int returnLineIndex;

    public LessonCursor(FleeLessonView lesson)
    {
        this.lesson = lesson;
        if (lesson == null || lesson.sections == null)
        {
            return;
        }

        for (int sectionIndex = 0; sectionIndex < lesson.sections.Length; sectionIndex++)
        {
            FleeLessonSection section = lesson.sections[sectionIndex];
            if (section == null || section.steps == null)
            {
                continue;
            }

            int stepsInSection = 0;
            for (int index = 0; index < section.steps.Length; index++)
            {
                FleeLessonStep step = section.steps[index];
                if (step == null)
                {
                    continue;
                }

                entries.Add(new StepEntry(sectionCount, stepsInSection, section, step));
                stepsInSection++;
            }

            if (stepsInSection > 0)
            {
                sectionCount++;
            }
        }

        // Which steps close their section is known once the section is
        // complete, so it is settled in a second pass.
        for (int index = 0; index < entries.Count; index++)
        {
            bool closesSection = index == entries.Count - 1
                || entries[index + 1].SectionIndex != entries[index].SectionIndex;
            entries[index].ClosesSection = closesSection;
        }
    }

    public FleeLessonView Lesson => lesson;

    /// <summary>False for a null Lesson or one with no step to teach.</summary>
    public bool HasSteps => entries.Count > 0;

    public int StepCount => entries.Count;
    public int SectionCount => sectionCount;

    /// <summary>0-based, counted across the whole Lesson.</summary>
    public int StepIndex => stepIndex;

    /// <summary>1-based, for "Step 3 of 9".</summary>
    public int StepNumber => HasSteps ? stepIndex + 1 : 0;

    public int SectionIndex => HasSteps ? entries[stepIndex].SectionIndex : -1;

    /// <summary>0-based inside the current section.</summary>
    public int StepIndexInSection => HasSteps ? entries[stepIndex].IndexInSection : -1;

    public FleeLessonSection CurrentSection => HasSteps ? entries[stepIndex].Section : null;
    public FleeLessonStep CurrentStep => HasSteps ? entries[stepIndex].Step : null;
    public string CurrentStepId => CurrentStep != null ? CurrentStep.stepId ?? string.Empty : string.Empty;

    /// <summary>The whole board for this step. Null only when there is no step.</summary>
    public FleeWhiteboard CurrentWhiteboard => CurrentStep != null ? CurrentStep.whiteboard : null;

    /// <summary>The Teacher lines of this step, blanks removed.</summary>
    public IReadOnlyList<string> CurrentLines =>
        HasSteps ? entries[stepIndex].Lines : (IReadOnlyList<string>)Array.Empty<string>();

    public int LineCount => CurrentLines.Count;

    /// <summary>0-based inside the current step's <see cref="CurrentLines"/>.</summary>
    public int LineIndex => lineIndex;

    public string CurrentLine =>
        lineIndex >= 0 && lineIndex < LineCount ? CurrentLines[lineIndex] : string.Empty;

    public bool IsFirstStep => stepIndex == 0;
    public bool IsLastStep => HasSteps && stepIndex == entries.Count - 1;

    /// <summary>
    /// True on the last step of a section, which is where that section's
    /// checks are asked. The checks come from <see cref="CurrentChecks"/>.
    /// </summary>
    public bool IsLastStepOfSection => HasSteps && entries[stepIndex].ClosesSection;

    /// <summary>True on the last line of the step, or on a step with none.</summary>
    public bool IsAtEndOfStep => lineIndex >= LineCount - 1;

    /// <summary>Set by <see cref="NextStep"/> on the last step, cleared by any other move.</summary>
    public bool IsFinished => isFinished;

    /// <summary>
    /// Back goes to the previous step, so it needs one, and it is switched off
    /// during a replay.
    /// </summary>
    public bool CanGoBack => HasSteps && stepIndex > 0 && !isReplaying;

    public bool HasAraBotQuestion => CurrentStep != null && CurrentStep.HasAraBotQuestion;
    public FleeAraBotQuestion CurrentAraBotQuestion =>
        HasAraBotQuestion ? CurrentStep.arabotQuestion : null;

    /// <summary>
    /// The current section's Comprehension Checks, in order, with any null or
    /// id-less entry left out.
    /// </summary>
    public IReadOnlyList<FleeComprehensionCheck> CurrentChecks =>
        HasSteps ? entries[stepIndex].Checks : (IReadOnlyList<FleeComprehensionCheck>)
            Array.Empty<FleeComprehensionCheck>();

    public bool IsReplaying => isReplaying;

    /// <summary>
    /// Where <see cref="EndReplay"/> will come back to. Meaningful only while
    /// <see cref="IsReplaying"/>.
    /// </summary>
    public int ReplayReturnStepIndex => returnStepIndex;
    public int ReplayReturnLineIndex => returnLineIndex;

    /// <summary>
    /// The controller says which line it has reached, so a later interjection
    /// can save the right place. Clamped to the step's lines.
    /// </summary>
    public void SetLine(int newLineIndex)
    {
        int lastLine = Math.Max(0, LineCount - 1);
        lineIndex = Math.Max(0, Math.Min(newLineIndex, lastLine));
    }

    /// <summary>
    /// Next Teacher line. On the last line it does not move and answers
    /// <see cref="LessonMove.EndOfStep"/>, which is the controller's cue to
    /// show Continue Lesson.
    /// </summary>
    public LessonMove Next()
    {
        if (!HasSteps)
        {
            return LessonMove.Stayed;
        }

        if (lineIndex + 1 < LineCount)
        {
            lineIndex++;
            isFinished = false;
            return LessonMove.Line;
        }

        return LessonMove.EndOfStep;
    }

    /// <summary>
    /// Continue Lesson: first line of the next step. On the last step it stays
    /// put and answers <see cref="LessonMove.Finished"/>. Does nothing during a
    /// replay.
    /// </summary>
    public LessonMove NextStep()
    {
        if (!HasSteps || isReplaying)
        {
            return LessonMove.Stayed;
        }

        if (stepIndex + 1 >= entries.Count)
        {
            isFinished = true;
            return LessonMove.Finished;
        }

        int previousSection = entries[stepIndex].SectionIndex;
        stepIndex++;
        lineIndex = 0;
        isFinished = false;
        return entries[stepIndex].SectionIndex != previousSection
            ? LessonMove.Section
            : LessonMove.Step;
    }

    /// <summary>
    /// Back: the previous step, from its first line, so its whiteboard and
    /// dialogue show again. Does nothing on the first step or during a replay.
    /// </summary>
    public bool Back()
    {
        if (!CanGoBack)
        {
            return false;
        }

        stepIndex--;
        lineIndex = 0;
        isFinished = false;
        return true;
    }

    /// <summary>
    /// Jump to a step to play it again, remembering where she was. Returns
    /// false, and moves nothing, for an unknown id. Asking again during a
    /// replay jumps again but keeps the original return point, so one
    /// <see cref="EndReplay"/> always brings her home.
    /// </summary>
    public bool BeginReplay(string stepId)
    {
        int target = FindStepIndex(stepId);
        if (target < 0)
        {
            return false;
        }

        if (!isReplaying)
        {
            returnStepIndex = stepIndex;
            returnLineIndex = lineIndex;
            isReplaying = true;
        }

        stepIndex = target;
        lineIndex = 0;
        isFinished = false;
        return true;
    }

    /// <summary>
    /// Come back from a replay to the saved step and line, and return that
    /// line index so the controller can resume the dialogue there. Returns -1
    /// when no replay was running.
    /// </summary>
    public int EndReplay()
    {
        if (!isReplaying)
        {
            return -1;
        }

        isReplaying = false;
        stepIndex = returnStepIndex;
        SetLine(returnLineIndex);
        isFinished = false;
        return lineIndex;
    }

    /// <summary>The flat index of the step with this id, or -1.</summary>
    public int FindStepIndex(string stepId)
    {
        if (string.IsNullOrWhiteSpace(stepId))
        {
            return -1;
        }

        string wanted = stepId.Trim();
        for (int index = 0; index < entries.Count; index++)
        {
            if (string.Equals(entries[index].Step.stepId, wanted, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private sealed class StepEntry
    {
        public StepEntry(int sectionIndex, int indexInSection, FleeLessonSection section, FleeLessonStep step)
        {
            SectionIndex = sectionIndex;
            IndexInSection = indexInSection;
            Section = section;
            Step = step;
            Lines = CleanLines(step.dialogue);
            Checks = CleanChecks(section.checks);
        }

        public int SectionIndex { get; }
        public int IndexInSection { get; }
        public FleeLessonSection Section { get; }
        public FleeLessonStep Step { get; }
        public string[] Lines { get; }
        public FleeComprehensionCheck[] Checks { get; }
        public bool ClosesSection { get; set; }

        private static string[] CleanLines(string[] source)
        {
            List<string> lines = new List<string>();
            if (source != null)
            {
                for (int index = 0; index < source.Length; index++)
                {
                    if (!string.IsNullOrWhiteSpace(source[index]))
                    {
                        lines.Add(source[index].Trim());
                    }
                }
            }

            return lines.ToArray();
        }

        private static FleeComprehensionCheck[] CleanChecks(FleeComprehensionCheck[] source)
        {
            List<FleeComprehensionCheck> checks = new List<FleeComprehensionCheck>();
            if (source != null)
            {
                for (int index = 0; index < source.Length; index++)
                {
                    FleeComprehensionCheck check = source[index];
                    if (check != null && !string.IsNullOrWhiteSpace(check.checkId))
                    {
                        checks.Add(check);
                    }
                }
            }

            return checks.ToArray();
        }
    }
}

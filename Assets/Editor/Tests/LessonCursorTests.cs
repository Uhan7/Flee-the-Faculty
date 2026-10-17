using NUnit.Framework;

// LessonCursor is plain C#, so these tests build their Lessons in code and need
// no scene, no JSON and no Unity API.
public class LessonCursorTests
{
    // Two sections, four steps:
    //   section-1: step-a (two lines, no question), step-b (one line, AraBOT Question)
    //   section-2: step-c (three lines), step-d (one line)
    private static FleeLessonView MakeLesson()
    {
        return new FleeLessonView
        {
            classroomId = "classroom-1",
            topic = "Test topic",
            language = "en",
            sections = new[]
            {
                new FleeLessonSection
                {
                    sectionId = "section-1",
                    title = "One",
                    steps = new[]
                    {
                        MakeStep("step-a", "Board A", "A1", "A2"),
                        MakeStep("step-b", "Board B", "B1")
                    },
                    checks = new[]
                    {
                        new FleeComprehensionCheck { checkId = "check-1", question = "Q1?" },
                        new FleeComprehensionCheck { checkId = "check-2", question = "Q2?" }
                    }
                },
                new FleeLessonSection
                {
                    sectionId = "section-2",
                    title = "Two",
                    steps = new[]
                    {
                        MakeStep("step-c", "Board C", "C1", "C2", "C3"),
                        MakeStep("step-d", "Board D", "D1")
                    },
                    checks = new[]
                    {
                        new FleeComprehensionCheck { checkId = "check-3", question = "Q3?" }
                    }
                }
            }
        };
    }

    private static FleeLessonStep MakeStep(string stepId, string heading, params string[] lines)
    {
        return new FleeLessonStep
        {
            stepId = stepId,
            dialogue = lines,
            whiteboard = new FleeWhiteboard
            {
                heading = heading,
                bullets = new[] { heading + " bullet" },
                figureId = string.Empty
            },
            arabotQuestion = stepId == "step-b"
                ? new FleeAraBotQuestion { questionId = "arabot-1", line = "Why?" }
                : new FleeAraBotQuestion()
        };
    }

    [Test]
    public void NullLesson_HasNothingToTeach()
    {
        LessonCursor cursor = new LessonCursor(null);

        Assert.IsFalse(cursor.HasSteps);
        Assert.AreEqual(0, cursor.StepCount);
        Assert.IsNull(cursor.CurrentStep);
        Assert.IsNull(cursor.CurrentWhiteboard);
        Assert.AreEqual(string.Empty, cursor.CurrentLine);
        Assert.AreEqual(LessonMove.Stayed, cursor.Next());
        Assert.AreEqual(LessonMove.Stayed, cursor.NextStep());
        Assert.IsFalse(cursor.Back());
        Assert.IsFalse(cursor.BeginReplay("step-a"));
    }

    [Test]
    public void LessonWithNullPiecesAndNoSteps_HasNothingToTeach()
    {
        Assert.IsFalse(new LessonCursor(new FleeLessonView()).HasSteps);
        Assert.IsFalse(new LessonCursor(new FleeLessonView { sections = new FleeLessonSection[] { null } }).HasSteps);
        Assert.IsFalse(new LessonCursor(new FleeLessonView
        {
            sections = new[] { new FleeLessonSection { steps = new FleeLessonStep[] { null } } }
        }).HasSteps);
    }

    [Test]
    public void StartsOnTheFirstLineOfTheFirstStep()
    {
        LessonCursor cursor = new LessonCursor(MakeLesson());

        Assert.IsTrue(cursor.HasSteps);
        Assert.AreEqual(4, cursor.StepCount);
        Assert.AreEqual(2, cursor.SectionCount);
        Assert.AreEqual(0, cursor.StepIndex);
        Assert.AreEqual(1, cursor.StepNumber);
        Assert.AreEqual(0, cursor.SectionIndex);
        Assert.AreEqual(0, cursor.StepIndexInSection);
        Assert.AreEqual("step-a", cursor.CurrentStepId);
        Assert.AreEqual("Board A", cursor.CurrentWhiteboard.heading);
        Assert.AreEqual(2, cursor.LineCount);
        Assert.AreEqual(0, cursor.LineIndex);
        Assert.AreEqual("A1", cursor.CurrentLine);
        Assert.IsTrue(cursor.IsFirstStep);
        Assert.IsFalse(cursor.IsAtEndOfStep);
        Assert.IsFalse(cursor.IsFinished);
    }

    [Test]
    public void Next_WalksTheLinesThenSignalsEndOfStepWithoutMoving()
    {
        LessonCursor cursor = new LessonCursor(MakeLesson());

        Assert.AreEqual(LessonMove.Line, cursor.Next());
        Assert.AreEqual("A2", cursor.CurrentLine);
        Assert.IsTrue(cursor.IsAtEndOfStep);

        Assert.AreEqual(LessonMove.EndOfStep, cursor.Next());
        Assert.AreEqual(LessonMove.EndOfStep, cursor.Next());
        Assert.AreEqual("step-a", cursor.CurrentStepId);
        Assert.AreEqual("A2", cursor.CurrentLine);
    }

    [Test]
    public void NextStep_MovesWithinASectionThenAcrossToTheNextOne()
    {
        LessonCursor cursor = new LessonCursor(MakeLesson());
        cursor.SetLine(1);

        Assert.AreEqual(LessonMove.Step, cursor.NextStep());
        Assert.AreEqual("step-b", cursor.CurrentStepId);
        Assert.AreEqual(0, cursor.LineIndex);
        Assert.AreEqual(1, cursor.StepIndexInSection);

        Assert.AreEqual(LessonMove.Section, cursor.NextStep());
        Assert.AreEqual("step-c", cursor.CurrentStepId);
        Assert.AreEqual(1, cursor.SectionIndex);
        Assert.AreEqual(0, cursor.StepIndexInSection);
        Assert.AreEqual(2, cursor.StepIndex);

        Assert.AreEqual(LessonMove.Step, cursor.NextStep());
        Assert.AreEqual("step-d", cursor.CurrentStepId);
    }

    [Test]
    public void NextStep_OnTheLastStepFinishesAndStaysPut()
    {
        LessonCursor cursor = new LessonCursor(MakeLesson());
        cursor.NextStep();
        cursor.NextStep();
        cursor.NextStep();

        Assert.IsTrue(cursor.IsLastStep);
        Assert.IsFalse(cursor.IsFinished);
        Assert.AreEqual(LessonMove.Finished, cursor.NextStep());
        Assert.IsTrue(cursor.IsFinished);
        Assert.AreEqual("step-d", cursor.CurrentStepId);
        Assert.AreEqual(LessonMove.Finished, cursor.NextStep());
    }

    [Test]
    public void IsLastStepOfSection_MarksWhereTheChecksAreAsked()
    {
        LessonCursor cursor = new LessonCursor(MakeLesson());

        Assert.IsFalse(cursor.IsLastStepOfSection);
        cursor.NextStep();
        Assert.IsTrue(cursor.IsLastStepOfSection);
        Assert.AreEqual(2, cursor.CurrentChecks.Count);
        Assert.AreEqual("check-1", cursor.CurrentChecks[0].checkId);

        cursor.NextStep();
        Assert.IsFalse(cursor.IsLastStepOfSection);
        cursor.NextStep();
        Assert.IsTrue(cursor.IsLastStepOfSection);
        Assert.AreEqual(1, cursor.CurrentChecks.Count);
        Assert.AreEqual("check-3", cursor.CurrentChecks[0].checkId);
    }

    [Test]
    public void Back_DoesNothingOnTheFirstStep()
    {
        LessonCursor cursor = new LessonCursor(MakeLesson());
        cursor.Next();

        Assert.IsFalse(cursor.CanGoBack);
        Assert.IsFalse(cursor.Back());
        Assert.AreEqual("step-a", cursor.CurrentStepId);
        Assert.AreEqual(1, cursor.LineIndex);
    }

    [Test]
    public void Back_GoesToThePreviousStepFromItsFirstLine()
    {
        LessonCursor cursor = new LessonCursor(MakeLesson());
        cursor.NextStep();
        cursor.NextStep();
        cursor.Next();

        Assert.AreEqual("step-c", cursor.CurrentStepId);
        Assert.AreEqual(1, cursor.LineIndex);
        Assert.IsTrue(cursor.CanGoBack);

        // Back crosses a section boundary, and lands on the first line of the
        // step it returns to, not on the line before.
        Assert.IsTrue(cursor.Back());
        Assert.AreEqual("step-b", cursor.CurrentStepId);
        Assert.AreEqual(0, cursor.LineIndex);
        Assert.AreEqual("Board B", cursor.CurrentWhiteboard.heading);

        Assert.IsTrue(cursor.Back());
        Assert.AreEqual("step-a", cursor.CurrentStepId);
        Assert.IsFalse(cursor.Back());
    }

    [Test]
    public void Back_AfterFinishingClearsFinished()
    {
        LessonCursor cursor = new LessonCursor(MakeLesson());
        while (cursor.NextStep() != LessonMove.Finished)
        {
        }

        Assert.IsTrue(cursor.IsFinished);
        Assert.IsTrue(cursor.Back());
        Assert.IsFalse(cursor.IsFinished);
        Assert.AreEqual("step-c", cursor.CurrentStepId);
    }

    [Test]
    public void SetLine_IsClampedToTheStepsLines()
    {
        LessonCursor cursor = new LessonCursor(MakeLesson());

        cursor.SetLine(99);
        Assert.AreEqual(1, cursor.LineIndex);
        cursor.SetLine(-5);
        Assert.AreEqual(0, cursor.LineIndex);
    }

    [Test]
    public void BlankLinesAndMissingPiecesAreSkipped()
    {
        FleeLessonView lesson = new FleeLessonView
        {
            sections = new[]
            {
                null,
                new FleeLessonSection
                {
                    sectionId = "s",
                    steps = new[]
                    {
                        null,
                        new FleeLessonStep { stepId = "only", dialogue = new[] { "  ", null, "Hello.", "" } }
                    },
                    checks = new[]
                    {
                        null,
                        new FleeComprehensionCheck { checkId = " ", question = "no id" },
                        new FleeComprehensionCheck { checkId = "kept", question = "ok" }
                    }
                }
            }
        };

        LessonCursor cursor = new LessonCursor(lesson);

        Assert.AreEqual(1, cursor.StepCount);
        Assert.AreEqual(1, cursor.LineCount);
        Assert.AreEqual("Hello.", cursor.CurrentLine);
        Assert.AreEqual(1, cursor.CurrentChecks.Count);
        Assert.AreEqual("kept", cursor.CurrentChecks[0].checkId);
        Assert.IsFalse(cursor.HasAraBotQuestion);
        Assert.AreEqual(LessonMove.EndOfStep, cursor.Next());
    }

    [Test]
    public void AStepWithNoLines_IsAlreadyAtItsEnd()
    {
        FleeLessonView lesson = new FleeLessonView
        {
            sections = new[]
            {
                new FleeLessonSection { steps = new[] { new FleeLessonStep { stepId = "silent" } } }
            }
        };

        LessonCursor cursor = new LessonCursor(lesson);

        Assert.AreEqual(0, cursor.LineCount);
        Assert.AreEqual(string.Empty, cursor.CurrentLine);
        Assert.IsTrue(cursor.IsAtEndOfStep);
        Assert.AreEqual(LessonMove.EndOfStep, cursor.Next());
    }

    [Test]
    public void AraBotQuestion_IsOnlyOnTheStepThatHasOne()
    {
        LessonCursor cursor = new LessonCursor(MakeLesson());

        Assert.IsFalse(cursor.HasAraBotQuestion);
        Assert.IsNull(cursor.CurrentAraBotQuestion);

        cursor.NextStep();
        Assert.IsTrue(cursor.HasAraBotQuestion);
        Assert.AreEqual("arabot-1", cursor.CurrentAraBotQuestion.questionId);
    }

    [Test]
    public void BeginReplay_JumpsToTheStepAndEndReplayComesBack()
    {
        LessonCursor cursor = new LessonCursor(MakeLesson());
        cursor.NextStep();
        cursor.NextStep();
        cursor.Next();

        Assert.IsTrue(cursor.BeginReplay("step-a"));
        Assert.IsTrue(cursor.IsReplaying);
        Assert.AreEqual("step-a", cursor.CurrentStepId);
        Assert.AreEqual(0, cursor.LineIndex);
        Assert.AreEqual("Board A", cursor.CurrentWhiteboard.heading);

        // The replay plays through like any step.
        Assert.AreEqual(LessonMove.Line, cursor.Next());
        Assert.AreEqual(LessonMove.EndOfStep, cursor.Next());

        int resumeAt = cursor.EndReplay();
        Assert.AreEqual(1, resumeAt);
        Assert.IsFalse(cursor.IsReplaying);
        Assert.AreEqual("step-c", cursor.CurrentStepId);
        Assert.AreEqual(1, cursor.LineIndex);
        Assert.AreEqual("C2", cursor.CurrentLine);
        Assert.AreEqual("Board C", cursor.CurrentWhiteboard.heading);
    }

    [Test]
    public void EndReplay_ResumesAtTheSavedLineAndTheLessonCarriesOn()
    {
        LessonCursor cursor = new LessonCursor(MakeLesson());
        cursor.SetLine(1);

        cursor.BeginReplay("step-d");
        cursor.EndReplay();

        Assert.AreEqual("step-a", cursor.CurrentStepId);
        Assert.AreEqual(1, cursor.LineIndex);
        Assert.AreEqual(LessonMove.EndOfStep, cursor.Next());
        Assert.AreEqual(LessonMove.Step, cursor.NextStep());
        Assert.AreEqual("step-b", cursor.CurrentStepId);
    }

    [Test]
    public void BeginReplay_WithAnUnknownIdMovesNothing()
    {
        LessonCursor cursor = new LessonCursor(MakeLesson());
        cursor.NextStep();

        Assert.IsFalse(cursor.BeginReplay("no-such-step"));
        Assert.IsFalse(cursor.BeginReplay(null));
        Assert.IsFalse(cursor.BeginReplay("  "));
        Assert.IsFalse(cursor.IsReplaying);
        Assert.AreEqual("step-b", cursor.CurrentStepId);
    }

    [Test]
    public void ReplayingAgain_KeepsTheOriginalReturnPoint()
    {
        LessonCursor cursor = new LessonCursor(MakeLesson());
        cursor.NextStep();
        cursor.NextStep();
        cursor.SetLine(2);

        cursor.BeginReplay("step-a");
        cursor.BeginReplay("step-d");
        Assert.AreEqual("step-d", cursor.CurrentStepId);
        Assert.AreEqual(2, cursor.ReplayReturnStepIndex);
        Assert.AreEqual(2, cursor.ReplayReturnLineIndex);

        Assert.AreEqual(2, cursor.EndReplay());
        Assert.AreEqual("step-c", cursor.CurrentStepId);
        Assert.AreEqual("C3", cursor.CurrentLine);
    }

    [Test]
    public void DuringAReplay_BackAndNextStepDoNothing()
    {
        LessonCursor cursor = new LessonCursor(MakeLesson());
        cursor.NextStep();
        cursor.BeginReplay("step-c");

        Assert.IsFalse(cursor.CanGoBack);
        Assert.IsFalse(cursor.Back());
        Assert.AreEqual(LessonMove.Stayed, cursor.NextStep());
        Assert.AreEqual("step-c", cursor.CurrentStepId);

        cursor.EndReplay();
        Assert.AreEqual("step-b", cursor.CurrentStepId);
        Assert.IsTrue(cursor.CanGoBack);
    }

    [Test]
    public void EndReplay_WithoutAReplayDoesNothing()
    {
        LessonCursor cursor = new LessonCursor(MakeLesson());
        cursor.NextStep();

        Assert.AreEqual(-1, cursor.EndReplay());
        Assert.AreEqual("step-b", cursor.CurrentStepId);
    }
}

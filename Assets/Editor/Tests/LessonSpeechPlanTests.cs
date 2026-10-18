using System.Collections.Generic;
using NUnit.Framework;

// LessonSpeechPlan is plain C#. The real paginator lives in the game assembly,
// which this test assembly cannot see, so a stand-in that cuts on the sentence
// marker " | " plays its part: it is enough to show how pages map to lines.
public class LessonSpeechPlanTests
{
    private static IReadOnlyList<string> CutOnBar(string text)
    {
        return text.Split(new[] { " | " }, System.StringSplitOptions.RemoveEmptyEntries);
    }

    [Test]
    public void Build_KeepsEachLineAsOnePageWithoutASplitter()
    {
        List<LessonSpeechPage> pages = LessonSpeechPlan.Build(
            new[] { "One.", "Two.", "Three." },
            0,
            LessonSpeaker.Teacher,
            null);

        Assert.AreEqual(3, pages.Count);
        Assert.AreEqual("Two.", pages[1].Text);
        Assert.AreEqual(1, pages[1].SourceLineIndex);
        Assert.AreEqual(LessonSpeaker.Teacher, pages[2].Speaker);
    }

    [Test]
    public void Build_PagesOfOneLongLineShareItsIndex()
    {
        List<LessonSpeechPage> pages = LessonSpeechPlan.Build(
            new[] { "First part. | Second part.", "Next." },
            0,
            LessonSpeaker.Teacher,
            CutOnBar);

        Assert.AreEqual(3, pages.Count);
        Assert.AreEqual("First part.", pages[0].Text);
        Assert.AreEqual(0, pages[0].SourceLineIndex);
        Assert.AreEqual("Second part.", pages[1].Text);
        Assert.AreEqual(0, pages[1].SourceLineIndex);
        Assert.AreEqual(1, pages[2].SourceLineIndex);
    }

    [Test]
    public void Build_StartsAtTheSavedLineAndKeepsOriginalNumbers()
    {
        // She interrupted on line 1, so the plan resumes there, and the pages
        // still say 1 and 2, not 0 and 1: the cursor counts the whole step.
        List<LessonSpeechPage> pages = LessonSpeechPlan.Build(
            new[] { "A.", "B. | B again.", "C." },
            1,
            LessonSpeaker.Teacher,
            CutOnBar);

        Assert.AreEqual(3, pages.Count);
        Assert.AreEqual("B.", pages[0].Text);
        Assert.AreEqual(1, pages[0].SourceLineIndex);
        Assert.AreEqual(2, pages[2].SourceLineIndex);
    }

    [Test]
    public void Build_SkipsBlankLinesWithoutRenumbering()
    {
        List<LessonSpeechPage> pages = LessonSpeechPlan.Build(
            new[] { "A.", "   ", null, "D." },
            0,
            LessonSpeaker.Teacher,
            null);

        Assert.AreEqual(2, pages.Count);
        Assert.AreEqual(3, pages[1].SourceLineIndex);
    }

    [Test]
    public void Build_StartPastTheEndOrNullGivesNothing()
    {
        Assert.AreEqual(0, LessonSpeechPlan.Build(new[] { "A." }, 5, LessonSpeaker.Teacher, null).Count);
        Assert.AreEqual(0, LessonSpeechPlan.Build(null, 0, LessonSpeaker.Teacher, null).Count);
    }

    [Test]
    public void Build_NegativeStartCountsFromTheTop()
    {
        List<LessonSpeechPage> pages = LessonSpeechPlan.Build(
            new[] { "A.", "B." },
            -3,
            LessonSpeaker.Teacher,
            null);

        Assert.AreEqual(2, pages.Count);
        Assert.AreEqual(0, pages[0].SourceLineIndex);
    }

    [Test]
    public void Build_AFailingSplitterFallsBackToTheWholeLine()
    {
        List<LessonSpeechPage> pages = LessonSpeechPlan.Build(
            new[] { "Whole line." },
            0,
            LessonSpeaker.AraBot,
            text => new string[0]);

        Assert.AreEqual(1, pages.Count);
        Assert.AreEqual("Whole line.", pages[0].Text);
        Assert.AreEqual(LessonSpeaker.AraBot, pages[0].Speaker);
    }

    [Test]
    public void BuildSingle_NumbersTheLineZeroAndTrimsIt()
    {
        List<LessonSpeechPage> pages = LessonSpeechPlan.BuildSingle(
            "  Hello there.  ",
            LessonSpeaker.AraBot,
            null);

        Assert.AreEqual(1, pages.Count);
        Assert.AreEqual("Hello there.", pages[0].Text);
        Assert.AreEqual(0, pages[0].SourceLineIndex);
        Assert.AreEqual(0, LessonSpeechPlan.BuildSingle("", LessonSpeaker.Teacher, null).Count);
        Assert.AreEqual(0, LessonSpeechPlan.BuildSingle(null, LessonSpeaker.Teacher, null).Count);
    }

    [Test]
    public void Append_AddsToAnExistingPlan()
    {
        List<LessonSpeechPage> pages = new List<LessonSpeechPage>();
        LessonSpeechPlan.Append(pages, "Quick check.", LessonSpeaker.Teacher, 0, null);
        LessonSpeechPlan.Append(pages, "What is it?", LessonSpeaker.Teacher, 1, null);
        LessonSpeechPlan.Append(pages, "", LessonSpeaker.Teacher, 2, null);

        Assert.AreEqual(2, pages.Count);
        Assert.AreEqual(1, pages[1].SourceLineIndex);
    }

    [Test]
    public void SourceLineOf_AnswersTheLineOfAPageAndMinusOneOutsideThePlan()
    {
        List<LessonSpeechPage> pages = LessonSpeechPlan.Build(
            new[] { "A. | A2.", "B." },
            0,
            LessonSpeaker.Teacher,
            CutOnBar);

        Assert.AreEqual(0, LessonSpeechPlan.SourceLineOf(pages, 1));
        Assert.AreEqual(1, LessonSpeechPlan.SourceLineOf(pages, 2));
        Assert.AreEqual(-1, LessonSpeechPlan.SourceLineOf(pages, 3));
        Assert.AreEqual(-1, LessonSpeechPlan.SourceLineOf(pages, -1));
        Assert.AreEqual(-1, LessonSpeechPlan.SourceLineOf(null, 0));
    }

    [Test]
    public void ResumeThroughTheCursor_StartsOnTheLineThatWasInterrupted()
    {
        // The whole job, end to end: dialogue boxes report pages, the plan turns
        // a page into a line, the cursor remembers it, and the plan rebuilt from
        // that line picks up where she left off.
        FleeLessonView lesson = new FleeLessonView
        {
            sections = new[]
            {
                new FleeLessonSection
                {
                    sectionId = "s1",
                    steps = new[]
                    {
                        new FleeLessonStep
                        {
                            stepId = "a",
                            dialogue = new[] { "Line zero.", "Line one. | Line one again.", "Line two." },
                            whiteboard = new FleeWhiteboard()
                        }
                    }
                }
            }
        };
        LessonCursor cursor = new LessonCursor(lesson);
        List<LessonSpeechPage> pages = LessonSpeechPlan.Build(
            cursor.CurrentLines,
            0,
            LessonSpeaker.Teacher,
            CutOnBar);

        // She is on the second box of line one when she presses the mic.
        cursor.SetLine(LessonSpeechPlan.SourceLineOf(pages, 2));
        Assert.AreEqual(1, cursor.LineIndex);

        List<LessonSpeechPage> resumed = LessonSpeechPlan.Build(
            cursor.CurrentLines,
            cursor.LineIndex,
            LessonSpeaker.Teacher,
            CutOnBar);

        Assert.AreEqual("Line one.", resumed[0].Text);
        Assert.AreEqual("Line two.", resumed[resumed.Count - 1].Text);
        Assert.AreEqual(3, resumed.Count);
    }
}

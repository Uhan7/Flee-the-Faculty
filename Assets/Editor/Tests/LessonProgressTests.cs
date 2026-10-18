using NUnit.Framework;

public class LessonProgressTests
{
    [Test]
    public void AQuestionIsAskedOnce()
    {
        LessonProgress progress = new LessonProgress();

        Assert.IsTrue(progress.ShouldAskQuestion("q1"));
        progress.MarkQuestionAsked("q1");
        Assert.IsFalse(progress.ShouldAskQuestion("q1"));
        Assert.IsFalse(progress.ShouldAskQuestion("  q1  "));
        Assert.IsTrue(progress.ShouldAskQuestion("q2"));
    }

    [Test]
    public void ABlankQuestionIdIsNeverAsked()
    {
        LessonProgress progress = new LessonProgress();

        Assert.IsFalse(progress.ShouldAskQuestion(""));
        Assert.IsFalse(progress.ShouldAskQuestion("   "));
        Assert.IsFalse(progress.ShouldAskQuestion(null));
        progress.MarkQuestionAsked(null);
        progress.MarkQuestionAsked("");
    }

    [Test]
    public void ASectionsChecksRunOnce()
    {
        LessonProgress progress = new LessonProgress();

        Assert.IsTrue(progress.ShouldRunChecks("section-1"));
        progress.MarkChecksRun("section-1");
        Assert.IsFalse(progress.ShouldRunChecks("section-1"));
        Assert.IsTrue(progress.ShouldRunChecks("section-2"));
    }

    [Test]
    public void QuestionsAndChecksAreTrackedSeparately()
    {
        LessonProgress progress = new LessonProgress();
        progress.MarkQuestionAsked("same");

        Assert.IsTrue(progress.ShouldRunChecks("same"));
    }

    [Test]
    public void SectionKey_FallsBackToThePositionWhenTheIdIsBlank()
    {
        Assert.AreEqual("photo", LessonProgress.SectionKey(" photo ", 4));
        Assert.AreEqual("section-4", LessonProgress.SectionKey("", 4));
        Assert.AreEqual("section-0", LessonProgress.SectionKey(null, 0));
    }

    [Test]
    public void Ledger_RemembersAClassroomThatHadItsLearningPhase()
    {
        LearningRunLedger ledger = new LearningRunLedger();

        Assert.IsFalse(ledger.HasRun("classroom-1"));
        ledger.MarkRun("classroom-1");
        Assert.IsTrue(ledger.HasRun("classroom-1"));
        Assert.IsTrue(ledger.HasRun(" classroom-1 "));
        Assert.IsFalse(ledger.HasRun("classroom-2"));
    }

    [Test]
    public void Ledger_IgnoresBlankIds()
    {
        LearningRunLedger ledger = new LearningRunLedger();
        ledger.MarkRun("");
        ledger.MarkRun(null);

        Assert.IsFalse(ledger.HasRun(""));
        Assert.IsFalse(ledger.HasRun(null));
    }

    [Test]
    public void Ledger_ClearForgetsEverything()
    {
        LearningRunLedger ledger = new LearningRunLedger();
        ledger.MarkRun("classroom-1");
        ledger.Clear();

        Assert.IsFalse(ledger.HasRun("classroom-1"));
    }

    [Test]
    public void Ledger_IdsAreCaseSensitive()
    {
        LearningRunLedger ledger = new LearningRunLedger();
        ledger.MarkRun("Abc");

        Assert.IsFalse(ledger.HasRun("abc"));
    }
}

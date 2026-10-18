using NUnit.Framework;

public class LessonLocalLinesTests
{
    [Test]
    public void ClosingLine_ShoutsTheTopicInEnglish()
    {
        Assert.AreEqual(
            "YIPPEE! I UNDERSTAND THE WATER CYCLE NOW!!",
            LessonLocalLines.ClosingLine("en", "the water cycle"));
    }

    [Test]
    public void ClosingLine_FlattensWhitespaceAndCapsTheLength()
    {
        string spaced = LessonLocalLines.ClosingLine("en", "  photo \n synthesis\t ");
        Assert.AreEqual("YIPPEE! I UNDERSTAND PHOTO SYNTHESIS NOW!!", spaced);

        string longTopic = new string('a', 200);
        string capped = LessonLocalLines.ClosingLine("en", longTopic);
        Assert.AreEqual(
            "YIPPEE! I UNDERSTAND " + new string('A', 60) + " NOW!!",
            capped);
    }

    [Test]
    public void ClosingLine_WithoutATopicStillMakesSense()
    {
        Assert.AreEqual("YIPPEE! I UNDERSTAND IT NOW!!", LessonLocalLines.ClosingLine("en", ""));
        Assert.AreEqual("YIPPEE! I UNDERSTAND IT NOW!!", LessonLocalLines.ClosingLine("en", null));
        Assert.AreEqual("YIPPEE! I UNDERSTAND IT NOW!!", LessonLocalLines.ClosingLine(null, "   "));
    }

    [Test]
    public void ClosingLine_SpeaksFilipinoForAFilipinoLesson()
    {
        string line = LessonLocalLines.ClosingLine("fil", "ang siklo ng tubig");
        StringAssert.StartsWith("YEHEY!", line);
        StringAssert.Contains("ANG SIKLO NG TUBIG", line);
        Assert.AreEqual("YEHEY! NAINTINDIHAN KO NA NGAYON!!", LessonLocalLines.ClosingLine("FIL", ""));
    }

    [Test]
    public void IsFilipino_ReadsTheWireValueAndNothingElse()
    {
        Assert.IsTrue(LessonLocalLines.IsFilipino("fil"));
        Assert.IsTrue(LessonLocalLines.IsFilipino(" FIL "));
        Assert.IsFalse(LessonLocalLines.IsFilipino("en"));
        Assert.IsFalse(LessonLocalLines.IsFilipino(""));
        Assert.IsFalse(LessonLocalLines.IsFilipino(null));
    }

    [Test]
    public void EveryFixedLineIsNonBlankInBothLanguages()
    {
        foreach (string language in new[] { "en", "fil" })
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(LessonLocalLines.CheckIntro(language)), language);
            Assert.IsFalse(string.IsNullOrWhiteSpace(LessonLocalLines.InterjectionFailed(language)), language);
            Assert.IsFalse(string.IsNullOrWhiteSpace(LessonLocalLines.CheckFailed(language)), language);
            Assert.IsFalse(string.IsNullOrWhiteSpace(LessonLocalLines.AraBotAnswerFailed(language)), language);
            Assert.IsFalse(string.IsNullOrWhiteSpace(LessonLocalLines.AskInstructions(language)), language);
            Assert.IsFalse(string.IsNullOrWhiteSpace(LessonLocalLines.AnswerInstructions(language)), language);
            Assert.IsFalse(string.IsNullOrWhiteSpace(LessonLocalLines.CancelLabel(language)), language);
            Assert.IsFalse(string.IsNullOrWhiteSpace(LessonLocalLines.SkipLabel(language)), language);
        }
    }

    [Test]
    public void TheTeacherNeverAddressesAraDirectly()
    {
        // In Learning the Teacher talks to AraBOT, and her mic reaches the
        // Teacher through AraBOT. The Teacher's own fixed lines leave "Ara" out.
        foreach (string language in new[] { "en", "fil" })
        {
            StringAssert.DoesNotContain("Ara", LessonLocalLines.CheckIntro(language));
            StringAssert.DoesNotContain("Ara", LessonLocalLines.CheckFailed(language));
            StringAssert.DoesNotContain("Ara", LessonLocalLines.InterjectionFailed(language));
        }
    }

    [Test]
    public void AnswerText_TrimsWhatWasSaid()
    {
        Assert.AreEqual("plants make sugar", LessonAnswerText.Clean("  plants make sugar \n"));
    }

    [Test]
    public void AnswerText_TheStockNoSpeechSentencesAreNotAnswers()
    {
        Assert.AreEqual(string.Empty, LessonAnswerText.Clean("No speech was submitted."));
        Assert.AreEqual(string.Empty, LessonAnswerText.Clean("  No speech or fallback text was submitted. "));
        Assert.AreEqual(string.Empty, LessonAnswerText.Clean(""));
        Assert.AreEqual(string.Empty, LessonAnswerText.Clean(null));
        Assert.AreEqual(string.Empty, LessonAnswerText.Clean("   "));
    }

    [Test]
    public void AnswerText_AnAnswerThatMerelyContainsTheSentenceStaysAnAnswer()
    {
        string answer = LessonAnswerText.Clean("I said No speech was submitted. today");
        Assert.AreEqual("I said No speech was submitted. today", answer);
    }
}

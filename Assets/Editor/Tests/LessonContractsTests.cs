using NUnit.Framework;
using UnityEngine;

// The wire models and the mock Lesson, read the way the game reads them: through
// JsonUtility. These need the editor (JsonUtility and Resources), which is why
// they are separate from the plain-C# cursor tests.
public class LessonContractsTests
{
    private const string MockResourcePath = "Mock/lesson-view.photosynthesis";

    private static string LoadMockText()
    {
        TextAsset asset = Resources.Load<TextAsset>(MockResourcePath);
        Assert.IsNotNull(asset, "Assets/Resources/" + MockResourcePath + ".json is missing.");
        return asset.text;
    }

    private static FleeLessonView LoadMock()
    {
        FleeLessonView lesson = JsonUtility.FromJson<FleeLessonView>(LoadMockText());
        Assert.IsNotNull(lesson);
        return lesson;
    }

    [Test]
    public void MockLesson_ParsesAsAPhotosynthesisLesson()
    {
        FleeLessonView lesson = LoadMock();

        Assert.AreEqual("Photosynthesis", lesson.topic);
        Assert.AreEqual("en", lesson.language);
        Assert.IsFalse(string.IsNullOrEmpty(lesson.classroomId));
        Assert.AreEqual(1, lesson.sections.Length);
        Assert.AreEqual(3, lesson.sections[0].steps.Length);
        Assert.AreEqual(1, lesson.sections[0].checks.Length);
        Assert.AreEqual("check-1", lesson.sections[0].checks[0].checkId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(lesson.sections[0].checks[0].question));
    }

    [Test]
    public void MockLesson_HasExactlyOneAraBotQuestion()
    {
        FleeLessonStep[] steps = LoadMock().sections[0].steps;

        int questions = 0;
        for (int index = 0; index < steps.Length; index++)
        {
            if (steps[index].HasAraBotQuestion)
            {
                questions++;
                Assert.AreEqual("arabot-1", steps[index].arabotQuestion.questionId);
                Assert.IsFalse(string.IsNullOrWhiteSpace(steps[index].arabotQuestion.line));
            }
        }

        Assert.AreEqual(1, questions);
    }

    [Test]
    public void MockLesson_StepsWithNullQuestionsReadAsNoQuestion()
    {
        // The mock writes "arabotQuestion": null on two steps. JsonUtility turns
        // that into an empty instance, never null, and HasAraBotQuestion is how
        // the rest of the code asks.
        FleeLessonStep[] steps = LoadMock().sections[0].steps;

        Assert.IsFalse(steps[0].HasAraBotQuestion);
        Assert.IsTrue(steps[1].HasAraBotQuestion);
        Assert.IsFalse(steps[2].HasAraBotQuestion);
    }

    [Test]
    public void MockLesson_StaysInsideTheLessonLimits()
    {
        FleeLessonStep[] steps = LoadMock().sections[0].steps;

        for (int index = 0; index < steps.Length; index++)
        {
            FleeLessonStep step = steps[index];
            Assert.IsFalse(string.IsNullOrWhiteSpace(step.stepId));
            Assert.That(step.dialogue.Length, Is.InRange(1, 4));
            for (int line = 0; line < step.dialogue.Length; line++)
            {
                Assert.LessOrEqual(step.dialogue[line].Length, 300);
            }

            Assert.IsFalse(string.IsNullOrWhiteSpace(step.whiteboard.heading));
            Assert.LessOrEqual(step.whiteboard.bullets.Length, 6);
            for (int bullet = 0; bullet < step.whiteboard.bullets.Length; bullet++)
            {
                Assert.LessOrEqual(step.whiteboard.bullets[bullet].Length, 80);
            }

            Assert.IsTrue(string.IsNullOrEmpty(step.whiteboard.figureId));
        }
    }

    [Test]
    public void MockLesson_CarriesNoAnswerKey()
    {
        string text = LoadMockText().ToLowerInvariant();

        Assert.IsFalse(text.Contains("modelanswer"));
        Assert.IsFalse(text.Contains("reactionline"));
        Assert.IsFalse(text.Contains("correction"));
        Assert.IsFalse(text.Contains("misconception"));
    }

    [Test]
    public void MockLesson_CanBeWalkedFromStartToFinish()
    {
        LessonCursor cursor = new LessonCursor(LoadMock());

        Assert.IsTrue(cursor.HasSteps);
        Assert.AreEqual(3, cursor.StepCount);
        Assert.AreEqual(1, cursor.SectionCount);

        int stepsSeen = 1;
        while (true)
        {
            while (cursor.Next() == LessonMove.Line)
            {
            }

            if (cursor.NextStep() == LessonMove.Finished)
            {
                break;
            }

            stepsSeen++;
        }

        Assert.AreEqual(3, stepsSeen);
        Assert.IsTrue(cursor.IsLastStepOfSection);
        Assert.AreEqual(1, cursor.CurrentChecks.Count);
    }

    [Test]
    public void AraBotQuestion_NullMissingOrBlankAllReadAsNone()
    {
        FleeLessonStep nullQuestion = JsonUtility.FromJson<FleeLessonStep>(
            "{\"stepId\":\"s\",\"dialogue\":[\"Hi.\"],\"arabotQuestion\":null}");
        FleeLessonStep missingQuestion = JsonUtility.FromJson<FleeLessonStep>(
            "{\"stepId\":\"s\",\"dialogue\":[\"Hi.\"]}");
        FleeLessonStep blankQuestion = JsonUtility.FromJson<FleeLessonStep>(
            "{\"stepId\":\"s\",\"arabotQuestion\":{\"questionId\":\"\",\"line\":\"\"}}");
        FleeLessonStep realQuestion = JsonUtility.FromJson<FleeLessonStep>(
            "{\"stepId\":\"s\",\"arabotQuestion\":{\"questionId\":\"q1\",\"line\":\"Why?\"}}");

        Assert.IsFalse(nullQuestion.HasAraBotQuestion);
        Assert.IsFalse(missingQuestion.HasAraBotQuestion);
        Assert.IsFalse(blankQuestion.HasAraBotQuestion);
        Assert.IsTrue(realQuestion.HasAraBotQuestion);
    }

    [Test]
    public void NullableResponseFields_ReadAsBlank()
    {
        FleeLessonQuestionResponse deferral = JsonUtility.FromJson<FleeLessonQuestionResponse>(
            "{\"teacherLine\":\"Let's come back to that.\",\"replayStepId\":null}");
        FleeLessonQuestionResponse replay = JsonUtility.FromJson<FleeLessonQuestionResponse>(
            "{\"teacherLine\":\"Good one.\",\"replayStepId\":\"step-2\"}");
        FleeAraBotAnswerResponse arabotOnly = JsonUtility.FromJson<FleeAraBotAnswerResponse>(
            "{\"arabotLine\":\"Oh!\",\"teacherLine\":null,\"replayStepId\":null}");
        FleeAraBotAnswerResponse arabotWithReplay = JsonUtility.FromJson<FleeAraBotAnswerResponse>(
            "{\"arabotLine\":\"Oh?\",\"teacherLine\":\"Let's look again.\",\"replayStepId\":\"step-1\"}");

        Assert.IsFalse(deferral.HasReplay);
        Assert.IsTrue(replay.HasReplay);
        Assert.AreEqual("step-2", replay.replayStepId);
        Assert.IsFalse(arabotOnly.HasTeacherLine);
        Assert.IsFalse(arabotOnly.HasReplay);
        Assert.IsTrue(arabotWithReplay.HasTeacherLine);
        Assert.IsTrue(arabotWithReplay.HasReplay);
    }

    [Test]
    public void CheckResponse_ReadsTheModelAnswerOnlyFromTheResponse()
    {
        FleeCheckAnswerResponse response = JsonUtility.FromJson<FleeCheckAnswerResponse>(
            "{\"teacherLine\":\"Here is how I would say it.\",\"modelAnswer\":\"Sugar and oxygen.\"}");

        Assert.AreEqual("Sugar and oxygen.", response.modelAnswer);
    }

    [Test]
    public void Requests_SerialiseWithTheCamelCaseNamesTheServiceExpects()
    {
        string question = JsonUtility.ToJson(new FleeLessonQuestionRequest
        {
            classroomId = "c",
            stepId = "s",
            question = "q"
        });
        string check = JsonUtility.ToJson(new FleeCheckAnswerRequest
        {
            classroomId = "c",
            checkId = "k",
            answer = "a"
        });
        string arabot = JsonUtility.ToJson(new FleeAraBotAnswerRequest
        {
            classroomId = "c",
            questionId = "k",
            answer = "a"
        });

        StringAssert.Contains("\"classroomId\":\"c\"", question);
        StringAssert.Contains("\"stepId\":\"s\"", question);
        StringAssert.Contains("\"question\":\"q\"", question);
        StringAssert.Contains("\"checkId\":\"k\"", check);
        StringAssert.Contains("\"answer\":\"a\"", check);
        StringAssert.Contains("\"questionId\":\"k\"", arabot);
        StringAssert.Contains("\"answer\":\"a\"", arabot);
    }
}

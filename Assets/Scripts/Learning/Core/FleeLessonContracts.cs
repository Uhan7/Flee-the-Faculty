using System;

// The wire models for the Learning phase: what the service sends for a Lesson
// and what the client sends back for a Learner Interjection, a Comprehension
// Check and an AraBOT Question.
//
// They mirror `LessonView` and friends in `contract.py` of the service, field
// for field and in camelCase, because JsonUtility matches on the field name.
// The client does not vendor the service's schema, so a change there is a
// change here by hand.
//
// Why these are public top-level classes when the DTOs in FleeApiClient are
// private nested ones: those are mapped into immutable session objects before
// anybody else sees them, whereas a Lesson is read-only content that the
// cursor, the views and the tests all consume as it arrived. A copy of it
// would only be a second place to forget a field. They live in the
// Flee.Learning.Core assembly (see the asmdef beside this file) so an EditMode
// test assembly can reference them, which Assembly-CSharp does not allow.
//
// Three things to know when reading a Lesson that came through JsonUtility:
//  - A JSON null for a nested object becomes an empty instance rather than
//    null. A blank `questionId` therefore means "this step has no AraBOT
//    Question", and `HasAraBotQuestion` is the one way to ask.
//  - A JSON null for a string becomes "". `figureId`, `replayStepId` and
//    `teacherLine` are nullable on the wire, so blank means none.
//  - Arrays can come through null. Read them with the helpers on these
//    classes, or null-check, never index them blind.
//
// There is no answer key anywhere in here, by design: no model answer on a
// check, no reaction line and no Correction on an AraBOT Question. The service
// holds those and speaks them only after she has answered.

[Serializable]
public sealed class FleeLessonView
{
    public string classroomId;
    public string topic;

    /// <summary>`en` or `fil`, same as the Classroom's.</summary>
    public string language;

    public FleeLessonSection[] sections;
}

[Serializable]
public sealed class FleeLessonSection
{
    public string sectionId;
    public string title;
    public FleeLessonStep[] steps;

    /// <summary>
    /// One or two questions the Teacher asks once the section's steps are done.
    /// Ungraded: she answers aloud, then the service sends the model answer.
    /// </summary>
    public FleeComprehensionCheck[] checks;
}

[Serializable]
public sealed class FleeLessonStep
{
    public string stepId;

    /// <summary>One to four Teacher lines, each under 300 characters.</summary>
    public string[] dialogue;

    /// <summary>The whole board at this step, not a delta.</summary>
    public FleeWhiteboard whiteboard;

    /// <summary>
    /// Empty (blank `questionId`) on most steps. See
    /// <see cref="HasAraBotQuestion"/>.
    /// </summary>
    public FleeAraBotQuestion arabotQuestion;

    public bool HasAraBotQuestion =>
        arabotQuestion != null && !string.IsNullOrWhiteSpace(arabotQuestion.questionId);
}

[Serializable]
public sealed class FleeWhiteboard
{
    public string heading;

    /// <summary>At most 6, each at most 80 characters.</summary>
    public string[] bullets;

    /// <summary>Reserved. Always blank for now: the board is text only.</summary>
    public string figureId;
}

[Serializable]
public sealed class FleeAraBotQuestion
{
    public string questionId;

    /// <summary>What AraBOT asks. Her answer is said aloud, never typed in.</summary>
    public string line;
}

[Serializable]
public sealed class FleeComprehensionCheck
{
    public string checkId;
    public string question;
}

// POST /v1/lessons/questions: the Learner Interjection. `question` is what she
// said, machine-transcribed, and the service caps it at 300 characters. It is
// never logged on either side.

[Serializable]
public sealed class FleeLessonQuestionRequest
{
    public string classroomId;
    public string stepId;
    public string question;
}

[Serializable]
public sealed class FleeLessonQuestionResponse
{
    public string teacherLine;

    /// <summary>Blank when the Teacher defers instead of replaying a step.</summary>
    public string replayStepId;

    public bool HasReplay => !string.IsNullOrWhiteSpace(replayStepId);
}

// POST /v1/lessons/checks: she answers a Comprehension Check aloud.

[Serializable]
public sealed class FleeCheckAnswerRequest
{
    public string classroomId;
    public string checkId;
    public string answer;
}

[Serializable]
public sealed class FleeCheckAnswerResponse
{
    public string teacherLine;

    /// <summary>The verified model answer, said after she has answered.</summary>
    public string modelAnswer;
}

// POST /v1/lessons/arabot: she answers an AraBOT Question aloud.

[Serializable]
public sealed class FleeAraBotAnswerRequest
{
    public string classroomId;
    public string questionId;
    public string answer;
}

[Serializable]
public sealed class FleeAraBotAnswerResponse
{
    /// <summary>AraBOT's pregenerated reaction.</summary>
    public string arabotLine;

    /// <summary>Blank when the Teacher has nothing to add.</summary>
    public string teacherLine;

    /// <summary>Blank when no step should be replayed.</summary>
    public string replayStepId;

    public bool HasTeacherLine => !string.IsNullOrWhiteSpace(teacherLine);
    public bool HasReplay => !string.IsNullOrWhiteSpace(replayStepId);
}

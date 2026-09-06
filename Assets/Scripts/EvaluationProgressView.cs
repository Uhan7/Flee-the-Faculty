using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class EvaluationProgressView : MonoBehaviour
{
    [SerializeField] private GameObject[] studentSlots;
    [SerializeField] private GameObject[] passedMarks;
    [SerializeField] private GameObject[] failedMarks;
    [SerializeField] private TMP_Text assessedCountText;

    public int SlotCount => studentSlots != null ? studentSlots.Length : 0;

    public void Initialize(int studentCount)
    {
        int visibleStudentCount = Mathf.Clamp(studentCount, 0, SlotCount);
        for (int index = 0; index < SlotCount; index++)
        {
            if (studentSlots[index] != null)
            {
                studentSlots[index].SetActive(index < visibleStudentCount);
            }

            SetMarkVisible(passedMarks, index, false);
            SetMarkVisible(failedMarks, index, false);
        }

        SetAssessedCount(0, studentCount);

        if (studentCount > SlotCount)
        {
            Debug.LogWarning(
                "The Evaluation Progress HUD has " + SlotCount + " slots for " +
                studentCount + " students.",
                this);
        }
    }

    public void SetStudentResult(int studentIndex, bool passed, int evaluated, int total)
    {
        SetMarkVisible(passedMarks, studentIndex, passed);
        SetMarkVisible(failedMarks, studentIndex, !passed);
        SetAssessedCount(evaluated, total);
    }

    private void SetAssessedCount(int evaluated, int total)
    {
        if (assessedCountText != null)
        {
            assessedCountText.text = evaluated + " / " + total + " assessed";
        }
    }

    private static void SetMarkVisible(GameObject[] marks, int index, bool visible)
    {
        if (marks != null && index >= 0 && index < marks.Length && marks[index] != null)
        {
            marks[index].SetActive(visible);
        }
    }
}

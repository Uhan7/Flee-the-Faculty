using System;
using UnityEngine;

[CreateAssetMenu(fileName = "Report Rating Settings", menuName = "Flee the Faculty/Report Rating Settings")]
public sealed class ReportRatingSettings : ScriptableObject
{
    [Serializable]
    public sealed class RatingBand
    {
        [Range(0, 100)] public int minimumScore;
        public string grade;
        [TextArea(1, 3)] public string[] possibleTexts = Array.Empty<string>();

        public string PickText()
        {
            string[] choices = possibleTexts == null
                ? Array.Empty<string>()
                : Array.FindAll(possibleTexts, text => !string.IsNullOrWhiteSpace(text));
            return choices.Length > 0 ? choices[UnityEngine.Random.Range(0, choices.Length)] : grade;
        }
    }

    [SerializeField, TextArea(3, 6)] private string gradingReference;
    [Tooltip("The highest qualifying minimum score wins. Each band has its own random phrase bank.")]
    [SerializeField] private RatingBand[] bands = Array.Empty<RatingBand>();

    public RatingBand[] Bands => bands;

    public static ReportRatingSettings Load()
    {
        return Resources.Load<ReportRatingSettings>("Report Rating Settings");
    }

    public RatingBand Resolve(int score)
    {
        RatingBand result = null;
        foreach (RatingBand band in bands)
        {
            if (band != null && score >= band.minimumScore
                && (result == null || band.minimumScore > result.minimumScore))
            {
                result = band;
            }
        }
        return result;
    }
}

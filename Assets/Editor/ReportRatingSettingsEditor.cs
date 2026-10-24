using UnityEditor;

public static class ReportRatingSettingsEditor
{
    [MenuItem("Flee the Faculty/Edit Report Ratings")]
    public static void SelectSettings()
    {
        ReportRatingSettings settings = ReportRatingSettings.Load();
        Selection.activeObject = settings;
        EditorGUIUtility.PingObject(settings);
    }
}

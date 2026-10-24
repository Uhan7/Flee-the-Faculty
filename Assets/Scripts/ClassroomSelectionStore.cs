using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Applies the title-screen classroom choice to the persistent API client.
/// </summary>
public static class ClassroomSelectionStore
{
    public const int DefaultGradeLevel = 5;
    public const int MinimumStudentCount = 3;
    public const int MaximumStudentCount = 8;
    public const int DefaultAutomaticStudentCount = 5;
    public const int MaximumMaterialCharacters = 3000;

    public static void SelectPreset(string topic, string presetId)
    {
        FleeApiClient.ResetClassroomSession();
        FleeApiClient.GetOrCreate().ConfigureClassroomSource(
            false,
            topic,
            DefaultGradeLevel,
            presetId);
        FleeApiClient.GetOrCreate().ConfigureClassroomLanguage(
            FleeClassroomLanguage.English,
            string.Empty);
        FleeApiClient.GetOrCreate().ConfigureStudentCount(0);
    }

    public static void SelectCustom(
        string topic,
        string material,
        int gradeLevel,
        FleeClassroomLanguage language,
        string additionalNotes,
        int studentCount)
    {
        FleeApiClient.ResetClassroomSession();
        FleeApiClient client = FleeApiClient.GetOrCreate();
        client.ConfigureClassroomSource(
            true,
            BuildGroundedTopic(topic, material),
            Mathf.Clamp(gradeLevel, 1, 12),
            string.Empty);
        client.ConfigureClassroomLanguage(language, additionalNotes);
        client.ConfigureStudentCount(ClampStudentCount(studentCount));
    }

    public static void SelectMaterials(
        string[] materialIds,
        FleeClassroomLanguage language,
        string additionalNotes,
        int studentCount,
        int gradeLevel = DefaultGradeLevel)
    {
        FleeApiClient.ResetClassroomSession();
        FleeApiClient client = FleeApiClient.GetOrCreate();
        client.ConfigureClassroomMaterials(materialIds, gradeLevel);
        client.ConfigureClassroomLanguage(language, additionalNotes);
        client.ConfigureStudentCount(ClampStudentCount(studentCount));
    }

    public static int ClampStudentCount(int studentCount)
    {
        return Mathf.Clamp(studentCount, MinimumStudentCount, MaximumStudentCount);
    }

    public static int EstimateStudentCountFromMaterial(string material)
    {
        if (string.IsNullOrWhiteSpace(material))
        {
            return DefaultAutomaticStudentCount;
        }

        string[] lines = material.Replace("\r", string.Empty).Split('\n');
        int questionCount = 0;
        int listItemCount = 0;
        int sentenceCount = 0;
        int wordCount = 0;
        bool previousWasWordCharacter = false;

        for (int index = 0; index < material.Length; index++)
        {
            char character = material[index];
            if (character == '?')
            {
                questionCount++;
            }

            if (character == '.' || character == '!' || character == '?')
            {
                sentenceCount++;
            }

            bool isWordCharacter = char.IsLetterOrDigit(character);
            if (isWordCharacter && !previousWasWordCharacter)
            {
                wordCount++;
            }

            previousWasWordCharacter = isWordCharacter;
        }

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].TrimStart();
            if (line.StartsWith("- ", StringComparison.Ordinal)
                || line.StartsWith("* ", StringComparison.Ordinal)
                || StartsWithNumberedItem(line))
            {
                listItemCount++;
            }
        }

        int learningItemEstimate = Mathf.Max(
            questionCount,
            listItemCount,
            Mathf.CeilToInt(sentenceCount / 2f),
            Mathf.CeilToInt(wordCount / 80f));
        return StudentCountForLearningItems(learningItemEstimate);
    }

    public static int EstimateStudentCountFromUpload(FleeMaterialSession material)
    {
        if (material == null)
        {
            return DefaultAutomaticStudentCount;
        }

        int questionCount = material.SampleQuestions != null ? material.SampleQuestions.Length : 0;
        int topicCount = material.Topics != null ? material.Topics.Length : 0;
        int vocabularyCount = material.Vocabulary != null ? material.Vocabulary.Length : 0;
        int learningItemEstimate = Mathf.Max(
            questionCount,
            topicCount * 2,
            Mathf.CeilToInt(vocabularyCount / 3f));
        return learningItemEstimate > 0
            ? StudentCountForLearningItems(learningItemEstimate)
            : DefaultAutomaticStudentCount;
    }

    private static int StudentCountForLearningItems(int learningItemCount)
    {
        if (learningItemCount <= 2)
        {
            return 3;
        }

        if (learningItemCount <= 4)
        {
            return 4;
        }

        if (learningItemCount <= 6)
        {
            return 5;
        }

        if (learningItemCount <= 9)
        {
            return 6;
        }

        if (learningItemCount <= 13)
        {
            return 7;
        }

        return 8;
    }

    private static bool StartsWithNumberedItem(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || !char.IsDigit(line[0]))
        {
            return false;
        }

        int index = 1;
        while (index < line.Length && char.IsDigit(line[index]))
        {
            index++;
        }

        return index < line.Length && (line[index] == '.' || line[index] == ')');
    }

    public static string BuildGroundedTopic(string topic, string material)
    {
        string safeTopic = string.IsNullOrWhiteSpace(topic)
            ? "the uploaded lesson"
            : CollapseWhitespace(topic.Trim());
        string safeMaterial = CollapseWhitespace(material);

        if (safeMaterial.Length > MaximumMaterialCharacters)
        {
            safeMaterial = safeMaterial.Substring(0, MaximumMaterialCharacters).TrimEnd();
        }

        if (string.IsNullOrWhiteSpace(safeMaterial))
        {
            return safeTopic;
        }

        return safeTopic
            + ". Create every student misconception, question, expected answer, and evaluation "
            + "using only the learning material below. Do not test facts that are not stated or "
            + "directly supported by it."
            + " Learning material: "
            + safeMaterial;
    }

    public static string CollapseWhitespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        StringBuilder result = new StringBuilder(value.Length);
        bool previousWasWhitespace = false;
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    result.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            result.Append(character);
            previousWasWhitespace = false;
        }

        return result.ToString().Trim();
    }
}

[Serializable]
public sealed class SavedClassroomDefinition
{
    public string id;
    public string topic;
    public string material;
    public string materialId;
    public int gradeLevel;
    public FleeClassroomLanguage language;
    public string additionalNotes;
    public int studentCountOverride;
    public int automaticStudentCount;
    public int paperStyle = -1;
}

public static class SavedClassroomStore
{
    private const string StorageKey = "Flee.SavedCustomClassrooms.v1";

    [Serializable]
    private sealed class SavedClassroomCollection
    {
        public List<SavedClassroomDefinition> classrooms = new List<SavedClassroomDefinition>();
    }

    public static IReadOnlyList<SavedClassroomDefinition> LoadAll()
    {
        return Load().classrooms;
    }

    public static SavedClassroomDefinition Find(string classroomId)
    {
        SavedClassroomCollection collection = Load();
        for (int index = 0; index < collection.classrooms.Count; index++)
        {
            SavedClassroomDefinition classroom = collection.classrooms[index];
            if (classroom != null && string.Equals(classroom.id, classroomId, StringComparison.Ordinal))
            {
                return classroom;
            }
        }

        return null;
    }

    public static SavedClassroomDefinition Save(
        string classroomId,
        string topic,
        string material,
        string materialId,
        int gradeLevel,
        FleeClassroomLanguage language,
        string additionalNotes,
        int studentCountOverride,
        int automaticStudentCount,
        int paperStyle = -1)
    {
        SavedClassroomCollection collection = Load();
        SavedClassroomDefinition saved = null;
        for (int index = 0; index < collection.classrooms.Count; index++)
        {
            if (collection.classrooms[index] != null
                && string.Equals(collection.classrooms[index].id, classroomId, StringComparison.Ordinal))
            {
                saved = collection.classrooms[index];
                collection.classrooms.RemoveAt(index);
                break;
            }
        }

        if (saved == null)
        {
            saved = new SavedClassroomDefinition
            {
                id = Guid.NewGuid().ToString("N")
            };
        }

        saved.topic = string.IsNullOrWhiteSpace(topic) ? "My uploaded lesson" : topic.Trim();
        saved.material = string.IsNullOrWhiteSpace(material) ? string.Empty : material.Trim();
        saved.materialId = string.IsNullOrWhiteSpace(materialId) ? string.Empty : materialId.Trim();
        saved.gradeLevel = Mathf.Clamp(gradeLevel, 1, 12);
        if (paperStyle >= 0)
        {
            saved.paperStyle = Mathf.Clamp(paperStyle, 0, 9);
        }
        saved.language = language;
        saved.additionalNotes = string.IsNullOrWhiteSpace(additionalNotes)
            ? string.Empty
            : additionalNotes.Trim();
        saved.studentCountOverride = studentCountOverride <= 0
            ? 0
            : ClassroomSelectionStore.ClampStudentCount(studentCountOverride);
        saved.automaticStudentCount = ClassroomSelectionStore.ClampStudentCount(
            automaticStudentCount <= 0
                ? ClassroomSelectionStore.DefaultAutomaticStudentCount
                : automaticStudentCount);
        collection.classrooms.Insert(0, saved);

        Persist(collection);
        return saved;
    }

    public static void Delete(string classroomId)
    {
        SavedClassroomCollection collection = Load();
        collection.classrooms.RemoveAll(classroom => classroom == null
            || string.Equals(classroom.id, classroomId, StringComparison.Ordinal));
        Persist(collection);
    }

    private static SavedClassroomCollection Load()
    {
        string json = PlayerPrefs.GetString(StorageKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new SavedClassroomCollection();
        }

        try
        {
            SavedClassroomCollection collection = JsonUtility.FromJson<SavedClassroomCollection>(json);
            if (collection == null)
            {
                return new SavedClassroomCollection();
            }

            collection.classrooms ??= new List<SavedClassroomDefinition>();
            collection.classrooms.RemoveAll(classroom => classroom == null);
            if (json.IndexOf("\"paperStyle\"", StringComparison.Ordinal) < 0)
            {
                // Older recipes did not retain a paper choice. Keep their existing
                // fallback colour instead of mistaking an absent field for Aqua.
                foreach (SavedClassroomDefinition classroom in collection.classrooms)
                {
                    classroom.paperStyle = -1;
                }
            }
            return collection;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Saved classrooms could not be read: " + exception.Message);
            return new SavedClassroomCollection();
        }
    }

    private static void Persist(SavedClassroomCollection collection)
    {
        PlayerPrefs.SetString(StorageKey, JsonUtility.ToJson(collection));
        PlayerPrefs.Save();
    }
}

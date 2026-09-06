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
    public const int MaximumMaterialCharacters = 3000;
    private const string EnglishOnlyInstruction =
        " Use English for every student's question, response, explanation, and teacher evaluation, "
        + "even when the lesson topic or learning material is Filipino.";

    public static void SelectPreset(string topic, string presetId)
    {
        FleeApiClient.ResetClassroomSession();
        FleeApiClient.GetOrCreate().ConfigureClassroomSource(
            false,
            topic,
            DefaultGradeLevel,
            presetId);
    }

    public static void SelectCustom(string topic, string material, int gradeLevel)
    {
        FleeApiClient.ResetClassroomSession();
        FleeApiClient.GetOrCreate().ConfigureClassroomSource(
            true,
            BuildGroundedTopic(topic, material),
            Mathf.Clamp(gradeLevel, 1, 12),
            string.Empty);
    }

    public static void SelectMaterials(params string[] materialIds)
    {
        FleeApiClient.ResetClassroomSession();
        FleeApiClient.GetOrCreate().ConfigureClassroomMaterials(materialIds);
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
            return safeTopic + "." + EnglishOnlyInstruction;
        }

        return safeTopic
            + ". Create every student misconception, question, expected answer, and evaluation "
            + "using only the learning material below. Do not test facts that are not stated or "
            + "directly supported by it."
            + EnglishOnlyInstruction
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
}

public static class SavedClassroomStore
{
    private const string StorageKey = "Flee.SavedCustomClassrooms.v1";
    private const int MaximumSavedClassrooms = 5;

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
        int gradeLevel)
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
        collection.classrooms.Insert(0, saved);
        if (collection.classrooms.Count > MaximumSavedClassrooms)
        {
            collection.classrooms.RemoveRange(
                MaximumSavedClassrooms,
                collection.classrooms.Count - MaximumSavedClassrooms);
        }

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

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>Retains original uploads on this device, separate from the classroom recipe.</summary>
public static class ClassroomSourceFiles
{
    [Serializable]
    private sealed class Metadata
    {
        public string fileName;
        public string contentType;
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void FleeDownloadWorksheet(string fileName, string contentType, string base64);
    [DllImport("__Internal")] private static extern void FleeSyncWorksheetFiles();
#endif

    private static string GetPath(string materialId)
    {
        using (SHA256 hash = SHA256.Create())
        {
            string key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(materialId))).Replace("-", string.Empty);
            return Path.Combine(Application.persistentDataPath, "Classroom Sources", key);
        }
    }

    public static bool Exists(string materialId)
    {
        return !string.IsNullOrWhiteSpace(materialId)
            && File.Exists(GetPath(materialId) + ".bin")
            && File.Exists(GetPath(materialId) + ".json");
    }

    public static void Save(string materialId, string fileName, string contentType, byte[] contents)
    {
        string path = GetPath(materialId);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path + ".bin", contents);
        File.WriteAllText(path + ".json", JsonUtility.ToJson(new Metadata
        {
            fileName = Path.GetFileName(fileName),
            contentType = contentType
        }));
#if UNITY_WEBGL && !UNITY_EDITOR
        FleeSyncWorksheetFiles();
#endif
    }

    public static void Retrieve(string materialId)
    {
        if (!Exists(materialId))
        {
            throw new FileNotFoundException("The original upload was not saved on this device. Upload it again to keep a downloadable copy.");
        }
        string path = GetPath(materialId);
        Metadata metadata = JsonUtility.FromJson<Metadata>(File.ReadAllText(path + ".json"));
        byte[] contents = File.ReadAllBytes(path + ".bin");
#if UNITY_WEBGL && !UNITY_EDITOR
        FleeDownloadWorksheet(metadata.fileName, metadata.contentType, Convert.ToBase64String(contents));
#elif UNITY_EDITOR
        string destination = UnityEditor.EditorUtility.SaveFilePanel("Save original classroom file", string.Empty, metadata.fileName, Path.GetExtension(metadata.fileName).TrimStart('.'));
        if (!string.IsNullOrEmpty(destination))
        {
            File.WriteAllBytes(destination, contents);
        }
#else
        string destination = Path.Combine(Application.temporaryCachePath, metadata.fileName);
        File.WriteAllBytes(destination, contents);
        Application.OpenURL(new Uri(destination).AbsoluteUri);
#endif
    }
}

using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Put the speech model next to the WebGL build, so it deploys with the game.
///
/// The model is 146MB, which GitHub rejects, so it cannot live in the project.
/// It does not have to: the build output is not the repository. Copying it in
/// after the build means the game fetches it from its own origin, with no CORS
/// and no dependence on anyone else's hosting staying up.
///
/// The file is downloaded once into a cache outside Assets and reused by every
/// build after that. Nothing here runs for non-WebGL targets.
/// </summary>
public sealed class VoiceModelPostBuild : IPostprocessBuildWithReport
{
    /// <summary>Where the model sits inside the build, relative to its root.</summary>
    public const string RelativePath = "StreamingAssets/Voices/runtime/model.gguf";

    /// <summary>
    /// Kyutai's weights, quantised to 8 bits by Laurent Mazare. CC BY 4.0.
    /// Downloaded once, then cached outside Assets so Unity never imports it.
    /// </summary>
    private const string SourceUrl =
        "https://huggingface.co/lmz/pocket-tts-without-voice-cloning-q8/resolve/main/tts_b6369a24.gguf";

    private const string TokenizerUrl =
        "https://huggingface.co/kyutai/pocket-tts-without-voice-cloning/resolve/main/tokenizer.model";

    private const long ExpectedModelBytes = 146499264;

    public int callbackOrder => 0;

    private static string CacheDirectory =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "VoiceModel"));

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.WebGL)
        {
            return;
        }

        string buildRoot = report.summary.outputPath;
        if (File.Exists(buildRoot))
        {
            buildRoot = Path.GetDirectoryName(buildRoot);
        }

        if (!EnsureCached())
        {
            Debug.LogWarning(
                "The speech model is not available, so this build will fetch it from "
                + "HuggingFace at run time instead of from its own origin. Run "
                + "Flee the Faculty > Voices > Download Speech Model and build again.");
            return;
        }

        Copy(Path.Combine(CacheDirectory, "model.gguf"), Path.Combine(buildRoot, RelativePath));
        Copy(
            Path.Combine(CacheDirectory, "tokenizer.model"),
            Path.Combine(buildRoot, "StreamingAssets/Voices/runtime/tokenizer.model"));

        Debug.Log($"Speech model copied into the build at {RelativePath}.");
    }

    [MenuItem("Flee the Faculty/Voices/Download Speech Model", priority = 102)]
    public static void DownloadNow()
    {
        if (EnsureCached())
        {
            EditorUtility.DisplayDialog(
                "Speech model ready",
                $"Cached in {CacheDirectory}.\n\n"
                + "Every WebGL build from now on copies it into the build output, so the "
                + "game loads it from its own origin.",
                "OK");
        }
    }

    /// <summary>
    /// Download the model and tokenizer if they are not cached yet. Returns
    /// false when the download fails, which is a warning rather than a build
    /// failure: the game still works, it just fetches from HuggingFace instead.
    /// </summary>
    private static bool EnsureCached()
    {
        Directory.CreateDirectory(CacheDirectory);
        return Fetch(SourceUrl, Path.Combine(CacheDirectory, "model.gguf"), ExpectedModelBytes)
            && Fetch(TokenizerUrl, Path.Combine(CacheDirectory, "tokenizer.model"), 0);
    }

    private static bool Fetch(string url, string destination, long expectedBytes)
    {
        if (File.Exists(destination)
            && (expectedBytes == 0 || new FileInfo(destination).Length == expectedBytes))
        {
            return true;
        }

        string label = Path.GetFileName(destination);
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.downloadHandler = new DownloadHandlerFile(destination);
            request.SendWebRequest();

            try
            {
                while (!request.isDone)
                {
                    bool cancelled = EditorUtility.DisplayCancelableProgressBar(
                        "Downloading the speech model",
                        $"{label}, {request.downloadedBytes / 1_000_000}MB of "
                        + $"{(expectedBytes > 0 ? expectedBytes / 1_000_000 : 0)}MB",
                        request.downloadProgress);

                    if (cancelled)
                    {
                        request.Abort();
                        break;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"Could not download {label}: {request.error}");
                SafeDelete(destination);
                return false;
            }
        }

        return true;
    }

    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        File.Copy(source, destination, overwrite: true);
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A partial file that cannot be removed is not worth failing over.
            // The length check on the next attempt rejects it anyway.
        }
    }
}

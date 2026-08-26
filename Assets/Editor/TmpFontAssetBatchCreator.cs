using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public static class TmpFontAssetBatchCreator
{
    private const string OutputFolder = "Assets/Fonts/TMP";

    private static readonly FontDefinition[] FontDefinitions =
    {
        new FontDefinition("Assets/Fonts/~Non-TMP/Bangpop-8OlyZ.ttf", "Bangpop TMP SDF"),
        new FontDefinition("Assets/Fonts/~Non-TMP/CozyAutumns-aYomo.ttf", "Cozy Autumns TMP SDF"),
        new FontDefinition("Assets/Fonts/~Non-TMP/HandsonBold-9MnrL.otf", "Handson Bold TMP SDF"),
        new FontDefinition("Assets/Fonts/~Non-TMP/MalamPoek-7OlZE.ttf", "Malam Poek TMP SDF"),
        new FontDefinition("Assets/Fonts/~Non-TMP/MatchaMint-7OdpB.ttf", "Matcha Mint TMP SDF")
    };

    [MenuItem("Tools/Create Missing TMP Font Assets")]
    public static void CreateMissingFontAssets()
    {
        List<string> createdAssets = new List<string>();

        foreach (FontDefinition definition in FontDefinitions)
        {
            string outputPath = $"{OutputFolder}/{definition.AssetName}.asset";
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(outputPath) != null)
            {
                continue;
            }

            Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(definition.SourcePath);
            if (sourceFont == null)
            {
                Debug.LogWarning($"Could not create TMP font asset because the source font is missing: {definition.SourcePath}");
                continue;
            }

            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
                sourceFont,
                90,
                9,
                GlyphRenderMode.SDFAA,
                1024,
                1024,
                AtlasPopulationMode.Dynamic,
                true);

            if (fontAsset == null)
            {
                Debug.LogWarning($"TextMesh Pro could not load the font face: {definition.SourcePath}");
                continue;
            }

            fontAsset.name = definition.AssetName;
            fontAsset.atlasTextures[0].name = $"{definition.AssetName} Atlas";
            fontAsset.material.name = $"{definition.AssetName} Material";

            AssetDatabase.CreateAsset(fontAsset, outputPath);
            AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            EditorUtility.SetDirty(fontAsset);
            createdAssets.Add(outputPath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(createdAssets.Count > 0
            ? $"Created {createdAssets.Count} TMP font assets:\n{string.Join("\n", createdAssets)}"
            : "All configured TMP font assets already exist.");
    }

    private readonly struct FontDefinition
    {
        public FontDefinition(string sourcePath, string assetName)
        {
            SourcePath = sourcePath;
            AssetName = assetName;
        }

        public string SourcePath { get; }
        public string AssetName { get; }
    }
}

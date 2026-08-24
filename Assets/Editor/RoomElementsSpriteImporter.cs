using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

internal sealed class RoomElementsSpriteImporter : AssetPostprocessor
{
    private const string RoomElementsPath = "Assets/Sprites/Classroom/Room Elements.png";
    private static readonly Vector4 BoxBorder = new Vector4(20f, 20f, 20f, 20f);

    public override uint GetVersion()
    {
        return 1;
    }

    private void OnPreprocessTexture()
    {
        if (assetPath != RoomElementsPath)
        {
            return;
        }

        TextureImporter textureImporter = (TextureImporter)assetImporter;
        textureImporter.textureType = TextureImporterType.Sprite;
        textureImporter.spriteImportMode = SpriteImportMode.Multiple;
        textureImporter.spritePixelsPerUnit = 100f;
        textureImporter.mipmapEnabled = false;
        textureImporter.alphaIsTransparency = true;

        TextureImporterSettings textureSettings = new TextureImporterSettings();
        textureImporter.ReadTextureSettings(textureSettings);
        textureSettings.spriteMeshType = SpriteMeshType.FullRect;
        textureImporter.SetTextureSettings(textureSettings);

        SpriteRect[] spriteRects = CreateSpriteRects();
        SpriteDataProviderFactories factories = new SpriteDataProviderFactories();
        factories.Init();

        ISpriteEditorDataProvider dataProvider = factories.GetSpriteEditorDataProviderFromObject(assetImporter);
        dataProvider.InitSpriteEditorDataProvider();
        dataProvider.SetSpriteRects(spriteRects);

        ISpriteNameFileIdDataProvider nameProvider = dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        nameProvider?.SetNameFileIdPairs(
            spriteRects.Select(spriteRect => new SpriteNameFileIdPair(spriteRect.name, spriteRect.spriteID)));
        dataProvider.Apply();
    }

    private static SpriteRect[] CreateSpriteRects()
    {
        List<SpriteRect> sprites = new List<SpriteRect>(31)
        {
            CreateBox("RoomBox_R1_C1", 16, 1440, 301, 301, "4551251cf003f4b90800000000000000"),
            CreateBox("RoomBox_R1_C2", 406, 1437, 301, 301, "c7c341bfd452af330800000000000000"),
            CreateBox("RoomBox_R1_C3", 771, 1441, 301, 301, "116d1d443d7c4f710800000000000000"),
            CreateBox("RoomBox_R1_C4", 1161, 1439, 301, 300, "8e1c61746ee839e20800000000000000"),
            CreateBox("RoomBox_R1_C5", 1537, 1437, 301, 300, "19539f580f961bef0800000000000000"),

            CreateBox("RoomBox_R2_C1", 15, 1067, 300, 301, "21250ba005981d3c0800000000000000"),
            CreateBox("RoomBox_R2_C2", 408, 1062, 301, 301, "8683be9d4021245b0800000000000000"),
            CreateBox("RoomBox_R2_C3", 769, 1069, 301, 300, "c862f39b9883ae4b0800000000000000"),
            CreateBox("RoomBox_R2_C4", 1162, 1063, 301, 301, "700a2227f12ba5ea0800000000000000"),
            CreateBox("RoomBox_R2_C5", 1539, 1061, 300, 301, "6d4affb2cf6491cc0800000000000000"),

            CreateBox("RoomBox_R3_C1", 15, 720, 301, 301, "6a40fbfdbe8b9e670800000000000000"),
            CreateBox("RoomBox_R3_C2", 408, 715, 301, 301, "484c569772f68b520800000000000000"),
            CreateBox("RoomBox_R3_C3", 769, 722, 301, 301, "a0594aa1f70e08190800000000000000"),
            CreateBox("RoomBox_R3_C4", 1163, 717, 300, 300, "dfabafe3f504f61a0800000000000000"),
            CreateBox("RoomBox_R3_C5", 1539, 715, 301, 300, "03d92f5b5e35e2840800000000000000"),

            CreateBox("RoomBox_R4_C1", 18, 366, 301, 301, "2a65c0934e96cb9a0800000000000000"),
            CreateBox("RoomBox_R4_C2", 411, 361, 301, 301, "b18a32b5ec0ad85a0800000000000000"),
            CreateBox("RoomBox_R4_C3", 773, 368, 301, 301, "abac56d59658bfb30800000000000000"),
            CreateBox("RoomBox_R4_C4", 1166, 363, 301, 300, "6cf973674b8de3d10800000000000000"),
            CreateBox("RoomBox_R4_C5", 1542, 361, 301, 300, "589b3353ba7ce4780800000000000000"),

            CreateBox("RoomBox_R5_C1", 18, 20, 301, 301, "6b43658e198101720800000000000000"),
            CreateBox("RoomBox_R5_C2", 412, 15, 301, 300, "03dc26949e0789d60800000000000000"),
            CreateBox("RoomBox_R5_C3", 773, 21, 301, 301, "23b78fae0debf45f0800000000000000"),
            CreateBox("RoomBox_R5_C4", 1166, 16, 301, 301, "0bea0637f731a2620800000000000000"),
            CreateBox("RoomBox_R5_C5", 1542, 14, 301, 301, "c2da8579a68996700800000000000000"),

            CreateSprite("RoomPattern_Stripes_Light", 1982, 1440, 300, 300, "00d711873496fa090800000000000000"),
            CreateSprite("RoomPattern_Stripes_Warm", 2422, 1450, 300, 300, "3b0baad7fbd145f70800000000000000"),
            CreateSprite("Door_Double", 2035, 897, 536, 501, "304d417ebea641700800000000000000"),
            CreateSprite("Door_Left", 1984, 366, 253, 477, "0d723680746aa6a70800000000000000"),
            CreateSprite("Door_Right", 2343, 356, 254, 475, "5a53c6fc1b2317c90800000000000000"),
            CreateSprite("WallDecal_Brick", 2017, 136, 149, 70, "1b605556695b3ae30800000000000000")
        };

        return sprites.ToArray();
    }

    private static SpriteRect CreateBox(string name, float x, float y, float width, float height, string spriteId)
    {
        SpriteRect spriteRect = CreateSprite(name, x, y, width, height, spriteId);
        spriteRect.border = BoxBorder;
        return spriteRect;
    }

    private static SpriteRect CreateSprite(string name, float x, float y, float width, float height, string spriteId)
    {
        return new SpriteRect
        {
            name = name,
            rect = new Rect(x, y, width, height),
            alignment = SpriteAlignment.Center,
            pivot = new Vector2(0.5f, 0.5f),
            border = Vector4.zero,
            spriteID = new GUID(spriteId)
        };
    }
}

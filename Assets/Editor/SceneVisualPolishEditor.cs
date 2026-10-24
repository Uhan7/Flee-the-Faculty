using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

[CustomEditor(typeof(SceneVisualPolish))]
public sealed class SceneVisualPolishEditor : Editor
{
    private const string PrefabPath = "Assets/Resources/Visual Polish Rig.prefab";
    private const string VisualPolishFolder = "Assets/Visual Polish";
    private const string ShadowTexturePath = VisualPolishFolder + "/Soft Contact Shadow.png";
    private const string DustMaterialPath = VisualPolishFolder + "/Ambient Dust.mat";
    private const string StudentPrefabPath = "Assets/Prefabs/Base Student.prefab";
    private const string AraBotPrefabPath = "Assets/Prefabs/AraBOT.prefab";
    private const string RenderPipelineAssetPath = "Assets/~Others/Settings/UniversalRP.asset";
    private const float BalancedRenderScale = 0.8f;
    private const float BalancedShadowSoftness = 0.15f;
    private const int BalancedDustParticleLimit = 36;
    private const int MaximumRoomLights = 8;

    private static readonly string[] PrimaryScenePaths =
    {
        "Assets/Scenes/Main Menu.unity",
        "Assets/Scenes/Classroom and Movement.unity",
        "Assets/Scenes/Teacher Evaluation.unity"
    };

    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox(
            "Room lights and dust are real editable children, so their result is visible before Play Mode. "
            + "Manual child editing is the default. Root layout/sync options are opt-in and will overwrite matching child values.",
            MessageType.Info);

        DrawDefaultInspector();

        EditorGUILayout.Space();
        if (GUILayout.Button("Apply Opt-In Layout / Sync Now"))
        {
            SceneVisualPolish polish = (SceneVisualPolish)target;
            polish.RefreshVisibleEffects();
            EditorUtility.SetDirty(polish);
            EditorSceneManager.MarkSceneDirty(polish.gameObject.scene);
        }

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Reapply While Playing"))
            {
                ((SceneVisualPolish)target).Apply();
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Balanced Performance keeps every manually placed light, but allows one symmetric left/right pair to cast shadows, "
            + "turns off Bloom, lowers the render scale to 80%, and trims the dust budget.",
            MessageType.Info);
        if (GUILayout.Button("Apply Balanced Performance To This Scene"))
        {
            ApplyBalancedPerformance((SceneVisualPolish)target);
        }

        EditorGUILayout.HelpBox(
            "Bloom, color grading, and vignette still preview in Play Mode. Lights and dust are editable in the scene now.",
            MessageType.None);
    }

    [MenuItem("GameObject/Flee the Faculty/Add Visual Polish Override", false, 10)]
    [MenuItem("Tools/Flee the Faculty/Visual Polish/Add Scene Override")]
    private static void AddSceneOverride()
    {
        SceneVisualPolish existing = Object.FindFirstObjectByType<SceneVisualPolish>(FindObjectsInactive.Include);
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing.gameObject);
            Debug.Log("This scene already has a Visual Polish Rig. It is selected in the Hierarchy.", existing);
            return;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError("Visual Polish Rig prefab is missing at " + PrefabPath + ".");
            return;
        }

        GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (instance == null)
        {
            return;
        }

        instance.name = "Visual Polish Rig - Scene Override";
        Undo.RegisterCreatedObjectUndo(instance, "Add Visual Polish Override");
        Selection.activeGameObject = instance;
        EditorSceneManager.MarkSceneDirty(instance.scene);
    }

    [MenuItem("Tools/Flee the Faculty/Visual Polish/Select Shared Defaults")]
    private static void SelectSharedDefaults()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
    }

    [MenuItem("Tools/Flee the Faculty/Visual Polish/Build Editable Rig In Main Scenes")]
    public static void BuildEditableRigInMainScenes()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        string originalScenePath = activeScene.path;
        if (activeScene.isDirty)
        {
            Debug.LogWarning("Save the current scene before rebuilding the editable visual polish rig.");
            return;
        }

        EnsureVisualPolishFolder();
        Material dustMaterial = CreateOrUpdateDustMaterial();
        if (dustMaterial == null)
        {
            return;
        }

        BuildEditableSharedPrefab(dustMaterial);
        for (int index = 0; index < PrimaryScenePaths.Length; index++)
        {
            InstallRigInScene(PrimaryScenePaths[index]);
        }

        if (!string.IsNullOrEmpty(originalScenePath))
        {
            EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Editable visual polish lights and dust were installed in all three main scenes.");
    }

    [MenuItem("Tools/Flee the Faculty/Visual Polish/Make Main Scene Rigs Manual")]
    public static void MakeMainSceneRigsManual()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        string originalScenePath = activeScene.path;
        if (activeScene.isDirty)
        {
            Debug.LogWarning("Save the current scene before migrating the visual polish rigs to manual mode.");
            return;
        }

        for (int sceneIndex = 0; sceneIndex < PrimaryScenePaths.Length; sceneIndex++)
        {
            Scene scene = EditorSceneManager.OpenScene(PrimaryScenePaths[sceneIndex], OpenSceneMode.Single);
            SceneVisualPolish[] rigs = Object.FindObjectsByType<SceneVisualPolish>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int rigIndex = 0; rigIndex < rigs.Length; rigIndex++)
            {
                if (rigs[rigIndex].gameObject.scene != scene)
                {
                    continue;
                }

                SerializedObject serializedRig = new SerializedObject(rigs[rigIndex]);
                serializedRig.FindProperty("globalLightEnabled").boolValue = false;
                serializedRig.FindProperty("autoLayoutRoomLights").boolValue = false;
                serializedRig.FindProperty("syncRoomLightProperties").boolValue = false;
                serializedRig.FindProperty("syncDustParticleProperties").boolValue = false;
                serializedRig.FindProperty("dustFollowsCamera").boolValue = false;
                serializedRig.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(rigs[rigIndex]);
                EditorUtility.SetDirty(rigs[rigIndex]);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        if (!string.IsNullOrEmpty(originalScenePath))
        {
            EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("All main-scene visual polish rigs now preserve manual child and Global Light edits by default.");
    }

    [MenuItem("Tools/Flee the Faculty/Visual Polish/Apply Balanced Performance To Open Scene")]
    public static void ApplyBalancedPerformanceToOpenScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        SceneVisualPolish[] rigs = Object.FindObjectsByType<SceneVisualPolish>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        int adjustedRigCount = 0;
        for (int index = 0; index < rigs.Length; index++)
        {
            if (rigs[index].gameObject.scene != activeScene)
            {
                continue;
            }

            ApplyBalancedPerformance(rigs[index]);
            adjustedRigCount++;
        }

        if (adjustedRigCount == 0)
        {
            ApplyBalancedRendererDefaults();
            Debug.LogWarning("The open scene has no Visual Polish Rig. Only the shared 80% render scale was applied.");
            return;
        }

        Debug.Log(
            $"Balanced performance was applied to {adjustedRigCount} Visual Polish Rig(s) in {activeScene.name}. "
            + "Save the scene to keep these visible settings.");
    }

    [MenuItem("Tools/Flee the Faculty/Visual Polish/Rebuild Visible Prefab Shadows")]
    public static void RebuildVisiblePrefabShadows()
    {
        EnsureVisualPolishFolder();
        Sprite shadowSprite = CreateOrUpdateShadowSprite();
        if (shadowSprite == null)
        {
            Debug.LogError("Could not create the contact shadow sprite.");
            return;
        }

        InstallShadowOnPrefab(
            StudentPrefabPath,
            shadowSprite,
            new Color(0.12f, 0.09f, 0.13f, 0.42f),
            0.95f,
            0.24f,
            0.015f);
        InstallShadowOnPrefab(
            AraBotPrefabPath,
            shadowSprite,
            new Color(0.1f, 0.08f, 0.12f, 0.44f),
            1.18f,
            0.26f,
            0.015f);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Visible contact shadows were rebuilt on Base Student and AraBOT.");
    }

    [MenuItem("Tools/Flee the Faculty/Visual Polish/Use Stable Character Contact Shadows")]
    public static void UseStableCharacterContactShadows()
    {
        RemoveRealtimeShadowCasters(StudentPrefabPath);
        RemoveRealtimeShadowCasters(AraBotPrefabPath);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            "Students and AraBOT now use stable contact shadows without giant side-light projections.");
    }

    [MenuItem("Tools/Flee the Faculty/Visual Polish/Fix AraBOT Projected Shadow Leak")]
    public static void FixAraBotProjectedShadowLeak()
    {
        RemoveRealtimeShadowCasters(AraBotPrefabPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("AraBOT's projected 2D shadow was removed; its soft contact shadow remains enabled.");
    }

    private static void EnsureVisualPolishFolder()
    {
        if (!AssetDatabase.IsValidFolder(VisualPolishFolder))
        {
            AssetDatabase.CreateFolder("Assets", "Visual Polish");
        }
    }

    private static void ApplyBalancedPerformance(SceneVisualPolish polish)
    {
        if (polish == null)
        {
            return;
        }

        Undo.RecordObject(polish, "Apply Balanced Visual Performance");
        SerializedObject serializedPolish = new SerializedObject(polish);
        serializedPolish.FindProperty("bloomEnabled").boolValue = false;
        serializedPolish.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(polish);
        EditorUtility.SetDirty(polish);

        Transform lightsRoot = polish.transform.Find(SceneVisualPolish.EditableLightsName);
        if (lightsRoot != null)
        {
            Light2D[] lights = lightsRoot.GetComponentsInChildren<Light2D>(true);
            List<Light2D> activeLights = new List<Light2D>();
            for (int index = 0; index < lights.Length; index++)
            {
                Light2D light = lights[index];
                if (light == null || !light.gameObject.activeInHierarchy || !light.enabled)
                {
                    continue;
                }

                activeLights.Add(light);
            }

            FindSymmetricShadowPair(activeLights, out Light2D leftShadowLight, out Light2D rightShadowLight);

            for (int index = 0; index < lights.Length; index++)
            {
                Light2D light = lights[index];
                if (light == null)
                {
                    continue;
                }

                Undo.RecordObject(light, "Apply Balanced Light Shadows");
                bool castsBalancedShadow = light == leftShadowLight || light == rightShadowLight;
                light.shadowsEnabled = castsBalancedShadow;
                if (castsBalancedShadow)
                {
                    light.shadowSoftness = Mathf.Min(light.shadowSoftness, BalancedShadowSoftness);
                }

                PrefabUtility.RecordPrefabInstancePropertyModifications(light);
                EditorUtility.SetDirty(light);
            }
        }

        Transform dustTransform = polish.transform.Find(SceneVisualPolish.EditableDustName);
        ParticleSystem dust = dustTransform != null ? dustTransform.GetComponent<ParticleSystem>() : null;
        if (dust != null)
        {
            Undo.RecordObject(dust, "Apply Balanced Dust Budget");
            ParticleSystem.MainModule main = dust.main;
            main.maxParticles = Mathf.Min(main.maxParticles, BalancedDustParticleLimit);
            PrefabUtility.RecordPrefabInstancePropertyModifications(dust);
            EditorUtility.SetDirty(dust);
        }

        ApplyBalancedRendererDefaults();
        EditorSceneManager.MarkSceneDirty(polish.gameObject.scene);
    }

    private static void FindSymmetricShadowPair(
        List<Light2D> activeLights,
        out Light2D leftShadowLight,
        out Light2D rightShadowLight)
    {
        leftShadowLight = null;
        rightShadowLight = null;
        if (activeLights.Count == 0)
        {
            return;
        }

        if (activeLights.Count == 1)
        {
            leftShadowLight = activeLights[0];
            return;
        }

        float minimumX = float.PositiveInfinity;
        float maximumX = float.NegativeInfinity;
        for (int index = 0; index < activeLights.Count; index++)
        {
            float x = activeLights[index].transform.position.x;
            minimumX = Mathf.Min(minimumX, x);
            maximumX = Mathf.Max(maximumX, x);
        }

        float centerX = (minimumX + maximumX) * 0.5f;
        float bestScore = float.PositiveInfinity;
        for (int firstIndex = 0; firstIndex < activeLights.Count; firstIndex++)
        {
            Light2D first = activeLights[firstIndex];
            for (int secondIndex = firstIndex + 1; secondIndex < activeLights.Count; secondIndex++)
            {
                Light2D second = activeLights[secondIndex];
                Light2D left = first.transform.position.x <= second.transform.position.x ? first : second;
                Light2D right = left == first ? second : first;
                if (left.transform.position.x > centerX || right.transform.position.x < centerX)
                {
                    continue;
                }

                float verticalMismatch = Mathf.Abs(left.transform.position.y - right.transform.position.y);
                float leftDistance = centerX - left.transform.position.x;
                float rightDistance = right.transform.position.x - centerX;
                float horizontalMismatch = Mathf.Abs(leftDistance - rightDistance);
                float score = verticalMismatch + (horizontalMismatch * 0.1f);
                if (score >= bestScore)
                {
                    continue;
                }

                bestScore = score;
                leftShadowLight = left;
                rightShadowLight = right;
            }
        }

        if (leftShadowLight == null)
        {
            leftShadowLight = activeLights[0];
            rightShadowLight = activeLights[1];
        }
    }

    private static void ApplyBalancedRendererDefaults()
    {
        UniversalRenderPipelineAsset pipeline =
            AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RenderPipelineAssetPath);
        if (pipeline == null)
        {
            Debug.LogWarning("Could not find the URP asset at " + RenderPipelineAssetPath + ".");
            return;
        }

        pipeline.renderScale = BalancedRenderScale;
        EditorUtility.SetDirty(pipeline);
        AssetDatabase.SaveAssetIfDirty(pipeline);
    }

    private static Sprite CreateOrUpdateShadowSprite()
    {
        const int textureWidth = 128;
        const int textureHeight = 64;
        Texture2D texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[textureWidth * textureHeight];

        for (int y = 0; y < textureHeight; y++)
        {
            float normalizedY = ((y + 0.5f) / textureHeight * 2f) - 1f;
            for (int x = 0; x < textureWidth; x++)
            {
                float normalizedX = ((x + 0.5f) / textureWidth * 2f) - 1f;
                float distance = (normalizedX * normalizedX) + (normalizedY * normalizedY);
                float alpha = Mathf.Pow(Mathf.Clamp01(1f - distance), 2.4f);
                pixels[(y * textureWidth) + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, false);
        File.WriteAllBytes(ShadowTexturePath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(ShadowTexturePath, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = AssetImporter.GetAtPath(ShadowTexturePath) as TextureImporter;
        if (importer == null)
        {
            return null;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 128f;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(ShadowTexturePath);
    }

    private static Material CreateOrUpdateDustMaterial()
    {
        Shader shader = Shader.Find("Flee the Faculty/Soft Particle");
        if (shader == null)
        {
            Debug.LogError("The visual polish dust shader could not be found.");
            return null;
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(DustMaterialPath);
        if (material == null)
        {
            material = new Material(shader)
            {
                name = "Ambient Dust"
            };
            AssetDatabase.CreateAsset(material, DustMaterialPath);
        }
        else
        {
            material.shader = shader;
            EditorUtility.SetDirty(material);
        }

        return material;
    }

    private static void BuildEditableSharedPrefab(Material dustMaterial)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            SceneVisualPolish polish = root.GetComponent<SceneVisualPolish>();
            if (polish == null)
            {
                polish = root.AddComponent<SceneVisualPolish>();
            }

            Transform lightsRoot = FindOrCreateChild(root.transform, SceneVisualPolish.EditableLightsName);
            for (int index = 0; index < MaximumRoomLights; index++)
            {
                Transform lightTransform = FindOrCreateChild(lightsRoot, "Room Light " + (index + 1));
                if (lightTransform.GetComponent<Light2D>() == null)
                {
                    lightTransform.gameObject.AddComponent<Light2D>();
                }
            }

            Transform dustTransform = FindOrCreateChild(root.transform, SceneVisualPolish.EditableDustName);
            ParticleSystem dust = dustTransform.GetComponent<ParticleSystem>();
            if (dust == null)
            {
                dust = dustTransform.gameObject.AddComponent<ParticleSystem>();
            }

            ParticleSystemRenderer renderer = dust.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = dustMaterial;
            polish.RefreshVisibleEffects();
            EditorUtility.SetDirty(polish);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Transform FindOrCreateChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null)
        {
            return child;
        }

        GameObject childObject = new GameObject(childName);
        child = childObject.transform;
        child.SetParent(parent, false);
        return child;
    }

    private static void InstallRigInScene(string scenePath)
    {
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        SceneVisualPolish[] rigs = Object.FindObjectsByType<SceneVisualPolish>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        SceneVisualPolish sceneRig = null;
        for (int index = 0; index < rigs.Length; index++)
        {
            if (rigs[index].gameObject.scene == scene)
            {
                sceneRig = rigs[index];
                break;
            }
        }

        if (sceneRig == null)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (instance == null)
            {
                return;
            }

            instance.name = "Visual Polish Rig";
            sceneRig = instance.GetComponent<SceneVisualPolish>();
        }

        sceneRig.RefreshVisibleEffects();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void InstallShadowOnPrefab(
        string prefabPath,
        Sprite shadowSprite,
        Color shadowColor,
        float width,
        float height,
        float yOffset)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Transform shadowTransform = root.transform.Find(SoftContactShadow.ShadowObjectName);
            if (shadowTransform == null)
            {
                GameObject shadowObject = new GameObject(SoftContactShadow.ShadowObjectName);
                shadowTransform = shadowObject.transform;
                shadowTransform.SetParent(root.transform, false);
            }

            SpriteRenderer renderer = shadowTransform.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                renderer = shadowTransform.gameObject.AddComponent<SpriteRenderer>();
            }

            renderer.sprite = shadowSprite;
            renderer.color = shadowColor;
            renderer.sortingLayerName = "Characters";
            renderer.sortingOrder = -10;

            SoftContactShadow shadow = root.GetComponent<SoftContactShadow>();
            if (shadow == null)
            {
                shadow = root.AddComponent<SoftContactShadow>();
            }

            SerializedObject serializedShadow = new SerializedObject(shadow);
            serializedShadow.FindProperty("color").colorValue = shadowColor;
            serializedShadow.FindProperty("width").floatValue = width;
            serializedShadow.FindProperty("height").floatValue = height;
            serializedShadow.FindProperty("yOffset").floatValue = yOffset;
            serializedShadow.ApplyModifiedPropertiesWithoutUndo();
            shadow.Refresh(true);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void InstallSpriteShadowCasters(string prefabPath, params string[] spritePaths)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            ShadowCaster2D[] existingCasters = root.GetComponentsInChildren<ShadowCaster2D>(true);
            for (int index = 0; index < existingCasters.Length; index++)
            {
                Object.DestroyImmediate(existingCasters[index]);
            }

            Transform oldManualCaster = root.transform.Find("Shadow Caster");
            if (oldManualCaster != null)
            {
                Object.DestroyImmediate(oldManualCaster.gameObject);
            }

            CompositeShadowCaster2D composite = root.GetComponent<CompositeShadowCaster2D>();
            if (composite == null)
            {
                composite = root.AddComponent<CompositeShadowCaster2D>();
            }

            for (int index = 0; index < spritePaths.Length; index++)
            {
                Transform spriteTransform = root.transform.Find(spritePaths[index]);
                if (spriteTransform == null)
                {
                    Debug.LogWarning($"Could not find '{spritePaths[index]}' in {prefabPath}.", root);
                    continue;
                }

                SpriteRenderer spriteRenderer = spriteTransform.GetComponent<SpriteRenderer>();
                if (spriteRenderer == null)
                {
                    Debug.LogWarning($"'{spritePaths[index]}' has no SpriteRenderer in {prefabPath}.", root);
                    continue;
                }

                ShadowCaster2D caster = spriteTransform.gameObject.AddComponent<ShadowCaster2D>();
                caster.castingOption = ShadowCaster2D.ShadowCastingOptions.CastShadow;
                caster.alphaCutoff = 0.1f;
                caster.trimEdge = 0.015f;
                EditorUtility.SetDirty(caster);
            }

            EditorUtility.SetDirty(composite);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void RemoveRealtimeShadowCasters(string prefabPath)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            ShadowCaster2D[] casters = root.GetComponentsInChildren<ShadowCaster2D>(true);
            for (int index = 0; index < casters.Length; index++)
            {
                Object.DestroyImmediate(casters[index]);
            }

            CompositeShadowCaster2D[] composites = root.GetComponentsInChildren<CompositeShadowCaster2D>(true);
            for (int index = 0; index < composites.Length; index++)
            {
                Object.DestroyImmediate(composites[index]);
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}

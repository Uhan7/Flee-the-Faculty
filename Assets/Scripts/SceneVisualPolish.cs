using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[AddComponentMenu("Flee the Faculty/Scene Visual Polish")]
[DisallowMultipleComponent]
public sealed class SceneVisualPolish : MonoBehaviour
{
    public const string EditableDustName = "Ambient Dust (Editable)";
    public const string EditableLightsName = "Room Accent Lights (Editable)";
    private const float CharacterShadowScanInterval = 2f;

    [Header("Master")]
    [Tooltip("Turns every effect in this rig on or off without losing the settings below.")]
    [SerializeField] private bool visualPolishEnabled = true;

    [Header("Color Grading")]
    [Tooltip("Enables the lightweight URP post-processing pass on the main camera.")]
    [SerializeField] private bool colorGradingEnabled = true;
    [SerializeField, Range(-2f, 2f)] private float postExposure = 0.05f;
    [SerializeField, Range(-100f, 100f)] private float contrast = 6f;
    [SerializeField, Range(-100f, 100f)] private float saturation = 5f;
    [SerializeField, Range(-100f, 100f)] private float temperature = 3f;
    [SerializeField, Range(-100f, 100f)] private float tint;
    [SerializeField] private Color colorFilter = new Color(1f, 0.995f, 0.98f, 1f);

    [Header("Bloom")]
    [SerializeField] private bool bloomEnabled;
    [SerializeField, Range(0f, 2f)] private float bloomIntensity = 0.12f;
    [SerializeField, Min(0f)] private float bloomThreshold = 0.85f;
    [SerializeField, Range(0f, 1f)] private float bloomScatter = 0.62f;
    [SerializeField] private Color bloomTint = new Color(1f, 0.93f, 0.78f, 1f);

    [Header("Vignette")]
    [SerializeField] private bool vignetteEnabled = true;
    [SerializeField, Range(0f, 0.5f)] private float vignetteIntensity = 0.06f;
    [SerializeField, Range(0.01f, 1f)] private float vignetteSmoothness = 0.55f;
    [SerializeField] private Color vignetteColor = new Color(0.12f, 0.08f, 0.16f, 1f);

    [Header("2D Global Light")]
    [Tooltip("OPT-IN: Overwrites the scene's existing Global Light color and intensity during Play Mode. Leave disabled to preserve manual scene lighting.")]
    [SerializeField] private bool globalLightEnabled;
    [SerializeField] private Color globalLightColor = new Color(1f, 0.97f, 0.91f, 1f);
    [SerializeField, Range(0f, 2f)] private float globalLightIntensity = 0.98f;
    [Tooltip("A tiny brightness drift can keep the room from feeling completely static.")]
    [SerializeField] private bool animateGlobalLight;
    [SerializeField, Range(0f, 0.2f)] private float lightPulseAmount = 0.015f;
    [SerializeField, Range(0.01f, 2f)] private float lightPulseSpeed = 0.16f;

    [Header("Room Accent Lights")]
    [Tooltip("Controls the real editable Point Light 2D children stored under this rig.")]
    [SerializeField] private bool roomLightsEnabled = true;
    [SerializeField, Range(1, 8)] private int roomLightCount = 3;
    [Tooltip("OPT-IN: Repositions the editable children from Center and Spacing. Leave disabled to preserve manual positions.")]
    [SerializeField] private bool autoLayoutRoomLights;
    [Tooltip("OPT-IN: Overwrites child color, intensity, radius, and falloff from the root. Leave disabled to preserve manual Light 2D edits.")]
    [SerializeField] private bool syncRoomLightProperties;
    [SerializeField] private Vector2 roomLightCenter = new Vector2(0f, 1.8f);
    [SerializeField, Min(0f)] private float roomLightSpacing = 5.2f;
    [SerializeField] private Color roomLightColor = new Color(1f, 0.78f, 0.46f, 1f);
    [SerializeField, Range(0f, 2f)] private float roomLightIntensity = 0.075f;
    [SerializeField, Min(0f)] private float roomLightInnerRadius = 0.4f;
    [SerializeField, Min(0.1f)] private float roomLightOuterRadius = 3.2f;
    [SerializeField, Range(0f, 1f)] private float roomLightFalloff = 0.82f;

    [Header("Ambient Dust")]
    [SerializeField] private bool ambientDustEnabled = true;
    [Tooltip("OPT-IN: Overwrites the editable Particle System from the root dust settings. Leave disabled to preserve manual module edits.")]
    [SerializeField] private bool syncDustParticleProperties;
    [Tooltip("OPT-IN: Moves the dust volume with the gameplay camera. Leave disabled to preserve its manual position.")]
    [SerializeField] private bool dustFollowsCamera;
    [SerializeField, Range(0, 200)] private int dustMaxParticles = 55;
    [SerializeField, Range(0f, 20f)] private float dustEmissionRate = 2.6f;
    [SerializeField] private Color dustColor = new Color(1f, 0.9f, 0.65f, 0.24f);
    [SerializeField, Min(0.5f)] private float dustLifetime = 10f;
    [SerializeField] private Vector2 dustSizeRange = new Vector2(0.025f, 0.075f);
    [SerializeField] private Vector2 dustDrift = new Vector2(0.025f, 0.045f);
    [SerializeField, Min(0f)] private float dustAreaPadding = 1f;
    [SerializeField] private string dustSortingLayer = "Foreground FX";
    [SerializeField] private int dustSortingOrder;

    [Header("Character Grounding")]
    [Tooltip("Adds a soft generated contact shadow to characters that use Character Procedural Animation.")]
    [SerializeField] private bool characterShadowsEnabled = true;
    [SerializeField] private Color characterShadowColor = new Color(0.12f, 0.09f, 0.13f, 0.42f);
    [SerializeField, Range(0.2f, 2f)] private float characterShadowWidth = 0.95f;
    [SerializeField, Range(0.05f, 1f)] private float characterShadowHeight = 0.24f;
    [SerializeField] private float characterShadowYOffset = 0.015f;

    private Camera targetCamera;
    private UniversalAdditionalCameraData cameraData;
    private bool originalPostProcessing;
    private AntialiasingMode originalAntialiasing;
    private Volume generatedVolume;
    private VolumeProfile generatedVolumeProfile;
    private Light2D globalLight;
    private Color originalLightColor;
    private float originalLightIntensity;
    private Transform roomLightsRoot;
    private ParticleSystem dustParticles;
    private readonly List<SoftContactShadow> generatedShadows = new List<SoftContactShadow>();
    private Coroutine reapplyRoutine;
    private Coroutine characterShadowScanRoutine;
    private bool hasApplied;

    private void OnEnable()
    {
        Apply();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying
            && (autoLayoutRoomLights || syncRoomLightProperties || syncDustParticleProperties))
        {
            RefreshVisibleEffects();
        }
    }

    private void Update()
    {
        if (!visualPolishEnabled || !globalLightEnabled || !animateGlobalLight || globalLight == null)
        {
            return;
        }

        float pulse = Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * lightPulseSpeed) * lightPulseAmount;
        globalLight.intensity = Mathf.Max(0f, globalLightIntensity + pulse);
    }

    private void LateUpdate()
    {
        if (!dustFollowsCamera || dustParticles == null || targetCamera == null)
        {
            return;
        }

        Vector3 cameraPosition = targetCamera.transform.position;
        float horizontalSway = Mathf.Sin(Time.unscaledTime * 0.19f) * dustDrift.x;
        float verticalSway = Mathf.Cos(Time.unscaledTime * 0.13f) * dustDrift.y;
        dustParticles.transform.position = new Vector3(
            cameraPosition.x + horizontalSway,
            cameraPosition.y + verticalSway,
            0f);
    }

    private void OnDisable()
    {
        if (reapplyRoutine != null)
        {
            StopCoroutine(reapplyRoutine);
            reapplyRoutine = null;
        }

        RestoreChangedSceneObjects();
        DestroyGeneratedObjects();
    }

    [ContextMenu("Apply Visual Polish Now")]
    public void Apply()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (hasApplied)
        {
            if (reapplyRoutine != null)
            {
                StopCoroutine(reapplyRoutine);
            }

            reapplyRoutine = StartCoroutine(ReapplyAfterCleanup());
            return;
        }

        ApplyFresh();
    }

    private IEnumerator ReapplyAfterCleanup()
    {
        RestoreChangedSceneObjects();
        DestroyGeneratedObjects();
        yield return null;
        reapplyRoutine = null;

        if (isActiveAndEnabled)
        {
            ApplyFresh();
        }
    }

    private void ApplyFresh()
    {
        if (!visualPolishEnabled)
        {
            return;
        }

        targetCamera = Camera.main;
        ApplyPostProcessing();
        ApplyGlobalLight();
        RefreshVisibleEffects();
        CreateCharacterShadows();
        StartCharacterShadowScanner();
        hasApplied = true;
    }

    private void ApplyPostProcessing()
    {
        if (targetCamera == null || (!colorGradingEnabled && !bloomEnabled && !vignetteEnabled))
        {
            return;
        }

        cameraData = targetCamera.GetUniversalAdditionalCameraData();
        originalPostProcessing = cameraData.renderPostProcessing;
        originalAntialiasing = cameraData.antialiasing;
        cameraData.renderPostProcessing = true;
        cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

        generatedVolume = gameObject.AddComponent<Volume>();
        generatedVolume.isGlobal = true;
        generatedVolume.priority = 80f;
        generatedVolume.weight = 1f;
        generatedVolumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        generatedVolumeProfile.name = "Runtime Visual Polish Profile";
        generatedVolume.sharedProfile = generatedVolumeProfile;

        if (colorGradingEnabled)
        {
            ColorAdjustments color = generatedVolumeProfile.Add<ColorAdjustments>(true);
            color.postExposure.Override(postExposure);
            color.contrast.Override(contrast);
            color.saturation.Override(saturation);
            color.colorFilter.Override(colorFilter);

            WhiteBalance whiteBalance = generatedVolumeProfile.Add<WhiteBalance>(true);
            whiteBalance.temperature.Override(temperature);
            whiteBalance.tint.Override(tint);
        }

        if (bloomEnabled)
        {
            Bloom bloom = generatedVolumeProfile.Add<Bloom>(true);
            bloom.intensity.Override(bloomIntensity);
            bloom.threshold.Override(bloomThreshold);
            bloom.scatter.Override(bloomScatter);
            bloom.tint.Override(bloomTint);
            bloom.highQualityFiltering.Override(false);
        }

        if (vignetteEnabled)
        {
            Vignette vignette = generatedVolumeProfile.Add<Vignette>(true);
            vignette.intensity.Override(vignetteIntensity);
            vignette.smoothness.Override(vignetteSmoothness);
            vignette.color.Override(vignetteColor);
            vignette.rounded.Override(false);
        }
    }

    private void ApplyGlobalLight()
    {
        if (!globalLightEnabled)
        {
            return;
        }

        Light2D[] lights = FindObjectsByType<Light2D>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int index = 0; index < lights.Length; index++)
        {
            if (lights[index].lightType == Light2D.LightType.Global)
            {
                globalLight = lights[index];
                break;
            }
        }

        if (globalLight == null)
        {
            return;
        }

        originalLightColor = globalLight.color;
        originalLightIntensity = globalLight.intensity;
        globalLight.color = globalLightColor;
        globalLight.intensity = globalLightIntensity;
    }

    public void RefreshVisibleEffects()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        ConfigureRoomLights();
        ConfigureAmbientDust();
    }

    private void ConfigureRoomLights()
    {
        if (roomLightsRoot == null)
        {
            Transform existing = transform.Find(EditableLightsName);
            if (existing == null)
            {
                return;
            }

            roomLightsRoot = existing;
        }

        Light2D[] lights = roomLightsRoot.GetComponentsInChildren<Light2D>(true);
        if (lights.Length == 0)
        {
            return;
        }

        if (!autoLayoutRoomLights && !syncRoomLightProperties)
        {
            return;
        }

        float totalWidth = roomLightSpacing * (roomLightCount - 1);
        float firstX = roomLightCenter.x - (totalWidth * 0.5f);
        float outerRadius = Mathf.Max(0.1f, roomLightOuterRadius);
        float innerRadius = Mathf.Clamp(roomLightInnerRadius, 0f, outerRadius);

        for (int index = 0; index < lights.Length; index++)
        {
            Light2D light = lights[index];
            bool lightEnabled = roomLightsEnabled && index < roomLightCount;
            light.gameObject.SetActive(lightEnabled);
            if (autoLayoutRoomLights)
            {
                light.transform.localPosition = new Vector3(
                    firstX + (roomLightSpacing * index),
                    roomLightCenter.y,
                    0f);
            }

            if (!syncRoomLightProperties)
            {
                continue;
            }

            light.lightType = Light2D.LightType.Point;
            light.blendStyleIndex = 1;
            light.overlapOperation = Light2D.OverlapOperation.Additive;
            light.color = roomLightColor;
            light.intensity = roomLightIntensity;
            light.pointLightInnerRadius = innerRadius;
            light.pointLightOuterRadius = outerRadius;
            light.pointLightInnerAngle = 360f;
            light.pointLightOuterAngle = 360f;
            light.falloffIntensity = roomLightFalloff;
            light.shadowsEnabled = false;
        }
    }

    private void ConfigureAmbientDust()
    {
        if (dustParticles == null)
        {
            Transform existing = transform.Find(EditableDustName);
            if (existing != null)
            {
                dustParticles = existing.GetComponent<ParticleSystem>();
            }
        }

        if (dustParticles == null)
        {
            return;
        }

        if (!syncDustParticleProperties)
        {
            return;
        }

        bool dustEnabled = ambientDustEnabled && dustMaxParticles > 0 && dustEmissionRate > 0f;
        dustParticles.gameObject.SetActive(dustEnabled);
        if (!dustEnabled)
        {
            return;
        }

        ParticleSystemRenderer dustRenderer = dustParticles.GetComponent<ParticleSystemRenderer>();

        ParticleSystem.MainModule main = dustParticles.main;
        main.loop = true;
        main.prewarm = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = dustMaxParticles;
        main.startLifetime = new ParticleSystem.MinMaxCurve(dustLifetime * 0.7f, dustLifetime * 1.25f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(
            Mathf.Min(dustSizeRange.x, dustSizeRange.y),
            Mathf.Max(dustSizeRange.x, dustSizeRange.y));
        main.startColor = dustColor;

        ParticleSystem.EmissionModule emission = dustParticles.emission;
        emission.rateOverTime = dustEmissionRate;

        Camera sizingCamera = targetCamera != null ? targetCamera : Camera.main;
        float halfHeight = sizingCamera != null && sizingCamera.orthographic
            ? sizingCamera.orthographicSize + dustAreaPadding
            : 6f + dustAreaPadding;
        float aspect = sizingCamera != null && sizingCamera.aspect > 0f
            ? sizingCamera.aspect
            : (16f / 9f);
        ParticleSystem.ShapeModule shape = dustParticles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(halfHeight * aspect * 2f, halfHeight * 2f, 0.1f);

        ParticleSystem.NoiseModule noise = dustParticles.noise;
        noise.enabled = true;
        noise.quality = ParticleSystemNoiseQuality.Low;
        noise.strength = 0.045f;
        noise.frequency = 0.18f;
        noise.scrollSpeed = 0.05f;
        noise.damping = true;

        dustRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        dustRenderer.sortingLayerName = dustSortingLayer;
        dustRenderer.sortingOrder = dustSortingOrder;
        dustRenderer.shadowCastingMode = ShadowCastingMode.Off;
        dustRenderer.receiveShadows = false;

        if (Application.isPlaying && !dustParticles.isPlaying)
        {
            dustParticles.Play();
        }
    }

    private void CreateCharacterShadows()
    {
        if (!characterShadowsEnabled)
        {
            return;
        }

        CharacterProceduralAnimation[] characters = FindObjectsByType<CharacterProceduralAnimation>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int index = 0; index < characters.Length; index++)
        {
            CharacterProceduralAnimation character = characters[index];
            if (character == null || !character.gameObject.scene.IsValid())
            {
                continue;
            }

            SoftContactShadow shadow = character.GetComponent<SoftContactShadow>();
            if (shadow == null)
            {
                shadow = character.gameObject.AddComponent<SoftContactShadow>();
                generatedShadows.Add(shadow);
                shadow.Configure(
                    characterShadowColor,
                    characterShadowWidth,
                    characterShadowHeight,
                    characterShadowYOffset);
            }
        }
    }

    private void StartCharacterShadowScanner()
    {
        if (!characterShadowsEnabled || characterShadowScanRoutine != null)
        {
            return;
        }

        characterShadowScanRoutine = StartCoroutine(ScanForSpawnedCharacters());
    }

    private IEnumerator ScanForSpawnedCharacters()
    {
        WaitForSeconds wait = new WaitForSeconds(CharacterShadowScanInterval);
        while (true)
        {
            yield return wait;
            CreateCharacterShadows();
        }
    }

    private void RestoreChangedSceneObjects()
    {
        if (!hasApplied)
        {
            return;
        }

        if (cameraData != null)
        {
            cameraData.renderPostProcessing = originalPostProcessing;
            cameraData.antialiasing = originalAntialiasing;
        }

        if (globalLight != null)
        {
            globalLight.color = originalLightColor;
            globalLight.intensity = originalLightIntensity;
        }

        hasApplied = false;
        cameraData = null;
        globalLight = null;
    }

    private void DestroyGeneratedObjects()
    {
        if (characterShadowScanRoutine != null)
        {
            StopCoroutine(characterShadowScanRoutine);
            characterShadowScanRoutine = null;
        }

        for (int index = generatedShadows.Count - 1; index >= 0; index--)
        {
            if (generatedShadows[index] != null)
            {
                Destroy(generatedShadows[index]);
            }
        }

        generatedShadows.Clear();

        roomLightsRoot = null;
        dustParticles = null;

        if (generatedVolume != null)
        {
            Destroy(generatedVolume);
            generatedVolume = null;
        }

        if (generatedVolumeProfile != null)
        {
            Destroy(generatedVolumeProfile);
            generatedVolumeProfile = null;
        }
    }
}

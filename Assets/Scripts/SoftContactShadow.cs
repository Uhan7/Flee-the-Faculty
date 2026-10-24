using UnityEngine;

[ExecuteAlways]
[AddComponentMenu("Flee the Faculty/Soft Contact Shadow")]
[DisallowMultipleComponent]
public sealed class SoftContactShadow : MonoBehaviour
{
    public const string ShadowObjectName = "Contact Shadow";

    [SerializeField] private Color color = new Color(0.12f, 0.09f, 0.13f, 0.3f);
    [SerializeField, Range(0.2f, 2f)] private float width = 0.82f;
    [SerializeField, Range(0.05f, 1f)] private float height = 0.22f;
    [SerializeField] private float yOffset = 0.025f;

    private static Sprite sharedShadowSprite;
    private SpriteRenderer shadowRenderer;
    private Vector3 stableLocalAnchor;
    private float stableWorldWidth;
    private int stableSortingLayerId;
    private int stableSortingOrder;
    private bool anchorCaptured;

    private void OnEnable()
    {
        EnsureRenderer();
        CaptureStableAnchor();
        UpdateShadow();
    }

    private void OnValidate()
    {
        anchorCaptured = false;
        EnsureRenderer();
        CaptureStableAnchor();
        UpdateShadow();
    }

    private void LateUpdate()
    {
        UpdateShadow();
    }

    private void OnDestroy()
    {
        if (shadowRenderer != null)
        {
            if (Application.isPlaying)
            {
                Destroy(shadowRenderer.gameObject);
            }
            else
            {
                DestroyImmediate(shadowRenderer.gameObject);
            }
        }
    }

    public void Configure(Color shadowColor, float shadowWidth, float shadowHeight, float shadowYOffset)
    {
        color = shadowColor;
        width = shadowWidth;
        height = shadowHeight;
        yOffset = shadowYOffset;
        EnsureRenderer();
        CaptureStableAnchor();
        UpdateShadow();
    }

    public void Refresh(bool recaptureAnchor = false)
    {
        if (recaptureAnchor)
        {
            anchorCaptured = false;
        }

        EnsureRenderer();
        CaptureStableAnchor();
        UpdateShadow();
    }

    private void EnsureRenderer()
    {
        if (shadowRenderer != null)
        {
            return;
        }

        Transform existing = transform.Find(ShadowObjectName);
        GameObject shadowObject;
        if (existing != null)
        {
            shadowObject = existing.gameObject;
        }
        else
        {
            shadowObject = new GameObject(ShadowObjectName);
            shadowObject.transform.SetParent(transform, false);
        }

        shadowRenderer = shadowObject.GetComponent<SpriteRenderer>();
        if (shadowRenderer == null)
        {
            shadowRenderer = shadowObject.AddComponent<SpriteRenderer>();
        }

        if (shadowRenderer.sprite == null)
        {
            shadowRenderer.sprite = GetOrCreateShadowSprite();
        }
    }

    private void UpdateShadow()
    {
        if (shadowRenderer == null)
        {
            return;
        }

        if (!anchorCaptured)
        {
            CaptureStableAnchor();
        }

        if (!anchorCaptured)
        {
            shadowRenderer.enabled = false;
            return;
        }

        shadowRenderer.enabled = true;
        shadowRenderer.color = color;
        shadowRenderer.sortingLayerID = stableSortingLayerId;
        shadowRenderer.sortingOrder = stableSortingOrder;

        Transform shadowTransform = shadowRenderer.transform;
        shadowTransform.position = transform.TransformPoint(stableLocalAnchor);
        shadowTransform.rotation = Quaternion.identity;

        Vector3 parentScale = transform.lossyScale;
        float parentScaleX = Mathf.Max(0.001f, Mathf.Abs(parentScale.x));
        float parentScaleY = Mathf.Max(0.001f, Mathf.Abs(parentScale.y));
        shadowTransform.localScale = new Vector3(
            stableWorldWidth / parentScaleX,
            (height * 2f) / parentScaleY,
            1f);
    }

    private void CaptureStableAnchor()
    {
        if (anchorCaptured)
        {
            return;
        }

        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
        Bounds characterBounds = default;
        SpriteRenderer referenceRenderer = null;
        int minimumOrder = int.MaxValue;
        bool hasBounds = false;

        for (int index = 0; index < renderers.Length; index++)
        {
            SpriteRenderer current = renderers[index];
            if (current == null || current == shadowRenderer || !current.enabled || current.sprite == null)
            {
                continue;
            }

            if (current.sortingLayerName != "Characters")
            {
                continue;
            }

            if (!hasBounds)
            {
                characterBounds = current.bounds;
                referenceRenderer = current;
                hasBounds = true;
            }
            else
            {
                characterBounds.Encapsulate(current.bounds);
            }

            if (current.sortingOrder < minimumOrder)
            {
                minimumOrder = current.sortingOrder;
                referenceRenderer = current;
            }
        }

        if (!hasBounds || referenceRenderer == null)
        {
            return;
        }

        Vector3 worldAnchor = new Vector3(
            characterBounds.center.x,
            characterBounds.min.y + yOffset,
            transform.position.z);
        stableLocalAnchor = transform.InverseTransformPoint(worldAnchor);
        stableWorldWidth = Mathf.Max(width, characterBounds.size.x * 0.58f);
        stableSortingLayerId = referenceRenderer.sortingLayerID;
        stableSortingOrder = minimumOrder - 10;
        anchorCaptured = true;
    }

    private static Sprite GetOrCreateShadowSprite()
    {
        if (sharedShadowSprite != null)
        {
            return sharedShadowSprite;
        }

        const int textureWidth = 64;
        const int textureHeight = 32;
        Texture2D texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false)
        {
            name = "Runtime Soft Contact Shadow",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };

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
        texture.Apply(false, true);
        sharedShadowSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, textureWidth, textureHeight),
            new Vector2(0.5f, 0.5f),
            textureWidth);
        sharedShadowSprite.name = "Runtime Soft Contact Shadow";
        sharedShadowSprite.hideFlags = HideFlags.DontSave;
        return sharedShadowSprite;
    }
}

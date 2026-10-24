using UnityEngine;
using UnityEngine.UI;

/// <summary>Temporary circular report stamp until the illustrated asset is available.</summary>
public sealed class ReportRatingBadge : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper vertices)
    {
        vertices.Clear();
        const int segments = 64;
        Rect rect = rectTransform.rect;
        Vector2 center = rect.center;
        float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
        vertices.AddVert(center, color, Vector2.zero);
        for (int index = 0; index <= segments; index++)
        {
            float angle = index * Mathf.PI * 2f / segments;
            Vector2 position = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            vertices.AddVert(position, color, Vector2.zero);
            if (index > 0)
            {
                vertices.AddTriangle(0, index, index + 1);
            }
        }
    }
}

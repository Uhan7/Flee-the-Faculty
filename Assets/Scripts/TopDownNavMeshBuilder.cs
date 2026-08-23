using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class TopDownNavMeshBuilder : MonoBehaviour
{
    [SerializeField] private float groundThickness = 0.25f;
    [SerializeField] private float obstacleHeight = 2f;
    [SerializeField] private float obstaclePadding = 0.2f;

    private NavMeshData navMeshData;
    private NavMeshDataInstance navMeshInstance;
    private SpriteRenderer walkableAreaRenderer;

    private void Awake()
    {
        walkableAreaRenderer = GetComponent<SpriteRenderer>();
        BuildNavMesh();
    }

    private void OnDisable()
    {
        RemoveNavMesh();
    }

    public void BuildNavMesh()
    {
        if (walkableAreaRenderer == null)
        {
            walkableAreaRenderer = GetComponent<SpriteRenderer>();
        }

        if (walkableAreaRenderer == null || NavMesh.GetSettingsCount() == 0)
        {
            return;
        }

        Bounds walkableBounds = walkableAreaRenderer.bounds;
        int notWalkableArea = NavMesh.GetAreaFromName("Not Walkable");
        List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>
        {
            CreateGroundSource(walkableBounds)
        };

        BoxCollider2D[] colliders = Object.FindObjectsByType<BoxCollider2D>(FindObjectsSortMode.None);
        for (int index = 0; index < colliders.Length; index++)
        {
            BoxCollider2D collider = colliders[index];

            if (!IsObstacle(collider))
            {
                continue;
            }

            sources.Add(CreateObstacleSource(collider.bounds, notWalkableArea));
        }

        Bounds buildBounds = CreateBuildBounds(walkableBounds);
        NavMeshBuildSettings settings = NavMesh.GetSettingsByIndex(0);
        NavMeshData builtNavMesh = NavMeshBuilder.BuildNavMeshData(settings, sources, buildBounds, Vector3.zero, Quaternion.identity);

        if (builtNavMesh == null)
        {
            return;
        }

        RemoveNavMesh();
        navMeshData = builtNavMesh;
        navMeshInstance = NavMesh.AddNavMeshData(navMeshData);
    }

    private bool IsObstacle(BoxCollider2D collider)
    {
        if (collider == null || !collider.enabled || collider.isTrigger || collider.gameObject == gameObject)
        {
            return false;
        }

        if (collider.attachedRigidbody != null)
        {
            return false;
        }

        return true;
    }

    private NavMeshBuildSource CreateGroundSource(Bounds walkableBounds)
    {
        Vector3 worldCenter = new Vector3(walkableBounds.center.x, -groundThickness * 0.5f, walkableBounds.center.y);
        Vector3 size = new Vector3(walkableBounds.size.x, groundThickness, walkableBounds.size.y);

        return new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Box,
            transform = Matrix4x4.TRS(worldCenter, Quaternion.identity, Vector3.one),
            size = size,
            area = 0
        };
    }

    private NavMeshBuildSource CreateObstacleSource(Bounds obstacleBounds, int notWalkableArea)
    {
        Vector3 worldCenter = new Vector3(obstacleBounds.center.x, 0f, obstacleBounds.center.y);
        Vector3 size = new Vector3(
            obstacleBounds.size.x + obstaclePadding * 2f,
            obstacleHeight,
            obstacleBounds.size.y + obstaclePadding * 2f);

        return new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.ModifierBox,
            transform = Matrix4x4.TRS(worldCenter, Quaternion.identity, Vector3.one),
            size = size,
            area = notWalkableArea
        };
    }

    private Bounds CreateBuildBounds(Bounds walkableBounds)
    {
        Vector3 center = new Vector3(walkableBounds.center.x, obstacleHeight * 0.5f, walkableBounds.center.y);
        Vector3 size = new Vector3(
            walkableBounds.size.x + obstaclePadding * 4f,
            obstacleHeight + groundThickness,
            walkableBounds.size.y + obstaclePadding * 4f);

        return new Bounds(center, size);
    }

    private void RemoveNavMesh()
    {
        if (navMeshInstance.valid)
        {
            navMeshInstance.Remove();
        }
    }
}

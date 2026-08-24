using System;
using System.Collections;
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
    [SerializeField, Min(0.05f)] private float rebuildIntervalSeconds = 0.35f;
    [SerializeField, Min(0f)] private float obstacleChangeThreshold = 0.02f;

    private readonly List<ObstacleSnapshot> latestObstacleSnapshots = new List<ObstacleSnapshot>();
    private readonly List<ObstacleSnapshot> appliedObstacleSnapshots = new List<ObstacleSnapshot>();

    private NavMeshData navMeshData;
    private NavMeshDataInstance navMeshInstance;
    private SpriteRenderer walkableAreaRenderer;
    private AsyncOperation rebuildOperation;
    private Coroutine rebuildCoroutine;
    private Bounds pendingWalkableBounds;
    private Vector3 appliedWalkableCenter;
    private Vector3 appliedWalkableSize;
    private bool hasLayoutSnapshot;

    private void Awake()
    {
        walkableAreaRenderer = GetComponent<SpriteRenderer>();
    }

    private void OnEnable()
    {
        BuildNavMesh();

        if (Application.isPlaying && rebuildIntervalSeconds > 0f)
        {
            rebuildCoroutine = StartCoroutine(RebuildPeriodically());
        }
    }

    private void OnDisable()
    {
        if (rebuildCoroutine != null)
        {
            StopCoroutine(rebuildCoroutine);
            rebuildCoroutine = null;
        }

        rebuildOperation = null;
        hasLayoutSnapshot = false;
        latestObstacleSnapshots.Clear();
        appliedObstacleSnapshots.Clear();
        RemoveNavMesh();
        navMeshData = null;
    }

    public void BuildNavMesh()
    {
        if (!TryCollectBuildData(out List<NavMeshBuildSource> sources, out Bounds buildBounds))
        {
            return;
        }

        ApplyBuild(sources, buildBounds, useAsyncUpdate: false);
    }

    // Re-samples obstacle positions on a cadence so small student motion can affect pathing.
    private IEnumerator RebuildPeriodically()
    {
        WaitForSeconds wait = new WaitForSeconds(rebuildIntervalSeconds);

        while (true)
        {
            yield return wait;

            if (rebuildOperation != null && !rebuildOperation.isDone)
            {
                continue;
            }

            if (!TryCollectBuildData(out List<NavMeshBuildSource> sources, out Bounds buildBounds))
            {
                continue;
            }

            if (!HasLayoutChanged())
            {
                continue;
            }

            ApplyBuild(sources, buildBounds, useAsyncUpdate: true);
        }
    }

    private bool TryCollectBuildData(out List<NavMeshBuildSource> sources, out Bounds buildBounds)
    {
        sources = null;
        buildBounds = default;

        if (walkableAreaRenderer == null)
        {
            walkableAreaRenderer = GetComponent<SpriteRenderer>();
        }

        if (walkableAreaRenderer == null || NavMesh.GetSettingsCount() == 0)
        {
            return false;
        }

        Bounds walkableBounds = walkableAreaRenderer.bounds;
        pendingWalkableBounds = walkableBounds;
        latestObstacleSnapshots.Clear();

        int notWalkableArea = NavMesh.GetAreaFromName("Not Walkable");
        sources = new List<NavMeshBuildSource>
        {
            CreateGroundSource(walkableBounds)
        };

        BoxCollider2D[] colliders = UnityEngine.Object.FindObjectsByType<BoxCollider2D>(FindObjectsSortMode.None);
        Array.Sort(colliders, CompareCollidersByInstanceId);

        for (int index = 0; index < colliders.Length; index++)
        {
            BoxCollider2D collider = colliders[index];

            if (!IsObstacle(collider))
            {
                continue;
            }

            Bounds obstacleBounds = collider.bounds;
            sources.Add(CreateObstacleSource(obstacleBounds, notWalkableArea));
            latestObstacleSnapshots.Add(new ObstacleSnapshot(collider.GetInstanceID(), obstacleBounds.center, obstacleBounds.size));
        }

        buildBounds = CreateBuildBounds(walkableBounds);
        return true;
    }

    private void ApplyBuild(List<NavMeshBuildSource> sources, Bounds buildBounds, bool useAsyncUpdate)
    {
        NavMeshBuildSettings settings = NavMesh.GetSettingsByIndex(0);

        if (useAsyncUpdate && navMeshData != null)
        {
            rebuildOperation = NavMeshBuilder.UpdateNavMeshDataAsync(navMeshData, settings, sources, buildBounds);
            if (rebuildOperation == null)
            {
                return;
            }

            rebuildOperation.completed += OnRebuildCompleted;
            SaveLayoutSnapshot();
            return;
        }

        NavMeshData builtNavMesh = NavMeshBuilder.BuildNavMeshData(settings, sources, buildBounds, Vector3.zero, Quaternion.identity);
        if (builtNavMesh == null)
        {
            return;
        }

        RemoveNavMesh();
        navMeshData = builtNavMesh;
        navMeshInstance = NavMesh.AddNavMeshData(navMeshData);
        SaveLayoutSnapshot();
    }

    private bool HasLayoutChanged()
    {
        if (!hasLayoutSnapshot)
        {
            return true;
        }

        if (HasVectorChanged(appliedWalkableCenter, pendingWalkableBounds.center)
            || HasVectorChanged(appliedWalkableSize, pendingWalkableBounds.size))
        {
            return true;
        }

        if (latestObstacleSnapshots.Count != appliedObstacleSnapshots.Count)
        {
            return true;
        }

        for (int index = 0; index < latestObstacleSnapshots.Count; index++)
        {
            if (!latestObstacleSnapshots[index].Matches(appliedObstacleSnapshots[index], obstacleChangeThreshold))
            {
                return true;
            }
        }

        return false;
    }

    private void SaveLayoutSnapshot()
    {
        appliedWalkableCenter = pendingWalkableBounds.center;
        appliedWalkableSize = pendingWalkableBounds.size;
        appliedObstacleSnapshots.Clear();
        appliedObstacleSnapshots.AddRange(latestObstacleSnapshots);
        hasLayoutSnapshot = true;
    }

    private void OnRebuildCompleted(AsyncOperation operation)
    {
        if (rebuildOperation == operation)
        {
            rebuildOperation = null;
        }
    }

    private bool HasVectorChanged(Vector3 from, Vector3 to)
    {
        return (from - to).sqrMagnitude > obstacleChangeThreshold * obstacleChangeThreshold;
    }

    private static int CompareCollidersByInstanceId(BoxCollider2D left, BoxCollider2D right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left == null)
        {
            return -1;
        }

        if (right == null)
        {
            return 1;
        }

        return left.GetInstanceID().CompareTo(right.GetInstanceID());
    }

    private bool IsObstacle(BoxCollider2D collider)
    {
        if (collider == null || !collider.enabled || collider.isTrigger || collider.gameObject == gameObject)
        {
            return false;
        }

        Rigidbody2D attachedBody = collider.attachedRigidbody;
        if (attachedBody != null)
        {
            if (attachedBody.TryGetComponent(out AraBotClickToMove _))
            {
                return false;
            }

            if (attachedBody.gameObject == gameObject)
            {
                return false;
            }
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

        navMeshInstance = default;
    }

    private readonly struct ObstacleSnapshot
    {
        private readonly int instanceId;
        private readonly Vector3 center;
        private readonly Vector3 size;

        public ObstacleSnapshot(int instanceId, Vector3 center, Vector3 size)
        {
            this.instanceId = instanceId;
            this.center = center;
            this.size = size;
        }

        public bool Matches(ObstacleSnapshot other, float threshold)
        {
            if (instanceId != other.instanceId)
            {
                return false;
            }

            float thresholdSquared = threshold * threshold;
            return (center - other.center).sqrMagnitude <= thresholdSquared
                && (size - other.size).sqrMagnitude <= thresholdSquared;
        }
    }
}

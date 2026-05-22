using System.Collections.Generic;
using UnityEngine;

public class MapZoneComponent : MonoBehaviour
{
    [Header("Zone ID")]
    [SerializeField] private int id;
    public int SpawnId => id;

    // Несколько коллайдеров на одну зону: дорога/комната описывается
    // набором прямоугольных сегментов, а не одним большим AABB.
    // Сериализуем список — Unity сохранит ссылки при перезагрузке сцены.
    [Header("Zone Colliders (segments)")]
    [SerializeField] private List<BoxCollider> boxColliders = new();
    public IReadOnlyList<BoxCollider> BoxColliders => boxColliders;

    private List<Vector3> samplePoints = new();

    // На референс-карте зоны расставлены вручную в сцене — там нет MapGenerator,
    // который зарегистрировал бы их в MapManager. Делаем это сами на старте.
    // Идемпотентно: RegisterZone проверяет дубликаты.
    private void Awake()
    {
        MapManager mapManager = MapManager.Instance;
        if (mapManager != null)
            mapManager.RegisterZone(this);
    }

    public void InitializeZone(int zoneId, IEnumerable<BoxCollider> colliders)
    {
        id = zoneId;
        boxColliders.Clear();
        if (colliders == null)
            return;

        foreach (BoxCollider collider in colliders)
        {
            if (collider != null)
                boxColliders.Add(collider);
        }
    }

    // Совместимость со старым API: одна зона = один коллайдер.
    public void InitializeZone(int zoneId, BoxCollider collider)
    {
        id = zoneId;
        boxColliders.Clear();
        if (collider != null)
            boxColliders.Add(collider);
    }

    public void SetSamplePoints(List<Vector3> points)
    {
        samplePoints = points ?? new List<Vector3>();
    }

    public float GetWeight(BotRole role)
    {
        MapManager mapManager = MapManager.Instance;
        if (mapManager == null) return 0f;
        return mapManager.GetWeight(this, role);
    }

    public bool ContainsWorldPointXZ(Vector3 worldPoint)
    {
        foreach (BoxCollider collider in boxColliders)
        {
            if (collider == null) continue;
            Bounds b = collider.bounds;
            if (worldPoint.x >= b.min.x && worldPoint.x <= b.max.x
                && worldPoint.z >= b.min.z && worldPoint.z <= b.max.z)
                return true;
        }
        return false;
    }

    // Дороги/нейтраль — дискретные sample (клетки). Site переопределяет — непрерывно в коллайдере.
    public virtual Vector3 GetRandomPointInZone()
    {
        if (samplePoints.Count > 0)
            return samplePoints[Random.Range(0, samplePoints.Count)];

        return GetRandomPointInColliderBounds();
    }

    protected Vector3 GetRandomPointInColliderBounds()
    {
        BoxCollider collider = PickColliderWeightedByVolume();
        if (collider == null)
            return transform.position;

        Bounds bounds = collider.bounds;
        return new Vector3(
            Random.Range(bounds.min.x, bounds.max.x),
            bounds.center.y,
            Random.Range(bounds.min.z, bounds.max.z));
    }

    // Ближайшая к goal точка внутри зоны (для main/link — «передний» край в сторону сайта, не у спавна защиты).
    public Vector3 GetPointToward(Vector3 goal)
    {
        if (samplePoints.Count > 0)
            return PickClosestSample(goal);

        return ClampWorldPointToZone(goal);
    }

    // Ближайшая точка зоны к fromWorld (старт движения по дороге).
    public Vector3 GetNearestPointInZone(Vector3 fromWorld)
    {
        if (samplePoints.Count > 0)
            return PickClosestSample(fromWorld);

        return ClampWorldPointToZone(fromWorld);
    }

    // Старт по дороге: случайная sample в радиусе от бота — чтобы не сбиваться в одну «ближайшую» точку.
    public Vector3 GetEntryPointNear(Vector3 fromWorld, float pickRadius)
    {
        pickRadius = Mathf.Max(1f, pickRadius);

        if (samplePoints.Count > 0)
        {
            List<Vector3> pool = CollectSamplesWithin(fromWorld, pickRadius);
            if (pool.Count == 0)
                pool = CollectSamplesWithin(fromWorld, pickRadius * 2f);
            if (pool.Count == 0)
                return PickRandomAmongNearest(fromWorld, 3);
            return pool[Random.Range(0, pool.Count)];
        }

        return JitterAround(ClampWorldPointToZone(fromWorld), pickRadius * 0.5f);
    }

    // Конец маршрута по дороге: случайная точка среди «передних» sample к goal, не одна ближайшая к цели.
    public Vector3 GetRandomForwardPoint(Vector3 goalWorld, float forwardPortion = 0.4f)
    {
        forwardPortion = Mathf.Clamp(forwardPortion, 0.05f, 1f);

        if (samplePoints.Count == 0)
            return GetPointToward(goalWorld);

        int take = Mathf.Max(1, Mathf.CeilToInt(samplePoints.Count * forwardPortion));
        var ranked = new List<Vector3>(samplePoints);
        ranked.Sort((a, b) =>
            HorizontalSqrDistance(a, goalWorld).CompareTo(HorizontalSqrDistance(b, goalWorld)));
        return ranked[Random.Range(0, take)];
    }

    // Цепочка точек вдоль зоны: разнесённый старт/финиш, промежуточные по шагу.
    public List<Vector3> GetWaypointsToward(
        Vector3 fromWorld,
        Vector3 goalWorld,
        float stepMeters,
        float entryPickRadius = 5f,
        float forwardPickPortion = 0.4f)
    {
        Vector3 start = GetEntryPointNear(fromWorld, entryPickRadius);
        Vector3 end = GetRandomForwardPoint(goalWorld, forwardPickPortion);
        stepMeters = Mathf.Max(0.5f, stepMeters);

        List<Vector3> candidates = CollectCandidatePoints();
        if (candidates.Count == 0)
            return HorizontalSqrDistance(start, end) > 0.01f
                ? new List<Vector3> { start, end }
                : new List<Vector3> { end };

        Vector3 axis = end - start;
        axis.y = 0f;
        if (axis.sqrMagnitude < 0.25f)
            return HorizontalSqrDistance(start, end) > 0.01f
                ? new List<Vector3> { start, end }
                : new List<Vector3> { end };

        axis.Normalize();
        candidates.Sort((a, b) =>
        {
            float da = Vector3.Dot(a - start, axis);
            float db = Vector3.Dot(b - start, axis);
            return da.CompareTo(db);
        });

        var waypoints = new List<Vector3> { start };
        float lastAlong = 0f;
        float endAlong = Vector3.Dot(end - start, axis);

        foreach (Vector3 p in candidates)
        {
            float along = Vector3.Dot(p - start, axis);
            if (along <= 0.01f) continue;
            if (along >= endAlong - 0.01f) continue;
            if (along - lastAlong < stepMeters) continue;

            waypoints.Add(p);
            lastAlong = along;
        }

        if (HorizontalSqrDistance(waypoints[waypoints.Count - 1], end) > 0.01f)
            waypoints.Add(end);

        return waypoints;
    }

    private Vector3 ClampWorldPointToZone(Vector3 worldPoint)
    {
        if (boxColliders.Count == 0)
            return transform.position;

        Vector3 best = transform.position;
        float bestSq = float.PositiveInfinity;

        foreach (BoxCollider collider in boxColliders)
        {
            if (collider == null) continue;
            Bounds bounds = collider.bounds;
            Vector3 p = new Vector3(
                Mathf.Clamp(worldPoint.x, bounds.min.x, bounds.max.x),
                bounds.center.y,
                Mathf.Clamp(worldPoint.z, bounds.min.z, bounds.max.z));
            float sq = HorizontalSqrDistance(p, worldPoint);
            if (sq < bestSq)
            {
                bestSq = sq;
                best = p;
            }
        }

        return best;
    }

    private List<Vector3> CollectCandidatePoints()
    {
        if (samplePoints.Count > 0)
            return new List<Vector3>(samplePoints);

        var points = new List<Vector3>();
        foreach (BoxCollider collider in boxColliders)
        {
            if (collider == null) continue;
            points.Add(collider.bounds.center);
        }
        return points;
    }

    private Vector3 PickClosestSample(Vector3 goal)
    {
        Vector3 best = samplePoints[0];
        float bestSq = HorizontalSqrDistance(best, goal);
        for (int i = 1; i < samplePoints.Count; i++)
        {
            float sq = HorizontalSqrDistance(samplePoints[i], goal);
            if (sq < bestSq)
            {
                bestSq = sq;
                best = samplePoints[i];
            }
        }
        return best;
    }

    private static float HorizontalSqrDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    private List<Vector3> CollectSamplesWithin(Vector3 fromWorld, float radius)
    {
        float radiusSq = radius * radius;
        var pool = new List<Vector3>();
        for (int i = 0; i < samplePoints.Count; i++)
        {
            if (HorizontalSqrDistance(samplePoints[i], fromWorld) <= radiusSq)
                pool.Add(samplePoints[i]);
        }
        return pool;
    }

    private Vector3 PickRandomAmongNearest(Vector3 fromWorld, int count)
    {
        count = Mathf.Clamp(count, 1, samplePoints.Count);
        var ranked = new List<Vector3>(samplePoints);
        ranked.Sort((a, b) =>
            HorizontalSqrDistance(a, fromWorld).CompareTo(HorizontalSqrDistance(b, fromWorld)));
        return ranked[Random.Range(0, count)];
    }

    private Vector3 JitterAround(Vector3 center, float radius)
    {
        if (boxColliders.Count == 0)
            return center;

        float angle = Random.value * Mathf.PI * 2f;
        float dist = Random.Range(0f, radius);
        Vector3 offset = new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
        return ClampWorldPointToZone(center + offset);
    }

    protected BoxCollider PickColliderWeightedByVolume()
    {
        float totalArea = 0f;
        foreach (BoxCollider collider in boxColliders)
        {
            if (collider == null)
                continue;

            Vector3 size = collider.size;
            totalArea += Mathf.Max(0.0001f, size.x * size.z);
        }

        if (totalArea <= 0f)
            return null;

        float pick = Random.value * totalArea;
        float accumulated = 0f;
        foreach (BoxCollider collider in boxColliders)
        {
            if (collider == null)
                continue;

            Vector3 size = collider.size;
            accumulated += Mathf.Max(0.0001f, size.x * size.z);
            if (pick <= accumulated)
                return collider;
        }

        return boxColliders[boxColliders.Count - 1];
    }
}

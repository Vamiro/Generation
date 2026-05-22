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

    public Vector3 GetRandomPointInZone()
    {
        if (samplePoints.Count > 0)
            return samplePoints[Random.Range(0, samplePoints.Count)];

        // Fallback: случайный сегмент → случайная точка внутри его bounds.
        // Выбор сегмента взвешен по объёму, чтобы крупный сегмент чаще давал точку.
        if (boxColliders.Count == 0)
            return transform.position;

        BoxCollider chosen = PickColliderWeightedByVolume();
        if (chosen == null)
            return transform.position;

        Bounds bounds = chosen.bounds;
        return new Vector3(
            Random.Range(bounds.min.x, bounds.max.x),
            bounds.center.y,
            Random.Range(bounds.min.z, bounds.max.z)
        );
    }

    private BoxCollider PickColliderWeightedByVolume()
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

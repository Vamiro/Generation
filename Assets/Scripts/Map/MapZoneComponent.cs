using System.Collections.Generic;
using UnityEngine;

public class MapZoneComponent : MonoBehaviour
{
    [Header("Zone ID")]
    [SerializeField] private int id;
    public int SpawnId => id;

    [Header("Zone Weights")]
    [SerializeField] private float attackWeight = 0f;
    [SerializeField] private float defenseWeight = 0f;
    [SerializeField] private float flankWeight = 0f;
    [SerializeField] private float scoutWeight = 0f;

    // Несколько коллайдеров на одну зону: дорога/комната описывается
    // набором прямоугольных сегментов, а не одним большим AABB.
    // Сериализуем список — Unity сохранит ссылки при перезагрузке сцены.
    [Header("Zone Colliders (segments)")]
    [SerializeField] private List<BoxCollider> boxColliders = new();
    public IReadOnlyList<BoxCollider> BoxColliders => boxColliders;

    // Tactical slots генерируются MapGenerator.TacticalSlots после PlaceCovers.
    // Хранятся прямо в зоне, чтобы бот мог запросить "дай Hold-слот этого сайта".
    // Не [SerializeField]: пересоздаются при каждой генерации карты, в сцену сохранять не нужно.
    private readonly List<TacticalSlot> tacticalSlots = new();
    public IReadOnlyList<TacticalSlot> TacticalSlots => tacticalSlots;

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
        return role switch
        {
            BotRole.Attacker => attackWeight,
            BotRole.Defender => defenseWeight,
            BotRole.Flanker => flankWeight,
            BotRole.Scout => scoutWeight,
            _ => 1f
        };
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

    // ─────────────────── Tactical slots API ───────────────────

    public void AddTacticalSlot(TacticalSlot slot)
    {
        if (slot == null) return;
        tacticalSlots.Add(slot);
    }

    /// <summary>
    /// Удаляет конкретный слот из зоны. Нужно для <c>TacticalSlotMarker.OnDisable</c> —
    /// чтобы при отключении/удалении маркера в Edit Mode слот не "висел" в списке зоны.
    /// </summary>
    public bool RemoveTacticalSlot(TacticalSlot slot)
    {
        if (slot == null) return false;
        return tacticalSlots.Remove(slot);
    }

    public void ClearTacticalSlots()
    {
        tacticalSlots.Clear();
    }

    /// <summary>
    /// Берёт ближайший к requester-у свободный слот указанного типа и бронирует его.
    /// Если у requester-а уже забронирован слот в этой зоне — сначала освобождает.
    /// Возвращает null, если свободных слотов нужного типа нет.
    /// Детерминированно: при равных дистанциях выбирается слот с минимальным
    /// (worldPos.x, worldPos.z). От Random не зависит — воспроизводимость сохраняется.
    /// </summary>
    public TacticalSlot TryAcquireSlot(TacticalSlotKind kind, BotComponent requester)
    {
        if (requester == null) return null;

        ReleaseSlotOf(requester);

        Vector3 from = requester.transform.position;
        TacticalSlot best = null;
        float bestSqr = float.PositiveInfinity;
        for (int i = 0; i < tacticalSlots.Count; i++)
        {
            TacticalSlot s = tacticalSlots[i];
            if (s.kind != kind || !s.IsFree) continue;

            float sqr = (s.worldPos - from).sqrMagnitude;
            if (sqr < bestSqr || (sqr == bestSqr && (best == null || CompareSlotsByPosition(s, best) < 0)))
            {
                bestSqr = sqr;
                best = s;
            }
        }

        if (best != null)
            best.occupant = requester;
        return best;
    }

    public void ReleaseSlotOf(BotComponent requester)
    {
        if (requester == null) return;
        for (int i = 0; i < tacticalSlots.Count; i++)
        {
            if (tacticalSlots[i].occupant == requester)
                tacticalSlots[i].occupant = null;
        }
    }

    // Стабильный порядок: сначала по x, потом по z. Не зависит от Random.
    private static int CompareSlotsByPosition(TacticalSlot a, TacticalSlot b)
    {
        int cmp = a.worldPos.x.CompareTo(b.worldPos.x);
        if (cmp != 0) return cmp;
        return a.worldPos.z.CompareTo(b.worldPos.z);
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

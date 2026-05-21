using UnityEngine;

[DisallowMultipleComponent]
public class TacticalSlotMarker : MonoBehaviour
{
    [Header("Параметры слота")]
    [Tooltip("Тип: HoldDefender (защитник держит угол) или PeekAttacker (атакер пикает).")]
    public TacticalSlotKind kind = TacticalSlotKind.HoldDefender;

    [Tooltip("Высота укрытия. Full = блокирует полностью; Half = только присевшего (перила, низкий бордюр).")]
    public CoverHeight coverHeight = CoverHeight.Full;

    [Min(0.1f), Tooltip("Радиус (м), в пределах которого слот считает цель прикрытой. По умолчанию ~1.5 м.")]
    public float influenceRadius = 1.5f;

    [Header("Зона")]
    [Tooltip("Зона, к которой принадлежит слот. Если не назначена — будет найдена автоматически по позиции маркера.")]
    public MapZoneComponent zone;

    [Header("Отладка")]
    [Tooltip("Длина стрелки facingDir в gizmo (Scene view).")]
    public float gizmoArrowLength = 1.5f;

    [Tooltip("Цвет gizmo маркера.")]
    public Color gizmoColor = new Color(0.2f, 1f, 0.4f, 1f);

    private TacticalSlot _registeredSlot;
    private MapZoneComponent _registeredZone;

    private void OnEnable()
    {
        MapZoneComponent owner = zone != null ? zone : FindZoneAtPosition(transform.position);
        if (owner == null)
        {
            Debug.LogWarning($"TacticalSlotMarker '{name}': не удалось найти зону по позиции. " +
                             "Назначьте поле 'zone' в инспекторе или поместите маркер внутрь триггер-коллайдера зоны.", this);
            return;
        }

        _registeredSlot = new TacticalSlot
        {
            worldPos        = transform.position,
            facingDir       = transform.forward,
            kind            = kind,
            coverHeight     = coverHeight,
            influenceRadius = influenceRadius,
            // coverScore для ручных слотов не считаем — это поле автогенератора (см. MapGenerator.TacticalSlots).
            // Оставляем 0; влияния на текущую боевую модель не имеет.
            coverScore      = 0,
        };
        owner.AddTacticalSlot(_registeredSlot);
        _registeredZone = owner;
    }

    private void OnDisable()
    {
        if (_registeredSlot == null || _registeredZone == null) return;

        // Освобождаем бронь, если кто-то стоял на этом слоте, и удаляем из списка зоны.
        if (_registeredSlot.occupant != null)
            _registeredZone.ReleaseSlotOf(_registeredSlot.occupant);
        _registeredZone.RemoveTacticalSlot(_registeredSlot);

        _registeredSlot = null;
        _registeredZone = null;
    }

    // Авто-поиск зоны по триггер-коллайдерам в позиции маркера. Размер слоя — небольшой,
    // зон на карте обычно <50, поэтому OverlapSphere с малым радиусом + перебор хитов
    // в инспекторе работает мгновенно и не зависит от слоёв Physics.
    private static MapZoneComponent FindZoneAtPosition(Vector3 worldPos)
    {
        // Маленький радиус — мы стоим внутри триггера зоны, не на границе.
        Collider[] hits = Physics.OverlapSphere(worldPos, 0.05f, ~0, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0) return null;

        MapZoneComponent best = null;
        foreach (Collider c in hits)
        {
            if (c == null) continue;
            var z = c.GetComponentInParent<MapZoneComponent>();
            if (z != null) { best = z; break; }
        }
        return best;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;
        Gizmos.DrawSphere(transform.position, 0.15f);

        // Стрелка facingDir — куда смотрит бот.
        Vector3 end = transform.position + transform.forward * gizmoArrowLength;
        Gizmos.DrawLine(transform.position, end);

        // Маленькое крыло на конце, чтобы видно было направление.
        Vector3 right = transform.position + transform.forward * (gizmoArrowLength * 0.7f) + transform.right * 0.15f;
        Vector3 left  = transform.position + transform.forward * (gizmoArrowLength * 0.7f) - transform.right * 0.15f;
        Gizmos.DrawLine(end, right);
        Gizmos.DrawLine(end, left);

        // Радиус влияния — пунктиром (Gizmos.DrawWireSphere — лучшее, что есть без doodling).
        Color rim = gizmoColor; rim.a = 0.25f;
        Gizmos.color = rim;
        Gizmos.DrawWireSphere(transform.position, influenceRadius);
    }
}

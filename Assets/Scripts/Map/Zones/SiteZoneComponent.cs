using UnityEngine;

// На Site не ходим по центрам клеток samplePoints — только непрерывная точка в коллайдере зоны.
public class SiteZoneComponent : MapZoneComponent
{
    public override Vector3 GetRandomPointInZone() => GetRandomPointInColliderBounds();
}

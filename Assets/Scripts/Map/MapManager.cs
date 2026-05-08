using System.Collections.Generic;
using UnityEngine;

public class MapManager : MonoSingleton<MapManager>
{
    [SerializeField] private List<MapZoneComponent> zones = new();
    public List<MapZoneComponent> Zones => zones;
    public List<SiteZoneComponent> SiteZones => GetZonesOfType<SiteZoneComponent>();
    public List<RoadZoneComponent> RoadZones => GetZonesOfType<RoadZoneComponent>();
    public List<SpawnZoneComponent> SpawnZones => GetZonesOfType<SpawnZoneComponent>();
    public List<NeutralZoneComponent> NeutralZones => GetZonesOfType<NeutralZoneComponent>();

    protected override void Awake()
    {
        base.Awake();

        if (zones == null)
            zones = new List<MapZoneComponent>();

        zones.RemoveAll(zone => zone == null);
    }

    public void ClearZones()
    {
        zones.Clear();
    }

    public void RegisterZone(MapZoneComponent zone)
    {
        if (zone == null || zones.Contains(zone))
            return;

        zones.Add(zone);
    }

    public void RegisterZones(IEnumerable<MapZoneComponent> newZones)
    {
        if (newZones == null)
            return;

        foreach (MapZoneComponent zone in newZones)
            RegisterZone(zone);
    }

    private List<TZone> GetZonesOfType<TZone>() where TZone : MapZoneComponent
    {
        for (int i = zones.Count - 1; i >= 0; i--)
        {
            if (zones[i] == null)
                zones.RemoveAt(i);
        }

        List<TZone> result = new();
        foreach (MapZoneComponent zone in zones)
        {
            if (zone is TZone typedZone)
                result.Add(typedZone);
        }

        return result;
    }
}

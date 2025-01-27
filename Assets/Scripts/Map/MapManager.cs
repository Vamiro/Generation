using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MapManager : MonoSingleton<MapManager>
{
    [SerializeField] private List<MapZoneComponent> zones;
    public List<MapZoneComponent> Zones => zones;
    public List<SiteZoneComponent> SiteZones => zones.OfType<SiteZoneComponent>().ToList();
    public List<RoadZoneComponent> RoadZones => zones.OfType<RoadZoneComponent>().ToList();
    public List<SpawnZoneComponent> SpawnZones => zones.OfType<SpawnZoneComponent>().ToList();
    public List<NeutralZoneComponent> NeutralZones => zones.OfType<NeutralZoneComponent>().ToList();
}

using System.Linq;
using UnityEngine;

public class DefenderTeamManager : TeamManager
{
    private bool _isRotated = false;
    private float _delay = 10f;
    private float _currentTime = 0f;
    private bool _isMovingToSite = true;

    public override void Start()
    {
        base.Start();
        AssignRoles();
    }

    public override void Update()
    {
        base.Update();
        
        if (!_isMovingToSite) return;
        if (MapManager.Instance.SiteZones.Count == 0)
            return;
        
        _currentTime += Time.deltaTime;
        if (!(_currentTime >= _delay)) return;
        _isMovingToSite = false;
        _currentTime = 0;
        TryRepositionDefenders(0, 2, MapManager.Instance.SiteZones[0]);

        var secondSite = MapManager.Instance.SiteZones.Count > 1
            ? MapManager.Instance.SiteZones[1]
            : MapManager.Instance.SiteZones[0];
        TryRepositionDefenders(2, 4, secondSite);
    }

    private void AssignRoles()
    {
        if (MapManager.Instance.SiteZones.Count == 0)
        {
            Debug.LogWarning("DefenderTeamManager: отсутствуют Site-зоны.");
            return;
        }
            
        // Первые два бота защищают первую точку
        for (var i = 0; i < 2; i++) Bots[i].AssignRole(BotRole.Defender, MapManager.Instance.SiteZones[0]);
        // Вторые два бота защищают вторую точку
        var secondSite = MapManager.Instance.SiteZones.Count > 1
            ? MapManager.Instance.SiteZones[1]
            : MapManager.Instance.SiteZones[0];
        for (var i = 2; i < 4; i++) Bots[i].AssignRole(BotRole.Defender, secondSite);

        // Последний бот — скаут
        Bots[4].AssignRole(BotRole.Scout);
        // Шанс пойти в нейтральную зону, либо на любую дорогу
        var chanceToGoToNeutral = Random.Range(0, 2);
        if (chanceToGoToNeutral == 0)
        {
            if (MapManager.Instance.RoadZones.Count > 0)
                Bots[4].MoveToZone(MapManager.Instance.RoadZones[Random.Range(0, MapManager.Instance.RoadZones.Count)]);
        }
        else
        {
            if (MapManager.Instance.NeutralZones.Count > 0)
                Bots[4].MoveToZone(MapManager.Instance.NeutralZones[Random.Range(0, MapManager.Instance.NeutralZones.Count)]);
            else if (MapManager.Instance.RoadZones.Count > 0)
                Bots[4].MoveToZone(MapManager.Instance.RoadZones[Random.Range(0, MapManager.Instance.RoadZones.Count)]);
        }
    }

    private void TryRepositionDefenders(int start, int end, MapZoneComponent siteZone)
    {
        for (var i = start; i < end; i++)
        {
            var chance = Random.Range(0, 4);
            if (chance is 0 or 1)
            {
                Bots[i].MoveToZone(FindPreferredRoad(siteZone, RoadType.Main));
            }
            else if (chance == 2)
            {
                Bots[i].MoveToZone(FindPreferredRoad(siteZone, RoadType.Link));
            }
        }
    }

    private MapZoneComponent FindPreferredRoad(MapZoneComponent siteZone, RoadType preferredType)
    {
        var roadZones = MapManager.Instance.RoadZones;
        var exact = roadZones.FirstOrDefault(zone => zone.roadToSite == siteZone && zone.roadType == preferredType);
        if (exact != null)
            return exact;

        var sameSiteFallback = roadZones.FirstOrDefault(zone => zone.roadToSite == siteZone);
        if (sameSiteFallback != null)
            return sameSiteFallback;

        if (roadZones.Count > 0)
            return roadZones[Random.Range(0, roadZones.Count)];

        Debug.LogWarning("DefenderTeamManager: отсутствуют Road-зоны для reposition.");
        return siteZone;
    }
}

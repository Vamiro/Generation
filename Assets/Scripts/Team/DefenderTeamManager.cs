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
        PruneDeadBots();
        if (Bots == null || Bots.Count == 0) return;

        if (!_isMovingToSite) return;
        if (MapManager.Instance == null || MapManager.Instance.SiteZones.Count == 0)
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

        var siteA = MapManager.Instance.SiteZones[0];
        var siteB = MapManager.Instance.SiteZones.Count > 1
            ? MapManager.Instance.SiteZones[1]
            : siteA;

        AssignRoleSafe(0, BotRole.Defender, siteA);
        AssignRoleSafe(1, BotRole.Defender, siteA);
        AssignRoleSafe(2, BotRole.Defender, siteB);
        AssignRoleSafe(3, BotRole.Defender, siteB);
        AssignRoleSafe(4, BotRole.Scout, null);

        if (Bots.Count <= 4 || Bots[4] == null) return;

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

    private void AssignRoleSafe(int index, BotRole role, MapZoneComponent zone)
    {
        if (index < 0 || index >= Bots.Count) return;
        BotComponent bot = Bots[index];
        if (bot == null) return;
        bot.AssignRole(role, zone);
    }

    private void TryRepositionDefenders(int start, int end, MapZoneComponent siteZone)
    {
        int clampedEnd = Mathf.Min(end, Bots.Count);
        for (var i = start; i < clampedEnd; i++)
        {
            BotComponent bot = Bots[i];
            if (bot == null) continue;

            var chance = Random.Range(0, 4);
            if (chance is 0 or 1)
                bot.MoveToZone(FindPreferredRoad(siteZone, RoadType.Main));
            else if (chance == 2)
                bot.MoveToZone(FindPreferredRoad(siteZone, RoadType.Link));
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

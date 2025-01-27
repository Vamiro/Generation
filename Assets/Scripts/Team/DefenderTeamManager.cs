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
        
        _currentTime += Time.deltaTime;
        if (!(_currentTime >= _delay)) return;
        _isMovingToSite = false;
        _currentTime = 0;
        TryRepositionDefenders(0, 2, MapManager.Instance.SiteZones[0]);
        TryRepositionDefenders(2, 4, MapManager.Instance.SiteZones[1]);
    }

    private void AssignRoles()
    {
            
        // Первые два бота защищают первую точку
        for (var i = 0; i < 2; i++) Bots[i].AssignRole(BotRole.Defender, MapManager.Instance.SiteZones[0]);
        // Вторые два бота защищают вторую точку
        for (var i = 2; i < 4; i++) Bots[i].AssignRole(BotRole.Defender, MapManager.Instance.SiteZones[1]);

        // Последний бот — скаут
        Bots[4].AssignRole(BotRole.Scout);
        // Шанс пойти в нейтральную зону, либо на любую дорогу
        var chanceToGoToNeutral = Random.Range(0, 2);
        if (chanceToGoToNeutral == 0)
        {
            Bots[4].MoveToZone(MapManager.Instance.RoadZones[Random.Range(0, MapManager.Instance.RoadZones.Count)]);
        }
        else
        {
            Bots[4].MoveToZone(MapManager.Instance.NeutralZones[Random.Range(0, MapManager.Instance.NeutralZones.Count)]);
        }
    }

    private void TryRepositionDefenders(int start, int end, MapZoneComponent siteZone)
    {
        for (var i = start; i < end; i++)
        {
            var chance = Random.Range(0, 4);
            if (chance is 0 or 1)
            {
                Bots[i].MoveToZone(MapManager.Instance.RoadZones.First(zone => zone.roadToSite == siteZone && zone.roadType == RoadType.Main));
            }
            else if (chance == 2)
            {
                Bots[i].MoveToZone(MapManager.Instance.RoadZones.First(zone => zone.roadToSite == siteZone && zone.roadType == RoadType.Link));
            }
        }
    }
}
using System.Linq;
using UnityEngine;

public class DefenderTeamManager : TeamManager
{
    private bool _isRotated = false;

    public override void Start()
    {
        base.Start();
        AssignRoles();
    }

    public override void Update()
    {
        base.Update();
    }

    private void AssignRoles()
    {
            
        // Первые два бота защищают первую точку
        AssignDefenderRoles(0, 2, MapManager.Instance.SiteZones[0]);

        // Вторые два бота защищают вторую точку
        AssignDefenderRoles(2, 4, MapManager.Instance.SiteZones[1]);


        // Последний бот — скаут
        Bots[4].AssignRole(BotRole.Scout);
        // Шанс пойти в нейтральную зону, либо на любую дорогу
        var chanceToGoToNeutral = Random.Range(0, 4);
        if (chanceToGoToNeutral == 0)
        {
            Bots[4].MoveToZone(MapManager.Instance.RoadZones[Random.Range(0, MapManager.Instance.RoadZones.Count)]);
        }
        else
        {
            Bots[4].MoveToZone(MapManager.Instance.NeutralZones[Random.Range(0, MapManager.Instance.NeutralZones.Count)]);
        }
    }

    private void AssignDefenderRoles(int start, int end, MapZoneComponent siteZone)
    {
        for (var i = start; i < end; i++)
        {
            Bots[i].AssignRole(BotRole.Defender, siteZone);
            var chance = Random.Range(0, 6);
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
using System.Linq;
using UnityEngine;

public class AttackerTeamManager : TeamManager
{
    private int _rotationThreshold = 2; // Разница сил для ротации
    private bool _isRotated = false;
    private bool _isMovingToSite = false;

    public override void Start()
    {
        base.Start();
        AssignRoles();
        ChooseTargetSite();
    }

    public override void Update()
    {
        base.Update();
        CheckForRotation();
        CheckOnPosition();
    }

    private void AssignRoles()
    {
        // Первые 2 бота — атакующие
        for (var i = 0; i < 2; i++) Bots[i].AssignRole(BotRole.Attacker);

        // Следующие 2 бота — фланкеры
        for (var i = 2; i < 4; i++) Bots[i].AssignRole(BotRole.Flanker);

        // Последний бот — скаут
        Bots[4].AssignRole(BotRole.Scout);
    }

    private void ChooseTargetSite()
    {
        if (!targetSite)
        {
            // Выбираем случайную SiteVolume
            targetSite = MapManager.Instance.SiteZones[Random.Range(0, MapManager.Instance.SiteZones.Count)];
        }
        else
        {
            // Выбираем следующую SiteVolume
            var index = MapManager.Instance.SiteZones.IndexOf(targetSite);
            targetSite = MapManager.Instance.SiteZones[(index + 1) % MapManager.Instance.SiteZones.Count];
        }

        // Назначаем цели
        foreach (var bot in Bots)
        {
            if (bot.Role == BotRole.Attacker)
                bot.MoveToZone(MapManager.Instance.RoadZones.First(zone => zone.roadToSite == targetSite && zone.roadType == RoadType.Main));

            if (bot.Role == BotRole.Flanker)
                bot.MoveToZone(MapManager.Instance.RoadZones.First(zone => zone.roadToSite == targetSite && zone.roadType == RoadType.Link));

            if (bot.Role == BotRole.Scout)
                bot.MoveToZone(MapManager.Instance.NeutralZones[Random.Range(0, MapManager.Instance.NeutralZones.Count)]);
        }
        
        _isMovingToSite = false;
    }

    private void CheckForRotation()
    {
        if (OtherTeam.Bots.Count - Bots.Count < _rotationThreshold || _isRotated) return;
        _isRotated = true;
        foreach (var bot in Bots) bot.AssignRole(BotRole.Attacker);
        ChooseTargetSite();
    }
    
    private void CheckOnPosition()
    {
        if (_isMovingToSite || !Bots.All(bot => bot.IsOnPosition)) return;
        _isMovingToSite = true;
        foreach (var bot in Bots)
        {
            if (bot.Role is BotRole.Attacker or BotRole.Flanker)
                bot.MoveToZone(targetSite);
        }
    }
}

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
        if (MapManager.Instance.SiteZones.Count == 0)
        {
            Debug.LogWarning("AttackerTeamManager: отсутствуют Site-зоны.");
            return;
        }

        if (!targetSite)
        {
            // Выбираем случайную SiteVolume
            targetSite = MapManager.Instance.SiteZones[Random.Range(0, MapManager.Instance.SiteZones.Count)];
            
            // Назначаем цели
            foreach (var bot in Bots)
            {
                if (bot.Role == BotRole.Attacker)
                    bot.MoveToZone(FindPreferredRoad(targetSite, RoadType.Main) ?? targetSite);

                if (bot.Role == BotRole.Flanker)
                    bot.MoveToZone(FindPreferredRoad(targetSite, RoadType.Link) ?? targetSite);

                if (bot.Role == BotRole.Scout)
                {
                    var chanceToGoToNeutral = Random.Range(0, 2);
                    if (chanceToGoToNeutral == 0)
                    {
                        var roadZones = MapManager.Instance.RoadZones;
                        if (roadZones.Count > 0)
                            bot.MoveToZone(roadZones[Random.Range(0, roadZones.Count)]);
                    }
                    else
                    {
                        var neutralZones = MapManager.Instance.NeutralZones;
                        if (neutralZones.Count > 0)
                            bot.MoveToZone(neutralZones[Random.Range(0, neutralZones.Count)]);
                    }
                }
            }
        
            _isMovingToSite = false;
        }
        else
        {
            // Выбираем следующую SiteVolume
            var index = MapManager.Instance.SiteZones.IndexOf(targetSite);
            if (index < 0)
                index = 0;

            targetSite = MapManager.Instance.SiteZones[(index + 1) % MapManager.Instance.SiteZones.Count];
            
            foreach (var bot in Bots)
            {
                var neutralZones = MapManager.Instance.NeutralZones;
                if (neutralZones.Count > 0)
                    bot.MoveToZone(neutralZones[Random.Range(0, neutralZones.Count)]);
            }
            
            OtherTeam.NotifyDefendersAboutRotate(targetSite);
            
            // Назначаем цели
            _isMovingToSite = false;
        }
    }

    private void CheckForRotation()
    {
        if (Bots.Count == 1 && Bots[0].Role != BotRole.Attacker) Bots[0].AssignRole(BotRole.Attacker, targetSite);
        
        if (OtherTeam.Bots.Count - Bots.Count < _rotationThreshold || _isRotated) return;
        _isRotated = true;
        foreach (var bot in Bots) bot.AssignRole(BotRole.Attacker);
        ChooseTargetSite();
    }
    
    private void CheckOnPosition()
    {
        if (_isMovingToSite || targetSite == null || !Bots.All(bot => bot.IsOnPosition)) return;
        _isMovingToSite = true;
        foreach (var bot in Bots)
        {
            if (bot.Role is BotRole.Attacker or BotRole.Flanker)
                bot.MoveToZone(targetSite);
        }
    }

    private MapZoneComponent FindPreferredRoad(SiteZoneComponent site, RoadType preferredType)
    {
        var roadZones = MapManager.Instance.RoadZones;
        var exact = roadZones.FirstOrDefault(zone => zone.roadToSite == site && zone.roadType == preferredType);
        if (exact != null)
            return exact;

        var sameSiteFallback = roadZones.FirstOrDefault(zone => zone.roadToSite == site);
        if (sameSiteFallback != null)
            return sameSiteFallback;

        return roadZones.Count > 0 ? roadZones[Random.Range(0, roadZones.Count)] : null;
    }
}

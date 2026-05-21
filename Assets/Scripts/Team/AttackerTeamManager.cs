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
        // Перед любой LINQ/итерацией убираем destroyed-ссылки, иначе .All / .Count падают
        // в момент, когда бот уже Destroy(gameObject), но ещё не успел сам выйти из Bots.
        PruneDeadBots();
        if (Bots == null || Bots.Count == 0) return;
        if (OtherTeam == null) return;
        CheckForRotation();
        CheckOnPosition();
    }

    private void AssignRoles()
    {
        AssignRoleSafe(0, BotRole.Attacker);
        AssignRoleSafe(1, BotRole.Attacker);
        AssignRoleSafe(2, BotRole.Flanker);
        AssignRoleSafe(3, BotRole.Flanker);
        AssignRoleSafe(4, BotRole.Scout);
    }

    private void AssignRoleSafe(int index, BotRole role)
    {
        if (index < 0 || index >= Bots.Count) return;
        BotComponent bot = Bots[index];
        if (bot == null) return;
        bot.AssignRole(role);
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
                if (bot == null) continue;
                if (bot.Role == BotRole.Attacker)
                    MoveBotToZoneOrPeek(bot, FindPreferredRoad(targetSite, RoadType.Main) ?? targetSite);

                if (bot.Role == BotRole.Flanker)
                    MoveBotToZoneOrPeek(bot, FindPreferredRoad(targetSite, RoadType.Link) ?? targetSite);

                if (bot.Role == BotRole.Scout)
                {
                    var chanceToGoToNeutral = Random.Range(0, 2);
                    if (chanceToGoToNeutral == 0)
                    {
                        var roadZones = MapManager.Instance.RoadZones;
                        if (roadZones.Count > 0)
                            MoveBotToZoneOrPeek(bot, roadZones[Random.Range(0, roadZones.Count)]);
                    }
                    else
                    {
                        var neutralZones = MapManager.Instance.NeutralZones;
                        if (neutralZones.Count > 0)
                            MoveBotToZoneOrPeek(bot, neutralZones[Random.Range(0, neutralZones.Count)]);
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
                if (bot == null) continue;
                var neutralZones = MapManager.Instance.NeutralZones;
                if (neutralZones.Count > 0)
                    MoveBotToZoneOrPeek(bot, neutralZones[Random.Range(0, neutralZones.Count)]);
            }

            if (OtherTeam != null)
                OtherTeam.NotifyDefendersAboutRotate(targetSite);
            
            // Назначаем цели
            _isMovingToSite = false;
        }
    }

    private void CheckForRotation()
    {
        if (Bots.Count == 1 && Bots[0] != null && Bots[0].Role != BotRole.Attacker)
            Bots[0].AssignRole(BotRole.Attacker, targetSite);

        var enemyBots = OtherTeam != null ? OtherTeam.Bots : null;
        int enemyAlive = 0;
        if (enemyBots != null)
        {
            for (int i = 0; i < enemyBots.Count; i++)
                if (enemyBots[i] != null) enemyAlive++;
        }

        if (enemyAlive - Bots.Count < _rotationThreshold || _isRotated) return;
        _isRotated = true;
        foreach (var bot in Bots)
        {
            if (bot != null) bot.AssignRole(BotRole.Attacker);
        }
        ChooseTargetSite();
    }

    private void CheckOnPosition()
    {
        if (_isMovingToSite || targetSite == null) return;

        for (int i = 0; i < Bots.Count; i++)
        {
            BotComponent bot = Bots[i];
            if (bot == null) return; // ещё не зачищено — пропустим этот кадр
            if (!bot.IsOnPosition) return;
        }

        _isMovingToSite = true;
        foreach (var bot in Bots)
        {
            if (bot == null) continue;
            if (bot.Role is BotRole.Attacker or BotRole.Flanker)
            {
                // На сайте Hold-слоты "развёрнуты на вход" (как у защитников),
                // но это ровно те углы, которые атакеру и нужно занять после захода:
                // встать у cover-а сайта, смотреть наружу от cover-а — это естественная
                // постплант-позиция. Если все Hold-слоты в сайте уже заняты (например,
                // приехали раньше товарищей) — фоллбэк на случайную точку сайта.
                if (!bot.TryMoveToTacticalSlot(targetSite, TacticalSlotKind.HoldDefender))
                    bot.MoveToZone(targetSite);
            }
        }
    }

    // Helper: попробовать занять Peek-слот в зоне, иначе обычное MoveToZone.
    // Peek-слоты живут на дорогах/нейтрале, Hold-слоты — внутри сайтов/комнат.
    // Поэтому пробуем Peek; если зона — сайт, Peek-слотов там нет и сразу идёт фоллбэк.
    private static void MoveBotToZoneOrPeek(BotComponent bot, MapZoneComponent zone)
    {
        if (bot == null || zone == null) return;
        if (bot.TryMoveToTacticalSlot(zone, TacticalSlotKind.PeekAttacker)) return;
        bot.MoveToZone(zone);
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

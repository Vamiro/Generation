using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class DefenderTeamManager : TeamManager
{
    [Header("Состав команды")]
    [SerializeField, Min(2), Tooltip("Минимум Defender на КАЖДОМ сайте. Лишний бот (если есть) — преимущественно Scout, реже +1 Defender на случайный сайт.")]
    private int minDefendersPerSite = 2;
    [SerializeField, Range(0f, 1f), Tooltip("Вероятность, что «лишний» бот станет +1 Defender на сайт (иначе Scout). По умолчанию низкая — в основном Scout.")]
    private float extraDefenderOnSiteProbability = 0.25f;

    [Header("Reposition-тик")]
    [SerializeField, Min(0f), Tooltip("Период reposition-тика (сек). Чаще = более суетливые защитники, реже = пассивные хольдеры.")]
    private float repositionDelay = 18f;
    [SerializeField, Range(0f, 1f), Tooltip("Вероятность выйти на main/link дорогу за один reposition-тик. Низкое значение = большинство матчей защитники держат сайт.")]
    private float pushToRoadProbability = 0.15f;
    [SerializeField, Range(0f, 1f), Tooltip("Из тех, кто выходит на дорогу: вероятность выбрать Main vs Link. 1 = всегда Main, 0 = всегда Link.")]
    private float roadPickMainBias = 0.67f;

    [Header("Агрессивность защиты")]
    [SerializeField, Tooltip("Сколько защитников каждого сайта СРАЗУ на старте выходят на main вместо site-hold. Геометрический параметр: определяет, как часто атакеры встречают защитника на дороге vs на сайте. На каждый матч и на каждый сайт берётся случайное целое из [min, max]. На сайте всегда остаётся минимум 1 защитник.")]
    private IntRange initialPushCount = new IntRange(0, 1);

    [Header("Scout-поведение")]
    [SerializeField, Range(0f, 1f), Tooltip("Вероятность, что Scout пойдёт в нейтральную зону (vs случайная дорога). 0.5 = поровну.")]
    private float scoutNeutralProbability = 0.5f;

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

        PushDefendersTowardSite();

        if (!_isMovingToSite) return;
        if (MapManager.Instance == null || MapManager.Instance.SiteZones.Count == 0)
            return;

        _currentTime += Time.deltaTime;
        if (!(_currentTime >= repositionDelay)) return;
        _isMovingToSite = false;
        _currentTime = 0;

        // Reposition по сайтам — split списка ботов пополам как раньше, без хардкода индексов.
        var sites = MapManager.Instance.SiteZones;
        int half = Bots.Count / 2;
        TryRepositionDefenders(0, half, sites[0]);
        var secondSite = sites.Count > 1 ? sites[1] : sites[0];
        TryRepositionDefenders(half, Bots.Count, secondSite);
    }

    private void AssignRoles()
    {
        if (MapManager.Instance.SiteZones.Count == 0)
        {
            Debug.LogWarning("DefenderTeamManager: отсутствуют Site-зоны.");
            return;
        }

        var sites = MapManager.Instance.SiteZones;
        int n = Bots.Count;
        int baseline = sites.Count * minDefendersPerSite;

        int scoutsToAssign = 0;
        int defendersOnSites = n;
        if (n > baseline)
        {
            if (Random.value < extraDefenderOnSiteProbability)
                defendersOnSites = n;
            else
            {
                scoutsToAssign = n - baseline;
                defendersOnSites = baseline;
            }
        }

        int[] perSite = ComputeDefendersDistribution(sites, defendersOnSites, minDefendersPerSite);

        int botIndex = 0;
        for (int siteIdx = 0; siteIdx < sites.Count; siteIdx++)
        {
            int count = perSite[siteIdx];
            int pushCount = RollInitialPushCount(count);

            for (int i = 0; i < count && botIndex < Bots.Count; i++, botIndex++)
            {
                bool shouldPush = i < pushCount;
                if (shouldPush)
                    AssignRoleAndPushToMain(botIndex, sites[siteIdx]);
                else
                    AssignRoleSafe(botIndex, BotRole.Defender, sites[siteIdx]);
            }
        }

        for (int s = 0; s < scoutsToAssign && botIndex < Bots.Count; s++, botIndex++)
            AssignScout(botIndex);
    }

    private void AssignScout(int index)
    {
        if (index < 0 || index >= Bots.Count) return;
        BotComponent scoutBot = Bots[index];
        if (scoutBot == null) return;

        scoutBot.AssignRole(BotRole.Scout, initialZone: null);

        bool goNeutral = Random.value < scoutNeutralProbability;
        if (goNeutral)
        {
            if (MapManager.Instance.NeutralZones.Count > 0)
                scoutBot.MoveToZone(MapManager.Instance.NeutralZones[Random.Range(0, MapManager.Instance.NeutralZones.Count)]);
            else if (MapManager.Instance.RoadZones.Count > 0)
                scoutBot.MoveToZone(MapManager.Instance.RoadZones[Random.Range(0, MapManager.Instance.RoadZones.Count)]);
        }
        else if (MapManager.Instance.RoadZones.Count > 0)
        {
            scoutBot.MoveToZone(MapManager.Instance.RoadZones[Random.Range(0, MapManager.Instance.RoadZones.Count)]);
        }
    }

    // Сначала minPerSite на каждый сайт, остаток — по defense-весам (largest-remainder).
    private int[] ComputeDefendersDistribution(
        IList<SiteZoneComponent> sites,
        int totalDefenders,
        int minPerSite)
    {
        int siteCount = sites.Count;
        int[] result = new int[siteCount];
        if (siteCount == 0 || totalDefenders <= 0) return result;

        int baseline = siteCount * minPerSite;
        if (totalDefenders < baseline)
        {
            for (int i = 0; i < siteCount; i++)
                result[i] = totalDefenders / siteCount;
            int rem = totalDefenders % siteCount;
            for (int i = 0; i < rem; i++)
                result[i]++;
            return result;
        }

        for (int i = 0; i < siteCount; i++)
            result[i] = minPerSite;

        int extra = totalDefenders - baseline;
        if (extra <= 0) return result;

        float totalWeight = 0f;
        for (int i = 0; i < siteCount; i++)
            totalWeight += Mathf.Max(0f, sites[i].GetWeight(BotRole.Defender));

        if (totalWeight <= 0f)
        {
            for (int e = 0; e < extra; e++)
                result[e % siteCount]++;
            return result;
        }

        float[] exact = new float[siteCount];
        int assigned = 0;
        for (int i = 0; i < siteCount; i++)
        {
            float w = Mathf.Max(0f, sites[i].GetWeight(BotRole.Defender));
            exact[i] = extra * (w / totalWeight);
            int add = Mathf.FloorToInt(exact[i]);
            result[i] += add;
            assigned += add;
        }

        int leftoverCount = extra - assigned;
        float[] frac = new float[siteCount];
        for (int i = 0; i < siteCount; i++)
            frac[i] = exact[i] - Mathf.FloorToInt(exact[i]);

        while (leftoverCount > 0)
        {
            int bestIdx = 0;
            float bestFrac = -1f;
            for (int i = 0; i < siteCount; i++)
                if (frac[i] > bestFrac) { bestFrac = frac[i]; bestIdx = i; }
            result[bestIdx]++;
            frac[bestIdx] -= 1f;
            leftoverCount--;
        }

        return result;
    }

    private void AssignRoleSafe(int index, BotRole role, MapZoneComponent zone)
    {
        if (index < 0 || index >= Bots.Count) return;
        BotComponent bot = Bots[index];
        if (bot == null) return;

        // Простой контракт: задаём роль + отправляем в случайную точку зоны.
        // Раньше тут была попытка занять TacticalSlot (HoldDefender) — удалено,
        // т.к. геометрия зоны сама даёт распределение позиций при многих матчах,
        // и слоты только маскировали зависимость winrate от формы зоны (см. §0).
        bot.AssignRole(role, initialZone: zone);
    }

    // Случайное число защитников сайта, выдвигаемых сразу на main. Защита от
    // "вышли все" — оставляем минимум 1 бота на site-hold (clamp к count - 1).
    private int RollInitialPushCount(int defendersOnSite)
    {
        if (defendersOnSite <= 1) return 0;
        int roll = initialPushCount.Clamped(0, defendersOnSite - 1).Random();
        return roll;
    }

    // Агрессивный аналог AssignRoleSafe: отправляем защитника сразу на main-дорогу,
    // ведущую к этому сайту. Геометрический эффект: атакеры встречают защитника
    // раньше, чем дойдут до сайта. Если дороги нет — фоллбэк на site-hold.
    private void AssignRoleAndPushToMain(int index, MapZoneComponent siteZone)
    {
        if (index < 0 || index >= Bots.Count) return;
        BotComponent bot = Bots[index];
        if (bot == null) return;

        MapZoneComponent main = FindPreferredRoad(siteZone, RoadType.Main);
        if (main == null)
        {
            AssignRoleSafe(index, BotRole.Defender, siteZone);
            return;
        }

        bot.AssignRole(BotRole.Defender, main, MarchGoal(siteZone));
    }

    // Симметрия с PushMainAttackersTowardSite: конец дороги к сайту → занять Site.
    private void PushDefendersTowardSite()
    {
        for (int i = 0; i < Bots.Count; i++)
        {
            BotComponent bot = Bots[i];
            if (bot == null || bot.Role != BotRole.Defender) continue;

            if (bot.CurrentZone is not RoadZoneComponent road || road.roadToSite == null)
                continue;

            SiteZoneComponent site = road.roadToSite;
            if (bot.IsInsideZone(site))
            {
                bot.HoldZone(site);
                continue;
            }

            if (bot.CurrentZone == site) continue;

            if (bot.IsOnPosition)
                bot.MoveToZone(site, MarchGoal(site));
        }
    }

    private void TryRepositionDefenders(int start, int end, MapZoneComponent siteZone)
    {
        int clampedEnd = Mathf.Min(end, Bots.Count);
        for (var i = start; i < clampedEnd; i++)
        {
            BotComponent bot = Bots[i];
            if (bot == null) continue;

            // Простая бинарная развилка: с вероятностью pushToRoadProbability
            // выходим на дорогу (Main или Link по roadPickMainBias), иначе — остаёмся
            // (idle-reposition внутри сайта продолжится сам в BotComponent).
            if (Random.value >= pushToRoadProbability) continue;

            RoadType pickType = Random.value < roadPickMainBias ? RoadType.Main : RoadType.Link;
            MapZoneComponent road = FindPreferredRoad(siteZone, pickType);
            if (road == null) continue;

            bot.MoveToZone(road, MarchGoal(siteZone));
        }
    }

    private static Vector3 MarchGoal(MapZoneComponent zone) =>
        zone != null ? zone.transform.position : Vector3.zero;

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

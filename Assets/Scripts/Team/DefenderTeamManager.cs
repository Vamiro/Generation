using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class DefenderTeamManager : TeamManager
{
    [Header("Состав команды")]
    [SerializeField, Min(1), Tooltip("Сколько защитников держат КАЖДЫЙ сайт. Если defense-веса у сайтов в MapManager разные — будет применено как пропорция (см. AssignRoles).")]
    private int defendersPerSite = 2;
    [SerializeField, Min(0), Tooltip("Сколько ботов идут в Scout (разведка нейтрали или дороги). Остаток после распределения по сайтам.")]
    private int scoutCount = 1;

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

        // Распределение защитников по сайтам по defense-весу из MapManager.
        // Если все веса = 0 → равное распределение по defendersPerSite на каждый сайт.
        int totalDefenders = Mathf.Max(0, Bots.Count - scoutCount);
        int[] perSite = ComputeDefendersDistribution(sites, totalDefenders);

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

        // Scout(ы) — остаток
        while (botIndex < Bots.Count - 1)
        {
            AssignRoleSafe(botIndex, BotRole.Defender, sites[0]);
            botIndex++;
        }

        while (botIndex < Bots.Count)
        {
            BotComponent scoutBot = Bots[botIndex];
            if (scoutBot != null)
            {
                scoutBot.AssignRole(BotRole.Scout, initialZone: null);

                bool goNeutral = Random.value < scoutNeutralProbability;
                if (goNeutral)
                {
                    if (MapManager.Instance.NeutralZones.Count > 0)
                        scoutBot.MoveToZone(MapManager.Instance.NeutralZones[Random.Range(0, MapManager.Instance.NeutralZones.Count)]);
                    else if (MapManager.Instance.RoadZones.Count > 0)
                        scoutBot.MoveToZone(MapManager.Instance.RoadZones[Random.Range(0, MapManager.Instance.RoadZones.Count)]);
                }
                else
                {
                    if (MapManager.Instance.RoadZones.Count > 0)
                        scoutBot.MoveToZone(MapManager.Instance.RoadZones[Random.Range(0, MapManager.Instance.RoadZones.Count)]);
                }
            }
            botIndex++;
        }
    }

    // Распределение N защитников по списку сайтов пропорционально их defense-весу (из MapManager).
    // Если все веса 0 — поровну (с округлением; остаток уходит первому сайту).
    private int[] ComputeDefendersDistribution(IList<SiteZoneComponent> sites, int totalDefenders)
    {
        int[] result = new int[sites.Count];
        if (sites.Count == 0 || totalDefenders <= 0) return result;

        float totalWeight = 0f;
        for (int i = 0; i < sites.Count; i++)
            totalWeight += Mathf.Max(0f, sites[i].GetWeight(BotRole.Defender));

        if (totalWeight <= 0f)
        {
            int perSite = Mathf.Min(defendersPerSite, totalDefenders / sites.Count);
            for (int i = 0; i < sites.Count; i++) result[i] = perSite;
            int leftover = totalDefenders - perSite * sites.Count;
            if (leftover > 0) result[0] += leftover;
            return result;
        }

        float[] exact = new float[sites.Count];
        int assigned = 0;
        for (int i = 0; i < sites.Count; i++)
        {
            float w = Mathf.Max(0f, sites[i].GetWeight(BotRole.Defender));
            exact[i] = totalDefenders * (w / totalWeight);
            result[i] = Mathf.FloorToInt(exact[i]);
            assigned += result[i];
        }

        // Остатки раздаём сайтам с наибольшей дробной частью (стандартный largest-remainder method).
        int leftoverCount = totalDefenders - assigned;
        float[] frac = new float[sites.Count];
        for (int i = 0; i < sites.Count; i++) frac[i] = exact[i] - Mathf.FloorToInt(exact[i]);
        while (leftoverCount > 0)
        {
            int bestIdx = 0;
            float bestFrac = -1f;
            for (int i = 0; i < sites.Count; i++)
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

        bot.AssignRole(BotRole.Defender, initialZone: main);
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

            bot.MoveToZone(road);
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

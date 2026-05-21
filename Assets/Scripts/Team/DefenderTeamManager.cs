using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class DefenderTeamManager : TeamManager
{
    [Header("Состав команды")]
    [SerializeField, Min(1), Tooltip("Сколько защитников держат КАЖДЫЙ сайт. Если defenseWeight у зон разные — будет применено как пропорция (см. AssignRoles). Раньше было захардкожено 2.")]
    private int defendersPerSite = 2;
    [SerializeField, Min(0), Tooltip("Сколько ботов идут в Scout (разведка нейтрали или дороги). Остаток после распределения по сайтам.")]
    private int scoutCount = 1;

    [Header("Reposition-тик")]
    [SerializeField, Min(0f), Tooltip("Период reposition-тика (сек). Чаще = более суетливые защитники, реже = пассивные хольдеры. Раньше было захардкожено 10.")]
    private float repositionDelay = 10f;
    [SerializeField, Min(0f), Tooltip("Вес выбора Main-дороги при reposition. Сравнивается с linkWeight/stayWeight. Старое поведение ~ 2/1/1 (chance 0|1 из 4 = Main).")]
    private float repositionMainWeight = 2f;
    [SerializeField, Min(0f), Tooltip("Вес выбора Link-дороги при reposition. Старое поведение: 1 (chance 2 из 4).")]
    private float repositionLinkWeight = 1f;
    [SerializeField, Min(0f), Tooltip("Вес 'остаться на сайте' (бот не двигается этим тиком). Старое поведение: 1 (chance 3 из 4).")]
    private float repositionStayWeight = 1f;

    [Header("Scout-поведение")]
    [SerializeField, Range(0f, 1f), Tooltip("Вероятность, что Scout пойдёт в нейтральную зону (vs случайная дорога). 0.5 = поровну.")]
    private float scoutNeutralProbability = 0.5f;

    private bool _isRotated = false;
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

        // Reposition по сайтам — split списка ботов пополам как раньше, но без хардкода индексов.
        // Это сохраняет старое поведение "первая половина reposition вокруг siteA, вторая — siteB".
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

        // Распределение защитников по сайтам по defenseWeight.
        // Если все веса = 0 → равное распределение по defendersPerSite на каждый сайт.
        // Если веса заданы → перераспределяем пропорционально (например, siteA.defenseWeight = 1.5,
        // siteB.defenseWeight = 1.0 → на siteA уйдёт 60% защитников, на siteB — 40%).
        int totalDefenders = Mathf.Max(0, Bots.Count - scoutCount);
        int[] perSite = ComputeDefendersDistribution(sites, totalDefenders);

        int botIndex = 0;
        for (int siteIdx = 0; siteIdx < sites.Count; siteIdx++)
        {
            int count = perSite[siteIdx];
            for (int i = 0; i < count && botIndex < Bots.Count; i++, botIndex++)
                AssignRoleSafe(botIndex, BotRole.Defender, sites[siteIdx]);
        }

        // Scout(ы) — остаток
        while (botIndex < Bots.Count - 1) // оставим минимум 1 для последнего Scout-блока
        {
            // если конфигурация даёт >1 защитника-сверх-плана и scoutCount>1 — назначаем им роль Defender дефолт-сайту
            AssignRoleSafe(botIndex, BotRole.Defender, sites[0]);
            botIndex++;
        }

        // Назначаем Scout-ы и отправляем разведать
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

    // Распределение N защитников по списку сайтов пропорционально их defenseWeight.
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
            // Все веса нулевые — старое поведение: defendersPerSite на каждый сайт, остаток → сайт 0.
            int perSite = Mathf.Min(defendersPerSite, totalDefenders / sites.Count);
            for (int i = 0; i < sites.Count; i++) result[i] = perSite;
            int leftover = totalDefenders - perSite * sites.Count;
            if (leftover > 0) result[0] += leftover;
            return result;
        }

        // Веса заданы — пропорциональное распределение через floor + раздача остатков.
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
        // После присвоения сайту дополнительной единицы — уменьшаем его "виртуальную дробь",
        // чтобы тот же сайт не получил ещё одну на следующем витке.
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

        // Сначала роль (она задаёт скорость и т.п.), потом — попытка занять тактический слот
        // в зоне. Если слотов нет (этап C не нагенерил из-за отсутствия коверов) — фоллбэк
        // на стандартный MoveToZone внутри AssignRole.
        bot.AssignRole(role, initialZone: null);
        if (zone != null && role == BotRole.Defender)
        {
            if (!bot.TryMoveToTacticalSlot(zone, TacticalSlotKind.HoldDefender))
                bot.MoveToZone(zone);
        }
        else if (zone != null)
        {
            bot.MoveToZone(zone);
        }
    }

    private void TryRepositionDefenders(int start, int end, MapZoneComponent siteZone)
    {
        int clampedEnd = Mathf.Min(end, Bots.Count);
        for (var i = start; i < clampedEnd; i++)
        {
            BotComponent bot = Bots[i];
            if (bot == null) continue;

            // Взвешенный выбор: Main / Link / остаться. Старые chance-таблицы (0|1, 2, 3 из 4)
            // заменены на параметры в инспекторе — это даёт калибровщику ручки настройки стиля защиты.
            float totalW = Mathf.Max(0f, repositionMainWeight + repositionLinkWeight + repositionStayWeight);
            if (totalW <= 0f) continue;
            float pick = Random.value * totalW;

            MapZoneComponent road = null;
            if (pick < repositionMainWeight)
                road = FindPreferredRoad(siteZone, RoadType.Main);
            else if (pick < repositionMainWeight + repositionLinkWeight)
                road = FindPreferredRoad(siteZone, RoadType.Link);
            // else: "stay" — пропускаем reposition

            if (road == null) continue;

            // На дороге защитник тоже хочет hold-angle (у стены/cover, смотрит вглубь).
            // Семантически это HoldDefender; но генератор слотов кладёт на дорогах PeekAttacker.
            // Пробуем сначала HoldDefender (вдруг кусок дороги пересекается с зоной с Hold-слотами),
            // потом PeekAttacker (это и есть наш кейс), потом фоллбэк на случайную точку.
            if (bot.TryMoveToTacticalSlot(road, TacticalSlotKind.HoldDefender)) continue;
            if (bot.TryMoveToTacticalSlot(road, TacticalSlotKind.PeekAttacker)) continue;
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

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class AttackerTeamManager : TeamManager
{
    [Header("Состав команды (botsPerTeam у MatchManager)")]
    [SerializeField, Min(2), Tooltip("Минимум Attacker (Main) на матч. Остальные боты случайно делятся между Flanker и Scout.")]
    private int minAttackerCount = 2;

    private int _rolledFlankerCount;
    private int _rolledScoutCount;

    [Header("Тактика")]
    [SerializeField, Min(0), Tooltip("Разница в численности (защитников минус атакующих), при которой команда меняет атакуемый сайт. Меньше = чаще ротируют.")]
    private int rotationThreshold = 2;
    [SerializeField, Min(0f), Tooltip("Если спавн атакеров ближе к сайту ≤ этого расстояния (м) — идут сразу на сайт, без main/link.")]
    private float directToSiteDistance = 35f;

    private bool _isRotated = false;
    private bool _flankersCommitted = false;
    private Vector3 _attackerSpawnCenter;

    public override void Start()
    {
        base.Start();
        CacheAttackerSpawnCenter();
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
        PushMainAttackersTowardSite();
        CheckFlankersOnLink();
    }

    private void AssignRoles()
    {
        int n = Bots.Count;
        int attackers = Mathf.Clamp(minAttackerCount, 2, Mathf.Max(2, n));
        int pool = Mathf.Max(0, n - attackers);

        _rolledScoutCount = Random.Range(0, pool + 1);
        _rolledFlankerCount = pool - _rolledScoutCount;

        var roles = new List<BotRole>(n);
        for (int i = 0; i < attackers; i++) roles.Add(BotRole.Attacker);
        for (int i = 0; i < _rolledFlankerCount; i++) roles.Add(BotRole.Flanker);
        for (int i = 0; i < _rolledScoutCount; i++) roles.Add(BotRole.Scout);

        ShuffleRoles(roles);

        for (int i = 0; i < n; i++)
            AssignRoleSafe(i, roles[i]);
    }

    private static void ShuffleRoles(List<BotRole> roles)
    {
        for (int i = roles.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (roles[i], roles[j]) = (roles[j], roles[i]);
        }
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
            // Выбираем SiteVolume по весу из MapManager.siteAWeights/siteBWeights (если все веса 0 — uniform random).
            targetSite = PickWeightedSite(MapManager.Instance.SiteZones, BotRole.Attacker);

            SendBotsTowardTargetSite();

            _flankersCommitted = false;
        }
        else
        {
            // Выбираем следующую SiteVolume
            var index = MapManager.Instance.SiteZones.IndexOf(targetSite);
            if (index < 0)
                index = 0;

            targetSite = MapManager.Instance.SiteZones[(index + 1) % MapManager.Instance.SiteZones.Count];
            
            SendBotsTowardTargetSite();

            if (OtherTeam != null)
                OtherTeam.NotifyDefendersAboutRotate(targetSite);

            _flankersCommitted = false;
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

        if (HasCapturedTargetSite()) return;
        if (enemyAlive - Bots.Count < rotationThreshold || _isRotated) return;
        _isRotated = true;
        foreach (var bot in Bots)
        {
            if (bot != null) bot.AssignRole(BotRole.Attacker);
        }
        ChooseTargetSite();
    }

    // Main: занять сайт сразу (конец main / уже на site). Не ждём фланкеров.
    private void PushMainAttackersTowardSite()
    {
        if (targetSite == null) return;

        for (int i = 0; i < Bots.Count; i++)
        {
            BotComponent bot = Bots[i];
            if (bot == null || bot.Role != BotRole.Attacker) continue;

            if (bot.IsInsideZone(targetSite))
            {
                bot.HoldZone(targetSite);
                continue;
            }

            if (bot.CurrentZone == targetSite)
                continue;

            if (ShouldGoDirectToSite())
            {
                bot.MoveToZone(targetSite);
                continue;
            }

            if (bot.IsOnPosition && IsOnMainRoadToTarget(bot))
                bot.MoveToZone(targetSite);
        }
    }

    // Если есть фланкеры — ждём только их на Link; затем пускаем на сайт.
    private void CheckFlankersOnLink()
    {
        if (targetSite == null || _flankersCommitted) return;
        if (_rolledFlankerCount <= 0) return;
        if (!AllFlankersOnLink()) return;

        _flankersCommitted = true;
        for (int i = 0; i < Bots.Count; i++)
        {
            BotComponent bot = Bots[i];
            if (bot == null || bot.Role != BotRole.Flanker) continue;

            if (bot.IsInsideZone(targetSite))
                bot.HoldZone(targetSite);
            else
                bot.MoveToZone(targetSite);
        }
    }

    // Сайт «занят»: хотя бы один Attacker/Flanker внутри targetSite — ротации нет.
    private bool HasCapturedTargetSite()
    {
        if (targetSite == null) return false;

        for (int i = 0; i < Bots.Count; i++)
        {
            BotComponent bot = Bots[i];
            if (bot == null) continue;
            if (bot.Role is not (BotRole.Attacker or BotRole.Flanker)) continue;
            if (bot.IsInsideZone(targetSite)) return true;
        }
        return false;
    }

    private bool IsOnMainRoadToTarget(BotComponent bot)
    {
        if (bot.CurrentZone is not RoadZoneComponent road) return false;
        return road.roadType == RoadType.Main && road.roadToSite == targetSite;
    }

    private bool AllFlankersOnLink()
    {
        bool hasFlanker = false;
        for (int i = 0; i < Bots.Count; i++)
        {
            BotComponent bot = Bots[i];
            if (bot == null || bot.Role != BotRole.Flanker) continue;
            hasFlanker = true;

            if (targetSite != null && bot.IsInsideZone(targetSite)) continue;
            if (!bot.IsOnPosition) return false;
        }
        return hasFlanker;
    }

    private void CacheAttackerSpawnCenter()
    {
        var spawns = MapManager.Instance != null ? MapManager.Instance.SpawnZones : null;
        if (spawns == null || spawns.Count == 0)
        {
            _attackerSpawnCenter = Vector3.zero;
            return;
        }

        SpawnZoneComponent attacker = spawns[0];
        for (int i = 1; i < spawns.Count; i++)
        {
            if (spawns[i] != null && spawns[i].transform.position.z < attacker.transform.position.z)
                attacker = spawns[i];
        }
        _attackerSpawnCenter = attacker.transform.position;
    }

    private Vector3 GetSiteGoal() =>
        targetSite != null ? targetSite.transform.position : Vector3.zero;

    private bool ShouldGoDirectToSite() =>
        targetSite != null
        && directToSiteDistance > 0f
        && Vector3.Distance(_attackerSpawnCenter, targetSite.transform.position) <= directToSiteDistance;

    private void SendBotsTowardTargetSite()
    {
        if (targetSite == null) return;

        Vector3 siteGoal = GetSiteGoal();

        foreach (var bot in Bots)
        {
            if (bot == null) continue;

            if (bot.IsInsideZone(targetSite))
            {
                bot.HoldZone(targetSite);
                continue;
            }

            MapZoneComponent dest = ShouldGoDirectToSite()
                ? targetSite
                : bot.Role switch
                {
                    BotRole.Attacker => RoadOrSite(targetSite, RoadType.Main),
                    BotRole.Flanker  => RoadOrSite(targetSite, RoadType.Link),
                    BotRole.Scout    => RoadOrSite(targetSite, RoadType.Link),
                    _              => targetSite
                };

            bot.MoveToZone(dest, siteGoal);
        }
    }

    private MapZoneComponent RoadOrSite(SiteZoneComponent site, RoadType type)
    {
        MapZoneComponent road = FindPreferredRoad(site, type);
        return road != null ? road : site;
    }

    private MapZoneComponent FindPreferredRoad(SiteZoneComponent site, RoadType preferredType)
    {
        var roadZones = MapManager.Instance.RoadZones;
        var exact = roadZones.FirstOrDefault(zone => zone.roadToSite == site && zone.roadType == preferredType);
        if (exact != null) return exact;

        var sameSiteFallback = roadZones.FirstOrDefault(zone => zone.roadToSite == site);
        return sameSiteFallback;
    }

    // Weighted random по зональным весам из MapManager. Если все веса ≤ 0 — uniform random (старое поведение).
    // Используется для выбора атакуемого сайта: на референс-карте можно выставить в MapManager
    // siteAWeights.attack = 1.0, siteBWeights.attack = 1.5 → атакеры в 1.5 раза чаще выбирают B
    // (так регулируется карта-специфичный приоритет атаки).
    private static SiteZoneComponent PickWeightedSite(List<SiteZoneComponent> sites, BotRole role)
    {
        if (sites == null || sites.Count == 0) return null;
        if (sites.Count == 1) return sites[0];

        float total = 0f;
        for (int i = 0; i < sites.Count; i++)
        {
            float w = Mathf.Max(0f, sites[i].GetWeight(role));
            total += w;
        }

        if (total <= 0f)
            return sites[Random.Range(0, sites.Count)];

        float pick = Random.value * total;
        float acc = 0f;
        for (int i = 0; i < sites.Count; i++)
        {
            acc += Mathf.Max(0f, sites[i].GetWeight(role));
            if (pick <= acc) return sites[i];
        }
        return sites[sites.Count - 1];
    }
}

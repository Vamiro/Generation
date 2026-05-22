using System;
using System.Collections.Generic;
using UnityEngine;

public enum MatchSide
{
    Attackers,
    Defenders
}

public enum MatchOutcome
{
    AttackersWin,
    DefendersWin,
    Timeout
}

// JSON-сериализуемый дамп статистики (сохраняется через Storage).
// JsonUtility поддерживает только public-поля + типы с [Serializable].
[Serializable]
public class StatsData : StorageData<StatsData>
{
    public List<MatchRecord> Matches = new();
}

[Serializable]
public class MatchRecord
{
    public int MatchNumber;
    public string Outcome;          // MatchOutcome.ToString()
    public float DurationSeconds;   // игровое время от старта до завершения
    public string AttackerTargetSite; // имя SiteZone (если известно)
    public int AttackersAliveAtEnd;
    public int DefendersAliveAtEnd;
    public int Deaths;              // всего смертей в матче
    public float TimeToFirstBlood;  // -1 если не было убийств
}

/// <summary>
/// Сбор и агрегация метрик матчей. Один экземпляр на сцену (MonoSingleton).
/// MatchManager / BotComponent отправляют события через статические геттеры/MonoSingleton.Instance.
///
/// Хранит:
///   - Глобальные счётчики (всего матчей, побед каждой стороны, ничьих).
///   - Per-match массив MatchRecord (для расчётов avg и для save в Storage).
///   - Per-zone счётчики смертей и per-role счётчики смертей за всё время сбора.
///
/// Лайфцикл: сбрасывается при R-регенерации карты (MapGenerator вызывает ResetAll()).
/// JSON: сохраняется в Storage по StopLoop / OnApplicationQuit.
/// </summary>
public class MatchStatsCollector : MonoSingleton<MatchStatsCollector>
{
    [Header("Поведение")]
    [SerializeField, Tooltip("Автоматически сохранять статистику в Storage по окончании серии и при выходе.")]
    private bool autoSaveOnFinish = true;
    [SerializeField, Tooltip("При R-регенерации карты сбрасывать накопленную статистику.")]
    private bool resetOnMapRegen = true;

    // ─────────────────── Read-only сводка для инспектора ───────────────────
    [Header("Сводка (read-only)")]
    [SerializeField, ReadOnlyInInspector, Tooltip("Всего сыгранных матчей (включая таймауты).")]
    private int matchesPlayed;
    [SerializeField, ReadOnlyInInspector, Tooltip("Матчей, завершившихся победой одной из сторон (таймауты исключены). Только по ним считаются винрейты и средние.")]
    private int decisiveMatches;
    [SerializeField, ReadOnlyInInspector] private int attackerWins;
    [SerializeField, ReadOnlyInInspector] private int defenderWins;
    [SerializeField, ReadOnlyInInspector, Tooltip("Матчи, завершившиеся по таймауту. В статистику винрейтов/средних не входят, просто счётчик.")]
    private int timeouts;
    [SerializeField, ReadOnlyInInspector, Tooltip("% от decisiveMatches (без учёта таймаутов).")]
    private string attackerWinRate = "—";
    [SerializeField, ReadOnlyInInspector, Tooltip("% от decisiveMatches (без учёта таймаутов).")]
    private string defenderWinRate = "—";
    [SerializeField, ReadOnlyInInspector, Tooltip("Среднее по decisiveMatches (таймауты не входят).")]
    private string avgRoundDuration = "—";
    [SerializeField, ReadOnlyInInspector, Tooltip("Среднее по decisiveMatches (таймауты не входят).")]
    private string avgTimeToFirstBlood = "—";
    [SerializeField, ReadOnlyInInspector, Tooltip("Среднее по decisiveMatches (таймауты не входят).")]
    private string avgWinnerSurvivors = "—";

    [Header("Сайты (атакеры)")]
    [SerializeField, ReadOnlyInInspector, TextArea(2, 6)] private string siteAttackStats = "—";

    [Header("Смерти по ролям")]
    [SerializeField, ReadOnlyInInspector] private int deathsAttackerRole;
    [SerializeField, ReadOnlyInInspector] private int deathsFlankerRole;
    [SerializeField, ReadOnlyInInspector] private int deathsScoutRole;
    [SerializeField, ReadOnlyInInspector] private int deathsDefenderRole;

    [Header("Топ зон по смертям")]
    [SerializeField, ReadOnlyInInspector] private string topKillZones = "—";

    // ─────────────────── Внутренние агрегаты ───────────────────
    private readonly List<MatchRecord> _matches = new();
    private readonly Dictionary<string, int> _deathsByZone = new();
    private readonly Dictionary<BotRole, int> _deathsByRole = new();
    // siteName → (attacks, wins). attacks = сколько раз атакер выбрал этот сайт.
    private readonly Dictionary<string, (int attacks, int wins)> _bySite = new();

    private float _firstBloodSecondsThisMatch = -1f;
    private int _deathsThisMatch;

    public int MatchesPlayed => matchesPlayed;
    public int AttackerWins => attackerWins;
    public int DefenderWins => defenderWins;

    public string GetWinRateLogSuffix()
    {
        if (decisiveMatches <= 0)
            return "WR: —";
        return $"WR A={attackerWinRate} D={defenderWinRate}";
    }

    // ─────────────────── API для MatchManager ───────────────────

    public void OnMatchStarted()
    {
        _firstBloodSecondsThisMatch = -1f;
        _deathsThisMatch = 0;
    }

    public void OnMatchEnded(MatchOutcome outcome, float durationSeconds, string attackerTargetSiteName,
        int attackersAliveAtEnd, int defendersAliveAtEnd)
    {
        var record = new MatchRecord
        {
            MatchNumber = _matches.Count + 1,
            Outcome = outcome.ToString(),
            DurationSeconds = durationSeconds,
            AttackerTargetSite = attackerTargetSiteName ?? string.Empty,
            AttackersAliveAtEnd = attackersAliveAtEnd,
            DefendersAliveAtEnd = defendersAliveAtEnd,
            Deaths = _deathsThisMatch,
            TimeToFirstBlood = _firstBloodSecondsThisMatch
        };
        _matches.Add(record);
        matchesPlayed = _matches.Count;

        switch (outcome)
        {
            case MatchOutcome.AttackersWin: attackerWins++; break;
            case MatchOutcome.DefendersWin: defenderWins++; break;
            case MatchOutcome.Timeout: timeouts++; break;
        }

        // Per-site винрейт считаем только по решающим матчам — иначе таймауты
        // раздуют знаменатель (attacks) и winrate сайта станет заниженным.
        if (outcome != MatchOutcome.Timeout && !string.IsNullOrEmpty(attackerTargetSiteName))
        {
            if (!_bySite.TryGetValue(attackerTargetSiteName, out var t))
                t = (0, 0);
            t.attacks++;
            if (outcome == MatchOutcome.AttackersWin) t.wins++;
            _bySite[attackerTargetSiteName] = t;
        }

        RefreshInspector();
    }

    public void OnBotDied(MatchSide side, BotRole role, Vector3 position, float matchTimeSeconds)
    {
        _deathsThisMatch++;
        if (_firstBloodSecondsThisMatch < 0f)
            _firstBloodSecondsThisMatch = matchTimeSeconds;

        if (!_deathsByRole.ContainsKey(role))
            _deathsByRole[role] = 0;
        _deathsByRole[role]++;

        string zoneName = ResolveZoneNameAt(position);
        if (!_deathsByZone.ContainsKey(zoneName))
            _deathsByZone[zoneName] = 0;
        _deathsByZone[zoneName]++;

        RefreshInspectorRoleZoneCounters();
    }

    public void ResetAll()
    {
        _matches.Clear();
        _deathsByZone.Clear();
        _deathsByRole.Clear();
        _bySite.Clear();
        _firstBloodSecondsThisMatch = -1f;
        _deathsThisMatch = 0;

        matchesPlayed = 0;
        decisiveMatches = 0;
        attackerWins = 0;
        defenderWins = 0;
        timeouts = 0;
        RefreshInspector();
    }

    public void SaveToStorage()
    {
        StatsData.Instance.Matches = new List<MatchRecord>(_matches);
        StatsData.Instance.Save();
        Debug.Log($"MatchStatsCollector: статистика сохранена в Storage ({_matches.Count} матчей).");
    }

    // ─────────────────── Unity callbacks ───────────────────

    private void OnApplicationQuit()
    {
        if (autoSaveOnFinish) SaveToStorage();
    }

    public bool ResetOnMapRegen => resetOnMapRegen;
    public bool AutoSaveOnFinish => autoSaveOnFinish;

    // ─────────────────── Helpers ───────────────────

    private string ResolveZoneNameAt(Vector3 worldPos)
    {
        var mapManager = MapManager.Instance;
        if (mapManager == null) return "Unknown";

        MapZoneComponent closest = null;
        float bestSqr = float.MaxValue;

        foreach (MapZoneComponent zone in mapManager.Zones)
        {
            if (zone == null) continue;
            // Проверяем попадание в любой из BoxCollider-сегментов зоны.
            foreach (BoxCollider col in zone.BoxColliders)
            {
                if (col == null) continue;
                if (col.bounds.Contains(new Vector3(worldPos.x, col.bounds.center.y, worldPos.z)))
                    return zone.name;
            }
            // Иначе — ближайший центр (fallback на случай, если бот умер в стене/над укрытием).
            float sqr = (zone.transform.position - worldPos).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                closest = zone;
            }
        }

        return closest != null ? closest.name : "Unknown";
    }

    private void RefreshInspector()
    {
        // Все агрегаты (винрейты, средние) считаем ТОЛЬКО по матчам, завершившимся
        // победой одной из сторон. Таймауты — это «недоигранные» раунды, они искажают
        // и среднюю длительность (всегда == matchTimeout), и винрейты, и avgWinnerSurvivors.
        // Их количество выводится отдельным полем `timeouts`.
        decisiveMatches = attackerWins + defenderWins;

        if (decisiveMatches > 0)
        {
            attackerWinRate = $"{100f * attackerWins / decisiveMatches:F1}%";
            defenderWinRate = $"{100f * defenderWins / decisiveMatches:F1}%";

            float sumDur = 0f, sumWinnerSurv = 0f;
            int firstBloodSamples = 0;
            float sumFirstBlood = 0f;
            foreach (var m in _matches)
            {
                if (m.Outcome == nameof(MatchOutcome.Timeout)) continue;

                sumDur += m.DurationSeconds;
                if (m.Outcome == nameof(MatchOutcome.AttackersWin)) sumWinnerSurv += m.AttackersAliveAtEnd;
                else if (m.Outcome == nameof(MatchOutcome.DefendersWin)) sumWinnerSurv += m.DefendersAliveAtEnd;
                if (m.TimeToFirstBlood >= 0f)
                {
                    sumFirstBlood += m.TimeToFirstBlood;
                    firstBloodSamples++;
                }
            }
            avgRoundDuration = $"{sumDur / decisiveMatches:F1} s";
            avgWinnerSurvivors = $"{sumWinnerSurv / decisiveMatches:F2}";
            avgTimeToFirstBlood = firstBloodSamples > 0
                ? $"{sumFirstBlood / firstBloodSamples:F1} s ({firstBloodSamples}/{decisiveMatches} матчей)"
                : "—";
        }
        else
        {
            attackerWinRate = defenderWinRate = "—";
            avgRoundDuration = avgWinnerSurvivors = avgTimeToFirstBlood = "—";
        }

        siteAttackStats = FormatSiteStats();
        RefreshInspectorRoleZoneCounters();
    }

    private string FormatSiteStats()
    {
        if (_bySite.Count == 0) return "—";

        var keys = new List<string>(_bySite.Keys);
        keys.Sort(StringComparer.Ordinal);

        var sb = new System.Text.StringBuilder();
        foreach (var key in keys)
        {
            var t = _bySite[key];
            float wr = t.attacks > 0 ? 100f * t.wins / t.attacks : 0f;
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(key).Append(": ").Append(t.attacks).Append(" атак, ")
              .Append(t.wins).Append(" побед (").AppendFormat("{0:F1}", wr).Append("%)");
        }
        return sb.ToString();
    }

    private void RefreshInspectorRoleZoneCounters()
    {
        deathsAttackerRole = _deathsByRole.TryGetValue(BotRole.Attacker, out var a) ? a : 0;
        deathsFlankerRole  = _deathsByRole.TryGetValue(BotRole.Flanker,  out var f) ? f : 0;
        deathsScoutRole    = _deathsByRole.TryGetValue(BotRole.Scout,    out var s) ? s : 0;
        deathsDefenderRole = _deathsByRole.TryGetValue(BotRole.Defender, out var d) ? d : 0;

        // Топ-5 зон.
        var pairs = new List<KeyValuePair<string, int>>(_deathsByZone);
        pairs.Sort((x, y) => y.Value.CompareTo(x.Value));
        int take = Mathf.Min(5, pairs.Count);
        if (take == 0)
        {
            topKillZones = "—";
            return;
        }
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < take; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(pairs[i].Key).Append(":").Append(pairs[i].Value);
        }
        topKillZones = sb.ToString();
    }
}

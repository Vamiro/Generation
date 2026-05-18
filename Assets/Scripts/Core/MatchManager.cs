using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Жизненный цикл матча и бесконечный цикл матчей (match loop).
///
/// Состояния:
///   Idle      — цикл выключен. На сцене нет команд/ботов.
///   Running   — идёт активный матч (есть AttackerTeamManager + DefenderTeamManager).
///   Cooldown  — матч закончился (победа/таймаут), ждём betweenMatchesDelay перед новым.
///
/// Хоткеи:
///   W — включить цикл (если был Idle) и запустить первый матч.
///   S — выключить цикл и удалить команды/ботов. Карта не трогается.
///   R — обрабатывается MapGenerator, который перед регенерацией зовёт StopLoop().
///
/// Конец матча:
///   - LiveBotsCount одной из команд == 0 (победа).
///   - elapsed >= matchTimeout (ничья / таймаут).
/// </summary>
public class MatchManager : MonoSingleton<MatchManager>
{
    private enum MatchState { Idle, Running, Cooldown }

    [Header("Префабы")]
    [SerializeField, Tooltip("Префаб бота (должен содержать BotComponent и NavMeshAgent).")]
    private BotComponent botPrefab;

    [Header("Состав")]
    [SerializeField, Min(1), Tooltip("Количество ботов в каждой команде.")]
    private int botsPerTeam = 5;

    [Header("Спавн")]
    [SerializeField, Tooltip("Сэмплировать стартовую позицию на NavMesh (рекомендуется).")]
    private bool snapSpawnToNavMesh = true;
    [SerializeField, Min(0.1f), Tooltip("Радиус поиска точки на NavMesh при сэмплировании.")]
    private float navMeshSampleRadius = 3f;

    [Header("Цикл матчей")]
    [SerializeField, Min(1f), Tooltip("Максимальная длительность одного матча в секундах (по таймауту фиксируется ничья).")]
    private float matchTimeout = 120f;
    [SerializeField, Min(0f), Tooltip("Пауза между матчами в секундах (даёт время увидеть результат).")]
    private float betweenMatchesDelay = 2f;
    [SerializeField, Min(0), Tooltip("Лимит матчей в одной серии. 0 = бесконечный цикл.")]
    private int maxMatches = 0;

    [Header("Состояние (read-only)")]
    [SerializeField, ReadOnlyInInspector, Tooltip("Сколько матчей уже сыграно в текущей серии.")]
    private int matchesPlayed;
    [SerializeField, ReadOnlyInInspector, Tooltip("Текущее состояние цикла.")]
    private string currentState = "Idle";

    private AttackerTeamManager _attackers;
    private DefenderTeamManager _defenders;

    private MatchState _state = MatchState.Idle;
    private bool _loopEnabled;
    private float _stateTimer;

    public bool IsLoopEnabled => _loopEnabled;
    public bool IsMatchActive => _state == MatchState.Running;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.W))
            StartLoop();
        if (Input.GetKeyDown(KeyCode.S))
            StopLoop();

        TickStateMachine();
        currentState = _state.ToString();
    }

    public void StartLoop()
    {
        if (_loopEnabled)
        {
            Debug.Log("MatchManager: цикл уже запущен.");
            return;
        }

        _loopEnabled = true;
        matchesPlayed = 0;
        TryStartMatch();
    }

    /// <summary>
    /// Выключает цикл и убирает команды/ботов со сцены. Карта остаётся нетронутой.
    /// Вызывается по S, а также из MapGenerator перед регенерацией карты (R).
    /// </summary>
    public void StopLoop()
    {
        _loopEnabled = false;
        DestroyTeams();
        _state = MatchState.Idle;
        _stateTimer = 0f;
        Debug.Log("MatchManager: цикл остановлен.");
    }

    // Поведенческий совместимый alias: внешние вызывалки (MapGenerator) ожидают StopMatch.
    public void StopMatch() => StopLoop();

    private void TickStateMachine()
    {
        switch (_state)
        {
            case MatchState.Running:
                _stateTimer += Time.deltaTime;

                int attackersAlive = _attackers != null ? _attackers.LiveBotsCount : 0;
                int defendersAlive = _defenders != null ? _defenders.LiveBotsCount : 0;

                bool timedOut = _stateTimer >= matchTimeout;
                bool oneSideEmpty = attackersAlive == 0 || defendersAlive == 0;

                if (timedOut || oneSideEmpty)
                {
                    string outcome = !oneSideEmpty
                        ? "Timeout (ничья)"
                        : (attackersAlive == 0 ? "Defenders win" : "Attackers win");
                    Debug.Log($"MatchManager: матч #{matchesPlayed + 1} завершён — {outcome}. " +
                              $"A={attackersAlive}, D={defendersAlive}, t={_stateTimer:F1}s");

                    matchesPlayed++;
                    DestroyTeams();
                    _state = MatchState.Cooldown;
                    _stateTimer = 0f;
                }
                break;

            case MatchState.Cooldown:
                if (!_loopEnabled)
                {
                    _state = MatchState.Idle;
                    break;
                }

                _stateTimer += Time.deltaTime;
                if (_stateTimer >= betweenMatchesDelay)
                {
                    if (maxMatches > 0 && matchesPlayed >= maxMatches)
                    {
                        Debug.Log($"MatchManager: серия завершена ({matchesPlayed}/{maxMatches} матчей).");
                        _loopEnabled = false;
                        _state = MatchState.Idle;
                        _stateTimer = 0f;
                    }
                    else
                    {
                        TryStartMatch();
                    }
                }
                break;

            case MatchState.Idle:
            default:
                break;
        }
    }

    private void TryStartMatch()
    {
        if (botPrefab == null)
        {
            Debug.LogError("MatchManager: не назначен botPrefab. Цикл остановлен.");
            _loopEnabled = false;
            _state = MatchState.Idle;
            return;
        }

        var mapManager = MapManager.Instance;
        var spawnZones = mapManager.SpawnZones;
        if (spawnZones == null || spawnZones.Count < 2)
        {
            Debug.LogError("MatchManager: на карте нет двух SpawnZone. Цикл остановлен.");
            _loopEnabled = false;
            _state = MatchState.Idle;
            return;
        }

        if (NavMesh.CalculateTriangulation().vertices.Length == 0)
        {
            Debug.LogError("MatchManager: NavMesh не запечён. Цикл остановлен.");
            _loopEnabled = false;
            _state = MatchState.Idle;
            return;
        }

        // Атакер — спавн с бо́льшим Z (см. MapGenerator.Layout.PlaceSpawnZones).
        SpawnZoneComponent attackerZone, defenderZone;
        if (spawnZones[0].transform.position.z >= spawnZones[1].transform.position.z)
        {
            attackerZone = spawnZones[0];
            defenderZone = spawnZones[1];
        }
        else
        {
            attackerZone = spawnZones[1];
            defenderZone = spawnZones[0];
        }

        _attackers = CreateTeam<AttackerTeamManager>("AttackerTeamManager", attackerZone);
        _defenders = CreateTeam<DefenderTeamManager>("DefenderTeamManager", defenderZone);

        _attackers.SetOtherTeam(_defenders);
        _defenders.SetOtherTeam(_attackers);

        _state = MatchState.Running;
        _stateTimer = 0f;
        Debug.Log($"MatchManager: матч #{matchesPlayed + 1} стартовал. A={_attackers.LiveBotsCount}, D={_defenders.LiveBotsCount}.");
    }

    private void DestroyTeams()
    {
        if (_attackers != null)
        {
            Destroy(_attackers.gameObject);
            _attackers = null;
        }
        if (_defenders != null)
        {
            Destroy(_defenders.gameObject);
            _defenders = null;
        }
    }

    private T CreateTeam<T>(string name, SpawnZoneComponent spawnZone) where T : TeamManager
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var team = go.AddComponent<T>();

        var bots = SpawnBots(spawnZone, go.transform);
        team.SetBots(bots);
        return team;
    }

    private List<BotComponent> SpawnBots(SpawnZoneComponent zone, Transform parent)
    {
        var result = new List<BotComponent>(botsPerTeam);
        for (int i = 0; i < botsPerTeam; i++)
        {
            Vector3 pos = zone.GetRandomPointInZone();
            pos.y = 1f;

            if (snapSpawnToNavMesh)
            {
                if (!NavMesh.SamplePosition(pos, out var hit, navMeshSampleRadius, NavMesh.AllAreas))
                {
                    Debug.LogWarning($"MatchManager: не удалось найти точку на NavMesh рядом с {pos} (radius={navMeshSampleRadius}). Бот пропущен.");
                    continue;
                }
                pos = hit.position;
            }

            var bot = Instantiate(botPrefab, pos, Quaternion.identity, parent);
            result.Add(bot);
        }
        return result;
    }
}

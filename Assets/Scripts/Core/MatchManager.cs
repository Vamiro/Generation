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

    [Header("Скорость симуляции")]
    [SerializeField, Range(0.1f, 50f), Tooltip("Множитель скорости симуляции. Меняется в полёте (слайдер, хоткеи [ / ] / \\). Реальный потолок зависит от CPU.")]
    private float simulationSpeed = 1f;
    [SerializeField, Tooltip("Автоматически масштабировать Time.fixedDeltaTime, чтобы физика оставалась плавной при ускорении. Иначе ускорение увеличит количество FixedUpdate в секунду.")]
    private bool scaleFixedDeltaTime = true;
    [SerializeField, Tooltip("Шаг изменения скорости хоткеями [ / ].")]
    private float simulationSpeedStep = 1f;
    [SerializeField, Tooltip("Включить хоткеи [ / ] / \\ для управления скоростью в Play Mode.")]
    private bool enableSpeedHotkeys = true;

    [Header("Headless-режим (для батч-симуляций)")]
    [SerializeField, Tooltip("Отключает рендер сцены (камеры, vSync) — весь CPU отдаётся симуляции/физике. Включается хоткеем H в Play Mode.")]
    private bool headlessMode;
    [SerializeField, Range(1, 60), Tooltip("Целевой FPS в headless-режиме (Application.targetFrameRate). Меньше = больше игрового времени на каждый рендер-кадр.")]
    private int headlessTargetFps = 10;

    private const float SimSpeedMin = 0.1f;
    private const float SimSpeedMax = 50f;

    private float _appliedSimulationSpeed = -1f;
    private float _baselineFixedDeltaTime;
    private bool _appliedHeadless;
    private int _savedVSyncCount;
    private int _savedTargetFps;

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
    public float CurrentMatchTime => _state == MatchState.Running ? _stateTimer : 0f;

    protected override void Awake()
    {
        base.Awake();
        // Запоминаем дефолтное значение из Project Settings → Time, чтобы корректно
        // масштабировать его при ускорении и восстановить при выключении.
        _baselineFixedDeltaTime = Time.fixedDeltaTime;
        _savedVSyncCount = QualitySettings.vSyncCount;
        _savedTargetFps = Application.targetFrameRate;
    }

    private void OnEnable()
    {
        ApplySimulationSpeed(simulationSpeed, force: true);
        ApplyHeadlessMode(force: true);
    }

    private void OnDisable()
    {
        // Возвращаем нормальный темп — иначе Edit Mode/другие сцены унаследуют ускорение.
        Time.timeScale = 1f;
        Time.fixedDeltaTime = _baselineFixedDeltaTime > 0f ? _baselineFixedDeltaTime : 0.02f;
        RestoreRenderSettings();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Чтобы слайдер в инспекторе реагировал в Play Mode без задержки на Update.
        if (Application.isPlaying)
            ApplySimulationSpeed(simulationSpeed, force: true);
    }
#endif

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.W))
            StartLoop();
        if (Input.GetKeyDown(KeyCode.S))
            StopLoop();

        HandleSpeedHotkeys();
        ApplySimulationSpeed(simulationSpeed);
        ApplyHeadlessMode();

        try
        {
            TickStateMachine();
        }
        catch (System.Exception ex)
        {
            // Не даём одиночному NRE подвесить цикл матчей. Чистим состояние и идём в Cooldown.
            Debug.LogError($"MatchManager: исключение в TickStateMachine — {ex}");
            DestroyTeams();
            _state = MatchState.Cooldown;
            _stateTimer = 0f;
        }

        currentState = _state.ToString();
    }

    private void HandleSpeedHotkeys()
    {
        if (!enableSpeedHotkeys)
            return;

        float step = Mathf.Max(0.01f, simulationSpeedStep);
        if (Input.GetKeyDown(KeyCode.LeftBracket))
            simulationSpeed = Mathf.Max(SimSpeedMin, simulationSpeed - step);
        else if (Input.GetKeyDown(KeyCode.RightBracket))
            simulationSpeed = Mathf.Min(SimSpeedMax, simulationSpeed + step);
        else if (Input.GetKeyDown(KeyCode.Backslash))
            simulationSpeed = 1f;

        if (Input.GetKeyDown(KeyCode.H))
            headlessMode = !headlessMode;
    }

    private void ApplySimulationSpeed(float speed, bool force = false)
    {
        speed = Mathf.Clamp(speed, SimSpeedMin, SimSpeedMax);
        if (!force && Mathf.Approximately(speed, _appliedSimulationSpeed))
            return;

        Time.timeScale = speed;
        if (scaleFixedDeltaTime && _baselineFixedDeltaTime > 0f)
            Time.fixedDeltaTime = _baselineFixedDeltaTime * speed;

        _appliedSimulationSpeed = speed;
    }

    public void SetSimulationSpeed(float speed)
    {
        simulationSpeed = Mathf.Clamp(speed, SimSpeedMin, SimSpeedMax);
        ApplySimulationSpeed(simulationSpeed, force: true);
    }

    // Headless = "не рендерим, дайте CPU физике и логике". Снимаем vSync,
    // ставим низкий target fps, отключаем все камеры в сцене. Включается флагом
    // или хоткеем H. Выход — вернуть всё в исходное состояние из Awake.
    private void ApplyHeadlessMode(bool force = false)
    {
        if (headlessMode)
        {
            // При включённом headless каждый кадр пересинхронизируем targetFrameRate —
            // чтобы изменение headlessTargetFps в инспекторе подхватывалось без рестарта.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Mathf.Clamp(headlessTargetFps, 1, 60);
            if (!_appliedHeadless || force)
                SetCamerasEnabled(false);
            _appliedHeadless = true;
        }
        else if (_appliedHeadless || force)
        {
            RestoreRenderSettings();
            SetCamerasEnabled(true);
            _appliedHeadless = false;
        }
    }

    private void RestoreRenderSettings()
    {
        QualitySettings.vSyncCount = _savedVSyncCount;
        Application.targetFrameRate = _savedTargetFps;
    }

    private void SetCamerasEnabled(bool enabled)
    {
        Camera[] cams = Camera.allCameras;
        for (int i = 0; i < cams.Length; i++)
        {
            if (cams[i] != null)
                cams[i].enabled = enabled;
        }
    }

    public float SimulationSpeed => simulationSpeed;

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

        var stats = MatchStatsCollector.Instance;
        if (stats != null && stats.AutoSaveOnFinish)
            stats.SaveToStorage();

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
                    MatchOutcome outcome;
                    if (!oneSideEmpty) outcome = MatchOutcome.Timeout;
                    else outcome = attackersAlive == 0 ? MatchOutcome.DefendersWin : MatchOutcome.AttackersWin;

                    string attackerSiteName = _attackers != null && _attackers.targetSite != null
                        ? _attackers.targetSite.name
                        : null;

                    var stats = MatchStatsCollector.Instance;
                    if (stats != null)
                        stats.OnMatchEnded(outcome, _stateTimer, attackerSiteName, attackersAlive, defendersAlive);

                    string winRate = stats != null ? stats.GetWinRateLogSuffix() : "WR: —";
                    Debug.Log($"MatchManager: матч #{matchesPlayed + 1} завершён — {outcome}. " +
                              $"A={attackersAlive}, D={defendersAlive}, t={_stateTimer:F1}s, {winRate}");

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

        // Атакер — спавн с МЕНЬШИМ Z, защитник — с бо́льшим. См. MapGenerator.Layout.PlaceSpawnZones:
        //   attackerZ = innerPadding + halfZ              (нижняя кромка карты)
        //   defenderZ = height - 1 - innerPadding - ...   (верхняя кромка карты)
        SpawnZoneComponent attackerZone, defenderZone;
        if (spawnZones[0].transform.position.z <= spawnZones[1].transform.position.z)
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

        // Если хоть одна команда не получила ни одного бота (например, NavMesh.SamplePosition
        // не нашёл точку) — отменяем матч, иначе TickStateMachine сразу зафиксирует чужую победу
        // и серия начнёт бесконечно перезапускать пустые матчи.
        if (_attackers.LiveBotsCount == 0 || _defenders.LiveBotsCount == 0)
        {
            Debug.LogError($"MatchManager: матч не стартовал — пустая команда (A={_attackers.LiveBotsCount}, D={_defenders.LiveBotsCount}). Цикл остановлен.");
            DestroyTeams();
            _loopEnabled = false;
            _state = MatchState.Idle;
            return;
        }

        _attackers.SetOtherTeam(_defenders);
        _defenders.SetOtherTeam(_attackers);

        _state = MatchState.Running;
        _stateTimer = 0f;

        var stats = MatchStatsCollector.Instance;
        if (stats != null) stats.OnMatchStarted();
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

        const float spawnY = 1f;
        const float roofDeltaThreshold = 0.9f; // ~ blockSize; крыша 1-го яруса как раз настолько выше
        const int sampleAttempts = 8;

        for (int i = 0; i < botsPerTeam; i++)
        {
            Vector3 spawnPos = default;
            bool found = false;
            Vector3 lastPos = default;
            float lastHitY = float.NaN;
            int navMeshHits = 0;

            for (int attempt = 0; attempt < sampleAttempts && !found; attempt++)
            {
                Vector3 pos = zone.GetRandomPointInZone();
                pos.y = spawnY;
                lastPos = pos;

                if (!snapSpawnToNavMesh)
                {
                    spawnPos = pos;
                    found = true;
                    break;
                }

                if (!NavMesh.SamplePosition(pos, out var hit, navMeshSampleRadius, NavMesh.AllAreas))
                    continue;

                navMeshHits++;
                lastHitY = hit.position.y;

                // Если снап ушёл вверх больше, чем на высоту одного яруса — это крыша
                // соседней стены/укрытия. Берём другую sample-точку.
                if (hit.position.y - pos.y > roofDeltaThreshold)
                    continue;

                spawnPos = hit.position;
                found = true;
            }

            if (!found)
            {
                Debug.LogWarning(
                    $"MatchManager: не удалось найти точку на NavMesh рядом с {lastPos} в {zone.name} за {sampleAttempts} попыток " +
                    $"(radius={navMeshSampleRadius}, navMeshHits={navMeshHits}, lastHitY={(float.IsNaN(lastHitY) ? "—" : lastHitY.ToString("F2"))}). " +
                    "Бот пропущен.");
                continue;
            }

            var bot = Instantiate(botPrefab, spawnPos, Quaternion.identity, parent);
            result.Add(bot);
        }
        return result;
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

public enum BotRole
{
    Attacker,
    Defender,
    Flanker,
    Scout
}

/// <summary>
/// Минималистичный поведенческий агент. Модель боя сознательно упрощена под задачу
/// ВКР — изучение влияния геометрии карты на winrate (см. Docs/Architecture.md §0).
///
/// Боевой контракт:
///   • HP = 1 — один успешный выстрел = смерть. Это убирает HP-маневрирование
///     и делает каждую дуэль интерпретируемой как "кто первым попал".
///   • После обнаружения врага в LOS копится reactionTime, затем бросок
///     Random &lt; pHit. pHit зависит ТОЛЬКО от дистанции (distance falloff).
///   • Никаких ролевых множителей, штрафов за движение цели, cover-bonus
///     в формуле pHit. Влияние укрытий — через рейкаст LOS (если cover между
///     стрелком и целью, LOS блокируется и бой не начинается).
///
/// Источник модели: Cardamone et al. 2011, Karavolos et al. 2018 — см. §0.
///
/// Поведение вне боя:
///   • Roles + speed по роли (Inspector).
///   • MoveToZone(zone) → бот едет в случайную точку зоны (GetRandomPointInZone).
///   • Idle reposition: периодически меняет точку в зоне, чтобы защитники
///     не "застывали" в одной точке после прибытия.
/// </summary>
public class BotComponent : MonoBehaviour
{
    [Header("Зависимости")]
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private GameObject deathEffect;

    [Header("Роль")]
    [SerializeField, Tooltip("Тактическая роль бота. Задаётся TeamManager-ом в рантайме.")]
    private BotRole role;

    [Header("Бой — реакция и темп стрельбы")]
    [SerializeField, Min(0f), Tooltip("Задержка между выстрелами (сек). Имитирует время реакции + прицеливания. Одинакова для всех ботов — это глобальная константа, источник 'преимущества защитника' (см. §0): защитник на месте уже накопил часть таймера к моменту первой встречи.")]
    private float reactionTime = 0.3f;

    [Header("Модель попадания (5 параметров)")]
    [SerializeField, Range(0f, 1f), Tooltip("Базовая вероятность попадания на близкой дистанции (≤ fullAccuracyRange) без помех. По умолчанию 0.5 — сохраняет старый баланс. Karavolos 2018: одна цифра, общая для всех.")]
    private float baseAccuracy = 0.5f;
    [SerializeField, Min(0f), Tooltip("До какой дистанции (м) точность остаётся максимальной (= baseAccuracy). Дальше — линейный спад.")]
    private float fullAccuracyRange = 8f;
    [SerializeField, Min(0.1f), Tooltip("Максимальная значимая дистанция (м). На ней точность падает до minAccuracyAtMaxRange. За ней — остаётся минимум (не уходим в 0).")]
    private float maxRange = 40f;
    [SerializeField, Range(0f, 1f), Tooltip("Множитель точности на дистанции maxRange и далее. 0.25 = на длинной дистанции попадание в 4 раза реже.")]
    private float minAccuracyAtMaxRange = 0.25f;

    [Header("Бой — здоровье и урон")]
    [SerializeField, Min(1), Tooltip("Здоровье бота. По умолчанию 1: один успешный выстрел = смерть (как в Valorant). Каждая дуэль интерпретируется как 'кто первый попал, тот выиграл' — чище для исследования геометрии.")]
    private int maxHealth = 1;
    [SerializeField, Min(1), Tooltip("Урон за один успешный выстрел.")]
    private int damagePerShot = 1;

    [Header("Прицеливание (визуально + влияние на тайминг)")]
    [SerializeField, Min(0f), Tooltip("Скорость поворота тела к цели (град/сек). Влияет только визуально: после первого обнаружения врага бот доворачивается, и пока не довернулся — может быть в невыгодной позе. Само 'разрешено стрелять' определяется только reactionTime (упрощено относительно прошлой версии с aimAngleTolerance).")]
    private float aimTurnSpeed = 540f;

    [Header("Выбор цели")]
    [SerializeField, Min(0f), Tooltip("Максимальная дистанция, на которой бот вообще замечает врага. 0 = без лимита.")]
    private float sightRange = 60f;
    [SerializeField, Min(0f), Tooltip("Штраф к score за каждый метр дистанции до цели (меньше score = приоритетнее). 0 = дистанция не учитывается.")]
    private float distancePriorityWeight = 1f;
    [SerializeField, Min(0f), Tooltip("Бонус (вычитается из score) для врагов, которые сами уже целятся в нас. Реагируем первым на самого опасного.")]
    private float threatPriorityBonus = 15f;

    [Header("Reposition tick")]
    [SerializeField, Min(0f), Tooltip("Если бот не в бою (нет видимой цели) и стоит на позиции дольше этого времени — пробует новую точку в той же зоне. 0 = никогда.")]
    private float repositionDelay = 6f;
    [SerializeField, Range(0f, 1f), Tooltip("Случайный разброс repositionDelay (±%). Чтобы боты не дёргались синхронно.")]
    private float repositionJitter = 0.4f;

    [Header("Скорости передвижения по ролям")]
    [SerializeField, Min(0f), Tooltip("Скорость NavMeshAgent для роли Attacker (м/с).")]
    private float attackerSpeed = 5f;
    [SerializeField, Min(0f), Tooltip("Скорость NavMeshAgent для роли Defender (м/с).")]
    private float defenderSpeed = 5f;
    [SerializeField, Min(0f), Tooltip("Скорость NavMeshAgent для роли Flanker (м/с).")]
    private float flankerSpeed = 5f;
    [SerializeField, Min(0f), Tooltip("Скорость NavMeshAgent для роли Scout (м/с). Меньше — медленнее на разведке.")]
    private float scoutSpeed = 3.5f;

    private TeamManager _teamManager;
    private BotComponent _target;
    private float _currentReactionTime;
    private MapZoneComponent _currentZone;
    private int _currentHealth;
    private float _idleTimer;
    private float _nextRepositionAt;

    public BotRole Role
    {
        get => role;
        private set => role = value;
    }

    public BotComponent CurrentTarget => _target;

    public void SetAgentSpeed(float speed)
    {
        if (agent != null) agent.speed = speed;
    }

    // remainingDistance валиден только пока агент на NavMesh, путь рассчитан и не в pendingPath.
    public bool IsOnPosition
    {
        get
        {
            if (agent == null || !agent.isOnNavMesh) return false;
            if (agent.pathPending) return false;
            return agent.remainingDistance < 0.01f;
        }
    }

    private void Awake()
    {
        _currentHealth = maxHealth;
        if (agent != null)
        {
            // Поворот мы контролируем сами в FaceTarget(), чтобы бот "целился" в неподвижного
            // врага даже когда NavMeshAgent уже остановился. Иначе агент не разворачивался бы.
            agent.updateRotation = false;
        }
        ScheduleNextReposition();
    }

    public void Init(TeamManager teamManager)
    {
        _teamManager = teamManager;
    }

    private void Update()
    {
        if (_teamManager == null) return;

        if (_target != null)
        {
            // Бой: одновременно доворачиваемся к цели (визуально) и копим reactionTime.
            // Когда таймер выкипел — Shoot(). Стрелять разрешено независимо от того,
            // довернулся ли ствол: ось "выстрел" определяется только таймером и LOS.
            _idleTimer = 0f;
            FaceTarget();

            _currentReactionTime += Time.deltaTime;
            if (_currentReactionTime >= reactionTime)
            {
                _currentReactionTime = 0f;
                Shoot();
            }
        }
        else
        {
            _currentReactionTime = 0f;
            HandleIdleReposition();
        }
    }

    private void FixedUpdate()
    {
        if (_teamManager == null) return;
        TeamManager other = _teamManager.OtherTeam;
        if (other == null) return;

        var enemyBots = other.Bots;
        if (enemyBots == null) return;

        _target = PickBestTarget(enemyBots);
    }

    // Скоринг: чем меньше — тем приоритетнее. База = дистанция * вес,
    // минус бонус, если враг уже целится в нас (его _target == this).
    private BotComponent PickBestTarget(IReadOnlyList<BotComponent> enemyBots)
    {
        BotComponent best = null;
        float bestScore = float.PositiveInfinity;

        for (int i = 0; i < enemyBots.Count; i++)
        {
            BotComponent enemy = enemyBots[i];
            if (enemy == null) continue;

            Vector3 toEnemy = enemy.transform.position - transform.position;
            float dist = toEnemy.magnitude;
            if (sightRange > 0f && dist > sightRange) continue;

            if (!HasLineOfSightTo(enemy)) continue;

            float score = dist * distancePriorityWeight;
            if (enemy.CurrentTarget == this) score -= threatPriorityBonus;

            if (score < bestScore)
            {
                bestScore = score;
                best = enemy;
            }
        }

        if (best != null)
            Debug.DrawLine(transform.position, best.transform.position, Color.red);

        return best;
    }

    // Geometry-aware видимость: LOS блокируется любым коллайдером на пути —
    // стенами, cover-блоками. Это и есть единственный механизм работы укрытий
    // (см. §0). Cover между стрелком и целью → бой не начинается.
    private bool HasLineOfSightTo(BotComponent enemy)
    {
        Vector3 dir = enemy.transform.position - transform.position;
        var ray = new Ray(transform.position, dir);
        if (!Physics.Raycast(ray, out var hit, dir.magnitude + 1f)) return false;
        if (hit.collider == null) return false;
        return hit.collider.gameObject == enemy.gameObject;
    }

    public void MoveToZone(MapZoneComponent zone)
    {
        _currentZone = zone;
        if (zone == null || agent == null || !agent.isOnNavMesh) return;
        agent.SetDestination(zone.GetRandomPointInZone());
        ScheduleNextReposition();
        _idleTimer = 0f;
    }

    public void AssignRole(BotRole newRole, MapZoneComponent initialZone = null)
    {
        Role = newRole;
        SetAgentSpeed(GetSpeedForRole(Role));

        if (initialZone != null)
            MoveToZone(initialZone);
    }

    private float GetSpeedForRole(BotRole r) => r switch
    {
        BotRole.Attacker => attackerSpeed,
        BotRole.Defender => defenderSpeed,
        BotRole.Flanker  => flankerSpeed,
        BotRole.Scout    => scoutSpeed,
        _ => attackerSpeed,
    };

    private void Shoot()
    {
        if (_target == null) return;

        // Финальная проверка LOS — цель могла уйти за угол между FixedUpdate и выстрелом.
        if (!HasLineOfSightTo(_target))
        {
            _target = null;
            return;
        }

        float pHit = ComputeHitChanceAgainst(_target);
        if (Random.value > pHit) return;

        BotComponent victim = _target;
        victim.ReceiveDamage(damagePerShot);
        if (victim == null) _target = null;
    }

    // pHit = baseAccuracy * distanceFalloff(distance).
    // Никаких ролевых множителей, cover-штрафов, штрафов за движение. Источник модели —
    // §0 (Karavolos 2018: одна цифра + falloff по range). Cover влияет геометрически
    // через HasLineOfSightTo — если cover между стрелком и целью, до Shoot() мы не доходим.
    private float ComputeHitChanceAgainst(BotComponent target)
    {
        if (target == null) return 0f;

        float dist = Vector3.Distance(transform.position, target.transform.position);
        float falloff;
        if (maxRange <= fullAccuracyRange || dist <= fullAccuracyRange)
            falloff = 1f;
        else
        {
            float t = Mathf.InverseLerp(fullAccuracyRange, maxRange, dist);
            falloff = Mathf.Lerp(1f, Mathf.Clamp01(minAccuracyAtMaxRange), t);
        }

        return Mathf.Clamp01(baseAccuracy * falloff);
    }

    public void ReceiveDamage(int amount)
    {
        if (amount <= 0) return;
        _currentHealth -= amount;
        if (_currentHealth <= 0)
            Die();
    }

    private void FaceTarget()
    {
        if (_target == null) return;
        Vector3 dir = _target.transform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;

        Quaternion desired = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, aimTurnSpeed * Time.deltaTime);
    }

    private void HandleIdleReposition()
    {
        if (repositionDelay <= 0f) return;
        if (_currentZone == null) return;
        if (agent == null || !agent.isOnNavMesh) return;
        if (!IsOnPosition) { _idleTimer = 0f; return; }

        _idleTimer += Time.deltaTime;
        if (_idleTimer < _nextRepositionAt) return;

        _idleTimer = 0f;
        ScheduleNextReposition();
        agent.SetDestination(_currentZone.GetRandomPointInZone());
    }

    private void ScheduleNextReposition()
    {
        float jitter = Mathf.Clamp01(repositionJitter);
        float k = 1f + Random.Range(-jitter, jitter);
        _nextRepositionAt = Mathf.Max(0.1f, repositionDelay * k);
    }

    private void Die()
    {
        TeamManager team = _teamManager;
        TeamManager other = team != null ? team.OtherTeam : null;

        if (team != null && other != null)
        {
            switch (role)
            {
                case BotRole.Defender:
                    team.NotifyDefenders(other.targetSite);
                    break;
                case BotRole.Attacker:
                    other.NotifyDefenders(team.targetSite);
                    break;
            }
        }

        var gm = GameManager.Instance;
        if (gm != null)
        {
            gm.SaveDeathPosition(transform.position);
            gm.IncreaseDeathCount();
        }

        var stats = MatchStatsCollector.Instance;
        if (stats != null && team != null)
        {
            MatchSide side = team is AttackerTeamManager ? MatchSide.Attackers : MatchSide.Defenders;
            float matchTime = MatchManager.Instance != null ? MatchManager.Instance.CurrentMatchTime : 0f;
            stats.OnBotDied(side, role, transform.position, matchTime);
        }

        if (team != null && team.Bots != null)
            team.Bots.Remove(this);

        if (deathEffect != null)
        {
            var spawnDeathEffect = new Vector3(transform.position.x, 50f, transform.position.z);
            var obj = Instantiate(deathEffect, spawnDeathEffect, Quaternion.identity);
            obj.transform.SetParent(GetOrCreateDeathMarkersRoot(), worldPositionStays: true);
        }
        Destroy(gameObject);
    }

    // Лениво создаёт контейнер "DeathMarkers" в корне сцены. Не child мап-генератора —
    // чтобы метки переживали R-регенерацию карты. И НЕ DontDestroyOnLoad —
    // чтобы не утекали между сценами.
    private static Transform _deathMarkersRoot;
    private static Transform GetOrCreateDeathMarkersRoot()
    {
        if (_deathMarkersRoot != null)
            return _deathMarkersRoot;

        var existing = GameObject.Find("DeathMarkers");
        if (existing != null)
        {
            _deathMarkersRoot = existing.transform;
            return _deathMarkersRoot;
        }

        _deathMarkersRoot = new GameObject("DeathMarkers").transform;
        return _deathMarkersRoot;
    }
}

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
/// Поведенческий агент бота. Этап A целевой модели:
///  - HP + урон (бой больше не one-shot);
///  - ручной поворот к выбранной цели (видно, что бот "целится");
///  - выбор цели по приоритету (ближайший + кто сам нас видит), а не "последний в цикле";
///  - периодическое перемещение внутри текущей зоны, если бот не в бою —
///    чтобы защитники/атакеры не "застывали" в одной случайной точке.
///
/// Все магические значения (reactionTime, точность, HP, частота reposition) вынесены
/// в Inspector как [SerializeField] с русскими [Tooltip]. На этапе E они переедут
/// в ScriptableObject-профиль (BotProfile) — для воспроизводимой автокалибровки (ВКР, разд. 11.1).
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
    [SerializeField, Min(0f), Tooltip("Задержка между выстрелами (сек). Имитирует реакцию + время прицеливания.")]
    private float reactionTime = 0.3f;
    [SerializeField, Range(0f, 1f), Tooltip("Базовая вероятность попадания при идеальных условиях. Этап B заменит её на модель с учётом дистанции/укрытий.")]
    private float chanceToShoot = 0.5f;

    [Header("Бой — здоровье и урон")]
    [SerializeField, Min(1), Tooltip("Здоровье бота. При hp ≤ 0 — Die(). Несколько выстрелов вместо мгновенной смерти убирает one-shot и делает бой более реалистичным.")]
    private int maxHealth = 2;
    [SerializeField, Min(1), Tooltip("Урон за один успешный выстрел.")]
    private int damagePerShot = 1;

    [Header("Прицеливание")]
    [SerializeField, Min(0f), Tooltip("Скорость поворота тела к цели (град/сек). Пока бот не довернулся — стрелять не разрешено.")]
    private float aimTurnSpeed = 540f;
    [SerializeField, Range(0f, 45f), Tooltip("Допуск по углу (градусы), при котором стрельба уже считается \"прицельной\".")]
    private float aimAngleTolerance = 8f;

    [Header("Выбор цели")]
    [SerializeField, Min(0f), Tooltip("Максимальная дистанция, на которой бот вообще замечает врага. 0 = без лимита.")]
    private float sightRange = 60f;
    [SerializeField, Min(0f), Tooltip("Штраф к score за каждый метр дистанции до цели (меньше score = приоритетнее). 0 = дистанция не учитывается.")]
    private float distancePriorityWeight = 1f;
    [SerializeField, Min(0f), Tooltip("Бонус (вычитается из score) для врагов, которые сами уже целятся в нас.")]
    private float threatPriorityBonus = 15f;

    [Header("Reposition tick")]
    [SerializeField, Min(0f), Tooltip("Если бот не в бою (нет видимой цели) и стоит на позиции дольше этого времени — пробует новую точку в той же зоне. 0 = никогда (старое поведение).")]
    private float repositionDelay = 6f;
    [SerializeField, Range(0f, 1f), Tooltip("Случайный разброс repositionDelay (±%). Чтобы боты не дёргались синхронно.")]
    private float repositionJitter = 0.4f;

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
    // Иначе Unity бросает варнинг "GetRemainingDistance can only be called on an active agent...".
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
            // Поворот мы контролируем сами, чтобы можно было "целиться" в неподвижного врага,
            // даже когда NavMeshAgent уже остановился (иначе агент бы не разворачивался).
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
        // Все ссылки проверяем Unity-null-safe (==), потому что MonoBehaviour может
        // быть "фейково" уничтожен между кадрами.
        if (_teamManager == null) return;

        if (_target != null)
        {
            // Бой: целимся, потом стреляем, как только реакция выкипела и ствол смотрит на врага.
            _idleTimer = 0f;
            FaceTarget();

            _currentReactionTime += Time.deltaTime;
            if (_currentReactionTime >= reactionTime && IsAimedAtTarget())
            {
                _currentReactionTime = 0f;
                Shoot();
            }
        }
        else
        {
            // Не в бою: копим idleTimer и периодически меняем точку внутри текущей зоны,
            // чтобы боты не "застывали" в одной случайной точке после первого MoveToZone.
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
    // минус бонус, если враг сам нас сейчас "видит" (его _target == this) —
    // на такого врага реагируем первым, иначе он успеет выстрелить.
    private BotComponent PickBestTarget(IReadOnlyList<BotComponent> enemyBots)
    {
        BotComponent best = null;
        float bestScore = float.PositiveInfinity;

        for (int i = 0; i < enemyBots.Count; i++)
        {
            BotComponent enemy = enemyBots[i];
            // Destroyed-боты могут лежать в списке между смертью и Bots.Remove(this).
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
        SetAgentSpeed(Role == BotRole.Scout ? 3.5f : 5f);

        if (initialZone != null)
        {
            MoveToZone(initialZone);
        }
    }

    private void Shoot()
    {
        if (_target == null) return;

        // На случай, если цель ушла за угол между FixedUpdate-ом и выстрелом —
        // финальная проверка LOS, и тогда сбрасываем цель, чтобы пересчитать в FixedUpdate.
        if (!HasLineOfSightTo(_target))
        {
            _target = null;
            return;
        }

        if (Random.value > chanceToShoot) return;

        BotComponent victim = _target;
        victim.ReceiveDamage(damagePerShot);
        // Цель не обнуляем — если жертва ещё жива, продолжаем стрелять по ней в следующих тиках.
        if (victim == null) _target = null;
    }

    // Внешний API: чтобы Этап B (HitModel) мог наносить рассчитанный урон, минуя дефолтный путь.
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

    private bool IsAimedAtTarget()
    {
        if (_target == null) return false;
        Vector3 dir = _target.transform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return true;
        float angle = Quaternion.Angle(transform.rotation, Quaternion.LookRotation(dir));
        return angle <= aimAngleTolerance;
    }

    private void HandleIdleReposition()
    {
        if (repositionDelay <= 0f) return;
        if (_currentZone == null) return;
        if (agent == null || !agent.isOnNavMesh) return;
        // Пока бот ещё едет к предыдущей цели — не накручиваем таймер.
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
        // Уведомляем оппонента/свою команду — но безопасно: к этому моменту
        // MatchManager мог уже начать DestroyTeams, и любая ссылка может оказаться Unity-null.
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

        // Метрика смерти: сторона определяется по типу TeamManager-а (Attacker vs Defender),
        // время — текущий runtime матча. Если Collector-а нет — молча пропускаем.
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
    // чтобы не утекали между сценами и не засоряли persistent-иерархию.
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

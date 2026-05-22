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
///   • MoveToZone(zone) → точка в зоне; на дороге с goal — от ближайшей точки по waypoints к сайту.
///   • IsOnPosition — в радиусе arrivalRadius от текущей цели, не точное совпадение с клеткой.
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
    [SerializeField, Min(1f), Tooltip("Множитель pHit при реальном холде на Site (стоит, не бежит к точке). Не срабатывает у бегущих в зону — только у остановившихся / HoldZone.")]
    private float siteHoldAccuracyMultiplier = 1.2f;
    [SerializeField, Min(0f), Tooltip("Считаем «стоит на месте» для hold-бонуса, если скорость NavMeshAgent ниже (м/с).")]
    private float holdStationarySpeed = 0.15f;

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

    [Header("Hold на Site — лёгкое перемещение")]
    [SerializeField, Min(0f), Tooltip("Период (сек) между попытками сменить точку внутри Site при hold. 0 = никогда.")]
    private float siteHoldRepositionDelay = 12f;
    [SerializeField, Range(0f, 1f), Tooltip("Вероятность реально пойти в новую точку Site после тика (иначе остаётся на месте).")]
    private float siteHoldRepositionChance = 0.35f;
    [SerializeField, Range(0f, 1f), Tooltip("Разброс siteHoldRepositionDelay (±%).")]
    private float siteHoldRepositionJitter = 0.3f;

    [Header("Скорости передвижения")]
    [SerializeField, Min(0f), Tooltip("Скорость NavMeshAgent для Attacker/Defender/Flanker — одинакова у обеих сторон (баланс только через тактику команд).")]
    private float combatMoveSpeed = 5f;
    [SerializeField, Min(0f), Tooltip("Скорость Scout (м/с). Только тактическая роль разведки.")]
    private float scoutSpeed = 3.5f;

    [Header("Навигация")]
    [SerializeField, Min(0.5f), Tooltip("Точка маршрута считается достигнутой, если бот в этом радиусе (м) от неё — не нужно вставать точно в центр клетки.")]
    private float arrivalRadius = 2f;
    [SerializeField, Min(1f), Tooltip("Шаг между промежуточными точками при движении по дороге (м).")]
    private float roadWaypointStep = 6f;
    [SerializeField, Min(1f), Tooltip("Радиус (м) выбора стартовой точки на дороге вокруг бота — разносит группу, не одна «ближайшая» sample.")]
    private float roadEntryPickRadius = 5f;
    [SerializeField, Range(0.05f, 1f), Tooltip("Доля «передних» sample на дороге для финиша к сайту (случайная среди них, не одна ближайшая к цели).")]
    private float roadForwardPickPortion = 0.4f;

    private TeamManager _teamManager;
    private BotComponent _target;
    private float _currentReactionTime;
    private MapZoneComponent _currentZone;
    private Vector3? _zoneMoveGoal;
    private int _currentHealth;
    private float _idleTimer;
    private float _nextRepositionAt;
    private Vector3 _navDestination;
    private List<Vector3> _waypoints;
    private int _waypointIndex;
    private bool _explicitSiteHold;

    public BotRole Role
    {
        get => role;
        private set => role = value;
    }

    public BotComponent CurrentTarget => _target;
    public MapZoneComponent CurrentZone => _currentZone;

    public bool IsInsideZone(MapZoneComponent zone) =>
        zone != null && zone.ContainsWorldPointXZ(transform.position);

    public void SetAgentSpeed(float speed)
    {
        if (agent != null) agent.speed = speed;
    }

    // Достигли текущей цели NavMesh: близко к _navDestination или remainingDistance ≤ arrivalRadius.
    public bool IsOnPosition
    {
        get
        {
            if (agent == null || !agent.isOnNavMesh) return false;
            if (_waypoints != null && _waypointIndex < _waypoints.Count - 1) return false;
            return HasReachedNavDestination();
        }
    }

    private void Awake()
    {
        _currentHealth = maxHealth;
        _navDestination = transform.position;
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
            TryEarlySiteHold();
            TickRoadWaypoints();
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

    // Удержание Site: сразу случайная точка внутри зоны (не стоим на входе), дальше — редкий siteHoldReposition.
    public void HoldZone(MapZoneComponent zone)
    {
        if (zone == null) return;

        _currentZone = zone;
        _zoneMoveGoal = null;
        _waypoints = null;
        _waypointIndex = 0;
        _idleTimer = 0f;

        if (agent == null || !agent.isOnNavMesh) return;

        bool inside = zone.ContainsWorldPointXZ(transform.position);
        _explicitSiteHold = inside;

        if (inside)
        {
            SetNavDestination(zone.GetRandomPointInZone());
            ScheduleNextSiteHoldReposition();
        }
        else
        {
            _explicitSiteHold = false;
            SetNavDestination(zone.GetRandomPointInZone());
            ScheduleNextReposition();
        }
    }

    public void MoveToZone(MapZoneComponent zone, Vector3? towardGoal = null)
    {
        if (zone == null || agent == null || !agent.isOnNavMesh) return;

        if (towardGoal.HasValue && TryEarlySiteHoldOnMarch()) return;

        _explicitSiteHold = false;
        _currentZone = zone;
        _zoneMoveGoal = towardGoal;
        _waypoints = null;
        _waypointIndex = 0;

        if (zone is SiteZoneComponent && IsInsideZone(zone))
        {
            HoldZone(zone);
            return;
        }

        if (zone is RoadZoneComponent && towardGoal.HasValue)
            BeginRoadMove(zone, towardGoal.Value);
        else
            SetNavDestination(ResolveDestinationInZone(zone));

        ScheduleNextReposition();
        _idleTimer = 0f;
    }

    private void BeginRoadMove(MapZoneComponent road, Vector3 goalWorld)
    {
        _waypoints = road.GetWaypointsToward(
            transform.position,
            goalWorld,
            roadWaypointStep,
            roadEntryPickRadius,
            roadForwardPickPortion);
        TrimLeadingPassedWaypoints();

        if (_waypoints == null || _waypoints.Count == 0)
        {
            SetNavDestination(road.GetRandomForwardPoint(goalWorld, roadForwardPickPortion));
            return;
        }

        _waypointIndex = 0;
        SetNavDestination(_waypoints[0]);
    }

    private void TrimLeadingPassedWaypoints()
    {
        if (_waypoints == null) return;

        while (_waypoints.Count > 1
               && HorizontalSqrDistance(transform.position, _waypoints[0]) <= arrivalRadius * arrivalRadius)
            _waypoints.RemoveAt(0);
    }

    // Зашли на Site, к которому шли по дороге (goal) — hold, как у атаки, так и у защиты.
    private void TryEarlySiteHold()
    {
        TryEarlySiteHoldOnMarch();
    }

    private bool TryEarlySiteHoldOnMarch()
    {
        if (!_zoneMoveGoal.HasValue) return false;

        SiteZoneComponent site = ResolveSiteForMarchGoal(_zoneMoveGoal.Value);
        if (site == null || !IsInsideZone(site)) return false;

        HoldZone(site);
        return true;
    }

    private static SiteZoneComponent ResolveSiteForMarchGoal(Vector3 goalWorld)
    {
        MapManager map = MapManager.Instance;
        if (map == null) return null;

        SiteZoneComponent best = null;
        float bestSq = float.PositiveInfinity;
        var sites = map.SiteZones;
        for (int i = 0; i < sites.Count; i++)
        {
            SiteZoneComponent site = sites[i];
            if (site == null) continue;
            float sq = HorizontalSqrDistance(site.transform.position, goalWorld);
            if (sq < bestSq)
            {
                bestSq = sq;
                best = site;
            }
        }
        return best;
    }

    private void SetNavDestination(Vector3 worldPoint)
    {
        _navDestination = worldPoint;
        agent.SetDestination(worldPoint);
    }

    private bool HasReachedNavDestination()
    {
        if (agent.pathPending) return false;

        if (HorizontalSqrDistance(transform.position, _navDestination) <= arrivalRadius * arrivalRadius)
            return true;

        return !float.IsPositiveInfinity(agent.remainingDistance)
               && agent.remainingDistance <= arrivalRadius;
    }

    private void TickRoadWaypoints()
    {
        if (_waypoints == null || _waypoints.Count == 0) return;
        if (_waypointIndex >= _waypoints.Count - 1) return;
        if (!HasReachedNavDestination()) return;

        _waypointIndex++;
        SetNavDestination(_waypoints[_waypointIndex]);
        _idleTimer = 0f;
    }

    private static float HorizontalSqrDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    private Vector3 ResolveDestinationInZone(MapZoneComponent zone)
    {
        if (zone == null) return transform.position;
        return _zoneMoveGoal.HasValue
            ? zone.GetPointToward(_zoneMoveGoal.Value)
            : zone.GetRandomPointInZone();
    }

    public void AssignRole(BotRole newRole, MapZoneComponent initialZone = null, Vector3? towardGoal = null)
    {
        Role = newRole;
        SetAgentSpeed(GetSpeedForRole(Role));

        if (initialZone == null) return;

        if (initialZone.ContainsWorldPointXZ(transform.position))
            HoldZone(initialZone);
        else if (initialZone is SiteZoneComponent)
            MoveToZone(initialZone);
        else
            MoveToZone(initialZone, towardGoal ?? MarchGoalForZone(initialZone));
    }

    private static Vector3? MarchGoalForZone(MapZoneComponent zone) =>
        zone != null ? zone.transform.position : null;

    private float GetSpeedForRole(BotRole r) =>
        r == BotRole.Scout ? scoutSpeed : combatMoveSpeed;

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

        float pHit = baseAccuracy * falloff;
        if (HasSiteHoldAdvantage())
            pHit *= siteHoldAccuracyMultiplier;
        return Mathf.Clamp01(pHit);
    }

    // Hold-бонус только если бот реально стоит в Site, а не «проезжает» в радиусе arrivalRadius.
    private bool HasSiteHoldAdvantage()
    {
        if (!IsInsideAnySite()) return false;
        if (!IsStationaryForHold()) return false;

        if (_explicitSiteHold) return true;

        return IsOnPosition;
    }

    private bool IsInsideAnySite()
    {
        if (_currentZone is SiteZoneComponent site && IsInsideZone(site))
            return true;

        MapManager map = MapManager.Instance;
        if (map == null) return false;

        Vector3 pos = transform.position;
        var sites = map.SiteZones;
        for (int i = 0; i < sites.Count; i++)
        {
            SiteZoneComponent s = sites[i];
            if (s != null && s.ContainsWorldPointXZ(pos))
                return true;
        }
        return false;
    }

    private bool IsStationaryForHold()
    {
        if (agent == null) return true;
        return agent.velocity.sqrMagnitude <= holdStationarySpeed * holdStationarySpeed;
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
        if (IsSiteHolding())
        {
            HandleSiteHoldReposition();
            return;
        }

        if (repositionDelay <= 0f) return;
        if (_currentZone == null) return;
        if (agent == null || !agent.isOnNavMesh) return;
        if (!IsOnPosition) { _idleTimer = 0f; return; }

        _idleTimer += Time.deltaTime;
        if (_idleTimer < _nextRepositionAt) return;

        _idleTimer = 0f;
        ScheduleNextReposition();

        if (_currentZone is RoadZoneComponent && _zoneMoveGoal.HasValue)
            BeginRoadMove(_currentZone, _zoneMoveGoal.Value);
        else
        {
            _waypoints = null;
            _waypointIndex = 0;
            SetNavDestination(ResolveDestinationInZone(_currentZone));
        }
    }

    private bool IsSiteHolding()
    {
        if (!IsInsideAnySite()) return false;
        if (_explicitSiteHold) return true;
        return _currentZone is SiteZoneComponent;
    }

    private void HandleSiteHoldReposition()
    {
        if (siteHoldRepositionDelay <= 0f) return;
        if (agent == null || !agent.isOnNavMesh) return;
        if (!IsOnPosition) { _idleTimer = 0f; return; }

        _idleTimer += Time.deltaTime;
        if (_idleTimer < _nextRepositionAt) return;

        _idleTimer = 0f;
        ScheduleNextSiteHoldReposition();

        if (Random.value > siteHoldRepositionChance) return;

        MapZoneComponent site = ResolveSiteZoneForHold();
        if (site == null) return;

        _explicitSiteHold = true;
        SetNavDestination(site.GetRandomPointInZone());
    }

    private MapZoneComponent ResolveSiteZoneForHold()
    {
        if (_currentZone is SiteZoneComponent siteZone)
            return siteZone;

        MapManager map = MapManager.Instance;
        if (map == null) return null;

        Vector3 pos = transform.position;
        var sites = map.SiteZones;
        for (int i = 0; i < sites.Count; i++)
        {
            SiteZoneComponent s = sites[i];
            if (s != null && s.ContainsWorldPointXZ(pos))
                return s;
        }
        return null;
    }

    private void ScheduleNextReposition()
    {
        float jitter = Mathf.Clamp01(repositionJitter);
        float k = 1f + Random.Range(-jitter, jitter);
        _nextRepositionAt = Mathf.Max(0.1f, repositionDelay * k);
    }

    private void ScheduleNextSiteHoldReposition()
    {
        float jitter = Mathf.Clamp01(siteHoldRepositionJitter);
        float k = 1f + Random.Range(-jitter, jitter);
        _nextRepositionAt = Mathf.Max(0.1f, siteHoldRepositionDelay * k);
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

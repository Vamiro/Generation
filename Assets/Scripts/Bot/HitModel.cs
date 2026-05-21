using System;
using UnityEngine;

/// <summary>
/// Параметрическая модель попадания. Этап B плана переработки ботов.
///
/// Заменяет старую плоскую константу <see cref="BotComponent"/>.<c>chanceToShoot</c> на
/// функцию <c>p_hit = base * roleAcc * distanceFalloff * (1 - coverPenalty) * (1 - movingPenalty)</c>.
///
/// Закрывает требование ВКР, разд. 3.2 («навыки ботов»):
///   "Боты должны уметь точно стрелять по врагам, учитывая свою точность,
///    которая определяется различными факторами, такими как местность, объекты на пути к цели".
///
/// Все коэффициенты вынесены в <see cref="HitModelConfig"/> (см. ниже). Бот хранит этот конфиг
/// в [SerializeField], на этапе E конфиг переедет в BotProfile ScriptableObject как есть,
/// без переписывания формулы.
///
/// Дефолты подобраны так, чтобы на средней дистанции (~10 м, нет укрытий, цель не бежит)
/// результат ≈ совпадал со старым chanceToShoot = 0.5 — это сохраняет текущий баланс
/// матчей до тех пор, пока пользователь сам не начнёт настраивать профиль.
/// </summary>
public static class HitModel
{
    /// <summary>
    /// Контекст одного выстрела. Заполняется на стороне <see cref="BotComponent"/>.
    /// Сделан как <c>ref struct</c>-замена в виде обычного struct — без аллокаций.
    /// </summary>
    public struct ShotContext
    {
        public Vector3 ShooterPos;
        public Vector3 TargetPos;
        public BotRole ShooterRole;
        /// <summary>Скорость цели (NavMeshAgent.velocity.magnitude). 0 если стоит.</summary>
        public float TargetSpeed;
        /// <summary>Цель «приникла к укрытию» — рядом стена или cover-блок. См. <see cref="CoverProbe"/>.</summary>
        public bool TargetInCover;
    }

    /// <summary>
    /// Вероятность попадания одного выстрела в диапазоне [0..1].
    /// Гарантия: для одних и тех же входов возвращает один и тот же результат — детерминированно,
    /// без вызовов Random. Сам бросок «попал/промахнулся» делает caller через UnityEngine.Random.
    /// </summary>
    public static float ComputeHitChance(in ShotContext ctx, HitModelConfig cfg)
    {
        if (cfg == null) return 0f;

        float pBase = Mathf.Clamp01(cfg.baseAccuracy);
        float pRole = Mathf.Clamp(cfg.GetRoleMultiplier(ctx.ShooterRole), 0f, 4f);

        float dist = Vector3.Distance(ctx.ShooterPos, ctx.TargetPos);
        float pDist = ComputeDistanceFalloff(dist, cfg);

        float pCover = ctx.TargetInCover ? (1f - Mathf.Clamp01(cfg.targetCoverPenalty)) : 1f;

        float pMove = 1f;
        if (cfg.movingSpeedThreshold > 0f && ctx.TargetSpeed > cfg.movingSpeedThreshold)
            pMove = 1f - Mathf.Clamp01(cfg.targetMovingPenalty);

        float p = pBase * pRole * pDist * pCover * pMove;
        return Mathf.Clamp01(p);
    }

    /// <summary>
    /// Линейный фолл-офф: 1.0 в пределах <c>fullAccuracyRange</c>,
    /// дальше линейно убывает до <c>minAccuracyAtMaxRange</c> на <c>maxRange</c>.
    /// За <c>maxRange</c> — остаётся <c>minAccuracyAtMaxRange</c> (не уходим в 0,
    /// чтобы перестрелки через всю карту были возможны, но маловероятны).
    /// </summary>
    private static float ComputeDistanceFalloff(float dist, HitModelConfig cfg)
    {
        if (cfg.maxRange <= cfg.fullAccuracyRange) return 1f;
        if (dist <= cfg.fullAccuracyRange) return 1f;

        float t = Mathf.InverseLerp(cfg.fullAccuracyRange, cfg.maxRange, dist);
        return Mathf.Lerp(1f, Mathf.Clamp01(cfg.minAccuracyAtMaxRange), t);
    }

    /// <summary>
    /// Проверка «цель в укрытии»: смотрит в 4 соседние с целью клетки сетки карты.
    /// Если хотя бы одна — Wall или содержит cover-блок (по <c>MapGenerator.IsCellOccupiedByCover</c>),
    /// цель считается «приникнувшей» (защищена с одного бока).
    ///
    /// Дополнительно требуем, чтобы это укрытие было НЕ на той стороне, откуда стреляют —
    /// иначе бот, стоящий вплотную к стене лицом к нам, всё равно «в укрытии», что неверно.
    /// </summary>
    public static bool IsTargetUsingCover(
        Vector3 shooterPos,
        Vector3 targetPos,
        MapGenerator map)
    {
        if (map == null) return false;

        float cell = map.BlockSize;
        if (cell <= 0f) return false;

        int tx = Mathf.RoundToInt(targetPos.x / cell);
        int tz = Mathf.RoundToInt(targetPos.z / cell);

        // Направление "от цели к стрелку" в клеточных координатах.
        // Сторона укрытия, ПРОТИВОПОЛОЖНАЯ стрелку, нам не помогает — она не блокирует луч.
        Vector3 toShooter = shooterPos - targetPos;
        float ax = Mathf.Abs(toShooter.x);
        float az = Mathf.Abs(toShooter.z);
        int shooterDx = ax > az ? (toShooter.x > 0f ? 1 : -1) : 0;
        int shooterDz = ax > az ? 0 : (toShooter.z > 0f ? 1 : -1);

        int[] dx = { 1, -1, 0, 0 };
        int[] dz = { 0, 0, 1, -1 };
        for (int i = 0; i < 4; i++)
        {
            int nx = tx + dx[i];
            int nz = tz + dz[i];

            // Сосед на стороне стрелка не считаем укрытием — луч пришёл оттуда.
            if (dx[i] == shooterDx && dz[i] == shooterDz) continue;

            if (map.IsCellWall(nx, nz)) return true;
            if (map.IsCellOccupiedByCover(nx, nz)) return true;
        }

        return false;
    }
}

/// <summary>
/// Все коэффициенты модели попадания. Хранится в <see cref="BotComponent"/> как [SerializeField] +
/// <c>[Serializable]</c> — поэтому показывается в инспекторе как раскрывающаяся группа.
/// На этапе E переедет в BotProfile (ScriptableObject) без переписывания.
/// </summary>
[Serializable]
public class HitModelConfig
{
    [Header("Базовая точность")]
    [Range(0f, 1f), Tooltip("База: вероятность попасть в идеальных условиях (близко, без укрытия, цель стоит). Старый chanceToShoot = 0.5 — примерно так и оставлено.")]
    public float baseAccuracy = 0.5f;

    [Header("Множители по ролям")]
    [Range(0f, 2f)] public float attackerMultiplier = 1.0f;
    [Range(0f, 2f)] public float defenderMultiplier = 1.1f;
    [Range(0f, 2f)] public float flankerMultiplier  = 1.0f;
    [Range(0f, 2f)] public float scoutMultiplier    = 0.9f;

    [Header("Дистанция")]
    [Min(0f), Tooltip("Дистанция (м), на которой точность ещё максимальная (множитель = 1).")]
    public float fullAccuracyRange = 8f;
    [Min(0.1f), Tooltip("Максимальная значимая дистанция (м). Дальше — фиксированный минимум точности.")]
    public float maxRange = 40f;
    [Range(0f, 1f), Tooltip("Минимальный множитель точности на дистанции maxRange и далее. 0.2 = на длинной дистанции попадание в 5 раз реже.")]
    public float minAccuracyAtMaxRange = 0.25f;

    [Header("Цель в укрытии")]
    [Range(0f, 1f), Tooltip("Штраф к p_hit, если цель «приникла» к стене/cover-блоку сбоку от линии огня. 0.5 = шанс попадания падает вдвое.")]
    public float targetCoverPenalty = 0.5f;

    [Header("Цель движется")]
    [Min(0f), Tooltip("Порог скорости (м/с), выше которого цель считается «бегущей». 0 = штраф за движение отключён.")]
    public float movingSpeedThreshold = 1.0f;
    [Range(0f, 1f), Tooltip("Штраф к p_hit для бегущей цели. 0.3 = попасть на 30% сложнее.")]
    public float targetMovingPenalty = 0.3f;

    public float GetRoleMultiplier(BotRole role) => role switch
    {
        BotRole.Attacker => attackerMultiplier,
        BotRole.Defender => defenderMultiplier,
        BotRole.Flanker  => flankerMultiplier,
        BotRole.Scout    => scoutMultiplier,
        _ => 1f
    };
}

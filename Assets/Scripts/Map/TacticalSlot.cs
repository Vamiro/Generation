using System;
using UnityEngine;

/// <summary>
/// Тактический слот — именованная точка внутри <see cref="MapZoneComponent"/>, куда
/// можно осмысленно отправить бота, и куда бот будет смотреть после прихода.
///
/// Архитектурно это эквивалент <c>HidingSpot</c>/<c>EncounterSpot</c> из CS:GO
/// (NavMesh, см. cstrike15_src) и Camp-node из YaPB (open-source CS 1.6 bot, MIT).
/// В этих системах геометрия карты компилируется на этапе подготовки в дискретный
/// набор тактических точек, и весь runtime-loop бота работает только с ними —
/// никаких рейкастов "есть ли тут стена" в боевом цикле.
///
/// Источник наполнения для нашего проекта — два независимых пути, которые пишут
/// в один и тот же список зоны:
///   • <see cref="MapGenerator.BuildTacticalSlots"/> — автогенерация по сетке cover/wall
///     (аналог nav_analyze в Source Engine).
///   • <c>TacticalSlotMarker</c> — ручная разметка в сцене для референс-карт без сетки
///     (аналог nav_mark_walkable / YaPB campNode editor).
///
/// POCO, а не MonoBehaviour — слоты в зоне хранятся в списке, лишних GameObject-ов не плодим.
/// </summary>
[Serializable]
public class TacticalSlot
{
    /// <summary>Мировые координаты точки, куда едет бот.</summary>
    public Vector3 worldPos;

    /// <summary>Направление, в которое бот разворачивается после прихода (XZ). Нормализован.
    /// Семантика: бот стоит ЗА cover-ом и смотрит "наружу через него" в эту сторону.
    /// Используется и для <c>HandleIdleFacing</c> (визуальный hold-угол), и для
    /// <see cref="HitModel.IsTargetUsingCover"/> (определение "цель прикрыта от стрелка"
    /// через сравнение этого направления с вектором target→shooter).</summary>
    public Vector3 facingDir;

    public TacticalSlotKind kind;

    /// <summary>Высота укрытия (Full = блокирует выстрел в полный рост, Half = только присевшего).
    /// На Этапе 1 различие пока косметическое: <see cref="HitModel"/> считает Full и Half
    /// одинаково "в укрытии". Поле зарезервировано для будущих этапов и для ручной
    /// настройки на референс-карте (ящики/перила = Half, бочки/стены = Full).</summary>
    public CoverHeight coverHeight = CoverHeight.Full;

    /// <summary>Радиус (м), в пределах которого слот "прикрывает" цель относительно
    /// стрелка (см. <see cref="HitModel.IsTargetUsingCover"/>). По умолчанию ~1.5 м —
    /// чуть больше, чем размер блока сетки, чтобы цель, стоящая ровно на слоте,
    /// считалась прикрытой даже если её точная позиция плавает.</summary>
    public float influenceRadius = 1.5f;

    /// <summary>Насколько хорошо точка прикрыта: сколько сторон (1–4) закрыты стеной/cover-блоком.
    /// Сохраняется автогенератором для будущих эвристик (этап F: предпочтение крайних углов).</summary>
    public int coverScore;

    // Не сериализуется: бронирование в рантайме. Если занят — другой бот этот слот не возьмёт.
    [NonSerialized] public BotComponent occupant;

    public bool IsFree => occupant == null;
}

/// <summary>
/// Типы слотов. Для MVP только Hold и Peek — остальные паттерны (Crossfire, Lurk) —
/// этап F дорожной карты.
/// </summary>
public enum TacticalSlotKind
{
    /// <summary>Защитная позиция внутри зоны (Site/Neutral/Room): сбоку от cover, смотрит на вход в зону.</summary>
    HoldDefender,

    /// <summary>Атакующая позиция: сбоку от cover на дороге/в нейтрале, смотрит вглубь сайта.</summary>
    PeekAttacker,
}

/// <summary>
/// Высота укрытия. Аналог crouch-aware spots в CS NavMesh и YaPB (флаг "Crouch")
/// на Camp-нодах. Влияет на штраф к <c>p_hit</c> в <see cref="HitModel"/>.
/// </summary>
public enum CoverHeight
{
    /// <summary>Полное укрытие: бот в полный рост за ним невидим — например, стена / большой ящик.</summary>
    Full,
    /// <summary>Низкое укрытие: блокирует только присевшего — например, перила, низкий бордюр.</summary>
    Half,
}

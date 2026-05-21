using System;
using UnityEngine;

/// <summary>
/// Тактический слот — именованная точка внутри <see cref="MapZoneComponent"/>, куда
/// можно осмысленно отправить бота, и куда бот будет смотреть после прихода.
///
/// Этап C плана переработки ботов. Закрывает "стоят в случайной точке зоны":
/// вместо <c>GetRandomPointInZone()</c> бот идёт в одну из заранее посчитанных Hold/Peek позиций,
/// привязанных к cover-блокам и стенам — как hold-spot'ы в Valorant.
///
/// POCO, а не MonoBehaviour — слоты в зоне хранятся в списке, лишних GameObject-ов не плодим.
/// </summary>
[Serializable]
public class TacticalSlot
{
    /// <summary>Мировые координаты точки, куда едет бот.</summary>
    public Vector3 worldPos;

    /// <summary>Направление, в которое бот разворачивается после прихода (XZ). Нормализован.</summary>
    public Vector3 facingDir;

    public TacticalSlotKind kind;

    /// <summary>Насколько хорошо точка прикрыта: сколько сторон (1–4) закрыты стеной/cover-блоком.</summary>
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

using System;
using UnityEngine;

/// <summary>Диапазон int значений [min, max] — отображается в одну строку в инспекторе.</summary>
[Serializable]
public struct IntRange
{
    public int min;
    public int max;

    public IntRange(int min, int max)
    {
        this.min = min;
        this.max = Mathf.Max(min, max);
    }

    /// <summary>Случайное целое число в диапазоне [min, max] включительно.</summary>
    public int Random() => UnityEngine.Random.Range(min, max + 1);

    /// <summary>Ограничивает диапазон заданными пределами.</summary>
    public IntRange Clamped(int absMin, int absMax) =>
        new IntRange(Mathf.Clamp(min, absMin, absMax), Mathf.Clamp(max, absMin, absMax));
}

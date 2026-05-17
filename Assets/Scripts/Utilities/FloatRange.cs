using System;
using UnityEngine;

/// <summary>Диапазон float значений [min, max] — отображается в одну строку в инспекторе.</summary>
[Serializable]
public struct FloatRange
{
    public float min;
    public float max;

    public FloatRange(float min, float max)
    {
        this.min = min;
        this.max = Mathf.Max(min, max);
    }

    /// <summary>Случайное значение в диапазоне [min, max).</summary>
    public float Random() => UnityEngine.Random.Range(min, max);

    /// <summary>Линейная интерполяция внутри диапазона.</summary>
    public float Lerp(float t) => Mathf.Lerp(min, max, t);

    /// <summary>Ограничивает диапазон заданными пределами.</summary>
    public FloatRange Clamped(float absMin, float absMax) =>
        new FloatRange(Mathf.Clamp(min, absMin, absMax), Mathf.Clamp(max, absMin, absMax));
}

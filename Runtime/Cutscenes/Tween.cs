using System;
using System.Collections.Generic;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Cutscenes;

public enum Ease
{
    Linear,
    In,
    Out,
    InOut,
    OutStrong,
    OutBack,
}

public static class Easing
{
    public static float Apply(Ease ease, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return ease switch
        {
            Ease.Linear => t,
            Ease.In => t * t,
            Ease.Out => 1f - (1f - t) * (1f - t),
            Ease.InOut => t < 0.5f ? 2f * t * t : 1f - MathF.Pow(-2f * t + 2f, 2f) * 0.5f,
            Ease.OutStrong => 1f - MathF.Pow(1f - t, 3f),
            Ease.OutBack => 1f + 2.70158f * MathF.Pow(t - 1f, 3f) + 1.70158f * MathF.Pow(t - 1f, 2f),
            _ => t,
        };
    }

    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
}

public static class Tween
{
    public static IEnumerator<Wait> For(CoroutineRunner runner, float duration, Ease ease, Action<float> apply)
    {
        if (apply == null) yield break;

        if (duration <= 0f)
        {
            apply(1f);
            yield break;
        }

        apply(0f);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            yield return Wait.NextFrame;
            elapsed += runner.Delta;
            apply(Easing.Apply(ease, MathF.Min(1f, elapsed / duration)));
        }

        apply(1f);
    }
}

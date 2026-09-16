using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PixelCore.Runtime.Animation;

public class AnimationClip
{
    public string Name { get; set; }
    public Texture2D? Texture { get; set; }
    public List<AnimationFrame> Frames { get; } = new();
    public bool Loop { get; set; } = true;

    public string SourceId { get; set; } = "";

    public float PivotX { get; set; } = 0.5f;
    public float PivotY { get; set; } = 1f;

    public float TotalDuration
    {
        get
        {
            float total = 0;
            foreach (var frame in Frames)
                total += frame.Duration;
            return total;
        }
    }

    public AnimationClip(string name)
    {
        Name = name;
    }

    public AnimationClip AddFrame(AnimationFrame frame)
    {
        Frames.Add(frame);
        return this;
    }

    public AnimationClip AddFramesFromRow(int startX, int y, int count, int frameWidth, int frameHeight, float duration = 0.1f)
    {
        for (int i = 0; i < count; i++)
        {
            Frames.Add(AnimationFrame.FromSheet(startX + i, y, frameWidth, frameHeight, duration));
        }
        return this;
    }

    public int GetFrameIndex(float time)
    {
        if (Frames.Count == 0) return 0;

        float totalDuration = TotalDuration;
        if (totalDuration <= 0) return 0;

        if (Loop)
        {
            time = time % totalDuration;
        }
        else
        {
            time = MathF.Min(time, totalDuration - 0.001f);
        }

        float elapsed = 0;
        for (int i = 0; i < Frames.Count; i++)
        {
            elapsed += Frames[i].Duration;
            if (time < elapsed)
                return i;
        }

        return Frames.Count - 1;
    }

    public AnimationFrame GetFrame(float time)
    {
        int index = GetFrameIndex(time);
        return Frames[index];
    }
}

namespace PixelCore.Runtime.Core;

public class TimeControl
{
    public float TimeScale { get; set; } = 1f;

    public bool Paused { get; set; } = false;

    public float GameplayDelta(float rawDelta) => Paused ? 0f : rawDelta * TimeScale;
}

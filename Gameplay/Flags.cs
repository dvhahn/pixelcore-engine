using PixelCore.Runtime.Save;

namespace PixelCore.Gameplay;

public static class Flags
{
    public static class Village
    {
        public static readonly FlagKey StoneTouched = new("village.stoneTouched", FlagLifetime.Day);

        internal static void Touch() => _ = StoneTouched;
    }

    public static void TouchAll()
    {
        Village.Touch();
    }
}

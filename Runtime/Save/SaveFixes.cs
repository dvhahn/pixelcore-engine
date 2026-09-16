using System;
using System.Collections.Generic;

namespace PixelCore.Runtime.Save;

public enum SaveFix
{
}

public static class SaveFixes
{
    public const int MaxFix = 0;

    public static void ApplyAll(SaveData data, List<string> notes)
    {
        if (data.LastAppliedFix > MaxFix)
        {
            notes.Add($"⚠ The save's fix cursor ({data.LastAppliedFix}) is ahead of this build ({MaxFix}) - " +
                      "it may have been saved by a newer version. Migrations are skipped.");
            return;
        }

        for (int fix = data.LastAppliedFix + 1; fix <= MaxFix; fix++)
        {
            Apply(fix, data, notes);
            data.LastAppliedFix = fix;
        }
    }

    private static void Apply(int fix, SaveData data, List<string> notes)
    {
        switch (fix)
        {
            default:
                notes.Add($"⚠ SaveFix {fix}: no implementation (added to the enum with no case) - skipped");
                Console.WriteLine($"[Save] ⚠ SaveFix {fix} has no implementation - add a case to SaveFixes.Apply");
                break;
        }
    }

    internal static int DeclaredMax()
    {
        int max = 0;
        foreach (var v in Enum.GetValues<SaveFix>())
        {
            int n = (int)v;
            if (n > max) max = n;
        }
        return max;
    }
}

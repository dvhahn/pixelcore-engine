using System;
using System.Collections.Generic;

namespace PixelCore.Editor;

internal static class GameLoopGuard
{
    private static readonly HashSet<string> _reported = new();

    internal static int FaultCount { get; private set; }

    internal static int ReportCount { get; private set; }

    internal static bool ShouldReport(string surface, Exception ex)
    {
        FaultCount++;
        string key = surface + "|" + ex.GetType().FullName + "|" + FirstFrame(ex);
        if (!_reported.Add(key)) return false;
        ReportCount++;
        return true;
    }

    internal static void Reset() => _reported.Clear();

    internal static void ResetForTest()
    {
        _reported.Clear();
        FaultCount = 0;
        ReportCount = 0;
    }

    private static string FirstFrame(Exception ex)
    {
        string? st = ex.StackTrace;
        if (string.IsNullOrEmpty(st)) return "";
        int nl = st.IndexOf('\n');
        return (nl < 0 ? st : st.Substring(0, nl)).Trim();
    }
}

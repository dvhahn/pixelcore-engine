using System;

namespace PixelCore.Runtime.Cutscenes;

public sealed class CutsceneHaltException : Exception
{
    public CutsceneHaltException(string message) : base(message) { }
}

public static class CutsceneLog
{
    public static int ArgCount { get; private set; }
    public static int SkipCount { get; private set; }
    public static int HaltCount { get; private set; }

    public static string LastMessage { get; private set; } = "";

    public static void ResetCounters() { ArgCount = SkipCount = HaltCount = 0; LastMessage = ""; }

    public static void Arg(string cutscene, string verb, string param, string detail, string fallback)
    {
        ArgCount++;
        Emit($"[Cutscene] ⚠1 {cutscene} - {verb}({param}) - {detail} -> using {fallback}");
    }

    public static bool Skip(string cutscene, string verb, string param, string detail)
    {
        SkipCount++;
        Emit($"[Cutscene] ⚠2 {cutscene} - {verb}({param}) - {detail} -> skipping this verb");
        return true;
    }

    public static void Halt(string cutscene, string verb, string detail)
    {
        HaltCount++;
        string msg = $"[Cutscene] ✖3 {cutscene} - {verb} - {detail} -> aborting the cutscene";
        Emit(msg);
        throw new CutsceneHaltException(msg);
    }

    private static void Emit(string message)
    {
        LastMessage = message;
        Console.WriteLine(message);
    }
}

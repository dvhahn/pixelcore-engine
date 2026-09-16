using System;
using System.IO;

namespace PixelCore.Runtime.Core;

public static class CrashSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        _pass = 0; _fail = 0;
        Console.WriteLine("=== Crash log self-test ===");

        string? path = null;
        try
        {
            Console.WriteLine("crashtest_marker_A");
            Console.WriteLine("crashtest_marker_B");

            var inner = new InvalidOperationException("inner reason");
            var ex = new ApplicationException("outer reason", inner);
            path = CrashHandler.Write(ex, "injected by test");

            Check("1. the file is created", path != null && File.Exists(path), path ?? "(null)");
            if (path == null || !File.Exists(path)) return;

            string body = File.ReadAllText(path);
            Check("2. the exception type and message are present",
                body.Contains("ApplicationException") && body.Contains("outer reason"));
            Check("* 2. the InnerException chain is preserved (one layer alone loses the real cause)",
                body.Contains("InvalidOperationException") && body.Contains("inner reason"));
            Check("2. the reason (which path it came from) is present", body.Contains("injected by test"));
            Check("* 3. the preceding console lines are present (what it was doing)",
                body.Contains("crashtest_marker_A") && body.Contains("crashtest_marker_B"));

            var probe = new StringWriter();
            var prev = Console.Out;
            Console.SetOut(probe);
            Console.WriteLine("verbatim");
            Console.SetOut(prev);
            Check("* 4. the tee does not alter output", probe.ToString().TrimEnd() == "verbatim",
                probe.ToString());

            var p2 = CrashHandler.Write(null, "no exception object");
            Check("* 5. a null exception still records without throwing (the native-crash path)",
                p2 != null && File.ReadAllText(p2).Contains("no exception object"));

            for (int i = 0; i < 500; i++) CrashHandler.Remember($"flood_{i}");
            var p3 = CrashHandler.Write(new Exception("cap"), "cap check");
            string b3 = File.ReadAllText(p3!);
            Check("6. the tail is capped (older lines are pushed out)",
                !b3.Contains("flood_0") && b3.Contains("flood_499"),
                b3.Contains("flood_0") ? "entry 0 is still present" : "entry 499 is missing");
        }
        finally
        {
            try { if (path != null && File.Exists(path)) File.Delete(path); } catch { }
            Console.WriteLine($"=== Crash: {_pass} passed, {_fail} failed ===");
        }
    }

    private static void Check(string name, bool ok, string? detail = null)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name +
            (ok || detail == null ? "" : $"   [{detail}]"));
        if (ok) _pass++; else _fail++;
    }
}

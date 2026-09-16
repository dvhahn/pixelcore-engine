using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PixelCore.Runtime.Core;

public static class CrashHandler
{
    private const int TailLines = 200;

    private static readonly Queue<string> _tail = new(TailLines + 1);
    private static bool _installed;

    public static string? LastLogPath { get; private set; }

    public static void Install()
    {
        if (_installed) return;
        _installed = true;

        Console.SetOut(new TailWriter(Console.Out));
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write(e.ExceptionObject as Exception, "UnhandledException");
    }

    public static string? Write(Exception? ex, string reason)
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "crash.log");
            var sb = new StringBuilder();
            sb.AppendLine($"# PixelCore crash — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"# reason: {reason}");
            sb.AppendLine();

            if (ex == null) sb.AppendLine("(no exception object - a native crash, or a non-Exception throw)");
            else
            {
                sb.AppendLine(ex.ToString());
            }

            sb.AppendLine();
            sb.AppendLine($"# last {_tail.Count} console lines (what it was doing)");
            foreach (var line in _tail) sb.AppendLine(line);

            File.WriteAllText(path, sb.ToString());
            LastLogPath = path;

            Console.Error.WriteLine($"[Crash] {reason} - written to: {path}");
            return path;
        }
        catch
        {
            return null;
        }
    }

    internal static void Remember(string line)
    {
        lock (_tail)
        {
            _tail.Enqueue(line);
            while (_tail.Count > TailLines) _tail.Dequeue();
        }
    }

    private sealed class TailWriter : TextWriter
    {
        private readonly TextWriter _inner;
        private readonly StringBuilder _cur = new();

        public TailWriter(TextWriter inner) => _inner = inner;
        public override Encoding Encoding => _inner.Encoding;

        public override void Write(char value)
        {
            _inner.Write(value);
            if (value == '\n') { Remember(_cur.ToString().TrimEnd('\r')); _cur.Clear(); }
            else if (_cur.Length < 4096) _cur.Append(value);
        }

        public override void Write(string? value)
        {
            if (value == null) return;
            foreach (char c in value) Write(c);
        }

        public override void Flush() => _inner.Flush();
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ImGuiNET;

namespace PixelCore.Editor;

public enum LogSeverity { Info, Warn, Error }

public static class EditorConsole
{
    private const int Capacity = 500;

    private readonly record struct Entry(DateTime Time, LogSeverity Sev, string Text);

    private static readonly object _lock = new();
    private static readonly List<Entry> _entries = new(Capacity + 1);
    private static readonly List<int> _filtered = new(Capacity);
    private static bool _installed;

    public static int ErrorCount { get; private set; }
    public static int WarnCount { get; private set; }

    public static bool Open;

    public static float Height = 190f;

    private static bool _showInfo = true, _showWarn = true, _showError = true;
    private static float _copyFlash;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        Console.SetOut(new TeeWriter(Console.Out, fromErrorStream: false));
        Console.SetError(new TeeWriter(Console.Error, fromErrorStream: true));
    }

    public static void Add(LogSeverity sev, string text)
    {
        if (text.Contains("Scissor rect and viewport appear not to overlap")) return;

        lock (_lock)
        {
            _entries.Add(new Entry(DateTime.Now, sev, text));
            if (sev == LogSeverity.Error) ErrorCount++;
            else if (sev == LogSeverity.Warn) WarnCount++;

            if (_entries.Count > Capacity)
            {
                var evicted = _entries[0].Sev;
                _entries.RemoveAt(0);
                if (evicted == LogSeverity.Error) ErrorCount--;
                else if (evicted == LogSeverity.Warn) WarnCount--;
            }
        }
    }

    public static void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            ErrorCount = 0;
            WarnCount = 0;
        }
    }

    private static LogSeverity Classify(string text)
    {
        if (text.Contains("0 failed", StringComparison.OrdinalIgnoreCase))
            return LogSeverity.Info;
        if (text.Contains("fail", StringComparison.OrdinalIgnoreCase)
            || text.Contains("error", StringComparison.OrdinalIgnoreCase)
            || text.Contains("exception", StringComparison.OrdinalIgnoreCase))
            return LogSeverity.Error;
        if (text.Contains("warn", StringComparison.OrdinalIgnoreCase))
            return LogSeverity.Warn;
        return LogSeverity.Info;
    }

    public static void DrawDrawer(EditorPrefs prefs, float bottomReserve)
    {
        ImGui.InvisibleButton("##console_resize", new System.Numerics.Vector2(-1, 5));
        bool resizeHovered = ImGui.IsItemHovered();
        bool resizeActive = ImGui.IsItemActive();
        if (resizeHovered || resizeActive) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNS);
        if (resizeActive)
            Height = Math.Clamp(Height - ImGui.GetIO().MouseDelta.Y, 90f, 600f);
        if (ImGui.IsItemDeactivated() && MathF.Abs(prefs.ConsoleHeight - Height) > 0.5f)
        {
            prefs.ConsoleHeight = Height;
            prefs.Save();
        }
        {
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            float midY = (min.Y + max.Y) * 0.5f;
            uint lineColor = resizeActive || resizeHovered
                ? ImGui.GetColorU32(EditorTheme.Accent)
                : ImGui.GetColorU32(ImGuiCol.TableBorderStrong);
            ImGui.GetWindowDrawList().AddLine(
                new System.Numerics.Vector2(min.X, midY),
                new System.Numerics.Vector2(max.X, midY), lineColor, 1f);
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(10, 5));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, EditorTheme.Recessed);
        ImGui.BeginChild("console_drawer", new System.Numerics.Vector2(0, -bottomReserve),
            ImGuiChildFlags.AlwaysUseWindowPadding,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        DrawHeader();
        DrawBody();

        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
    }

    private static void DrawHeader()
    {
        ImGui.PushFont(ImGuiRenderer.SemiBoldFont);
        ImGui.TextDisabled(Icons.Terminal + "  Console");
        ImGui.PopFont();

        if (_copyFlash > 0f)
        {
            _copyFlash -= ImGui.GetIO().DeltaTime;
            ImGui.SameLine(0, 12);
            ImGui.TextColored(EditorTheme.Success, "Copied");
        }

        ImGui.PushFont(ImGuiRenderer.MonoFont);
        string errLabel = $"Errors {ErrorCount}";
        string warnLabel = $"Warnings {WarnCount}";
        const string infoLabel = "Info";
        float chipsW = ImGui.CalcTextSize(errLabel).X + ImGui.CalcTextSize(warnLabel).X
                     + ImGui.CalcTextSize(infoLabel).X + 16 * 3
                     + ImGui.GetTextLineHeight() * 2 + 16 * 2
                     + 8 * 4;
        ImGui.SameLine(ImGui.GetWindowWidth() - chipsW - 10);

        var dimCol = ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled];
        FilterChip(errLabel, ErrorCount > 0 ? EditorTheme.Danger : dimCol, ref _showError);
        ImGui.SameLine(0, 8);
        FilterChip(warnLabel, WarnCount > 0 ? EditorTheme.Accent : dimCol, ref _showWarn);
        ImGui.SameLine(0, 8);
        FilterChip(infoLabel, ImGui.GetStyle().Colors[(int)ImGuiCol.Text], ref _showInfo);
        ImGui.PopFont();

        ImGui.SameLine(0, 8);
        ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0, 0, 0, 0));
        if (ImGui.Button(Icons.Trash + "##console_clear")) Clear();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Clear");
        ImGui.SameLine(0, 4);
        if (ImGui.Button(Icons.Xmark + "##console_close")) Open = false;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Close (Cmd+J)");
        ImGui.PopStyleColor();

        ImGui.Separator();
    }

    private static void FilterChip(string label, System.Numerics.Vector4 color, ref bool on)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, on ? color
            : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.PushStyleColor(ImGuiCol.Button, on ? EditorTheme.ItemBg : new System.Numerics.Vector4(0, 0, 0, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new System.Numerics.Vector2(8, 1));
        if (ImGui.SmallButton(label)) on = !on;
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(2);
    }

    private static void DrawBody()
    {
        ImGui.PushFont(ImGuiRenderer.MonoFont);
        ImGui.BeginChild("console_lines", new System.Numerics.Vector2(0, 0),
            ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);

        bool stickToBottom = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4f;

        lock (_lock)
        {
            _filtered.Clear();
            for (int i = 0; i < _entries.Count; i++)
            {
                var s = _entries[i].Sev;
                if ((s == LogSeverity.Error && _showError)
                    || (s == LogSeverity.Warn && _showWarn)
                    || (s == LogSeverity.Info && _showInfo))
                    _filtered.Add(i);
            }

            if (_filtered.Count == 0)
            {
                ImGui.TextDisabled(_entries.Count == 0 ? "No log entries" : "No entries match the filter");
            }
            else
            {
                ImGuiListClipperPtr clipper;
                unsafe { clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper()); }
                clipper.Begin(_filtered.Count, ImGui.GetTextLineHeightWithSpacing());
                while (clipper.Step())
                {
                    for (int row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
                    {
                        var e = _entries[_filtered[row]];
                        ImGui.BeginGroup();
                        ImGui.TextDisabled(e.Time.ToString("HH:mm:ss"));
                        ImGui.SameLine(0, 10);

                        var col = e.Sev switch
                        {
                            LogSeverity.Error => EditorTheme.Danger,
                            LogSeverity.Warn => EditorTheme.Accent,
                            _ => ImGui.GetStyle().Colors[(int)ImGuiCol.Text],
                        };
                        int tagEnd = e.Sev == LogSeverity.Info && e.Text.StartsWith("[")
                            ? e.Text.IndexOf(']') : -1;
                        if (tagEnd > 0 && tagEnd < 40 && tagEnd + 1 < e.Text.Length)
                        {
                            ImGui.TextDisabled(e.Text.Substring(0, tagEnd + 1));
                            ImGui.SameLine(0, 6);
                            ImGui.PushStyleColor(ImGuiCol.Text, col);
                            ImGui.TextUnformatted(e.Text.Substring(tagEnd + 1).TrimStart());
                            ImGui.PopStyleColor();
                        }
                        else
                        {
                            ImGui.PushStyleColor(ImGuiCol.Text, col);
                            ImGui.TextUnformatted(e.Text);
                            ImGui.PopStyleColor();
                        }
                        ImGui.EndGroup();

                        if (ImGui.IsItemHovered()) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                        if (ImGui.IsItemClicked())
                        {
                            ImGui.SetClipboardText(e.Text);
                            _copyFlash = 1.2f;
                        }
                    }
                }
                clipper.End();
                clipper.Destroy();
            }
        }

        if (stickToBottom)
            ImGui.SetScrollHereY(1.0f);

        ImGui.EndChild();
        ImGui.PopFont();
    }

    private sealed class TeeWriter : TextWriter
    {
        private readonly TextWriter _inner;
        private readonly bool _fromErrorStream;
        private readonly StringBuilder _line = new();

        public TeeWriter(TextWriter inner, bool fromErrorStream)
        {
            _inner = inner;
            _fromErrorStream = fromErrorStream;
        }

        public override Encoding Encoding => _inner.Encoding;

        public override void Write(char value)
        {
            _inner.Write(value);
            Accumulate(value);
        }

        public override void Write(string? value)
        {
            _inner.Write(value);
            if (value == null) return;
            foreach (var c in value) Accumulate(c);
        }

        public override void WriteLine(string? value)
        {
            _inner.WriteLine(value);
            if (value != null)
                foreach (var c in value) Accumulate(c);
            Accumulate('\n');
        }

        public override void Flush() => _inner.Flush();

        private void Accumulate(char c)
        {
            if (c == '\n')
            {
                string text;
                lock (_line)
                {
                    text = _line.ToString();
                    _line.Clear();
                }
                if (text.Length > 0)
                    Add(_fromErrorStream ? LogSeverity.Error : Classify(text), text);
            }
            else if (c != '\r')
            {
                lock (_line) _line.Append(c);
            }
        }
    }
}

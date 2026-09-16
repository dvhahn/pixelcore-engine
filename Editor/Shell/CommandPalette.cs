using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;

namespace PixelCore.Editor;

public class CommandPalette
{
    public sealed record Item(string Label, string Detail, AssetKind? Kind, Action Action);

    private bool _open;
    private bool _justOpened;
    private string _query = "";
    private int _selected;
    private bool _focusInput;
    private Func<IEnumerable<Item>> _source = () => Array.Empty<Item>();

    private List<Item> _items = new();
    private List<Item> _results = new();
    private string _resultsFor = " ";
    private string _shownQuery = "";

    private const int MaxResults = 14;

    public bool IsOpen => _open;

    public void SetSource(Func<IEnumerable<Item>> source) => _source = source;

    public void Open()
    {
        _open = true;
        _justOpened = true;
        _query = "";
        _selected = 0;
        _focusInput = true;
        _items = _source().ToList();
        _resultsFor = " ";
    }

    public void Close() => _open = false;

    public void Draw()
    {
        if (!_open) return;

        var vp = ImGui.GetMainViewport();
        float width = Math.Min(560f, vp.Size.X * 0.6f);
        ImGui.SetNextWindowPos(
            new System.Numerics.Vector2(vp.Pos.X + vp.Size.X * 0.5f, vp.Pos.Y + 80),
            ImGuiCond.Always, new System.Numerics.Vector2(0.5f, 0f));
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(width, 0));

        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize
                  | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoDocking
                  | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoNavInputs;

        if (ImGui.Begin("###commandPalette", flags))
        {
            if (_focusInput)
            {
                ImGui.SetKeyboardFocusHere();
                _focusInput = false;
            }
            ImGui.SetNextItemWidth(-1);
            bool enter = ImGui.InputTextWithHint("##paletteQuery", Icons.Search + "  Search assets and commands...",
                ref _query, 128, ImGuiInputTextFlags.EnterReturnsTrue);
            if (ImGui.IsItemEdited())
            {
                _selected = 0;
            }
            bool inputActive = ImGui.IsItemActive();

            string preedit = inputActive ? ImGuiRenderer.ImePreedit : "";
            var results = Filter(_query + preedit);
            _shownQuery = _query + preedit;
            if (results.Count == 0 && preedit.Length > 0)
            {
                results = Filter(_query);
                _shownQuery = _query;
            }

            int dir = ImGui.IsKeyPressed(ImGuiKey.DownArrow, true) ? 1
                    : ImGui.IsKeyPressed(ImGuiKey.UpArrow, true) ? -1 : 0;
            if (results.Count > 0) _selected = Math.Clamp(_selected + dir, 0, results.Count - 1);
            else _selected = 0;

            ImGui.Separator();

            DrawResults(results, dir != 0);

            ImGui.TextDisabled("up/down move · enter open · esc close");

            bool enterKey = enter || ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter);
            if (enterKey && _selected >= 0 && _selected < results.Count)
                Execute(results[_selected]);
            else if (enter)
                _focusInput = true;
            else if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                Close();

            if (!_justOpened && !ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
                Close();

            if (_open && !inputActive && !_justOpened && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
                _focusInput = true;
        }
        ImGui.End();

        _justOpened = false;
    }

    private void DrawResults(List<Item> results, bool moved)
    {
        float rowH = ImGui.GetTextLineHeight() + 8f;
        float listH = Math.Max(1, Math.Min(results.Count, MaxResults)) * (rowH + 2f) + 4f;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,
            new System.Numerics.Vector2(ImGui.GetStyle().ItemSpacing.X, 2f));
        ImGui.BeginChild("##paletteResults", new System.Numerics.Vector2(0, listH),
            ImGuiChildFlags.None, ImGuiWindowFlags.NoNavInputs);

        if (results.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled(_shownQuery.Length == 0 ? "   Type to search..." : "   No results");
        }

        var clear = new System.Numerics.Vector4(0, 0, 0, 0);
        for (int i = 0; i < results.Count; i++)
        {
            var item = results[i];
            bool on = i == _selected;

            var rowMin = ImGui.GetCursorScreenPos();
            var rowMax = new System.Numerics.Vector2(
                rowMin.X + ImGui.GetContentRegionAvail().X, rowMin.Y + rowH);

            bool hovered = !on && ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(rowMin, rowMax);
            var dl = ImGui.GetWindowDrawList();
            if (on) dl.AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(EditorTheme.AccentSoft), 5f);
            else if (hovered) dl.AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(EditorTheme.ItemBg), 5f);

            ImGui.PushStyleColor(ImGuiCol.Header, clear);
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, clear);
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, clear);
            bool hit = ImGui.Selectable($"##pal_{i}", false, ImGuiSelectableFlags.None,
                new System.Numerics.Vector2(0, rowH));
            ImGui.PopStyleColor(3);

            DrawRow(item, rowMin, rowMax);

            if (on && moved) ImGui.SetScrollHereY();

            if (hit)
            {
                Execute(item);
                break;
            }
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    private void DrawRow(Item item, System.Numerics.Vector2 rowMin, System.Numerics.Vector2 rowMax)
    {
        const float Pad = 8f;
        const float IconW = 22f;

        var dl = ImGui.GetWindowDrawList();
        float lineH = ImGui.GetTextLineHeight();
        float y = rowMin.Y + (rowMax.Y - rowMin.Y - lineH) * 0.5f;
        uint cText = ImGui.GetColorU32(ImGuiCol.Text);
        uint cDim = ImGui.GetColorU32(ImGuiCol.TextDisabled);

        if (item.Kind is AssetKind kind)
            dl.AddText(new System.Numerics.Vector2(rowMin.X + Pad, y),
                ImGui.GetColorU32(AssetKinds.IconColor(kind)), AssetKinds.Icon(kind));
        else
            dl.AddText(new System.Numerics.Vector2(rowMin.X + Pad, y), cDim, Icons.Gear);

        float labelRight = rowMax.X - Pad;
        if (item.Detail.Length > 0)
        {
            ImGui.PushFont(ImGuiRenderer.MonoFont);
            float dw = ImGui.CalcTextSize(item.Detail).X;
            float dy = rowMin.Y + (rowMax.Y - rowMin.Y - ImGui.GetTextLineHeight()) * 0.5f;
            dl.AddText(new System.Numerics.Vector2(rowMax.X - Pad - dw, dy), cDim, item.Detail);
            ImGui.PopFont();
            labelRight = rowMax.X - Pad - dw - 12f;
        }

        float x = rowMin.X + Pad + IconW;
        dl.PushClipRect(new System.Numerics.Vector2(x, rowMin.Y),
                        new System.Numerics.Vector2(Math.Max(x, labelRight), rowMax.Y), true);

        string q = _shownQuery;
        int m = q.Length > 0 ? item.Label.IndexOf(q, StringComparison.OrdinalIgnoreCase) : -1;
        if (m < 0)
        {
            dl.AddText(new System.Numerics.Vector2(x, y), cText, item.Label);
        }
        else
        {
            string pre = item.Label.Substring(0, m);
            string mid = item.Label.Substring(m, q.Length);
            string post = item.Label.Substring(m + q.Length);
            uint cAccent = ImGui.GetColorU32(EditorTheme.Accent);
            dl.AddText(new System.Numerics.Vector2(x, y), cText, pre);
            x += ImGui.CalcTextSize(pre).X;
            dl.AddText(new System.Numerics.Vector2(x, y), cAccent, mid);
            x += ImGui.CalcTextSize(mid).X;
            dl.AddText(new System.Numerics.Vector2(x, y), cText, post);
        }

        dl.PopClipRect();
    }

    private void Execute(Item item)
    {
        Close();
        item.Action();
    }

    private List<Item> Filter(string query)
    {
        if (query == _resultsFor) return _results;
        _results = Search(query);
        _resultsFor = query;
        return _results;
    }

    private List<Item> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return _items
                .OrderBy(i => i.Kind == AssetKind.Scene ? 0 : i.Kind == null ? 1 : 2)
                .Take(MaxResults).ToList();
        }

        return _items
            .Select(i => (item: i, score: Score(i, query)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.item.Label.Length)
            .Take(MaxResults)
            .Select(x => x.item)
            .ToList();
    }

    private static int Score(Item item, string q)
    {
        if (item.Label.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 300;
        if (item.Label.Contains(q, StringComparison.OrdinalIgnoreCase)) return 200;
        if (item.Detail.Contains(q, StringComparison.OrdinalIgnoreCase)) return 100;
        return 0;
    }
}

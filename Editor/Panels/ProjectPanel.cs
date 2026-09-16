using System;
using System.IO;
using System.Collections.Generic;
using ImGuiNET;
using Microsoft.Xna.Framework.Graphics;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Core;

namespace PixelCore.Editor.Panels;

public class ProjectPanel
{
    private readonly EditorState _state;
    private readonly GraphicsDevice _graphicsDevice;

    private readonly string _rootPath;
    private bool _needsRefresh = true;

    public void RequestRefresh() => _needsRefresh = true;

    private readonly List<AssetEntry> _assets = new();
    private FolderNode _root = new();

    public IReadOnlyList<AssetEntry> Assets => _assets;

    private readonly List<string> _selection = new();
    private string? _anchorPath;
    private List<string> _rowOrder = new();
    private List<string> _prevRowOrder = new();

    private string[] _draggedPaths = Array.Empty<string>();
    private string? _draggedScenePath;

    private string _filter = "";
    private bool _filterLive;

    private string? _draggedSceneId;
    public string? DraggedSceneId => _draggedSceneId;

    private string? _draggedTexturePath;
    public string? DraggedTexturePath => _draggedTexturePath;

    private string? _draggedAudioPath;
    public string? DraggedAudioPath => _draggedAudioPath;

    private string? _draggedAnimPath;
    public string? DraggedAnimPath => _draggedAnimPath;

    private string? _draggedPostPath;
    public string? DraggedPostPath => _draggedPostPath;

    public event Action<string>? OnTextureDoubleClicked;

    public event Action<string>? OnPostApplyRequested;

    public event Action<string>? OnTexturePlaceRequested;

    public event Action<string>? OnSceneOpenRequested;

    public event Action<string>? OnAnimationOpenRequested;

    public event Action<string>? OnRenameRequested;

    public event Action<string>? OnImportRequested;

    public event Action<string>? OnNewFolderRequested;

    public event Action<string>? OnDeleteFolderRequested;

    public event Action<string[]>? OnDeleteRequested;

    public void ClearSelection() => _selection.Clear();

    public event Action<string[], string>? OnMoveRequested;

    private static readonly string SourceContentPath = EditorApp.ContentRoot;

    private readonly HashSet<string> _openDirs = new();
    private bool _hasOpenDirState;
    private bool _openDirsDirty;

    private string? _pendingReveal;
    private HashSet<string>? _pendingRevealDirs;
    private string? _flashPath;
    private double _flashStart = -999;
    private const double FlashSeconds = 1.2;

    public event Action? OnOpenFoldersChanged;

    public void SeedOpenFolders(List<string>? dirs)
    {
        _openDirs.Clear();
        _hasOpenDirState = dirs != null;
        if (dirs != null) foreach (var d in dirs) _openDirs.Add(d);
    }

    public List<string> GetOpenFolders() => new(_openDirs);

    public ProjectPanel(EditorState state, GraphicsDevice graphicsDevice)
    {
        _state = state;
        _graphicsDevice = graphicsDevice;
        _rootPath = SourceContentPath;
        if (!Directory.Exists(_rootPath))
            Directory.CreateDirectory(_rootPath);
    }

    public void DrawContent()
    {
        if (_needsRefresh)
        {
            Refresh();
            _needsRefresh = false;
        }

        bool filterQuiet = _filter.Length == 0 && !_filterLive;
        if (filterQuiet)
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new System.Numerics.Vector4(0, 0, 0, 0));
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##assetFilter", Icons.Search + "  Filter", ref _filter, 128);
        if (filterQuiet) ImGui.PopStyleColor();
        _filterLive = ImGui.IsItemActive() || ImGui.IsItemHovered();

        ImGui.Spacing();

        string? previewPath = _selection.Count > 0 ? _selection[^1] : null;
        bool showPreview = previewPath != null && IsImage(Path.GetExtension(previewPath).ToLowerInvariant())
            && ThumbnailCache.TryGet(previewPath, out _, out _, out _, out _);
        float cardH = showPreview ? 174f : 0f;

        ImGui.BeginChild("AssetTree", new System.Numerics.Vector2(0, -cardH), ImGuiChildFlags.None);

        (_prevRowOrder, _rowOrder) = (_rowOrder, _prevRowOrder);
        _rowOrder.Clear();

        bool filtering = !string.IsNullOrWhiteSpace(_filter);

        if (filtering)
        {
            foreach (var e in _assets)
                if (e.RelPath.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                    DrawFileItem(e, showDir: true);
        }
        else
        {
            DrawFolderNode(_root, topLevel: true);
        }

        if (ImGui.BeginPopupContextWindow("##assetTreeCtx", ImGuiPopupFlags.MouseButtonRight
            | ImGuiPopupFlags.NoOpenOverItems))
        {
            if (ImGui.MenuItem(Icons.Plus + "  Import assets..."))
                OnImportRequested?.Invoke(_rootPath);
            if (ImGui.MenuItem(Icons.Folder + "  New folder"))
                OnNewFolderRequested?.Invoke(_rootPath);
            ImGui.Separator();
            if (ImGui.MenuItem(Icons.FolderOpen + "  Reveal in file manager"))
                RevealInFinder(_rootPath);
            if (ImGui.MenuItem(Icons.Undo + "  Rescan"))
                _needsRefresh = true;
            ImGui.EndPopup();
        }

        ImGui.EndChild();

        if (showPreview) DrawPreviewCard(previewPath!);

        _pendingReveal = null;
        _pendingRevealDirs = null;

        if (_openDirsDirty)
        {
            _openDirsDirty = false;
            _hasOpenDirState = true;
            OnOpenFoldersChanged?.Invoke();
        }
    }

    private void DrawPreviewCard(string fullPath)
    {
        if (!ThumbnailCache.TryGet(fullPath, out var texId, out var texSize, out var slice0, out var sliceCount))
            return;

        ImGui.PushStyleColor(ImGuiCol.ChildBg, EditorTheme.Recessed);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(8, 8));
        ImGui.BeginChild("asset_preview", new System.Numerics.Vector2(0, 0),
            ImGuiChildFlags.AlwaysUseWindowPadding);

        var dl = ImGui.GetWindowDrawList();
        var avail = ImGui.GetContentRegionAvail();
        float metaH = ImGui.GetTextLineHeightWithSpacing() * 2f + 2f;
        float imgAreaH = MathF.Max(40f, avail.Y - metaH);

        var areaMin = ImGui.GetCursorScreenPos();
        var areaSize = new System.Numerics.Vector2(avail.X, imgAreaH);
        EditorTheme.DrawCheckerboard(dl, areaMin, areaSize);
        dl.AddRect(areaMin, areaMin + areaSize, ImGui.GetColorU32(ImGuiCol.Border));

        var src = slice0 ?? new Microsoft.Xna.Framework.Rectangle(0, 0, texSize.X, texSize.Y);
        var drawSz = ThumbnailCache.FitSize(src, areaSize - new System.Numerics.Vector2(10, 10));
        var (uv0, uv1) = ThumbnailCache.Uv(src, texSize);
        var imgMin = areaMin + (areaSize - drawSz) * 0.5f;
        imgMin = new System.Numerics.Vector2(MathF.Round(imgMin.X), MathF.Round(imgMin.Y));
        dl.AddImage(texId, imgMin, imgMin + drawSz, uv0, uv1);

        ImGui.Dummy(new System.Numerics.Vector2(0, imgAreaH + 2));

        ImGui.PushFont(ImGuiRenderer.SemiBoldFont);
        ImGui.TextUnformatted(Path.GetFileName(fullPath));
        ImGui.PopFont();
        ImGui.PushFont(ImGuiRenderer.MonoFont);
        string meta = sliceCount > 0
            ? $"{texSize.X}x{texSize.Y} px · {sliceCount} slices (first {src.Width}x{src.Height})"
            : $"{texSize.X}×{texSize.Y} px";
        ImGui.TextDisabled(meta);
        ImGui.PopFont();

        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    private void DrawFolderNode(FolderNode node, bool topLevel = false)
    {
        foreach (var (name, child) in node.Dirs)
        {
            var flags = ImGuiTreeNodeFlags.SpanAvailWidth;
            if (_pendingRevealDirs != null && _pendingRevealDirs.Contains(child.RelDir))
                ImGui.SetNextItemOpen(true, ImGuiCond.Always);
            else if (_hasOpenDirState)
                ImGui.SetNextItemOpen(_openDirs.Contains(child.RelDir), ImGuiCond.Once);
            else if (topLevel)
                flags |= ImGuiTreeNodeFlags.DefaultOpen;

            bool open = ImGui.TreeNodeEx($"{Icons.Folder}  {name}###dir_{child.RelDir}", flags);

            if (open != _openDirs.Contains(child.RelDir))
            {
                if (open) _openDirs.Add(child.RelDir); else _openDirs.Remove(child.RelDir);
                _openDirsDirty = true;
            }

            DrawFolderContextMenu(child);
            DrawFolderDropTarget(child);
            if (open)
            {
                var dl = ImGui.GetWindowDrawList();
                var top = ImGui.GetCursorScreenPos();
                DrawFolderNode(child);
                var bottom = ImGui.GetCursorScreenPos();
                float gx = MathF.Round(top.X - ImGui.GetStyle().IndentSpacing * 0.5f);
                float gy1 = bottom.Y - ImGui.GetStyle().ItemSpacing.Y;
                if (gy1 > top.Y)
                    dl.AddLine(new System.Numerics.Vector2(gx, top.Y),
                               new System.Numerics.Vector2(gx, gy1),
                               ImGui.GetColorU32(ImGuiCol.Border), 1f);
                ImGui.TreePop();
            }
        }

        foreach (var entry in node.Files)
            DrawFileItem(entry, showDir: false);
    }

    private void DrawFolderDropTarget(FolderNode node)
    {
        if (!ImGui.BeginDragDropTarget()) return;

        string fullDir = Path.Combine(_rootPath, node.RelDir).Replace('\\', '/');
        unsafe
        {
            var multi = ImGui.AcceptDragDropPayload("ASSET_FILES");
            if (multi.NativePtr != null && _draggedPaths.Length > 0)
                OnMoveRequested?.Invoke(_draggedPaths, fullDir);

            var tex = ImGui.AcceptDragDropPayload("TEXTURE_FILE");
            if (tex.NativePtr != null && _draggedTexturePath != null)
                OnMoveRequested?.Invoke(new[] { _draggedTexturePath }, fullDir);

            var scene = ImGui.AcceptDragDropPayload("SCENE_FILE");
            if (scene.NativePtr != null && _draggedScenePath != null)
                OnMoveRequested?.Invoke(new[] { _draggedScenePath }, fullDir);

            var audio = ImGui.AcceptDragDropPayload("AUDIO_FILE");
            if (audio.NativePtr != null && _draggedAudioPath != null)
                OnMoveRequested?.Invoke(new[] { _draggedAudioPath }, fullDir);

            var anim = ImGui.AcceptDragDropPayload("ANIM_FILE");
            if (anim.NativePtr != null && _draggedAnimPath != null)
                OnMoveRequested?.Invoke(new[] { _draggedAnimPath }, fullDir);
        }
        ImGui.EndDragDropTarget();
    }

    private void DrawFolderContextMenu(FolderNode node)
    {
        if (!ImGui.BeginPopupContextItem()) return;

        string fullDir = Path.Combine(_rootPath, node.RelDir).Replace('\\', '/');

        if (ImGui.MenuItem(Icons.Plus + "  Import assets..."))
            OnImportRequested?.Invoke(fullDir);
        if (ImGui.MenuItem(Icons.Folder + "  New folder"))
            OnNewFolderRequested?.Invoke(fullDir);
        ImGui.Separator();
        if (ImGui.MenuItem("Rename"))
            OnRenameRequested?.Invoke(fullDir);
        if (ImGui.MenuItem("Reveal in file manager"))
            RevealInFinder(fullDir);
        if (ImGui.MenuItem("Copy path"))
            ImGui.SetClipboardText(node.RelDir);
        ImGui.Separator();
        if (ImGui.MenuItem(Icons.Trash + "  Move to trash"))
            OnDeleteFolderRequested?.Invoke(fullDir);

        ImGui.EndPopup();
    }

    private static string Ellipsize(string text, float maxW)
    {
        if (maxW <= 0f || text.Length == 0) return text;
        if (ImGui.CalcTextSize(text).X <= maxW) return text;

        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (ImGui.CalcTextSize(text.Substring(0, mid) + "…").X <= maxW) lo = mid;
            else hi = mid - 1;
        }
        return lo <= 0 ? "…" : text.Substring(0, lo) + "…";
    }

    private void DrawFileItem(AssetEntry entry, bool showDir)
    {
        float align = ImGui.GetTreeNodeToLabelSpacing();
        ImGui.Indent(align);

        _rowOrder.Add(entry.FullPath);
        bool isSelected = _selection.Contains(entry.FullPath);

        string bareName = Path.GetFileNameWithoutExtension(entry.Name);
        string ext = entry.Extension.TrimStart('.');

        float badgeW = 0f;
        if (ext.Length > 0)
        {
            ImGui.PushFont(ImGuiRenderer.MonoFont);
            badgeW = ImGui.CalcTextSize(ext).X + 12f;
            ImGui.PopFont();
        }
        const string IconGap = "      ";
        float nameMax = ImGui.GetContentRegionAvail().X
                      - ImGui.CalcTextSize(IconGap).X - badgeW;
        string shown = Ellipsize(
            showDir && entry.RelDir.Length > 0 ? $"{bareName}  · {entry.RelDir}" : bareName,
            nameMax);
        string label = IconGap + shown;

        if (isSelected)
            EditorWidgets.PushSelectionBg(ImGui.GetTextLineHeight() + ImGui.GetStyle().ItemSpacing.Y);
        bool rowClicked = ImGui.Selectable($"{label}###file_{entry.RelPath}", isSelected,
            ImGuiSelectableFlags.AllowDoubleClick);
        if (isSelected) EditorWidgets.PopSelectionBg();

        if (_pendingReveal != null && string.Equals(entry.FullPath, _pendingReveal, StringComparison.OrdinalIgnoreCase))
        {
            ImGui.SetScrollHereY(0.5f);
            _pendingReveal = null;
            _pendingRevealDirs = null;
        }
        if (_flashPath != null && string.Equals(entry.FullPath, _flashPath, StringComparison.OrdinalIgnoreCase))
        {
            float a = Shell.AssetPing.FlashAlpha(ImGui.GetTime(), _flashStart, FlashSeconds);
            if (a <= 0f) _flashPath = null;
            else
            {
                var accent = EditorTheme.Accent;
                var fill = new System.Numerics.Vector4(accent.X, accent.Y, accent.Z, 0.35f * a);
                var line = new System.Numerics.Vector4(accent.X, accent.Y, accent.Z, a);
                var mn = ImGui.GetItemRectMin();
                var mx = ImGui.GetItemRectMax();
                var dl = ImGui.GetWindowDrawList();
                dl.AddRectFilled(mn, mx, ImGui.GetColorU32(fill), 4f);
                dl.AddRect(mn, mx, ImGui.GetColorU32(line), 4f);
            }
        }
        if (rowClicked)
        {
            var io = ImGui.GetIO();
            if (io.KeyShift && _anchorPath != null)
            {
                SelectRange(_anchorPath, entry.FullPath);
            }
            else if (io.KeySuper || io.KeyCtrl)
            {
                if (!_selection.Remove(entry.FullPath)) _selection.Add(entry.FullPath);
                _anchorPath = entry.FullPath;
            }
            else
            {
                _selection.Clear();
                _selection.Add(entry.FullPath);
                _anchorPath = entry.FullPath;
            }

            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                OnFileDoubleClicked(entry);
        }

        if (IsImage(entry.Extension) &&
            ThumbnailCache.TryGet(entry.FullPath, out var thumbId, out var texSize, out var slice0, out _))
        {
            var dl = ImGui.GetWindowDrawList();
            var src = slice0 ?? new Microsoft.Xna.Framework.Rectangle(0, 0, texSize.X, texSize.Y);
            float rowH = ImGui.GetItemRectSize().Y;
            float slot = MathF.Min(16f, rowH - 3f);
            var sMin = new System.Numerics.Vector2(
                ImGui.GetItemRectMin().X + 2f,
                MathF.Round(ImGui.GetItemRectMin().Y + (rowH - slot) * 0.5f));
            var sMax = sMin + new System.Numerics.Vector2(slot, slot);
            dl.AddRectFilled(sMin, sMax, ImGui.GetColorU32(EditorTheme.Recessed), 2f);
            dl.AddRect(sMin, sMax, ImGui.GetColorU32(ImGuiCol.Border), 2f);

            var drawSz = ThumbnailCache.FitSize(src,
                new System.Numerics.Vector2(slot - 2f, slot - 2f));
            var (uv0, uv1) = ThumbnailCache.Uv(src, texSize);
            var pmin = sMin + (new System.Numerics.Vector2(slot, slot) - drawSz) * 0.5f;
            dl.AddImage(thumbId, pmin, pmin + drawSz, uv0, uv1);
        }
        else
        {
            var iconPos = new System.Numerics.Vector2(
                ImGui.GetItemRectMin().X + 2,
                ImGui.GetItemRectMin().Y + (ImGui.GetItemRectSize().Y - ImGui.GetTextLineHeight()) * 0.5f);
            ImGui.GetWindowDrawList().AddText(iconPos,
                ImGui.GetColorU32(AssetKinds.IconColor(entry.Kind)), AssetKinds.Icon(entry.Kind));
        }

        if (ext.Length > 0 && (isSelected || ImGui.IsItemHovered()))
        {
            ImGui.PushFont(ImGuiRenderer.MonoFont);
            var ets = ImGui.CalcTextSize(ext);
            ImGui.GetWindowDrawList().AddText(
                new System.Numerics.Vector2(
                    ImGui.GetItemRectMax().X - ets.X - 6,
                    ImGui.GetItemRectMin().Y + (ImGui.GetItemRectSize().Y - ets.Y) * 0.5f),
                ImGui.GetColorU32(ImGuiCol.TextDisabled), ext);
            ImGui.PopFont();
        }

        if (ImGui.BeginDragDropSource())
        {
            if (entry.Extension == ".post")
            {
                _draggedPostPath = entry.FullPath;
                ImGui.SetDragDropPayload("POST_FILE", IntPtr.Zero, 0);
                ImGui.Text($"{Icons.Film}  {entry.Name}");
            }
            else if (_selection.Count > 1 && _selection.Contains(entry.FullPath))
            {
                _draggedPaths = _selection.ToArray();
                ImGui.SetDragDropPayload("ASSET_FILES", IntPtr.Zero, 0);
                ImGui.Text($"{Icons.Copy}  {_draggedPaths.Length} assets");
            }
            else if (entry.Kind is AssetKind.Scene or AssetKind.Prefab)
            {
                _draggedSceneId = AssetRegistry.Instance.GetOrCreateId(entry.FullPath);
                _draggedScenePath = entry.FullPath;
                ImGui.SetDragDropPayload("SCENE_FILE", IntPtr.Zero, 0);
                ImGui.Text($"{AssetKinds.Icon(entry.Kind)}  {entry.Name}");
            }
            else if (IsImage(entry.Extension))
            {
                _draggedTexturePath = entry.FullPath;
                ImGui.SetDragDropPayload("TEXTURE_FILE", IntPtr.Zero, 0);
                ImGui.Text($"{Icons.Image}  {entry.Name}");
            }
            else if (entry.Kind == AssetKind.Audio)
            {
                _draggedAudioPath = entry.FullPath;
                ImGui.SetDragDropPayload("AUDIO_FILE", IntPtr.Zero, 0);
                ImGui.Text($"{AssetKinds.Icon(entry.Kind)}  {entry.Name}");
            }
            else if (entry.Kind == AssetKind.Animation)
            {
                _draggedAnimPath = entry.FullPath;
                ImGui.SetDragDropPayload("ANIM_FILE", IntPtr.Zero, 0);
                ImGui.Text($"{AssetKinds.Icon(entry.Kind)}  {entry.Name}");
            }
            else
            {
                _draggedPaths = new[] { entry.FullPath };
                ImGui.SetDragDropPayload("ASSET_FILES", IntPtr.Zero, 0);
                ImGui.Text($"{AssetKinds.Icon(entry.Kind)}  {entry.Name}");
            }
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginPopupContextItem())
        {
            if (!_selection.Contains(entry.FullPath))
            {
                _selection.Clear();
                _selection.Add(entry.FullPath);
                _anchorPath = entry.FullPath;
            }
            bool bulk = _selection.Count > 1;

            if (!bulk && IsImage(entry.Extension))
            {
                if (ImGui.MenuItem(Icons.Plus + "  Place in scene"))
                    OnTexturePlaceRequested?.Invoke(entry.FullPath);
                ImGui.Separator();
            }
            if (!bulk && entry.Extension == ".post")
            {
                if (ImGui.MenuItem(Icons.Film + "  Apply to this scene"))
                    OnPostApplyRequested?.Invoke(entry.FullPath);
                ImGui.Separator();
            }
            if (!bulk)
            {
                if (ImGui.MenuItem("Rename"))
                    OnRenameRequested?.Invoke(entry.FullPath);
                if (ImGui.MenuItem(Icons.Copy + "  Duplicate"))
                    DuplicateAsset(entry);
                ImGui.Separator();
                if (ImGui.MenuItem("Reveal in file manager"))
                    RevealInFinder(entry.FullPath);
                if (ImGui.MenuItem("Copy path"))
                    ImGui.SetClipboardText(entry.RelPath);
                ImGui.Separator();
            }
            if (ImGui.MenuItem(Icons.Trash + (bulk ? $"  Move to trash ({_selection.Count})" : "  Move to trash")))
                OnDeleteRequested?.Invoke(_selection.ToArray());
            ImGui.EndPopup();
        }

        ImGui.Unindent(align);
    }

    public void SelectAssetByQuery(string query)
    {
        foreach (var a in _assets)
        {
            if (!IsImage(a.Extension)) continue;
            if (!a.RelPath.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            _selection.Clear();
            _selection.Add(a.FullPath);
            _anchorPath = a.FullPath;
            return;
        }
    }

    public void Reveal(string fullPath)
    {
        var full = Path.GetFullPath(fullPath).Replace('\\', '/');
        var entry = _assets.Find(a => string.Equals(a.FullPath, full, StringComparison.OrdinalIgnoreCase));
        if (entry == null) _needsRefresh = true;
        var target = entry?.FullPath ?? full;
        var rel = entry?.RelPath ?? EditorApp.ToContentRelative(full);

        var dirs = Shell.AssetPing.AncestorDirs(rel);
        foreach (var d in dirs)
            if (_openDirs.Add(d)) _openDirsDirty = true;
        _pendingRevealDirs = new HashSet<string>(dirs);
        _pendingReveal = target;

        _filter = "";
        _selection.Clear();
        _selection.Add(target);
        _anchorPath = target;

        _flashPath = target;
        _flashStart = ImGui.GetTime();
    }

    public void OpenAsset(string fullPath)
    {
        var full = Path.GetFullPath(fullPath).Replace('\\', '/');
        var entry = _assets.Find(a => string.Equals(a.FullPath, full, StringComparison.OrdinalIgnoreCase));
        if (entry != null) OnFileDoubleClicked(entry);
        else if (File.Exists(full)) RevealInFinder(full);
    }

    private void SelectRange(string anchor, string clicked)
    {
        var order = _prevRowOrder.Count > 0 ? _prevRowOrder : _rowOrder;
        int a = order.IndexOf(anchor);
        int b = order.IndexOf(clicked);
        if (a < 0 || b < 0)
        {
            _selection.Clear();
            _selection.Add(clicked);
            _anchorPath = clicked;
            return;
        }
        if (a > b) (a, b) = (b, a);
        _selection.Clear();
        for (int i = a; i <= b; i++)
            _selection.Add(order[i]);
    }

    private void OnFileDoubleClicked(AssetEntry entry)
    {
        switch (entry.Kind)
        {
            case AssetKind.Scene:
            case AssetKind.Prefab:
                OnSceneOpenRequested?.Invoke(entry.FullPath);
                break;
            case AssetKind.Sprite when IsImage(entry.Extension):
                OnTextureDoubleClicked?.Invoke(entry.FullPath);
                break;
            case AssetKind.Animation:
                OnAnimationOpenRequested?.Invoke(entry.FullPath);
                break;
            default:
                if (entry.Extension == ".post") OnPostApplyRequested?.Invoke(entry.FullPath);
                else RevealInFinder(entry.FullPath);
                break;
        }
    }

    private readonly List<string> _allRelDirs = new();

    private void Refresh()
    {
        _assets.Clear();
        _allRelDirs.Clear();
        _root = new FolderNode();

        if (!Directory.Exists(_rootPath)) return;
        ScanDir(_rootPath);

        _assets.Sort((a, b) => string.Compare(a.RelPath, b.RelPath, StringComparison.OrdinalIgnoreCase));

        _selection.RemoveAll(p => !File.Exists(p));

        var liveImages = new HashSet<string>();
        foreach (var a in _assets)
            if (IsImage(a.Extension)) liveImages.Add(a.FullPath);
        ThumbnailCache.Prune(liveImages);

        foreach (var relDir in _allRelDirs)
            EnsureNode(relDir);
        foreach (var entry in _assets)
            EnsureNode(entry.RelDir).Files.Add(entry);
    }

    private FolderNode EnsureNode(string relDir)
    {
        var node = _root;
        if (relDir.Length == 0) return node;
        foreach (var part in relDir.Split('/'))
        {
            if (!node.Dirs.TryGetValue(part, out var child))
            {
                child = new FolderNode
                {
                    RelDir = node.RelDir.Length == 0 ? part : node.RelDir + "/" + part
                };
                node.Dirs[part] = child;
            }
            node = child;
        }
        return node;
    }

    private void ScanDir(string dir)
    {
        foreach (var sub in Directory.GetDirectories(dir))
        {
            var name = Path.GetFileName(sub);
            if (name.StartsWith(".") || name == "bin" || name == "obj") continue;
            _allRelDirs.Add(EditorApp.ToContentRelative(sub));
            ScanDir(sub);
        }

        foreach (var file in Directory.GetFiles(dir))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith(".")) continue;

            var full = Path.GetFullPath(file).Replace('\\', '/');
            var rel = EditorApp.ToContentRelative(full);
            var relDir = Path.GetDirectoryName(rel)?.Replace('\\', '/') ?? "";
            var ext = Path.GetExtension(file).ToLowerInvariant();

            if (ext == ".atlas") continue;

            if (ext == ".fxb") continue;

            if (name == "assets.json" && relDir.Length == 0) continue;

            var kind = AssetKinds.Classify(ext);
            if (kind == AssetKind.Scene && PeekKindIsPrefab(full))
                kind = AssetKind.Prefab;

            _assets.Add(new AssetEntry
            {
                Name = name,
                FullPath = full,
                RelPath = rel,
                RelDir = relDir,
                Extension = ext,
                Kind = kind
            });
        }
    }

    internal static bool PeekKindIsPrefab(string fullPath)
    {
        try
        {
            using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Span<byte> buf = stackalloc byte[256];
            int n = fs.Read(buf);
            var head = System.Text.Encoding.UTF8.GetString(buf[..n]);
            return head.Contains("\"kind\": \"Prefab\"", StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private static bool IsImage(string ext) =>
        ext is ".png" or ".jpg" or ".jpeg" or ".bmp";

    private void DuplicateAsset(AssetEntry entry)
    {
        try
        {
            var dir = Path.GetDirectoryName(entry.FullPath)!;
            var baseName = Path.GetFileNameWithoutExtension(entry.Name);
            var dest = UniquePath(dir, baseName, entry.Extension);
            File.Copy(entry.FullPath, dest);

            if (entry.Extension == ".anim")
                ReissueEmbeddedId(dest);

            if (IsImage(entry.Extension))
            {
                var sidecar = Path.ChangeExtension(entry.FullPath, ".atlas");
                if (File.Exists(sidecar))
                {
                    var destAtlas = Path.ChangeExtension(dest, ".atlas");
                    File.Copy(sidecar, destAtlas);
                    ReissueEmbeddedId(destAtlas);
                    var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(destAtlas));
                    if (node != null)
                    {
                        node["TexturePath"] = Path.GetFileName(dest);
                        AtomicFile.WriteAllText(destAtlas, node.ToJsonString(
                            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    }
                }
            }

            Console.WriteLine($"[Assets] duplicated: {entry.Name} -> {Path.GetFileName(dest)}");
            _needsRefresh = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Assets] duplicate failed: {entry.Name} - {ex.Message}");
        }
    }

    private static void ReissueEmbeddedId(string path)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path));
        if (node?["Id"] == null) return;
        node["Id"] = AssetRegistry.Instance.NewId();
        AtomicFile.WriteAllText(path, node.ToJsonString(
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    internal static string UniquePath(string dir, string baseName, string ext)
    {
        var dest = Path.Combine(dir, baseName + ext);
        for (int i = 1; File.Exists(dest) || Directory.Exists(dest); i++)
            dest = Path.Combine(dir, $"{baseName}_{i}{ext}");
        return dest.Replace('\\', '/');
    }

    internal static bool MoveToTrash(string path)
    {
        try
        {
            var name = Path.GetFileName(path.TrimEnd('/', '\\'));

            if (OperatingSystem.IsWindows())
            {
                if (!MoveToRecycleBinWindows(path, out var why))
                {
                    Console.WriteLine($"[Assets] move to trash failed: {path} - {why}");
                    return false;
                }
                Console.WriteLine($"[Assets] moved to trash: {name}");
                return true;
            }

            if (!OperatingSystem.IsMacOS())
            {
                Console.WriteLine("[Assets] the trash is not implemented on this platform yet - deletion cancelled: " + path);
                return false;
            }

            var trash = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash");
            if (!Directory.Exists(trash))
            {
                Console.WriteLine("[Assets] no trash folder - deletion cancelled: " + path);
                return false;
            }
            var dest = UniquePath(trash, Path.GetFileNameWithoutExtension(name), Path.GetExtension(name));
            if (Directory.Exists(path)) Directory.Move(path, dest);
            else File.Move(path, dest);
            Console.WriteLine($"[Assets] moved to trash: {name}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Assets] move to trash failed: {path} - {ex.Message}");
            return false;
        }
    }

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential,
        CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll",
        CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern int SHFileOperationW(ref ShFileOpStruct lpFileOp);

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    private static bool MoveToRecycleBinWindows(string path, out string why)
    {
        var op = new ShFileOpStruct
        {
            wFunc = FO_DELETE,
            pFrom = Path.GetFullPath(path) + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT,
        };
        int rc = SHFileOperationW(ref op);
        if (rc != 0) { why = $"SHFileOperation code {rc} (0x{rc:X})"; return false; }
        if (op.fAnyOperationsAborted != 0) { why = "the shell cancelled the operation"; return false; }
        why = "";
        return true;
    }

    internal static void RevealInFinder(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (OperatingSystem.IsMacOS())
                System.Diagnostics.Process.Start("open", $"-R \"{path}\"");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to open explorer: {ex.Message}");
        }
    }

    private sealed class FolderNode
    {
        public string RelDir = "";
        public SortedDictionary<string, FolderNode> Dirs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<AssetEntry> Files { get; } = new();
    }
}

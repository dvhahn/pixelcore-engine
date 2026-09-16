using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using ImGuiNET;
using Microsoft.Xna.Framework.Graphics;
using PixelCore.Runtime.Animation;
using PixelCore.Runtime.Assets;

namespace PixelCore.Editor.Panels;

public class AnimationEditorPanel
{
    private readonly GraphicsDevice _graphicsDevice;

    private AnimationData _animation = new();
    private string _currentPath = "";
    private bool _isOpen;
    private bool _isDirty;

    private SpriteAtlas? _atlas;
    private string _atlasContentPath = "";
    private IntPtr _atlasTextureId;

    private bool _isPlaying;
    private float _playTime;
    private float _lastFrameTime;

    private int _selectedFrameIndex = -1;
    private int _draggedFrameIndex = -1;
    private int _lastAddedSliceIndex = -1;
    private float _zoom = 2f;

    private string _atlasPickerQuery = "";
    private List<(string Name, string Path)>? _atlasCache;
    private DateTime _atlasCacheTime;

    private Func<Texture2D, IntPtr>? _bindTextureFunc;

    public AnimationEditorPanel(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice;
    }

    public void SetTextureBinding(Func<Texture2D, IntPtr> bindTexture)
    {
        _bindTextureFunc = bindTexture;
    }

    public void SetAtlas(SpriteAtlas atlas, string? contentRelativePath = null, bool markEdited = true)
    {
        string previousAtlasId = _animation.AtlasId;
        bool wasDirty = _isDirty;

        if (markEdited) MarkEdited();

        BindAtlas(atlas, contentRelativePath ?? atlas.TexturePath, atlas.Id);
        if (markEdited) StageBulkRetarget(previousAtlasId, wasDirty);
    }

    private void BindAtlas(SpriteAtlas? atlas, string contentRelativePath, string atlasId)
    {
        _atlas = atlas;
        if (_atlas?.Texture != null && _bindTextureFunc != null)
        {
            _atlasTextureId = _bindTextureFunc(_atlas.Texture);
        }

        _atlasContentPath = contentRelativePath;
        _animation.AtlasPath = _atlasContentPath;
        _animation.AtlasId = atlasId;

        _clipCache = null;
    }

    private void SwapAtlasFromFile(string fullPath)
    {
        var atlasPath = Path.ChangeExtension(fullPath, ".atlas");
        if (!File.Exists(atlasPath))
        {
            OnWarning?.Invoke($"{Path.GetFileName(fullPath)}: no slices (.atlas)"
                + " - slice and save it in the sprite editor, then try again");
            return;
        }

        var atlas = SpriteAtlas.Load(atlasPath);
        if (atlas == null)
        {
            OnWarning?.Invoke($"{Path.GetFileName(atlasPath)}: could not be read - see the console");
            return;
        }

        var dir = Path.GetDirectoryName(atlasPath) ?? EditorApp.ContentRoot;
        atlas.LoadTexture(_graphicsDevice, dir);

        SetAtlas(atlas, EditorApp.ToContentRelative(Path.Combine(dir, atlas.TexturePath)));
    }

    private sealed record PendingBulk(string OldAtlasId, string NewAtlasId, string NewAtlasPath,
                                      List<string> Files, bool WasDirty);
    private PendingBulk? _pendingBulk;
    private bool _bulkPopupOpen;

    private void StageBulkRetarget(string oldAtlasId, bool wasDirty)
    {
        string origin = _pendingBulk?.OldAtlasId ?? oldAtlasId;
        bool originDirty = _pendingBulk?.WasDirty ?? wasDirty;
        ClearPendingBulk();

        if (origin.Length == 0 || origin == _animation.AtlasId) return;
        if (_animation.AtlasId.Length == 0) return;

        var others = AtlasRetarget.FindClipsUsing(ScanRoot, origin, _currentPath);
        if (others.Count == 0) return;

        _pendingBulk = new PendingBulk(origin, _animation.AtlasId, _animation.AtlasPath, others, originDirty);
    }

    private void ClearPendingBulk()
    {
        _pendingBulk = null;
        _bulkPopupOpen = false;
    }

    private void KeepOpenClipOnly() => ClearPendingBulk();

    private void CancelBulkRevert()
    {
        var bulk = _pendingBulk;
        ClearPendingBulk();
        if (bulk == null) return;

        Undo();
        _isDirty = bulk.WasDirty;
    }

    private void ApplyBulkRetarget()
    {
        var bulk = _pendingBulk;
        ClearPendingBulk();
        if (bulk == null) return;

        int changed = AtlasRetarget.Retarget(bulk.Files, bulk.NewAtlasId, bulk.NewAtlasPath);

        foreach (var f in bulk.Files) Runtime.Assets.AssetEvents.RaiseSaved(f);
        _clipCache = null;

        OnNotice?.Invoke(changed > 0
            ? $"Retargeted {changed} clips to the new sheet - open scenes need reopening to see it"
            : "No clips needed changing (they already pointed at the new sheet)");
    }

    private void DrawBulkRetargetConfirm()
    {
        if (_pendingBulk == null) return;
        var bulk = _pendingBulk;

        const string popupId = "Retarget the other clips too?###AnimBulkRetarget";
        if (!_bulkPopupOpen)
        {
            ImGui.OpenPopup(popupId);
            _bulkPopupOpen = true;
        }
        else if (!ImGui.IsPopupOpen(popupId))
        {
            CancelBulkRevert();
            return;
        }

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        bool visible = true;
        if (ImGui.BeginPopupModal(popupId, ref visible, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text($"{bulk.Files.Count} other clips use this sheet. Retarget all of them?");
            ImGui.Spacing();

            ImGui.BeginChild("##bulkList", new Vector2(420, Math.Min(bulk.Files.Count, 8) * ImGui.GetTextLineHeightWithSpacing() + 6),
                ImGuiChildFlags.FrameStyle);
            foreach (var f in bulk.Files)
                ImGui.TextUnformatted(EditorApp.ToContentRelative(f));
            ImGui.EndChild();

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1, 0.7f, 0.2f, 1),
                $"{Icons.Warning}  [Retarget all] writes to disk immediately and cannot be undone with Cmd+Z.");
            ImGui.TextDisabled("[Only this clip]  changes just the open clip (kept as one undo step).");
            ImGui.TextDisabled("[Cancel]  undoes this swap entirely - the sheet reverts (Esc and closing the window do the same).");
            ImGui.TextDisabled("Neither option touches the frames (the cell numbers).");
            ImGui.Spacing();

            if (ImGui.Button("Retarget all"))
            {
                ApplyBulkRetarget();
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Only this clip"))
            {
                KeepOpenClipOnly();
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                CancelBulkRevert();
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        if (!visible) CancelBulkRevert();
    }

    public Func<string?>? DraggedTexturePathProvider { get; set; }

    public event Action<string>? OnNotice;

    public event Action<string>? OnWarning;

    public void New()
    {
        _animation = new AnimationData
        {
            Name = "New Animation",
            AtlasPath = _atlasContentPath,
            AtlasId = _atlas?.Id ?? "",
        };
        _currentPath = "";
        _isOpen = true;
        _isDirty = false;
        _selectedFrameIndex = -1;
        _lastAddedSliceIndex = -1;
        _isPlaying = false;
        _playTime = 0;
        _undoStack.Clear();
        _redoStack.Clear();
        _idleSnapshot = null;
        ClearPendingBulk();
    }

    public void Open(string filePath)
    {
        if (_isDirty && _currentPath.Length > 0 && _currentPath == filePath)
        {
            _isOpen = true;
            return;
        }

        var anim = AnimationData.Load(filePath);
        if (anim == null) return;

        _animation = anim;
        _currentPath = filePath;
        _isOpen = true;
        _isDirty = false;
        _selectedFrameIndex = -1;
        _lastAddedSliceIndex = -1;
        _isPlaying = false;
        _playTime = 0;
        _undoStack.Clear();
        _redoStack.Clear();
        _idleSnapshot = null;
        ClearPendingBulk();

        string? atlasRel = null;
        if (!string.IsNullOrEmpty(_animation.AtlasId))
            atlasRel = AssetRegistry.Instance.GetPath(_animation.AtlasId);
        if (atlasRel == null && !string.IsNullOrEmpty(_animation.AtlasPath))
            atlasRel = Path.ChangeExtension(_animation.AtlasPath, ".atlas");

        _atlasContentPath = _animation.AtlasPath;
        _clipCache = null;
        if (atlasRel != null)
        {
            var atlasPath = Path.Combine(EditorApp.ContentRoot, atlasRel);
            _atlas = SpriteAtlas.Load(atlasPath);
            if (_atlas != null)
            {
                _atlas.LoadTexture(_graphicsDevice, Path.GetDirectoryName(atlasPath) ?? EditorApp.ContentRoot);

                if (_atlas.Texture != null && _bindTextureFunc != null)
                {
                    _atlasTextureId = _bindTextureFunc(_atlas.Texture);
                }
            }
        }
    }

    public void Close()
    {
        _isOpen = false;
        _isPlaying = false;
    }

    public void Hide()
    {
        _isOpen = false;
        _isPlaying = false;
    }

    public bool IsDirty => _isDirty;

    public string CurrentPath => _currentPath;

    private sealed record AnimSnapshot(string Name, bool Loop, List<FrameData> Frames,
                                       SpriteAtlas? Atlas, string AtlasContentPath, string AtlasId);

    private readonly List<AnimSnapshot> _undoStack = new();
    private readonly List<AnimSnapshot> _redoStack = new();
    private AnimSnapshot? _idleSnapshot;
    private bool _undoPushedThisGesture;
    private const int UndoCap = 64;

    private List<FrameData> CopyFrames()
    {
        var list = new List<FrameData>(_animation.Frames.Count);
        foreach (var f in _animation.Frames)
            list.Add(new FrameData { SliceIndex = f.SliceIndex, Duration = f.Duration, EventName = f.EventName });
        return list;
    }

    private AnimSnapshot Capture() => new(_animation.Name, _animation.Loop, CopyFrames(),
                                          _atlas, _atlasContentPath, _animation.AtlasId);

    private void Restore(AnimSnapshot s)
    {
        if (!ReferenceEquals(_atlas, s.Atlas) || _atlasContentPath != s.AtlasContentPath
            || _animation.AtlasId != s.AtlasId)
            BindAtlas(s.Atlas, s.AtlasContentPath, s.AtlasId);

        _animation.Name = s.Name;
        _animation.Loop = s.Loop;
        _animation.Frames.Clear();
        foreach (var f in s.Frames)
            _animation.Frames.Add(new FrameData { SliceIndex = f.SliceIndex, Duration = f.Duration, EventName = f.EventName });
        if (_selectedFrameIndex >= _animation.Frames.Count)
            _selectedFrameIndex = _animation.Frames.Count - 1;
        _playTime = Math.Min(_playTime, _animation.TotalDuration);
    }

    private void MarkEdited()
    {
        if (!_undoPushedThisGesture && _idleSnapshot != null)
        {
            _undoStack.Add(_idleSnapshot);
            if (_undoStack.Count > UndoCap) _undoStack.RemoveAt(0);
            _redoStack.Clear();
            _undoPushedThisGesture = true;
        }
        _isDirty = true;
    }

    private void CaptureIdle()
    {
        _idleSnapshot = Capture();
        _undoPushedThisGesture = false;
    }

    internal static string? SelfTestContentRoot;
    private static string ScanRoot => SelfTestContentRoot ?? EditorApp.ContentRoot;

    internal void SelfTestFrameBoundary() => CaptureIdle();
    internal void SelfTestUndo() => Undo();
    internal void SelfTestRedo() => Redo();
    internal void SelfTestSave() => SaveCurrent();
    internal AnimationData SelfTestAnimation => _animation;
    internal SpriteAtlas? SelfTestAtlas => _atlas;
    internal int SelfTestPendingBulkCount => _pendingBulk?.Files.Count ?? 0;
    internal void SelfTestApplyBulk() => ApplyBulkRetarget();
    internal void SelfTestKeepOpenClipOnly() => KeepOpenClipOnly();
    internal void SelfTestCancelBulk() => CancelBulkRevert();
    internal bool SelfTestBulkPopupOpen => _bulkPopupOpen;
    internal void SelfTestMarkBulkPopupOpen() => _bulkPopupOpen = true;
    internal List<(string Name, string Path)> SelfTestClipList() => ClipsInCurrentFolder();

    private void Undo()
    {
        if (_undoStack.Count == 0) return;
        _redoStack.Add(Capture());
        Restore(_undoStack[^1]);
        _undoStack.RemoveAt(_undoStack.Count - 1);
        _isDirty = true;
        _idleSnapshot = Capture();
        _undoPushedThisGesture = false;
    }

    private void Redo()
    {
        if (_redoStack.Count == 0) return;
        _undoStack.Add(Capture());
        Restore(_redoStack[^1]);
        _redoStack.RemoveAt(_redoStack.Count - 1);
        _isDirty = true;
        _idleSnapshot = Capture();
        _undoPushedThisGesture = false;
    }

    private string? _pendingSwitchPath;

    public void RequestOpen(string filePath) => RequestSwitch(filePath);

    private void RequestSwitch(string path)
    {
        if (_isDirty) _pendingSwitchPath = path;
        else DoSwitch(path);
    }

    private void DoSwitch(string path)
    {
        if (path.Length == 0) New();
        else Open(path);
    }

    private void DrawSwitchConfirm()
    {
        if (_pendingSwitchPath == null) return;
        string pending = _pendingSwitchPath;

        const string popupId = "Unsaved changes###AnimSwitchConfirm";
        if (!ImGui.IsPopupOpen(popupId)) ImGui.OpenPopup(popupId);

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        bool visible = true;
        if (ImGui.BeginPopupModal(popupId, ref visible, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text($"The clip '{_animation.Name}' has unsaved changes.");
            ImGui.Spacing();

            if (ImGui.Button("Save and switch"))
            {
                SaveCurrent();
                _pendingSwitchPath = null;
                DoSwitch(pending);
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Discard and switch"))
            {
                _isDirty = false;
                _pendingSwitchPath = null;
                DoSwitch(pending);
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                _pendingSwitchPath = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        if (!visible) _pendingSwitchPath = null;
    }

    private List<(string Name, string Path)>? _clipCache;
    private DateTime _clipCacheTime;

    private string CurrentClipDir()
    {
        var fallback = Path.Combine(EditorApp.ContentRoot, "Animations");
        return string.IsNullOrEmpty(_currentPath) ? fallback
            : Path.GetDirectoryName(_currentPath) ?? fallback;
    }

    private List<(string Name, string Path)> ClipsInCurrentFolder()
    {
        if (_clipCache != null && (DateTime.Now - _clipCacheTime).TotalSeconds < 5)
            return _clipCache;

        var result = new List<(string Name, string Path)>();
        try
        {
            foreach (var f in Directory.GetFiles(CurrentClipDir(), "*.anim", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var data = AnimationData.Load(f);
                    if (data != null) result.Add((data.Name, f));
                }
                catch {  }
            }
            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }
        catch {  }

        _clipCache = result;
        _clipCacheTime = DateTime.Now;
        return _clipCache;
    }

    public void SaveIfDirty()
    {
        if (_isDirty) SaveCurrent();
    }

    private void SaveCurrent()
    {
        var dir = CurrentClipDir();
        Directory.CreateDirectory(dir);
        var savePath = Path.Combine(dir, $"{_animation.Name}.anim");
        var rel = EditorApp.ToContentRelative(savePath);

        if (string.IsNullOrEmpty(_animation.Id))
            _animation.Id = AssetRegistry.Instance.GetOrCreateId(rel);
        else if (AssetRegistry.Instance.GetPath(_animation.Id) != null)
            AssetRegistry.Instance.UpdatePath(_animation.Id, rel);
        else
            AssetRegistry.Instance.Register(_animation.Id, rel);

        if (string.IsNullOrEmpty(_animation.AtlasId) && !string.IsNullOrEmpty(_animation.AtlasPath))
        {
            var atlasRel = Path.ChangeExtension(_animation.AtlasPath, ".atlas").Replace('\\', '/');
            _animation.AtlasId = AssetRegistry.Instance.GetId(atlasRel) ?? "";
        }

        _animation.Save(savePath);
        _currentPath = savePath;
        _isDirty = false;
        _clipCache = null;

        Runtime.Assets.AssetEvents.RaiseSaved(savePath);

        OnSaved?.Invoke(savePath);
    }

    public void Update(float deltaTime)
    {
        if (!_isPlaying || _animation.Frames.Count == 0) return;

        _playTime += deltaTime;

        if (_animation.Loop)
        {
            if (_playTime >= _animation.TotalDuration)
                _playTime %= _animation.TotalDuration;
        }
        else
        {
            if (_playTime >= _animation.TotalDuration)
            {
                _playTime = _animation.TotalDuration;
                _isPlaying = false;
            }
        }
    }

    public void Draw(uint dockId = 0, bool forceDock = false)
    {
        if (!_isOpen) return;

        if (!ImGui.IsAnyItemActive()) CaptureIdle();

        if (dockId != 0)
        {
            EditorWidgets.DocWindowClass();
            ImGui.SetNextWindowDockID(dockId, ImGuiCond.Always);
        }
        _ = forceDock;

        ImGui.SetNextWindowSize(new Vector2(700, 500), ImGuiCond.FirstUseEver);

        string title = _isDirty ? $"Animation Editor - {_animation.Name}*###AnimEditor"
                                : $"Animation Editor - {_animation.Name}###AnimEditor";

        bool open = _isOpen;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, EditorWidgets.DocTabPadding);
        bool docVisible = ImGui.Begin(title, ref open);
        ImGui.PopStyleVar();
        if (docVisible)
        {
            if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && !ImGui.GetIO().WantTextInput)
            {
                var kio = ImGui.GetIO();
                bool cmdOrCtrl = kio.KeySuper || kio.KeyCtrl;
                if (cmdOrCtrl && ImGui.IsKeyPressed(ImGuiKey.Z))
                {
                    if (kio.KeyShift) Redo();
                    else Undo();
                }
            }

            DrawToolbar();
            ImGui.Separator();

            float leftWidth = ImGui.GetContentRegionAvail().X * 0.35f;

            ImGui.BeginChild("AtlasSlices", new Vector2(leftWidth, 0), ImGuiChildFlags.FrameStyle);
            DrawAtlasSlices();
            ImGui.EndChild();
            AcceptTextureDrop();

            ImGui.SameLine();

            ImGui.BeginChild("Timeline", new Vector2(0, 0), ImGuiChildFlags.FrameStyle);
            DrawPreview();
            ImGui.Separator();
            DrawTimeline();
            ImGui.Separator();
            DrawFrameProperties();
            ImGui.EndChild();
        }
        ImGui.End();

        if (!open)
        {
            if (_isDirty) _closeConfirmPending = true;
            else Close();
        }
        DrawCloseConfirm();
        DrawSwitchConfirm();
        DrawBulkRetargetConfirm();
    }

    private bool _closeConfirmPending;

    private void DrawCloseConfirm()
    {
        if (!_closeConfirmPending) return;

        const string popupId = "Unsaved changes###AnimCloseConfirm";
        if (!ImGui.IsPopupOpen(popupId)) ImGui.OpenPopup(popupId);

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        bool visible = true;
        if (ImGui.BeginPopupModal(popupId, ref visible, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text($"The clip '{_animation.Name}' has unsaved changes.");
            ImGui.Spacing();

            if (ImGui.Button("Save and close"))
            {
                SaveCurrent();
                _closeConfirmPending = false;
                Close();
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Discard and close"))
            {
                _isDirty = false;
                _closeConfirmPending = false;
                Close();
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                _closeConfirmPending = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        if (!visible) _closeConfirmPending = false;
    }

    private void DrawToolbar()
    {
        if (ImGui.Button(Icons.New, new Vector2(30, 0))) RequestSwitch("");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("New clip");
        ImGui.SameLine(0, 3);
        if (ImGui.Button(Icons.Save, new Vector2(30, 0))) SaveCurrent();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Save (.anim)");

        ImGui.SameLine(0, 12);

        if (_isPlaying)
        {
            if (ImGui.Button(Icons.Pause, new Vector2(30, 0)))
                _isPlaying = false;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Pause");
        }
        else
        {
            if (ImGui.Button(Icons.Play, new Vector2(30, 0)))
            {
                _isPlaying = true;
                if (_playTime >= _animation.TotalDuration)
                    _playTime = 0;
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Play");
        }
        ImGui.SameLine(0, 3);

        if (ImGui.Button(Icons.Stop, new Vector2(30, 0)))
        {
            _isPlaying = false;
            _playTime = 0;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Stop (back to the start)");

        ImGui.SameLine(0, 12);
        ImGui.BeginDisabled(_undoStack.Count == 0);
        if (ImGui.Button(Icons.Undo, new Vector2(30, 0))) Undo();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Undo (Cmd+Z)");
        ImGui.SameLine(0, 3);
        ImGui.BeginDisabled(_redoStack.Count == 0);
        if (ImGui.Button(Icons.Redo, new Vector2(30, 0))) Redo();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Redo (Cmd+Shift+Z)");
        ImGui.SameLine();

        ImGui.SetNextItemWidth(74);
        int playMode = _animation.Loop ? 0 : 1;
        if (ImGui.Combo("##playMode", ref playMode, "Loop\0Once\0"))
        {
            _animation.Loop = playMode == 0;
            MarkEdited();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Loop repeats\nOnce stops on the last frame when it finishes (play once)");
        ImGui.SameLine();

        ImGui.Text("Sheet");
        ImGui.SameLine();
        string sheetPath = _atlasContentPath.Length > 0 ? _atlasContentPath : (_atlas?.TexturePath ?? "");
        string sheetLabel = _atlas == null ? "(none)" : Path.GetFileNameWithoutExtension(sheetPath);
        string sheetFace = $"{Icons.Image}  {sheetLabel}";
        float sheetW = Math.Clamp(ImGui.CalcTextSize(sheetFace).X + ImGui.GetStyle().FramePadding.X * 2 + 4,
                                  110f, 260f);
        if (ImGui.Button($"{sheetFace}###atlasField", new Vector2(sheetW, 0)))
        {
            _atlasPickerQuery = "";
            _atlasCache = null;
            ImGui.OpenPopup("###atlasPicker");
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip((sheetPath.Length > 0 ? sheetPath : "(no sheet)") + "\n\n"
                + "Click to swap, or drag a PNG here from the explorer\n"
                + "The frames (cell numbers) are kept - a PNG re-exported on the same grid just plays");
        AcceptTextureDrop();
        DrawAtlasPicker();
        ImGui.SameLine();

        ImGui.Text("Clip");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        if (ImGui.BeginCombo("##clipSwitch", _animation.Name))
        {
            foreach (var (clipName, clipPath) in ClipsInCurrentFolder())
            {
                bool current = clipPath == _currentPath;
                if (ImGui.Selectable($"{clipName}###clip_{clipPath}", current) && !current)
                    RequestSwitch(clipPath);
            }
            ImGui.Separator();
            if (ImGui.Selectable(Icons.Plus + "  New clip"))
                RequestSwitch("");
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("The clips in this folder - click to switch\n(swapping the sheet does not change this list)");
        ImGui.SameLine();

        ImGui.Text("Name");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(130);
        string name = _animation.Name;
        if (ImGui.InputText("##animName", ref name, 64))
        {
            _animation.Name = name;
            MarkEdited();
        }
    }

    private void AddFrame(int sliceIndex)
    {
        float dur = _animation.Frames.Count > 0 ? _animation.Frames[^1].Duration : 0.1f;
        _animation.Frames.Add(new FrameData
        {
            SliceIndex = sliceIndex,
            Duration = dur
        });
        _lastAddedSliceIndex = sliceIndex;
        MarkEdited();
    }

    private void DrawAtlasPicker()
    {
        ImGui.SetNextWindowSize(new Vector2(340, 380), ImGuiCond.Appearing);
        if (!ImGui.BeginPopup("###atlasPicker")) return;

        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##atlasQuery", Icons.Search + "  Search", ref _atlasPickerQuery, 128);
        ImGui.Separator();

        string currentRel = _atlasContentPath.Length > 0
            ? Path.ChangeExtension(_atlasContentPath, ".atlas")!.Replace('\\', '/')
            : "";

        ImGui.BeginChild("##atlasList", new Vector2(0, 0));
        foreach (var (name, atlasFull) in AtlasFiles())
        {
            if (_atlasPickerQuery.Length > 0 &&
                name.IndexOf(_atlasPickerQuery, StringComparison.OrdinalIgnoreCase) < 0) continue;

            bool current = string.Equals(EditorApp.ToContentRelative(atlasFull), currentRel,
                                         StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable($"{name}###atlas_{atlasFull}", current) && !current)
            {
                SwapAtlasFromFile(atlasFull);
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.EndChild();
        ImGui.EndPopup();
    }

    private List<(string Name, string Path)> AtlasFiles()
    {
        if (_atlasCache != null && (DateTime.Now - _atlasCacheTime).TotalSeconds < 5)
            return _atlasCache;

        var result = new List<(string Name, string Path)>();
        try
        {
            foreach (var f in Directory.GetFiles(ScanRoot, "*.atlas", SearchOption.AllDirectories))
            {
                var rel = EditorApp.ToContentRelative(f);
                result.Add((Path.ChangeExtension(rel, null) ?? rel, f));
            }
            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }
        catch {  }

        _atlasCache = result;
        _atlasCacheTime = DateTime.Now;
        return _atlasCache;
    }

    private void AcceptTextureDrop()
    {
        if (!ImGui.BeginDragDropTarget()) return;
        unsafe
        {
            var payload = ImGui.AcceptDragDropPayload("TEXTURE_FILE");
            if (payload.NativePtr != null)
            {
                var dragged = DraggedTexturePathProvider?.Invoke();
                if (!string.IsNullOrEmpty(dragged)) SwapAtlasFromFile(dragged);
            }
        }
        ImGui.EndDragDropTarget();
    }

    private void DrawAtlasSlices()
    {
        ImGui.TextDisabled("Click to add, Shift-click to add a range");
        ImGui.SameLine();
        if (ImGui.SmallButton("Add all") && _atlas != null)
        {
            for (int s = 0; s < _atlas.Slices.Count; s++)
                AddFrame(s);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Adds every slice as a frame, in order");

        ImGui.Separator();

        if (_atlas == null || _atlas.Texture == null)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "No sheet");
            ImGui.TextWrapped("Pick one from [Sheet] in the toolbar, or drag a PNG here from the explorer.");
            return;
        }

        int outOfRange = 0;
        foreach (var f in _animation.Frames)
            if (f.SliceIndex >= _atlas.Slices.Count) outOfRange++;
        if (outOfRange > 0)
        {
            ImGui.TextColored(new Vector4(1, 0.7f, 0.2f, 1),
                $"{Icons.Warning}  {outOfRange} frames are past this sheet's cell count ({_atlas.Slices.Count})");
            ImGui.Separator();
        }

        float windowRight = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;

        for (int i = 0; i < _atlas.Slices.Count; i++)
        {
            var slice = _atlas.Slices[i];

            float u0 = slice.X / (float)_atlas.Texture.Width;
            float v0 = slice.Y / (float)_atlas.Texture.Height;
            float u1 = (slice.X + slice.Width) / (float)_atlas.Texture.Width;
            float v1 = (slice.Y + slice.Height) / (float)_atlas.Texture.Height;

            float displayW = slice.Width * _zoom;
            float displayH = slice.Height * _zoom;

            ImGui.PushID(i);

            if (ImGui.ImageButton($"slice{i}", _atlasTextureId, new Vector2(displayW, displayH),
                new Vector2(u0, v0), new Vector2(u1, v1)))
            {
                if (ImGui.GetIO().KeyShift && _lastAddedSliceIndex >= 0)
                {
                    int dir = Math.Sign(i - _lastAddedSliceIndex);
                    if (dir == 0) AddFrame(i);
                    else
                        for (int s = _lastAddedSliceIndex + dir; dir > 0 ? s <= i : s >= i; s += dir)
                            AddFrame(s);
                }
                else if (ImGui.GetIO().KeyShift)
                {
                    for (int s = 0; s <= i; s++)
                        AddFrame(s);
                }
                else
                {
                    AddFrame(i);
                }
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"{slice.Name}\n{slice.Width}x{slice.Height}");
            }

            ImGui.PopID();

            if (i + 1 < _atlas.Slices.Count)
            {
                float nextW = _atlas.Slices[i + 1].Width * _zoom;
                float nextEnd = ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + nextW;
                if (nextEnd < windowRight)
                    ImGui.SameLine();
            }
        }
    }

    private void DrawPreview()
    {
        ImGui.Text("Preview");

        if (_atlas?.Texture == null || _animation.Frames.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No frames");
            return;
        }

        int frameIndex = _animation.GetFrameAtTime(_playTime);
        if (frameIndex < 0 || frameIndex >= _animation.Frames.Count) return;

        var frame = _animation.Frames[frameIndex];
        if (frame.SliceIndex >= _atlas.Slices.Count) return;

        var slice = _atlas.Slices[frame.SliceIndex];

        float u0 = slice.X / (float)_atlas.Texture.Width;
        float v0 = slice.Y / (float)_atlas.Texture.Height;
        float u1 = (slice.X + slice.Width) / (float)_atlas.Texture.Width;
        float v1 = (slice.Y + slice.Height) / (float)_atlas.Texture.Height;

        float previewScale = 3f;
        var imgSize = new Vector2(slice.Width * previewScale, slice.Height * previewScale);

        var drawList = ImGui.GetWindowDrawList();
        var imgPos = ImGui.GetCursorScreenPos();
        EditorTheme.DrawCheckerboard(drawList, imgPos, imgSize);
        ImGui.Image(_atlasTextureId, imgSize, new Vector2(u0, v0), new Vector2(u1, v1));
        drawList.AddRect(imgPos, imgPos + imgSize, ImGui.GetColorU32(ImGuiCol.Border));

        ImGui.SameLine();
        ImGui.Text($"Frame {frameIndex + 1}/{_animation.Frames.Count}\nTime: {_playTime:F2}s / {_animation.TotalDuration:F2}s");
    }

    private const float TimelinePps = 480f;
    private const float TimelineMinCellW = 28f;
    private const float TimelineCellH = 56f;

    private float StripXAtTime(float time, float[] widths)
    {
        float x = 0, t = 0;
        for (int i = 0; i < widths.Length; i++)
        {
            float d = _animation.Frames[i].Duration;
            if (time < t + d)
            {
                float frac = d > 0 ? Math.Clamp((time - t) / d, 0f, 1f) : 0f;
                return x + frac * widths[i];
            }
            x += widths[i];
            t += d;
        }
        return x;
    }

    private float TimeAtStripX(float xPos, float[] widths)
    {
        float x = 0, t = 0;
        for (int i = 0; i < widths.Length; i++)
        {
            if (xPos < x + widths[i])
            {
                float frac = Math.Clamp((xPos - x) / widths[i], 0f, 1f);
                return t + frac * _animation.Frames[i].Duration;
            }
            x += widths[i];
            t += _animation.Frames[i].Duration;
        }
        return MathF.Max(0f, _animation.TotalDuration - 0.0001f);
    }

    private void DrawTimeline()
    {
        ImGui.Text("Timeline");
        if (_animation.Frames.Count > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"{_animation.TotalDuration:F2}s · {_animation.Frames.Count} frames");

            ImGui.SameLine();
            ImGui.Text("FPS");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(84);
            float d0 = _animation.Frames[0].Duration;
            bool uniform = true;
            foreach (var f in _animation.Frames)
                if (MathF.Abs(f.Duration - d0) > 0.0005f) { uniform = false; break; }
            int fps = Math.Max(1, (int)MathF.Round(1f / MathF.Max(0.001f, d0)));
            if (ImGui.InputInt("##clipFps", ref fps))
            {
                fps = Math.Clamp(fps, 1, 60);
                foreach (var f in _animation.Frames)
                    f.Duration = 1f / fps;
                MarkEdited();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(uniform
                    ? "The clip's playback speed."
                    : "This clip has per-frame lengths.\nEntering an FPS makes them uniform.");
            if (!uniform)
            {
                ImGui.SameLine();
                ImGui.TextColored(EditorTheme.Accent, "variable");
            }
        }

        if (_animation.Frames.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "Click sprites on the left to add frames");
            return;
        }

        int count = _animation.Frames.Count;
        var widths = new float[count];
        float totalWidth = 0;
        for (int i = 0; i < count; i++)
        {
            widths[i] = MathF.Max(TimelineMinCellW, _animation.Frames[i].Duration * TimelinePps);
            totalWidth += widths[i];
        }

        float labelH = ImGui.GetTextLineHeight();
        float stripH = TimelineCellH + labelH + 4;

        ImGui.BeginChild("TimelineStrip", new Vector2(0, stripH + ImGui.GetStyle().ScrollbarSize),
            ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);

        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();

        int currentFrame = _animation.GetFrameAtTime(_playTime);
        uint accentCol = ImGui.GetColorU32(ImGuiCol.CheckMark);
        uint borderCol = ImGui.GetColorU32(ImGuiCol.Border);
        uint dimTextCol = ImGui.GetColorU32(ImGuiCol.TextDisabled);

        int deleteIndex = -1;
        int duplicateIndex = -1;
        int moveFrom = -1, moveTo = -1;

        float x = 0;
        for (int i = 0; i < count; i++)
        {
            var frame = _animation.Frames[i];
            float cellW = widths[i];
            var cellPos = origin + new Vector2(x, 0);
            var cellSize = new Vector2(cellW, TimelineCellH);

            ImGui.PushID(i);
            ImGui.SetCursorScreenPos(cellPos);
            bool clicked = ImGui.InvisibleButton("cell", cellSize);
            bool hovered = ImGui.IsItemHovered();

            if (hovered && _atlas != null && frame.SliceIndex >= 0 && frame.SliceIndex < _atlas.Slices.Count)
                ImGui.SetTooltip($"Slice #{frame.SliceIndex} · {_atlas.Slices[frame.SliceIndex].Name}\n{frame.Duration:F2}s"
                    + (string.IsNullOrEmpty(frame.EventName) ? "" : $"\nEvent {frame.EventName}"));

            if (ImGui.BeginPopupContextItem())
            {
                if (ImGui.MenuItem("Delete")) deleteIndex = i;
                if (ImGui.MenuItem("Duplicate")) duplicateIndex = i;
                ImGui.EndPopup();
            }

            if (ImGui.BeginDragDropSource())
            {
                _draggedFrameIndex = i;
                ImGui.SetDragDropPayload("ANIM_FRAME", IntPtr.Zero, 0);
                ImGui.Text($"{Icons.Film}  Frame {i + 1}");
                ImGui.EndDragDropSource();
            }
            if (ImGui.BeginDragDropTarget())
            {
                var framePayload = ImGui.AcceptDragDropPayload("ANIM_FRAME");
                unsafe
                {
                    if (framePayload.NativePtr != null && _draggedFrameIndex >= 0 && _draggedFrameIndex != i)
                    {
                        moveFrom = _draggedFrameIndex;
                        moveTo = i;
                        _draggedFrameIndex = -1;
                    }
                }
                ImGui.EndDragDropTarget();
            }

            uint cellBg = ImGui.GetColorU32(hovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg);
            drawList.AddRectFilled(cellPos, cellPos + cellSize, cellBg);
            if (i == currentFrame)
                drawList.AddRectFilled(cellPos, cellPos + cellSize, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.04f)));

            if (_atlas?.Texture != null && frame.SliceIndex >= 0 && frame.SliceIndex < _atlas.Slices.Count)
            {
                var slice = _atlas.Slices[frame.SliceIndex];
                float thumbMax = TimelineCellH - 8;
                float scale = thumbMax / MathF.Max(slice.Width, slice.Height);
                if (scale >= 1f) scale = MathF.Floor(scale);
                var thumbSize = new Vector2(slice.Width * scale, slice.Height * scale);
                var thumbPos = cellPos + new Vector2(
                    MathF.Min(4f, MathF.Max(0f, (cellW - thumbSize.X) / 2f)),
                    (TimelineCellH - thumbSize.Y) / 2f);

                float u0 = slice.X / (float)_atlas.Texture.Width;
                float v0 = slice.Y / (float)_atlas.Texture.Height;
                float u1 = (slice.X + slice.Width) / (float)_atlas.Texture.Width;
                float v1 = (slice.Y + slice.Height) / (float)_atlas.Texture.Height;

                drawList.PushClipRect(cellPos, cellPos + cellSize, true);
                EditorTheme.DrawCheckerboard(drawList, thumbPos, thumbSize, 6f);
                drawList.AddImage(_atlasTextureId, thumbPos, thumbPos + thumbSize,
                    new Vector2(u0, v0), new Vector2(u1, v1));
                drawList.PopClipRect();
            }

            ImGui.PushFont(ImGuiRenderer.MonoFont);
            drawList.AddText(cellPos + new Vector2(4f, 2f), dimTextCol, $"{i + 1}");
            string sliceLabel = $"s{frame.SliceIndex}";
            var sliceLabelSize = ImGui.CalcTextSize(sliceLabel);
            drawList.AddText(cellPos + new Vector2(cellW - sliceLabelSize.X - 4f, 2f), dimTextCol, sliceLabel);
            string durLabel = $"{frame.Duration:F2}";
            var durLabelSize = ImGui.CalcTextSize(durLabel);
            drawList.AddText(cellPos + new Vector2((cellW - durLabelSize.X) / 2f, TimelineCellH + 2f),
                dimTextCol, durLabel);

            if (!string.IsNullOrEmpty(frame.EventName))
            {
                drawList.PushClipRect(cellPos, cellPos + cellSize, true);
                drawList.AddText(cellPos + new Vector2(4f, TimelineCellH - ImGui.GetTextLineHeight() - 2f),
                    accentCol, frame.EventName);
                drawList.PopClipRect();

                float tickX = cellPos.X + cellW * 0.5f;
                drawList.AddLine(new Vector2(tickX, cellPos.Y), new Vector2(tickX, cellPos.Y + 6f), accentCol, 2f);
            }
            ImGui.PopFont();

            drawList.AddRect(cellPos, cellPos + cellSize, borderCol);
            if (i == _selectedFrameIndex)
                drawList.AddRect(cellPos, cellPos + cellSize, accentCol, 0f, ImDrawFlags.None, 2f);

            if (clicked)
            {
                _selectedFrameIndex = i;
                if (!_isPlaying)
                {
                    float start = 0;
                    for (int j = 0; j < i; j++) start += _animation.Frames[j].Duration;
                    _playTime = start;
                }
            }

            ImGui.PopID();
            x += cellW;
        }

        float headX = StripXAtTime(_playTime, widths);
        drawList.AddLine(origin + new Vector2(headX, 0), origin + new Vector2(headX, TimelineCellH), accentCol, 1.5f);
        drawList.AddTriangleFilled(
            origin + new Vector2(headX - 4f, 0),
            origin + new Vector2(headX + 4f, 0),
            origin + new Vector2(headX, 6f), accentCol);

        ImGui.SetCursorScreenPos(origin + new Vector2(0, TimelineCellH));
        ImGui.InvisibleButton("##scrubStrip", new Vector2(MathF.Max(totalWidth, 1f), labelH + 4));
        if (ImGui.IsItemHovered() || ImGui.IsItemActive())
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
        if (ImGui.IsItemActive())
        {
            _isPlaying = false;
            _playTime = TimeAtStripX(ImGui.GetMousePos().X - origin.X, widths);
            _selectedFrameIndex = _animation.GetFrameAtTime(_playTime);
        }

        ImGui.EndChild();

        if (deleteIndex >= 0)
        {
            _animation.Frames.RemoveAt(deleteIndex);
            MarkEdited();
            if (_selectedFrameIndex >= _animation.Frames.Count)
                _selectedFrameIndex = _animation.Frames.Count - 1;
        }
        if (duplicateIndex >= 0)
        {
            var src = _animation.Frames[duplicateIndex];
            _animation.Frames.Insert(duplicateIndex + 1, new FrameData
            {
                SliceIndex = src.SliceIndex,
                Duration = src.Duration,
                EventName = src.EventName
            });
            MarkEdited();
        }

        if (moveFrom >= 0 && moveTo >= 0 && moveFrom != moveTo
            && moveFrom < _animation.Frames.Count && moveTo < _animation.Frames.Count)
        {
            var moved = _animation.Frames[moveFrom];
            _animation.Frames.RemoveAt(moveFrom);
            _animation.Frames.Insert(moveTo, moved);
            _selectedFrameIndex = moveTo;
            MarkEdited();
        }
    }

    private void DrawFrameProperties()
    {
        if (_selectedFrameIndex < 0 || _selectedFrameIndex >= _animation.Frames.Count)
        {
            ImGui.Text("Select a frame to edit");
            return;
        }

        var frame = _animation.Frames[_selectedFrameIndex];

        ImGui.Text($"Frame {_selectedFrameIndex + 1} Properties");

        int sliceIndex = frame.SliceIndex;
        ImGui.SetNextItemWidth(80);
        if (ImGui.InputInt("Slice Index", ref sliceIndex))
        {
            if (_atlas != null)
            {
                frame.SliceIndex = Math.Clamp(sliceIndex, 0, _atlas.Slices.Count - 1);
                MarkEdited();
            }
        }

        string eventName = frame.EventName ?? "";
        ImGui.SetNextItemWidth(150);
        if (ImGui.InputText("Event", ref eventName, 64))
        {
            frame.EventName = string.IsNullOrWhiteSpace(eventName) ? null : eventName;
            MarkEdited();
        }
    }

    public event Action<string>? OnSaved;

    public bool IsOpen => _isOpen;

    public AnimationData? CurrentAnimation => _animation;
}

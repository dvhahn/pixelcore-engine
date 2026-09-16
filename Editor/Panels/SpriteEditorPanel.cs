using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using ImGuiNET;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PixelCore.Runtime.Assets;
using PixelCore.Editor.Commands;

namespace PixelCore.Editor.Panels;

public class SpriteEditorPanel
{
    private readonly GraphicsDevice _graphicsDevice;

    private SpriteAtlas? _atlas;
    private string _currentPath = "";
    private bool _isOpen;
    private bool _isDirty;
    private bool _closeConfirmPending;

    private IntPtr _textureId;
    private Texture2D? _boundTexture;

    private int _selectedSliceIndex = -1;

    private float _sliceInspectorHeight;

    private static float StepperPairWidth(string labelA, string labelB)
    {
        float inner = ImGui.GetStyle().ItemInnerSpacing.X;
        float labels = ImGui.CalcTextSize(labelA).X + ImGui.CalcTextSize(labelB).X;
        float avail = ImGui.GetContentRegionAvail().X;
        float w = (avail - labels - inner * 2f - ImGui.GetStyle().ItemSpacing.X) * 0.5f;
        return MathF.Max(w, ImGui.GetFrameHeight() * 2f + inner * 2f + 18f);
    }

    private readonly struct TightInnerSpacing : IDisposable
    {
        public TightInnerSpacing()
        {
            var st = ImGui.GetStyle();
            ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, new System.Numerics.Vector2(2f, st.ItemInnerSpacing.Y));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new System.Numerics.Vector2(4f, st.FramePadding.Y));
        }
        public void Dispose() => ImGui.PopStyleVar(2);
    }
    private float _zoom = 1f;
    private float _zoomLevel = float.NaN;
    private int _prevScrollWheelValue;
    private int _frameScrollDelta;
    private Vector2 _scrollOffset;
    private bool _fitRequest = true;
    private bool _oneToOneRequest;

    private bool _isDraggingPivot;
    private float _pivotOrigX, _pivotOrigY;

    private int _gridWidth = 16;
    private int _gridHeight = 16;
    private int _offsetX, _offsetY;
    private int _spacingX, _spacingY;

    private int _draggedSliceIndex = -1;

    private bool _isDraggingSelection;
    private Vector2 _dragStartTex;
    private Vector2 _dragEndTex;

    private bool _isPanning;
    private Vector2 _panStartMouse;
    private Vector2 _panStartOffset;

    private enum ResizeEdge { None, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }
    private ResizeEdge _resizeEdge = ResizeEdge.None;
    private bool _isResizing;
    private SpriteSlice? _resizingSlice;
    private int _resizeOrigX, _resizeOrigY, _resizeOrigW, _resizeOrigH;

    private bool _isMovingSlice;
    private int _moveOrigX, _moveOrigY;
    private Vector2 _moveStartMouse;

    private readonly CommandHistory _commandHistory = new();

    public SpriteEditorPanel(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice;
    }

    public void SetTextureBinding(Func<Texture2D, IntPtr> bindTexture)
    {
        _bindTextureFunc = bindTexture;
    }
    private Func<Texture2D, IntPtr>? _bindTextureFunc;

    public event Action<SpriteAtlas>? OnUseInAnimation;

    public event Action<string>? OnSaved;

    public void SelectSliceForShot(int index)
    {
        if (_atlas != null && index >= 0 && index < _atlas.Slices.Count) _selectedSliceIndex = index;
    }

    public void OpenTexture(string pngPath)
    {
        if (_isDirty && _atlas != null && _currentPath == pngPath)
        {
            _isOpen = true;
            return;
        }

        _currentPath = pngPath;
        _isOpen = true;
        _isDirty = false;

        var atlasPath = Path.ChangeExtension(pngPath, ".atlas");
        _atlas = SpriteAtlas.Load(atlasPath);

        if (_atlas == null)
        {
            _atlas = new SpriteAtlas
            {
                TexturePath = Path.GetFileName(pngPath)
            };
        }

        var contentPath = Path.GetDirectoryName(pngPath) ?? "";
        _atlas.LoadTexture(_graphicsDevice, contentPath);

        BindTexture();

        _gridWidth = _atlas.CellWidth;
        _gridHeight = _atlas.CellHeight;
        _offsetX = _atlas.OffsetX;
        _offsetY = _atlas.OffsetY;
        _spacingX = _atlas.SpacingX;
        _spacingY = _atlas.SpacingY;

        _selectedSliceIndex = -1;
        _scrollOffset = Vector2.Zero;
        _zoomLevel = float.NaN;
        _fitRequest = true;
    }

    private void BindTexture()
    {
        if (_atlas?.Texture != null && _bindTextureFunc != null)
        {
            if (_boundTexture != _atlas.Texture)
            {
                _textureId = _bindTextureFunc(_atlas.Texture);
                _boundTexture = _atlas.Texture;
            }
        }
    }

    public void Hide() => _isOpen = false;

    public bool IsDirty => _isDirty;

    public void SaveIfDirty()
    {
        if (_isDirty) SaveAtlas();
    }

    public void Close()
    {
        _isOpen = false;
        _isDirty = false;
        _atlas = null;
        _selectedSliceIndex = -1;
    }

    public void Draw(uint dockId = 0, bool forceDock = false)
    {
        var mouseState = Mouse.GetState();
        _frameScrollDelta = mouseState.ScrollWheelValue - _prevScrollWheelValue;
        _prevScrollWheelValue = mouseState.ScrollWheelValue;

        if (!_isOpen || _atlas == null) return;

        if (dockId != 0)
        {
            EditorWidgets.DocWindowClass();
            ImGui.SetNextWindowDockID(dockId, ImGuiCond.Always);
        }
        _ = forceDock;

        ImGui.SetNextWindowSize(new Vector2(800, 600), ImGuiCond.FirstUseEver);

        bool open = _isOpen;
        var wflags = _isDirty ? ImGuiWindowFlags.UnsavedDocument : ImGuiWindowFlags.None;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, EditorWidgets.DocTabPadding);
        bool docVisible = ImGui.Begin($"Sprite Editor - {Path.GetFileName(_currentPath)}###SpriteEditor", ref open, wflags);
        ImGui.PopStyleVar();
        if (docVisible)
        {
            if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
            {
                var io = ImGui.GetIO();
                bool cmdOrCtrl = io.KeySuper || io.KeyCtrl;

                if (cmdOrCtrl && ImGui.IsKeyPressed(ImGuiKey.Z))
                {
                    if (io.KeyShift)
                    {
                        if (_commandHistory.CanRedo) { _commandHistory.Redo(); _isDirty = true; }
                    }
                    else
                    {
                        if (_commandHistory.CanUndo) { _commandHistory.Undo(); _isDirty = true; }
                    }
                }
            }

            DrawToolbar();
            ImGui.Separator();

            float leftWidth = ImGui.GetContentRegionAvail().X * 0.7f;

            ImGui.BeginChild("TextureView", new Vector2(leftWidth, 0), ImGuiChildFlags.FrameStyle);
            DrawTextureView();
            ImGui.EndChild();

            ImGui.SameLine();

            ImGui.BeginChild("SliceList", new Vector2(0, 0), ImGuiChildFlags.FrameStyle);
            DrawSliceList();
            ImGui.EndChild();
        }
        ImGui.End();

        if (!open)
        {
            if (_isDirty) _closeConfirmPending = true;
            else Close();
        }
        DrawCloseConfirm();
    }

    private void DrawCloseConfirm()
    {
        if (!_closeConfirmPending) return;

        const string popupId = "Unsaved changes###SpriteCloseConfirm";
        if (!ImGui.IsPopupOpen(popupId)) ImGui.OpenPopup(popupId);

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        bool visible = true;
        if (ImGui.BeginPopupModal(popupId, ref visible, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text($"'{Path.GetFileName(_currentPath)}' has unsaved slice changes.");
            ImGui.Spacing();

            if (ImGui.Button("Save and close"))
            {
                SaveAtlas();
                _closeConfirmPending = false;
                Close();
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Discard and close"))
            {
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

    private void SaveAtlas()
    {
        if (_atlas == null) return;
        var atlasPath = Path.ChangeExtension(_currentPath, ".atlas");

        if (string.IsNullOrEmpty(_atlas.Id))
            _atlas.Id = AssetRegistry.Instance.GetOrCreateId(EditorApp.ToContentRelative(atlasPath));

        _atlas.Save(atlasPath);
        _isDirty = false;
        OnSaved?.Invoke(atlasPath);
        Runtime.Assets.AssetEvents.RaiseSaved(EditorApp.ToContentRelative(atlasPath), _atlas.Id);
    }

    internal void SelfTestSaveAtlas(SpriteAtlas atlas, string pngPath)
    {
        _atlas = atlas;
        _currentPath = pngPath;
        SaveAtlas();
    }

    private void DrawToolbar()
    {
        DrawModeButton("Single", SliceMode.Single);
        ImGui.SameLine(0, 3);
        DrawModeButton("Grid", SliceMode.Grid);
        ImGui.SameLine(0, 3);
        DrawModeButton("Manual", SliceMode.Manual);

        if (_atlas!.Mode == SliceMode.Grid)
        {
            ImGui.SameLine(0, 14);
            ImGui.SetNextItemWidth(52);
            if (ImGui.InputInt("W", ref _gridWidth, 0)) _gridWidth = Math.Max(1, _gridWidth);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(52);
            if (ImGui.InputInt("H", ref _gridHeight, 0)) _gridHeight = Math.Max(1, _gridHeight);
            ImGui.SameLine();
            ImGui.Text("Off");
            ImGui.SameLine(); ImGui.SetNextItemWidth(42);
            if (ImGui.InputInt("##offX", ref _offsetX, 0)) _offsetX = Math.Max(0, _offsetX);
            ImGui.SameLine(); ImGui.SetNextItemWidth(42);
            if (ImGui.InputInt("##offY", ref _offsetY, 0)) _offsetY = Math.Max(0, _offsetY);
            ImGui.SameLine(); ImGui.Text("Gap");
            ImGui.SameLine(); ImGui.SetNextItemWidth(42);
            if (ImGui.InputInt("##gapX", ref _spacingX, 0)) _spacingX = Math.Max(0, _spacingX);
            ImGui.SameLine(); ImGui.SetNextItemWidth(42);
            if (ImGui.InputInt("##gapY", ref _spacingY, 0)) _spacingY = Math.Max(0, _spacingY);
            ImGui.SameLine();
            if (ImGui.Button("Apply"))
            {
                _atlas.CellWidth = _gridWidth;
                _atlas.CellHeight = _gridHeight;
                _atlas.OffsetX = _offsetX;
                _atlas.OffsetY = _offsetY;
                _atlas.SpacingX = _spacingX;
                _atlas.SpacingY = _spacingY;
                _atlas.ApplyGridSlicing();
                _isDirty = true;
            }
        }

        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float clusterW = 34f + 30f * 5 + 6f + 24f * 2 + spacing;
        ImGui.SameLine();
        float rightAlign = ImGui.GetContentRegionAvail().X - clusterW;
        if (rightAlign > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + rightAlign);

        if (ImGui.Button("1:1", new Vector2(34, 0))) _oneToOneRequest = true;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Actual size (100%)");
        ImGui.SameLine(0, 3);
        if (ImGui.Button(Icons.Expand, new Vector2(30, 0))) _fitRequest = true;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Fit to screen");

        ImGui.SameLine(0, 12);
        if (ImGui.Button(Icons.Film, new Vector2(30, 0))) OnUseInAnimation?.Invoke(_atlas);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Send to the animation editor");
        ImGui.SameLine(0, 3);
        if (ImGui.Button(Icons.Save, new Vector2(30, 0))) SaveAtlas();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Save (.atlas)");

        ImGui.SameLine(0, 12);
        ImGui.BeginDisabled(!_commandHistory.CanUndo);
        if (ImGui.Button(Icons.Undo, new Vector2(30, 0))) { _commandHistory.Undo(); _isDirty = true; }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Undo (Cmd+Z)");
        ImGui.SameLine(0, 3);
        ImGui.BeginDisabled(!_commandHistory.CanRedo);
        if (ImGui.Button(Icons.Redo, new Vector2(30, 0))) { _commandHistory.Redo(); _isDirty = true; }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Redo (Cmd+Shift+Z)");
    }

    private void DrawModeButton(string label, SliceMode mode)
    {
        bool on = _atlas!.Mode == mode;
        if (on)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, EditorTheme.AccentSoft);
            ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Accent);
        }
        if (ImGui.Button(label) && !on) SetMode(mode);
        if (on) ImGui.PopStyleColor(2);
    }

    private void SetMode(SliceMode mode)
    {
        _atlas!.Mode = mode;
        _isDirty = true;
        switch (mode)
        {
            case SliceMode.Single:
                _atlas.Slices.Clear();
                if (_atlas.Texture != null)
                {
                    _atlas.Slices.Add(new SpriteSlice
                    {
                        Name = Path.GetFileNameWithoutExtension(_currentPath),
                        X = 0, Y = 0,
                        Width = _atlas.Texture.Width,
                        Height = _atlas.Texture.Height
                    });
                }
                break;
            case SliceMode.Manual:
                _atlas.Slices.Clear();
                _selectedSliceIndex = -1;
                break;
        }
    }

    private void DrawTextureView()
    {
        if (_atlas?.Texture == null)
        {
            ImGui.TextDisabled("No texture");
            return;
        }

        var texture = _atlas.Texture;
        var baseCursorPos = ImGui.GetCursorScreenPos();
        var canvasSize = ImGui.GetContentRegionAvail();

        if (_fitRequest && canvasSize.X > 16 && canvasSize.Y > 16)
        {
            FitToCanvas(texture, canvasSize);
            _fitRequest = false;
        }
        if (_oneToOneRequest)
        {
            _zoom = 1f;
            _zoomLevel = float.NaN;
            _scrollOffset = new Vector2(
                (canvasSize.X - texture.Width) * 0.5f,
                (canvasSize.Y - texture.Height) * 0.5f);
            _oneToOneRequest = false;
        }

        var displaySize = new Vector2(texture.Width * _zoom, texture.Height * _zoom);
        var canvasPos = baseCursorPos + _scrollOffset;

        ImGui.InvisibleButton("##canvas", canvasSize.Y > 0 ? canvasSize : new Vector2(displaySize.X, displaySize.Y));
        bool isCanvasHovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(baseCursorPos, baseCursorPos + canvasSize, ImGui.GetColorU32(EditorTheme.Recessed));
        EditorTheme.DrawCheckerboard(drawList, canvasPos, displaySize);
        drawList.AddImage(_textureId, canvasPos, canvasPos + displaySize);
        drawList.AddRect(canvasPos, canvasPos + displaySize, ImGui.GetColorU32(ImGuiCol.Border));

        if (_atlas.Mode == SliceMode.Grid && _gridWidth > 0 && _gridHeight > 0)
        {
            uint gridColor = ImGui.GetColorU32(ImGuiCol.TextDisabled, 0.45f);
            int stepX = _gridWidth + _spacingX;
            int stepY = _gridHeight + _spacingY;
            if (stepX > 0 && stepY > 0)
            {
                var clipMin = drawList.GetClipRectMin();
                var clipMax = drawList.GetClipRectMax();
                for (int gy = _offsetY; gy + _gridHeight <= texture.Height; gy += stepY)
                {
                    float cy = canvasPos.Y + gy * _zoom;
                    if (cy > clipMax.Y) break;
                    if (cy + _gridHeight * _zoom < clipMin.Y) continue;
                    for (int gx = _offsetX; gx + _gridWidth <= texture.Width; gx += stepX)
                    {
                        float cx = canvasPos.X + gx * _zoom;
                        if (cx > clipMax.X) break;
                        if (cx + _gridWidth * _zoom < clipMin.X) continue;
                        var cmin = new Vector2(cx, cy);
                        var cmax = new Vector2(cx + _gridWidth * _zoom, cy + _gridHeight * _zoom);
                        drawList.AddRect(cmin, cmax, gridColor);
                    }
                }
            }
        }

        uint accent = ImGui.GetColorU32(EditorTheme.Accent);
        uint sliceCol = ImGui.GetColorU32(new Vector4(
            EditorTheme.Prefab.X, EditorTheme.Prefab.Y, EditorTheme.Prefab.Z, 0.55f));
        for (int i = 0; i < _atlas.Slices.Count; i++)
        {
            var slice = _atlas.Slices[i];
            var min = new Vector2(canvasPos.X + slice.X * _zoom, canvasPos.Y + slice.Y * _zoom);
            var max = new Vector2(min.X + slice.Width * _zoom, min.Y + slice.Height * _zoom);
            bool sel = i == _selectedSliceIndex;
            drawList.AddRect(min, max, sel ? accent : sliceCol, 0, ImDrawFlags.None, sel ? 2f : 1f);
        }

        bool pivotBusy = HandlePivotDrag(canvasPos, texture, isCanvasHovered);
        if (!pivotBusy)
        {
            if (_atlas.Mode == SliceMode.Manual)
                HandleManualSliceCreation(canvasPos, texture, isCanvasHovered);
            else if (isCanvasHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                SelectSliceAtMouse(canvasPos);
        }

        if (_isDraggingSelection)
        {
            var selMin = new Vector2(
                canvasPos.X + Math.Min(_dragStartTex.X, _dragEndTex.X) * _zoom,
                canvasPos.Y + Math.Min(_dragStartTex.Y, _dragEndTex.Y) * _zoom);
            var selMax = new Vector2(
                canvasPos.X + Math.Max(_dragStartTex.X, _dragEndTex.X) * _zoom,
                canvasPos.Y + Math.Max(_dragStartTex.Y, _dragEndTex.Y) * _zoom);

            drawList.AddRectFilled(selMin, selMax, ImGui.GetColorU32(new Vector4(
                EditorTheme.Accent.X, EditorTheme.Accent.Y, EditorTheme.Accent.Z, 0.15f)));
            drawList.AddRect(selMin, selMax, accent, 0, ImDrawFlags.None, 1.5f);
        }

        if (_selectedSliceIndex >= 0 && _selectedSliceIndex < _atlas.Slices.Count)
        {
            var slice = _atlas.Slices[_selectedSliceIndex];
            DrawResizeHandles(drawList, canvasPos, slice);
            if (!pivotBusy) HandleSliceResize(canvasPos, texture, slice);
            DrawPivotMarker(drawList, canvasPos, slice);
            DrawSliceTag(drawList, canvasPos, slice);
        }

        float chipX = baseCursorPos.X + 10;
        float chipY = baseCursorPos.Y + 10;
        chipX += SceneViewPanel.DrawHudChip(drawList, new Vector2(chipX, chipY), $"{_zoom * 100:0}%") + 6;
        chipX += SceneViewPanel.DrawHudChip(drawList, new Vector2(chipX, chipY),
            $"{texture.Width} × {texture.Height}") + 6;
        if (isCanvasHovered)
        {
            var mp = ImGui.GetMousePos();
            int px = (int)MathF.Floor((mp.X - canvasPos.X) / _zoom);
            int py = (int)MathF.Floor((mp.Y - canvasPos.Y) / _zoom);
            if (px >= 0 && py >= 0 && px < texture.Width && py < texture.Height)
                SceneViewPanel.DrawHudChip(drawList, new Vector2(chipX, chipY), $"{px}, {py}");
        }

        HandlePanning(isCanvasHovered);

        HandleZoom(isCanvasHovered, baseCursorPos);
    }

    private void FitToCanvas(Texture2D texture, Vector2 canvas)
    {
        float fit = MathF.Min((canvas.X - 16) / texture.Width, (canvas.Y - 16) / texture.Height);
        float z = fit >= 1f ? MathF.Max(1f, MathF.Floor(fit)) : MathF.Max(0.25f, MathF.Floor(fit * 4f) / 4f);
        _zoom = Math.Clamp(z, 0.25f, 32f);
        _zoomLevel = float.NaN;
        _scrollOffset = new Vector2(
            (canvas.X - texture.Width * _zoom) * 0.5f,
            (canvas.Y - texture.Height * _zoom) * 0.5f);
    }

    private void HandleZoom(bool isHovered, Vector2 canvasOrigin)
    {
        if (!isHovered || !ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)) return;

        if (_frameScrollDelta == 0) return;

        var mousePos = ImGui.GetMousePos();
        float texX = (mousePos.X - canvasOrigin.X - _scrollOffset.X) / _zoom;
        float texY = (mousePos.Y - canvasOrigin.Y - _scrollOffset.Y) / _zoom;

        if (float.IsNaN(_zoomLevel) || Math.Abs(SceneViewPanel.LevelToZoom(MathF.Round(_zoomLevel)) - _zoom) > 0.001f)
            _zoomLevel = SceneViewPanel.ZoomToLevel(_zoom);
        _zoomLevel = Math.Clamp(_zoomLevel + ImGuiRenderer.TameScrollDelta(_frameScrollDelta) / 120f,
            SceneViewPanel.ZoomToLevel(0.25f), SceneViewPanel.ZoomToLevel(32f));
        _zoom = SceneViewPanel.LevelToZoom(MathF.Round(_zoomLevel));

        _scrollOffset.X = mousePos.X - canvasOrigin.X - texX * _zoom;
        _scrollOffset.Y = mousePos.Y - canvasOrigin.Y - texY * _zoom;
    }

    private bool HandlePivotDrag(Vector2 canvasPos, Texture2D texture, bool isHovered)
    {
        if (_selectedSliceIndex < 0 || _selectedSliceIndex >= _atlas!.Slices.Count)
            return false;

        var slice = _atlas.Slices[_selectedSliceIndex];
        var mouse = ImGui.GetMousePos();
        var pivotScreen = new Vector2(
            canvasPos.X + (slice.X + slice.PivotX * slice.Width) * _zoom,
            canvasPos.Y + (slice.Y + slice.PivotY * slice.Height) * _zoom);

        if (!_isDraggingPivot && isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left)
            && Vector2.Distance(mouse, pivotScreen) < 8f)
        {
            _isDraggingPivot = true;
            _pivotOrigX = slice.PivotX;
            _pivotOrigY = slice.PivotY;
        }

        if (_isDraggingPivot)
        {
            float texX = (mouse.X - canvasPos.X) / _zoom - slice.X;
            float texY = (mouse.Y - canvasPos.Y) / _zoom - slice.Y;
            int px = (int)Math.Clamp(MathF.Round(texX), 0, slice.Width);
            int py = (int)Math.Clamp(MathF.Round(texY), 0, slice.Height);
            slice.PivotX = slice.Width > 0 ? px / (float)slice.Width : 0.5f;
            slice.PivotY = slice.Height > 0 ? py / (float)slice.Height : 0.5f;

            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                if (Math.Abs(slice.PivotX - _pivotOrigX) > 0.0001f ||
                    Math.Abs(slice.PivotY - _pivotOrigY) > 0.0001f)
                {
                    _commandHistory.AddExecuted(new SetPivotCommand(slice,
                        _pivotOrigX, _pivotOrigY, slice.PivotX, slice.PivotY));
                    _isDirty = true;
                }
                _isDraggingPivot = false;
            }
            return true;
        }
        return false;
    }

    private void DrawPivotMarker(ImDrawListPtr drawList, Vector2 canvasPos, SpriteSlice slice)
    {
        var p = new Vector2(
            canvasPos.X + (slice.X + slice.PivotX * slice.Width) * _zoom,
            canvasPos.Y + (slice.Y + slice.PivotY * slice.Height) * _zoom);
        uint amber = ImGui.GetColorU32(EditorTheme.Accent);
        uint outline = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f));
        const float r = 5f;

        drawList.AddCircle(p, r + 1f, outline, 0, 3.5f);
        drawList.AddLine(new Vector2(p.X - r - 5, p.Y), new Vector2(p.X + r + 5, p.Y), outline, 3.5f);
        drawList.AddLine(new Vector2(p.X, p.Y - r - 5), new Vector2(p.X, p.Y + r + 5), outline, 3.5f);
        drawList.AddCircle(p, r, amber, 0, 1.5f);
        drawList.AddLine(new Vector2(p.X - r - 4, p.Y), new Vector2(p.X + r + 4, p.Y), amber, 1.5f);
        drawList.AddLine(new Vector2(p.X, p.Y - r - 4), new Vector2(p.X, p.Y + r + 4), amber, 1.5f);
    }

    private void DrawSliceTag(ImDrawListPtr drawList, Vector2 canvasPos, SpriteSlice slice)
    {
        var min = new Vector2(canvasPos.X + slice.X * _zoom, canvasPos.Y + slice.Y * _zoom);
        string tag = $"{slice.Name} · {slice.Width}×{slice.Height}";
        ImGui.PushFont(ImGuiRenderer.MonoFont);
        var ts = ImGui.CalcTextSize(tag);
        var pad = new Vector2(7, 3);
        var tagMin = new Vector2(min.X - 1, min.Y - ts.Y - pad.Y * 2 - 7);
        if (tagMin.Y < ImGui.GetWindowPos().Y + 4)
            tagMin.Y = min.Y + slice.Height * _zoom + 7;
        var tagMax = tagMin + ts + pad * 2;
        drawList.AddRectFilled(tagMin, tagMax, ImGui.GetColorU32(EditorTheme.Accent), 4f);
        drawList.AddText(tagMin + pad, ImGui.GetColorU32(new Vector4(0.10f, 0.08f, 0.03f, 1f)), tag);
        ImGui.PopFont();
    }

    private void DrawResizeHandles(ImDrawListPtr drawList, Vector2 canvasPos, SpriteSlice slice)
    {
        float handleSize = 6f;
        uint handleColor = ImGui.GetColorU32(EditorTheme.Accent);

        var min = new Vector2(canvasPos.X + slice.X * _zoom, canvasPos.Y + slice.Y * _zoom);
        var max = new Vector2(min.X + slice.Width * _zoom, min.Y + slice.Height * _zoom);
        var center = (min + max) / 2;

        Vector2[] handles = {
            new(min.X, min.Y),
            new(center.X, min.Y),
            new(max.X, min.Y),
            new(max.X, center.Y),
            new(max.X, max.Y),
            new(center.X, max.Y),
            new(min.X, max.Y),
            new(min.X, center.Y),
        };

        foreach (var h in handles)
        {
            drawList.AddRectFilled(
                new Vector2(h.X - handleSize / 2, h.Y - handleSize / 2),
                new Vector2(h.X + handleSize / 2, h.Y + handleSize / 2),
                handleColor
            );
        }
    }

    private void HandleSliceResize(Vector2 canvasPos, Texture2D texture, SpriteSlice slice)
    {
        var mousePos = ImGui.GetMousePos();
        float handleSize = 8f;

        var min = new Vector2(canvasPos.X + slice.X * _zoom, canvasPos.Y + slice.Y * _zoom);
        var max = new Vector2(min.X + slice.Width * _zoom, min.Y + slice.Height * _zoom);

        if (!_isResizing && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _resizeEdge = DetectResizeEdge(mousePos, min, max, handleSize);
            if (_resizeEdge != ResizeEdge.None)
            {
                _isResizing = true;
                _resizingSlice = slice;
                _resizeOrigX = slice.X;
                _resizeOrigY = slice.Y;
                _resizeOrigW = slice.Width;
                _resizeOrigH = slice.Height;
            }
        }

        if (_isResizing && _resizingSlice == slice)
        {
            int texX = (int)((mousePos.X - canvasPos.X) / _zoom);
            int texY = (int)((mousePos.Y - canvasPos.Y) / _zoom);
            texX = Math.Clamp(texX, 0, texture.Width);
            texY = Math.Clamp(texY, 0, texture.Height);

            switch (_resizeEdge)
            {
                case ResizeEdge.Left:
                    slice.Width = _resizeOrigX + _resizeOrigW - texX;
                    slice.X = texX;
                    break;
                case ResizeEdge.Right:
                    slice.Width = texX - slice.X;
                    break;
                case ResizeEdge.Top:
                    slice.Height = _resizeOrigY + _resizeOrigH - texY;
                    slice.Y = texY;
                    break;
                case ResizeEdge.Bottom:
                    slice.Height = texY - slice.Y;
                    break;
                case ResizeEdge.TopLeft:
                    slice.Width = _resizeOrigX + _resizeOrigW - texX;
                    slice.Height = _resizeOrigY + _resizeOrigH - texY;
                    slice.X = texX;
                    slice.Y = texY;
                    break;
                case ResizeEdge.TopRight:
                    slice.Width = texX - slice.X;
                    slice.Height = _resizeOrigY + _resizeOrigH - texY;
                    slice.Y = texY;
                    break;
                case ResizeEdge.BottomLeft:
                    slice.Width = _resizeOrigX + _resizeOrigW - texX;
                    slice.Height = texY - slice.Y;
                    slice.X = texX;
                    break;
                case ResizeEdge.BottomRight:
                    slice.Width = texX - slice.X;
                    slice.Height = texY - slice.Y;
                    break;
            }

            if (slice.Width < 1) { slice.Width = 1; slice.X = _resizeOrigX + _resizeOrigW - 1; }
            if (slice.Height < 1) { slice.Height = 1; slice.Y = _resizeOrigY + _resizeOrigH - 1; }

            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                if (slice.X != _resizeOrigX || slice.Y != _resizeOrigY ||
                    slice.Width != _resizeOrigW || slice.Height != _resizeOrigH)
                {
                    var cmd = new ResizeSliceCommand(slice,
                        _resizeOrigX, _resizeOrigY, _resizeOrigW, _resizeOrigH,
                        slice.X, slice.Y, slice.Width, slice.Height);
                    _commandHistory.AddExecuted(cmd);
                    _isDirty = true;
                }

                _isResizing = false;
                _resizeEdge = ResizeEdge.None;
            }
        }
    }

    private ResizeEdge DetectResizeEdge(Vector2 mousePos, Vector2 min, Vector2 max, float threshold)
    {
        var center = (min + max) / 2;

        bool nearLeft = Math.Abs(mousePos.X - min.X) < threshold;
        bool nearRight = Math.Abs(mousePos.X - max.X) < threshold;
        bool nearTop = Math.Abs(mousePos.Y - min.Y) < threshold;
        bool nearBottom = Math.Abs(mousePos.Y - max.Y) < threshold;
        bool inYRange = mousePos.Y >= min.Y - threshold && mousePos.Y <= max.Y + threshold;
        bool inXRange = mousePos.X >= min.X - threshold && mousePos.X <= max.X + threshold;

        if (nearLeft && nearTop) return ResizeEdge.TopLeft;
        if (nearRight && nearTop) return ResizeEdge.TopRight;
        if (nearLeft && nearBottom) return ResizeEdge.BottomLeft;
        if (nearRight && nearBottom) return ResizeEdge.BottomRight;
        if (nearLeft && inYRange) return ResizeEdge.Left;
        if (nearRight && inYRange) return ResizeEdge.Right;
        if (nearTop && inXRange) return ResizeEdge.Top;
        if (nearBottom && inXRange) return ResizeEdge.Bottom;

        return ResizeEdge.None;
    }

    private void HandlePanning(bool isCanvasHovered)
    {
        if (!_isPanning && (!isCanvasHovered || !ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))) return;

        if (!_isPanning &&
            (ImGui.IsMouseClicked(ImGuiMouseButton.Right) || ImGui.IsMouseClicked(ImGuiMouseButton.Middle)))
        {
            _isPanning = true;
            _panStartMouse = ImGui.GetMousePos();
            _panStartOffset = _scrollOffset;
        }

        if (_isPanning)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Right) || ImGui.IsMouseDown(ImGuiMouseButton.Middle))
            {
                var delta = ImGui.GetMousePos() - _panStartMouse;
                _scrollOffset = _panStartOffset + delta;
            }
            else
            {
                _isPanning = false;
            }
        }
    }

    private void HandleManualSliceCreation(Vector2 canvasPos, Texture2D texture, bool isHovered)
    {
        var mousePos = ImGui.GetMousePos();
        int texX = (int)Math.Clamp((mousePos.X - canvasPos.X) / _zoom, 0, texture.Width);
        int texY = (int)Math.Clamp((mousePos.Y - canvasPos.Y) / _zoom, 0, texture.Height);

        if (isHovered)
        {
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                if (_selectedSliceIndex >= 0 && _selectedSliceIndex < _atlas!.Slices.Count)
                {
                    var selected = _atlas.Slices[_selectedSliceIndex];
                    var min = new Vector2(canvasPos.X + selected.X * _zoom, canvasPos.Y + selected.Y * _zoom);
                    var max = new Vector2(min.X + selected.Width * _zoom, min.Y + selected.Height * _zoom);

                    float handleSize = 8f;
                    var edge = DetectResizeEdge(mousePos, min, max, handleSize);

                    if (edge == ResizeEdge.None &&
                        mousePos.X > min.X && mousePos.X < max.X &&
                        mousePos.Y > min.Y && mousePos.Y < max.Y)
                    {
                        _isMovingSlice = true;
                        _moveOrigX = selected.X;
                        _moveOrigY = selected.Y;
                        _moveStartMouse = mousePos;
                        return;
                    }
                }

                bool clickedOnSlice = false;
                for (int i = 0; i < _atlas!.Slices.Count; i++)
                {
                    var slice = _atlas.Slices[i];
                    if (texX >= slice.X && texX < slice.X + slice.Width &&
                        texY >= slice.Y && texY < slice.Y + slice.Height)
                    {
                        _selectedSliceIndex = i;
                        clickedOnSlice = true;
                        break;
                    }
                }

                if (!clickedOnSlice)
                {
                    _isDraggingSelection = true;
                    _dragStartTex = new Vector2(texX, texY);
                    _dragEndTex = _dragStartTex;
                    _selectedSliceIndex = -1;
                }
            }
        }

        if (_isMovingSlice && _selectedSliceIndex >= 0 && _selectedSliceIndex < _atlas!.Slices.Count)
        {
            var slice = _atlas.Slices[_selectedSliceIndex];
            var delta = mousePos - _moveStartMouse;
            int newX = _moveOrigX + (int)(delta.X / _zoom);
            int newY = _moveOrigY + (int)(delta.Y / _zoom);

            newX = Math.Clamp(newX, 0, texture.Width - slice.Width);
            newY = Math.Clamp(newY, 0, texture.Height - slice.Height);

            slice.X = newX;
            slice.Y = newY;

            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                if (slice.X != _moveOrigX || slice.Y != _moveOrigY)
                {
                    var cmd = new MoveSliceCommand(slice, _moveOrigX, _moveOrigY, slice.X, slice.Y);
                    _commandHistory.AddExecuted(cmd);
                    _isDirty = true;
                }
                _isMovingSlice = false;
            }
            return;
        }

        if (_isDraggingSelection)
        {
            _dragEndTex = new Vector2(texX, texY);

            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                _isDraggingSelection = false;

                int x = (int)Math.Min(_dragStartTex.X, _dragEndTex.X);
                int y = (int)Math.Min(_dragStartTex.Y, _dragEndTex.Y);
                int w = (int)Math.Abs(_dragEndTex.X - _dragStartTex.X);
                int h = (int)Math.Abs(_dragEndTex.Y - _dragStartTex.Y);

                if (w >= 2 && h >= 2)
                {
                    var newSlice = new SpriteSlice
                    {
                        Name = $"slice_{_atlas!.Slices.Count}",
                        X = x,
                        Y = y,
                        Width = w,
                        Height = h
                    };

                    var cmd = new CreateSliceCommand(_atlas, newSlice);
                    _commandHistory.Execute(cmd);
                    _selectedSliceIndex = cmd.InsertedIndex;
                    _isDirty = true;
                }
            }
        }
    }

    private void SelectSliceAtMouse(Vector2 canvasPos)
    {
        var mousePos = ImGui.GetMousePos();
        int texX = (int)((mousePos.X - canvasPos.X) / _zoom);
        int texY = (int)((mousePos.Y - canvasPos.Y) / _zoom);

        _selectedSliceIndex = -1;
        for (int i = 0; i < _atlas!.Slices.Count; i++)
        {
            var slice = _atlas.Slices[i];
            if (texX >= slice.X && texX < slice.X + slice.Width &&
                texY >= slice.Y && texY < slice.Y + slice.Height)
            {
                _selectedSliceIndex = i;
                break;
            }
        }
    }

    private void DrawSliceList()
    {
        ImGui.TextDisabled($"Slices ({_atlas!.Slices.Count})");
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear") && _atlas.Slices.Count > 0)
        {
            var cmd = new ClearSlicesCommand(_atlas);
            _commandHistory.Execute(cmd);
            _selectedSliceIndex = -1;
            _isDirty = true;
        }
        ImGui.Separator();

        bool hasSelection = _selectedSliceIndex >= 0 && _selectedSliceIndex < _atlas.Slices.Count;
        float avail = ImGui.GetContentRegionAvail().Y;
        float reserve = hasSelection ? MathF.Min(_sliceInspectorHeight, avail * 0.6f) : 0f;
        ImGui.BeginChild("SliceScroll", new System.Numerics.Vector2(0, -reserve));

        for (int i = 0; i < _atlas.Slices.Count; i++)
        {
            var slice = _atlas.Slices[i];
            bool isSelected = i == _selectedSliceIndex;

            if (ImGui.Selectable($"{slice.Name}###sl_{i}", isSelected))
            {
                _selectedSliceIndex = i;
            }

            {
                ImGui.PushFont(ImGuiRenderer.MonoFont);
                string dim = $"{slice.Width}×{slice.Height}";
                var dts = ImGui.CalcTextSize(dim);
                ImGui.GetWindowDrawList().AddText(
                    new Vector2(ImGui.GetItemRectMax().X - dts.X - 6,
                        ImGui.GetItemRectMin().Y + (ImGui.GetItemRectSize().Y - dts.Y) * 0.5f),
                    ImGui.GetColorU32(ImGuiCol.TextDisabled), dim);
                ImGui.PopFont();
            }

            if (ImGui.BeginDragDropSource())
            {
                _draggedSliceIndex = i;
                ImGui.SetDragDropPayload("SPRITE_SLICE", IntPtr.Zero, 0);
                ImGui.Text($"{Icons.Image}  {slice.Name}");
                ImGui.EndDragDropSource();
            }
        }

        ImGui.EndChild();

        if (hasSelection)
        {
            float inspectorTop = ImGui.GetCursorPosY();
            ImGui.Separator();
            ImGui.TextDisabled("Selected slice");

            var slice = _atlas.Slices[_selectedSliceIndex];

            string name = slice.Name;
            if (ImGui.InputText("Name", ref name, 64))
            {
                slice.Name = name;
                _isDirty = true;
            }

            int x = slice.X, y = slice.Y;
            int w = slice.Width, h = slice.Height;
            int texW = _atlas.Texture?.Width ?? 4096;
            int texH = _atlas.Texture?.Height ?? 4096;

            using var _sp = new TightInnerSpacing();
            float fw = StepperPairWidth("X", "Y");
            ImGui.SetNextItemWidth(fw);
            if (ImGui.InputInt("X", ref x)) { slice.X = Math.Clamp(x, 0, texW - 1); _isDirty = true; }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(fw);
            if (ImGui.InputInt("Y", ref y)) { slice.Y = Math.Clamp(y, 0, texH - 1); _isDirty = true; }

            fw = StepperPairWidth("W", "H");
            ImGui.SetNextItemWidth(fw);
            if (ImGui.InputInt("W", ref w)) { slice.Width = Math.Clamp(w, 1, texW); _isDirty = true; }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(fw);
            if (ImGui.InputInt("H", ref h)) { slice.Height = Math.Clamp(h, 1, texH); _isDirty = true; }

            ImGui.Spacing();
            ImGui.TextDisabled(Icons.Crosshair + "  Pivot");
            DrawPivotPresets(slice);

            int pvx = (int)MathF.Round(slice.PivotX * slice.Width);
            int pvy = (int)MathF.Round(slice.PivotY * slice.Height);
            float pw = StepperPairWidth("X", "Y");
            ImGui.SetNextItemWidth(pw);
            if (ImGui.InputInt("X##pvx", ref pvx) && slice.Width > 0)
            {
                slice.PivotX = Math.Clamp(pvx, 0, slice.Width) / (float)slice.Width;
                _isDirty = true;
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(pw);
            if (ImGui.InputInt("Y##pvy", ref pvy) && slice.Height > 0)
            {
                slice.PivotY = Math.Clamp(pvy, 0, slice.Height) / (float)slice.Height;
                _isDirty = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("The pivot in pixels - the crosshair on the canvas can be dragged directly too");

            ImGui.Spacing();
            if (ImGui.Button("Delete", new Vector2(-1, 0)))
            {
                var cmd = new DeleteSliceCommand(_atlas, _selectedSliceIndex);
                _commandHistory.Execute(cmd);
                _selectedSliceIndex = -1;
                _isDirty = true;
            }

            _sliceInspectorHeight = ImGui.GetCursorPosY() - inspectorTop;
        }
    }

    private void DrawPivotPresets(SpriteSlice slice)
    {
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                if (col > 0) ImGui.SameLine(0, 2);
                float nx = col * 0.5f, ny = row * 0.5f;
                bool on = Math.Abs(slice.PivotX - nx) < 0.001f && Math.Abs(slice.PivotY - ny) < 0.001f;

                ImGui.PushID(row * 3 + col);
                if (on)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, EditorTheme.AccentSoft);
                    ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Accent);
                }
                if (ImGui.Button(Icons.Circle, new Vector2(24, 20)) && !on)
                {
                    float ox = slice.PivotX, oy = slice.PivotY;
                    slice.PivotX = nx;
                    slice.PivotY = ny;
                    _commandHistory.AddExecuted(new SetPivotCommand(slice, ox, oy, nx, ny));
                    _isDirty = true;
                }
                if (on) ImGui.PopStyleColor(2);
                if (row == 2 && col == 1 && ImGui.IsItemHovered())
                    ImGui.SetTooltip("Bottom centre (the top-down under-foot standard)");
                ImGui.PopID();
            }
        }

        DrawApplyPivotToSheet(slice);
    }

    private void DrawApplyPivotToSheet(SpriteSlice slice)
    {
        if (_atlas == null || _atlas.Slices.Count < 2) return;

        int differing = PivotSpread.CountDiffering(_atlas, slice);

        ImGui.Spacing();
        ImGui.BeginDisabled(differing == 0);

        if (ImGui.Button($"Apply to whole sheet ({_atlas.Slices.Count})", new Vector2(-1, 0)))
        {
            var cmds = PivotSpread.Build(_atlas, slice);
            if (cmds.Length > 0)
            {
                _commandHistory.Execute(new CompositeCommand(
                    $"Apply Pivot to Sheet ({cmds.Length})", cmds));
                _isDirty = true;
            }
        }

        ImGui.EndDisabled();

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(differing == 0
                ? "Every slice in the sheet already shares this pivot"
                : $"Applies this pivot ({slice.PivotX:0.###}, {slice.PivotY:0.###}) to the other {differing}.\n"
                + "A character sheet needs one pivot - differing per frame makes the entity jump during animation.");
    }

    public CommandHistory History => _commandHistory;

    public SpriteAtlas? CurrentAtlas => _atlas;

    public int SelectedSliceIndex => _selectedSliceIndex;

    public bool IsOpen => _isOpen;

    public int DraggedSliceIndex => _draggedSliceIndex;

    public string CurrentTexturePath => _currentPath;
}

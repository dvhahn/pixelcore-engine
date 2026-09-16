using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Components;
using PixelCore.Editor.Commands;

namespace PixelCore.Editor.Panels;

public class SceneViewPanel : IDisposable
{
    private readonly EditorState _state;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ImGuiRenderer _imGuiRenderer;

    public RenderTarget2D? RenderTarget { get; private set; }

    private IntPtr _renderTargetId;
    private int _lastWidth;
    private int _lastHeight;
    private int _currentWidth;
    private int _currentHeight;

    private float _viewScale = 1f;

    private System.Numerics.Vector2 _imageScreenPos;
    private bool _isHovered;
    private bool _isFocused;

    private bool _isDragging;
    private Vector2 _dragStartWorldPos;
    private Vector2 _dragStartEntityPos;
    private Entity? _draggedEntity;

    private readonly List<(Entity Entity, Vector2 StartPos)> _dragGroup = new();

    private bool _isBoxSelecting;
    private bool _boxAdditive;
    private Vector2 _boxStartWorld;
    private Vector2 _boxEndWorld;

    private int _dragAxis;
    public const float GIZMO_ARROW_LENGTH = 36f;
    public const float GIZMO_HIT_RADIUS = 8f;

    private List<Entity> _pressHits = new();
    private Vector2 _pressLocalPos;
    private bool _pressOnSelected;

    private bool _isPanning;
    private Vector2 _panStartMousePos;
    private Vector2 _panStartCameraPos;

    private int _prevScrollWheelValue;
    private float _zoomLevel = float.NaN;

    public PixelCore.Runtime.Systems.TimeOfDay? Clock { get; set; }
    private bool _chipHotspot;

    private Camera? _camera;

    public (Func<Vector2, Vector2> W2L, Func<Vector2, Vector2> L2W, float Scale)? PlayCompose;

    private bool CanEdit => _state.IsEditMode || (_state.IsPlayMode && _state.PlayEditUnlocked);

    public event Action<Vector2>? OnTextureDropped;

    public event Action<Vector2>? OnSpriteSliceDropped;

    public event Action<Vector2>? OnSceneFileDropped;

    public event Action? OnPostFileDropped;

    public SceneViewPanel(EditorState state, GraphicsDevice graphicsDevice, ImGuiRenderer imGuiRenderer)
    {
        _state = state;
        _graphicsDevice = graphicsDevice;
        _imGuiRenderer = imGuiRenderer;
        _viewScale = imGuiRenderer.DpiScale;

        CreateRenderTarget(800, 600);
    }

    public void SetCamera(Camera camera)
    {
        _camera = camera;
    }

    public (int Width, int Height) GetViewSize() => (_currentWidth, _currentHeight);

    public float ViewScale => _viewScale;

    public int RtWidth => Math.Max(1, (int)MathF.Round(_currentWidth * _viewScale));

    public int RtHeight => Math.Max(1, (int)MathF.Round(_currentHeight * _viewScale));

    public bool IsHovered => _isHovered;

    public Action<ImDrawListPtr, System.Numerics.Vector2, Camera>? ToolOverlay { get; set; }

    public int DragAxis => _dragAxis;

    public Vector2 ScreenToLocal(Vector2 screenPos)
    {
        return new Vector2(
            screenPos.X - _imageScreenPos.X,
            screenPos.Y - _imageScreenPos.Y
        );
    }

    public Vector2 LocalToWorld(Vector2 localPos)
    {
        if (PlayCompose is { } pc) return pc.L2W(localPos * _viewScale);
        if (_camera == null) return localPos;
        return _camera.ScreenToWorld(localPos);
    }

    internal static float ZoomToLevel(float zoom)
        => zoom >= 1f ? zoom - 1f : (zoom - 1f) * 4f;

    internal static float LevelToZoom(float level)
        => level >= 0f ? level + 1f : level * 0.25f + 1f;

    private void DetachPlayCameraIfNeeded()
    {
        if (_state.IsPlayMode && _state.PlayEditUnlocked)
            _state.PlayCameraDetached = true;
    }

    internal static void StartPlayAt(EditorState state, Vector2 worldPos)
    {
        var player = state.CurrentScene?.FindPlayer();
        if (player == null)
        {
            EditorConsole.Add(LogSeverity.Warn,
                $"[Play from here] there is no '{Scene.PlayerName}' entity, so it was ignored - nothing to place");
            return;
        }

        state.SetMode(EditorMode.Play);

        var t = player.GetComponent<Transform>();
        if (t != null) t.Position = worldPos;

        if (player.GetComponent<Rigidbody2D>() is { } rb) rb.Velocity = Vector2.Zero;
    }

    public void HandleInput(bool tilemapEditing)
    {
        if (!CanEdit) return;
        if (_camera == null) return;

        var mouseState = Mouse.GetState();
        int scrollDelta = mouseState.ScrollWheelValue - _prevScrollWheelValue;
        _prevScrollWheelValue = mouseState.ScrollWheelValue;

        if (!_isHovered && !_isDragging && !_isPanning) return;

        var io = ImGui.GetIO();
        var mouseScreenPos = new Vector2(io.MousePos.X, io.MousePos.Y);
        var mouseLocalPos = ScreenToLocal(mouseScreenPos);
        var mouseWorldPos = LocalToWorld(mouseLocalPos);

        bool ctrl = EditorInput.Ctrl;

        if (ctrl && ImGui.IsMouseClicked(ImGuiMouseButton.Right)
            && _isHovered && !_isDragging && !tilemapEditing)
            StartPlayAt(_state, mouseWorldPos);

        bool panStart = (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && !ctrl)
                        || ImGui.IsMouseClicked(ImGuiMouseButton.Middle);
        if (panStart && _isHovered)
        {
            DetachPlayCameraIfNeeded();
            _isPanning = true;
            _panStartMousePos = mouseLocalPos;
            _panStartCameraPos = _camera.Position;
        }

        if (_isPanning)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Right) || ImGui.IsMouseDown(ImGuiMouseButton.Middle))
            {
                var delta = (mouseLocalPos - _panStartMousePos) / _camera.Zoom;
                _camera.Position = _panStartCameraPos - delta;
            }
            else
            {
                _isPanning = false;
            }
        }

        if (_isHovered && scrollDelta != 0)
        {
            DetachPlayCameraIfNeeded();
            if (float.IsNaN(_zoomLevel) || MathF.Abs(LevelToZoom(MathF.Round(_zoomLevel)) - _camera.Zoom) > 0.001f)
                _zoomLevel = ZoomToLevel(_camera.Zoom);

            _zoomLevel = MathHelper.Clamp(_zoomLevel + ImGuiRenderer.TameScrollDelta(scrollDelta) / 120f,
                ZoomToLevel(0.25f), ZoomToLevel(16f));
            _camera.Zoom = LevelToZoom(MathF.Round(_zoomLevel));
        }

        if (_isPanning) return;

        if (tilemapEditing)
        {
            _isBoxSelecting = false;
            return;
        }

        if (_state.EditingCollider is { } ecv &&
            (ecv.Entity != _state.SelectedEntity || !ecv.Entity.Components.Contains(ecv)))
            _state.EditingCollider = null;

        if (_state.EditingShadow is { } esv &&
            (esv.Entity != _state.SelectedEntity || !esv.Entity.Components.Contains(esv) || !esv.CastShadow))
            _state.EditingShadow = null;

        if (_state.EditingCameraFraming &&
            (_state.SelectedEntity != null || _state.CurrentScene == null))
            _state.EditingCameraFraming = false;

        if (_state.EditingCameraFraming && _state.CurrentScene is { } fscene &&
            HandleFramingEdit(fscene, mouseWorldPos))
            return;

        if (_state.EditingCollider is { Enabled: true } editCol &&
            HandleColliderEdit(editCol, mouseWorldPos))
            return;

        if (_state.EditingShadow is { Enabled: true } editShadow &&
            HandleShadowEdit(editShadow, mouseWorldPos))
            return;

        if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && _isHovered)
        {
            var rawHits = HitTestAll(mouseWorldPos);
            if (rawHits.Count > 0 && rawHits[0].HideFromSerialization)
            {
                _state.Select(rawHits[0]);
                _isDragging = false;
                _isBoxSelecting = false;
                return;
            }
        }

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && _isHovered)
        {
            int hitAxis = HitTestGizmoArrow(mouseWorldPos);

            var chipHit = HitTestGizmoChip(new System.Numerics.Vector2(io.MousePos.X, io.MousePos.Y));

            if (chipHit != null && _state.ShowGizmos)
            {
                if (io.KeyShift || io.KeySuper || io.KeyCtrl) _state.ToggleSelection(chipHit);
                else _state.Select(chipHit);
                _pressOnSelected = false;
                BeginEntityDrag(chipHit, 0, mouseWorldPos);
            }
            else if (hitAxis > 0 && _state.SelectedEntity != null)
            {
                BeginEntityDrag(_state.SelectedEntity, hitAxis, mouseWorldPos);
                _pressOnSelected = false;
            }
            else
            {
                bool additive = io.KeyShift || io.KeySuper || io.KeyCtrl;

                var hits = PromoteHits(HitTestAll(mouseWorldPos));

                if (hits.Count > 0)
                {
                    if (additive)
                    {
                        _state.ToggleSelection(hits[0]);
                    }
                    else
                    {
                        Entity target;
                        var selected = _state.SelectedEntity;
                        bool selectedHit = selected != null && hits.Contains(selected);

                        if (_state.Selection.Count > 1 && hits.Exists(h => _state.IsSelected(h)))
                        {
                            target = hits.Find(h => _state.IsSelected(h))!;
                        }
                        else
                        {
                            target = selectedHit ? selected! : hits[0];
                            if (!selectedHit) _state.Select(target);
                        }

                        _pressHits = hits;
                        _pressLocalPos = mouseLocalPos;
                        _pressOnSelected = selectedHit && _state.Selection.Count == 1;

                        BeginEntityDrag(target, 0, mouseWorldPos);
                    }
                }
                else
                {
                    _isBoxSelecting = true;
                    _boxAdditive = additive;
                    _boxStartWorld = mouseWorldPos;
                    _boxEndWorld = mouseWorldPos;
                }
            }
        }

        if (_isBoxSelecting)
        {
            _boxEndWorld = mouseWorldPos;
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                _isBoxSelecting = false;
                var bmin = Vector2.Min(_boxStartWorld, _boxEndWorld);
                var bmax = Vector2.Max(_boxStartWorld, _boxEndWorld);

                if ((bmax - bmin).Length() * _camera.Zoom < 4f)
                {
                    if (!_boxAdditive) _state.ClearSelection();
                }
                else
                {
                    var rect = new Rectangle(
                        (int)MathF.Floor(bmin.X), (int)MathF.Floor(bmin.Y),
                        (int)MathF.Ceiling(bmax.X - bmin.X), (int)MathF.Ceiling(bmax.Y - bmin.Y));
                    _state.SelectMany(PromoteHits(EntitiesInRect(rect)), _boxAdditive);
                }
            }
        }

        if (_isDragging && _draggedEntity != null)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                var delta = mouseWorldPos - _dragStartWorldPos;

                if (_dragAxis == 1) delta.Y = 0;
                else if (_dragAxis == 2) delta.X = 0;

                if (_state.SnapToGrid)
                {
                    var snapped = _dragStartEntityPos + delta;
                    snapped.X = MathF.Round(snapped.X / _state.GridSize) * _state.GridSize;
                    snapped.Y = MathF.Round(snapped.Y / _state.GridSize) * _state.GridSize;
                    delta = snapped - _dragStartEntityPos;
                }
                else
                {
                    delta.X = MathF.Round(delta.X);
                    delta.Y = MathF.Round(delta.Y);
                }

                foreach (var (entity, startPos) in _dragGroup)
                {
                    var transform = entity.GetComponent<Transform>();
                    if (transform == null) continue;
                    transform.Position = startPos + delta;
                }
            }
            else
            {
                var moves = new List<ICommand>();
                foreach (var (entity, startPos) in _dragGroup)
                {
                    var transform = entity.GetComponent<Transform>();
                    if (transform != null && transform.Position != startPos)
                    {
                        moves.Add(new MoveEntityCommand(entity, startPos, transform.Position));
                        _state.KeepThroughStop(entity);
                    }
                }

                if (moves.Count == 1)
                {
                    _state.CommandHistory.AddExecuted(moves[0]);
                    _state.MarkDirty();
                }
                else if (moves.Count > 1)
                {
                    _state.CommandHistory.AddExecuted(new CompositeCommand($"Move {moves.Count} entities", moves.ToArray()));
                    _state.MarkDirty();
                }
                else if (_pressOnSelected && _pressHits.Count > 1
                         && Vector2.DistanceSquared(mouseLocalPos, _pressLocalPos) < 9f)
                {
                    int cur = _pressHits.IndexOf(_draggedEntity);
                    _state.Select(_pressHits[(cur + 1) % _pressHits.Count]);
                }

                _isDragging = false;
                _draggedEntity = null;
                _dragGroup.Clear();
                _dragAxis = 0;
                _pressOnSelected = false;
            }
        }
    }

    private void BeginEntityDrag(Entity target, int axis, Vector2 mouseWorldPos)
    {
        _isDragging = true;
        _dragAxis = axis;
        _draggedEntity = target;
        _dragStartWorldPos = mouseWorldPos;
        var t = target.GetComponent<Transform>();
        _dragStartEntityPos = t?.Position ?? Vector2.Zero;

        _dragGroup.Clear();
        var seen = new HashSet<Entity>();
        if (_state.IsSelected(target) && _state.Selection.Count > 1)
        {
            foreach (var e in _state.Selection)
                AddToDragGroup(e, seen);
        }
        else
        {
            AddToDragGroup(target, seen);
        }
    }

    private void AddToDragGroup(Entity e, HashSet<Entity> seen)
    {
        if (!seen.Add(e)) return;
        var t = e.GetComponent<Transform>();
        if (t != null) _dragGroup.Add((e, t.Position));
        foreach (var child in e.Children)
            AddToDragGroup(child, seen);
    }

    private List<Entity> PromoteHits(List<Entity> raw)
    {
        Entity? insideHost = _state.SelectedEntity is { HideFromSerialization: true } sel
            ? PromoteToInstanceHost(sel) : null;

        var result = new List<Entity>();
        foreach (var e in raw)
        {
            var p = e;
            if (e.HideFromSerialization)
            {
                var host = PromoteToInstanceHost(e);
                if (host != insideHost) p = host;
            }
            if (!result.Contains(p)) result.Add(p);
        }
        return result;
    }

    private static Entity PromoteToInstanceHost(Entity e)
    {
        while (e.HideFromSerialization && e.Parent != null) e = e.Parent;
        return e;
    }

    private List<Entity> EntitiesInRect(Rectangle rect)
    {
        var result = new List<Entity>();
        if (_state.CurrentScene == null) return result;

        foreach (var entity in _state.CurrentScene.Entities)
        {
            if (entity.GetComponent<Transform>() == null) continue;
            if (GetEntityBounds(entity).Intersects(rect))
                result.Add(entity);
        }
        return result;
    }

    private int HitTestGizmoArrow(Vector2 worldPos)
    {
        var anchor = GizmoAnchor();
        if (anchor == null) return 0;
        var center = anchor.Value;

        float zoom = _camera?.Zoom ?? 1f;
        float arrowLength = GIZMO_ARROW_LENGTH / zoom;
        float hitRadius = GIZMO_HIT_RADIUS / zoom;

        var xArrowEnd = center + new Vector2(arrowLength, 0);
        if (PointToLineDistance(worldPos, center, xArrowEnd) < hitRadius)
            return 1;

        var yArrowEnd = center + new Vector2(0, -arrowLength);
        if (PointToLineDistance(worldPos, center, yArrowEnd) < hitRadius)
            return 2;

        return 0;
    }

    private float PointToLineDistance(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
    {
        var line = lineEnd - lineStart;
        float lineLength = line.Length();
        if (lineLength < 0.001f) return Vector2.Distance(point, lineStart);

        var lineDir = line / lineLength;
        var toPoint = point - lineStart;
        float projection = Vector2.Dot(toPoint, lineDir);

        projection = MathHelper.Clamp(projection, 0, lineLength);

        var closestPoint = lineStart + lineDir * projection;
        return Vector2.Distance(point, closestPoint);
    }

    private List<Entity> HitTestAll(Vector2 worldPos)
    {
        if (_state.CurrentScene == null) return new List<Entity>();

        var drawn = _state.CurrentScene.BuildDrawList();
        _depth.Clear();
        for (int i = 0; i < drawn.Count; i++) _depth[drawn[i]] = i;

        var hits = new List<(Entity e, int depth, float area)>();

        foreach (var entity in _state.CurrentScene.Entities)
        {
            if (entity.GetComponent<Transform>() == null) continue;

            var bounds = GetEntityBounds(entity);
            if (!bounds.Contains((int)MathF.Floor(worldPos.X), (int)MathF.Floor(worldPos.Y)))
                continue;

            int depth = int.MinValue;
            foreach (var component in entity.Components)
            {
                if (component is IRenderable r && _depth.TryGetValue(r, out int d) && d > depth)
                    depth = d;
            }

            hits.Add((entity, depth, (float)bounds.Width * bounds.Height));
        }

        return hits
            .OrderByDescending(h => h.depth)
            .ThenBy(h => h.area)
            .Select(h => h.e)
            .ToList();
    }

    private readonly Dictionary<IRenderable, int> _depth = new();

    public Rectangle GetEntityBounds(Entity entity)
    {
        var transform = entity.GetComponent<Transform>();
        if (transform == null) return Rectangle.Empty;

        var pos = transform.Position;
        var size = GetEntityVisualSize(entity);

        var sr = entity.GetComponent<SpriteRenderer>();
        bool visualFromSprite = sr != null && sr.GetDrawSize() != Vector2.Zero;
        if (!visualFromSprite && entity.GetComponent<Collider2D>() is { } col)
        {
            var b = col.GetBounds();
            return new Rectangle((int)b.Min.X, (int)b.Min.Y, (int)b.Width, (int)b.Height);
        }

        if (sr != null && sr.GetDrawSize() != Vector2.Zero)
            return sr.GetDestRect(transform);

        return new Rectangle(
            (int)(pos.X - size.X / 2),
            (int)(pos.Y - size.Y / 2),
            (int)size.X,
            (int)size.Y
        );
    }

    public static Vector2 GetEntityVisualSize(Entity entity)
    {
        var sr = entity.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            var s = sr.GetDrawSize();
            if (s != Vector2.Zero) return s;
        }

        var col = entity.GetComponent<Collider2D>();
        if (col != null)
        {
            var b = col.GetBounds();
            return new Vector2(b.Width, b.Height);
        }

        return new Vector2(16, 16);
    }

    public (Rectangle bounds, Vector2 anchor, bool isDragging)? GetSelectedGizmo()
    {
        if (_state.SelectedEntity == null) return null;
        if (!CanEdit) return null;

        var anchor = GizmoAnchor();
        if (anchor == null) return null;
        return (SelectionBounds(), anchor.Value, _isDragging);
    }

    private Vector2? GizmoAnchor()
    {
        var primary = _state.SelectedEntity;
        if (primary == null) return null;

        if (_state.Selection.Count > 1)
        {
            var b = SelectionBounds();
            if (b.Width > 0 || b.Height > 0) return new Vector2(b.Center.X, b.Center.Y);
        }

        var t = primary.GetComponent<Transform>();
        if (t != null) return t.Position;
        var pb = GetEntityBounds(primary);
        return new Vector2(pb.Center.X, pb.Center.Y);
    }

    private Rectangle SelectionBounds()
    {
        Rectangle? union = null;
        foreach (var e in _state.Selection)
        {
            if (e.GetComponent<Transform>() == null) continue;
            var b = GetEntityBounds(e);
            union = union.HasValue ? Rectangle.Union(union.Value, b) : b;
        }
        return union ?? Rectangle.Empty;
    }

    public enum FrameTarget { None, All, Selection }

    private FrameTarget _pendingFrame;

    public void RequestFrame(FrameTarget what) => _pendingFrame = what;

    public bool FrameEntities(IEnumerable<Entity> entities)
    {
        if (_camera == null || _currentWidth <= 0 || _currentHeight <= 0) return false;

        bool any = false;
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        void Accumulate(Entity e)
        {
            var b = GetEntityBounds(e);
            if (b.Width > 0 || b.Height > 0)
            {
                minX = MathF.Min(minX, b.Left);   minY = MathF.Min(minY, b.Top);
                maxX = MathF.Max(maxX, b.Right);  maxY = MathF.Max(maxY, b.Bottom);
                any = true;
            }
            foreach (var child in e.Children) Accumulate(child);
        }

        foreach (var e in entities) Accumulate(e);
        if (!any) return false;

        _camera.Position = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);

        float w = MathF.Max(1f, maxX - minX), h = MathF.Max(1f, maxY - minY);
        float fit = MathF.Min(_currentWidth / w, _currentHeight / h) * 0.9f;
        float level = MathF.Floor(ZoomToLevel(fit));
        level = Math.Clamp(level, ZoomToLevel(0.25f), ZoomToLevel(16f));
        _camera.Zoom = LevelToZoom(level);
        return true;
    }

    private void ConsumePendingFrame()
    {
        if (_pendingFrame == FrameTarget.None || _state.CurrentScene == null) return;
        var target = _pendingFrame;
        _pendingFrame = FrameTarget.None;

        if (target == FrameTarget.Selection && _state.Selection.Count > 0)
        {
            if (FrameEntities(_state.Selection)) return;
        }
        FrameEntities(_state.CurrentScene.Entities);
    }

    public RenderTarget2D? AcquireRenderTarget()
    {
        int rw = RtWidth, rh = RtHeight;
        if (rw > 0 && rh > 0 && (rw != _lastWidth || rh != _lastHeight))
        {
            CreateRenderTarget(rw, rh);
        }
        return RenderTarget;
    }

    private static void DrawViewportCornerCaps(System.Numerics.Vector2 min, System.Numerics.Vector2 max)
    {
        float r = ImGui.GetStyle().ChildRounding;
        if (r < 0.5f) return;
        if (max.X - min.X < r * 2f || max.Y - min.Y < r * 2f) return;
        var dl = ImGui.GetWindowDrawList();
        uint col = ImGui.GetColorU32(EditorTheme.Recessed);
        const float pi = MathF.PI;

        dl.PathLineTo(min);
        dl.PathArcTo(new System.Numerics.Vector2(min.X + r, min.Y + r), r, pi, pi * 1.5f);
        dl.PathFillConvex(col);

        dl.PathLineTo(new System.Numerics.Vector2(max.X, min.Y));
        dl.PathArcTo(new System.Numerics.Vector2(max.X - r, min.Y + r), r, pi * 1.5f, pi * 2f);
        dl.PathFillConvex(col);

        dl.PathLineTo(max);
        dl.PathArcTo(new System.Numerics.Vector2(max.X - r, max.Y - r), r, 0f, pi * 0.5f);
        dl.PathFillConvex(col);

        dl.PathLineTo(new System.Numerics.Vector2(min.X, max.Y));
        dl.PathArcTo(new System.Numerics.Vector2(min.X + r, max.Y - r), r, pi * 0.5f, pi);
        dl.PathFillConvex(col);
    }

    public void DrawContent()
    {
        {
            _viewScale = _imGuiRenderer.DpiScale;
            var size = ImGui.GetContentRegionAvail();
            _currentWidth = Math.Max((int)size.X, 1);
            _currentHeight = Math.Max((int)size.Y, 1);

            ConsumePendingFrame();

            if (RenderTarget != null)
            {
                _imageScreenPos = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddRectFilled(_imageScreenPos,
                    new System.Numerics.Vector2(_imageScreenPos.X + _currentWidth,
                                                _imageScreenPos.Y + _currentHeight),
                    ImGui.GetColorU32(EditorTheme.ViewportBackdrop));
                ImGui.Image(_renderTargetId, new System.Numerics.Vector2(_currentWidth, _currentHeight));
                DrawViewportCornerCaps(
                    new System.Numerics.Vector2(_imageScreenPos.X, _imageScreenPos.Y),
                    new System.Numerics.Vector2(_imageScreenPos.X + _currentWidth,
                                                _imageScreenPos.Y + _currentHeight));
            }

            _isHovered = ImGui.IsItemHovered();
            if (_chipHotspot) _isHovered = false;
            _isFocused = ImGui.IsWindowFocused();

            if (ImGui.BeginDragDropTarget())
            {
                var texturePayload = ImGui.AcceptDragDropPayload("TEXTURE_FILE");
                unsafe
                {
                    if (texturePayload.NativePtr != null)
                    {
                        var io = ImGui.GetIO();
                        var mouseLocalPos = ScreenToLocal(new Vector2(io.MousePos.X, io.MousePos.Y));
                        var worldPos = LocalToWorld(mouseLocalPos);
                        OnTextureDropped?.Invoke(worldPos);
                    }
                }

                var slicePayload = ImGui.AcceptDragDropPayload("SPRITE_SLICE");
                unsafe
                {
                    if (slicePayload.NativePtr != null)
                    {
                        var io = ImGui.GetIO();
                        var mouseLocalPos = ScreenToLocal(new Vector2(io.MousePos.X, io.MousePos.Y));
                        var worldPos = LocalToWorld(mouseLocalPos);
                        OnSpriteSliceDropped?.Invoke(worldPos);
                    }
                }

                var sceneFilePayload = ImGui.AcceptDragDropPayload("SCENE_FILE");
                unsafe
                {
                    if (sceneFilePayload.NativePtr != null)
                    {
                        var io = ImGui.GetIO();
                        var mouseLocalPos = ScreenToLocal(new Vector2(io.MousePos.X, io.MousePos.Y));
                        var worldPos = LocalToWorld(mouseLocalPos);
                        OnSceneFileDropped?.Invoke(worldPos);
                    }
                }

                var postPayload = ImGui.AcceptDragDropPayload("POST_FILE");
                unsafe
                {
                    if (postPayload.NativePtr != null) OnPostFileDropped?.Invoke();
                }

                ImGui.EndDragDropTarget();
            }

            DrawOverlay();
        }
    }

    private void DrawOverlay()
    {
        if (_camera == null) return;

        var dl = ImGui.GetWindowDrawList();
        var vpMin = _imageScreenPos;
        var vpMax = new System.Numerics.Vector2(vpMin.X + _currentWidth, vpMin.Y + _currentHeight);
        dl.PushClipRect(vpMin, vpMax, true);

        if (_state.IsPlayMode)
        {
            var a = EditorTheme.Accent;
            uint amber = ImGui.GetColorU32(a);
            uint glowTop = ImGui.GetColorU32(new System.Numerics.Vector4(a.X, a.Y, a.Z, 0.10f));
            uint glowNone = ImGui.GetColorU32(new System.Numerics.Vector4(a.X, a.Y, a.Z, 0f));
            dl.AddRectFilled(vpMin, new System.Numerics.Vector2(vpMax.X, vpMin.Y + 2.5f), amber);
            dl.AddRectFilledMultiColor(
                new System.Numerics.Vector2(vpMin.X, vpMin.Y + 2.5f),
                new System.Numerics.Vector2(vpMax.X, vpMin.Y + 26f),
                glowTop, glowTop, glowNone, glowNone);
            dl.AddRect(vpMin, vpMax,
                ImGui.GetColorU32(new System.Numerics.Vector4(a.X, a.Y, a.Z, 0.45f)),
                0f, ImDrawFlags.None, 1.5f);
        }

        if (_state.CurrentScene != null
            && (_state.ShowColliders || (CanEdit && _state.SelectedEntity != null)))
            DrawCollidersOverlay(dl, vpMin, showAll: _state.ShowColliders);

        if (_state.IsPlayMode)
            DrawPlayOverlayButtons(dl, vpMin, vpMax);

        if (_state.IsPlayMode && _state.PlayCameraDetached)
            DrawGameViewRect(dl, vpMin);

        if (!CanEdit)
        {
            dl.PopClipRect();
            return;
        }

        if (_state.ShowGrid && _state.IsEditMode)
            DrawGrid(dl, vpMin);

        if (_state.ShowGizmos && _state.CurrentScene != null)
            DrawEntityGizmos(dl, vpMin);

        var chipPos = new System.Numerics.Vector2(vpMin.X + 10, vpMin.Y + 10);
        string sizeText = $"{_currentWidth} × {_currentHeight}";
        if (_state.IsEditMode || _state.PlayCameraDetached)
            DrawHudChipSplit(dl, chipPos, $"{_camera.Zoom * 100:0}%", sizeText);
        else
            DrawHudChip(dl, chipPos, sizeText);

        _chipHotspot = false;
        DrawViewToolbar(dl, vpMin, vpMax.X - 10);

        var e = _state.SelectedEntity;
        if (e != null)
        {
            var b = GetEntityBounds(e);
            var tl = _camera.WorldToScreen(new Vector2(b.X, b.Y));
            var br = _camera.WorldToScreen(new Vector2(b.Right, b.Bottom));
            var min = new System.Numerics.Vector2(vpMin.X + MathF.Round(tl.X), vpMin.Y + MathF.Round(tl.Y));
            var max = new System.Numerics.Vector2(vpMin.X + MathF.Round(br.X), vpMin.Y + MathF.Round(br.Y));

            uint amber = ImGui.GetColorU32(EditorTheme.Accent);
            if (_isDragging)
                dl.AddRect(min, max, amber, 0f, ImDrawFlags.None, 1.5f);
            else
                AddDashedRect(dl, min, max, amber, 5f, 4f, 1.5f);

            const float hs = 3.5f;
            var corners = new[]
            {
                min,
                new System.Numerics.Vector2(max.X, min.Y),
                new System.Numerics.Vector2(min.X, max.Y),
                max
            };
            foreach (var c in corners)
                dl.AddRectFilled(
                    new System.Numerics.Vector2(c.X - hs, c.Y - hs),
                    new System.Numerics.Vector2(c.X + hs, c.Y + hs), amber, 1.5f);

            string kind = EntityKinds.Label(e);
            string tag = kind.Length > 0 ? $"{e.Name} · {kind}" : e.Name;
            ImGui.PushFont(ImGuiRenderer.MonoFont);
            var ts = ImGui.CalcTextSize(tag);
            var pad = new System.Numerics.Vector2(7, 3);
            var tagMin = new System.Numerics.Vector2(min.X - 1, min.Y - ts.Y - pad.Y * 2 - 7);
            if (tagMin.Y < vpMin.Y + 4) tagMin.Y = max.Y + 7;
            var tagMax = new System.Numerics.Vector2(tagMin.X + ts.X + pad.X * 2, tagMin.Y + ts.Y + pad.Y * 2);
            dl.AddRectFilled(tagMin, tagMax, amber, 4f);
            dl.AddText(new System.Numerics.Vector2(tagMin.X + pad.X, tagMin.Y + pad.Y),
                ImGui.GetColorU32(new System.Numerics.Vector4(0.10f, 0.08f, 0.03f, 1f)), tag);
            ImGui.PopFont();

            foreach (var sel in _state.Selection)
            {
                if (sel == e) continue;
                var sb = GetEntityBounds(sel);
                var stl = _camera.WorldToScreen(new Vector2(sb.X, sb.Y));
                var sbr = _camera.WorldToScreen(new Vector2(sb.Right, sb.Bottom));
                dl.AddRect(
                    new System.Numerics.Vector2(vpMin.X + MathF.Round(stl.X), vpMin.Y + MathF.Round(stl.Y)),
                    new System.Numerics.Vector2(vpMin.X + MathF.Round(sbr.X), vpMin.Y + MathF.Round(sbr.Y)),
                    amber, 0f, ImDrawFlags.None, 1f);
            }

            var selLight = e.GetComponent<Light2D>();
            var selLightTf = e.GetComponent<PixelCore.Runtime.Components.Transform>();
            if (selLight is { Enabled: true } && selLightTf != null)
                DrawLightGizmo(dl, vpMin, selLight, selLightTf.Position);

            var selInter = e.GetComponent<Interactable>();
            if (selInter is { Enabled: true } && selLightTf != null)
                DrawInteractRangeGizmo(dl, vpMin, selLightTf.Position, selInter.Range);

            var selEmitter = e.GetComponent<SoundEmitter>();
            if (selEmitter is { Enabled: true } && selLightTf != null)
                DrawSoundRangeGizmo(dl, vpMin, selLightTf.Position, selEmitter);

            if (e.GetComponent<Talker>() is { Enabled: true } && selLightTf != null)
                DrawBubbleAnchorGizmo(dl, vpMin, e);

            if (_state.EditingCollider is { Enabled: true } editCol && editCol.Entity == e)
                DrawColliderEditOverlay(dl, vpMin, editCol);

            if (_state.EditingShadow is { Enabled: true } editShadow && editShadow.Entity == e)
                DrawShadowEditOverlay(dl, vpMin, editShadow);
        }

        if (_state.EditingCameraFraming && _state.CurrentScene is { } framingScene)
            DrawFramingEditOverlay(dl, vpMin, framingScene);

        if (_isBoxSelecting)
        {
            var bmin = Vector2.Min(_boxStartWorld, _boxEndWorld);
            var bmax = Vector2.Max(_boxStartWorld, _boxEndWorld);
            var smin = _camera.WorldToScreen(bmin);
            var smax = _camera.WorldToScreen(bmax);
            var rmin = new System.Numerics.Vector2(vpMin.X + smin.X, vpMin.Y + smin.Y);
            var rmax = new System.Numerics.Vector2(vpMin.X + smax.X, vpMin.Y + smax.Y);
            dl.AddRectFilled(rmin, rmax, ImGui.GetColorU32(new System.Numerics.Vector4(
                EditorTheme.Accent.X, EditorTheme.Accent.Y, EditorTheme.Accent.Z, 0.12f)));
            dl.AddRect(rmin, rmax, ImGui.GetColorU32(EditorTheme.Accent), 0f, ImDrawFlags.None, 1f);
        }

        ToolOverlay?.Invoke(dl, vpMin, _camera!);

        dl.PopClipRect();
    }

    private void DrawGrid(ImDrawListPtr dl, System.Numerics.Vector2 vpMin)
    {
        if (_camera == null) return;

        float zoom = _camera.Zoom;
        float step = Math.Max(1, _state.GridSize);
        const float minPx = 14f;
        while (step * zoom < minPx) step *= 2f;

        float fade = Math.Clamp((step * zoom - minPx) / minPx, 0f, 1f);
        uint minor = ImGui.GetColorU32(ImGuiCol.Text, 0.030f + 0.020f * fade);
        uint major = ImGui.GetColorU32(ImGuiCol.Text, 0.070f + 0.025f * fade);
        uint axisX = ImGui.GetColorU32(new System.Numerics.Vector4(
            EditorTheme.AxisX.X, EditorTheme.AxisX.Y, EditorTheme.AxisX.Z, 0.50f));
        uint axisY = ImGui.GetColorU32(new System.Numerics.Vector4(
            EditorTheme.AxisY.X, EditorTheme.AxisY.Y, EditorTheme.AxisY.Z, 0.50f));

        var worldTL = _camera.ScreenToWorld(Vector2.Zero);
        var worldBR = _camera.ScreenToWorld(new Vector2(_currentWidth, _currentHeight));

        for (float wx = MathF.Floor(worldTL.X / step) * step; wx <= worldBR.X; wx += step)
        {
            long k = (long)MathF.Round(wx / step);
            if (k == 0) continue;
            float sx = vpMin.X + MathF.Round(_camera.WorldToScreen(new Vector2(wx, 0)).X) + 0.5f;
            dl.AddLine(new System.Numerics.Vector2(sx, vpMin.Y),
                       new System.Numerics.Vector2(sx, vpMin.Y + _currentHeight),
                       k % 8 == 0 ? major : minor, 1f);
        }

        for (float wy = MathF.Floor(worldTL.Y / step) * step; wy <= worldBR.Y; wy += step)
        {
            long k = (long)MathF.Round(wy / step);
            if (k == 0) continue;
            float sy = vpMin.Y + MathF.Round(_camera.WorldToScreen(new Vector2(0, wy)).Y) + 0.5f;
            dl.AddLine(new System.Numerics.Vector2(vpMin.X, sy),
                       new System.Numerics.Vector2(vpMin.X + _currentWidth, sy),
                       k % 8 == 0 ? major : minor, 1f);
        }

        if (worldTL.X <= 0 && worldBR.X >= 0)
        {
            float sx = vpMin.X + MathF.Round(_camera.WorldToScreen(Vector2.Zero).X) + 0.5f;
            dl.AddLine(new System.Numerics.Vector2(sx, vpMin.Y),
                       new System.Numerics.Vector2(sx, vpMin.Y + _currentHeight), axisY, 1f);
        }
        if (worldTL.Y <= 0 && worldBR.Y >= 0)
        {
            float sy = vpMin.Y + MathF.Round(_camera.WorldToScreen(Vector2.Zero).Y) + 0.5f;
            dl.AddLine(new System.Numerics.Vector2(vpMin.X, sy),
                       new System.Numerics.Vector2(vpMin.X + _currentWidth, sy), axisX, 1f);
        }
    }

    private void DrawInteractRangeGizmo(ImDrawListPtr dl, System.Numerics.Vector2 vpMin,
        Vector2 worldPos, float range)
    {
        if (_camera == null) return;
        float radius = (Interactor.DefaultReach + range) * _camera.Zoom;
        var s = _camera.WorldToScreen(worldPos);
        var c = new System.Numerics.Vector2(vpMin.X + s.X, vpMin.Y + s.Y);

        var col = new System.Numerics.Vector4(0.74f, 0.56f, 0.96f, 1f);
        dl.AddCircleFilled(c, radius, ImGui.GetColorU32(
            new System.Numerics.Vector4(col.X, col.Y, col.Z, 0.07f)));
        dl.AddCircle(c, radius, ImGui.GetColorU32(
            new System.Numerics.Vector4(col.X, col.Y, col.Z, 0.85f)), 0, 1f);

        ImGui.PushFont(ImGuiRenderer.MonoFont);
        string txt = $"{Interactor.DefaultReach + range:0.#}px";
        var ts = ImGui.CalcTextSize(txt);
        var tp = new System.Numerics.Vector2(c.X - ts.X * 0.5f, c.Y - radius - ts.Y - 3f);
        dl.AddRectFilled(tp - new System.Numerics.Vector2(4, 2), tp + ts + new System.Numerics.Vector2(4, 2),
            ImGui.GetColorU32(new System.Numerics.Vector4(0.06f, 0.07f, 0.09f, 0.72f)), 3f);
        dl.AddText(tp, ImGui.GetColorU32(col), txt);
        ImGui.PopFont();
    }

    private void DrawSoundRangeGizmo(ImDrawListPtr dl, System.Numerics.Vector2 vpMin,
        Vector2 worldPos, SoundEmitter emitter)
    {
        if (_camera == null || emitter.Radius <= 0f) return;
        float radius = emitter.Radius * _camera.Zoom;
        var s = _camera.WorldToScreen(worldPos);
        var c = new System.Numerics.Vector2(vpMin.X + s.X, vpMin.Y + s.Y);

        var col = new System.Numerics.Vector4(0.42f, 0.80f, 0.92f, 1f);
        dl.AddCircleFilled(c, radius, ImGui.GetColorU32(
            new System.Numerics.Vector4(col.X, col.Y, col.Z, 0.06f)));
        dl.AddCircle(c, radius, ImGui.GetColorU32(
            new System.Numerics.Vector4(col.X, col.Y, col.Z, 0.85f)), 0, 1f);

        dl.AddCircle(c, radius * 0.5f, ImGui.GetColorU32(
            new System.Numerics.Vector4(col.X, col.Y, col.Z, 0.30f)), 0, 1f);

        ImGui.PushFont(ImGuiRenderer.MonoFont);
        string txt = $"{emitter.Radius:0.#}px";
        if (emitter.Volume < 0.999f) txt += $" · {emitter.Volume:0.##}";
        var ts = ImGui.CalcTextSize(txt);
        var tp = new System.Numerics.Vector2(c.X - ts.X * 0.5f, c.Y - radius - ts.Y - 3f);
        dl.AddRectFilled(tp - new System.Numerics.Vector2(4, 2), tp + ts + new System.Numerics.Vector2(4, 2),
            ImGui.GetColorU32(new System.Numerics.Vector4(0.06f, 0.07f, 0.09f, 0.72f)), 3f);
        dl.AddText(tp, ImGui.GetColorU32(col), txt);
        ImGui.PopFont();
    }

    private void DrawBubbleAnchorGizmo(ImDrawListPtr dl, System.Numerics.Vector2 vpMin, Entity e)
    {
        if (_camera == null) return;

        var anchor = Talker.AnchorFor(e);
        var feet = e.GetComponent<PixelCore.Runtime.Components.Transform>()?.Position ?? anchor;
        var size = PixelCore.Runtime.UI.DialogueBubble.GizmoNominalSize();
        int tail = PixelCore.Runtime.UI.DialogueBubble.TailTipX;

        var topLeft = _camera.WorldToScreen(new Vector2(anchor.X - tail, anchor.Y - size.Y));
        var botRight = _camera.WorldToScreen(new Vector2(anchor.X - tail + size.X, anchor.Y));
        var a = new System.Numerics.Vector2(vpMin.X + MathF.Round(topLeft.X), vpMin.Y + MathF.Round(topLeft.Y));
        var b = new System.Numerics.Vector2(vpMin.X + MathF.Round(botRight.X), vpMin.Y + MathF.Round(botRight.Y));

        var col = new System.Numerics.Vector4(0.98f, 0.55f, 0.70f, 1f);
        uint fill = ImGui.GetColorU32(new System.Numerics.Vector4(col.X, col.Y, col.Z, 0.10f));
        uint line = ImGui.GetColorU32(new System.Numerics.Vector4(col.X, col.Y, col.Z, 0.90f));

        dl.AddRectFilled(a, b, fill, 2f);
        dl.AddRect(a, b, line, 2f, ImDrawFlags.None, 1f);

        var ap = _camera.WorldToScreen(anchor);
        var fp = _camera.WorldToScreen(feet);
        var apS = new System.Numerics.Vector2(vpMin.X + MathF.Round(ap.X), vpMin.Y + MathF.Round(ap.Y));
        var fpS = new System.Numerics.Vector2(vpMin.X + MathF.Round(fp.X), vpMin.Y + MathF.Round(fp.Y));
        dl.AddLine(apS, fpS, ImGui.GetColorU32(new System.Numerics.Vector4(col.X, col.Y, col.Z, 0.45f)), 1f);
        dl.AddLine(apS - new System.Numerics.Vector2(3, 0), apS + new System.Numerics.Vector2(3, 0), line, 1f);
        dl.AddLine(apS - new System.Numerics.Vector2(0, 3), apS + new System.Numerics.Vector2(0, 3), line, 1f);

        var talker = e.GetComponent<Talker>();
        bool auto = talker == null || talker.BubbleAnchor == Vector2.Zero;
        ImGui.PushFont(ImGuiRenderer.MonoFont);
        string txt = auto ? "Bubble (auto)" : $"Bubble {talker!.BubbleAnchor.X:0},{talker.BubbleAnchor.Y:0}";
        var ts = ImGui.CalcTextSize(txt);
        var tp = new System.Numerics.Vector2(a.X, a.Y - ts.Y - 3f);
        dl.AddRectFilled(tp - new System.Numerics.Vector2(4, 2), tp + ts + new System.Numerics.Vector2(4, 2),
            ImGui.GetColorU32(new System.Numerics.Vector4(0.06f, 0.07f, 0.09f, 0.72f)), 3f);
        dl.AddText(tp, ImGui.GetColorU32(col), txt);
        ImGui.PopFont();
    }

    private const float GizmoIconHalf = ImGuiRenderer.GizmoIconSize * 0.5f;

    private readonly List<(Entity Entity, System.Numerics.Vector2 Center, float Radius)> _gizmoHits = new();

    private Entity? HitTestGizmoChip(System.Numerics.Vector2 screenPos)
    {
        for (int i = _gizmoHits.Count - 1; i >= 0; i--)
        {
            var (e, c, r) = _gizmoHits[i];
            float dx = screenPos.X - c.X, dy = screenPos.Y - c.Y;
            if (dx * dx + dy * dy <= r * r) return e;
        }
        return null;
    }

    private static bool HasVisual(Entity e)
    {
        foreach (var c in e.Components)
            if (c is SpriteRenderer or TilemapRenderer) return true;
        return false;
    }

    private static bool IsSpawnPoint(Entity e) => Shell.DoorRefs.IsSpawnName(e.Name);

    private void DrawEntityGizmos(ImDrawListPtr dl, System.Numerics.Vector2 vpMin)
    {
        _gizmoHits.Clear();

        var spawnCol = new System.Numerics.Vector4(0.35f, 0.80f, 0.95f, 1f);
        var emptyCol = new System.Numerics.Vector4(0.62f, 0.65f, 0.70f, 1f);

        System.Numerics.Vector2? footMin = null, footMax = null;
        if (_state.CurrentScene!.FindPlayer() is { } player &&
            player.GetComponent<Transform>() is { } ptf)
        {
            foreach (var c in player.Components)
            {
                if (c is not Collider2D pc || !pc.Enabled || pc.IsTrigger) continue;
                var b = pc.GetBounds();
                footMin = new System.Numerics.Vector2(b.Min.X - ptf.Position.X, b.Min.Y - ptf.Position.Y);
                footMax = new System.Numerics.Vector2(b.Max.X - ptf.Position.X, b.Max.Y - ptf.Position.Y);
                break;
            }
        }

        foreach (var e in _state.CurrentScene.Entities)
        {
            if (!e.ActiveInHierarchy) continue;
            var tf = e.GetComponent<Transform>();
            if (tf == null) continue;

            bool spawn = IsSpawnPoint(e);
            bool interactable = e.GetComponent<Interactable>() != null;
            var light = e.GetComponent<Light2D>();
            bool visual = HasVisual(e);

            bool emitter = e.GetComponent<SoundEmitter>() is { Enabled: true };

            var s = _camera!.WorldToScreen(tf.Position);
            var at = new System.Numerics.Vector2(vpMin.X + MathF.Round(s.X), vpMin.Y + MathF.Round(s.Y));

            if (emitter) DrawGizmoIcon(dl, at, Icons.Speaker, e);

            if (visual && !spawn) continue;

            if (spawn)
            {
                float chipY = at.Y - GizmoIconHalf - 4f;
                if (footMin is { } fmin && footMax is { } fmax)
                {
                    var g0 = _camera.WorldToScreen(new Vector2(tf.Position.X + fmin.X, tf.Position.Y + fmin.Y));
                    var g1 = _camera.WorldToScreen(new Vector2(tf.Position.X + fmax.X, tf.Position.Y + fmax.Y));
                    var gmin = new System.Numerics.Vector2(vpMin.X + MathF.Round(g0.X), vpMin.Y + MathF.Round(g0.Y));
                    var gmax = new System.Numerics.Vector2(vpMin.X + MathF.Round(g1.X), vpMin.Y + MathF.Round(g1.Y));
                    dl.AddRectFilled(gmin, gmax, ImGui.GetColorU32(
                        new System.Numerics.Vector4(spawnCol.X, spawnCol.Y, spawnCol.Z, 0.16f)));
                    dl.AddRect(gmin, gmax, ImGui.GetColorU32(
                        new System.Numerics.Vector4(spawnCol.X, spawnCol.Y, spawnCol.Z, 0.85f)),
                        0f, ImDrawFlags.None, 1f);
                    chipY = MathF.Min(chipY, gmin.Y - GizmoIconHalf - 2f);
                }

                uint sc = ImGui.GetColorU32(spawnCol);
                dl.AddLine(new System.Numerics.Vector2(at.X - 5, at.Y), new System.Numerics.Vector2(at.X + 5, at.Y), sc, 1f);
                dl.AddLine(new System.Numerics.Vector2(at.X, at.Y - 5), new System.Numerics.Vector2(at.X, at.Y + 5), sc, 1f);
                dl.AddLine(new System.Numerics.Vector2(at.X, chipY + GizmoIconHalf),
                    new System.Numerics.Vector2(at.X, at.Y - 5),
                    ImGui.GetColorU32(new System.Numerics.Vector4(spawnCol.X, spawnCol.Y, spawnCol.Z, 0.55f)), 1f);
                DrawGizmoIcon(dl, new System.Numerics.Vector2(at.X, chipY), Icons.MapPin, e);
                DrawGizmoLabel(dl, new System.Numerics.Vector2(at.X, at.Y + 7), e.Name, spawnCol);
            }
            else if (interactable)
            {
                DrawGizmoIcon(dl, at, Icons.Interact, e);
            }
            else if (light is { Enabled: true })
            {
                DrawGizmoIcon(dl, at, Icons.Lightbulb, e);
            }
            else if (emitter)
            {
            }
            else
            {
                uint ec = ImGui.GetColorU32(new System.Numerics.Vector4(emptyCol.X, emptyCol.Y, emptyCol.Z, 0.55f));
                dl.AddLine(new System.Numerics.Vector2(at.X - 3, at.Y), new System.Numerics.Vector2(at.X + 3, at.Y), ec, 1f);
                dl.AddLine(new System.Numerics.Vector2(at.X, at.Y - 3), new System.Numerics.Vector2(at.X, at.Y + 3), ec, 1f);
            }
        }
    }

    private const uint GizmoIconColor = 0xD9FFFFFF;

    private void DrawGizmoIcon(ImDrawListPtr dl, System.Numerics.Vector2 center,
        string glyph, Entity? owner = null)
    {
        var font = ImGuiRenderer.GizmoIconFont;
        const float px = ImGuiRenderer.GizmoIconSize;
        var ts = font.CalcTextSizeA(px, float.MaxValue, 0f, glyph);
        var pos = new System.Numerics.Vector2(
            MathF.Round(center.X - ts.X * 0.5f), MathF.Round(center.Y - ts.Y * 0.5f));

        dl.AddText(font, px, pos, GizmoIconColor, glyph);

        if (owner != null) _gizmoHits.Add((owner, center, px * 0.5f));
    }

    private static void DrawGizmoLabel(ImDrawListPtr dl, System.Numerics.Vector2 center,
        string text, System.Numerics.Vector4 color)
    {
        ImGui.PushFont(ImGuiRenderer.MonoFont);
        var ts = ImGui.CalcTextSize(text);
        var pad = new System.Numerics.Vector2(4, 2);
        var min = new System.Numerics.Vector2(center.X - ts.X * 0.5f - pad.X, center.Y - pad.Y);
        var max = new System.Numerics.Vector2(min.X + ts.X + pad.X * 2, min.Y + ts.Y + pad.Y * 2);
        dl.AddRectFilled(min, max, ImGui.GetColorU32(new System.Numerics.Vector4(0.06f, 0.07f, 0.09f, 0.72f)), 3f);
        dl.AddText(new System.Numerics.Vector2(min.X + pad.X, min.Y + pad.Y),
            ImGui.GetColorU32(color), text);
        ImGui.PopFont();
    }

    private void DrawCollidersOverlay(ImDrawListPtr dl, System.Numerics.Vector2 vpMin, bool showAll)
    {
        uint fill = ImGui.GetColorU32(new System.Numerics.Vector4(0.45f, 1.00f, 0.25f, 0.22f));
        uint dim = ImGui.GetColorU32(new System.Numerics.Vector4(0.45f, 1.00f, 0.25f, 0.80f));
        uint bright = ImGui.GetColorU32(new System.Numerics.Vector4(0.60f, 1.00f, 0.35f, 1.00f));
        uint trigFill = ImGui.GetColorU32(new System.Numerics.Vector4(1.00f, 0.58f, 0.15f, 0.22f));
        uint trigDim = ImGui.GetColorU32(new System.Numerics.Vector4(1.00f, 0.58f, 0.15f, 0.80f));
        uint trigBright = ImGui.GetColorU32(new System.Numerics.Vector4(1.00f, 0.70f, 0.30f, 1.00f));

        foreach (var e in _state.CurrentScene!.Entities)
        {
            if (!e.ActiveInHierarchy) continue;
            bool selected = _state.IsSelected(e);
            if (!showAll && !selected) continue;
            bool primary = e == _state.SelectedEntity;
            foreach (var component in e.Components)
            {
                if (component is not Collider2D col || !col.Enabled) continue;
                bool trig = col.IsTrigger;
                DrawColliderShape(dl, vpMin, col,
                    trig ? (primary ? trigBright : trigDim) : (primary ? bright : dim), 1.0f,
                    selected ? (trig ? trigFill : fill) : 0u);
            }
        }
    }

    private void DrawColliderShape(ImDrawListPtr dl, System.Numerics.Vector2 vpMin,
        Collider2D col, uint color, float th, uint fill = 0)
    {
        var compose = _state.IsPlayMode ? PlayCompose : null;
        float inv = _viewScale > 0f ? 1f / _viewScale : 1f;
        System.Numerics.Vector2 S(Vector2 w)
        {
            var s = compose is { } pc ? pc.W2L(w) * inv : _camera!.WorldToScreen(w);
            return new System.Numerics.Vector2(vpMin.X + s.X, vpMin.Y + s.Y);
        }
        float zoom = compose is { } pcz ? pcz.Scale * inv : _camera!.Zoom;

        switch (col)
        {
            case CircleCollider2D circle:
                if (fill != 0) dl.AddCircleFilled(S(circle.Center), circle.Radius * zoom, fill);
                dl.AddCircle(S(circle.Center), circle.Radius * zoom, color, 0, th);
                break;

            case CapsuleCollider2D cap:
            {
                var (sa, sb) = cap.GetSegment();
                var a = S(sa); var b = S(sb);
                float r = cap.Radius * zoom;
                var dir = b - a;
                if (dir.LengthSquared() < 1e-4f)
                {
                    if (fill != 0) dl.AddCircleFilled(a, r, fill);
                    dl.AddCircle(a, r, color, 0, th);
                    break;
                }
                float ang = MathF.Atan2(dir.Y, dir.X);
                if (fill != 0)
                {
                    dl.PathArcTo(b, r, ang - MathF.PI / 2f, ang + MathF.PI / 2f, 16);
                    dl.PathArcTo(a, r, ang + MathF.PI / 2f, ang + 3f * MathF.PI / 2f, 16);
                    dl.PathFillConcave(fill);
                }
                dl.PathArcTo(b, r, ang - MathF.PI / 2f, ang + MathF.PI / 2f, 16);
                dl.PathArcTo(a, r, ang + MathF.PI / 2f, ang + 3f * MathF.PI / 2f, 16);
                dl.PathStroke(color, ImDrawFlags.Closed, th);
                break;
            }

            default:
            {
                var bd = col.GetBounds();
                var bMin = S(new Vector2(bd.Min.X, bd.Min.Y));
                var bMax = S(new Vector2(bd.Min.X + bd.Width, bd.Min.Y + bd.Height));
                if (fill != 0) dl.AddRectFilled(bMin, bMax, fill);
                dl.AddRect(bMin, bMax, color, 0f, ImDrawFlags.None, th);
                break;
            }
        }
    }

    private int _framingDragHandle = -1;
    private (Vector2 fixedPos, Rectangle bounds)? _framingDragStart;
    private const int FramingFixedHandle = 8;
    private const int FramingMinSize = 16;

    private static List<(int Id, Vector2 Pos)> GetFramingHandles(Scene scene)
    {
        var list = new List<(int, Vector2)>();
        if (scene.CameraBoundsEnabled)
        {
            var b = scene.CameraBounds;
            float l = b.Left, r = b.Right, t = b.Top, bo = b.Bottom;
            float cx = (l + r) * 0.5f, cy = (t + bo) * 0.5f;
            list.Add((0, new Vector2(l, cy)));
            list.Add((1, new Vector2(r, cy)));
            list.Add((2, new Vector2(cx, t)));
            list.Add((3, new Vector2(cx, bo)));
            list.Add((4, new Vector2(l, t)));
            list.Add((5, new Vector2(r, t)));
            list.Add((6, new Vector2(l, bo)));
            list.Add((7, new Vector2(r, bo)));
        }
        if (scene.CameraFixed)
            list.Add((FramingFixedHandle, scene.CameraFixedPos));
        return list;
    }

    private void DrawFramingEditOverlay(ImDrawListPtr dl, System.Numerics.Vector2 vpMin, Scene scene)
    {
        if (_camera == null) return;

        System.Numerics.Vector2 S(Vector2 w)
        {
            var s = _camera.WorldToScreen(w);
            return new System.Numerics.Vector2(vpMin.X + MathF.Round(s.X), vpMin.Y + MathF.Round(s.Y));
        }

        uint line = ImGui.GetColorU32(new System.Numerics.Vector4(0.45f, 0.78f, 1f, 0.95f));
        uint hot = ImGui.GetColorU32(new System.Numerics.Vector4(1f, 1f, 1f, 1f));
        uint outline = ImGui.GetColorU32(new System.Numerics.Vector4(0.04f, 0.10f, 0.16f, 1f));

        if (scene.CameraBoundsEnabled)
        {
            var b = scene.CameraBounds;
            var rMin = S(new Vector2(b.Left, b.Top));
            var rMax = S(new Vector2(b.Right, b.Bottom));
            dl.AddRect(rMin, rMax, line, 0f, ImDrawFlags.None, 1.5f);

            const string label = "Camera bounds";
            var ls = ImGui.CalcTextSize(label);
            dl.AddText(new System.Numerics.Vector2(rMin.X + 4, rMin.Y - ls.Y - 3), line, label);
        }

        if (scene.CameraFixed)
        {
            DrawGameViewRect(dl, vpMin, scene.CameraFixedPos, "Fixed camera");
            var c = S(scene.CameraFixedPos);
            const float arm = 7f;
            dl.AddLine(new System.Numerics.Vector2(c.X - arm, c.Y), new System.Numerics.Vector2(c.X + arm, c.Y), line, 1.5f);
            dl.AddLine(new System.Numerics.Vector2(c.X, c.Y - arm), new System.Numerics.Vector2(c.X, c.Y + arm), line, 1.5f);
        }
        else if (scene.CameraBoundsEnabled)
        {
            var anchor = scene.FindPlayer()?.GetComponent<Transform>()?.Position
                         ?? new Vector2(scene.CameraBounds.Center.X, scene.CameraBounds.Center.Y);
            DrawGameViewRect(dl, vpMin, anchor, "Game view");
        }

        var io = ImGui.GetIO();
        var mouseWorld = LocalToWorld(ScreenToLocal(new Vector2(io.MousePos.X, io.MousePos.Y)));
        float hitR = HANDLE_HIT_RADIUS / _camera.Zoom;

        foreach (var (id, pos) in GetFramingHandles(scene))
        {
            var p = S(pos);
            bool isHot = _framingDragHandle == id ||
                         (_framingDragHandle < 0 && Vector2.Distance(mouseWorld, pos) < hitR);
            float hs = isHot ? 4.5f : 3.5f;
            dl.AddRectFilled(new System.Numerics.Vector2(p.X - hs, p.Y - hs),
                             new System.Numerics.Vector2(p.X + hs, p.Y + hs), isHot ? hot : line, 1.5f);
            dl.AddRect(new System.Numerics.Vector2(p.X - hs, p.Y - hs),
                       new System.Numerics.Vector2(p.X + hs, p.Y + hs), outline, 1.5f);
            if (isHot && _isHovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
    }

    private bool HandleFramingEdit(Scene scene, Vector2 mouseWorld)
    {
        if (_framingDragHandle >= 0)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                ApplyFramingHandleDrag(scene, _framingDragHandle, mouseWorld);
                return true;
            }
            CommitFramingEdit(scene);
            _framingDragHandle = -1;
            _framingDragStart = null;
            return true;
        }

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && _isHovered)
        {
            float hitR = HANDLE_HIT_RADIUS / (_camera?.Zoom ?? 1f);
            foreach (var (id, pos) in GetFramingHandles(scene))
            {
                if (Vector2.Distance(mouseWorld, pos) >= hitR) continue;
                _framingDragHandle = id;
                _framingDragStart = (scene.CameraFixedPos, scene.CameraBounds);
                return true;
            }
        }
        return false;
    }

    private static void ApplyFramingHandleDrag(Scene scene, int handle, Vector2 mouseWorld)
    {
        if (handle == FramingFixedHandle)
        {
            scene.CameraFixedPos = new Vector2(MathF.Round(mouseWorld.X) + 0f, MathF.Round(mouseWorld.Y) + 0f);
            return;
        }

        var b = scene.CameraBounds;
        int l = b.Left, r = b.Right, t = b.Top, bo = b.Bottom;
        int mx = (int)MathF.Round(mouseWorld.X), my = (int)MathF.Round(mouseWorld.Y);

        void SetLeft() => l = Math.Min(mx, r - FramingMinSize);
        void SetRight() => r = Math.Max(mx, l + FramingMinSize);
        void SetTop() => t = Math.Min(my, bo - FramingMinSize);
        void SetBottom() => bo = Math.Max(my, t + FramingMinSize);

        switch (handle)
        {
            case 0: SetLeft(); break;
            case 1: SetRight(); break;
            case 2: SetTop(); break;
            case 3: SetBottom(); break;
            case 4: SetLeft(); SetTop(); break;
            case 5: SetRight(); SetTop(); break;
            case 6: SetLeft(); SetBottom(); break;
            case 7: SetRight(); SetBottom(); break;
        }
        scene.CameraBounds = new Rectangle(l, t, r - l, bo - t);
    }

    private void CommitFramingEdit(Scene scene)
    {
        if (_framingDragStart is not { } start) return;
        var now = (scene.CameraFixedPos, scene.CameraBounds);
        if (now == start) return;

        _state.CommandHistory.AddExecuted(new PropertyCommand<(Vector2 Fixed, Rectangle Bounds)>(
            this, "cameraFraming", start, now,
            v => { scene.CameraFixedPos = v.Fixed; scene.CameraBounds = v.Bounds; },
            "Edit Camera Framing"));
        _state.MarkDirty();
    }

    private static List<Vector2> GetColliderHandles(Collider2D col)
    {
        var handles = new List<Vector2>();
        switch (col)
        {
            case CircleCollider2D circle:
                handles.Add(circle.Center);
                handles.Add(circle.Center + new Vector2(circle.Radius, 0));
                break;

            case CapsuleCollider2D cap:
            {
                var c = cap.Center;
                var axis = cap.Horizontal ? new Vector2(1, 0) : new Vector2(0, 1);
                var perp = cap.Horizontal ? new Vector2(0, 1) : new Vector2(1, 0);
                float tip = cap.Length / 2f + cap.Radius;
                handles.Add(c);
                handles.Add(c + perp * cap.Radius);
                handles.Add(c - axis * tip);
                handles.Add(c + axis * tip);
                break;
            }

            case BoxCollider2D box:
            {
                var c = box.Center;
                var h = box.Size / 2f;
                handles.Add(c);
                handles.Add(c + new Vector2(-h.X, 0));
                handles.Add(c + new Vector2(h.X, 0));
                handles.Add(c + new Vector2(0, -h.Y));
                handles.Add(c + new Vector2(0, h.Y));
                break;
            }
        }
        return handles;
    }

    private void DrawColliderEditOverlay(ImDrawListPtr dl, System.Numerics.Vector2 vpMin,
        Collider2D col)
    {
        uint fill = ImGui.GetColorU32(col.IsTrigger
            ? new System.Numerics.Vector4(1.00f, 0.70f, 0.30f, 1f)
            : new System.Numerics.Vector4(0.31f, 1.00f, 0.47f, 1f));
        DrawColliderShape(dl, vpMin, col, fill, 1.5f);

        var io = ImGui.GetIO();
        var mouseWorld = LocalToWorld(ScreenToLocal(new Vector2(io.MousePos.X, io.MousePos.Y)));
        float hitR = HANDLE_HIT_RADIUS / _camera!.Zoom;

        var handles = GetColliderHandles(col);
        uint hot = ImGui.GetColorU32(new System.Numerics.Vector4(1f, 1f, 1f, 1f));
        uint outline = ImGui.GetColorU32(new System.Numerics.Vector4(0.05f, 0.15f, 0.08f, 1f));

        for (int i = 0; i < handles.Count; i++)
        {
            var s = _camera.WorldToScreen(handles[i]);
            var p = new System.Numerics.Vector2(vpMin.X + MathF.Round(s.X), vpMin.Y + MathF.Round(s.Y));
            bool isHot = _colDragHandle == i ||
                         (_colDragHandle < 0 && Vector2.Distance(mouseWorld, handles[i]) < hitR);
            float hs = isHot ? 4.5f : 3.5f;
            dl.AddRectFilled(new System.Numerics.Vector2(p.X - hs, p.Y - hs),
                             new System.Numerics.Vector2(p.X + hs, p.Y + hs), isHot ? hot : fill, 1.5f);
            dl.AddRect(new System.Numerics.Vector2(p.X - hs, p.Y - hs),
                       new System.Numerics.Vector2(p.X + hs, p.Y + hs), outline, 1.5f);
            if (isHot && _isHovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
    }

    private const float HANDLE_HIT_RADIUS = 7f;
    private int _colDragHandle = -1;
    private (Vector2 offset, Vector2 sizeOrRL)? _colDragStart;

    private bool HandleColliderEdit(Collider2D col, Vector2 mouseWorld)
    {
        if (_colDragHandle >= 0)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                ApplyColliderHandleDrag(col, _colDragHandle, mouseWorld);
                return true;
            }
            CommitColliderEdit(col);
            _colDragHandle = -1;
            _colDragStart = null;
            return true;
        }

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && _isHovered)
        {
            var handles = GetColliderHandles(col);
            float hitR = HANDLE_HIT_RADIUS / (_camera?.Zoom ?? 1f);
            for (int i = 0; i < handles.Count; i++)
            {
                if (Vector2.Distance(mouseWorld, handles[i]) >= hitR) continue;
                _colDragHandle = i;
                _colDragStart = col switch
                {
                    BoxCollider2D b => (b.Offset, b.Size),
                    CircleCollider2D ci => (ci.Offset, new Vector2(ci.Radius, 0)),
                    CapsuleCollider2D ca => (ca.Offset, new Vector2(ca.Radius, ca.Length)),
                    _ => (col.Offset, Vector2.Zero),
                };
                return true;
            }
        }
        return false;
    }

    private void ApplyColliderHandleDrag(Collider2D col, int handle, Vector2 mouseWorld)
    {
        var t = col.Entity.GetComponent<PixelCore.Runtime.Components.Transform>();
        if (t == null) return;
        Vector2 p = t.Position;
        Vector2 RoundV(Vector2 v) => new(MathF.Round(v.X) + 0f, MathF.Round(v.Y) + 0f);

        switch (col)
        {
            case CircleCollider2D circle:
                if (handle == 0) circle.Offset = RoundV(mouseWorld - p);
                else circle.Radius = MathF.Max(1f, MathF.Round(Vector2.Distance(mouseWorld, circle.Center)));
                break;

            case CapsuleCollider2D cap:
            {
                var axis = cap.Horizontal ? new Vector2(1, 0) : new Vector2(0, 1);
                var perp = cap.Horizontal ? new Vector2(0, 1) : new Vector2(1, 0);
                var rel = mouseWorld - cap.Center;
                if (handle == 0)
                {
                    cap.Offset = RoundV(mouseWorld - p);
                }
                else if (handle == 1)
                {
                    cap.Radius = MathF.Max(1f, MathF.Round(MathF.Abs(Vector2.Dot(rel, perp))));
                }
                else
                {
                    float half = cap.Length / 2f + cap.Radius;
                    float cAxis = Vector2.Dot(cap.Center, axis);
                    float span = cap.Radius * 2f;
                    float tipNeg = cAxis - half, tipPos = cAxis + half;
                    if (handle == 3) tipPos = MathF.Max(Vector2.Dot(mouseWorld, axis), tipNeg + span);
                    else             tipNeg = MathF.Min(Vector2.Dot(mouseWorld, axis), tipPos - span);
                    tipNeg = MathF.Round(tipNeg); tipPos = MathF.Round(tipPos);

                    cap.Length = MathF.Max(0f, (tipPos - tipNeg) - span);
                    var off = cap.Offset + axis * ((tipNeg + tipPos) / 2f - cAxis);
                    cap.Offset = new Vector2(off.X + 0f, off.Y + 0f);
                }
                break;
            }

            case BoxCollider2D box:
            {
                var c = box.Center;
                var h = box.Size / 2f;
                float left = c.X - h.X, right = c.X + h.X, top = c.Y - h.Y, bottom = c.Y + h.Y;
                switch (handle)
                {
                    case 0: box.Offset = RoundV(mouseWorld - p); return;
                    case 1: left = MathF.Min(mouseWorld.X, right - 1f); break;
                    case 2: right = MathF.Max(mouseWorld.X, left + 1f); break;
                    case 3: top = MathF.Min(mouseWorld.Y, bottom - 1f); break;
                    case 4: bottom = MathF.Max(mouseWorld.Y, top + 1f); break;
                }
                left = MathF.Round(left); right = MathF.Round(right);
                top = MathF.Round(top); bottom = MathF.Round(bottom);
                box.Size = new Vector2(right - left, bottom - top);
                var off = new Vector2((left + right) / 2f, (top + bottom) / 2f) - p;
                box.Offset = new Vector2(off.X + 0f, off.Y + 0f);
                break;
            }
        }
    }

    private void CommitColliderEdit(Collider2D col)
    {
        if (_colDragStart is not { } start) return;

        (Vector2, Vector2) now = col switch
        {
            BoxCollider2D b => (b.Offset, b.Size),
            CircleCollider2D ci => (ci.Offset, new Vector2(ci.Radius, 0)),
            CapsuleCollider2D ca => (ca.Offset, new Vector2(ca.Radius, ca.Length)),
            _ => (col.Offset, Vector2.Zero),
        };
        if (now == (start.offset, start.sizeOrRL)) return;

        Action<(Vector2 o, Vector2 s)> setter = col switch
        {
            BoxCollider2D b => v => { b.Offset = v.o; b.Size = v.s; },
            CircleCollider2D ci => v => { ci.Offset = v.o; ci.Radius = v.s.X; },
            CapsuleCollider2D ca => v => { ca.Offset = v.o; ca.Radius = v.s.X; ca.Length = v.s.Y; },
            _ => v => col.Offset = v.o,
        };
        _state.CommandHistory.AddExecuted(new PropertyCommand<(Vector2, Vector2)>(
            this, "colliderEdit", (start.offset, start.sizeOrRL), now, setter, "Edit Collider", col.Entity));
        _state.MarkDirty();
    }

    private int _shadowDragHandle = -1;
    private (int? w, int? h, Point offset)? _shadowDragStart;

    public static Rectangle ShadowRect(SpriteRenderer sr)
    {
        var t = sr.Entity?.GetComponent<PixelCore.Runtime.Components.Transform>();
        if (t == null) return Rectangle.Empty;
        var size = Runtime.Rendering.ShadowRenderer.PlateSize(sr, (int)sr.GetDrawSize().X);
        return Runtime.Rendering.ShadowRenderer.ShadowQuad(size, t.Position, sr.ShadowOffset);
    }

    private static List<Vector2> GetShadowHandles(SpriteRenderer sr)
    {
        var handles = new List<Vector2>();
        var r = ShadowRect(sr);
        if (r.Width <= 0 || r.Height <= 0) return handles;

        var c = new Vector2(r.X + r.Width / 2f, r.Y + r.Height / 2f);
        handles.Add(c);
        if (sr.ShadowTexture != null) return handles;

        handles.Add(new Vector2(r.Left, c.Y));
        handles.Add(new Vector2(r.Right, c.Y));
        handles.Add(new Vector2(c.X, r.Top));
        handles.Add(new Vector2(c.X, r.Bottom));
        return handles;
    }

    private bool HandleShadowEdit(SpriteRenderer sr, Vector2 mouseWorld)
    {
        if (_shadowDragHandle >= 0)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                ApplyShadowHandleDrag(sr, _shadowDragHandle, mouseWorld);
                return true;
            }
            CommitShadowEdit(sr);
            _shadowDragHandle = -1;
            _shadowDragStart = null;
            return true;
        }

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && _isHovered)
        {
            var handles = GetShadowHandles(sr);
            float hitR = HANDLE_HIT_RADIUS / (_camera?.Zoom ?? 1f);
            for (int i = 0; i < handles.Count; i++)
            {
                if (Vector2.Distance(mouseWorld, handles[i]) >= hitR) continue;
                _shadowDragHandle = i;
                _shadowDragStart = (sr.ShadowWidth, sr.ShadowHeight, sr.ShadowOffset);
                return true;
            }
        }
        return false;
    }

    private void ApplyShadowHandleDrag(SpriteRenderer sr, int handle, Vector2 mouseWorld)
    {
        var t = sr.Entity?.GetComponent<PixelCore.Runtime.Components.Transform>();
        if (t == null) return;

        var next = DragShadowHandle((sr.ShadowWidth, sr.ShadowHeight, sr.ShadowOffset),
                                    ShadowRect(sr), t.Position, handle, mouseWorld);
        sr.ShadowWidth = next.W; sr.ShadowHeight = next.H; sr.ShadowOffset = next.Offset;
    }

    public static (int? W, int? H, Point Offset) DragShadowHandle(
        (int? W, int? H, Point Offset) current, Rectangle rect, Vector2 foot, int handle, Vector2 mouseWorld)
    {
        if (handle == 0)
        {
            var d = mouseWorld - foot;
            return (current.W, current.H, new Point((int)MathF.Round(d.X), (int)MathF.Round(d.Y)));
        }
        if (rect.Width <= 0 || rect.Height <= 0) return current;

        float left = rect.Left, right = rect.Right, top = rect.Top, bottom = rect.Bottom;
        switch (handle)
        {
            case 1: left = MathF.Min(mouseWorld.X, right - 1f); break;
            case 2: right = MathF.Max(mouseWorld.X, left + 1f); break;
            case 3: top = MathF.Min(mouseWorld.Y, bottom - 1f); break;
            case 4: bottom = MathF.Max(mouseWorld.Y, top + 1f); break;
            default: return current;
        }
        left = MathF.Round(left); right = MathF.Round(right);
        top = MathF.Round(top); bottom = MathF.Round(bottom);

        int w = Math.Max(1, (int)(right - left));
        int h = Math.Max(1, (int)(bottom - top));

        var size = new Point(w, h);
        var off = new Point((int)MathF.Round((left + right) / 2f - foot.X),
                            (int)MathF.Round((top + bottom) / 2f - foot.Y));
        var probe = Runtime.Rendering.ShadowRenderer.ShadowQuad(size, foot, off);
        return (w, h, new Point(off.X + (int)left - probe.X, off.Y + (int)top - probe.Y));
    }

    private void CommitShadowEdit(SpriteRenderer sr)
    {
        if (_shadowDragStart is not { } start) return;
        var now = (sr.ShadowWidth, sr.ShadowHeight, sr.ShadowOffset);
        if (now == (start.w, start.h, start.offset)) return;

        _state.CommandHistory.AddExecuted(new PropertyCommand<(int? W, int? H, Point Offset)>(
            this, "shadowEdit", (start.w, start.h, start.offset), now,
            v => { sr.ShadowWidth = v.W; sr.ShadowHeight = v.H; sr.ShadowOffset = v.Offset; },
            "Edit Shadow", sr.Entity));
        _state.MarkDirty();
    }

    private void DrawShadowEditOverlay(ImDrawListPtr dl, System.Numerics.Vector2 vpMin, SpriteRenderer sr)
    {
        if (_camera == null) return;
        var r = ShadowRect(sr);
        if (r.Width <= 0 || r.Height <= 0) return;

        uint fill = ImGui.GetColorU32(new System.Numerics.Vector4(0.45f, 0.80f, 1.00f, 1f));
        var tl = _camera.WorldToScreen(new Vector2(r.Left, r.Top));
        var br = _camera.WorldToScreen(new Vector2(r.Right, r.Bottom));
        dl.AddRect(new System.Numerics.Vector2(vpMin.X + MathF.Round(tl.X), vpMin.Y + MathF.Round(tl.Y)),
                   new System.Numerics.Vector2(vpMin.X + MathF.Round(br.X), vpMin.Y + MathF.Round(br.Y)),
                   fill, 0f, ImDrawFlags.None, 1.5f);

        var io = ImGui.GetIO();
        var mouseWorld = LocalToWorld(ScreenToLocal(new Vector2(io.MousePos.X, io.MousePos.Y)));
        float hitR = HANDLE_HIT_RADIUS / _camera.Zoom;

        uint hot = ImGui.GetColorU32(new System.Numerics.Vector4(1f, 1f, 1f, 1f));
        uint outline = ImGui.GetColorU32(new System.Numerics.Vector4(0.05f, 0.10f, 0.15f, 1f));

        var handles = GetShadowHandles(sr);
        for (int i = 0; i < handles.Count; i++)
        {
            var sp = _camera.WorldToScreen(handles[i]);
            var p = new System.Numerics.Vector2(vpMin.X + MathF.Round(sp.X), vpMin.Y + MathF.Round(sp.Y));
            bool isHot = _shadowDragHandle == i ||
                         (_shadowDragHandle < 0 && Vector2.Distance(mouseWorld, handles[i]) < hitR);
            float hs = isHot ? 4.5f : 3.5f;
            dl.AddRectFilled(new System.Numerics.Vector2(p.X - hs, p.Y - hs),
                             new System.Numerics.Vector2(p.X + hs, p.Y + hs), isHot ? hot : fill, 1.5f);
            dl.AddRect(new System.Numerics.Vector2(p.X - hs, p.Y - hs),
                       new System.Numerics.Vector2(p.X + hs, p.Y + hs), outline, 1.5f);
            if (isHot && _isHovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
    }

    private void DrawLightGizmo(ImDrawListPtr dl, System.Numerics.Vector2 vpMin,
        Light2D light, Vector2 worldPos)
    {
        if (_camera == null) return;

        System.Numerics.Vector2 S(Vector2 w)
        {
            var s = _camera.WorldToScreen(w);
            return new System.Numerics.Vector2(vpMin.X + s.X, vpMin.Y + s.Y);
        }

        var lc = light.Color.ToVector4();
        uint col = ImGui.GetColorU32(new System.Numerics.Vector4(lc.X, lc.Y, lc.Z, 0.95f));
        var center = S(worldPos);
        const float th = 1.5f;

        switch (light.Shape)
        {
            case LightShape.Point:
                dl.AddCircle(center, light.Radius * _camera.Zoom, col, 0, th);
                break;

            case LightShape.Cone:
            {
                float half = MathHelper.ToRadians(MathHelper.Clamp(light.SpreadAngle, 2f, 170f) / 2f);
                float rot = MathHelper.ToRadians(light.Rotation);
                var edgeA = S(worldPos + new Vector2(MathF.Cos(rot - half), MathF.Sin(rot - half)) * light.Length);
                var edgeB = S(worldPos + new Vector2(MathF.Cos(rot + half), MathF.Sin(rot + half)) * light.Length);
                dl.AddLine(center, edgeA, col, th);
                dl.AddLine(center, edgeB, col, th);

                int segs = Math.Max(4, (int)(light.SpreadAngle / 8f));
                var prev = edgeA;
                for (int i = 1; i <= segs; i++)
                {
                    float a = rot - half + half * 2f * i / segs;
                    var p = S(worldPos + new Vector2(MathF.Cos(a), MathF.Sin(a)) * light.Length);
                    dl.AddLine(prev, p, col, th);
                    prev = p;
                }
                break;
            }

            case LightShape.Rect:
            {
                float rot = MathHelper.ToRadians(light.Rotation);
                var hx = new Vector2(MathF.Cos(rot), MathF.Sin(rot)) * (light.RectSize.X / 2f);
                var hy = new Vector2(-MathF.Sin(rot), MathF.Cos(rot)) * (light.RectSize.Y / 2f);
                var p1 = S(worldPos - hx - hy);
                var p2 = S(worldPos + hx - hy);
                var p3 = S(worldPos + hx + hy);
                var p4 = S(worldPos - hx + hy);
                dl.AddLine(p1, p2, col, th); dl.AddLine(p2, p3, col, th);
                dl.AddLine(p3, p4, col, th); dl.AddLine(p4, p1, col, th);
                break;
            }

            case LightShape.Texture:
            {
                float rot = MathHelper.ToRadians(light.Rotation);
                if (light.Texture == null)
                {
                    const float k = 6f;
                    dl.AddLine(center - new System.Numerics.Vector2(k, k), center + new System.Numerics.Vector2(k, k), col, th);
                    dl.AddLine(center - new System.Numerics.Vector2(k, -k), center + new System.Numerics.Vector2(k, -k), col, th);
                    break;
                }
                var half = new Vector2(light.Texture.Width * light.Scale.X, light.Texture.Height * light.Scale.Y) / 2f;
                var ax = new Vector2(MathF.Cos(rot), MathF.Sin(rot)) * half.X;
                var ay = new Vector2(-MathF.Sin(rot), MathF.Cos(rot)) * half.Y;
                var q1 = S(worldPos - ax - ay);
                var q2 = S(worldPos + ax - ay);
                var q3 = S(worldPos + ax + ay);
                var q4 = S(worldPos - ax + ay);
                dl.AddLine(q1, q2, col, th); dl.AddLine(q2, q3, col, th);
                dl.AddLine(q3, q4, col, th); dl.AddLine(q4, q1, col, th);
                break;
            }
        }
    }

    public event Action? OnViewTogglesChanged;

    private void DrawViewToolbar(ImDrawListPtr dl, System.Numerics.Vector2 vpMin, float rightEdge)
    {
        var audio = Runtime.Audio.AudioManager.Instance;
        (string Icon, string Tip, bool On, Action Toggle)[] items =
        {
            (Icons.Cube, $"Colliders {(_state.ShowColliders ? "on" : "off")}",
             _state.ShowColliders, () => _state.ShowColliders = !_state.ShowColliders),
            (Icons.MapPin, $"Gizmos {(_state.ShowGizmos ? "on" : "off")}",
             _state.ShowGizmos, () => _state.ShowGizmos = !_state.ShowGizmos),
            (audio.Muted ? Icons.SpeakerSlash : Icons.Speaker,
             audio.Muted ? "Muted - click to unmute" : "Sound on - click to mute",
             audio.Muted, () => audio.Muted = !audio.Muted),
        };

        const float BtnW = 24f, BtnH = 20f, Gap = 1f, Pad = 2f;

        string clockText = Clock == null ? ""
            : $"{Icons.Sun}  {(int)Clock.Hour:00}:{(int)(Clock.Hour % 1f * 60):00}";
        const float ClockPadX = 9f, DivW = 11f;
        float clockW = 0f;
        if (Clock != null)
        {
            ImGui.PushFont(ImGuiRenderer.MonoFont);
            clockW = DivW + ImGui.CalcTextSize(clockText).X + ClockPadX;
            ImGui.PopFont();
        }

        float groupW = items.Length * BtnW + (items.Length - 1) * Gap + Pad * 2 + clockW;
        float groupH = BtnH + Pad * 2;
        var gMin = new System.Numerics.Vector2(rightEdge - groupW, vpMin.Y + 10);
        var gMax = new System.Numerics.Vector2(rightEdge, gMin.Y + groupH);

        var rec = EditorTheme.Recessed;
        float gRound = HudChipRound;
        dl.AddRectFilled(gMin, gMax, ImGui.GetColorU32(
            new System.Numerics.Vector4(rec.X, rec.Y, rec.Z, 0.94f)), gRound);
        dl.AddRect(gMin, gMax, ImGui.GetColorU32(ImGuiCol.Border), gRound);

        var savedCursor = ImGui.GetCursorScreenPos();
        var accent = EditorTheme.Accent;

        for (int i = 0; i < items.Length; i++)
        {
            var (icon, tip, on, toggle) = items[i];
            var bMin = new System.Numerics.Vector2(gMin.X + Pad + i * (BtnW + Gap), gMin.Y + Pad);
            var bMax = new System.Numerics.Vector2(bMin.X + BtnW, bMin.Y + BtnH);

            ImGui.SetCursorScreenPos(bMin);
            ImGui.InvisibleButton($"##viewToggle{i}", new System.Numerics.Vector2(BtnW, BtnH));
            bool hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                _chipHotspot = true;
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                ImGui.SetTooltip(tip);
            }
            if (ImGui.IsItemClicked()) { toggle(); OnViewTogglesChanged?.Invoke(); }

            if (on)
                dl.AddRectFilled(bMin, bMax, ImGui.GetColorU32(
                    new System.Numerics.Vector4(accent.X, accent.Y, accent.Z, hovered ? 1f : 0.88f)), 5f);
            else if (hovered)
                dl.AddRectFilled(bMin, bMax, ImGui.GetColorU32(
                    new System.Numerics.Vector4(1f, 1f, 1f, 0.12f)), 5f);

            var ts = ImGui.CalcTextSize(icon);
            var tp = new System.Numerics.Vector2(
                MathF.Round(bMin.X + (BtnW - ts.X) * 0.5f), MathF.Round(bMin.Y + (BtnH - ts.Y) * 0.5f));
            dl.AddText(tp, on
                ? ImGui.GetColorU32(new System.Numerics.Vector4(0.10f, 0.08f, 0.03f, 1f))
                : ImGui.GetColorU32(hovered ? ImGuiCol.Text : ImGuiCol.TextDisabled), icon);
        }

        if (Clock != null)
        {
            float divX = MathF.Round(gMax.X - clockW + DivW * 0.5f);
            dl.AddLine(new System.Numerics.Vector2(divX, gMin.Y + 5),
                       new System.Numerics.Vector2(divX, gMax.Y - 5),
                       ImGui.GetColorU32(ImGuiCol.Border), 1f);

            ImGui.PushFont(ImGuiRenderer.MonoFont);
            var cts = ImGui.CalcTextSize(clockText);
            var ctp = new System.Numerics.Vector2(
                MathF.Round(gMax.X - clockW + DivW),
                MathF.Round(gMin.Y + (groupH - cts.Y) * 0.5f));
            dl.AddText(ctp, ImGui.GetColorU32(EditorTheme.Accent), clockText);
            ImGui.PopFont();

            var hitMin = new System.Numerics.Vector2(gMax.X - clockW + DivW * 0.5f, gMin.Y);
            ImGui.SetCursorScreenPos(hitMin);
            ImGui.InvisibleButton("##timeChip",
                new System.Numerics.Vector2(gMax.X - hitMin.X, groupH));
            if (ImGui.IsItemHovered())
            {
                _chipHotspot = true;
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                ImGui.SetTooltip("The in-game clock - click to scrub\nOutdoor ambience, sun shadows and window light (FollowSun) follow it");
            }
            if (ImGui.IsItemClicked()) ImGui.OpenPopup("##timeScrub");

            ImGui.SetNextWindowPos(new System.Numerics.Vector2(gMax.X, gMax.Y + 6),
                ImGuiCond.Appearing, new System.Numerics.Vector2(1f, 0f));
            if (ImGui.BeginPopup("##timeScrub"))
            {
                _chipHotspot = true;
                float hour = Clock.Hour;
                ImGui.TextDisabled(Icons.Sun + "  In-game clock");
                ImGui.SetNextItemWidth(180);
                if (ImGui.SliderFloat("##tod", ref hour, 0f, 24f, ""))
                    Clock.Hour = MathF.Min(hour, 23.999f);
                ImGui.SameLine();
                ImGui.PushFont(ImGuiRenderer.MonoFont);
                ImGui.TextDisabled($"{(int)Clock.Hour:00}:{(int)(Clock.Hour % 1f * 60):00}");
                ImGui.PopFont();
                ImGui.EndPopup();
            }
        }

        ImGui.SetCursorScreenPos(savedCursor);
        ImGui.Dummy(System.Numerics.Vector2.Zero);
    }

    private void DrawPlayOverlayButtons(ImDrawListPtr dl,
        System.Numerics.Vector2 vpMin, System.Numerics.Vector2 vpMax)
    {
        bool unlocked = _state.PlayEditUnlocked;
        var items = new (string Icon, string Tip, bool On, Action Toggle)[]
        {
            (unlocked ? Icons.LockOpen : Icons.Lock,
             unlocked
                ? "Editing unlocked (F8) - click and drag to edit. Locking returns the camera to the player"
                : "Editing locked (F8) - unlock to click and edit in the inspector during play",
             unlocked,
             _state.TogglePlayEditLock),
            (Icons.Pause,
             _state.PlayPaused ? "Paused - click to resume" : "Pause - freeze moving targets and click them",
             _state.PlayPaused,
             () => _state.PlayPaused = !_state.PlayPaused),
        };

        const float BtnW = 24f, BtnH = 20f, Gap = 1f, Pad = 2f;
        float groupW = items.Length * BtnW + (items.Length - 1) * Gap + Pad * 2;
        float groupH = BtnH + Pad * 2;
        var gMin = new System.Numerics.Vector2(vpMin.X + 10, vpMax.Y - 10 - groupH);
        var gMax = new System.Numerics.Vector2(gMin.X + groupW, gMin.Y + groupH);

        var rec = EditorTheme.Recessed;
        float gRound = groupH * 0.5f;
        dl.AddRectFilled(gMin, gMax, ImGui.GetColorU32(
            new System.Numerics.Vector4(rec.X, rec.Y, rec.Z, 0.94f)), gRound);
        dl.AddRect(gMin, gMax, ImGui.GetColorU32(ImGuiCol.Border), gRound);

        var savedCursor = ImGui.GetCursorScreenPos();
        var accent = EditorTheme.Accent;

        for (int i = 0; i < items.Length; i++)
        {
            var (icon, tip, on, toggle) = items[i];
            var bMin = new System.Numerics.Vector2(gMin.X + Pad + i * (BtnW + Gap), gMin.Y + Pad);
            var bMax = new System.Numerics.Vector2(bMin.X + BtnW, bMin.Y + BtnH);

            ImGui.SetCursorScreenPos(bMin);
            ImGui.InvisibleButton($"##playOverlay{i}", new System.Numerics.Vector2(BtnW, BtnH));
            bool hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                _chipHotspot = true;
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                ImGui.SetTooltip(tip);
            }
            if (ImGui.IsItemClicked()) toggle();

            if (on)
                dl.AddRectFilled(bMin, bMax, ImGui.GetColorU32(
                    new System.Numerics.Vector4(accent.X, accent.Y, accent.Z, hovered ? 1f : 0.88f)), BtnH * 0.5f);
            else if (hovered)
                dl.AddRectFilled(bMin, bMax, ImGui.GetColorU32(
                    new System.Numerics.Vector4(1f, 1f, 1f, 0.12f)), BtnH * 0.5f);

            var ts = ImGui.CalcTextSize(icon);
            var tp = new System.Numerics.Vector2(
                MathF.Round(bMin.X + (BtnW - ts.X) * 0.5f), MathF.Round(bMin.Y + (BtnH - ts.Y) * 0.5f));
            dl.AddText(tp, on
                ? ImGui.GetColorU32(new System.Numerics.Vector4(0.10f, 0.08f, 0.03f, 1f))
                : ImGui.GetColorU32(hovered ? ImGuiCol.Text : ImGuiCol.TextDisabled), icon);
        }

        ImGui.SetCursorScreenPos(savedCursor);
        ImGui.Dummy(System.Numerics.Vector2.Zero);
    }

    private void DrawGameViewRect(ImDrawListPtr dl, System.Numerics.Vector2 vpMin)
    {
        var pt = _state.CurrentScene?.FindPlayer()?.GetComponent<Transform>();
        if (pt == null) return;
        DrawGameViewRect(dl, vpMin, pt.Position, "Game view");
    }

    private void DrawGameViewRect(ImDrawListPtr dl, System.Numerics.Vector2 vpMin,
        Vector2 center, string label)
    {
        if (_camera == null) return;

        var half = new Vector2(_camera.GameWidth * 0.5f, _camera.GameHeight * 0.5f);
        var a = _camera.WorldToScreen(center - half);
        var b = _camera.WorldToScreen(center + half);
        var rMin = new System.Numerics.Vector2(vpMin.X + a.X, vpMin.Y + a.Y);
        var rMax = new System.Numerics.Vector2(vpMin.X + b.X, vpMin.Y + b.Y);

        dl.AddRect(rMin, rMax, ImGui.GetColorU32(new System.Numerics.Vector4(1f, 1f, 1f, 0.85f)),
            0f, ImDrawFlags.None, 1f);

        var ls = ImGui.CalcTextSize(label);
        dl.AddText(new System.Numerics.Vector2(rMin.X + 4, rMin.Y - ls.Y - 3),
            ImGui.GetColorU32(new System.Numerics.Vector4(1f, 1f, 1f, 0.7f)), label);
    }

    internal static readonly System.Numerics.Vector2 HudChipPad = new(9, 5);

    internal const float HudChipRound = 7f;

    internal static void DrawHudChipSplit(ImDrawListPtr dl, System.Numerics.Vector2 pos,
        string left, string right)
    {
        ImGui.PushFont(ImGuiRenderer.MonoFont);
        var lt = ImGui.CalcTextSize(left);
        var rt = ImGui.CalcTextSize(right);
        const float Mid = 17f;
        float h = MathF.Max(lt.Y, rt.Y) + HudChipPad.Y * 2;
        var max = new System.Numerics.Vector2(
            pos.X + lt.X + Mid + rt.X + HudChipPad.X * 2, pos.Y + h);

        var rec = EditorTheme.Recessed;
        dl.AddRectFilled(pos, max, ImGui.GetColorU32(
            new System.Numerics.Vector4(rec.X, rec.Y, rec.Z, 0.94f)), HudChipRound);
        dl.AddRect(pos, max, ImGui.GetColorU32(ImGuiCol.Border), HudChipRound);

        uint dim = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        dl.AddText(pos + HudChipPad, dim, left);
        float divX = MathF.Round(pos.X + HudChipPad.X + lt.X + Mid * 0.5f);
        dl.AddLine(new System.Numerics.Vector2(divX, pos.Y + 6),
                   new System.Numerics.Vector2(divX, max.Y - 6),
                   ImGui.GetColorU32(ImGuiCol.Border), 1f);
        dl.AddText(new System.Numerics.Vector2(pos.X + HudChipPad.X + lt.X + Mid, pos.Y + HudChipPad.Y),
                   dim, right);
        ImGui.PopFont();
    }

    internal static System.Numerics.Vector2 HudChipSize(string text)
    {
        ImGui.PushFont(ImGuiRenderer.MonoFont);
        var ts = ImGui.CalcTextSize(text);
        ImGui.PopFont();
        return ts + HudChipPad * 2;
    }

    internal static float DrawHudChip(ImDrawListPtr dl, System.Numerics.Vector2 pos, string text,
        uint? textColor = null)
    {
        ImGui.PushFont(ImGuiRenderer.MonoFont);
        var ts = ImGui.CalcTextSize(text);
        var max = new System.Numerics.Vector2(
            pos.X + ts.X + HudChipPad.X * 2, pos.Y + ts.Y + HudChipPad.Y * 2);
        float r = HudChipRound;
        var rec = EditorTheme.Recessed;
        dl.AddRectFilled(pos, max, ImGui.GetColorU32(
            new System.Numerics.Vector4(rec.X, rec.Y, rec.Z, 0.94f)), r);
        dl.AddRect(pos, max, ImGui.GetColorU32(ImGuiCol.Border), r);
        dl.AddText(pos + HudChipPad, textColor ?? ImGui.GetColorU32(ImGuiCol.TextDisabled), text);
        ImGui.PopFont();
        return max.X - pos.X;
    }

    private static void AddDashedRect(ImDrawListPtr dl, System.Numerics.Vector2 min, System.Numerics.Vector2 max,
        uint col, float dash, float gap, float thickness)
    {
        for (float x = min.X; x < max.X; x += dash + gap)
        {
            float x2 = MathF.Min(x + dash, max.X);
            dl.AddLine(new System.Numerics.Vector2(x, min.Y), new System.Numerics.Vector2(x2, min.Y), col, thickness);
            dl.AddLine(new System.Numerics.Vector2(x, max.Y), new System.Numerics.Vector2(x2, max.Y), col, thickness);
        }
        for (float y = min.Y; y < max.Y; y += dash + gap)
        {
            float y2 = MathF.Min(y + dash, max.Y);
            dl.AddLine(new System.Numerics.Vector2(min.X, y), new System.Numerics.Vector2(min.X, y2), col, thickness);
            dl.AddLine(new System.Numerics.Vector2(max.X, y), new System.Numerics.Vector2(max.X, y2), col, thickness);
        }
    }

    private void CreateRenderTarget(int width, int height)
    {
        if (RenderTarget != null)
        {
            _imGuiRenderer.UnbindTexture(_renderTargetId);
            RenderTarget.Dispose();
        }

        RenderTarget = new RenderTarget2D(
            _graphicsDevice,
            width,
            height,
            false,
            SurfaceFormat.Color,
            DepthFormat.None,
            0,
            RenderTargetUsage.PreserveContents
        );

        _renderTargetId = _imGuiRenderer.BindTexture(RenderTarget);

        _lastWidth = width;
        _lastHeight = height;
    }

    public void Dispose()
    {
        if (RenderTarget != null)
        {
            _imGuiRenderer.UnbindTexture(_renderTargetId);
            RenderTarget.Dispose();
            RenderTarget = null;
        }
    }
}

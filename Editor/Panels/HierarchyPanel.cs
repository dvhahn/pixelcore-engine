using System;
using System.Collections.Generic;
using System.IO;
using ImGuiNET;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Components;
using PixelCore.Editor.Commands;

namespace PixelCore.Editor.Panels;

public class HierarchyPanel
{
    private readonly EditorState _state;

    private readonly List<Entity> _dragged = new();

    private bool _dragStarted;

    private Entity? _pendingCollapse;

    private Entity? _renamingEntity;
    private string _renameBuffer = "";
    private bool _focusRename;

    public event System.Action<Entity>? OnPlayerCreated;

    public event System.Action<string>? OnToast;

    private bool _showSaveSceneDialog;
    private Entity? _entityToSave;
    private string _saveSceneName = "";

    public string? PendingSceneId { get; set; }

    private List<Entity> _rowOrder = new();
    private List<Entity> _prevRowOrder = new();
    private Entity? _selectionAnchor;

    private Entity? _pingTarget;
    private readonly HashSet<Entity> _pingAncestors = new();
    private bool _selfSelecting;

    public HierarchyPanel(EditorState state)
    {
        _state = state;

        _state.OnSelectionChanged += primary =>
        {
            if (_selfSelecting || primary == null) return;
            _pingTarget = primary;
        };
    }

    private void PreparePing()
    {
        _pingAncestors.Clear();
        if (_pingTarget == null) return;

        if (_pingTarget.Scene != _state.CurrentScene) { _pingTarget = null; return; }

        for (var p = _pingTarget.Parent; p != null; p = p.Parent)
            _pingAncestors.Add(p);
    }

    public void DrawContent()
    {
        if (_state.CurrentScene != null)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,
                new System.Numerics.Vector2(ImGui.GetStyle().ItemSpacing.X, 1));
            ImGui.BeginChild("EntityList", new System.Numerics.Vector2(0, 0),
                ImGuiChildFlags.None, ImGuiWindowFlags.NoNavInputs);
            bool listFocused = ImGui.IsWindowFocused();

            (_prevRowOrder, _rowOrder) = (_rowOrder, _prevRowOrder);
            _rowOrder.Clear();

            PreparePing();

            var rootEntities = new List<Entity>();
            foreach (var entity in _state.CurrentScene.Entities)
            {
                if (entity.IsRoot)
                    rootEntities.Add(entity);
            }

            if (rootEntities.Count == 0)
            {
                ImGui.Dummy(new System.Numerics.Vector2(0, 6));
                ImGui.TextDisabled("Right-click to create an entity");
            }

            for (int i = 0; i < rootEntities.Count; i++)
            {
                DrawDropZone(null, i);
                DrawEntityNode(rootEntities[i]);
            }

            DrawDropZone(null, rootEntities.Count);

            if (ImGui.BeginDragDropTarget())
            {
                var entityPayload = ImGui.AcceptDragDropPayload("ENTITY");
                unsafe
                {
                    if (entityPayload.NativePtr != null && _dragged.Count > 0)
                    {
                        var done = new List<ICommand>();
                        foreach (var d in _dragged)
                        {
                            if (d.Parent == null) continue;
                            var cmd = new SetParentCommand(d, null);
                            cmd.Execute();
                            done.Add(cmd);
                        }
                        CommitDrop(done, $"Unparent {done.Count} entities");
                        _dragged.Clear();
                    }
                }

                var scenePayload = ImGui.AcceptDragDropPayload("SCENE_FILE");
                unsafe
                {
                    if (scenePayload.NativePtr != null && PendingSceneId != null)
                    {
                        var cmd = new InstantiateSceneCommand(_state.CurrentScene!, PendingSceneId);
                        _state.ExecuteCommand(cmd);
                        if (cmd.CreatedEntity != null)
                            _state.Select(cmd.CreatedEntity);
                        PendingSceneId = null;
                    }
                }
                ImGui.EndDragDropTarget();
            }

            if (ImGui.BeginPopupContextWindow("HierarchyContext",
                ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
            {
                if (ImGui.MenuItem(Icons.Cube + "  Create empty entity"))
                {
                    var cmd = new CreateEntityCommand(_state.CurrentScene, "New Entity");
                    _state.ExecuteCommand(cmd);
                    _state.Select(cmd.CreatedEntity);
                    if (cmd.CreatedEntity != null) StartRename(cmd.CreatedEntity);
                }
                if (ImGui.MenuItem(Icons.Lightbulb + "  Create light"))
                {
                    var cmd = new CreateEntityCommand(_state.CurrentScene, "Light");
                    _state.ExecuteCommand(cmd);
                    cmd.CreatedEntity?.AddComponent<Light2D>();
                    _state.Select(cmd.CreatedEntity);
                    if (cmd.CreatedEntity != null) StartRename(cmd.CreatedEntity);
                }
                ImGui.Separator();
                bool hasPlayer = _state.CurrentScene?.FindPlayer() != null;
                if (hasPlayer) ImGui.BeginDisabled();
                if (ImGui.MenuItem(Icons.Play + "  Create player"))
                {
                    var cmd = new CreateEntityCommand(
                        _state.CurrentScene, Gameplay.Systems.LevelSetup.PlayerRigName);
                    _state.ExecuteCommand(cmd);
                    if (cmd.CreatedEntity is { } rig)
                    {
                        var inst = rig.AddComponent<SceneInstance>();
                        inst.SceneId = Gameplay.Systems.LevelSetup.PlayerPrefabId;
                        inst.Load();
                        _state.Select(rig);
                    }
                }
                if (hasPlayer) ImGui.EndDisabled();
                if (hasPlayer && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip("There is already a Player - one per scene");

                ImGui.Separator();
                if (!EntityClipboard.HasValue) ImGui.BeginDisabled();
                if (ImGui.MenuItem("Paste", "Ctrl+V"))
                    PasteEntities(null);
                if (!EntityClipboard.HasValue) ImGui.EndDisabled();
                ImGui.EndPopup();
            }

            NavigateWithArrows(listFocused);

            ImGui.EndChild();
            ImGui.PopStyleVar();
        }

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            _dragged.Clear();
            _dragStarted = false;
            _pendingCollapse = null;
        }

        DrawSaveSceneDialog();
    }

    private void DrawSaveSceneDialog()
    {
        if (!_showSaveSceneDialog || _entityToSave == null)
            return;

        ImGui.OpenPopup("Save as prefab");

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new System.Numerics.Vector2(0.5f, 0.5f));

        if (ImGui.BeginPopupModal("Save as prefab", ref _showSaveSceneDialog, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Prefab name:");
            ImGui.SetNextItemWidth(220);
            ImGui.InputText("##prefabname", ref _saveSceneName, 64);
            ImGui.TextDisabled("Content/Prefabs/" + _saveSceneName + ".scene");
            ImGui.TextDisabled("The selected entity and its children are replaced with an instance of this prefab.");

            ImGui.Spacing();

            if (ImGui.Button("Save", new System.Numerics.Vector2(80, 0)))
            {
                if (!string.IsNullOrWhiteSpace(_saveSceneName))
                {
                    var prefabsDir = Path.Combine(EditorApp.ContentRoot, "Prefabs");
                    if (!Directory.Exists(prefabsDir))
                        Directory.CreateDirectory(prefabsDir);

                    var filePath = Path.Combine(prefabsDir, _saveSceneName + ".scene");
                    _state.ExecuteCommand(new ConvertToPrefabCommand(
                        _state.CurrentScene!, _entityToSave, filePath, _state));

                    _showSaveSceneDialog = false;
                    _entityToSave = null;
                }
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new System.Numerics.Vector2(80, 0)))
            {
                _showSaveSceneDialog = false;
                _entityToSave = null;
            }

            ImGui.EndPopup();
        }
    }

    private void DrawDropZone(Entity? parent, int index)
    {
        float y = ImGui.GetCursorPosY();
        float advance = 2f + ImGui.GetStyle().ItemSpacing.Y;

        if (_dragged.Count == 0)
        {
            ImGui.Dummy(new System.Numerics.Vector2(0, 2f));
            return;
        }

        ImGui.PushID($"dropzone_{parent?.GetHashCode() ?? 0}_{index}");

        ImGui.SetCursorPosY(y - 3f);
        ImGui.InvisibleButton("##dropzone", new System.Numerics.Vector2(-1, 8f));

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
        {
            var drawList = ImGui.GetWindowDrawList();
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            float lineY = min.Y + 4f;
            drawList.AddLine(
                new System.Numerics.Vector2(min.X, lineY),
                new System.Numerics.Vector2(max.X, lineY),
                ImGui.GetColorU32(EditorTheme.Accent),
                2f
            );
        }

        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("ENTITY");
            unsafe
            {
                if (payload.NativePtr != null && _dragged.Count > 0 && _state.CurrentScene != null)
                    DropReorder(parent, index);
            }
            ImGui.EndDragDropTarget();
        }

        ImGui.PopID();
        ImGui.SetCursorPosY(y + advance);
    }

    private void DropReorder(Entity? parent, int index)
    {
        if (parent != null && _dragged.Contains(parent)) { _dragged.Clear(); return; }

        var scene = _state.CurrentScene!;
        var done = new List<ICommand>();
        Entity? prev = null;

        foreach (var d in _dragged)
        {
            if (parent != null && IsAncestorOf(d, parent)) continue;

            int at = prev == null ? index : scene.GetSiblingIndex(prev) + 1;
            var cmd = new ReorderEntityCommand(scene, d, parent, at);
            cmd.Execute();
            done.Add(cmd);
            prev = d;
        }

        CommitDrop(done, parent == null ? $"Move {done.Count} entities to root"
                                        : $"Move {done.Count} entities into '{parent.Name}'");
        _dragged.Clear();
    }

    private void CommitDrop(List<ICommand> done, string description)
    {
        if (done.Count == 0) return;
        _state.AddExecutedCommand(done.Count == 1
            ? done[0]
            : new CompositeCommand(description, done.ToArray()));
    }

    private void BuildDragSet(Entity grabbed)
    {
        _dragged.Clear();

        if (!_state.IsSelected(grabbed) || _state.Selection.Count <= 1)
        {
            _dragged.Add(grabbed);
            return;
        }

        var sel = new HashSet<Entity>(_state.Selection);
        bool HasSelectedAncestor(Entity e)
        {
            for (var p = e.Parent; p != null; p = p.Parent)
                if (sel.Contains(p)) return true;
            return false;
        }

        var order = _prevRowOrder.Count > 0 ? _prevRowOrder : _rowOrder;
        foreach (var e in order)
            if (sel.Contains(e) && !HasSelectedAncestor(e))
                _dragged.Add(e);

        foreach (var e in _state.Selection)
            if (!_dragged.Contains(e) && !order.Contains(e) && !HasSelectedAncestor(e))
                _dragged.Add(e);

        if (_dragged.Count == 0) _dragged.Add(grabbed);
    }

    private void NavigateWithArrows(bool listFocused)
    {
        if (!listFocused || _renamingEntity != null || ImGui.GetIO().WantTextInput) return;
        if (_rowOrder.Count == 0) return;

        int dir = ImGui.IsKeyPressed(ImGuiKey.DownArrow, true) ? 1
                : ImGui.IsKeyPressed(ImGuiKey.UpArrow, true) ? -1 : 0;
        if (dir == 0) return;

        int idx = _state.SelectedEntity == null ? -1 : _rowOrder.IndexOf(_state.SelectedEntity);
        int next = idx < 0
            ? (dir > 0 ? 0 : _rowOrder.Count - 1)
            : System.Math.Clamp(idx + dir, 0, _rowOrder.Count - 1);
        if (next == idx) return;

        var target = _rowOrder[next];
        _selfSelecting = true;
        try
        {
            if (ImGui.GetIO().KeyShift && _selectionAnchor != null)
                SelectRangeTo(target);
            else
            {
                _state.Select(target);
                _selectionAnchor = target;
            }
        }
        finally { _selfSelecting = false; }

        _pingTarget = target;
    }

    private void SelectRangeTo(Entity clicked)
    {
        var order = _prevRowOrder.Count > 0 ? _prevRowOrder : _rowOrder;
        int a = order.IndexOf(_selectionAnchor!);
        int b = order.IndexOf(clicked);
        if (a < 0 || b < 0) { _state.Select(clicked); _selectionAnchor = clicked; return; }
        if (a > b) (a, b) = (b, a);
        var range = new List<Entity>();
        for (int i = a; i <= b; i++)
            if (order[i] != clicked) range.Add(order[i]);
        range.Add(clicked);
        _state.SelectMany(range, additive: false);
    }

    private void DrawEntityNode(Entity entity)
    {
        _rowOrder.Add(entity);
        bool isSelected = _state.IsSelected(entity);
        bool hasChildren = entity.Children.Count > 0;
        bool isSceneInstance = entity.HasComponent<SceneInstance>();

        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (isSelected) flags |= ImGuiTreeNodeFlags.Selected;
        if (!hasChildren) flags |= ImGuiTreeNodeFlags.Leaf;

        ImGui.PushID(entity.GetHashCode());

        if (_renamingEntity == entity)
        {
            ImGui.SetNextItemWidth(-1);

            if (_focusRename)
            {
                ImGui.SetKeyboardFocusHere();
                _focusRename = false;
            }

            var inputFlags = ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll;
            bool entered = ImGui.InputText("##rename", ref _renameBuffer, 64, inputFlags);
            bool cancelled = ImGui.IsKeyPressed(ImGuiKey.Escape);

            if (entered || ImGui.IsItemDeactivated())
            {
                if (!cancelled && !string.IsNullOrWhiteSpace(_renameBuffer) && _renameBuffer != entity.Name)
                {
                    _state.ExecuteCommand(new RenameEntityCommand(entity, _renameBuffer));
                }
                _renamingEntity = null;
            }
            else if (cancelled)
            {
                _renamingEntity = null;
            }

            ImGui.PopID();
            return;
        }

        string displayName = GetEntityIcon(entity) + "  " + entity.Name;
        bool pushedColor = true;
        if (!entity.ActiveInHierarchy)
            ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.45f, 0.45f, 0.48f, 1f));
        else if (isSceneInstance)
            ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Prefab);
        else if (entity.HideFromSerialization)
        {
            var c = EditorTheme.Prefab;
            ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(c.X, c.Y, c.Z, 0.72f));
        }
        else
            pushedColor = false;

        {
            var rowPos = ImGui.GetCursorScreenPos();
            float textH = ImGui.GetTextLineHeight();
            var rowMin = new System.Numerics.Vector2(rowPos.X, rowPos.Y - 2f);
            var rowMax = new System.Numerics.Vector2(
                rowPos.X + ImGui.GetContentRegionAvail().X, rowPos.Y + textH + 2f);
            bool rowHovered = !isSelected && ImGui.IsWindowHovered()
                && ImGui.IsMouseHoveringRect(rowMin, rowMax);
            if (isSelected)
                ImGui.GetWindowDrawList().AddRectFilled(rowMin, rowMax,
                    ImGui.GetColorU32(EditorTheme.AccentSoft), 6f);
            else if (rowHovered)
                ImGui.GetWindowDrawList().AddRectFilled(rowMin, rowMax,
                    ImGui.GetColorU32(EditorTheme.ItemBg), 6f);
        }
        var clearHdr = new System.Numerics.Vector4(0, 0, 0, 0);
        ImGui.PushStyleColor(ImGuiCol.Header, clearHdr);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, clearHdr);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, clearHdr);
        if (FrameProf.ExpandAll) ImGui.SetNextItemOpen(true, ImGuiCond.Always);
        else if (_pingAncestors.Contains(entity)) ImGui.SetNextItemOpen(true, ImGuiCond.Always);
        FrameProf.Count("#hierRows", 1);
        bool nodeOpen = ImGui.TreeNodeEx(displayName, flags);

        if (_pingTarget == entity)
        {
            const float margin = 8f;
            float itemTop = ImGui.GetItemRectMin().Y, itemBot = ImGui.GetItemRectMax().Y;
            float viewTop = ImGui.GetWindowPos().Y, viewBot = viewTop + ImGui.GetWindowSize().Y;

            float delta = 0f;
            if (itemTop < viewTop + margin) delta = itemTop - (viewTop + margin);
            else if (itemBot > viewBot - margin) delta = itemBot - (viewBot - margin);
            if (delta != 0f) ImGui.SetScrollY(ImGui.GetScrollY() + delta);

            _pingTarget = null;
        }

        ImGui.PopStyleColor(3);

        if (pushedColor) ImGui.PopStyleColor();

        string kindLabel = EntityKinds.Label(entity);
        if (kindLabel.Length > 0)
        {
            ImGui.PushFont(ImGuiRenderer.MonoFont);
            var bts = ImGui.CalcTextSize(kindLabel);
            ImGui.GetWindowDrawList().AddText(
                new System.Numerics.Vector2(
                    ImGui.GetItemRectMax().X - bts.X - 6,
                    ImGui.GetItemRectMin().Y + (ImGui.GetItemRectSize().Y - bts.Y) * 0.5f),
                ImGui.GetColorU32(ImGuiCol.TextDisabled), kindLabel);
            ImGui.PopFont();
        }

        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
        {
            var io = ImGui.GetIO();
            _selfSelecting = true;
            try
            {
                if (io.KeyShift && _selectionAnchor != null)
                    SelectRangeTo(entity);
                else if (io.KeySuper || io.KeyCtrl)
                {
                    _state.ToggleSelection(entity);
                    _selectionAnchor = entity;
                }
                else if (_state.IsSelected(entity) && _state.Selection.Count > 1)
                {
                    _pendingCollapse = entity;
                }
                else
                {
                    _state.Select(entity);
                    _selectionAnchor = entity;
                }
            }
            finally { _selfSelecting = false; }
        }

        if (_pendingCollapse == entity && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            if (!_dragStarted && ImGui.IsItemHovered())
            {
                _selfSelecting = true;
                try { _state.Select(entity); _selectionAnchor = entity; }
                finally { _selfSelecting = false; }
            }
            _pendingCollapse = null;
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            _state.FocusRequest = entity;
        }

        if (ImGui.BeginDragDropSource())
        {
            if (!_dragStarted)
            {
                BuildDragSet(entity);
                _dragStarted = true;
            }
            ImGui.SetDragDropPayload("ENTITY", IntPtr.Zero, 0);
            ImGui.Text(_dragged.Count > 1 ? $"{Icons.Cubes}  {_dragged.Count} items" : entity.Name);
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("ENTITY");
            unsafe
            {
                if (payload.NativePtr != null && _dragged.Count > 0)
                {
                    if (_dragged.Contains(entity))
                    {
                        _dragged.Clear();
                    }
                    else
                    {
                        var done = new List<ICommand>();
                        foreach (var d in _dragged)
                        {
                            if (IsAncestorOf(d, entity)) continue;
                            if (d.Parent == entity) continue;
                            var cmd = new SetParentCommand(d, entity);
                            cmd.Execute();
                            done.Add(cmd);
                        }
                        CommitDrop(done, $"Reparent {done.Count} entities to '{entity.Name}'");
                        _dragged.Clear();
                    }
                }
            }
            ImGui.EndDragDropTarget();
        }

        if (ImGui.BeginPopupContextItem())
        {
            bool multiTarget = _state.IsSelected(entity) && _state.Selection.Count > 1;
            string suffix = multiTarget ? $" ({_state.Selection.Count})" : "";

            if (ImGui.MenuItem("Rename", "F2"))
            {
                StartRename(entity);
            }
            if (ImGui.MenuItem("Copy" + suffix, "Ctrl+C"))
            {
                CopyEntities(Targets(entity, multiTarget));
            }
            if (!EntityClipboard.HasValue) ImGui.BeginDisabled();
            if (ImGui.MenuItem("Paste", "Ctrl+V"))
            {
                PasteEntities(entity.Parent);
            }
            if (ImGui.MenuItem("Paste as child"))
            {
                PasteEntities(entity);
            }
            if (!EntityClipboard.HasValue) ImGui.EndDisabled();
            if (ImGui.MenuItem("Duplicate" + suffix, "Ctrl+D"))
            {
                DuplicateEntities(Targets(entity, multiTarget));
            }
            ImGui.Separator();
            if (ImGui.MenuItem("Create child"))
            {
                var cmd = new CreateEntityCommand(_state.CurrentScene!, "Child", entity);
                _state.ExecuteCommand(cmd);
                _state.Select(cmd.CreatedEntity);
                if (cmd.CreatedEntity != null) StartRename(cmd.CreatedEntity);
            }
            if (entity.Parent != null && ImGui.MenuItem("Clear parent"))
            {
                _state.ExecuteCommand(new SetParentCommand(entity, null));
            }
            if (!entity.HideFromSerialization)
            {
                ImGui.Separator();
                if (ImGui.MenuItem(Icons.Cubes + "  Save as prefab..."))
                {
                    _entityToSave = entity;
                    _saveSceneName = entity.Name;
                    _showSaveSceneDialog = true;
                }
            }
            if (isSceneInstance && ImGui.MenuItem(Icons.Cubes + "  Unpack prefab (break the link)"))
            {
                _state.ExecuteCommand(new UnpackSceneInstanceCommand(entity, _state));
            }
            ImGui.Separator();
            if (ImGui.MenuItem("Delete" + suffix, "Del"))
            {
                var targets = EntitySnapshot.TopLevel(Targets(entity, multiTarget));
                if (targets.Count == 1)
                {
                    _state.ExecuteCommand(new DeleteEntityCommand(_state.CurrentScene!, targets[0], _state));
                }
                else if (targets.Count > 1)
                {
                    var cmds = new List<Commands.ICommand>();
                    foreach (var sel in targets)
                        cmds.Add(new DeleteEntityCommand(_state.CurrentScene!, sel, _state));
                    _state.ExecuteCommand(new CompositeCommand($"Delete {cmds.Count} entities", cmds.ToArray()));
                }
            }
            ImGui.EndPopup();
        }

        if (nodeOpen)
        {
            var children = new List<Entity>(entity.Children);
            for (int i = 0; i < children.Count; i++)
            {
                DrawDropZone(entity, i);
                DrawEntityNode(children[i]);
            }
            DrawDropZone(entity, children.Count);

            ImGui.TreePop();
        }

        ImGui.PopID();
    }

    public void StartRename(Entity entity)
    {
        _renamingEntity = entity;
        _renameBuffer = entity.Name;
        _focusRename = true;
    }

    private static string GetEntityIcon(Entity e)
    {
        if (e.HasComponent<SceneInstance>()) return Icons.Cubes;
        if (e.HasComponent<Light2D>()) return Icons.Lightbulb;
        if (e.HasComponent<TilemapRenderer>()) return Icons.Map;
        if (e.HasComponent<Animator>() || e.HasComponent<SpriteRenderer>()) return Icons.Image;
        return Icons.Cube;
    }

    private bool IsAncestorOf(Entity ancestor, Entity descendant)
    {
        var current = descendant.Parent;
        while (current != null)
        {
            if (current == ancestor)
                return true;
            current = current.Parent;
        }
        return false;
    }

    private List<Entity> Targets(Entity entity, bool multiTarget) =>
        multiTarget ? new List<Entity>(_state.Selection) : new List<Entity> { entity };

    private void CopyEntities(List<Entity> targets)
    {
        if (targets.Count == 0) return;
        EntityClipboard.Copy(targets);
        OnToast?.Invoke($"{Icons.Copy}  copied {EntityClipboard.Count}");
    }

    private void PasteEntities(Entity? parent)
    {
        if (_state.CurrentScene == null || !EntityClipboard.HasValue) return;
        var created = EntityClipboard.Paste(_state.CurrentScene, parent, _state);
        if (created.Count > 0)
            OnToast?.Invoke($"{Icons.Copy}  pasted {created.Count}");
    }

    private void DuplicateEntities(List<Entity> targets)
    {
        if (_state.CurrentScene == null) return;
        EntitySnapshot.DuplicateMany(_state.CurrentScene, targets, _state);
    }
}

using System;
using System.Collections.Generic;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Components;
using PixelCore.Editor.Commands;
using PixelCore.Runtime.Serialization;

namespace PixelCore.Editor;

public enum EditorMode
{
    Edit,
    Play
}

public class EditorState
{
    public CommandHistory CommandHistory { get; set; } = new();

    public Scene? CurrentScene { get; set; }

    public Blackboard? Blackboard { get; set; }

    private Dictionary<string, object>? _blackboardSnapshot;

    private SceneData? _playEntryStash;

    internal SceneData? TakePlayEntryStash()
    {
        var stash = _playEntryStash;
        _playEntryStash = null;
        return stash;
    }

    internal bool HasPlayEntryStash => _playEntryStash != null;

    public string? CurrentScenePath { get; set; }

    public List<Entity> Selection { get; } = new();

    public Entity? SelectedEntity => Selection.Count > 0 ? Selection[^1] : null;

    public EditorMode Mode { get; set; } = EditorMode.Edit;

    public bool IsEditMode => Mode == EditorMode.Edit;

    public bool IsPlayMode => Mode == EditorMode.Play;

    public bool IsDirty { get; set; }

    public bool ShowGrid { get; set; } = true;

    public int GridSize { get; set; } = 16;

    public bool SnapToGrid { get; set; } = false;

    public bool ShowColliders { get; set; }

    public bool ShowGizmos { get; set; } = true;

    public Collider2D? EditingCollider { get; set; }

    public SpriteRenderer? EditingShadow { get; set; }

    public bool EditingCameraFraming { get; set; }

    public bool ShowDebugOverlay { get; set; }

    public bool PlayEditUnlocked { get; set; }

    public bool PlayCameraDetached { get; set; }

    public bool PlayPaused { get; set; }

    public void TogglePlayEditLock()
    {
        PlayEditUnlocked = !PlayEditUnlocked;
        if (!PlayEditUnlocked) PlayCameraDetached = false;
    }

    public Entity? FocusRequest { get; set; }

    public string? OpenSceneRequest { get; set; }

    private readonly PlayModeSnapshot _playModeSnapshot = new();

    public event Action<Entity?>? OnSelectionChanged;

    public event Action<EditorMode>? OnModeChanged;

    public void Select(Entity? entity)
    {
        if (entity == null)
        {
            if (Selection.Count == 0) return;
            Selection.Clear();
            OnSelectionChanged?.Invoke(null);
            return;
        }
        if (Selection.Count == 1 && Selection[0] == entity) return;
        Selection.Clear();
        Selection.Add(entity);
        OnSelectionChanged?.Invoke(entity);
    }

    public void ToggleSelection(Entity entity)
    {
        if (!Selection.Remove(entity))
            Selection.Add(entity);
        OnSelectionChanged?.Invoke(SelectedEntity);
    }

    public void SelectMany(IEnumerable<Entity> entities, bool additive = false)
    {
        if (!additive) Selection.Clear();
        foreach (var e in entities)
            if (!Selection.Contains(e))
                Selection.Add(e);
        OnSelectionChanged?.Invoke(SelectedEntity);
    }

    public bool IsSelected(Entity entity) => Selection.Contains(entity);

    public void Deselect(Entity entity)
    {
        if (Selection.Remove(entity))
            OnSelectionChanged?.Invoke(SelectedEntity);
    }

    public void ClearSelection()
    {
        Select(null);
    }

    public void SetMode(EditorMode mode)
    {
        if (Mode == mode) return;

        PlayEditUnlocked = false;
        PlayCameraDetached = false;
        PlayPaused = false;
        CurrentScene?.ResetAmbient();
        EditingCameraFraming = false;

        if (mode == EditorMode.Play)
            _playEntryStash = (CurrentScene != null && IsDirty) ? SceneSerializer.ToData(CurrentScene) : null;

        if (mode == EditorMode.Play && CurrentScene != null)
        {
            _playModeSnapshot.Capture(CurrentScene);
            ClearSelection();
        }

        if (mode == EditorMode.Edit && CurrentScene != null)
        {
            LastPlayEditKeptCount = _playModeSnapshot.ForgottenCount;
            _playModeSnapshot.Restore(CurrentScene);
            _playModeSnapshot.Clear();
            ClearSelection();
        }

        if (Blackboard != null)
        {
            if (mode == EditorMode.Play)
                _blackboardSnapshot = new Dictionary<string, object>(Blackboard.All);
            else if (_blackboardSnapshot != null)
            {
                Blackboard.LoadFrom(_blackboardSnapshot);
                _blackboardSnapshot = null;
            }
        }

        Mode = mode;
        OnModeChanged?.Invoke(mode);
    }

    public int LastPlayEditKeptCount { get; private set; }

    public void KeepThroughStop(Entity? entity)
    {
        if (entity == null || Mode != EditorMode.Play || !PlayEditUnlocked) return;
        _playModeSnapshot.Forget(entity);
    }

    public void KeepSelectionThroughStop()
    {
        if (Mode != EditorMode.Play || !PlayEditUnlocked) return;
        foreach (var e in Selection) _playModeSnapshot.Forget(e);
    }

    public void ToggleMode()
    {
        SetMode(Mode == EditorMode.Edit ? EditorMode.Play : EditorMode.Edit);
    }

    public void MarkDirty()
    {
        IsDirty = true;
    }

    public void ClearDirty()
    {
        IsDirty = false;
    }

    public void ExecuteCommand(ICommand command)
    {
        CommandHistory.Execute(command);
        MarkDirty();
    }

    public void AddExecutedCommand(ICommand command)
    {
        CommandHistory.AddExecuted(command);
        MarkDirty();
    }

    public void Undo()
    {
        if (CommandHistory.CanUndo)
        {
            CommandHistory.Undo();
            MarkDirty();
        }
    }

    public void Redo()
    {
        if (CommandHistory.CanRedo)
        {
            CommandHistory.Redo();
            MarkDirty();
        }
    }
}

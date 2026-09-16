using System;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Serialization;
using PixelCore.Editor.Commands;
using PixelCore.Editor.Panels;

namespace PixelCore.Editor;

public class EditorApp : IDisposable
{
    private readonly Game _game;
    private readonly GraphicsDevice _graphicsDevice;
    private ImGuiRenderer _imGuiRenderer = null!;

    public EditorState State { get; } = new();

    public PixelCore.Runtime.Systems.TimeOfDay? Clock { get; set; }

    private HierarchyPanel _hierarchyPanel = null!;

    public HierarchyPanel HierarchyPanelRef => _hierarchyPanel;
    private InspectorPanel _inspectorPanel = null!;
    private ProjectPanel _projectPanel = null!;
    private SceneViewPanel _sceneViewPanel = null!;

    public TilemapEditorPanel TilemapEditor { get; } = new();

    public SpriteEditorPanel SpriteEditor { get; private set; } = null!;

    public AnimationEditorPanel AnimationEditor { get; private set; } = null!;

    private RoomDocument _roomDoc = null!;

    private readonly System.Collections.Generic.List<SceneDoc> _docs = new();
    private SceneDoc _activeDoc = null!;
    private SceneDoc? _focusDoc;
    private SceneDoc? _closeDocTarget;
    private bool _closeDocModalOpen;

    public event Action<Scene>? OnActiveSceneChanged;
    private readonly CommandPalette _palette = new();
    private bool _explorerVisible = true;
    private uint _shellDockId;
    private bool _forceDockNextFrame;

    private bool _openSaveAsPopup;
    private bool _openOpenScenePopup;
    private string _saveAsName = "scene";

    private bool _openRenamePopup;
    private string _renameTargetPath = "";
    private string _renameName = "";

    private bool _openSceneConfirmPopup;
    private string? _pendingOpenScenePath;

    private bool _openNewSceneConfirmPopup;
    private bool _newAfterSave;

    private bool _aboutModalOpen;

    private string _toastMessage = "";
    private float _toastTimer;
    private float _toastDuration = 2.5f;
    private enum ToastKind { Ok, Error }
    private ToastKind _toastKind = ToastKind.Ok;
    private string _lastWindowTitle = "";

    private bool _quitModalOpen;
    private bool _quitAfterSave;

    public static readonly string ContentRoot =
        System.IO.Path.GetFullPath(Runtime.Assets.ContentPaths.Root).Replace('\\', '/').TrimEnd('/');
    private static readonly string ScenesDir = System.IO.Path.Combine(ContentRoot, "Scenes");

    private readonly EditorPrefs _prefs = EditorPrefs.Load();

    internal static string ToContentRelative(string absPath)
    {
        var full = System.IO.Path.GetFullPath(absPath).Replace('\\', '/');
        var root = System.IO.Path.GetFullPath(ContentRoot).Replace('\\', '/').TrimEnd('/');
        if (full == root) return "";
        if (full.StartsWith(root + "/")) return full.Substring(root.Length + 1);
        return System.IO.Path.GetFileName(absPath);
    }

    private KeyboardState _prevKeyboard;
    private KeyboardState _currKeyboard;

    private Camera? _camera;

    private (Microsoft.Xna.Framework.Vector2 Pos, float Zoom)? _pendingCamera;

    private bool _wasActive = true;

    public EditorApp(Game game)
    {
        _game = game;
        _graphicsDevice = game.GraphicsDevice;
    }

    public void Initialize()
    {
        if (Enum.TryParse<ThemeMode>(_prefs.Theme, out var savedTheme))
            EditorTheme.Mode = savedTheme;

        Runtime.Core.CodeHotReload.Applied += () => Toast($"{Icons.Terminal}  Code applied");

        _imGuiRenderer = new ImGuiRenderer(_game);
        _imGuiRenderer.RebuildFontAtlas();

        ThumbnailCache.Init(_graphicsDevice, _imGuiRenderer.BindTexture, _imGuiRenderer.UnbindTexture);

        State.OnModeChanged += mode =>
        {
            if (mode == EditorMode.Play)
            {
                WriteBackup("entering play");
                TilemapEditor.IsEditing = false;
            }
            else if (State.LastPlayEditKeptCount > 0)
            {
                Toast($"{State.LastPlayEditKeptCount} play-mode edits kept");
            }
            EditorTheme.SetPlayDim(mode == EditorMode.Play);
        };

        DebugOverlay.LoadFromPrefs(_prefs);
        State.ShowGrid = _prefs.GridVisible;
        State.ShowColliders = _prefs.CollidersVisible;
        State.ShowGizmos = _prefs.GizmosVisible;
        AssetKinds.Monochrome = _prefs.IconMonochrome;
        EditorConsole.Open = _prefs.ConsoleOpen;
        EditorConsole.Height = Math.Clamp(_prefs.ConsoleHeight, 90f, 600f);

        _hierarchyPanel = new HierarchyPanel(State);
        _hierarchyPanel.OnToast += Toast;
        _inspectorPanel = new InspectorPanel(State, _graphicsDevice);
        _projectPanel = new ProjectPanel(State, _graphicsDevice);
        _projectPanel.SeedOpenFolders(_prefs.OpenAssetFolders);
        _projectPanel.OnOpenFoldersChanged += () =>
        {
            _prefs.OpenAssetFolders = _projectPanel.GetOpenFolders();
            _prefs.Save();
        };
        _sceneViewPanel = new SceneViewPanel(State, _graphicsDevice, _imGuiRenderer);
        _sceneViewPanel.OnViewTogglesChanged += () =>
        {
            _prefs.GridVisible = State.ShowGrid;
            _prefs.CollidersVisible = State.ShowColliders;
            _prefs.GizmosVisible = State.ShowGizmos;
            _prefs.AudioMuted = Runtime.Audio.AudioManager.Instance.Muted;
            _prefs.Save();
        };
        SpriteEditor = new SpriteEditorPanel(_graphicsDevice);
        SpriteEditor.SetTextureBinding(_imGuiRenderer.BindTexture);
        SpriteEditor.OnSaved += AnnounceAssetSaved;

        AnimationEditor = new AnimationEditorPanel(_graphicsDevice);
        AnimationEditor.SetTextureBinding(_imGuiRenderer.BindTexture);
        AnimationEditor.OnSaved += AnnounceAssetSaved;

        TilemapEditor.Bind(State, _imGuiRenderer.BindTexture);
        _sceneViewPanel.ToolOverlay = TilemapEditor.DrawOverlay;

        _projectPanel.OnTextureDoubleClicked += path => SpriteEditor.OpenTexture(path);

        _projectPanel.OnSceneOpenRequested += RequestOpenScene;

        _sceneViewPanel.OnTextureDropped += worldPos => PlaceSprite(_projectPanel.DraggedTexturePath, worldPos);

        _projectPanel.OnTexturePlaceRequested += path => PlaceSprite(path, _camera?.Position ?? Vector2.Zero);

        _sceneViewPanel.OnSpriteSliceDropped += worldPos => CreateSpriteFromSlice(worldPos);

        _sceneViewPanel.OnSceneFileDropped += worldPos =>
        {
            if (_projectPanel.DraggedSceneId == null || State.CurrentScene == null) return;
            if (State.SnapToGrid)
            {
                worldPos.X = MathF.Round(worldPos.X / State.GridSize) * State.GridSize;
                worldPos.Y = MathF.Round(worldPos.Y / State.GridSize) * State.GridSize;
            }
            var cmd = new InstantiateSceneCommand(
                State.CurrentScene, _projectPanel.DraggedSceneId, null, worldPos);
            State.ExecuteCommand(cmd);
            if (cmd.CreatedEntity != null) State.Select(cmd.CreatedEntity);
        };

        _sceneViewPanel.OnPostFileDropped += () => ApplyPostAsset(_projectPanel.DraggedPostPath);
        _projectPanel.OnPostApplyRequested += ApplyPostAsset;

        SpriteEditor.OnUseInAnimation += atlas =>
        {
            bool replacing = AnimationEditor.IsOpen;

            if (!replacing) AnimationEditor.New();

            AnimationEditor.SetAtlas(atlas, ToContentRelative(SpriteEditor.CurrentTexturePath),
                                     markEdited: replacing);
        };

        AnimationEditor.DraggedTexturePathProvider = () => _projectPanel.DraggedTexturePath;
        AnimationEditor.OnWarning += ToastError;
        AnimationEditor.OnNotice += Toast;

        _projectPanel.OnAnimationOpenRequested += path => AnimationEditor.RequestOpen(path);

        _projectPanel.OnRenameRequested += path =>
        {
            _renameTargetPath = path;
            _renameName = System.IO.Path.GetFileNameWithoutExtension(path.TrimEnd('/'));
            _openRenamePopup = true;
        };

        _projectPanel.OnImportRequested += destDir =>
            FileDialog.OpenFiles(_game.Window.Handle, destDir, files => ImportFilesTo(destDir, files));

        _projectPanel.OnNewFolderRequested += parentDir =>
        {
            try
            {
                var dest = Panels.ProjectPanel.UniquePath(parentDir, "New Folder", "");
                System.IO.Directory.CreateDirectory(dest);
                _projectPanel.RequestRefresh();
                _renameTargetPath = dest;
                _renameName = System.IO.Path.GetFileName(dest);
                _openRenamePopup = true;
            }
            catch (Exception ex) { ToastError("New folder failed: " + ex.Message); }
        };

        _projectPanel.OnDeleteFolderRequested += DeleteFolderToTrash;
        _projectPanel.OnDeleteRequested += RequestDeleteAssets;

        _projectPanel.OnMoveRequested += MoveAssets;

        _inspectorPanel.SceneViewCenterProvider = () => _camera?.Position ?? Microsoft.Xna.Framework.Vector2.Zero;
        _inspectorPanel.DraggedTexturePathProvider = () => _projectPanel.DraggedTexturePath;
        _inspectorPanel.DraggedPostPathProvider = () => _projectPanel.DraggedPostPath;
        _inspectorPanel.DraggedAudioPathProvider = () => _projectPanel.DraggedAudioPath;
        _inspectorPanel.DraggedAnimPathProvider = () => _projectPanel.DraggedAnimPath;
        _inspectorPanel.OnPostAssetDropped = ApplyPostAsset;

        _inspectorPanel.OnRevealAssetRequested = full => { _explorerVisible = true; _projectPanel.Reveal(full); };
        _inspectorPanel.OnOpenAssetRequested = full =>
        {
            if (full.EndsWith(".scene", System.StringComparison.OrdinalIgnoreCase)) State.OpenSceneRequest = full;
            else _projectPanel.OpenAsset(full);
        };

        _roomDoc = new RoomDocument(State, _hierarchyPanel, _inspectorPanel, _sceneViewPanel, _prefs);

        _palette.SetSource(BuildPaletteItems);

        FileDropWatcher.Install();

        WindowGuard.Install();
    }

    private void DrawDeleteModal()
    {
        if (_pendingDelete == null) return;
        if (!ImGui.IsPopupOpen("Move to trash")) ImGui.OpenPopup("Move to trash");
        if (!ImGui.BeginPopupModal("Move to trash", ImGuiWindowFlags.AlwaysAutoResize)) return;

        ImGui.TextUnformatted(_pendingDelete.Length == 1
            ? System.IO.Path.GetFileName(_pendingDelete[0])
            : $"{_pendingDelete.Length} files");
        foreach (var p in _pendingDelete)
        {
            if (_pendingDelete.Length == 1) break;
            ImGui.TextDisabled("  " + ToContentRelative(p));
        }
        ImGui.TextDisabled("They go to the trash. This cannot be undone inside the editor.");
        ImGui.Dummy(new System.Numerics.Vector2(0, 4));

        if (ImGui.Button(Icons.Trash + "  Move to trash", new System.Numerics.Vector2(140, 0)))
        {
            int ok = DeleteAssetsConfirmed(_pendingDelete);
            if (ok > 0) Toast($"{Icons.Trash}  Moved to trash: {ok}");
            else ToastError("Move to trash failed");
            _projectPanel.ClearSelection();
            _projectPanel.RequestRefresh();
            _pendingDelete = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new System.Numerics.Vector2(90, 0)))
        {
            _pendingDelete = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    private void AnnounceAssetSaved(string fullPath)
    {
        _projectPanel.RequestRefresh();
        Toast($"{Icons.Save}  Saved: {ToContentRelative(fullPath)}");
    }

    private void ImportDroppedFiles()
    {
        bool any = false;
        while (FileDropWatcher.TryDequeue(out var src))
        {
            try
            {
                var ext = System.IO.Path.GetExtension(src).ToLowerInvariant();

                var kind = AssetKinds.Classify(ext);
                string subDir = kind switch
                {
                    AssetKind.Sprite => "Sprites",
                    AssetKind.Audio => "Audio",
                    AssetKind.Font => "Fonts",
                    AssetKind.Scene => "Scenes",
                    AssetKind.Animation => "Animations",
                    _ => ""
                };

                var destDir = System.IO.Path.Combine(ContentRoot, subDir);
                System.IO.Directory.CreateDirectory(destDir);

                var name = System.IO.Path.GetFileNameWithoutExtension(src);
                var dest = System.IO.Path.Combine(destDir, name + ext);
                for (int i = 1; System.IO.File.Exists(dest); i++)
                    dest = System.IO.Path.Combine(destDir, $"{name}_{i}{ext}");

                System.IO.File.Copy(src, dest);
                Toast($"{Icons.Plus}  Imported: {System.IO.Path.GetFileName(dest)}  ->  {subDir}/");
                any = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Import] failed: {ex.Message}");
                ToastError("Import failed: " + System.IO.Path.GetFileName(src));
            }
        }
        if (any) _projectPanel.RequestRefresh();
    }

    private void ImportFilesTo(string destDir, string[] files)
    {
        int ok = 0;
        foreach (var src in files)
        {
            try
            {
                if (!System.IO.File.Exists(src)) continue;
                var dest = Panels.ProjectPanel.UniquePath(destDir,
                    System.IO.Path.GetFileNameWithoutExtension(src),
                    System.IO.Path.GetExtension(src).ToLowerInvariant());
                System.IO.File.Copy(src, dest);
                Runtime.Assets.AssetEvents.RaiseImported(ToContentRelative(dest));
                ok++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Import] failed: {src} - {ex.Message}");
                ToastError("Import failed: " + System.IO.Path.GetFileName(src));
            }
        }
        if (ok > 0)
        {
            var relDir = ToContentRelative(destDir);
            Toast($"{Icons.Plus}  {ok} imported into {(relDir.Length > 0 ? relDir : "Content")}/");
            _projectPanel.RequestRefresh();
        }
    }

    private void RenameFolder(string oldDir, string newName)
    {
        newName = newName.Trim();
        oldDir = oldDir.TrimEnd('/');
        var parent = System.IO.Path.GetDirectoryName(oldDir)!;

        if (newName.Length == 0 ||
            newName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 ||
            newName.Contains('/') || newName.Contains('\\'))
        {
            ToastError("Unusable name: " + newName);
            return;
        }

        var newDir = System.IO.Path.Combine(parent, newName).Replace('\\', '/');
        if (newDir == oldDir) return;
        if (System.IO.Directory.Exists(newDir) || System.IO.File.Exists(newDir))
        {
            ToastError("That name already exists: " + newName);
            return;
        }

        string prefix = oldDir + "/";
        if (SpriteEditor.IsOpen && (SpriteEditor.CurrentTexturePath?.StartsWith(prefix) ?? false))
        {
            ToastError("It contains a file open in the sprite editor - close the tab and try again");
            return;
        }
        if (AnimationEditor.IsOpen && (AnimationEditor.CurrentPath?.StartsWith(prefix) ?? false))
        {
            ToastError("It contains a file open in the animation editor - close the tab and try again");
            return;
        }

        try
        {
            var oldFiles = System.IO.Directory.GetFiles(oldDir, "*", System.IO.SearchOption.AllDirectories);

            System.IO.Directory.Move(oldDir, newDir);

            foreach (var rawOld in oldFiles)
            {
                var oldFull = rawOld.Replace('\\', '/');
                var newFull = newDir + oldFull.Substring(oldDir.Length);
                UpdateRegistryPath(oldFull, newFull);
                Runtime.Assets.AssetEvents.RaiseMoved(ToContentRelative(oldFull), ToContentRelative(newFull));

                var ext = System.IO.Path.GetExtension(oldFull).ToLowerInvariant();
                if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp")
                {
                    RetargetScenePaths(
                        ToContentRelative(System.IO.Path.ChangeExtension(oldFull, ".atlas")),
                        ToContentRelative(System.IO.Path.ChangeExtension(newFull, ".atlas")),
                        ToContentRelative(oldFull), ToContentRelative(newFull));
                }
            }

            if (State.CurrentScenePath != null &&
                State.CurrentScenePath.Replace('\\', '/').StartsWith(prefix))
            {
                State.CurrentScenePath = newDir + State.CurrentScenePath.Replace('\\', '/').Substring(oldDir.Length);
                RememberLastScene(State.CurrentScenePath);
            }
            foreach (var d in _docs)
                if (d != _activeDoc && d.Path != null && d.Path.Replace('\\', '/').StartsWith(prefix))
                    d.Path = newDir + d.Path.Replace('\\', '/').Substring(oldDir.Length);

            SaveRegistryOrWarn();
            _projectPanel.RequestRefresh();
            Toast($"Folder renamed: {System.IO.Path.GetFileName(oldDir)} -> {newName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Rename] folder failed: {oldDir} -> {newName}\n{ex}");
            ToastError("Folder rename failed: " + ex.Message);
            _projectPanel.RequestRefresh();
        }
    }

    private void MoveAssets(string[] sources, string destDir)
    {
        destDir = destDir.Replace('\\', '/').TrimEnd('/');
        int ok = 0;
        foreach (var src in sources)
            if (MoveAssetTo(src.Replace('\\', '/'), destDir)) ok++;

        if (ok > 0)
        {
            SaveRegistryOrWarn();
            var relDir = ToContentRelative(destDir);
            Toast($"{Icons.FolderOpen}  {ok} moved into {(relDir.Length > 0 ? relDir : "Content")}/");
        }
        _projectPanel.RequestRefresh();
    }

    private bool MoveAssetTo(string oldFull, string destDir)
    {
        var fileName = System.IO.Path.GetFileName(oldFull);
        var oldDir = System.IO.Path.GetDirectoryName(oldFull)?.Replace('\\', '/').TrimEnd('/');
        if (oldDir == destDir) return false;

        var newFull = destDir + "/" + fileName;
        if (System.IO.File.Exists(newFull))
        {
            ToastError("Already there: " + fileName);
            return false;
        }

        var ext = System.IO.Path.GetExtension(oldFull).ToLowerInvariant();
        bool isImage = ext is ".png" or ".jpg" or ".jpeg" or ".bmp";
        if (isImage && SpriteEditor.IsOpen && SpriteEditor.CurrentTexturePath == oldFull)
        {
            ToastError("Open in the sprite editor - close the tab and try again: " + fileName);
            return false;
        }
        if (ext == ".anim" && AnimationEditor.IsOpen && AnimationEditor.CurrentPath == oldFull)
        {
            ToastError("Open in the animation editor - close the tab and try again: " + fileName);
            return false;
        }

        try
        {
            System.IO.File.Move(oldFull, newFull);
            UpdateRegistryPath(oldFull, newFull);
            Runtime.Assets.AssetEvents.RaiseMoved(ToContentRelative(oldFull), ToContentRelative(newFull));

            if (isImage)
            {
                var oldAtlas = System.IO.Path.ChangeExtension(oldFull, ".atlas");
                var newAtlas = System.IO.Path.ChangeExtension(newFull, ".atlas");
                if (System.IO.File.Exists(oldAtlas))
                {
                    System.IO.File.Move(oldAtlas, newAtlas);
                    UpdateRegistryPath(oldAtlas, newAtlas);
                }
                RetargetScenePaths(
                    ToContentRelative(oldAtlas), ToContentRelative(newAtlas),
                    ToContentRelative(oldFull), ToContentRelative(newFull));
            }

            if (ext == ".scene") FixupDocPaths(oldFull, newFull);
            if (ext == ".scene" &&
                State.CurrentScenePath != null &&
                System.IO.Path.GetFullPath(State.CurrentScenePath) == System.IO.Path.GetFullPath(oldFull))
            {
                State.CurrentScenePath = newFull;
                RememberLastScene(newFull);
                if (System.IO.File.Exists(oldFull + ".bak"))
                    System.IO.File.Move(oldFull + ".bak", newFull + ".bak");
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Move] failed: {oldFull} -> {destDir}\n{ex}");
            ToastError("Move failed: " + fileName);
            return false;
        }
    }

    private string[]? _pendingDelete;

    private void RequestDeleteAssets(string[] paths)
    {
        if (paths.Length == 0) return;

        var held = new System.Collections.Generic.List<(string Path, string What)>();
        foreach (var d in _docs)
        {
            var dp = d == _activeDoc ? State.CurrentScenePath : d.Path;
            if (!string.IsNullOrEmpty(dp)) held.Add((dp!, "open scene"));
        }
        if (SpriteEditor.IsOpen && !string.IsNullOrEmpty(SpriteEditor.CurrentTexturePath))
            held.Add((SpriteEditor.CurrentTexturePath!, "open in the sprite editor"));
        if (AnimationEditor.IsOpen && !string.IsNullOrEmpty(AnimationEditor.CurrentPath))
            held.Add((AnimationEditor.CurrentPath!, "open in the animation editor"));

        foreach (var p in paths)
            if (OpenDocReason(p, held) is { } why)
            {
                ToastError($"{why} - close the tab and try again: {System.IO.Path.GetFileName(p)}");
                return;
            }

        _pendingDelete = paths;
    }

    internal static string? OpenDocReason(string path, System.Collections.Generic.IReadOnlyList<(string Path, string What)> held)
    {
        var norm = path.Replace('\\', '/');
        foreach (var h in held)
            if (h.Path.Replace('\\', '/') == norm) return h.What;
        return null;
    }

    internal static int DeleteAssetsConfirmed(string[] paths)
    {
        int ok = 0;
        foreach (var path in paths)
        {
            if (!Panels.ProjectPanel.MoveToTrash(path)) continue;
            ok++;

            var sidecar = System.IO.Path.ChangeExtension(path, ".atlas");
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" && System.IO.File.Exists(sidecar))
                Panels.ProjectPanel.MoveToTrash(sidecar);

            Runtime.Assets.AssetEvents.RaiseDeleted(ToContentRelative(path));
        }
        return ok;
    }

    private void DeleteFolderToTrash(string dir)
    {
        dir = dir.TrimEnd('/');
        string prefix = dir + "/";

        if (_docs.Exists(d => (d == _activeDoc ? State.CurrentScenePath : d.Path) is { } dp
                && dp.Replace('\\', '/').StartsWith(prefix)))
        {
            ToastError("It contains an open scene - close that tab and try again");
            return;
        }
        if (SpriteEditor.IsOpen && (SpriteEditor.CurrentTexturePath?.StartsWith(prefix) ?? false))
        {
            ToastError("It contains a file open in the sprite editor - close the tab and try again");
            return;
        }
        if (AnimationEditor.IsOpen && (AnimationEditor.CurrentPath?.StartsWith(prefix) ?? false))
        {
            ToastError("It contains a file open in the animation editor - close the tab and try again");
            return;
        }

        if (Panels.ProjectPanel.MoveToTrash(dir))
        {
            Runtime.Assets.AssetEvents.RaiseDeleted(ToContentRelative(dir));
            Toast($"{Icons.Trash}  Moved to trash: {System.IO.Path.GetFileName(dir)}/");
        }
        else
            ToastError("Move to trash failed: " + System.IO.Path.GetFileName(dir));
        _projectPanel.RequestRefresh();
    }

    private void ApplyPostAsset(string? postPath)
    {
        var scene = State.CurrentScene;
        if (string.IsNullOrEmpty(postPath) || scene == null) return;

        var asset = PixelCore.Runtime.Assets.PostAsset.Load(postPath);
        if (asset == null || string.IsNullOrEmpty(asset.Id))
        {
            EditorConsole.Add(LogSeverity.Error,
                $"[Post] the profile could not be read: {System.IO.Path.GetFileName(postPath)}");
            return;
        }
        PixelCore.Runtime.Assets.PostAssetCache.Put(asset);

        var key = string.IsNullOrEmpty(asset.Name)
            ? System.IO.Path.GetFileNameWithoutExtension(postPath)
            : asset.Name;

        var existing = scene.PostPresets.Find(p => p.AssetId == asset.Id);
        if (existing == null)
        {
            var baseKey = key;
            for (int n = 1; scene.PostPresets.Exists(p => p.Key == key); n++) key = $"{baseKey}_{n}";

            existing = new PixelCore.Runtime.Rendering.PostPreset
            {
                Key = key,
                AssetId = asset.Id,
                Profile = asset.Profile.ToProfile(),
            };
            scene.PostPresets.Add(existing);
        }

        scene.ApplyPostPreset(existing.Key);
        State.MarkDirty();
        EditorConsole.Add(LogSeverity.Info, $"[Post] '{existing.Key}' applied - {scene.Name}");
    }

    private void PlaceSprite(string? texturePath, Vector2 worldPos)
    {
        if (string.IsNullOrEmpty(texturePath) || State.CurrentScene == null) return;

        try
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(texturePath);
            var entity = State.CurrentScene.CreateEntity(name);
            var transform = entity.GetComponent<Transform>()!;
            transform.Position = worldPos;

            var sprite = entity.AddComponent<SpriteRenderer>();
            sprite.Color = Color.White;

            using var stream = System.IO.File.OpenRead(texturePath);
            var texture = Texture2D.FromStream(_graphicsDevice, stream);
            sprite.Texture = texture;

            var atlasFull = System.IO.Path.ChangeExtension(texturePath, ".atlas");
            var atlas = System.IO.File.Exists(atlasFull) ? SpriteAtlas.Load(atlasFull) : null;

            if (atlas != null && atlas.Slices.Count > 0)
            {
                var slice = atlas.Slices[0];
                sprite.SourceRect = new Rectangle(slice.X, slice.Y, slice.Width, slice.Height);
                sprite.PivotX = slice.PivotX; sprite.PivotY = slice.PivotY;
                sprite.AtlasPath = ToContentRelative(atlasFull);
                sprite.SliceName = slice.Name;
                AssetRegistry.Instance.GetOrCreateId(sprite.AtlasPath);
            }
            else
            {
                sprite.TexturePath = ToContentRelative(texturePath);
                AssetRegistry.Instance.GetOrCreateId(sprite.TexturePath);
            }

            transform.Position = worldPos + sprite.GetDrawSize() *
                new Vector2(sprite.PivotX - 0.5f, sprite.PivotY - 0.5f);

            State.Select(entity);
            State.CommandHistory.AddExecuted(new SpawnEntityCommand(State.CurrentScene, entity, "Add Sprite", State));
            State.MarkDirty();
            Toast(Icons.Image + "  Added: " + name);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create sprite: {ex.Message}");
            ToastError("Placement failed: " + System.IO.Path.GetFileName(texturePath));
        }
    }

    private void CreateSpriteFromSlice(Vector2 worldPos)
    {
        var atlas = SpriteEditor.CurrentAtlas;
        var sliceIndex = SpriteEditor.DraggedSliceIndex;
        var texturePath = SpriteEditor.CurrentTexturePath;

        if (atlas?.Texture == null || sliceIndex < 0 || State.CurrentScene == null) return;
        if (sliceIndex >= atlas.Slices.Count) return;

        try
        {
            var slice = atlas.Slices[sliceIndex];

            var entity = State.CurrentScene.CreateEntity(slice.Name);

            var transform = entity.GetComponent<Transform>()!;
            transform.Position = worldPos;

            var sprite = entity.AddComponent<SpriteRenderer>();
            sprite.Texture = atlas.Texture;
            sprite.Color = Color.White;
            sprite.SourceRect = new Rectangle(slice.X, slice.Y, slice.Width, slice.Height);
            sprite.PivotX = slice.PivotX; sprite.PivotY = slice.PivotY;
            sprite.AtlasPath = ToContentRelative(System.IO.Path.ChangeExtension(texturePath, ".atlas"));
            sprite.SliceName = slice.Name;
            sprite.SliceIndex = sliceIndex;
            AssetRegistry.Instance.GetOrCreateId(sprite.AtlasPath);

            transform.Position = worldPos + sprite.GetDrawSize() *
                new Vector2(sprite.PivotX - 0.5f, sprite.PivotY - 0.5f);

            State.Select(entity);
            State.CommandHistory.AddExecuted(new SpawnEntityCommand(State.CurrentScene, entity, "Add Sprite", State));
            State.MarkDirty();
            Toast(Icons.Image + "  Added: " + slice.Name);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create sprite from slice: {ex.Message}");
            ToastError("Slice placement failed: " + System.IO.Path.GetFileName(texturePath));
        }
    }

    public void SetScene(Scene scene)
    {
        State.CurrentScene = scene;
        var doc = new SceneDoc(scene, null) { History = State.CommandHistory };
        _docs.Add(doc);
        _activeDoc = doc;
    }

    private void ActivateDoc(SceneDoc doc)
    {
        if (doc == _activeDoc) return;
        if (State.IsPlayMode) State.SetMode(EditorMode.Edit);

        var cur = _activeDoc;
        cur.Dirty = State.IsDirty;
        cur.Path = State.CurrentScenePath;
        cur.Selection.Clear();
        cur.Selection.AddRange(State.Selection);
        if (_camera != null) { cur.CameraPos = _camera.Position; cur.CameraZoom = _camera.Zoom; }
        RememberSceneView(cur.Path);

        _activeDoc = doc;
        State.ClearSelection();
        State.CurrentScene = doc.Scene;
        State.CurrentScenePath = doc.Path;
        State.IsDirty = doc.Dirty;
        State.CommandHistory = doc.History;
        State.Selection.AddRange(doc.Selection);
        State.EditingCollider = null;
        State.EditingShadow = null;
        if (_camera != null) { _camera.Position = doc.CameraPos; _camera.Zoom = doc.CameraZoom; }

        foreach (var e in doc.Scene.Entities)
            if (e.GetComponent<TilemapRenderer>() is { } tm) { TilemapEditor.SetTarget(tm); break; }

        if (doc.Path != null) RememberLastScene(doc.Path);
        OnActiveSceneChanged?.Invoke(doc.Scene);
    }

    private void RequestCloseDoc(SceneDoc doc)
    {
        bool dirty = doc == _activeDoc ? State.IsDirty : doc.Dirty;
        if (dirty) { _closeDocTarget = doc; _closeDocModalOpen = true; }
        else CloseDoc(doc);
    }

    private void CloseDoc(SceneDoc doc)
    {
        if (doc == _activeDoc) RememberSceneView(doc.Path);
        _docs.Remove(doc);
        if (_docs.Count == 0)
            _docs.Add(new SceneDoc(new Scene("Untitled"), null));
        if (doc == _activeDoc)
            ActivateDoc(_docs[^1]);
        doc.Scene.Clear();
    }

    private void DrawCloseDocModal()
    {
        if (_closeDocModalOpen) { ImGui.OpenPopup("Close document"); _closeDocModalOpen = false; }
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new System.Numerics.Vector2(0.5f, 0.5f));
        if (!ImGui.BeginPopupModal("Close document", ImGuiWindowFlags.AlwaysAutoResize)) return;

        var doc = _closeDocTarget;
        if (doc == null || !_docs.Contains(doc)) { ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }

        ImGui.Text($"'{doc.Scene.Name}' has unsaved changes.");
        ImGui.Dummy(new System.Numerics.Vector2(0, 4));

        bool hasPath = (doc == _activeDoc ? State.CurrentScenePath : doc.Path) != null;
        if (hasPath && ImGui.Button(Icons.Save + "  Save and close", new System.Numerics.Vector2(120, 0)))
        {
            var p = doc == _activeDoc ? State.CurrentScenePath! : doc.Path!;
            try
            {
                SceneSerializer.SaveToFile(SceneSerializer.ToData(doc.Scene), p);
                TryDeleteBackup(p);
                if (doc == _activeDoc) State.ClearDirty(); else doc.Dirty = false;
                CloseDoc(doc);
            }
            catch (Exception ex) { ToastError("Save failed: " + ex.Message); }
            _closeDocTarget = null;
            ImGui.CloseCurrentPopup();
        }
        if (hasPath) ImGui.SameLine();
        if (ImGui.Button("Do not save", new System.Numerics.Vector2(100, 0)))
        {
            CloseDoc(doc);
            _closeDocTarget = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new System.Numerics.Vector2(80, 0)))
        {
            _closeDocTarget = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    private void SaveAllDirtyDocs()
    {
        foreach (var d in _docs)
        {
            if (d == _activeDoc) continue;
            if (!d.Dirty || d.Path == null) continue;
            try
            {
                SceneSerializer.SaveToFile(SceneSerializer.ToData(d.Scene), d.Path);
                d.Dirty = false;
                TryDeleteBackup(d.Path);
            }
            catch (Exception ex) { Console.WriteLine($"[Scene] Save failed: {d.Path}\n{ex}"); }
        }
    }

    private void FixupDocPaths(string oldFull, string newFull)
    {
        foreach (var d in _docs)
        {
            if (d == _activeDoc || d.Path == null) continue;
            if (System.IO.Path.GetFullPath(d.Path) == System.IO.Path.GetFullPath(oldFull))
            {
                d.Path = newFull;
                d.Scene.Name = System.IO.Path.GetFileNameWithoutExtension(newFull);
            }
        }
    }

    public void SetTilemap(TilemapRenderer tilemap)
    {
        TilemapEditor.SetTarget(tilemap);
    }

    public void SetCamera(Camera camera)
    {
        _sceneViewPanel.SetCamera(camera);
    }

    public void HandleInput(Camera camera)
    {
        _camera = camera;

        if (_pendingCamera is { } pc)
        {
            camera.Position = pc.Pos;
            camera.Zoom = pc.Zoom;
            _pendingCamera = null;
        }

        _prevKeyboard = _currKeyboard;
        _currKeyboard = Keyboard.GetState();

        _sceneViewPanel.SetCamera(camera);

        _sceneViewPanel.HandleInput(TilemapEditor.IsEditing);

        TilemapEditor.HandleInput(camera, this);

        if (State.FocusRequest != null)
        {
            if (!_sceneViewPanel.FrameEntities(new[] { State.FocusRequest }) &&
                State.FocusRequest.GetComponent<Transform>() is { } transform)
            {
                camera.Position = transform.Position;
            }
            State.FocusRequest = null;
        }

        var io = ImGui.GetIO();
        bool typing = io.WantTextInput;
        bool popupOpen = ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel);

        bool ctrl = EditorInput.CmdOrCtrl;
        bool shift = EditorInput.Shift;

        if (!typing && !popupOpen)
        {
            if (ctrl && Input.IsKeyPressed(Input.SDL_SCANCODE_Z) && !shift) State.Undo();
            else if (ctrl && (Input.IsKeyPressed(Input.SDL_SCANCODE_Y) || (Input.IsKeyPressed(Input.SDL_SCANCODE_Z) && shift))) State.Redo();

            if ((Input.IsKeyPressed(Input.SDL_SCANCODE_DELETE) || Input.IsKeyPressed(Input.SDL_SCANCODE_BACKSPACE))
                && State.SelectedEntity != null)
                DeleteSelectedEntity();

            if (ctrl && Input.IsKeyPressed(Input.SDL_SCANCODE_D) && State.SelectedEntity != null)
                DuplicateSelectedEntity();

            if (Input.IsKeyPressed(Input.SDL_SCANCODE_F2) && State.SelectedEntity != null)
                _hierarchyPanel.StartRename(State.SelectedEntity);

            if (!ctrl && Input.IsKeyPressed(Input.SDL_SCANCODE_F))
                _sceneViewPanel.RequestFrame(State.Selection.Count > 0
                    ? SceneViewPanel.FrameTarget.Selection
                    : SceneViewPanel.FrameTarget.All);

            if (ctrl && Input.IsKeyPressed(Input.SDL_SCANCODE_C)) CopySelection();
            if (ctrl && Input.IsKeyPressed(Input.SDL_SCANCODE_V)) PasteClipboard();
        }

        if (ctrl && Input.IsKeyPressed(Input.SDL_SCANCODE_S))
        {
            if (shift) { _saveAsName = State.CurrentScene?.Name ?? "scene"; _openSaveAsPopup = true; }
            else SaveScene();
        }
        if (ctrl && Input.IsKeyPressed(Input.SDL_SCANCODE_O)) _openOpenScenePopup = true;
        if (ctrl && Input.IsKeyPressed(Input.SDL_SCANCODE_N)) RequestNewScene();

        if (ctrl && Input.IsKeyPressed(Input.SDL_SCANCODE_P)) _palette.Open();

        if (ctrl && Input.IsKeyPressed(Input.SDL_SCANCODE_J)) ToggleConsole();

        if (Input.IsKeyPressed(Input.SDL_SCANCODE_F5)) State.ToggleMode();

        if (State.IsPlayMode && Input.IsKeyPressed(Input.SDL_SCANCODE_F8))
            State.TogglePlayEditLock();
    }

    private void CloseAllDocuments()
    {
        foreach (var d in _docs.ToArray())
        {
            bool dirty = d == _activeDoc ? State.IsDirty : d.Dirty;
            if (!dirty) CloseDoc(d);
        }
        SpriteEditor.Hide();
        AnimationEditor.Hide();
    }

    private bool IsKeyPressed(Keys key)
    {
        return _currKeyboard.IsKeyDown(key) && !_prevKeyboard.IsKeyDown(key);
    }

    private void DeleteSelectedEntity()
    {
        if (State.Selection.Count == 0 || State.CurrentScene == null) return;

        var targets = EntitySnapshot.TopLevel(State.Selection.ToArray());

        if (targets.Count == 1)
        {
            State.ExecuteCommand(new DeleteEntityCommand(State.CurrentScene, targets[0], State));
            return;
        }

        var cmds = new System.Collections.Generic.List<ICommand>();
        foreach (var e in targets)
            cmds.Add(new DeleteEntityCommand(State.CurrentScene, e, State));
        State.ExecuteCommand(new CompositeCommand($"Delete {cmds.Count} entities", cmds.ToArray()));
    }

    private void DuplicateSelectedEntity()
    {
        if (State.Selection.Count == 0 || State.CurrentScene == null) return;

        EntitySnapshot.DuplicateMany(State.CurrentScene, State.Selection.ToArray(), State);
    }

    private void CopySelection()
    {
        if (State.Selection.Count == 0) return;
        EntityClipboard.Copy(State.Selection);
        Toast($"{Icons.Copy}  {EntityClipboard.Count} copied");
    }

    private void PasteClipboard()
    {
        if (!EntityClipboard.HasValue || State.CurrentScene == null) return;
        var created = EntityClipboard.Paste(State.CurrentScene, State.SelectedEntity?.Parent, State);
        if (created.Count > 0)
            Toast($"{Icons.Copy}  {created.Count} pasted");
    }

    private const float BackupInterval = 120f;
    private float _backupTimer;
    private bool _recoveryModalOpen;
    private string? _recoveryBakPath;

    public event Action? OnRuntimeSceneLoaded;

    private string BackupPathFor(string? scenePath) =>
        scenePath != null ? scenePath + ".bak"
                          : System.IO.Path.Combine(ScenesDir, "Untitled.scene.bak");

    private void WriteBackup(string reason)
    {
        if (State.CurrentScene == null || !State.IsDirty) return;
        try
        {
            var bak = BackupPathFor(State.CurrentScenePath);
            var data = SceneSerializer.ToData(State.CurrentScene);
            SceneSerializer.SaveToFile(data, bak);
            Console.WriteLine($"[Backup] {reason}: {bak}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Backup] failed ({reason}): {ex.Message}");
            ToastError("Automatic backup failed: " + ex.Message);
        }
    }

    private void TryDeleteBackup(string? scenePath)
    {
        try
        {
            var bak = BackupPathFor(scenePath);
            if (System.IO.File.Exists(bak)) System.IO.File.Delete(bak);
        }
        catch {  }
    }

    private void ApplyRecovery()
    {
        if (_recoveryBakPath == null || State.CurrentScene == null) return;
        SceneData? data = null;
        try { data = SceneSerializer.LoadFromFile(_recoveryBakPath); }
        catch (Exception ex) { Console.WriteLine($"[Backup] recovery load failed: {ex.Message}"); }
        if (data == null)
        {
            ToastError("Recovery failed: the backup file could not be read");
            return;
        }

        PlayRoomRestore.Recharge(State, data, keepDirty: true, () =>
        {
            foreach (var e in State.CurrentScene!.Entities)
            {
                var tm = e.GetComponent<TilemapRenderer>();
                if (tm != null) { TilemapEditor.SetTarget(tm); break; }
            }
            OnRuntimeSceneLoaded?.Invoke();
        });
        Toast(Icons.Save + "  Backup recovered - press Ctrl+S to confirm");
    }

    private void SaveScene()
    {
        if (State.IsPlayMode)
        {
            ToastError("Stop play before saving");
            return;
        }

        if (string.IsNullOrEmpty(State.CurrentScenePath))
        {
            _saveAsName = State.CurrentScene?.Name ?? "scene";
            _openSaveAsPopup = true;
            return;
        }
        DoSave(State.CurrentScenePath);
    }

    private void DoSave(string path)
    {
        if (State.CurrentScene == null) return;

        var affectedInstances = new System.Collections.Generic.List<SceneInstance>();
        if (_docs.Count > 1)
        {
            var savedFull = System.IO.Path.GetFullPath(path);
            foreach (var d in _docs)
            {
                if (d == _activeDoc) continue;
                foreach (var e in d.Scene.Entities)
                    if (e.GetComponent<SceneInstance>() is { IsLoaded: true } si &&
                        si.ScenePath != null &&
                        System.IO.Path.GetFullPath(si.ScenePath) == savedFull)
                    {
                        si.Overrides = InstanceOverrides.Compute(si);
                        affectedInstances.Add(si);
                    }
            }
        }

        try
        {
            var data = SceneSerializer.ToData(State.CurrentScene);
            SceneSerializer.SaveToFile(data, path);
            AssetRegistry.Instance.GetOrCreateId(ToContentRelative(path));
            SaveRegistryOrWarn();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Scene] Save failed: {path}\n{ex}");
            ToastError("Save failed: " + ex.Message);
            _quitAfterSave = false;
            _newAfterSave = false;
            return;
        }
        foreach (var si in affectedInstances) si.Load();

        State.CurrentScenePath = path;
        State.CurrentScene.Name = System.IO.Path.GetFileNameWithoutExtension(path);
        State.ClearDirty();
        RememberSceneView(path);
        RememberLastScene(path);
        TryDeleteBackup(path);
        TryDeleteBackup(null);
        Toast(Icons.Save + "  Saved: " + System.IO.Path.GetFileName(path));
        Console.WriteLine($"[Scene] Saved: {path}");

        WarnBrokenDoors("save");

        if (_quitAfterSave) { _quitAfterSave = false; _game.Exit(); }
        else if (_newAfterSave) { _newAfterSave = false; NewScene(); }
    }

    private bool HasUnsavedWork()
        => _docs.Exists(d => d == _activeDoc ? State.IsDirty : d.Dirty)
        || SpriteEditor.IsDirty
        || AnimationEditor.IsDirty;

    public void RequestQuit()
    {
        if (HasUnsavedWork()) _quitModalOpen = true;
        else _game.Exit();
    }

    private void SaveRegistryOrWarn()
    {
        if (!AssetRegistry.Instance.Save())
            ToastError("Saving the asset registry (assets.json) failed - check the console");
    }

    private void Toast(string message)
    {
        _toastMessage = message;
        _toastKind = ToastKind.Ok;
        _toastDuration = _toastTimer = 2.5f;
    }

    internal void ToastError(string message)
    {
        _toastMessage = Icons.Warning + "  " + message;
        _toastKind = ToastKind.Error;
        _toastDuration = _toastTimer = 4f;
    }

    private void OpenScene(string path)
    {
        if (State.CurrentScene == null) return;

        var openFull = System.IO.Path.GetFullPath(path);
        foreach (var d in _docs)
            if (d.Path != null && System.IO.Path.GetFullPath(d.Path) == openFull)
            {
                ActivateDoc(d);
                _focusDoc = d;
                return;
            }

        SceneData? data = null;
        try { data = SceneSerializer.LoadFromFile(path); }
        catch (Exception ex) { Console.WriteLine($"[Scene] Load failed: {path}\n{ex}"); }
        if (data == null)
        {
            ToastError("Scene open failed: " + System.IO.Path.GetFileName(path));
            Console.WriteLine($"[Scene] Load failed: {path}");
            return;
        }

        bool reuse = State.CurrentScenePath == null && !State.IsDirty;
        var doc = reuse ? _activeDoc : new SceneDoc(new Scene(data.Name), path);
        if (!reuse)
        {
            if (_camera != null) { doc.CameraPos = _camera.Position; doc.CameraZoom = _camera.Zoom; }
            _docs.Add(doc);
        }

        SceneSerializer.FromData(doc.Scene, data);
        doc.Scene.Name = data.Name;
        doc.Path = path;
        doc.Dirty = false;

        bool restored = TryRestoreSceneView(path, doc);

        if (doc != _activeDoc) ActivateDoc(doc);
        else
        {
            State.ClearSelection();
            State.CurrentScenePath = path;
            State.ClearDirty();
            if (restored && _camera != null) { _camera.Position = doc.CameraPos; _camera.Zoom = doc.CameraZoom; }
        }
        if (!restored) _sceneViewPanel.RequestFrame(SceneViewPanel.FrameTarget.All);
        _focusDoc = doc;

        foreach (var e in doc.Scene.Entities)
        {
            var tm = e.GetComponent<TilemapRenderer>();
            if (tm != null) { TilemapEditor.SetTarget(tm); break; }
        }

        RememberLastScene(path);
        OnRuntimeSceneLoaded?.Invoke();
        Toast(Icons.FolderOpen + "  Opened: " + System.IO.Path.GetFileName(path));
        Console.WriteLine($"[Scene] Opened: {path}");

        WarnBrokenDoors("open");
    }

    private void WarnBrokenDoors(string when)
    {
        int problems = Shell.DoorRefs.Validate();
        if (problems > 0)
            ToastError($"{problems} broken door references - see the console ({when})");
    }

    private void RequestOpenScene(string path)
    {
        OpenScene(path);
    }

    private void RememberLastScene(string path)
    {
        _prefs.LastScenePath = path;
        _prefs.Save();
    }

    private static string? ViewKey(string? path)
        => string.IsNullOrEmpty(path) ? null : System.IO.Path.GetFullPath(path);

    private void RememberSceneView(string? path)
    {
        if (_camera == null || ViewKey(path) is not { } key) return;
        _prefs.SceneViews[key] = new SceneViewState
        {
            X = _camera.Position.X,
            Y = _camera.Position.Y,
            Zoom = _camera.Zoom,
        };
    }

    private bool TryRestoreSceneView(string? path, SceneDoc doc)
    {
        if (ViewKey(path) is not { } key) return false;
        if (!_prefs.SceneViews.TryGetValue(key, out var v) || v.Zoom <= 0f) return false;
        doc.CameraPos = new Microsoft.Xna.Framework.Vector2(v.X, v.Y);
        doc.CameraZoom = v.Zoom;
        _pendingCamera = (doc.CameraPos, doc.CameraZoom);
        return true;
    }

    public bool TryLoadLastScene()
    {
        var path = _prefs.LastScenePath;
        bool loaded = false;
        if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
        {
            OpenScene(path);
            loaded = State.CurrentScenePath == path;
        }

        if (loaded)
        {
            var bak = BackupPathFor(path);
            if (System.IO.File.Exists(bak) &&
                System.IO.File.GetLastWriteTimeUtc(bak) > System.IO.File.GetLastWriteTimeUtc(path!))
            {
                _recoveryBakPath = bak;
                _recoveryModalOpen = true;
                Console.WriteLine($"[Backup] recovery candidate found: {bak}");
            }
        }
        else
        {
            var untitledBak = BackupPathFor(null);
            if (System.IO.File.Exists(untitledBak))
            {
                _recoveryBakPath = untitledBak;
                _recoveryModalOpen = true;
            }
        }

        return loaded;
    }

    public void OpenStartScene(string path) => OpenScene(path);

    private void SetTheme(ThemeMode mode)
    {
        EditorTheme.Mode = mode;
        EditorTheme.Apply();
        _prefs.Theme = mode.ToString();
        _prefs.Save();
    }

    private void RequestNewScene()
    {
        NewScene();
    }

    private void RenameAsset(string oldFull, string newName)
    {
        newName = newName.Trim();
        var ext = System.IO.Path.GetExtension(oldFull);
        var dir = System.IO.Path.GetDirectoryName(oldFull) ?? ContentRoot;

        if (newName.Length == 0 ||
            newName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 ||
            newName.Contains('/') || newName.Contains('\\'))
        {
            ToastError("Unusable name: " + newName);
            return;
        }

        var newFull = System.IO.Path.Combine(dir, newName + ext).Replace('\\', '/');
        if (newFull == oldFull) return;
        if (System.IO.File.Exists(newFull))
        {
            ToastError("That name already exists: " + newName + ext);
            return;
        }

        bool isImage = ext is ".png" or ".jpg" or ".jpeg" or ".bmp";
        if (isImage && SpriteEditor.IsOpen && SpriteEditor.CurrentTexturePath == oldFull)
        {
            ToastError("Open in the sprite editor - close the tab and try again");
            return;
        }
        if (ext == ".anim" && AnimationEditor.IsOpen && AnimationEditor.CurrentPath == oldFull)
        {
            ToastError("Open in the animation editor - close the tab and try again");
            return;
        }

        try
        {
            System.IO.File.Move(oldFull, newFull);
            UpdateRegistryPath(oldFull, newFull);

            if (isImage)
            {
                var oldAtlas = System.IO.Path.ChangeExtension(oldFull, ".atlas");
                var newAtlas = System.IO.Path.ChangeExtension(newFull, ".atlas");
                if (System.IO.File.Exists(oldAtlas))
                {
                    System.IO.File.Move(oldAtlas, newAtlas);
                    UpdateRegistryPath(oldAtlas, newAtlas);

                    var atlas = SpriteAtlas.Load(newAtlas);
                    if (atlas != null)
                    {
                        atlas.TexturePath = newName + ext;
                        atlas.Save(newAtlas);
                    }
                }

                RetargetScenePaths(
                    ToContentRelative(oldAtlas), ToContentRelative(newAtlas),
                    ToContentRelative(oldFull), ToContentRelative(newFull));
            }

            if (ext == ".anim")
            {
                Runtime.Assets.AssetEvents.RaiseRenamed(
                    ToContentRelative(oldFull), ToContentRelative(newFull));
            }

            if (ext == ".scene") FixupDocPaths(oldFull, newFull);
            if (ext == ".scene" &&
                State.CurrentScenePath != null &&
                System.IO.Path.GetFullPath(State.CurrentScenePath) == System.IO.Path.GetFullPath(oldFull))
            {
                State.CurrentScenePath = newFull;
                if (State.CurrentScene != null) State.CurrentScene.Name = newName;
                RememberLastScene(newFull);
                if (System.IO.File.Exists(oldFull + ".bak"))
                    System.IO.File.Move(oldFull + ".bak", newFull + ".bak");
            }

            SaveRegistryOrWarn();
            _projectPanel.RequestRefresh();
            Toast($"Renamed: {System.IO.Path.GetFileName(oldFull)} -> {newName + ext}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Rename] failed: {oldFull} -> {newName}{ext}\n{ex}");
            ToastError("Rename failed: " + ex.Message);
            _projectPanel.RequestRefresh();
        }
    }

    private static void UpdateRegistryPath(string oldFull, string newFull)
    {
        var id = AssetRegistry.Instance.GetId(ToContentRelative(oldFull));
        if (id != null) AssetRegistry.Instance.UpdatePath(id, ToContentRelative(newFull));
    }

    private void RetargetScenePaths(string oldAtlasRel, string newAtlasRel, string oldTexRel, string newTexRel)
    {
        if (State.CurrentScene == null) return;
        foreach (var e in State.CurrentScene.Entities)
        {
            foreach (var sr in e.GetComponents<SpriteRenderer>())
            {
                if (sr.AtlasPath == oldAtlasRel) sr.AtlasPath = newAtlasRel;
                if (sr.TexturePath == oldTexRel) sr.TexturePath = newTexRel;
            }
        }
    }

    public void RestoreSceneAfterRoomChange()
        => PlayRoomRestore.Run(State, LoadCurrentSceneFromDisk, () => OnRuntimeSceneLoaded?.Invoke());

    public void DiscardEditsAndReloadFromDisk()
        => PlayRoomRestore.Recharge(State, LoadCurrentSceneFromDisk(), keepDirty: false,
                                    () => OnRuntimeSceneLoaded?.Invoke());

    private SceneData? LoadCurrentSceneFromDisk()
    {
        var path = State.CurrentScenePath;
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
        try { return SceneSerializer.LoadFromFile(path); }
        catch (Exception ex) { Console.WriteLine($"[Scene] Reload failed: {path}\n{ex}"); return null; }
    }

    private void NewScene()
    {
        if (State.CurrentScene == null) return;
        var doc = new SceneDoc(new Scene("Untitled"), null);
        if (_camera != null) { doc.CameraPos = _camera.Position; doc.CameraZoom = _camera.Zoom; }
        _docs.Add(doc);
        ActivateDoc(doc);
        _focusDoc = doc;
    }

    private void DrawDialogs()
    {
        if (_openSaveAsPopup) { ImGui.OpenPopup("Save Scene As"); _openSaveAsPopup = false; }
        if (_openOpenScenePopup) { ImGui.OpenPopup("Open Scene"); _openOpenScenePopup = false; }
        if (_openRenamePopup) { ImGui.OpenPopup("Rename Asset"); _openRenamePopup = false; }

        var vp = ImGui.GetMainViewport();
        var center = new System.Numerics.Vector2(vp.Pos.X + vp.Size.X * 0.5f, vp.Pos.Y + vp.Size.Y * 0.5f);

        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new System.Numerics.Vector2(0.5f, 0.5f));
        if (ImGui.BeginPopupModal("Save Scene As", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Scene name:");
            ImGui.SetNextItemWidth(240);
            bool enter = ImGui.InputText("##sceneName", ref _saveAsName, 64, ImGuiInputTextFlags.EnterReturnsTrue);
            bool save = ImGui.Button(Icons.Save + "  Save", new System.Numerics.Vector2(100, 0)) || enter;
            if (save && !string.IsNullOrWhiteSpace(_saveAsName))
            {
                DoSave(System.IO.Path.Combine(ScenesDir, _saveAsName + ".scene"));
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new System.Numerics.Vector2(100, 0)))
            {
                _quitAfterSave = false;
                _newAfterSave = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new System.Numerics.Vector2(0.5f, 0.5f));
        if (ImGui.BeginPopupModal("Rename Asset", ImGuiWindowFlags.AlwaysAutoResize))
        {
            bool isDir = System.IO.Directory.Exists(_renameTargetPath);
            var oldName = System.IO.Path.GetFileName(_renameTargetPath.TrimEnd('/'));
            var renameExt = isDir ? "" : System.IO.Path.GetExtension(_renameTargetPath);
            ImGui.Text($"{(isDir ? Icons.Folder + "  " : "")}{oldName} →");
            ImGui.SetNextItemWidth(240);
            bool renameEnter = ImGui.InputText("##renameName", ref _renameName, 64, ImGuiInputTextFlags.EnterReturnsTrue);
            if (renameExt.Length > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled(renameExt);
            }
            if (renameExt is ".png" or ".jpg" or ".jpeg" or ".bmp")
                ImGui.TextDisabled("The .atlas sidecar is renamed too");
            if (isDir)
                ImGui.TextDisabled("Scene and registry references to the assets inside are retargeted automatically");
            bool doRename = ImGui.Button("Rename", new System.Numerics.Vector2(100, 0)) || renameEnter;
            if (doRename && !string.IsNullOrWhiteSpace(_renameName))
            {
                if (isDir) RenameFolder(_renameTargetPath, _renameName);
                else RenameAsset(_renameTargetPath, _renameName);
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new System.Numerics.Vector2(100, 0)))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }

        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new System.Numerics.Vector2(0.5f, 0.5f));
        if (ImGui.BeginPopupModal("Open Scene", ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (System.IO.Directory.Exists(ScenesDir))
            {
                var files = System.IO.Directory.GetFiles(ScenesDir, "*.scene");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                if (files.Length == 0) ImGui.TextDisabled("(no .scene files in Content/Scenes)");
                foreach (var f in files)
                    if (ImGui.Selectable(Icons.File + "  " + System.IO.Path.GetFileName(f)))
                    {
                        OpenScene(f);
                        ImGui.CloseCurrentPopup();
                    }
            }
            else ImGui.TextDisabled("(no Content/Scenes folder)");
            ImGui.Separator();
            if (ImGui.Button("Cancel", new System.Numerics.Vector2(100, 0))) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }

        if (_openSceneConfirmPopup) { ImGui.OpenPopup("Open Scene?"); _openSceneConfirmPopup = false; }
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new System.Numerics.Vector2(0.5f, 0.5f));
        if (ImGui.BeginPopupModal("Open Scene?", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text($"'{State.CurrentScene?.Name}' has unsaved changes.");
            ImGui.TextDisabled("Open another scene anyway? (the changes will be lost)");
            ImGui.Dummy(new System.Numerics.Vector2(0, 4));
            if (ImGui.Button(Icons.FolderOpen + "  Discard & Open", new System.Numerics.Vector2(150, 0)))
            {
                TryDeleteBackup(State.CurrentScenePath);
                if (_pendingOpenScenePath != null) OpenScene(_pendingOpenScenePath);
                _pendingOpenScenePath = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new System.Numerics.Vector2(90, 0)))
            {
                _pendingOpenScenePath = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        if (_openNewSceneConfirmPopup) { ImGui.OpenPopup("New Scene?"); _openNewSceneConfirmPopup = false; }
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new System.Numerics.Vector2(0.5f, 0.5f));
        if (ImGui.BeginPopupModal("New Scene?", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text($"'{State.CurrentScene?.Name}' has unsaved changes.");
            ImGui.TextDisabled("Save before creating a new scene?");
            ImGui.Dummy(new System.Numerics.Vector2(0, 4));

            if (ImGui.Button(Icons.Save + "  Save & New", new System.Numerics.Vector2(130, 0)))
            {
                _newAfterSave = true;
                ImGui.CloseCurrentPopup();
                State.SetMode(EditorMode.Edit);
                SaveScene();
            }
            ImGui.SameLine();
            if (ImGui.Button("New without Saving", new System.Numerics.Vector2(160, 0)))
            {
                TryDeleteBackup(State.CurrentScenePath);
                TryDeleteBackup(null);
                NewScene();
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new System.Numerics.Vector2(90, 0)))
            {
                _newAfterSave = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        if (_quitModalOpen) { ImGui.OpenPopup("Unsaved Changes"); _quitModalOpen = false; }
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new System.Numerics.Vector2(0.5f, 0.5f));
        if (ImGui.BeginPopupModal("Unsaved Changes", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("There are unsaved changes:");
            foreach (var d in _docs)
                if (d == _activeDoc ? State.IsDirty : d.Dirty)
                    ImGui.TextDisabled($"  {Icons.Cube}  Scene '{d.Scene.Name}'");
            if (SpriteEditor.IsDirty)
                ImGui.TextDisabled($"  {Icons.Image}  Sprite slices (.atlas)");
            if (AnimationEditor.IsDirty)
                ImGui.TextDisabled($"  {Icons.Film}  Animation clip (.anim)");
            ImGui.TextDisabled("Save before quitting?");
            ImGui.Dummy(new System.Numerics.Vector2(0, 4));

            if (ImGui.Button(Icons.Save + "  Save & Quit", new System.Numerics.Vector2(130, 0)))
            {
                SpriteEditor.SaveIfDirty();
                AnimationEditor.SaveIfDirty();
                SaveAllDirtyDocs();
                ImGui.CloseCurrentPopup();
                State.SetMode(EditorMode.Edit);
                if (State.IsDirty && State.CurrentScene != null)
                {
                    _quitAfterSave = true;
                    SaveScene();
                }
                else
                {
                    _game.Exit();
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Quit without Saving", new System.Numerics.Vector2(160, 0)))
            {
                TryDeleteBackup(State.CurrentScenePath);
                TryDeleteBackup(null);
                _game.Exit();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new System.Numerics.Vector2(90, 0)))
            {
                _quitAfterSave = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        if (_aboutModalOpen) { ImGui.OpenPopup("About PixelCore###About"); _aboutModalOpen = false; }
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new System.Numerics.Vector2(0.5f, 0.5f));
        if (ImGui.BeginPopupModal("About PixelCore###About", ImGuiWindowFlags.AlwaysAutoResize))
        {
            const float cell = 9f;
            const float logoSize = cell * 4;
            const float contentW = 250f;

            var p = ImGui.GetCursorScreenPos();
            DrawDotLogo(ImGui.GetWindowDrawList(),
                new System.Numerics.Vector2(p.X + (contentW - logoSize) * 0.5f, p.Y + 6), cell, 1.5f);
            ImGui.Dummy(new System.Numerics.Vector2(contentW, logoSize + 16));

            CenteredText("PixelCore");
            CenteredTextDisabled("A 2D top-down pixel engine with an in-game editor");
            ImGui.Dummy(new System.Numerics.Vector2(0, 4));
            ImGui.PushFont(ImGuiRenderer.MonoFont);
            CenteredTextDisabled("FNA · SDL3 · Metal · .NET 8");
            if (GitHash.Length > 0) CenteredTextDisabled("commit " + GitHash);
            ImGui.PopFont();

            ImGui.Dummy(new System.Numerics.Vector2(0, 10));
            const float closeW = 90f;
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - closeW) * 0.5f);
            if (ImGui.Button("Close", new System.Numerics.Vector2(closeW, 0)))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }

        if (_recoveryModalOpen) { ImGui.OpenPopup("Recover work"); _recoveryModalOpen = false; }
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new System.Numerics.Vector2(0.5f, 0.5f));
        if (ImGui.BeginPopupModal("Recover work", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("There is a backup of unsaved work.");
            if (_recoveryBakPath != null)
            {
                var t = System.IO.File.GetLastWriteTime(_recoveryBakPath);
                ImGui.TextDisabled($"{System.IO.Path.GetFileName(_recoveryBakPath)} · {t:yyyy-MM-dd HH:mm:ss}");
            }
            ImGui.TextDisabled("It looks like a scene that a crash or force quit left unsaved.");
            ImGui.Dummy(new System.Numerics.Vector2(0, 4));

            if (ImGui.Button(Icons.Save + "  Recover", new System.Numerics.Vector2(110, 0)))
            {
                ApplyRecovery();
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Discard", new System.Numerics.Vector2(90, 0)))
            {
                try { if (_recoveryBakPath != null) System.IO.File.Delete(_recoveryBakPath); } catch { }
                _recoveryBakPath = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    public (Rectangle bounds, Vector2 anchor, bool isDragging)? GetSelectedGizmo()
    {
        return _sceneViewPanel.GetSelectedGizmo();
    }

    public int GetDragAxis() => _sceneViewPanel.DragAxis;

    public void BeginLayout(GameTime gameTime)
    {
        _imGuiRenderer.BeginLayout(gameTime);

        float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        AnimationEditor.Update(deltaTime);

        if (_toastTimer > 0) _toastTimer -= deltaTime;

        if (State.Mode == EditorMode.Edit && State.IsDirty)
        {
            _backupTimer += deltaTime;
            if (_backupTimer >= BackupInterval)
            {
                _backupTimer = 0f;
                WriteBackup("periodic");
            }
        }
        else
        {
            _backupTimer = 0f;
        }
    }

    public void EndLayout()
    {
        _imGuiRenderer.EndLayout();
    }

    public RenderTarget2D? GetSceneRenderTarget() => _sceneViewPanel.AcquireRenderTarget();

    public (int Width, int Height) GetSceneViewSize() => _sceneViewPanel.GetViewSize();

    public bool IsSceneViewHovered() => _sceneViewPanel.IsHovered;

    public Vector2 ScreenToSceneLocal(Vector2 screenPos) => _sceneViewPanel.ScreenToLocal(screenPos);

    public Runtime.Core.Camera? RuntimeCamera { get; set; }

    public Runtime.Core.GameContext? RuntimeContext { get; set; }

    public Action? GameViewRequested { get; set; }

    public void DrawUI()
    {
        WindowChrome.Install(_game.Window.Handle);

        DebugOverlay.Draw(State, RuntimeCamera);

        bool active = _game.IsActive;
        if (active && !_wasActive) _projectPanel.RequestRefresh();
        _wasActive = active;

        ImportDroppedFiles();
        FileDialog.Pump();

        if (State.OpenSceneRequest != null)
        {
            var reqPath = State.OpenSceneRequest;
            State.OpenSceneRequest = null;
            RequestOpenScene(reqPath);
        }

        DrawMainMenuBar();
        using (FrameProf.Measure("  ui.shell")) DrawShell();

        bool forceDock = _forceDockNextFrame;
        _forceDockNextFrame = false;

        _hierarchyPanel.PendingSceneId = _projectPanel.DraggedSceneId;
        _sceneViewPanel.Clock = Clock;

        SceneDoc? toActivate = null;
        SceneDoc? toClose = null;
        foreach (var doc in _docs.ToArray())
        {
            if (_focusDoc == doc) ImGui.SetNextWindowFocus();
            using var _profDoc = FrameProf.Measure("  ui.roomDoc");
            var res = _roomDoc.Draw(_shellDockId, doc, doc == _activeDoc);
            if (res == RoomDocument.DocResult.Activate) toActivate = doc;
            else if (res == RoomDocument.DocResult.Close) toClose = doc;
        }
        _focusDoc = null;
        if (toActivate != null) ActivateDoc(toActivate);
        if (toClose != null) RequestCloseDoc(toClose);
        DrawCloseDocModal();

        SpriteEditor.Draw(_shellDockId, forceDock);
        AnimationEditor.Draw(_shellDockId, forceDock);
        DrawDeleteModal();

        TilemapEditor.Draw();

        _palette.Draw();
        DrawDialogs();
        UpdateWindowTitle();
        DrawToast();
    }

    private void DrawShell()
    {
        var viewport = ImGui.GetMainViewport();
        float menuBarHeight = _menuBarHeight > 0 ? _menuBarHeight : ImGui.GetFrameHeight();

        ImGui.SetNextWindowPos(new System.Numerics.Vector2(viewport.Pos.X, viewport.Pos.Y + menuBarHeight));
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(viewport.Size.X, viewport.Size.Y - menuBarHeight));
        ImGui.SetNextWindowViewport(viewport.ID);

        var windowFlags = ImGuiWindowFlags.NoDocking
            | ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoBringToFrontOnFocus
            | ImGuiWindowFlags.NoNavFocus
            | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, System.Numerics.Vector2.Zero);

        ImGui.Begin("ShellWindow", windowFlags);
        ImGui.PopStyleVar(3);

        var panelCol = ImGui.GetStyle().Colors[(int)ImGuiCol.MenuBarBg];
        uint panelBg = ImGui.GetColorU32(panelCol);
        uint canvasBg = ImGui.GetColorU32(EditorTheme.Recessed);
        ImGui.PushFont(ImGuiRenderer.MonoFont);
        float statusTextH = ImGui.GetTextLineHeight();
        ImGui.PopFont();
        float statusH = MathF.Max(statusTextH, ImGui.GetTextLineHeight())
                      + 5f * 2f
                      + ImGui.GetStyle().ItemSpacing.Y
                      + 2f;

        ImGui.BeginChild("shell_main",
            new System.Numerics.Vector2(0, -statusH), ImGuiChildFlags.None);

        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, System.Numerics.Vector2.Zero);
        int shellCols = _explorerVisible ? 2 : 1;
        string shellId = _explorerVisible ? "shell_ex2" : "shell_ex1";
        if (ImGui.BeginTable(shellId, shellCols,
                ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
        {
            if (_explorerVisible)
                ImGui.TableSetupColumn("explorer", ImGuiTableColumnFlags.WidthFixed, 250f);
            ImGui.TableSetupColumn("dock", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, canvasBg);

            if (_explorerVisible)
            {
                ImGui.TableNextColumn();
                ImGui.PushStyleColor(ImGuiCol.ChildBg, panelCol);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(8, 8));
                ImGui.BeginChild("explorer_child", new System.Numerics.Vector2(0, 0),
                    ImGuiChildFlags.AlwaysUseWindowPadding);
                EditorWidgets.PanelTitle(Icons.Folder, "Assets", EditorWidgets.PanelFocused());
                using (FrameProf.Measure("    ui.explorer")) _projectPanel.DrawContent();
                ImGui.EndChild();
                ImGui.PopStyleVar();
                ImGui.PopStyleColor();
            }

            ImGui.TableNextColumn();
            float drawerH = EditorConsole.Open ? EditorConsole.Height : 0f;
            ImGui.BeginChild("dock_host", new System.Numerics.Vector2(0, -drawerH));
            _shellDockId = ImGui.GetID("PixelCoreShellDock");
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, EditorWidgets.DocTabPadding);
            ImGui.DockSpace(_shellDockId, System.Numerics.Vector2.Zero, ImGuiDockNodeFlags.None);
            ImGui.PopStyleVar();
            ImGui.EndChild();
            _dockRectMin = ImGui.GetItemRectMin();
            _dockRectMax = ImGui.GetItemRectMax();

            if (EditorConsole.Open)
                EditorConsole.DrawDrawer(_prefs, 0f);

            ImGui.EndTable();
        }
        ImGui.PopStyleVar();

        ImGui.EndChild();

        DrawStatusBar(panelBg);

        ImGui.End();
    }

    public void SelectAssetForShot(string query) => _projectPanel.SelectAssetByQuery(query);

    public void RevealAssetForShot(string contentRelOrFull)
    {
        var full = System.IO.Path.IsPathRooted(contentRelOrFull) ? contentRelOrFull
                 : System.IO.Path.Combine(ContentRoot, contentRelOrFull);
        _inspectorPanel.OnRevealAssetRequested?.Invoke(System.IO.Path.GetFullPath(full));
    }

    public Panels.SceneViewPanel SceneView => _sceneViewPanel;

    private void ToggleConsole()
    {
        EditorConsole.Open = !EditorConsole.Open;
        _prefs.ConsoleOpen = EditorConsole.Open;
        _prefs.Save();
    }

    private System.Numerics.Vector2 _dockRectMin, _dockRectMax;

    private float _menuBarHeight;

    private void DrawDockEmptyState()
    {
        var center = new System.Numerics.Vector2(
            (_dockRectMin.X + _dockRectMax.X) * 0.5f,
            (_dockRectMin.Y + _dockRectMax.Y) * 0.5f);
        ImGui.SetNextWindowPos(center, ImGuiCond.Always, new System.Numerics.Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowBgAlpha(0f);
        var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize
                  | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoDocking
                  | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove;
        if (ImGui.Begin("##dockEmpty", flags))
        {
            const float cell = 5f;
            const float contentW = 280f;
            float mw = Mascot.GridW * cell, mh = Mascot.GridH * cell;

            var p = ImGui.GetCursorScreenPos();
            Mascot.Draw(ImGui.GetWindowDrawList(),
                new System.Numerics.Vector2(p.X + (contentW - mw) * 0.5f, p.Y), cell);
            ImGui.Dummy(new System.Numerics.Vector2(contentW, mh + 12));

            CenteredText("Quiet in here. What shall we open?");
            ImGui.Dummy(new System.Numerics.Vector2(0, 4));
            CenteredTextDisabled("Cmd+P - search assets and commands");

            ImGui.Dummy(new System.Numerics.Vector2(0, 10));
            string sceneName = State.CurrentScene?.Name ?? "Untitled";
        string btnLabel = $"{Icons.Cube}  Reopen room - {sceneName}";
            float bw = ImGui.CalcTextSize(btnLabel).X + 28;
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - bw) * 0.5f);
            if (ImGui.Button(btnLabel, new System.Numerics.Vector2(bw, 0)))
                _focusDoc = _activeDoc;
        }
        ImGui.End();
    }

    private static void CenteredText(string text)
    {
        ImGui.SetCursorPosX(System.Math.Max(0f, (ImGui.GetWindowWidth() - ImGui.CalcTextSize(text).X) * 0.5f));
        ImGui.TextUnformatted(text);
    }

    private static void CenteredTextDisabled(string text)
    {
        ImGui.SetCursorPosX(System.Math.Max(0f, (ImGui.GetWindowWidth() - ImGui.CalcTextSize(text).X) * 0.5f));
        ImGui.TextDisabled(text);
    }

    private const float ChipGap = 6f;
    private static readonly System.Numerics.Vector2 ChipPad = new(8f, 1f);

    private static float StatusChipWidth(string label)
        => ImGui.CalcTextSize(label).X + ChipPad.X * 2f;

    private static bool StatusChip(string id, string label, bool on, System.Numerics.Vector4 onColor)
    {
        var dim = ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled];
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, ChipPad);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 99f);
        ImGui.PushStyleColor(ImGuiCol.Button, on
            ? new System.Numerics.Vector4(onColor.X, onColor.Y, onColor.Z, 0.18f)
            : EditorTheme.Recessed);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, on
            ? new System.Numerics.Vector4(onColor.X, onColor.Y, onColor.Z, 0.30f)
            : EditorTheme.ItemBg);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, EditorTheme.ItemHoverBg);
        ImGui.PushStyleColor(ImGuiCol.Text, on ? onColor : dim);
        bool clicked = ImGui.Button(label + id);
        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar(2);
        if (ImGui.IsItemHovered()) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return clicked;
    }

    private void DrawStatusBar(uint panelBg)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, panelBg);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(12, 5));
        ImGui.BeginChild("shell_status", new System.Numerics.Vector2(0, 0),
            ImGuiChildFlags.AlwaysUseWindowPadding,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        ImGui.PushFont(ImGuiRenderer.MonoFont);

        if (State.IsPlayMode)
        {
            ImGui.TextColored(EditorTheme.Accent, Icons.Play + " Playing");
            ImGui.SameLine(0, 14);
        }
        if (State.IsDirty)
            ImGui.TextColored(EditorTheme.Accent, Icons.Circle + " Modified");
        else
            ImGui.TextColored(EditorTheme.Success, Icons.Circle + " Saved");

        if (State.CurrentScene != null)
        {
            ImGui.SameLine(0, 14);
            string sceneLabel = State.CurrentScenePath != null
                ? System.IO.Path.GetFileName(State.CurrentScenePath)
                : (State.CurrentScene?.Name ?? "") + " (unsaved)";
            ImGui.TextDisabled(sceneLabel);

            if (State.SelectedEntity != null)
            {
                ImGui.SameLine(0, 14);
                ImGui.TextDisabled(State.Selection.Count > 1
                    ? $"Selected: {State.SelectedEntity.Name} and {State.Selection.Count - 1} more"
                    : "Selected: " + State.SelectedEntity.Name);
            }
        }

        string consoleBadge = EditorConsole.ErrorCount > 0
            ? $"{Icons.Terminal} {EditorConsole.ErrorCount}"
            : EditorConsole.WarnCount > 0
                ? $"{Icons.Terminal} {EditorConsole.WarnCount}"
                : $"{Icons.Terminal} 0";
        var consoleColor = EditorConsole.ErrorCount > 0 ? EditorTheme.Danger
            : EditorConsole.WarnCount > 0 ? EditorTheme.Accent
            : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled];

        string snap = State.SnapToGrid ? $"Snap {State.GridSize}px" : "Snap off";
        string grid = State.ShowGrid ? $"Grid {State.GridSize}px" : "Grid off";
        string fps = State.IsPlayMode ? $"{ImGui.GetIO().Framerate:0} fps" : "";

        float rightW = StatusChipWidth(consoleBadge) + StatusChipWidth(snap) + StatusChipWidth(grid)
                     + ChipGap * 2
                     + (fps.Length > 0 ? ImGui.CalcTextSize(fps).X + ChipGap + 8 : 0);
        ImGui.SameLine(ImGui.GetWindowWidth() - rightW - 12);

        if (StatusChip("##sbConsole", consoleBadge, EditorConsole.Open, consoleColor)) ToggleConsole();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Console (Cmd+J)");

        ImGui.SameLine(0, ChipGap);
        if (StatusChip("##sbSnap", snap, State.SnapToGrid, EditorTheme.Accent))
            State.SnapToGrid = !State.SnapToGrid;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Toggle grid snapping");

        ImGui.SameLine(0, ChipGap);
        if (StatusChip("##sbGrid", grid, State.ShowGrid, EditorTheme.Accent))
        {
            State.ShowGrid = !State.ShowGrid;
            _prefs.GridVisible = State.ShowGrid;
            _prefs.Save();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Toggle the grid");

        if (fps.Length > 0)
        {
            ImGui.SameLine(0, ChipGap);
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled(fps);
        }

        ImGui.PopFont();
        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    private System.Collections.Generic.IEnumerable<CommandPalette.Item> BuildPaletteItems()
    {
        var items = new System.Collections.Generic.List<CommandPalette.Item>();

        foreach (var a in _projectPanel.Assets)
        {
            var e = a;
            System.Action action;
            if (e.Kind is AssetKind.Scene or AssetKind.Prefab)
                action = () => RequestOpenScene(e.FullPath);
            else if (e.Kind == AssetKind.Sprite && e.Extension != ".atlas")
                action = () => SpriteEditor.OpenTexture(e.FullPath);
            else if (e.Kind == AssetKind.Animation)
                action = () => AnimationEditor.RequestOpen(e.FullPath);
            else
                action = () => Panels.ProjectPanel.RevealInFinder(e.FullPath);

            items.Add(new CommandPalette.Item(e.Name, e.RelDir, e.Kind, action));
        }

        items.Add(new CommandPalette.Item("New scene", "Command · Ctrl+N", null, RequestNewScene));
        items.Add(new CommandPalette.Item("Open scene...", "Command · Ctrl+O", null, () => _openOpenScenePopup = true));
        items.Add(new CommandPalette.Item("Save", "Command · Ctrl+S", null, SaveScene));
        items.Add(new CommandPalette.Item("Save as...", "Command", null,
            () => { _saveAsName = State.CurrentScene?.Name ?? "scene"; _openSaveAsPopup = true; }));
        items.Add(new CommandPalette.Item("Toggle play/stop", "Command", null, () => State.ToggleMode()));
        items.Add(new CommandPalette.Item("Go to the active scene tab", "Command", null, () => _focusDoc = _activeDoc));
        items.Add(new CommandPalette.Item("Frame the whole scene", "Command · F", null,
            () => _sceneViewPanel.RequestFrame(SceneViewPanel.FrameTarget.All)));
        items.Add(new CommandPalette.Item("Frame the selection", "Command · F", null,
            () => _sceneViewPanel.RequestFrame(SceneViewPanel.FrameTarget.Selection)));
        items.Add(new CommandPalette.Item("Close all documents", "Command", null, CloseAllDocuments));
        items.Add(new CommandPalette.Item("Toggle the asset panel", "Command", null, () => _explorerVisible = !_explorerVisible));
        items.Add(new CommandPalette.Item("Toggle the console", "Command · Cmd+J", null, ToggleConsole));
        items.Add(new CommandPalette.Item("Toggle theme (dark / light)", "Command", null,
            () => SetTheme(EditorTheme.Mode == ThemeMode.Light ? ThemeMode.Dark : ThemeMode.Light)));
        items.Add(new CommandPalette.Item("Reset layout", "Command", null, () => _forceDockNextFrame = true));

        if (RuntimeContext != null)
        {
            var ctx = RuntimeContext;
            {
                var mode = Runtime.Systems.InteractionHighlight.Mode;
                items.Add(new CommandPalette.Item($"Highlight: {mode} -> next", "Interaction · cycle the aim indicator", null, () =>
                {
                    var m = Runtime.Systems.InteractionHighlight.Mode;
                    var next = m switch
                    {
                        Runtime.Components.InteractableHighlight.None => Runtime.Components.InteractableHighlight.Outline,
                        Runtime.Components.InteractableHighlight.Outline => Runtime.Components.InteractableHighlight.Tint,
                        _ => Runtime.Components.InteractableHighlight.None,
                    };
                    Runtime.Systems.InteractionHighlight.Mode = next;
                    Runtime.Systems.InteractionHighlight.Clear();
                    Toast($"{Icons.Eye}  Aim indicator: {next}");
                }));
            }

            items.Add(new CommandPalette.Item("Save: checkpoint now", "Save · only at a safe moment", null, () =>
            {
                if (State.IsEditMode) State.SetMode(EditorMode.Play);
                ctx.Save.Checkpoint("manual.palette");
            }));
            items.Add(new CommandPalette.Item("Save: continue", "Save · the last checkpoint", null, () =>
            {
                if (State.IsEditMode) State.SetMode(EditorMode.Play);
                ctx.Save.LoadLatest();
            }));
            items.Add(new CommandPalette.Item("Save: new game (day 1, morning)", "Save · from the start with no delta", null, () =>
            {
                if (State.IsEditMode) State.SetMode(EditorMode.Play);
                ctx.Save.ResetForNewGame();
                Gameplay.Systems.GameFlow.StartDay(1, Gameplay.Systems.DayPhase.Morning);
            }));
            items.Add(new CommandPalette.Item("Open the title screen", "Game · exactly as it boots", null, () =>
            {
                GameViewRequested?.Invoke();
                Gameplay.Systems.TitleMenu.Open();
            }));
        }

        foreach (var id in Runtime.Cutscenes.CutsceneDirector.Ids)
        {
            string cutsceneId = id;
            items.Add(new CommandPalette.Item($"Play cutscene: {cutsceneId}", "Cutscene · start here", null, () =>
            {
                if (State.IsEditMode) State.SetMode(EditorMode.Play);
                Runtime.Cutscenes.CutsceneDirector.Play(cutsceneId);
            }));
        }

        return items;
    }

    private static readonly string GitHash = ReadGitHash();

    private static string ReadGitHash()
    {
        try
        {
            var root = System.IO.Path.GetDirectoryName(ContentRoot)!;
            var headPath = System.IO.Path.Combine(root, ".git", "HEAD");
            if (!System.IO.File.Exists(headPath)) return "";
            var head = System.IO.File.ReadAllText(headPath).Trim();
            if (head.StartsWith("ref: "))
            {
                var refPath = System.IO.Path.Combine(root, ".git",
                    head.Substring(5).Trim().Replace('/', System.IO.Path.DirectorySeparatorChar));
                if (!System.IO.File.Exists(refPath)) return "";
                head = System.IO.File.ReadAllText(refPath).Trim();
            }
            return head.Length >= 7 ? head.Substring(0, 7) : head;
        }
        catch { return ""; }
    }

    private void UpdateWindowTitle()
    {
        var title = "PixelCore";
        if (State.CurrentScene != null) title += "  —  " + State.CurrentScene.Name;
        if (State.IsDirty) title += "  ●";
        if (title != _lastWindowTitle)
        {
            _game.Window.Title = title;
            _lastWindowTitle = title;
        }
    }

    private void DrawToast()
    {
        if (_toastTimer <= 0) return;
        float fadeOut = System.Math.Min(1f, _toastTimer);
        float appear = System.Math.Clamp((_toastDuration - _toastTimer) / 0.18f, 0f, 1f);
        float ease = 1f - (1f - appear) * (1f - appear);
        float alpha = fadeOut * (0.25f + 0.75f * ease);

        var toastColor = _toastKind == ToastKind.Error ? EditorTheme.Danger : EditorTheme.Success;

        var vp = ImGui.GetMainViewport();
        float drawerLift = EditorConsole.Open ? EditorConsole.Height : 0f;
        var pos = new System.Numerics.Vector2(
            vp.Pos.X + vp.Size.X * 0.5f,
            vp.Pos.Y + vp.Size.Y - 56 - drawerLift + (1f - ease) * 14f);
        ImGui.SetNextWindowPos(pos, ImGuiCond.Always, new System.Numerics.Vector2(0.5f, 1f));
        ImGui.SetNextWindowBgAlpha(0.96f * alpha);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(12, 8));
        ImGui.PushStyleColor(ImGuiCol.Border,
            new System.Numerics.Vector4(toastColor.X, toastColor.Y, toastColor.Z, 0.45f * alpha));

        var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoNav
                  | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing
                  | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoDocking;
        if (ImGui.Begin("##saveToast", flags))
            ImGui.TextColored(new System.Numerics.Vector4(toastColor.X, toastColor.Y, toastColor.Z, alpha), _toastMessage);
        ImGui.End();

        ImGui.PopStyleColor();
        ImGui.PopStyleVar(3);
    }

    private void DrawMainMenuBar()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new System.Numerics.Vector2(11, 10));
        if (ImGui.BeginMainMenuBar())
        {
            _menuBarHeight = ImGui.GetWindowHeight();

            float uiScale = ImGui.GetMainViewport().Size.X
                / System.Math.Max(1, _game.Window.ClientBounds.Width);

            if (WindowChrome.Unified && !WindowChrome.IsFullscreen)
                ImGui.Dummy(new System.Numerics.Vector2(78f * uiScale, 0));

            DrawLogo();

            {
                float y0 = ImGui.GetCursorPosY();
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new System.Numerics.Vector2(9, 6));
                ImGui.SetCursorPosY((ImGui.GetWindowHeight() - (ImGui.GetFontSize() + 12f)) * 0.5f);
                bool exOn = _explorerVisible;
                if (exOn)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, EditorTheme.AccentSoft);
                    ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Accent);
                }
                else
                    ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0, 0, 0, 0));
                if (ImGui.Button(Icons.Cubes + "###mbAssets"))
                    _explorerVisible = !_explorerVisible;
                ImGui.PopStyleColor(exOn ? 2 : 1);
                ImGui.PopStyleVar();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Asset panel");
                ImGui.SetCursorPosY(y0);
                ImGui.Dummy(new System.Numerics.Vector2(4, 0));
            }

            if (ImGui.BeginMenu("File"))
            {
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new System.Numerics.Vector2(8, 5));
                if (ImGui.MenuItem(Icons.New + "  New scene", "Ctrl+N")) RequestNewScene();
                if (ImGui.MenuItem(Icons.FolderOpen + "  Open scene...", "Ctrl+O")) _openOpenScenePopup = true;
                ImGui.Separator();
                if (ImGui.MenuItem(Icons.Save + "  Save", "Ctrl+S")) SaveScene();
                if (ImGui.MenuItem(Icons.Save + "  Save as...", "Ctrl+Shift+S"))
                {
                    _saveAsName = State.CurrentScene?.Name ?? "scene";
                    _openSaveAsPopup = true;
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Quit"))
                {
                    RequestQuit();
                }
                ImGui.PopStyleVar();
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Edit"))
            {
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new System.Numerics.Vector2(8, 5));
                if (ImGui.MenuItem(Icons.Undo + "  Undo", "Ctrl+Z", false, State.CommandHistory.CanUndo))
                {
                    State.Undo();
                }
                if (ImGui.MenuItem(Icons.Redo + "  Redo", "Ctrl+Y", false, State.CommandHistory.CanRedo))
                {
                    State.Redo();
                }
                ImGui.Separator();
                if (ImGui.MenuItem(Icons.Trash + "  Delete", "Del", false, State.SelectedEntity != null))
                {
                    DeleteSelectedEntity();
                }
                ImGui.PopStyleVar();
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("View"))
            {
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new System.Numerics.Vector2(8, 5));
                if (ImGui.MenuItem("Go to the active scene tab"))
                    _focusDoc = _activeDoc;
                if (ImGui.MenuItem("Asset panel", "", _explorerVisible))
                    _explorerVisible = !_explorerVisible;
                if (ImGui.MenuItem("Tilemap tools", "", TilemapEditor.IsOpen))
                    TilemapEditor.IsOpen = !TilemapEditor.IsOpen;
                if (ImGui.MenuItem("Debug overlay", "F1", State.ShowDebugOverlay))
                    State.ShowDebugOverlay = !State.ShowDebugOverlay;
                ImGui.Separator();
                if (ImGui.MenuItem(State.Selection.Count > 0 ? "Frame the selection" : "Frame the whole scene", "F"))
                    _sceneViewPanel.RequestFrame(State.Selection.Count > 0
                        ? SceneViewPanel.FrameTarget.Selection
                        : SceneViewPanel.FrameTarget.All);
                ImGui.Separator();
                if (ImGui.MenuItem("Show the grid", "", State.ShowGrid))
                {
                    State.ShowGrid = !State.ShowGrid;
                    _prefs.GridVisible = State.ShowGrid;
                    _prefs.Save();
                }
                if (ImGui.MenuItem("Show colliders", "", State.ShowColliders))
                {
                    State.ShowColliders = !State.ShowColliders;
                    _prefs.CollidersVisible = State.ShowColliders;
                    _prefs.Save();
                }
                if (ImGui.MenuItem("Show gizmos", "", State.ShowGizmos))
                {
                    State.ShowGizmos = !State.ShowGizmos;
                    _prefs.GizmosVisible = State.ShowGizmos;
                    _prefs.Save();
                }
                if (ImGui.MenuItem("Console", "Cmd+J", EditorConsole.Open))
                    ToggleConsole();
                if (ImGui.MenuItem("Command palette", "Cmd+P"))
                    _palette.Open();
                ImGui.Separator();
                bool isLight = EditorTheme.Mode == ThemeMode.Light;
                if (ImGui.MenuItem("Light theme", "", isLight))
                    SetTheme(isLight ? ThemeMode.Dark : ThemeMode.Light);
                if (ImGui.MenuItem("Monochrome icons", "", AssetKinds.Monochrome))
                {
                    AssetKinds.Monochrome = !AssetKinds.Monochrome;
                    _prefs.IconMonochrome = AssetKinds.Monochrome;
                    _prefs.Save();
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Reset layout"))
                    _forceDockNextFrame = true;
                ImGui.PopStyleVar();
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Help"))
            {
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new System.Numerics.Vector2(8, 5));
                if (ImGui.MenuItem("About PixelCore"))
                    _aboutModalOpen = true;
                ImGui.PopStyleVar();
                ImGui.EndMenu();
            }

            float menusEndX = ImGui.GetCursorScreenPos().X;
            const float PlayPadY = 4f;
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new System.Numerics.Vector2(16, PlayPadY));
            float playW = 84f;
            float playX = ImGui.GetWindowWidth() - playW - 10f;
            ImGui.SetCursorPosX(playX);
            ImGui.SetCursorPosY((ImGui.GetWindowHeight() - (ImGui.GetFontSize() + PlayPadY * 2f)) * 0.5f);

            if (WindowChrome.Unified)
            {
                var wpos = ImGui.GetWindowPos();
                var mp = ImGui.GetMousePos();
                bool inBand = mp.X >= menusEndX && mp.X < playX
                              && mp.Y >= wpos.Y && mp.Y < wpos.Y + ImGui.GetWindowHeight();
                bool nativeDragged = WindowChrome.UpdateTitleDrag(inBand,
                    ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left),
                    ImGui.IsMouseClicked(ImGuiMouseButton.Left),
                    ImGui.IsMouseDown(ImGuiMouseButton.Left));
                if (nativeDragged)
                    ImGui.GetIO().AddMouseButtonEvent((int)ImGuiMouseButton.Left, false);
            }

            var themeColors = ImGui.GetStyle().Colors;
            if (State.IsEditMode)
            {
                var acc = themeColors[(int)ImGuiCol.CheckMark];
                ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
                ImGui.PushStyleColor(ImGuiCol.Border, new System.Numerics.Vector4(acc.X, acc.Y, acc.Z, 0.55f));
                ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(acc.X, acc.Y, acc.Z, 0.14f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new System.Numerics.Vector4(acc.X, acc.Y, acc.Z, 0.30f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new System.Numerics.Vector4(acc.X, acc.Y, acc.Z, 0.44f));
                ImGui.PushStyleColor(ImGuiCol.Text, acc);
                if (ImGui.Button(Icons.Play + "  Play", new System.Numerics.Vector2(playW, 0)))
                {
                    State.SetMode(EditorMode.Play);
                }
                ImGui.PopStyleColor(5);
                ImGui.PopStyleVar();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Play (F5)");
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Button, EditorTheme.Danger);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, EditorTheme.DangerHover);
                if (ImGui.Button(Icons.Stop + "  Stop", new System.Numerics.Vector2(playW, 0)))
                {
                    State.SetMode(EditorMode.Edit);
                }
                ImGui.PopStyleColor(2);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Stop (F5)");
            }
            ImGui.PopStyleVar();

            ImGui.EndMainMenuBar();
        }
        ImGui.PopStyleVar();
    }

    private static void DrawDotLogo(ImDrawListPtr dl, System.Numerics.Vector2 origin, float cell, float gap)
    {
        uint amber = ImGui.GetColorU32(EditorTheme.Accent);
        uint amberDark = ImGui.GetColorU32(new System.Numerics.Vector4(0.54f, 0.39f, 0.23f, 1f));

        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            {
                int i = y * 4 + x;
                if (i == 12) continue;
                uint c = (i == 1 || i == 2 || i == 7 || i == 11) ? amberDark : amber;
                var p0 = new System.Numerics.Vector2(origin.X + x * cell, origin.Y + y * cell);
                dl.AddRectFilled(p0, new System.Numerics.Vector2(p0.X + cell - gap, p0.Y + cell - gap), c);
            }
    }

    private void DrawLogo()
    {
        var pos = ImGui.GetCursorScreenPos();
        float barH = ImGui.GetFrameHeight();
        const float cell = 3f;
        const float size = cell * 4;
        DrawDotLogo(ImGui.GetWindowDrawList(),
            new System.Numerics.Vector2(pos.X + 4, pos.Y + (barH - size) * 0.5f), cell, 0.5f);

        ImGui.Dummy(new System.Numerics.Vector2(size + 8, 0));
        ImGui.TextUnformatted("PixelCore");
        ImGui.Dummy(new System.Numerics.Vector2(6, 0));
    }

    public void Dispose()
    {
        RememberSceneView(State.CurrentScenePath);
        WindowBounds.Save(_game.Window.Handle, _prefs);
        _prefs.Save();
        _imGuiRenderer?.Dispose();
    }
}

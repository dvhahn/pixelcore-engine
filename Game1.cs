using System;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Systems;
using PixelCore.Runtime.Tilemap;
using PixelCore.Runtime.Animation;
using PixelCore.Runtime.Audio;
using PixelCore.Runtime.Assets;
using PixelCore.Gameplay.Systems;

#if DEBUG
using ImGuiNET;
using PixelCore.Editor;
#endif

namespace PixelCore;

public class Game1 : Game
{
    private GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;

    private Scene _scene = null!;
    private Camera _camera = null!;

    private Transform? _listenerTransform;
    private PhysicsSystem _physics = null!;
    private GameContext _ctx = null!;
    private Texture2D _pixelTexture = null!;

    private float _physicsAccumulator;
    private const float FixedDt = 1f / 60f;
    private KeyboardState _prevKeyboard;

    private RenderTarget2D? _gameRenderTarget;

    private RenderTarget2D? _gameRenderTargetAbove;

    private const int CameraBleed = 2;
    private RasterizerState? _scissorState;

    private readonly PixelCore.Runtime.Rendering.LightingRenderer _lighting = new();
    private readonly PixelCore.Runtime.Rendering.ShadowRenderer _shadow = new();
    private readonly PixelCore.Runtime.Rendering.PostProcessor _post = new();
    private readonly PixelCore.Runtime.Rendering.SkyRenderer _sky = new();
    private float _totalSeconds;

#if DEBUG
    private EditorApp _editor = null!;
    private bool _editorVisible = true;

    private bool _playEnteredByBacktick;

    private const float EditorDefaultZoom = 3f;

    private Vector2 _editorCamPos;
    private float _editorCamZoom = EditorDefaultZoom;
    private bool _textInputActive;
    private int _textInputOffFrames;

    private float _roomTestSeconds;
    private static readonly string? RoomTest = Environment.GetEnvironmentVariable("PIXELCORE_ROOMTEST");

    private static readonly string? CutsceneTest = Environment.GetEnvironmentVariable("PIXELCORE_CUTSCENE");
    private float _cutsceneTestSeconds;

    private float _saveTestSeconds;
    private bool _saveTestEntered;
#endif

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        _graphics.GraphicsProfile = GraphicsProfile.HiDef;
        _graphics.PreferredBackBufferWidth = 1920;
        _graphics.PreferredBackBufferHeight = 1080;
#if !DEBUG
        _graphics.IsFullScreen = Runtime.Core.GameSettings.Current.Fullscreen;
#endif
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.AllowUserResizing = true;

#if DEBUG
        if (!ShotRequested && Environment.GetEnvironmentVariable("PIXELCORE_LOCK60") != "1")
            IsFixedTimeStep = false;
#endif
    }

    protected override void Initialize()
    {
        InputMap.Validate();

#if DEBUG
        _editor = new EditorApp(this);
        _editor.Initialize();

        if (Environment.GetEnvironmentVariable("PIXELCORE_GAMEVIEW") == "1")
        {
            _editorVisible = false;
            _editor.State.SetMode(EditorMode.Play);
        }

        if (Environment.GetEnvironmentVariable("PIXELCORE_OVERLAY") == "1")
            _editor.State.ShowDebugOverlay = true;

        if (Environment.GetEnvironmentVariable("PIXELCORE_PLAYMODE") == "1")
            _editor.State.SetMode(EditorMode.Play);

        if (Runtime.Cutscenes.CutsceneRegression.Enabled)
        {
            _editorVisible = false;
            _editor.State.SetMode(EditorMode.Play);
        }
#endif

        _scene = new Scene("TestScene");

        _physics = new PhysicsSystem(_scene);

        _camera = new Camera(GraphicsDevice);
#if DEBUG
        _camera.Zoom = EditorDefaultZoom;
#endif

        _ctx = new GameContext { Scene = _scene, Camera = _camera, Physics = _physics };
        Runtime.Cutscenes.CutsceneDirector.Bind(_ctx);

#if DEBUG
        _editor.SetScene(_scene);
        _editor.Clock = _ctx.Clock;
        _editor.State.Blackboard = _ctx.Blackboard;

        _editor.OnActiveSceneChanged += scene =>
        {
            if (!ReferenceEquals(_scene, scene)) SoundEmitter.StopAllIn(_scene);

            _scene = scene;
            _physics.SetScene(scene);
            _ctx.Scene = scene;
        };

        _editor.State.OnModeChanged += mode =>
        {
            if (mode == EditorMode.Play)
            {
                GameLoopGuard.Reset();

                _editorCamPos = _camera.Position;
                _editorCamZoom = _camera.Zoom;
                _camera.Zoom = EditorDefaultZoom;
            }
            else
            {
                _camera.Position = _editorCamPos;
                _camera.Zoom = _editorCamZoom;

                if (RoomFlow.RoomChangedDuringPlay)
                {
                    RoomFlow.RoomChangedDuringPlay = false;
                    _editor.RestoreSceneAfterRoomChange();
                }
                RoomFlow.Abort();

                Runtime.Cutscenes.CutsceneDirector.Abort();
            }
        };
#endif

        AudioManager.Instance.ContentPath = Runtime.Assets.ContentPaths.Root;
        AudioManager.Instance.Initialize(Content);

        Runtime.Core.GameSettings.Current.Apply();

        base.Initialize();
    }

    protected override void LoadContent()
    {
        Console.WriteLine($"[HiDPI] env={Environment.GetEnvironmentVariable("FNA_GRAPHICS_ENABLE_HIGHDPI")}, " +
                          $"backbuffer={GraphicsDevice.PresentationParameters.BackBufferWidth}x{GraphicsDevice.PresentationParameters.BackBufferHeight}, " +
                          $"client={Window.ClientBounds.Width}x{Window.ClientBounds.Height}");

        _spriteBatch = new SpriteBatch(GraphicsDevice);
        SpriteRenderer.SpriteBatch = _spriteBatch;

        _lighting.Time = _ctx.Clock;

        _pixelTexture = new Texture2D(GraphicsDevice, 1, 1);
        _pixelTexture.SetData(new[] { Color.White });

        TextureLoader.Instance.Initialize(GraphicsDevice);

        Runtime.Text.TextService.Initialize(GraphicsDevice,
            System.IO.Path.Combine(AppContext.BaseDirectory, "Content", "Fonts", "Pretendard-SemiBold.ttf"));
        Runtime.Text.TextService.Load(Runtime.Text.FontSlot.Dialogue,
            System.IO.Path.Combine(AppContext.BaseDirectory, "Content", "Fonts", "Galmuri9.ttf"));

        Runtime.Text.Loc.Load();

        Runtime.Story.StoryLibrary.LoadDefault();
        Runtime.Story.DialogueRunner.Bind(_ctx.Blackboard);

        Runtime.Rendering.LightingProfiles.Load();

#if DEBUG
        Runtime.Core.ContentHotReload.Watch(Runtime.Story.StoryLibrary.DefaultRoot,
            "*" + Runtime.Story.StoryLibrary.Extension, "dialogue", () =>
            {
                Runtime.Story.DialogueRunner.Stop();
                Runtime.Story.StoryLibrary.LoadDefault();
                Runtime.Story.StoryLibrary.Current.ReportEventProblems();
            });
        Runtime.Core.ContentHotReload.Watch(Runtime.Text.Loc.DefaultRoot, "*.strings", "labels",
            () => Runtime.Text.Loc.Load());

        Runtime.Core.ContentHotReload.Watch(
            System.IO.Path.Combine(Runtime.Assets.ContentPaths.Root, "Lighting"),
            "*.json", "lighting profiles", () =>
            {
                Runtime.Rendering.LightingProfiles.Clear();
                Runtime.Rendering.LightingProfiles.Load();
            });

        Runtime.Core.ContentHotReload.Watch(
            System.IO.Path.Combine(Runtime.Assets.ContentPaths.Root, "Particles"),
            "*.particle", "particle presets", () =>
            {
                Runtime.Particles.ParticlePresetCache.Clear();
                Runtime.Particles.ParticleEmitter.ResetWarnings();
            });
#endif

#if DEBUG
        AssetScanner.Scan("Content", promote: true);
#else
        AssetScanner.Scan("Content", promote: false);
#endif

        var player = LevelSetup.LoadStartRoom(_scene);
        GameSystems.Initialize(_scene, player);

        Runtime.Story.StoryLibrary.Current.ReportEventProblems();

        RoomFlow.LoadRoomImpl = LoadRoomForGameplay;

        Runtime.Cutscenes.CutsceneDirector.RoomLoader =
            (sceneId, spawn) => RoomFlow.LoadImmediate(sceneId, spawn, _ctx.Scene, _camera);

        Runtime.Cutscenes.CutsceneDirector.PersistFacing = ActorFacing.Set;

        GameFlow.Bind(_ctx, _camera);

#if DEBUG
        TitleMenu.QuitRequested = () => _editor.RequestQuit();
        WirePauseMenu();
#else
        TitleMenu.QuitRequested = Exit;
        WirePauseMenu();
#endif

        _camera.Target = player;

#if DEBUG
        _editor.RuntimeCamera = _camera;

        _editor.RuntimeContext = _ctx;

        _editor.GameViewRequested = () => { if (_editorVisible) ToggleGameView(); };

        var tilemap = LevelSetup.GetTilemap(_scene);
        if (tilemap != null) _editor.SetTilemap(tilemap);

        if (player != null) _editor.State.Select(player);

        if (!_editor.TryLoadLastScene() && LevelSetup.StartRoomPath() is { } startRoomPath)
            _editor.OpenStartScene(startRoomPath);

        if (Environment.GetEnvironmentVariable("PIXELCORE_SHOT_WEATHER") is { Length: > 0 } shotWeather)
            GameFlow.Weather = shotWeather;

        var shotHour = Environment.GetEnvironmentVariable("PIXELCORE_SHOT_HOUR");
        if (!string.IsNullOrEmpty(shotHour) &&
            float.TryParse(shotHour, System.Globalization.CultureInfo.InvariantCulture, out var hour))
        {
            _ctx.Clock.Hour = hour;
        }

        if (Environment.GetEnvironmentVariable("PIXELCORE_TITLE") == "1")
        {
            _editorVisible = false;
            _editor.State.SetMode(EditorMode.Play);
            _scene.Clear();
            _scene.Name = "";
            TitleMenu.Open();
        }

        var shotScenePath = Environment.GetEnvironmentVariable("PIXELCORE_SHOT_SCENE");
        if (!string.IsNullOrEmpty(shotScenePath))
        {
            var shotData = Runtime.Serialization.SceneSerializer.LoadFromFile(shotScenePath);
            if (shotData != null)
            {
                Runtime.Serialization.SceneSerializer.FromData(_scene, shotData);
                Console.WriteLine($"[Scene] ShotScene: {shotScenePath}");
            }
            else Console.WriteLine($"[Scene] ShotScene failed to load: {shotScenePath}");
        }

#else
        _scene.Clear();
        _scene.Name = "";
        TitleMenu.Open();
#endif

        _camera.PixelPerfect = true;
        _camera.GameWidth = 320;
        _camera.GameHeight = 180;

        _camera.Deadzone = new Vector2(16, 16);

        Console.WriteLine($"[Camera] FollowSpeed {_camera.FollowSpeed:0.00} · Deadzone {_camera.Deadzone.X:0}"
            + $" · Settle {_camera.SettleDeadband:0.#}"
            + $" · rounding {(Runtime.Core.Camera.StateRoundingInDeadzone ? "on" : "off")}"
            + $" · subpixel {(SubpixelPan ? "on" : "off")}");

#if DEBUG
        Editor.WindowBounds.Restore(Window.Handle);
#endif
    }

#if DEBUG
#endif

    private float _bbSyncSeconds;
    private const float BbSyncDelay = 0.25f;

    protected override void Update(GameTime gameTime)
    {
#if DEBUG
        using var _prof = Editor.FrameProf.Measure("update.total");
#endif
        SDL3.SDL.SDL_GetWindowSizeInPixels(Window.Handle, out int drawW, out int drawH);
        var pp = GraphicsDevice.PresentationParameters;
        if (drawW > 0 && drawH > 0 && (drawW != pp.BackBufferWidth || drawH != pp.BackBufferHeight))
        {
            _bbSyncSeconds += (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_bbSyncSeconds >= BbSyncDelay)
            {
                _graphics.PreferredBackBufferWidth = drawW;
                _graphics.PreferredBackBufferHeight = drawH;
                _graphics.ApplyChanges();
                _bbSyncSeconds = 0f;
            }
        }
        else
        {
            _bbSyncSeconds = 0f;
        }

#if DEBUG
        Runtime.Core.ContentHotReload.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
        Runtime.Core.CodeHotReload.Update();
#endif

        Input.Update();
        Runtime.Core.Gamepad.Update();
#if DEBUG
        if (TraceWalk != null)
        {
            if (TraceWalk.Contains("right")) Input.DebugSetKey(Input.SDL_SCANCODE_RIGHT, true);
            if (TraceWalk.Contains("left")) Input.DebugSetKey(Input.SDL_SCANCODE_LEFT, true);
            if (TraceWalk.Contains("down")) Input.DebugSetKey(Input.SDL_SCANCODE_DOWN, true);
            if (TraceWalk.Contains("up")) Input.DebugSetKey(Input.SDL_SCANCODE_UP, true);
            if (TraceWalk.Contains("attack"))
                Input.DebugSetKey(Input.SDL_SCANCODE_J, (int)(_shotSeconds * 60f) % 24 < 2);
            if (TraceWalk.Contains("pause") && _shotSeconds > 1f)
                Input.DebugSetKey(Input.SDL_SCANCODE_ESCAPE, true);
        }
#endif

#if DEBUG
        Editor.WindowChrome.Pump();

        bool wantTextInput = _editorVisible && ImGui.GetIO().WantTextInput;
        _textInputOffFrames = wantTextInput ? 0 : _textInputOffFrames + 1;
        bool shouldBeActive = wantTextInput || (_textInputActive && _textInputOffFrames < 4);
        if (shouldBeActive != _textInputActive)
        {
            if (shouldBeActive) TextInputEXT.StartTextInput();
            else TextInputEXT.StopTextInput();
            _textInputActive = shouldBeActive;
        }
#endif

        Runtime.Rendering.ScreenFader.Update((float)gameTime.ElapsedGameTime.TotalSeconds);

        Runtime.Cutscenes.CinematicBars.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
        Runtime.Cutscenes.CutsceneDirector.Update((float)gameTime.ElapsedGameTime.TotalSeconds);

        if (Runtime.Cutscenes.CutsceneRegression.Enabled)
        {
            Runtime.Cutscenes.CutsceneRegression.Update(
                (float)gameTime.ElapsedGameTime.TotalSeconds, _scene?.Name ?? "?");
            if (Runtime.Cutscenes.CutsceneRegression.ExitRequested) Exit();
        }

        var keyboard = Keyboard.GetState();

#if DEBUG
        if (RoomTest != null && _roomTestSeconds < 0.5f)
        {
            _roomTestSeconds += (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_roomTestSeconds >= 0.5f)
            {
                var parts = RoomTest.Split(':');
                var testId = Runtime.Assets.AssetRegistry.Instance.FindSceneIdByName(parts[0], out var cands);
                if (testId == null)
                    Console.Error.WriteLine(
                        $"[RoomTest] ✘ room '{parts[0]}' is not in the registry" +
                        (cands.Count > 1 ? $" - {cands.Count} entries share that name" : ""));
                else
                    RoomFlow.Transition(testId, parts.Length > 1 ? parts[1] : "", source: "PIXELCORE_ROOMTEST");
            }
        }

        if (CutsceneTest != null && _cutsceneTestSeconds < 1f)
        {
            float before = _cutsceneTestSeconds;
            _cutsceneTestSeconds += (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (before < 0.5f && _cutsceneTestSeconds >= 0.5f) _editor.State.SetMode(EditorMode.Play);
            if (_cutsceneTestSeconds >= 1f) Runtime.Cutscenes.CutsceneDirector.Play(CutsceneTest);
        }

        if (SaveHarness.Enabled)
        {
            _saveTestSeconds += (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_saveTestSeconds >= 0.5f && !_saveTestEntered)
            {
                _saveTestEntered = true;
                _editor.State.SetMode(EditorMode.Play);
            }
            if (_saveTestEntered)
                SaveHarness.Update((float)gameTime.ElapsedGameTime.TotalSeconds, _ctx);
            if (SaveHarness.ExitRequested) Exit();
        }

        if (keyboard.IsKeyDown(Keys.OemTilde) && _prevKeyboard.IsKeyUp(Keys.OemTilde))
        {
            ToggleGameView();
        }

        if (keyboard.IsKeyDown(Keys.F1) && _prevKeyboard.IsKeyUp(Keys.F1))
        {
            _editor.State.ShowDebugOverlay = !_editor.State.ShowDebugOverlay;
        }
#endif

        bool typingInEditor = false;
#if DEBUG
        typingInEditor = _editorVisible && ImGui.GetIO().WantTextInput;
#endif

        bool altDown = Input.IsKeyDown(Input.SDL_SCANCODE_LALT) || Input.IsKeyDown(Input.SDL_SCANCODE_RALT);
        bool enterPressed = Input.IsKeyPressed(Input.SDL_SCANCODE_RETURN)
                         || Input.IsKeyPressed(Input.SDL_SCANCODE_KP_ENTER);
        if (altDown && enterPressed && !typingInEditor)
        {
            _graphics.IsFullScreen = !_graphics.IsFullScreen;
            _graphics.ApplyChanges();
            Runtime.Core.GameSettings.Current.Fullscreen = _graphics.IsFullScreen;
            Runtime.Core.GameSettings.MarkDirty();
        }
        bool escPressed = keyboard.IsKeyDown(Keys.Escape) && _prevKeyboard.IsKeyUp(Keys.Escape);
        _prevKeyboard = keyboard;

#if DEBUG
        if (escPressed && _editorVisible && !ImGui.GetIO().WantCaptureKeyboard)
            _editor.RequestQuit();

        if (Editor.WindowGuard.ConsumeCloseRequest() || Editor.WindowGuard.ConsumeQuitRequest())
            _editor.RequestQuit();
#endif

        float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

#if DEBUG
        _shotSeconds += deltaTime;

        _ctx.Time.Paused = _editor.State.PlayPaused;
#endif

        float gameplayDt = _ctx.Time.GameplayDelta(deltaTime);

        AudioManager.Instance.SetPaused(_ctx.Time.Paused);
        AudioManager.Instance.ListenerPosition =
            AudioManager.Instance.ListenerOverride
            ?? _listenerTransform?.Position
            ?? _camera.Position;
        AudioManager.Instance.Update(deltaTime);
        ApplySceneAudio();

#if DEBUG
        if (_editorVisible && IsActive)
        {
            _editor.HandleInput(_camera);
        }

        bool isPlayMode = _editor.State.IsPlayMode && !_editor.TilemapEditor.IsEditing;
#else
        bool isPlayMode = true;
#endif
        bool focusAwake = IsActive || TraceWalk != null || Runtime.Cutscenes.CutsceneRegression.Enabled;
        isPlayMode &= focusAwake;

        if (Runtime.UI.TitleScreen.Active && focusAwake) Runtime.UI.TitleScreen.Update();
        isPlayMode &= !Runtime.UI.TitleScreen.Active;

        bool canPause = isPlayMode
                        && !Runtime.Cutscenes.CutsceneDirector.IsPlaying
                        && !Runtime.UI.DialogueBox.Active;
        if (PauseMenu.Tick(canPause, focusAwake)) isPlayMode = false;

        _lighting.AllowDeferredSwitching = isPlayMode;

        if (isPlayMode)
        {
#if DEBUG
            try
            {
#endif
                GameSystems.Update(_scene, _camera, gameplayDt);

                bool monologueMouse = true;
#if DEBUG
                monologueMouse = !_editorVisible;
#endif
                Runtime.UI.MonologueScreen.Update(focusAwake, monologueMouse);

                _ctx.Save.Update(deltaTime);
#if DEBUG
            }
            catch (Exception ex)
            {
                OnGameFault("update.gameplay", ex);
                isPlayMode = false;
            }
#endif
        }

        if (isPlayMode) Runtime.Core.Rumble.Update(gameplayDt);
        else Runtime.Core.Rumble.Cancel();

        var player = _scene.FindPlayer();

        _listenerTransform = player?.GetComponent<Transform>();

        bool gameOwnsCamera = true;
#if DEBUG
        gameOwnsCamera = _editor.State.IsPlayMode && !_editor.State.PlayCameraDetached;
#endif
        ApplyCameraFraming(gameOwnsCamera, player);

        bool cameraTicked = false;
        if (isPlayMode)
        {
            _physicsAccumulator += gameplayDt;
            int safety = 0;
#if DEBUG
            try
            {
#endif
                while (_physicsAccumulator >= FixedDt && safety++ < 5)
                {
                    GameSystems.FixedTick(FixedDt);
                    _scene.FixedTick(FixedDt);
                    _physics.Update(FixedDt);
                    _camera.Update(FixedDt);
                    cameraTicked = true;
                    _physicsAccumulator -= FixedDt;
                }
#if DEBUG
            }
            catch (Exception ex)
            {
                _physicsAccumulator = 0f;
                OnGameFault("update.fixed", ex);
            }
#endif
        }
        if (!cameraTicked)
            _camera.Update(deltaTime);

        if (focusAwake) _ctx.Coroutines.Update(gameplayDt);

        _ctx.Clock.Update(gameplayDt);
        _sky.Update(gameplayDt);

#if DEBUG
        try
        {
#endif
            _scene.UpdatePostBlend(deltaTime);

            if (isPlayMode) _scene.UpdateAmbient(gameplayDt);

            if (isPlayMode) _scene.Update(gameplayDt);
            else _scene.FlushPending();
#if DEBUG
        }
        catch (Exception ex) { OnGameFault("update.scene", ex); }
#endif

        base.Update(gameTime);
    }

#if DEBUG
    private void ToggleGameView()
    {
        Runtime.UI.PauseScreen.Close();

        if (_editorVisible)
        {
            if (_editor.State.IsEditMode)
            {
                _editor.State.SetMode(EditorMode.Play);
                _playEnteredByBacktick = true;
            }
            _editor.State.PlayEditUnlocked = false;
            _editor.State.PlayCameraDetached = false;
            _editor.State.PlayPaused = false;
            _editorVisible = false;
        }
        else
        {
            _editorVisible = true;
            if (_playEnteredByBacktick)
            {
                _playEnteredByBacktick = false;
                _editor.State.SetMode(EditorMode.Edit);
            }
        }
    }

    protected override void EndDraw()
    {
        using var _prof = Editor.FrameProf.Measure("present(vsync wait)");
        base.EndDraw();
    }
#endif

#if DEBUG

    private void OnGameFault(string surface, Exception ex)
    {
        if (GameLoopGuard.ShouldReport(surface, ex))
        {
            bool stopping = _editor.State.IsPlayMode;
            Console.Error.WriteLine($"[game exception] {surface} - {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace ?? "(no stack - an exception that was never thrown)");
            EditorConsole.Open = true;
            _editor.ToastError(stopping ? "Game exception - play stopped. Check the console" : "Game exception - check the console");
        }

        if (_editor.State.IsPlayMode) _editor.State.SetMode(EditorMode.Edit);
    }

    private void RecoverGraphicsState()
    {
        try { _spriteBatch.End(); } catch {  }
        GraphicsDevice.SetRenderTarget(null);
    }
#endif

    private Runtime.Rendering.ProfileFrame _profileFrame = Runtime.Rendering.ProfileFrame.Neutral;

    private Runtime.Rendering.PostProfile ProfilePost =>
        Runtime.Rendering.LightingProfiles.ModulatePost(_scene.Post);

    private Runtime.Rendering.PostProfile? ProfilePostFrom =>
        _scene.PostBlendFrom is { } f ? Runtime.Rendering.LightingProfiles.ModulatePost(f) : null;

    private Runtime.Rendering.PostProfile? ProfilePostTo =>
        _scene.PostBlendTo is { } t ? Runtime.Rendering.LightingProfiles.ModulatePost(t) : null;

    private Runtime.Rendering.ProfileFrame UpdateLightingProfile(float dt)
    {
        var sceneId = Gameplay.Systems.GameFlow.CurrentRoom().SceneId;
        var tag = Gameplay.Systems.GameFlow.CurrentLightingTag();
        Runtime.Rendering.LightingProfiles.Update(sceneId, tag, _ctx.Clock.Hour, dt);
        return Runtime.Rendering.LightingProfiles.Current;
    }

    private void DrawAtmosphereParticles(Matrix view, RasterizerState? scissor = null)
    {
        if (!_scene.HasAtmosphere()) return;

        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
            SamplerState.LinearClamp, null, scissor, null, view);
        bool anyAlpha = _scene.DrawAtmosphere(_spriteBatch, additive: false);
        _spriteBatch.End();

        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive,
            SamplerState.LinearClamp, null, scissor, null, view);
        bool anyAdd = _scene.DrawAtmosphere(_spriteBatch, additive: true);
        _spriteBatch.End();

        _ = anyAlpha; _ = anyAdd;
    }

    protected override void Draw(GameTime gameTime)
    {
        _profileFrame = UpdateLightingProfile((float)gameTime.ElapsedGameTime.TotalSeconds);

        var baseAmbient = _scene.Exterior ? _ctx.Clock.AmbientColor : _scene.EffectiveAmbient;
        _lighting.AmbientOverride = (_profileFrame.IsNeutral && !_scene.Exterior)
            ? (Color?)null
            : _profileFrame.ModulateAmbient(baseAmbient);
        _lighting.IntensityScale = _profileFrame.LightScale;

        _totalSeconds = (float)gameTime.TotalGameTime.TotalSeconds;

#if DEBUG
        if (_editorVisible)
        {
            DrawEditorMode(gameTime);
        }
        else
        {
            try { DrawGameMode(); }
            catch (Exception ex) { RecoverGraphicsState(); OnGameFault("draw.world", ex); }
            _editor.BeginLayout(gameTime);
            float dbgToLogical = ImGui.GetIO().DisplaySize.X > 0f
                ? ImGui.GetIO().DisplaySize.X / GraphicsDevice.PresentationParameters.BackBufferWidth
                : 1f;
            var dbgMap = _dbgWorldToScreen;
            Editor.DebugOverlay.Draw(_editor.State, _camera,
                dbgMap == null ? null : w => dbgMap(w) * dbgToLogical,
                _dbgUiScale * dbgToLogical);
            _editor.EndLayout();
        }

        CaptureShotIfRequested();
        if (Editor.FrameProf.Enabled) Editor.FrameProf.ReportDisplayMode(Window.Handle);
        Editor.FrameProf.Count("#active", IsActive ? 1 : 0);
        if (Editor.FrameProf.EndFrame()) Exit();
#else
        DrawGameMode();
#endif

        base.Draw(gameTime);
    }

#if DEBUG
    private Func<Vector2, Vector2>? _dbgWorldToScreen;
    private float _dbgUiScale;
    private (Func<Vector2, Vector2> W2L, Func<Vector2, Vector2> L2W, float Scale)? _lastComposeMapping;
#endif

#if DEBUG
    private static Color CanvasClearColor()
    {
        var v = PixelCore.Editor.EditorTheme.ViewportClear;
        return new Color(v.X, v.Y, v.Z);
    }

    private int _shotFrameCounter;
    private static readonly bool ShotRequested =
        Environment.GetEnvironmentVariable("PIXELCORE_SHOT") == "1";

    private static readonly int ShotFrame =
        int.TryParse(Environment.GetEnvironmentVariable("PIXELCORE_SHOT_FRAME"), out var f) && f > 0 ? f : 60;

    private static readonly float ShotAfterSeconds =
        float.TryParse(Environment.GetEnvironmentVariable("PIXELCORE_SHOT_AFTER"), out var sec) && sec > 0f ? sec : 0f;

    private float _shotSeconds;

    private const int SettleFrames = 90;

    private void CaptureShotIfRequested()
    {
        if (!ShotRequested) return;

        bool fire = ShotAfterSeconds > 0f
            ? _shotSeconds >= ShotAfterSeconds && _shotFrameCounter++ == 0
            : ++_shotFrameCounter == ShotFrame;
        if (fire)
        {
            var shotTheme = Environment.GetEnvironmentVariable("PIXELCORE_SHOT_THEME");
            if (!string.IsNullOrEmpty(shotTheme))
            {
                EditorTheme.Mode = shotTheme == "light" ? ThemeMode.Light : ThemeMode.Dark;
                EditorTheme.Apply();
            }

            var selectName = Environment.GetEnvironmentVariable("PIXELCORE_SHOT_SELECT");
            if (!string.IsNullOrEmpty(selectName) && _scene.FindEntity(selectName) is { } target)
                _editor.State.Select(target);

            if (Environment.GetEnvironmentVariable("PIXELCORE_SHOT_PLAY") == "1")
                _editor.State.SetMode(EditorMode.Play);

            var playEdit = Environment.GetEnvironmentVariable("PIXELCORE_SHOT_PLAYEDIT");
            if (playEdit is "unlock" or "detach")
            {
                _editor.State.PlayEditUnlocked = true;
                if (playEdit == "detach") _editor.State.PlayCameraDetached = true;
            }

            if (Environment.GetEnvironmentVariable("PIXELCORE_SHOT_COLLIDERS") == "1")
            {
                _editor.State.ShowColliders = true;
                _editor.State.ShowDebugOverlay = true;
            }

            if (Environment.GetEnvironmentVariable("PIXELCORE_SHOT_TILEMAP") == "1")
            {
                _editor.TilemapEditor.IsOpen = true;
                _editor.TilemapEditor.IsEditing = true;
            }

            if (Environment.GetEnvironmentVariable("PIXELCORE_SHOT_EDIT_COLLIDER") == "1")
                _editor.State.EditingCollider = _editor.State.SelectedEntity?.GetComponent<Collider2D>();

            if (Environment.GetEnvironmentVariable("PIXELCORE_SHOT_FRAMING") == "1")
            {
                _camera.Zoom = 1f;
                if (!_scene.CameraBoundsEnabled && !_scene.CameraFixed)
                {
                    var c = _camera.Position;
                    int hw = (int)(_camera.GameWidth * 0.7f), hh = (int)(_camera.GameHeight * 0.7f);
                    _scene.CameraBoundsEnabled = true;
                    _scene.CameraBounds = new Rectangle((int)c.X - hw, (int)c.Y - hh, hw * 2, hh * 2);
                    _scene.CameraFixed = true;
                    _scene.CameraFixedPos = c;
                }
                _editor.State.ClearSelection();
                _editor.State.EditingCameraFraming = true;
            }

            var shotDialogue = Environment.GetEnvironmentVariable("PIXELCORE_SHOT_DIALOGUE");
            if (!string.IsNullOrEmpty(shotDialogue))
                Runtime.UI.DialogueBox.Show("Old Man", shotDialogue);

            var shotAnim = Environment.GetEnvironmentVariable("PIXELCORE_SHOT_ANIM");
            if (!string.IsNullOrEmpty(shotAnim))
                _editor.AnimationEditor.RequestOpen(shotAnim);

            var shotSprite = Environment.GetEnvironmentVariable("PIXELCORE_SHOT_SPRITE");
            if (!string.IsNullOrEmpty(shotSprite))
            {
                _editor.SpriteEditor.OpenTexture(shotSprite);
                if (int.TryParse(Environment.GetEnvironmentVariable("PIXELCORE_SHOT_SLICE"), out int si))
                    _editor.SpriteEditor.SelectSliceForShot(si);
            }

            var assetQuery = Environment.GetEnvironmentVariable("PIXELCORE_SHOT_ASSET");
            if (!string.IsNullOrEmpty(assetQuery))
                _editor.SelectAssetForShot(assetQuery);

            var shotPing = Environment.GetEnvironmentVariable("PIXELCORE_SHOT_PING");
            if (!string.IsNullOrEmpty(shotPing))
                _editor.RevealAssetForShot(shotPing);
        }
        if (ShotAfterSeconds > 0f) { if (!fire) return; }
        else if (_shotFrameCounter < SettleFrames) return;

        try
        {
            int w = GraphicsDevice.PresentationParameters.BackBufferWidth;
            int h = GraphicsDevice.PresentationParameters.BackBufferHeight;
            var data = new Color[w * h];
            GraphicsDevice.GetBackBufferData(data);
            using var tex = new Texture2D(GraphicsDevice, w, h);
            tex.SetData(data);
            using var fs = System.IO.File.Create("/tmp/pixelcore_shot.png");
            tex.SaveAsPng(fs, w, h);
            Console.WriteLine("[Shot] saved /tmp/pixelcore_shot.png");
        }
        catch (Exception ex) { Console.WriteLine($"[Shot] failed: {ex.Message}"); }

        Exit();
    }
#endif

#if DEBUG
    private void DrawEditorMode(GameTime gameTime)
    {
        var sceneTarget = _editor.GetSceneRenderTarget();
        if (sceneTarget != null)
        {
            int width = sceneTarget.Width;
            int height = sceneTarget.Height;

            float uiScale = _editor.SceneView.ViewScale;
            if (!(uiScale > 0f)) uiScale = 1f;
            int logicalW = Math.Max(1, (int)MathF.Round(width / uiScale));
            int logicalH = Math.Max(1, (int)MathF.Round(height / uiScale));

            ApplyViewZoom();

            bool playPixelPerfect = _editor.State.IsPlayMode && !_editor.TilemapEditor.IsEditing
                                    && !_editor.State.PlayCameraDetached
                                    && _camera.PixelPerfect
                                    && width >= _camera.GameWidth && height >= _camera.GameHeight;
            if (playPixelPerfect)
            {
                try
                {
                    RenderPixelPerfect(sceneTarget, width, height, stashDebugMapping: false);
                    _editor.SceneView.PlayCompose = _lastComposeMapping;
                }
                catch (Exception ex) { RecoverGraphicsState(); OnGameFault("draw.world", ex); }
                GraphicsDevice.SetRenderTarget(null);
                GraphicsDevice.Clear(CanvasClearColor());

                _editor.BeginLayout(gameTime);
                _editor.DrawUI();
                _editor.EndLayout();
                return;
            }
            _editor.SceneView.PlayCompose = null;

            try
            {
                _camera.UpdateViewport(logicalW, logicalH);

                var editorView = _camera.GetTransform() * Matrix.CreateScale(uiScale, uiScale, 1f);

                using (Editor.FrameProf.Measure("render.lights+shadows"))
                {
                    _lighting.PrepareLights(GraphicsDevice, _spriteBatch, _scene, editorView, width, height);
                    _shadow.PrepareShadows(GraphicsDevice, _scene, editorView, width, height);
                }

                var clearColor = _editor.State.IsEditMode ? Color.Transparent : _scene.BackColor;
                bool postShader = _post.UseShaderPath(GraphicsDevice, ProfilePost);

                GraphicsDevice.SetRenderTarget(postShader
                    ? _post.GetSourceTarget(GraphicsDevice, width, height)
                    : sceneTarget);
                GraphicsDevice.Clear(clearColor);
                DrawSky(width, height, _camera.Zoom * uiScale, _camera.Position);

                using (Editor.FrameProf.Measure("render.world"))
                {
                    DrawWorldWithShadows(editorView, width, height);

                    DrawAtmosphereParticles(editorView);

                    _lighting.Composite(_spriteBatch, width, height);
                }

                using (Editor.FrameProf.Measure("render.post"))
                if (postShader)
                {
                    var postSrc = _post.GetSourceTarget(GraphicsDevice, width, height);
                    _post.Prepare(GraphicsDevice, _spriteBatch, ProfilePost, postSrc,
                        ProfilePostFrom, ProfilePostTo, _scene.PostBlendT);
                    GraphicsDevice.SetRenderTarget(sceneTarget);
                    _post.Compose(_spriteBatch, ProfilePost, postSrc,
                        new Rectangle(0, 0, width, height), _totalSeconds);
                }
                else
                {
                    _post.ApplyFallback(GraphicsDevice, _spriteBatch, ProfilePost, new Rectangle(0, 0, width, height));
                }

                if (_editor.State.IsEditMode)
                {
                    _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null);
                    DrawSelectionGizmo(editorView, uiScale);
                    _spriteBatch.End();
                }
            }
            catch (Exception ex) { RecoverGraphicsState(); OnGameFault("draw.world", ex); }

            GraphicsDevice.SetRenderTarget(null);
        }

        GraphicsDevice.Clear(CanvasClearColor());

        using (Editor.FrameProf.Measure("ui.beginLayout")) _editor.BeginLayout(gameTime);
        using (Editor.FrameProf.Measure("ui.build")) _editor.DrawUI();
        using (Editor.FrameProf.Measure("ui.render")) _editor.EndLayout();
    }

    private static Rectangle WorldToScreenRect(Vector2 worldMin, Vector2 worldMax, Matrix view)
    {
        var tl = Vector2.Transform(worldMin, view);
        var br = Vector2.Transform(worldMax, view);
        return new Rectangle(
            (int)MathF.Round(tl.X), (int)MathF.Round(tl.Y),
            (int)MathF.Round(br.X - tl.X), (int)MathF.Round(br.Y - tl.Y));
    }

    private void DrawSelectionGizmo(Matrix view, float uiScale)
    {
        var gizmo = _editor.GetSelectedGizmo();
        if (gizmo == null) return;

        var (_, anchor, _) = gizmo.Value;

        var center = Vector2.Transform(anchor, view);
        center = new Vector2(MathF.Round(center.X), MathF.Round(center.Y));
        float arrowLength = Editor.Panels.SceneViewPanel.GIZMO_ARROW_LENGTH * uiScale;
        float arrowHeadSize = 7f * uiScale;
        int lineThickness = Math.Max(1, (int)MathF.Round(2f * uiScale));

        var dragAxis = _editor.GetDragAxis();
        var xColor = dragAxis == 1 ? Color.Yellow : new Color(226, 80, 65);
        var yColor = dragAxis == 2 ? Color.Yellow : new Color(120, 200, 90);

        DrawGizmoArrow(center, new Vector2(arrowLength, 0), xColor, lineThickness, arrowHeadSize);
        DrawGizmoArrow(center, new Vector2(0, -arrowLength), yColor, lineThickness, arrowHeadSize);
    }

    private void DrawRectOutline(Rectangle rect, Color color, int thickness)
    {
        _spriteBatch.Draw(_pixelTexture, new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
        _spriteBatch.Draw(_pixelTexture, new Rectangle(rect.X, rect.Bottom - thickness, rect.Width, thickness), color);
        _spriteBatch.Draw(_pixelTexture, new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
        _spriteBatch.Draw(_pixelTexture, new Rectangle(rect.Right - thickness, rect.Y, thickness, rect.Height), color);
    }

    private void DrawGizmoArrow(Vector2 start, Vector2 direction, Color color, int thickness, float headSize)
    {
        var end = start + direction;
        float length = direction.Length();
        float rotation = MathF.Atan2(direction.Y, direction.X);

        _spriteBatch.Draw(
            _pixelTexture,
            start,
            null,
            color,
            rotation,
            new Vector2(0, 0.5f),
            new Vector2(length - headSize, thickness),
            SpriteEffects.None,
            0
        );

        var tipStart = start + Vector2.Normalize(direction) * (length - headSize);
        for (int i = 0; i < 3; i++)
        {
            float t = i / 2f;
            var lineStart = tipStart + Vector2.Normalize(direction) * (headSize * t);
            float lineHalfWidth = headSize * 0.5f * (1 - t);

            var perpendicular = new Vector2(-direction.Y, direction.X);
            perpendicular.Normalize();

            var p1 = lineStart + perpendicular * lineHalfWidth;
            var p2 = lineStart - perpendicular * lineHalfWidth;

            DrawLine(p1, p2, color, thickness);
        }
    }

    private void DrawLine(Vector2 start, Vector2 end, Color color, int thickness)
    {
        var direction = end - start;
        float length = direction.Length();
        if (length < 0.1f) return;

        float rotation = MathF.Atan2(direction.Y, direction.X);

        _spriteBatch.Draw(
            _pixelTexture,
            start,
            null,
            color,
            rotation,
            new Vector2(0, 0.5f),
            new Vector2(length, thickness),
            SpriteEffects.None,
            0
        );
    }
#endif

    private void DrawWorldWithShadows(Matrix view, int width, int height, IRenderable? split = null)
    {
        if (_shadow.HasShadows)
        {
            float shadowOpacity = Runtime.Rendering.ShadowRenderer.BaseOpacity * _profileFrame.ShadowScale;

            void DrawBand(int min, int maxExcl)
            {
                _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, view);
                _scene.Draw(_spriteBatch, minLayer: min, maxLayerExclusive: maxExcl, split: split, belowSplit: true);
                _spriteBatch.End();
            }

            DrawBand(int.MinValue, RenderLayers.BelowEntities);
            _shadow.Composite(_spriteBatch, Runtime.Rendering.ShadowBand.Below, width, height, shadowOpacity);

            DrawBand(RenderLayers.BelowEntities, RenderLayers.Entities);
            _shadow.Composite(_spriteBatch, Runtime.Rendering.ShadowBand.Entities, width, height, shadowOpacity);

            DrawBand(RenderLayers.Entities, int.MaxValue);
        }
        else
        {
            _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, view);
            _scene.Draw(_spriteBatch, split: split, belowSplit: true);
            _spriteBatch.End();
        }

        DrawAdditivePass(view, split, belowSplit: true);
    }

    private void DrawAdditivePass(Matrix view, IRenderable? split, bool belowSplit)
    {
        if (!_scene.HasAdditive(split: split, belowSplit: belowSplit)) return;

        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.PointClamp, null, null, null, view);
        _scene.DrawAdditive(_spriteBatch, split: split, belowSplit: belowSplit);
        _spriteBatch.End();
    }

    private bool LoadRoomForGameplay(string sceneId)
    {
        var path = Runtime.Components.SceneInstance.ResolveSceneId(sceneId);
        if (string.IsNullOrEmpty(path))
        {
            Console.Error.WriteLine(
                $"[Scene] ✘ room load failed - scene asset id '{sceneId}' is not in the registry " +
                "(the scene was deleted, or assets.json has not been scanned)");
            return false;
        }

        var data = Runtime.Serialization.SceneSerializer.LoadFromFile(path);
        if (data == null)
        {
            Console.Error.WriteLine($"[Scene] ✘ room load failed - could not read the scene file: {path} (id {sceneId})");
            return false;
        }

        Runtime.Serialization.SceneSerializer.FromData(_scene, data);
        _scene.Name = data.Name;

        Console.WriteLine($"[Scene] Room: {path}");
        return true;
    }

    private void DrawGameMode()
    {
        if (Runtime.UI.TitleScreen.Active) { DrawTitleOnly(); return; }

        if (_camera.PixelPerfect)
        {
            RenderPixelPerfect(finalTarget: null,
                GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height,
                stashDebugMapping: true);
        }
        else
        {
            int vw = GraphicsDevice.Viewport.Width;
            int vh = GraphicsDevice.Viewport.Height;
            _camera.UpdateViewport(vw, vh);
            _lighting.PrepareLights(GraphicsDevice, _spriteBatch, _scene, _camera.GetTransform(), vw, vh);
            _shadow.PrepareShadows(GraphicsDevice, _scene, _camera.GetTransform(), vw, vh);

            bool postShader = _post.UseShaderPath(GraphicsDevice, ProfilePost);
            if (postShader)
                GraphicsDevice.SetRenderTarget(_post.GetSourceTarget(GraphicsDevice, vw, vh));

            GraphicsDevice.Clear(_scene.BackColor);
            DrawSky(vw, vh, _camera.Zoom, _camera.Position);

            var freeView = _camera.GetTransform();
            DrawWorldWithShadows(freeView, vw, vh);

            DrawAtmosphereParticles(freeView);
            _lighting.Composite(_spriteBatch, vw, vh);

            if (postShader)
            {
                var postSrc = _post.GetSourceTarget(GraphicsDevice, vw, vh);
                _post.Prepare(GraphicsDevice, _spriteBatch, ProfilePost, postSrc,
                    ProfilePostFrom, ProfilePostTo, _scene.PostBlendT);
                GraphicsDevice.SetRenderTarget(null);
                _post.Compose(_spriteBatch, ProfilePost, postSrc, new Rectangle(0, 0, vw, vh),
                    _totalSeconds);
            }
            else
            {
                _post.ApplyFallback(GraphicsDevice, _spriteBatch, ProfilePost, new Rectangle(0, 0, vw, vh));
            }

            Func<Vector2, Vector2> w2sFree = w => Vector2.Transform(w, freeView);
            float uiS = vh / (float)_camera.GameHeight;
            float areaW = _camera.GameWidth * uiS;
            Gameplay.Combat.HealthHud.DrawScreen(_spriteBatch, _scene,
                new Rectangle((int)((vw - areaW) / 2f), 0, (int)areaW, vh), uiS);
            InteractionPrompt.DrawScreen(_spriteBatch, _scene, w2sFree, Runtime.UI.UiScale.For(vh));
            Runtime.UI.DialogueBox.DrawScreen(_spriteBatch,
                new Rectangle((int)((vw - areaW) / 2f), 0, (int)areaW, vh), uiS,
                scene: _scene, worldToScreen: w2sFree);
#if DEBUG
            _dbgWorldToScreen = w2sFree; _dbgUiScale = _camera.Zoom;
#endif

            Runtime.Cutscenes.CinematicBars.Draw(_spriteBatch, _pixelTexture, vw, vh);

            Runtime.UI.MonologueScreen.Draw(_spriteBatch, new Rectangle(0, 0, vw, vh),
                Runtime.UI.UiScale.For(vh));

            Runtime.UI.PauseScreen.Draw(_spriteBatch, new Rectangle(0, 0, vw, vh),
                vh / (float)_camera.GameHeight);

            Runtime.Rendering.ScreenFader.Draw(_spriteBatch, _pixelTexture, vw, vh);
        }
    }

    private void WirePauseMenu()
    {
        PauseMenu.ReturnToTitleRequested = () =>
        {
            Runtime.UI.PauseScreen.Close();
            PauseMenu.Closed();
            TitleMenu.Open();
        };
        PauseMenu.FullscreenRequested = full =>
        {
            if (_graphics.IsFullScreen == full) return;
            _graphics.IsFullScreen = full;
            _graphics.ApplyChanges();
        };
    }

    private void DrawTitleOnly()
    {
        GraphicsDevice.SetRenderTarget(null);
        int vw = GraphicsDevice.Viewport.Width;
        int vh = GraphicsDevice.Viewport.Height;
        GraphicsDevice.Clear(Runtime.UI.TitleScreen.BackColor);

        float uiS = _camera.GameHeight > 0 ? vh / (float)_camera.GameHeight : 1f;
        Runtime.UI.TitleScreen.Draw(_spriteBatch, new Rectangle(0, 0, vw, vh), uiS);
        Runtime.Rendering.ScreenFader.Draw(_spriteBatch, _pixelTexture, vw, vh);
    }

    private void RenderPixelPerfect(RenderTarget2D? finalTarget, int vw, int vh, bool stashDebugMapping)
    {
        {
            EnsureGameRenderTarget();

            float prevZoom = _camera.Zoom;
            _camera.Zoom = 1f;

            ApplyViewZoom();
            EnsureGameRenderTarget();

            int bledW = _camera.GameWidth + CameraBleed * 2;
            int bledH = _camera.GameHeight + CameraBleed * 2;

            var camPos = _camera.Position;

            bool fractional = MathF.Abs(_scene.ViewZoom - 1f) > 0.0001f;
            float scale = MathF.Min(vw / (float)_camera.GameWidth, vh / (float)_camera.GameHeight);
            if (!fractional) scale = MathF.Max(1f, MathF.Floor(scale));

            float destW = _camera.GameWidth * scale, destH = _camera.GameHeight * scale;
            float destFX = MathF.Floor((vw - destW) * 0.5f), destFY = MathF.Floor((vh - destH) * 0.5f);
            var dest = new Rectangle(
                (int)MathF.Round(destFX), (int)MathF.Round(destFY),
                (int)MathF.Round(destW), (int)MathF.Round(destH));

            _camera.UpdateViewport(bledW, bledH);

            var lightView =
                Matrix.CreateTranslation(-camPos.X - _camera.ShakeOffset.X, -camPos.Y - _camera.ShakeOffset.Y, 0f) *
                Matrix.CreateScale(scale, scale, 1f) *
                Matrix.CreateTranslation(dest.Center.X, dest.Center.Y, 0f);
            _lighting.PrepareLights(GraphicsDevice, _spriteBatch, _scene, lightView, vw, vh);

            _shadow.PrepareShadows(GraphicsDevice, _scene, _camera.GetTransform(), bledW, bledH);

            var smooth = GetSmoothFollowRenderable();

            GraphicsDevice.SetRenderTarget(_gameRenderTarget);
            GraphicsDevice.Clear(_scene.BackColor);
            DrawSky(bledW, bledH, 1f, camPos);

            var rtView = _camera.GetTransform();
            DrawWorldWithShadows(rtView, bledW, bledH, smooth);

            if (smooth != null)
            {
                GraphicsDevice.SetRenderTarget(_gameRenderTargetAbove);
                GraphicsDevice.Clear(Color.Transparent);
                _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, rtView);
                _scene.Draw(_spriteBatch, split: smooth, belowSplit: false);
                _spriteBatch.End();

                DrawAdditivePass(rtView, smooth, belowSplit: false);
            }

            bool postShader = _post.UseShaderPath(GraphicsDevice, ProfilePost);
            var composeTarget = postShader ? _post.GetSourceTarget(GraphicsDevice, vw, vh) : finalTarget;
            GraphicsDevice.SetRenderTarget(composeTarget);

            GraphicsDevice.Clear(_scene.BackColor);

            float ox = SubpixelPan ? (MathF.Floor(camPos.X + 0.5f) - camPos.X) * scale : 0f;
            float oy = SubpixelPan ? (MathF.Floor(camPos.Y + 0.5f) - camPos.Y) * scale : 0f;
            var bledPos = new Vector2(
                destFX - CameraBleed * scale + ox,
                destFY - CameraBleed * scale + oy);

            if (!fractional || !PixZoomContinuousCompose)
                bledPos = new Vector2(MathF.Round(bledPos.X), MathF.Round(bledPos.Y));

            _scissorState ??= new RasterizerState { ScissorTestEnable = true };
            var prevScissor = GraphicsDevice.ScissorRectangle;
            GraphicsDevice.ScissorRectangle = dest;

            Effect? PrepareBlit() => fractional ? PreparePixZoom(bledW, bledH, scale, vw, vh) : null;

            var pixZoom = PrepareBlit();
            var blitFx = pixZoom;
            var blitSampler = pixZoom != null ? SamplerState.LinearClamp : SamplerState.PointClamp;

            _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, blitSampler, null, _scissorState, blitFx);
            _spriteBatch.Draw(_gameRenderTarget, bledPos, null, Color.White, 0f,
                Vector2.Zero, scale, SpriteEffects.None, 0f);
            _spriteBatch.End();

            if (smooth != null)
            {
                var tt = smooth.Entity.GetComponent<Transform>();
                if (tt != null)
                {
                    var bledHalf = new Vector2(MathF.Floor(bledW / 2f), MathF.Floor(bledH / 2f));
                    var rtTopLeftWorld = MathF.Floor(camPos.X + 0.5f) * Vector2.UnitX
                                       + MathF.Floor(camPos.Y + 0.5f) * Vector2.UnitY
                                       + _camera.ShakeOffset - bledHalf;

                    var smoothView =
                        Matrix.CreateTranslation(-rtTopLeftWorld.X, -rtTopLeftWorld.Y, 0f) *
                        Matrix.CreateScale(scale, scale, 1f) *
                        Matrix.CreateTranslation(bledPos.X, bledPos.Y, 0f);

                    PixelTrace(camPos, bledPos, tt.Position,
                        (tt.Position - rtTopLeftWorld) * scale, scale);

                    Effect? smoothFx = null;
                    if (fractional && smooth.EffectiveTexture is { } smoothTex)
                        smoothFx = PreparePixZoom(smoothTex.Width, smoothTex.Height, scale,
                            smoothView * Matrix.CreateOrthographicOffCenter(0, vw, vh, 0, 0, 1));
                    _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                        smoothFx != null ? SamplerState.LinearClamp : SamplerState.PointClamp,
                        null, _scissorState, smoothFx, smoothView);
                    smooth.RenderSmooth(_spriteBatch, tt.Position, rtTopLeftWorld, scale,
                        snapToScreenPixel: !fractional);
                    _spriteBatch.End();
                }

                PrepareBlit();
                _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, blitSampler, null, _scissorState, blitFx);
                _spriteBatch.Draw(_gameRenderTargetAbove, bledPos, null, Color.White, 0f,
                    Vector2.Zero, scale, SpriteEffects.None, 0f);
                _spriteBatch.End();
            }

            DrawAtmosphereParticles(lightView, _scissorState);

            _lighting.Composite(_spriteBatch, vw, vh, _scissorState);

            if (postShader)
            {
                var postSrc = _post.GetSourceTarget(GraphicsDevice, vw, vh);
                _post.Prepare(GraphicsDevice, _spriteBatch, ProfilePost, postSrc,
                    ProfilePostFrom, ProfilePostTo, _scene.PostBlendT);
                GraphicsDevice.SetRenderTarget(finalTarget);
                GraphicsDevice.Clear(_scene.BackColor);
                GraphicsDevice.ScissorRectangle = dest;
                _post.Compose(_spriteBatch, ProfilePost, postSrc, dest, _totalSeconds, _scissorState);
            }
            else
            {
                _post.ApplyFallback(GraphicsDevice, _spriteBatch, ProfilePost, dest, _scissorState);
            }

            Func<Vector2, Vector2> w2s = w => bledPos + Vector2.Transform(w, rtView) * scale;
            Gameplay.Combat.HealthHud.DrawScreen(_spriteBatch, _scene, dest, scale, _scissorState);
            InteractionPrompt.DrawScreen(_spriteBatch, _scene, w2s, Runtime.UI.UiScale.For(dest.Height), _scissorState);
            Runtime.UI.DialogueBox.DrawScreen(_spriteBatch, dest, scale, _scissorState,
                scene: _scene, worldToScreen: w2s);
#if DEBUG
            var rtViewInv = Matrix.Invert(rtView);
            Func<Vector2, Vector2> l2w = s => Vector2.Transform((s - bledPos) / scale, rtViewInv);
            _lastComposeMapping = (w2s, l2w, scale);
            if (stashDebugMapping)
            {
                _dbgWorldToScreen = w2s; _dbgUiScale = scale;
                Editor.DebugOverlay.ComposeInfo =
                    (vw, vh, _camera.GameWidth, _camera.GameHeight, scale, fractional, destFX, destFY);
            }
#endif

            GraphicsDevice.ScissorRectangle = prevScissor;

            Runtime.Cutscenes.CinematicBars.Draw(_spriteBatch, _pixelTexture, vw, vh);

            Runtime.UI.MonologueScreen.Draw(_spriteBatch, new Rectangle(0, 0, vw, vh),
                Runtime.UI.UiScale.For(vh));

            Runtime.UI.PauseScreen.Draw(_spriteBatch, new Rectangle(0, 0, vw, vh),
                vh / (float)_camera.GameHeight);

            Runtime.Rendering.ScreenFader.Draw(_spriteBatch, _pixelTexture, vw, vh);

            _camera.Zoom = prevZoom;
        }
    }

#if DEBUG
    private bool _sceneAudioOn;
#endif

    private void ApplySceneAudio()
    {
#if DEBUG
        bool audible = _editor.State.IsPlayMode;
        AudioManager.Instance.ListenerActive = audible;
        if (!audible)
        {
            if (_sceneAudioOn)
            {
                AudioManager.Instance.StopBGM(0.25f);
                AudioManager.Instance.SyncAmbients(null, 0.25f);
                SoundEmitter.StopAllIn(_scene);
                _sceneAudioOn = false;
            }
            return;
        }
        _sceneAudioOn = true;
#endif
        AudioManager.Instance.SyncAmbients(_scene.Ambients);
    }

    private const int BaseGameWidth = 320;
    private const int BaseGameHeight = 180;

    private void ApplyViewZoom()
    {
        float z = _scene?.ViewZoom ?? 1f;
        if (!(z > 0.1f) || z > 4f) z = 1f;
        int w = (int)MathF.Floor(BaseGameWidth * z * 0.5f + 0.5f) * 2;
        int h = (int)MathF.Floor(BaseGameHeight * z * 0.5f + 0.5f) * 2;
        if (_camera.GameWidth != w) _camera.GameWidth = w;
        if (_camera.GameHeight != h) _camera.GameHeight = h;

        Runtime.UI.DialogueBox.VirtualWidth = w;
        Runtime.UI.DialogueBox.VirtualHeight = h;
    }

    private void ApplyCameraFraming(bool gameOwns, Entity? player)
    {
        if (gameOwns && _scene != null && !Runtime.Cutscenes.CutsceneDirector.DampingOverridden)
            _camera.FollowDamping = _scene.CameraDampingEnabled
                ? _scene.CameraDamping
                : Runtime.Core.Camera.DefaultFollowDamping;

        if (gameOwns && Runtime.Cutscenes.CutsceneDirector.OwnsCamera) return;

        if (!gameOwns)
        {
            _camera.Target = null;
            _camera.Bounds = null;
            return;
        }

        if (_scene != null && _scene.CameraFixed)
        {
            _camera.Target = null;
            _camera.Position = _scene.CameraFixedPos;
            _camera.Bounds = null;
            return;
        }

        _camera.Target = player;
        _camera.Bounds = _scene is { CameraBoundsEnabled: true } ? _scene.CameraBounds : null;
    }

    private static readonly bool TracePixel =
        Environment.GetEnvironmentVariable("PIXELCORE_TRACE_PIXEL") == "1";
    private int _traceFrame;

    private static readonly string? TraceWalk =
#if DEBUG
        Environment.GetEnvironmentVariable("PIXELCORE_TRACE_WALK");
#else
        null;
#endif

    private void PixelTrace(Vector2 camPos, Vector2 bledPos, Vector2 world, Vector2 offset, float scale)
    {
        if (!TracePixel) return;
        Console.WriteLine(
            $"[px] f{_traceFrame++,-4} cam {camPos.X,9:0.000} | bledX {bledPos.X,7:0.#} | " +
            $"worldX {world.X,8:0.000} | offX {offset.X,10:0.0000} | screenX {bledPos.X + offset.X,10:0.0000} | scale {scale:0.####}");
    }

    private Effect? _pixZoom;
    private bool _pixZoomTried;

    public static float PixZoomSharpness
    {
        get => _pixZoomSharpness;
        set => _pixZoomSharpness = MathF.Min(MathF.Max(value, 1f), 8f);
    }
    private static float _pixZoomSharpness = 1.4f;

    public static bool PixZoomContinuousCompose { get; set; }

    public static bool SubpixelPan { get; set; } = true;

    private Effect? PreparePixZoom(int srcW, int srcH, float scale, int targetW, int targetH)
        => PreparePixZoom(srcW, srcH, scale,
            Matrix.CreateOrthographicOffCenter(0, targetW, targetH, 0, 0, 1));

    private Effect? PreparePixZoom(int srcW, int srcH, float scale, Matrix transform)
    {
        if (!_pixZoomTried)
        {
            _pixZoomTried = true;
            var path = System.IO.Path.Combine("Content", "Shaders", "PixZoom.fxb");
            if (!System.IO.File.Exists(path))
                Console.WriteLine("[PixZoom] PixZoom.fxb not found - fractional scales fall back to point sampling (uneven pixel sizes). Run scripts/compile-shaders.sh");
            else
                try { _pixZoom = new Effect(GraphicsDevice, System.IO.File.ReadAllBytes(path)); Console.WriteLine("[PixZoom] loaded - sharp bilinear active"); }
                catch (Exception ex) { Console.WriteLine($"[PixZoom] failed to load: {ex.Message}"); }
        }
        if (_pixZoom == null) return null;

        _pixZoom.Parameters["MatrixTransform"]?.SetValue(transform);
        _pixZoom.Parameters["SrcSize"]?.SetValue(new Vector2(srcW, srcH));
        _pixZoom.Parameters["Scale"]?.SetValue(scale);
        _pixZoom.Parameters["Sharpness"]?.SetValue(PixZoomSharpness);
        return _pixZoom;
    }

    private void EnsureGameRenderTarget()
    {
        int w = _camera.GameWidth + CameraBleed * 2;
        int h = _camera.GameHeight + CameraBleed * 2;
        if (_gameRenderTarget == null ||
            _gameRenderTarget.Width != w ||
            _gameRenderTarget.Height != h)
        {
            _gameRenderTarget?.Dispose();
            _gameRenderTarget = new RenderTarget2D(GraphicsDevice, w, h);
            _gameRenderTargetAbove?.Dispose();
            _gameRenderTargetAbove = new RenderTarget2D(GraphicsDevice, w, h);
        }
    }

    private void DrawSky(int rtW, int rtH, float artScale, Vector2 cameraPos)
    {
        var sky = _scene.Sky;
        if (sky == null) return;
        _sky.Draw(GraphicsDevice, _spriteBatch, sky, rtW, rtH, artScale, cameraPos);
    }

    private SpriteRenderer? GetSmoothFollowRenderable()
    {
        var target = _camera.Target ?? _scene.FindPlayer();
        if (target == null || !target.ActiveInHierarchy) return null;
        if (target.GetComponent<Transform>() == null) return null;
        var sr = target.GetComponent<SpriteRenderer>();
        return (sr != null && sr.Enabled) ? sr : null;
    }

    protected override void UnloadContent()
    {
        Runtime.Core.GameSettings.SaveIfDirty();

        Runtime.Core.Rumble.StopAll();

        AudioManager.Instance.Dispose();
#if DEBUG
        _editor?.Dispose();
#endif
        _gameRenderTarget?.Dispose();
        _sky.Dispose();
        _gameRenderTargetAbove?.Dispose();
        _scissorState?.Dispose();
        _lighting.Dispose();
        _shadow.Dispose();
        _post.Dispose();
        base.UnloadContent();
    }
}

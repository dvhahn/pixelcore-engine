using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.UI;

namespace PixelCore.Runtime.Cutscenes;

public static class CutsceneDirector
{
    public const string DoneKeyPrefix = "cutscene.done.";

    private static GameContext? _ctx;
    private static readonly Dictionary<string, Func<Cutscene, IEnumerator<Wait>>> _registry = new(StringComparer.Ordinal);
    private static readonly List<string> _order = new();

    private static Cutscene? _current;
    private static CoroutineRunner.CoroutineHandle? _handle;

    public static IReadOnlyList<string> Ids => _order;

    public static bool IsPlaying => _current != null;
    public static string CurrentId => _current?.Id ?? "";

    public static event Action<string, bool>? Finished;

    public static void Bind(GameContext ctx) => _ctx = ctx;

    public static Func<string, string, bool>? RoomLoader;

    public static Func<Entity, Vector2, bool>? PersistFacing;

    internal static bool FacingHookWarned;

    public static bool SuppressTriggers { get; set; }

    public static void Register(string id, Func<Cutscene, IEnumerator<Wait>> body,
                                string? room = null, string? spawn = null)
    {
        if (string.IsNullOrEmpty(id) || body == null) return;
        if (!_registry.ContainsKey(id)) _order.Add(id);
        else Console.WriteLine($"[Cutscene] ⚠ '{id}' registered twice - the later one wins");
        _registry[id] = body;

        if (!string.IsNullOrEmpty(room)) _declaredRooms[id] = (room, spawn ?? "");
        else _declaredRooms.Remove(id);
    }

    private static readonly Dictionary<string, (string Room, string Spawn)> _declaredRooms = new(StringComparer.Ordinal);

    internal static (string Room, string Spawn)? DeclaredRoom(string id)
        => _declaredRooms.TryGetValue(id, out var v) ? v : null;

    public static void ClearRegistry()
    {
        _registry.Clear(); _order.Clear(); _cast.Clear(); _declaredRooms.Clear();
        RoomLoader = null;
        PersistFacing = null;
        FacingHookWarned = false;
    }

    private static readonly Dictionary<string, string> _cast = new(StringComparer.Ordinal);

    public static void SetCast(string alias, string entityName)
    {
        if (string.IsNullOrEmpty(alias)) return;
        _cast[alias] = entityName;
    }

    internal static string ResolveAlias(string name)
        => _cast.TryGetValue(name, out var entity) ? entity : name;

    public static bool IsRegistered(string id) => _registry.ContainsKey(id);

#if DEBUG
    public static string? BodyMethodName(string id)
        => _registry.TryGetValue(id, out var body) ? body.Method.Name : null;
#endif

    public static bool HasPlayed(string id)
        => _ctx?.Blackboard.GetBool(DoneKeyPrefix + id) ?? false;

    public static bool Play(string id)
    {
        if (_ctx == null)
        { Console.WriteLine($"[Cutscene] ⚠ '{id}' failed to play - Bind(GameContext) was never called"); return false; }

        if (!_registry.TryGetValue(id, out var body))
        { Console.WriteLine($"[Cutscene] ⚠ no such cutscene: '{id}'"); return false; }

        if (IsPlaying)
        {
            Console.WriteLine($"[Cutscene] ⚠ '{id}' refused - '{CurrentId}' is still running");
            return false;
        }

        var c = new Cutscene(id, _ctx);
        _current = c;
        _handle = _ctx.Coroutines.Start(Run(c, body), $"cutscene:{id}");
        Console.WriteLine($"[Cutscene] ▶ {id}");
        return true;
    }

    public static void Stop()
    {
        if (!IsPlaying) return;
        Console.WriteLine($"[Cutscene] ■ {CurrentId} aborted");
        _handle?.Stop();
    }

    private static IEnumerator<Wait> Run(Cutscene c, Func<Cutscene, IEnumerator<Wait>> body)
    {
        bool completed = false;
        try
        {
            yield return Wait.For(body(c));
            completed = true;
        }
        finally
        {
            Finish(c, completed);
        }
    }

    private static void Finish(Cutscene c, bool completed)
    {
        c.StopForks();

        if (_owningCamera != null) { ReleaseCamera(_owningCamera, null); }
        CancelSkip();

        if (completed && _ctx != null)
            _ctx.Blackboard.SetBool(DoneKeyPrefix + c.Id, true);

        if (_current == c) { _current = null; _handle = null; }

        Console.WriteLine($"[Cutscene] {(completed ? "✔" : "✖")} {c.Id} {(completed ? "completed" : "aborted")}");
        Finished?.Invoke(c.Id, completed);
    }

    public static float SkipScale => 16f;

    public static float SkipHoldSeconds => 0.7f;

    public static bool Skipping { get; private set; }

    private static float _skipHeld;
    private static float _scaleBeforeSkip = 1f;
    private static bool _muteBeforeSkip;

    public static void RequestSkip()
    {
        if (Skipping || !IsPlaying || _ctx == null) return;

        Skipping = true;
        _scaleBeforeSkip = _ctx.Time.TimeScale;
        _ctx.Time.TimeScale = SkipScale;

        if (_ctx.Audio != null) { _muteBeforeSkip = _ctx.Audio.Muted; _ctx.Audio.Muted = true; }

        DialogueBox.FastForward = true;
        Console.WriteLine($"[Cutscene] ⏩ {CurrentId} skipping (x{SkipScale:0.#})");
    }

    public static void CancelSkip()
    {
        _skipHeld = 0f;
        if (!Skipping) return;

        Skipping = false;
        if (_ctx != null)
        {
            _ctx.Time.TimeScale = _scaleBeforeSkip;
            if (_ctx.Audio != null) _ctx.Audio.Muted = _muteBeforeSkip;
        }
        DialogueBox.FastForward = false;
    }

    public static void Update(float unscaledDt)
    {
        if (!IsPlaying) { _skipHeld = 0f; return; }

        if (InputMap.IsDown(GameAction.Skip))
        {
            _skipHeld += unscaledDt;
            if (_skipHeld >= SkipHoldSeconds) RequestSkip();
        }
        else _skipHeld = 0f;
    }

    public static float SkipHoldProgress
        => SkipHoldSeconds <= 0f ? 0f : Math.Clamp(_skipHeld / SkipHoldSeconds, 0f, 1f);

    private static Camera? _owningCamera;

    public static bool OwnsCamera => _owningCamera != null;

    internal static void TakeCamera(Camera cam)
    {
        if (_owningCamera == cam) return;
        _owningCamera = cam;
        cam.SetTarget(null, snap: false);
    }

    internal static void ReleaseCamera(Camera cam, Entity? target)
    {
        if (target != null) cam.SetTarget(target, snap: false);
        _owningCamera = null;
    }

    public static bool DampingOverridden => _dampingScopes > 0;

    private static int _dampingScopes;
    internal static void PushDamping() => _dampingScopes++;
    internal static void PopDamping() { if (_dampingScopes > 0) _dampingScopes--; }

    public static void Abort()
    {
        Stop();

        _ctx?.Coroutines.StopAll();

        CancelSkip();
        _current = null;
        _handle = null;
        _owningCamera = null;
        _dampingScopes = 0;
        FreezeState.ResetAll();
        CinematicBars.Set(0f);
    }
}

public readonly struct CameraDampingScope : IDisposable
{
    private readonly Camera? _camera;
    private readonly Vector2 _previous;

    internal CameraDampingScope(Camera? camera, Vector2 damping)
    {
        _camera = camera;
        _previous = camera?.FollowDamping ?? Vector2.Zero;
        if (camera != null) camera.FollowDamping = damping;
        CutsceneDirector.PushDamping();
    }

    public void Dispose()
    {
        if (_camera != null) _camera.FollowDamping = _previous;
        CutsceneDirector.PopDamping();
    }
}

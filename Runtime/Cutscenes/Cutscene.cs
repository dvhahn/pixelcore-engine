using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Rendering;
using PixelCore.Runtime.Story;

namespace PixelCore.Runtime.Cutscenes;

public enum Dir { Down, Up, Left, Right }

public static class Dirs
{
    public static Vector2 Vector(Dir d) => d switch
    {
        Dir.Left => new Vector2(-1f, 0f),
        Dir.Right => new Vector2(1f, 0f),
        Dir.Up => new Vector2(0f, -1f),
        _ => new Vector2(0f, 1f),
    };

    public static bool TryParse(string? name, out Dir dir)
    {
        dir = Dir.Down;
        if (string.IsNullOrEmpty(name)) return false;

        if (Array.IndexOf(Names, name) < 0) return false;
        return Enum.TryParse(name, ignoreCase: false, out dir);
    }

    public static string[] Names => Enum.GetNames<Dir>();
}

public sealed class Cutscene
{
    public static float DefaultWalkSpeed => Gameplay.Player.PlayerController.DefaultMoveSpeed;

    public string Id { get; }

    public GameContext Ctx { get; }
    public Scene Scene => Ctx.Scene;
    public Camera Camera => Ctx.Camera;
    public Blackboard Blackboard => Ctx.Blackboard;

    private readonly HashSet<string> _signals = new();
    private readonly List<CoroutineRunner.CoroutineHandle> _forks = new();

    private readonly HashSet<string> _fallbackLogged = new();

    internal Cutscene(string id, GameContext ctx)
    {
        Id = id;
        Ctx = ctx;
    }

    public FreezeScope Freeze(FreezeFlags flags = FreezeFlags.Cutscene) => new(flags);

    public CameraDampingScope CameraDamping(float x, float y) => new(Camera, new Vector2(x, y));

    public Entity? Find(string name) => TryFind(name, out var e, out _) ? e : null;

    public const string AnchorPrefix = "Anchor_";

    public bool TryFind(string name, out Entity? entity, out string? problem)
    {
        entity = null;
        problem = null;
        if (Scene == null || string.IsNullOrEmpty(name)) { problem = "the name is empty"; return false; }

        foreach (var candidateName in new[] { AnchorPrefix + name, CutsceneDirector.ResolveAlias(name), name })
        {
            var hits = Scene.FindEntities(candidateName);
            if (hits.Count > 1) { problem = Ambiguous(candidateName, hits); return false; }
            if (hits.Count == 1) { entity = hits[0]; return true; }
        }

        return false;
    }

    private static string Ambiguous(string searched, IReadOnlyList<Entity> hits)
    {
        var paths = new List<string>(hits.Count);
        foreach (var e in hits) paths.Add(PathOf(e));
        paths.Sort(System.StringComparer.Ordinal);
        return $"ambiguous - {hits.Count} candidates for '{searched}': {string.Join(", ", paths)}";
    }

    private static string PathOf(Entity e)
    {
        var parts = new List<string>();
        for (var cur = e; cur != null; cur = cur.Parent) parts.Add(cur.Name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    public Vector2? PositionOf(string name) => Find(name)?.GetComponent<Transform>()?.Position;

    private bool TryPositionOf(string name, out Vector2 position, out string? problem)
    {
        position = default;
        if (!TryFind(name, out var e, out problem)) return false;

        var t = e?.GetComponent<Transform>();
        if (t == null) { problem = $"no Transform: '{name}'"; return false; }
        position = t.Position;
        return true;
    }

    public Act Say(string blockId, string speaker = "") => new(SayRoutine(blockId, speaker));

    public Actor Actor(string name) => new(this, name);

    internal Act SayInline(string actorName, string text, int number)
        => new(SayInlineRoutine(actorName, text, number));

    private IEnumerator<Wait> SayInlineRoutine(string actorName, string text, int number)
    {
        if (string.IsNullOrEmpty(actorName))
            CutsceneLog.Arg(Id, "Say", "actor", "the name is empty", "playing with no speaker");
        else if (!TryFind(actorName, out var e, out var why) || e == null)
            CutsceneLog.Arg(Id, "Say", "actor", why ?? $"no such actor in the scene: '{actorName}'",
                            "the line plays anyway");
        else
            WarnIfOffscreen(actorName);

        if (!Story.DialogueRunner.PlayInline(Id, actorName, text, number))
        {
            CutsceneLog.Skip(Id, "Say", "line", "could not play the inline line (see the runner log)");
            yield break;
        }

        yield return Wait.Until(() => !Story.DialogueRunner.Active);
    }

    private IEnumerator<Wait> SayRoutine(string blockId, string speaker)
    {
        if (!string.IsNullOrEmpty(speaker)) WarnIfOffscreen(speaker);

        if (!DialogueRunner.Play(blockId, DialogueFlow.Auto))
        {
            CutsceneLog.Skip(Id, "Say", "block", $"no dialogue block '{blockId}'");
            yield break;
        }

        string playing = blockId;
        yield return Wait.Until(() => !DialogueRunner.Active || DialogueRunner.CurrentBlockId != playing);
    }

    public Act Walk(string actor, string anchor, float speed = 0f) => new(WalkRoutine(actor, anchor, null, speed));

    public Act WalkBy(string actor, float dx, float dy, float speed = 0f)
        => new(WalkRoutine(actor, "", new Vector2(dx, dy), speed));

    private IEnumerator<Wait> WalkRoutine(string actorName, string anchor, Vector2? relative, float speed)
    {
        var t = ActorTransform(actorName, "Walk");
        if (t == null) yield break;

        Vector2 target;
        if (relative.HasValue) target = t.Position + relative.Value;
        else
        {
            if (!TryPositionOf(anchor, out target, out var why))
            {
                CutsceneLog.Skip(Id, "Walk", "anchor",
                    why ?? $"no such anchor in the scene: '{anchor}' ({AnchorPrefix}{anchor})");
                yield break;
            }
        }

        if (speed <= 0f)
        {
            if (speed < 0f) CutsceneLog.Arg(Id, "Walk", "speed", $"{speed} is negative", $"the default {DefaultWalkSpeed}");
            speed = DefaultWalkSpeed;
        }

        var start = t.Position;
        var delta = target - start;
        float dist = delta.Length();
        if (dist < 0.01f) yield break;

        var animator = t.Entity?.GetComponent<Animator>();
        string suffix = Suffix(DirOf(delta));
        FaceEntity(t.Entity, DirOf(delta));
        PlayIfHas(animator, $"Walk_{suffix}");

        yield return Wait.For(Tween.For(Ctx.Coroutines, dist / speed, Ease.Linear,
            p => t.Position = Vector2.Lerp(start, target, p)));

        if (!PlayIfHas(animator, $"Idle_{suffix}")) PlayIfHas(animator, $"Walk_{suffix}", restart: false);
    }

    public Act GoToRoom(string room, string spawn) => new(GoToRoomRoutine(room, spawn));

    private IEnumerator<Wait> GoToRoomRoutine(string room, string spawn)
    {
        if (string.IsNullOrEmpty(room))
        { CutsceneLog.Skip(Id, "GoToRoom", "room", "the room name is empty"); yield break; }

        if (string.IsNullOrEmpty(spawn))
        {
            CutsceneLog.Skip(Id, "GoToRoom", "spawn",
                $"the landing point name for '{room}' is empty - it would contaminate the save resume point");
            yield break;
        }

        var sceneId = Assets.AssetRegistry.Instance.FindSceneIdByName(room, out var candidates);
        if (sceneId == null)
        {
            CutsceneLog.Skip(Id, "GoToRoom", "room",
                candidates.Count > 1
                    ? $"there are {candidates.Count} scenes called '{room}' - a name collision is never resolved arbitrarily"
                    : $"no scene called '{room}' in the registry (a file name typo, or it was never scanned)");
            yield break;
        }

        var loader = CutsceneDirector.RoomLoader;
        if (loader == null)
        {
            CutsceneLog.Skip(Id, "GoToRoom", "loader",
                $"no room loader was injected (CutsceneDirector.RoomLoader) - cannot go to '{room}'");
            yield break;
        }

        if (!loader(sceneId, spawn))
        { CutsceneLog.Skip(Id, "GoToRoom", "room", $"failed to load the room: '{room}' (spawn: '{spawn}')"); yield break; }

        yield break;
    }

    public Act Put(string actor, string anchor) => new(PutRoutine(actor, anchor));

    private IEnumerator<Wait> PutRoutine(string actorName, string anchor)
    {
        var t = ActorTransform(actorName, "Put");
        if (t == null) yield break;

        if (!TryPositionOf(anchor, out var p, out var why))
        {
            CutsceneLog.Skip(Id, "Put", "anchor",
                why ?? $"no such anchor in the scene: '{anchor}' ({AnchorPrefix}{anchor})");
            yield break;
        }

        t.Position = p;
        yield break;
    }

    public Act Face(string actor, Dir dir) => new(FaceRoutine(actor, dir));

    private IEnumerator<Wait> FaceRoutine(string actorName, Dir dir)
    {
        var t = ActorTransform(actorName, "Face");
        if (t == null) yield break;

        bool persisted = FaceEntity(t.Entity, dir);

        var animator = t.Entity?.GetComponent<Animator>();
        string suffix = Suffix(dir);
        bool played = PlayIfHas(animator, $"Idle_{suffix}") || PlayIfHas(animator, $"Walk_{suffix}");

        if (!played && !persisted)
        {
            CutsceneLog.Skip(Id, "Face", "direction",
                $"'{actorName}' has no directional clip (Idle_{suffix}, Walk_{suffix}) - "
                + "and it is an actor with no persistent facing, so nothing happens");
        }
        yield break;
    }

    public Act Show(string actor) => new(ActiveRoutine(actor, true, "Show"));

    public Act Hide(string actor) => new(ActiveRoutine(actor, false, "Hide"));

    private IEnumerator<Wait> ActiveRoutine(string actorName, bool active, string verb)
    {
        var t = ActorTransform(actorName, verb);
        if (t?.Entity == null) yield break;

        t.Entity.Active = active;
        yield break;
    }

    public Act Anim(string actor, string clip) => new(AnimRoutine(actor, clip));

    private IEnumerator<Wait> AnimRoutine(string actorName, string clip)
    {
        var t = ActorTransform(actorName, "Anim");
        if (t == null) yield break;

        var animator = t.Entity?.GetComponent<Animator>();
        if (animator == null)
        { CutsceneLog.Skip(Id, "Anim", "actor", $"no Animator: '{actorName}'"); yield break; }

        if (animator.HasClip(clip))
        {
            animator.Play(clip, restart: true);
        }
        else if (!PlayGlobalFallback(animator, actorName, clip))
        {
            yield break;
        }

        if (animator.CurrentClip is not { Loop: false }) yield break;

        bool done = false;
        void OnDone(string name) { if (name == clip) done = true; }
        animator.OnAnimationComplete += OnDone;
        try
        {
            float limit = MathF.Max(0.1f, animator.CurrentClip.TotalDuration * 3f);
            float waited = 0f;
            while (!done && waited < limit)
            {
                yield return Wait.NextFrame;
                waited += Ctx.Coroutines.Delta;
            }
            if (!done)
                CutsceneLog.Skip(Id, "Anim", "clip", $"no completion signal within {limit:0.#}s: '{clip}'");
        }
        finally { animator.OnAnimationComplete -= OnDone; }
    }

    private bool PlayGlobalFallback(Animator animator, string actorName, string clip)
    {
        var found = AnimClipCache.FindByName(clip);

        if (found.Count == 0)
        {
            CutsceneLog.Skip(Id, "Anim", "clip",
                $"'{clip}' - not among the {animator.ClipCount} clips registered on '{actorName}', and 0 globally");
            return false;
        }

        if (found.Count > 1)
        {
            var paths = new string[found.Count];
            for (int i = 0; i < found.Count; i++) paths[i] = found[i].Path;
            CutsceneLog.Skip(Id, "Anim", "clip",
                $"'{clip}' - ambiguous: {found.Count} globally: {string.Join(", ", paths)}");
            return false;
        }

        var resolved = AnimClipCache.Get(found[0].Id);
        if (resolved == null)
        {
            CutsceneLog.Skip(Id, "Anim", "clip",
                $"'{clip}' - found globally but could not be read: {found[0].Path} (Id {found[0].Id})");
            return false;
        }

        if (_fallbackLogged.Add($"{actorName}\u0000{clip}"))
            Console.WriteLine($"[Cutscene] global fallback: '{actorName}'/'{clip}' <- {found[0].Path}");

        animator.Play(resolved, restart: true);
        return true;
    }

    public Act CameraTo(string target, float duration = 0.8f, Ease ease = Ease.InOut)
        => new(CameraToRoutine(target, duration, ease));

    private IEnumerator<Wait> CameraToRoutine(string target, float duration, Ease ease)
    {
        if (!TryPositionOf(target, out var to, out var why))
        { CutsceneLog.Skip(Id, "CameraTo", "target", why ?? $"no such target in the scene: '{target}'"); yield break; }
        if (Camera == null) yield break;

        if (duration < 0f)
        { CutsceneLog.Arg(Id, "CameraTo", "duration", $"{duration} is negative", "0"); duration = 0f; }

        CutsceneDirector.TakeCamera(Camera);
        var from = Camera.Position;
        yield return Wait.For(Tween.For(Ctx.Coroutines, duration, ease,
            p => Camera.Position = Vector2.Lerp(from, to, p)));
    }

    public Act CameraFollow(string actor, float blend = 0f) => new(CameraFollowRoutine(actor, blend));

    private IEnumerator<Wait> CameraFollowRoutine(string actorName, float blend)
    {
        if (!TryFind(actorName, out var e, out var why) || e == null)
        { CutsceneLog.Skip(Id, "CameraFollow", "actor", why ?? $"no such actor in the scene: '{actorName}'"); yield break; }
        if (Camera == null) yield break;

        if (blend > 0f)
        {
            CutsceneDirector.TakeCamera(Camera);
            var from = Camera.Position;
            var t = e.GetComponent<Transform>();
            yield return Wait.For(Tween.For(Ctx.Coroutines, blend, Ease.InOut,
                p => Camera.Position = Vector2.Lerp(from, t != null ? t.Position + Camera.FollowOffset : from, p)));
        }

        CutsceneDirector.ReleaseCamera(Camera, e);
    }

    public Act FadeTo(float alpha, float duration) => new(FadeRoutine(alpha, duration));

    private IEnumerator<Wait> FadeRoutine(float alpha, float duration)
    {
        if (duration < 0f)
        { CutsceneLog.Arg(Id, "FadeTo", "duration", $"{duration} is negative", "0"); duration = 0f; }

        float from = ScreenFader.Alpha;
        float to = Math.Clamp(alpha, 0f, 1f);
        yield return Wait.For(Tween.For(Ctx.Coroutines, duration, Ease.Linear,
            p => ScreenFader.Set(Easing.Lerp(from, to, p))));
    }

    public Act Blackout() => new(BlackoutRoutine());

    private IEnumerator<Wait> BlackoutRoutine()
    {
        UI.MonologueScreen.Open();
        yield break;
    }

    public Act Voice(string text, int number = 0) => new(VoiceRoutine(text, number));

    private IEnumerator<Wait> VoiceRoutine(string text, int number)
    {
        if (string.IsNullOrEmpty(text))
        { CutsceneLog.Arg(Id, "Voice", "line", "it is empty", "skipping the line"); yield break; }

        if (!UI.MonologueScreen.Active)
        {
            CutsceneLog.Arg(Id, "Voice", "screen", "the monologue screen is not open", "calling Blackout instead");
            UI.MonologueScreen.Open();
        }

        string shown = number > 0 ? Text.Loc.Line($"{Id}.{number}", text) : text;
        UI.MonologueScreen.SetLine(shown);

        if (CutsceneDirector.Skipping) yield break;
        yield return Wait.Until(() => UI.MonologueScreen.TakeAdvance() || CutsceneDirector.Skipping);
    }

    public Act Silence(float seconds) => new(SilenceRoutine(seconds));

    private IEnumerator<Wait> SilenceRoutine(float seconds)
    {
        if (seconds < 0f)
        { CutsceneLog.Arg(Id, "Silence", "duration", $"{seconds} is negative", "0"); seconds = 0f; }

        UI.MonologueScreen.ClearLine();
        UI.MonologueScreen.AcceptInput = false;
        try
        {
            yield return Wait.Seconds(seconds);
        }
        finally
        {
            UI.MonologueScreen.AcceptInput = true;
            UI.MonologueScreen.ClearPending();
        }
    }

    public Act Reveal(float duration = 1.5f) => new(RevealRoutine(duration));

    private IEnumerator<Wait> RevealRoutine(float duration)
    {
        if (duration < 0f)
        { CutsceneLog.Arg(Id, "Reveal", "duration", $"{duration} is negative", "0"); duration = 0f; }

        if (!UI.MonologueScreen.Active)
        { CutsceneLog.Skip(Id, "Reveal", "screen", "the monologue screen is not open - there is no backdrop to lift"); yield break; }

        UI.MonologueScreen.ClearLine();

        float from = UI.MonologueScreen.Backdrop;
        yield return Wait.For(Tween.For(Ctx.Coroutines, duration, Ease.Linear,
            p => UI.MonologueScreen.Backdrop = Easing.Lerp(from, 0f, p)));

        UI.MonologueScreen.Close();
    }

    public Act Shake(float intensity, float duration) => new(ShakeRoutine(intensity, duration));

    private IEnumerator<Wait> ShakeRoutine(float intensity, float duration)
    {
        Camera?.Shake(intensity, duration);
        yield break;
    }

    public Act Rumble(float strength, float duration) => new(RumbleRoutine(strength, duration));

    private IEnumerator<Wait> RumbleRoutine(float strength, float duration)
    {
        if (duration < 0f)
        { CutsceneLog.Arg(Id, "Rumble", "duration", $"{duration} is negative", "0"); duration = 0f; }

        Core.Rumble.Play(strength, duration);
        yield break;
    }

    public Act Sfx(string name, float volume = 1f) => new(SfxRoutine(name, volume));

    private IEnumerator<Wait> SfxRoutine(string name, float volume)
    {
        if (!TryResolveAudio(name, "Sfx", out var path)) yield break;

        Ctx.Audio?.PlaySFX(path, volume);
        yield break;
    }

    public Act Bgm(string name, float fade = 1f) => new(BgmRoutine(name, fade));

    private IEnumerator<Wait> BgmRoutine(string name, float fade)
    {
        if (fade < 0f)
        { CutsceneLog.Arg(Id, "Bgm", "fade", $"{fade} is negative", "0"); fade = 0f; }

        if (!TryResolveAudio(name, "Bgm", out var path)) yield break;

        Ctx.Audio?.PlayBGM(path, fade);
        yield break;
    }

    public Act StopBgm(float fadeOut = 1f) => new(StopBgmRoutine(fadeOut));

    private IEnumerator<Wait> StopBgmRoutine(float fadeOut)
    {
        if (fadeOut < 0f)
        { CutsceneLog.Arg(Id, "StopBgm", "fade", $"{fadeOut} is negative", "0"); fadeOut = 0f; }

        Ctx.Audio?.StopBGM(fadeOut);
        yield break;
    }

    private bool TryResolveAudio(string name, string verb, out string path)
    {
        path = "";
        if (string.IsNullOrEmpty(name)) { CutsceneLog.Skip(Id, verb, "name", "it is empty"); return false; }

        if (name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0)
        {
            CutsceneLog.Skip(Id, verb, name, "names only (no paths) - write just the file name");
            return false;
        }

        var id = Assets.AssetRegistry.Instance.FindAudioIdByName(name, out var candidates);
        if (id == null)
        {
            CutsceneLog.Skip(Id, verb, name, candidates.Count == 0
                ? "0 globally - there is no audio file with that name"
                : $"ambiguous - {candidates.Count} globally: {PathsOf(candidates)}");
            return false;
        }

        var resolved = Assets.AssetRegistry.Instance.GetPath(id);
        if (string.IsNullOrEmpty(resolved))
        { CutsceneLog.Skip(Id, verb, name, $"registry id {id} yields no path"); return false; }

        path = resolved;
        return true;
    }

    private static string PathsOf(IReadOnlyList<string> ids)
    {
        var parts = new List<string>(ids.Count);
        foreach (var i in ids) parts.Add(Assets.AssetRegistry.Instance.GetPath(i) ?? i);
        return string.Join(", ", parts);
    }

    public Act MoodTo(string preset, float duration = 0.7f) => new(MoodRoutine(preset, duration));

    private IEnumerator<Wait> MoodRoutine(string preset, float duration)
    {
        if (Scene == null || !Scene.BlendPostPreset(preset, duration))
        { CutsceneLog.Skip(Id, "MoodTo", "preset", $"no such post preset in the scene: '{preset}'"); yield break; }

        yield return Wait.Seconds(duration);
    }

    public FxScope Fx(Rendering.FxLayer layer) => new(Rendering.FxStack.Push(layer));

    public readonly struct FxScope : IDisposable
    {
        private readonly int _id;
        public FxScope(int id) => _id = id;
        public void Dispose() => Rendering.FxStack.Pop(_id);
    }

    public Act TweenFor(float duration, Ease ease, Action<float> apply)
        => new(Tween.For(Ctx.Coroutines, duration, ease, apply));

    public Act Parallel(params Act[] acts) => new(ParallelRoutine(acts));

    private IEnumerator<Wait> ParallelRoutine(Act[] acts)
    {
        if (acts == null || acts.Length == 0) yield break;

        var handles = new List<CoroutineRunner.CoroutineHandle>(acts.Length);
        foreach (var a in acts)
            if (a.Routine != null) handles.Add(Ctx.Coroutines.Start(a.Routine, $"{Id}/parallel"));

        try
        {
            yield return Wait.Until(() =>
            {
                foreach (var h in handles) if (!h.IsDone) return false;
                return true;
            });
        }
        finally
        {
            foreach (var h in handles) h.Stop();
        }
    }

    public ForkHandle Fork(Act act)
    {
        if (act.Routine == null) return new ForkHandle(null);
        var h = Ctx.Coroutines.Start(act.Routine, $"{Id}/fork");
        _forks.Add(h);
        return new ForkHandle(h);
    }

    public Act Signal(string name) => new(SignalRoutine(name));

    private IEnumerator<Wait> SignalRoutine(string name)
    {
        _signals.Add(name);
        yield break;
    }

    public Act WaitSignal(string name) => new(WaitSignalRoutine(name));

    private IEnumerator<Wait> WaitSignalRoutine(string name)
    {
        yield return Wait.Until(() => _signals.Contains(name));
    }

    internal void StopForks()
    {
        foreach (var h in _forks) h.Stop();
        _forks.Clear();
    }

    private Transform? ActorTransform(string actorName, string verb)
    {
        if (string.IsNullOrEmpty(actorName))
        { CutsceneLog.Skip(Id, verb, "actor", "the name is empty"); return null; }

        if (!TryFind(actorName, out var e, out var why) || e == null)
        { CutsceneLog.Skip(Id, verb, "actor", why ?? $"no such actor in the scene: '{actorName}'"); return null; }

        var t = e.GetComponent<Transform>();
        if (t == null) { CutsceneLog.Skip(Id, verb, "actor", $"no Transform: '{actorName}'"); return null; }
        return t;
    }

    private void WarnIfOffscreen(string actorName)
    {
        var p = PositionOf(actorName);
        if (p == null || Camera == null) return;

        float halfW = Camera.GameWidth * 0.5f;
        float halfH = Camera.GameHeight * 0.5f;
        var d = p.Value - Camera.Position;
        if (MathF.Abs(d.X) <= halfW && MathF.Abs(d.Y) <= halfH) return;

        Console.WriteLine($"[Cutscene] ⚠ {Id} - Say - the speaker '{actorName}' is talking off screen "
            + $"(speaker {p.Value.X:0},{p.Value.Y:0} / camera {Camera.Position.X:0},{Camera.Position.Y:0})");
    }

    private static bool PlayIfHas(Animator? animator, string clip, bool restart = true)
    {
        if (animator == null || !animator.HasClip(clip)) return false;
        animator.Play(clip, restart);
        return true;
    }

    private static bool FaceEntity(Entity? e, Dir dir)
    {
        if (e == null) return false;

        var v = Vector(dir);
        e.GetComponent<Interactor>()?.SetFacing(v);

        if (CutsceneDirector.PersistFacing != null)
            return CutsceneDirector.PersistFacing(e, v);

        if (!CutsceneDirector.FacingHookWarned)
        {
            CutsceneDirector.FacingHookWarned = true;
            Console.WriteLine(
                "[Cutscene] ⚠ no persistent facing hook was injected (CutsceneDirector.PersistFacing) - "
                + "c.Face and c.Walk cannot leave a facing, and it reverts on the first frame after the cutscene ends");
        }
        return false;
    }

    internal static Dir DirOf(Vector2 delta)
        => MathF.Abs(delta.X) >= MathF.Abs(delta.Y)
            ? (delta.X < 0f ? Dir.Left : Dir.Right)
            : (delta.Y < 0f ? Dir.Up : Dir.Down);

    internal static Vector2 Vector(Dir d) => Dirs.Vector(d);

    internal static string Suffix(Dir d) => d switch
    {
        Dir.Left => "L", Dir.Right => "R", Dir.Up => "U", _ => "D",
    };
}

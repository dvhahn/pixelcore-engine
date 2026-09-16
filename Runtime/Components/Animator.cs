using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Animation;
using PixelCore.Runtime.Assets;

namespace PixelCore.Runtime.Components;

[ComponentCategory(ComponentCategories.Render)]
public class Animator : Component
{
    private SpriteRenderer? Renderer => Entity?.GetComponent<SpriteRenderer>();

    private Dictionary<string, AnimationClip> _clips = new();

    private readonly HashSet<string> _missWarned = new();

    private static string MissKey(string clipName) => clipName;

    private void WarnMiss(string clipName)
    {
        if (!_missWarned.Add(MissKey(clipName))) return;

        var owner = Entity?.Name ?? "(no entity)";
        var held = _clips.Count == 0 ? "(none)" : string.Join(", ", _clips.Keys);
        Console.WriteLine($"[Animator] ⚠ no such clip: '{clipName}' ({owner}) - holding: {held}");
    }

    public int ClipCount => _clips.Count;

    public AnimationClip? CurrentClip { get; private set; }

    public float CurrentTime { get; private set; }

    public float Speed { get; set; } = 1f;

    public bool IsPlaying { get; private set; }

    public event Action<string>? OnAnimationComplete;

    public event Action<string>? OnFrameEvent;

    private int _lastFrameIndex = -1;

    public void AddClip(AnimationClip clip)
    {
        _clips[clip.Name] = clip;
        _missWarned.Remove(MissKey(clip.Name));
    }

    public bool RemoveClip(string clipName)
    {
        if (!_clips.Remove(clipName)) return false;

        if (CurrentClip?.Name == clipName)
        {
            Stop();
            CurrentClip = null;
        }
        if (DefaultClip == clipName) DefaultClip = null;
        return true;
    }

    public void ClearClips()
    {
        UnresolvedClipIds.Clear();
        foreach (var name in new List<string>(_clips.Keys))
            RemoveClip(name);
    }

    public IReadOnlyCollection<AnimationClip> Clips => _clips.Values;

    public List<string> UnresolvedClipIds { get; } = new();

    public string? DefaultClip { get; set; }

    public bool HasClip(string clipName) => _clips.ContainsKey(clipName);

    public bool LoadFromFile(string animPath, GraphicsDevice graphicsDevice, string contentPath)
    {
        var animData = AnimationData.Load(animPath);
        if (animData == null) return false;

        string? atlasRel = null;
        if (!string.IsNullOrEmpty(animData.AtlasId))
            atlasRel = AssetRegistry.Instance.GetPath(animData.AtlasId);
        atlasRel ??= Path.ChangeExtension(animData.AtlasPath, ".atlas");

        var atlasPath = Path.Combine(contentPath, atlasRel);
        var atlas = SpriteAtlas.Load(atlasPath);
        if (atlas == null) return false;

        atlas.LoadTexture(graphicsDevice, Path.GetDirectoryName(atlasPath) ?? contentPath);
        if (atlas.Texture == null) return false;

        var clip = animData.ToClip(atlas);
        AddClip(clip);

        return true;
    }

    public void PlayFirst()
    {
        foreach (var clipName in _clips.Keys)
        {
            Play(clipName);
            break;
        }
    }

    public void Play(string clipName, bool restart = false)
    {
        if (!_clips.TryGetValue(clipName, out var clip))
        {
            WarnMiss(clipName);
            return;
        }
        Play(clip, restart);
    }

    public void Play(AnimationClip clip, bool restart = false)
    {
        if (CurrentClip == clip && !restart && IsPlaying)
            return;

        CurrentClip = clip;
        CurrentTime = 0;
        IsPlaying = true;
        _lastFrameIndex = -1;

        SyncFrame();
    }

    public void Stop()
    {
        IsPlaying = false;
    }

    public void Pause()
    {
        IsPlaying = false;
    }

    public void Resume()
    {
        IsPlaying = true;
    }

    public readonly struct PlaybackState
    {
        public readonly AnimationClip? Clip;
        public readonly float Time;
        public readonly bool Playing;

        public PlaybackState(AnimationClip? clip, float time, bool playing)
        {
            Clip = clip; Time = time; Playing = playing;
        }
    }

    public PlaybackState CapturePlayback() => new(CurrentClip, CurrentTime, IsPlaying);

    public void RestorePlayback(PlaybackState state)
    {
        CurrentClip = state.Clip;
        CurrentTime = state.Time;
        IsPlaying = state.Playing;
        _lastFrameIndex = -1;
    }

    public void ClearFrame()
    {
        var r = Renderer;
        if (r != null) r.FrameOverride = null;
        _frameReleased = true;
    }

    private bool _frameReleased;

    public override void OnDestroy() => ClearFrame();

    public override void Update(float deltaTime)
    {
        if (CurrentClip == null)
            return;
        if (CurrentClip.Frames.Count == 0)
            return;

        if (!IsPlaying)
        {
            if (!_frameReleased && Renderer is { FrameOverride: null }) SyncFrame();
            return;
        }

        CurrentTime += deltaTime * Speed;

        int currentFrameIndex = CurrentClip.GetFrameIndex(CurrentTime);
        if (currentFrameIndex != _lastFrameIndex)
        {
            var frame = CurrentClip.Frames[currentFrameIndex];
            if (!string.IsNullOrEmpty(frame.EventName))
            {
                OnFrameEvent?.Invoke(frame.EventName);
            }
            _lastFrameIndex = currentFrameIndex;
        }

        if (!CurrentClip.Loop && CurrentTime >= CurrentClip.TotalDuration)
        {
            IsPlaying = false;
            OnAnimationComplete?.Invoke(CurrentClip.Name);
        }

        SyncFrame();
    }

    private void SyncFrame()
    {
        if (CurrentClip == null || CurrentClip.Frames.Count == 0) return;
        var r = Renderer;
        if (r == null) return;
        r.FrameOverride = new SpriteRenderer.FrameOutput(
            CurrentClip.Texture,
            CurrentClip.GetFrame(CurrentTime).SourceRect,
            CurrentClip.PivotX,
            CurrentClip.PivotY);
        _frameReleased = false;
    }
}

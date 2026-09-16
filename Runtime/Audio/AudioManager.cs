using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;

namespace PixelCore.Runtime.Audio;

public class AudioManager : IDisposable
{
    private static AudioManager? _instance;
    public static AudioManager Instance => _instance ??= new AudioManager();

    #region Volume

    private float _masterVolume = 1f;
    private readonly float[] _busVolume = { 1f, 1f, 1f, 1f, 1f };

    public float MasterVolume
    {
        get => _masterVolume;
        set { _masterVolume = Math.Clamp(value, 0f, 1f); ApplyVolumes(); }
    }

    public float GetBusVolume(AudioBus bus) => _busVolume[(int)bus];

    public void SetBusVolume(AudioBus bus, float value)
    {
        _busFade[(int)bus].Active = false;
        _busVolume[(int)bus] = Math.Clamp(value, 0f, 1f);
        ApplyVolumes();
    }

    private bool _muted;

    public bool Muted
    {
        get => _muted;
        set { if (_muted == value) return; _muted = value; ApplyVolumes(); }
    }

    public float EffectiveVolume(AudioBus bus)
        => _muted ? 0f : _masterVolume * _busVolume[(int)bus];

    public float BGMVolume
    {
        get => GetBusVolume(AudioBus.Music);
        set => SetBusVolume(AudioBus.Music, value);
    }

    public float SFXVolume
    {
        get => GetBusVolume(AudioBus.SFX);
        set => SetBusVolume(AudioBus.SFX, value);
    }

    private struct BusFade
    {
        public float From, To, Elapsed, Duration;
        public bool Active;
    }

    private readonly BusFade[] _busFade = new BusFade[AudioBusRules.Count];

    public void FadeBusVolume(AudioBus bus, float target, float duration)
    {
        target = Math.Clamp(target, 0f, 1f);

        if (duration <= 0f) { SetBusVolume(bus, target); return; }

        ref var cur = ref _busFade[(int)bus];
        bool alreadyHeadingThere = cur.Active
            ? MathF.Abs(cur.To - target) < 1e-4f
            : MathF.Abs(_busVolume[(int)bus] - target) < 1e-4f;
        if (alreadyHeadingThere) return;

        _busFade[(int)bus] = new BusFade
        {
            From = _busVolume[(int)bus],
            To = target,
            Elapsed = 0f,
            Duration = duration,
            Active = true,
        };
    }

    public bool IsBusFading(AudioBus bus) => _busFade[(int)bus].Active;

    public void CancelBusFade(AudioBus bus) => _busFade[(int)bus].Active = false;

    private void UpdateBusFades(float deltaTime)
    {
        bool changed = false;

        for (int i = 0; i < _busFade.Length; i++)
        {
            ref var f = ref _busFade[i];
            if (!f.Active) continue;

            f.Elapsed += deltaTime;
            float t = Math.Min(f.Elapsed / f.Duration, 1f);
            _busVolume[i] = f.From + (f.To - f.From) * t;
            changed = true;

            if (t >= 1f)
            {
                _busVolume[i] = f.To;
                f.Active = false;
            }
        }

        if (changed) ApplyVolumes();
    }

    #endregion

    #region Libraries

    private readonly Dictionary<string, SoundEffect> _sfxLibrary = new();

    private readonly Dictionary<string, SoundEffect> _soundCache = new();

    private readonly HashSet<string> _missing = new();

    public int MaxConcurrentSFX { get; set; } = 32;

    private bool _maxWarned;

    #endregion

    #region Tracked instances

    private sealed class Tracked
    {
        public SoundEffectInstance Instance = null!;
        public AudioBus Bus;
        public float Gain = 1f;
        public string Key = "";

        public bool Spatial;

        public float FadeFrom, FadeTo, FadeElapsed, FadeDuration;
        public bool StopAtFadeEnd;

        public bool Fading => FadeDuration > 0f;

        public float FadeGain => Fading
            ? FadeFrom + (FadeTo - FadeFrom) * Math.Min(FadeElapsed / FadeDuration, 1f)
            : 1f;
    }

    private readonly List<Tracked> _tracked = new();

    private readonly List<OggStream> _streams = new();

    #endregion

    #region BGM state

    private SoundEffectInstance? _currentBGM;
    private string? _currentBGMPath;

    public string? CurrentBGM => _currentBGMPath;

    public bool IsBGMPlaying => _currentBGM?.State == SoundState.Playing;

    private bool _isFading;
    private float _fadeTimer;
    private float _fadeDuration;
    private float _fadeFromGain;
    private float _fadeToGain;
    private Action? _onFadeComplete;

    private float _bgmGain = 1f;

    private string? _bgmRequested;

    private bool _bgmLooping;

    private Action? _onBGMEnd;

    private bool _bgmStopping;

    #endregion

    #region Pause

    private bool _paused;

    public void SetPaused(bool paused)
    {
        if (_paused == paused) return;
        _paused = paused;

        foreach (var t in _tracked)
        {
            if (!AudioBusRules.PausesWithGame(t.Bus)) continue;
            if (paused)
            {
                if (t.Instance.State == SoundState.Playing) t.Instance.Pause();
            }
            else
            {
                if (t.Instance.State == SoundState.Paused) t.Instance.Resume();
            }
        }
    }

    #endregion

    public string ContentPath { get; set; } = "Content";

    public Vector2 ListenerPosition { get; set; }

    public Vector2? ListenerOverride { get; set; }

    public bool ListenerActive { get; set; } = true;

    private ContentManager? _content;
    private bool _disposed;

    private AudioManager() { }

    public void Initialize(ContentManager? content = null) => _content = content;

    #region Registration

    public void RegisterSFX(string name, SoundEffect sound) => _sfxLibrary[name] = sound;

    public void UnregisterSFX(string name) => _sfxLibrary.Remove(name);

    #endregion

    #region BGM

    public void PlayBGM(string pathOrName, float fade = 1f)
    {
        if (string.IsNullOrEmpty(pathOrName)) return;
        if (IsSameTrackRequested(pathOrName)) return;

        _bgmRequested = pathOrName;

        if (ResumeIfSameTrack(pathOrName, fade * 0.5f)) return;
        if (IsBGMPlaying) CrossFadeBGM(pathOrName, fade);
        else StartBGM(pathOrName, loop: true, fadeIn: fade);
    }

    public void PlayOnce(string pathOrName, float fadeIn = 0f, Action? onEnd = null)
    {
        if (string.IsNullOrEmpty(pathOrName)) return;

        StartBGM(pathOrName, loop: false, fadeIn: fadeIn);
        _onBGMEnd = onEnd;
    }

    public void PlayStinger(string pathOrName, float volume = 1f)
        => PlayOneShot(pathOrName, AudioBus.Music, volume, 0f, 0f);

    private void StartBGM(string pathOrName, bool loop, float fadeIn)
    {
        StopBGMImmediate();

        try
        {
            _currentBGM = CreateInstanceFor(pathOrName, loop);
            if (_currentBGM == null) return;

            _currentBGM.IsLooped = loop;
            _bgmLooping = loop;
            _currentBGMPath = pathOrName;
            _bgmRequested = pathOrName;
            _bgmStopping = false;

            if (fadeIn > 0f)
            {
                _bgmGain = 0f;
                _currentBGM.Volume = 0f;
                _currentBGM.Play();
                StartFade(0f, 1f, fadeIn, null);
            }
            else
            {
                _bgmGain = 1f;
                _currentBGM.Volume = BGMLiveVolume();
                _currentBGM.Play();
            }
        }
        catch (NoAudioHardwareException ex)
        {
            ReportDeviceFailure("BGM playback", ex);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioManager] Failed to play BGM: {ex.Message}");
        }
    }

    private bool IsSameTrackRequested(string pathOrName)
        => pathOrName == _bgmRequested
        && _currentBGM != null
        && _currentBGM.State != SoundState.Stopped;

    private bool ResumeIfSameTrack(string pathOrName, float fadeIn)
    {
        if (_currentBGMPath != pathOrName || !IsBGMPlaying) return false;

        if (_bgmStopping)
        {
            StartFade(_bgmGain, 1f, MathF.Max(fadeIn, 0.15f), null);
            _bgmStopping = false;
        }
        return true;
    }

    public void StopBGM(float fadeOut = 0f)
    {
        if (_currentBGM == null) return;

        if (fadeOut > 0f && IsBGMPlaying)
        {
        _bgmStopping = true;
            StartFade(_bgmGain, 0f, fadeOut, StopBGMImmediate);
        }
        else
        {
            StopBGMImmediate();
        }
    }

    private void StopBGMImmediate()
    {
        _isFading = false;
        _bgmGain = 1f;
        _bgmStopping = false;
        _bgmRequested = null;
        _onFadeComplete = null;
        _bgmLooping = false;
        _onBGMEnd = null;

        if (_currentBGM != null)
        {
            _currentBGM.Stop();
            _currentBGM.Dispose();
            _currentBGM = null;
        }
        _currentBGMPath = null;
    }

    public void PauseBGM() => _currentBGM?.Pause();

    public void ResumeBGM() => _currentBGM?.Resume();

    public void CrossFadeBGM(string newPathOrName, float duration = 1f, bool loop = true)
    {
        if (_currentBGMPath == newPathOrName) return;

        var half = duration / 2f;
        if (IsBGMPlaying)
        {
            _bgmStopping = true;
            StartFade(_bgmGain, 0f, half, () =>
            {
                StopBGMImmediate();
                StartBGM(newPathOrName, loop, half);
            });
        }
        else
        {
            StartBGM(newPathOrName, loop, half);
        }
    }

    #endregion

    #region One-shot

    public void PlaySFX(string pathOrName, float volume = 1f, float pitch = 0f, float pan = 0f)
        => PlayOneShot(pathOrName, AudioBus.SFX, volume, pitch, pan);

    public void PlayUI(string pathOrName, float volume = 1f)
        => PlayOneShot(pathOrName, AudioBus.UI, volume, 0f, 0f);

    private void PlayOneShot(string pathOrName, AudioBus bus, float volume, float pitch, float pan)
    {
        if (_paused && AudioBusRules.PausesWithGame(bus)) return;

        EnsureDeviceProbed();
        if (SilentMode) return;

        try
        {
            if (ResolveOggPath(pathOrName) != null)
            {
                PlayTracked(pathOrName, bus, volume, fadeIn: 0f, loop: false);
                return;
            }

            var sound = LoadSound(pathOrName);
            if (sound == null) return;

            var v = EffectiveVolume(bus) * Math.Clamp(volume, 0f, 1f);
            sound.Play(Math.Clamp(v, 0f, 1f), Math.Clamp(pitch, -1f, 1f), Math.Clamp(pan, -1f, 1f));
        }
        catch (NoAudioHardwareException ex) { ReportDeviceFailure("SFX playback", ex); }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioManager] Failed to play SFX: {ex.Message}");
        }
    }

    #endregion

    #region Loops / Ambient / Voice

    public SoundEffectInstance? PlayLoop(string pathOrName, AudioBus bus = AudioBus.SFX,
                                         float volume = 1f, float fadeIn = 0f,
                                         bool spatial = false)
        => PlayTracked(pathOrName, bus, volume, fadeIn, loop: true, spatial: spatial);

    private SoundEffectInstance? PlayTracked(string pathOrName, AudioBus bus,
                                             float volume, float fadeIn, bool loop,
                                             bool spatial = false)
    {
        CleanupTracked();

        if (_tracked.Count >= MaxConcurrentSFX)
        {
            if (!_maxWarned)
            {
                Console.WriteLine($"[AudioManager] concurrent tracking limit reached ({MaxConcurrentSFX}) - new loops and voices will not start");
                _maxWarned = true;
            }
            return null;
        }
        _maxWarned = false;

        try
        {
            var instance = CreateInstanceFor(pathOrName, loop);
            if (instance == null) return null;

            var t = new Tracked
            {
                Instance = instance,
                Bus = bus,
                Gain = Math.Clamp(volume, 0f, 1f),
                Key = pathOrName,
                Spatial = spatial,
            };
            t.Instance.IsLooped = loop;

            if (fadeIn > 0f)
            {
                t.FadeFrom = 0f; t.FadeTo = 1f; t.FadeElapsed = 0f; t.FadeDuration = fadeIn;
            }
            t.Instance.Volume = LiveVolume(t);
            t.Instance.Play();

            if (_paused && AudioBusRules.PausesWithGame(bus)) t.Instance.Pause();

            _tracked.Add(t);
            return t.Instance;
        }
        catch (NoAudioHardwareException ex) { ReportDeviceFailure("loop playback", ex); return null; }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioManager] Failed to play loop: {ex.Message}");
            return null;
        }
    }

    public void PlayAmbient(string pathOrName, float volume = 1f, float fadeIn = 0.5f)
    {
        foreach (var t in _tracked)
        {
            if (t.Spatial) continue;
            if (t.Bus != AudioBus.Ambient || t.Key != pathOrName) continue;
            if (t.Instance.State == SoundState.Stopped) continue;

            t.Gain = Math.Clamp(volume, 0f, 1f);

            if (t.StopAtFadeEnd)
            {
                t.FadeFrom = t.FadeGain;
                t.FadeTo = 1f;
                t.FadeElapsed = 0f;
                t.FadeDuration = MathF.Max(fadeIn, 0.15f);
                t.StopAtFadeEnd = false;
            }
            else
            {
                t.Instance.Volume = LiveVolume(t);
            }
            return;
        }

        PlayLoop(pathOrName, AudioBus.Ambient, volume, fadeIn);
    }

    public void StopAmbient(string pathOrName, float fadeOut = 0.5f)
        => StopLoop(pathOrName, AudioBus.Ambient, fadeOut);

    public void SyncAmbients(IReadOnlyList<AmbientTrack>? wanted, float fade = 0.5f)
    {
        _ambientKeys.Clear();
        if (wanted != null)
            foreach (var w in wanted)
                if (w.ResolvePath() is { Length: > 0 } path)
                    _ambientKeys.Add((path, w.Volume));

        foreach (var (path, volume) in _ambientKeys)
            PlayAmbient(path, volume, fade);

        foreach (var t in _tracked)
        {
            if (t.Spatial) continue;
            if (t.Bus != AudioBus.Ambient || t.StopAtFadeEnd) continue;
            if (!Wants(t.Key)) BeginFadeOut(t, fade);
        }

        bool Wants(string key)
        {
            foreach (var (path, _) in _ambientKeys) if (path == key) return true;
            return false;
        }
    }

    private readonly List<(string Path, float Volume)> _ambientKeys = new();

    public void StopAllAmbient(float fadeOut = 0.5f)
    {
        foreach (var t in _tracked)
            if (t.Bus == AudioBus.Ambient && !t.Spatial) BeginFadeOut(t, fadeOut);
    }

    public void StopLoop(string pathOrName, AudioBus bus = AudioBus.SFX, float fadeOut = 0f)
    {
        foreach (var t in _tracked)
            if (t.Bus == bus && t.Key == pathOrName && !t.Spatial) BeginFadeOut(t, fadeOut);
    }

    public void StopLoopInstance(SoundEffectInstance? instance, float fadeOut = 0f)
    {
        if (instance == null) return;
        foreach (var t in _tracked)
            if (ReferenceEquals(t.Instance, instance)) { BeginFadeOut(t, fadeOut); return; }
    }

    internal bool IsFadingOut(SoundEffectInstance? instance)
    {
        if (instance == null) return false;
        foreach (var t in _tracked)
            if (ReferenceEquals(t.Instance, instance)) return t.StopAtFadeEnd;
        return false;
    }

    internal int TrackedCount => _tracked.Count;

    internal readonly record struct PlayingInfo(
        string Key, AudioBus Bus, float Volume, float Gain, float Pan, bool Spatial, bool FadingOut);

    internal void SnapshotPlaying(List<PlayingInfo> into)
    {
        into.Clear();
        foreach (var t in _tracked)
            into.Add(new PlayingInfo(t.Key, t.Bus, t.Instance.Volume, t.Gain * t.FadeGain,
                                     t.Instance.Pan, t.Spatial, t.StopAtFadeEnd));
    }

    internal float BGMGain => _currentBGM == null ? 0f : _bgmGain;

    internal float BGMOutputVolume => _currentBGM?.Volume ?? 0f;

    public void SetLoopSpatial(SoundEffectInstance? instance, float gain, float pan)
    {
        if (instance == null || instance.IsDisposed) return;

        foreach (var t in _tracked)
        {
            if (!ReferenceEquals(t.Instance, instance)) continue;

            t.Gain = Math.Clamp(gain, 0f, 1f);
            if (!t.Fading) t.Instance.Volume = LiveVolume(t);
            t.Instance.Pan = Math.Clamp(pan, -1f, 1f);
            return;
        }
    }

    public void PlayVoice(string pathOrName, float volume = 1f)
    {
        StopVoice();
        PlayTracked(pathOrName, AudioBus.Voice, volume, fadeIn: 0f, loop: false);
    }

    public void StopVoice()
    {
        foreach (var t in _tracked)
            if (t.Bus == AudioBus.Voice) BeginFadeOut(t, 0f);
    }

    private static void BeginFadeOut(Tracked t, float seconds)
    {
        if (seconds <= 0f)
        {
            t.Instance.Stop();
            t.StopAtFadeEnd = true;
            t.FadeDuration = 0f;
            return;
        }
        t.FadeFrom = t.FadeGain;
        t.FadeTo = 0f;
        t.FadeElapsed = 0f;
        t.FadeDuration = seconds;
        t.StopAtFadeEnd = true;
    }

    public void StopAllSFX()
    {
        foreach (var t in _tracked)
        {
            t.Instance.Stop();
            t.Instance.Dispose();
        }
        _tracked.Clear();
    }

    public string? SoloKey
    {
        get => _soloKey;
        set { if (_soloKey == value) return; _soloKey = value; ApplyVolumes(); }
    }
    private string? _soloKey;

    public const string SoloBGM = "bgm";

    private float LiveVolume(Tracked t)
    {
        if (_soloKey != null && t.Key != _soloKey) return 0f;
        return Math.Clamp(EffectiveVolume(t.Bus) * t.Gain * t.FadeGain, 0f, 1f);
    }

    private float BGMLiveVolume()
    {
        if (_soloKey != null && _soloKey != SoloBGM && _soloKey != _currentBGMPath) return 0f;
        return Math.Clamp(EffectiveVolume(AudioBus.Music) * _bgmGain, 0f, 1f);
    }

    private void ApplyVolumes()
    {
        if (_currentBGM != null)
            _currentBGM.Volume = BGMLiveVolume();

        foreach (var t in _tracked)
            t.Instance.Volume = LiveVolume(t);
    }

    private void CleanupTracked()
    {
        _tracked.RemoveAll(t =>
        {
            if (t.Instance.State != SoundState.Stopped) return false;
            t.Instance.Dispose();
            return true;
        });
    }

    #endregion

    #region Loading

    public bool SilentMode { get; private set; }

    private bool _deviceProbed;

    internal Action? DeviceProbeOverride;

    internal void EnsureDeviceProbed()
    {
        if (_deviceProbed) return;
        _deviceProbed = true;
        try
        {
            if (DeviceProbeOverride != null) DeviceProbeOverride();
            else _ = SoundEffect.MasterVolume;
        }
        catch (Exception ex)
        {
            ReportDeviceFailure("device probe", ex);
        }
    }

    internal void ReportDeviceFailure(string where, Exception? ex = null)
    {
        if (SilentMode) return;
        SilentMode = true;

        try { StopBGM(0f); StopAllSFX(); StopAllAmbient(0f); }
        catch {  }

        Console.WriteLine($"[AudioManager] ⚠ the audio device is unusable - continuing in silence ({where})" +
                          (ex == null ? "" : $": {ex.GetType().Name}: {ex.Message}"));
    }

    private SoundEffectInstance? CreateInstanceFor(string pathOrName, bool loop)
    {
        EnsureDeviceProbed();

        if (SilentMode) return null;

        try { return CreateInstanceForCore(pathOrName, loop); }
        catch (NoAudioHardwareException ex) { ReportDeviceFailure("instance creation", ex); return null; }
    }

    private SoundEffectInstance? CreateInstanceForCore(string pathOrName, bool loop)
    {
        if (_sfxLibrary.ContainsKey(pathOrName) || _soundCache.ContainsKey(pathOrName))
            return LoadSound(pathOrName)?.CreateInstance();

        if (_missing.Contains(pathOrName)) return null;

        var ogg = ResolveOggPath(pathOrName);
        if (ogg == null) return LoadSound(pathOrName)?.CreateInstance();

        var stream = OggStream.TryOpen(ogg, loop);
        if (stream == null)
        {
            _missing.Add(pathOrName);
            return null;
        }

        _streams.Add(stream);
        return stream.Instance;
    }

    internal string? ResolveOggPath(string pathOrName)
    {
        var full = Path.Combine(ContentPath, pathOrName);

        if (full.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
            return File.Exists(full) ? full : null;

        if (Path.HasExtension(full)) return null;

        var candidate = full + ".ogg";
        return File.Exists(candidate) ? candidate : null;
    }

    private SoundEffect? LoadSound(string pathOrName)
    {
        if (_sfxLibrary.TryGetValue(pathOrName, out var registered)) return registered;
        if (_soundCache.TryGetValue(pathOrName, out var cached)) return cached;
        if (_missing.Contains(pathOrName)) return null;

        EnsureDeviceProbed();
        if (SilentMode) return null;

        SoundEffect? sound = null;

        if (_content != null)
        {
            try { sound = _content.Load<SoundEffect>(pathOrName); }
            catch {  }
        }

        if (sound == null)
        {
            var fullPath = Path.Combine(ContentPath, pathOrName);
            if (!fullPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                fullPath += ".wav";

            if (File.Exists(fullPath))
            {
                using var stream = File.OpenRead(fullPath);
                sound = SoundEffect.FromStream(stream);
            }
        }

        if (sound != null) _soundCache[pathOrName] = sound;
        else
        {
            _missing.Add(pathOrName);
            Console.WriteLine($"[AudioManager] Sound not found: {pathOrName}");
        }
        return sound;
    }

    public void Preload(string path)
    {
        if (ResolveOggPath(path) != null) return;
        LoadSound(path);
    }

    public void ClearCache()
    {
        foreach (var sound in _soundCache.Values) sound.Dispose();
        _soundCache.Clear();
        _missing.Clear();
    }

    #endregion

    #region Update

    public void Update(float deltaTime)
    {
        UpdateStreams();
        UpdateBusFades(deltaTime);
        UpdateBGMFade(deltaTime);
        UpdateBGMEnd();
        UpdateTracked(deltaTime);
    }

    private void UpdateStreams()
    {
        for (int i = _streams.Count - 1; i >= 0; i--)
        {
            var s = _streams[i];
            if (s.Instance.IsDisposed)
            {
                s.Dispose();
                _streams.RemoveAt(i);
                continue;
            }
            s.Update();
        }
    }

    private void UpdateBGMEnd()
    {
        if (_currentBGM == null || _bgmLooping) return;
        if (_currentBGM.State != SoundState.Stopped) return;

        var callback = _onBGMEnd;
        StopBGMImmediate();
        callback?.Invoke();
    }

    private void UpdateBGMFade(float deltaTime)
    {
        if (!_isFading || _currentBGM == null) return;

        _fadeTimer += deltaTime;
        var t = Math.Min(_fadeTimer / _fadeDuration, 1f);
        _bgmGain = _fadeFromGain + (_fadeToGain - _fadeFromGain) * t;
        _currentBGM.Volume = BGMLiveVolume();

        if (t >= 1f)
        {
            _isFading = false;
            var callback = _onFadeComplete;
            _onFadeComplete = null;
            callback?.Invoke();
        }
    }

    private void UpdateTracked(float deltaTime)
    {
        for (int i = _tracked.Count - 1; i >= 0; i--)
        {
            var t = _tracked[i];

            if (t.Fading)
            {
                if (!(_paused && AudioBusRules.PausesWithGame(t.Bus)))
                    t.FadeElapsed += deltaTime;

                t.Instance.Volume = LiveVolume(t);

                if (t.FadeElapsed >= t.FadeDuration)
                {
                    t.FadeDuration = 0f;
                    if (t.StopAtFadeEnd) t.Instance.Stop();
                    else t.Instance.Volume = LiveVolume(t);
                }
            }

            if (t.Instance.State == SoundState.Stopped)
            {
                t.Instance.Dispose();
                _tracked.RemoveAt(i);
            }
        }
    }

    private void StartFade(float from, float to, float duration, Action? onComplete)
    {
        _isFading = true;
        _fadeTimer = 0f;
        _fadeDuration = Math.Max(duration, 0.001f);
        _fadeFromGain = from;
        _fadeToGain = to;
        _onFadeComplete = onComplete;
    }

    #endregion

    #region Utility

    public void StopAll()
    {
        StopBGM(0f);
        StopAllSFX();
    }

    #endregion

    #region Dispose

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopAll();
        ClearCache();

        foreach (var s in _streams) s.Dispose();
        _streams.Clear();

        foreach (var sound in _sfxLibrary.Values) sound.Dispose();
        _sfxLibrary.Clear();

        _instance = null;
    }

    #endregion
}

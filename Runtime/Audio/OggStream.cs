using System;
using Microsoft.Xna.Framework.Audio;

namespace PixelCore.Runtime.Audio;

internal sealed class OggStream : IDisposable
{
    public const float ChunkSeconds = 0.25f;

    private const int TargetPending = 3;

    private readonly OggDecoder _decoder;
    private readonly byte[] _pcm;
    private bool _eof;
    private bool _disposed;

    public DynamicSoundEffectInstance Instance { get; }

    public bool IsLooped { get; }

    public string Path { get; }

    internal static int ConstructedCount;

    private OggStream(OggDecoder decoder, string path, bool loop)
    {
        _decoder = decoder;
        Path = path;
        IsLooped = loop;
        _pcm = new byte[decoder.ChunkBytes];

        ConstructedCount++;
        Instance = new DynamicSoundEffectInstance(
            decoder.SampleRate,
            decoder.Channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo);
    }

    public static OggStream? TryOpen(string fullPath, bool loop)
    {
        AudioManager.Instance.EnsureDeviceProbed();
        if (AudioManager.Instance.SilentMode) return null;

        var decoder = OggDecoder.Open(fullPath, ChunkSeconds);
        if (decoder == null) return null;

        try
        {
            var stream = new OggStream(decoder, fullPath, loop);
            stream.Prime();
            return stream;
        }
        catch (NoAudioHardwareException)
        {
            decoder.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            decoder.Dispose();
            Console.WriteLine($"[OggStream] failed to prepare playback: {fullPath} - {ex.Message}");
            return null;
        }
    }

    private void Prime()
    {
        for (int i = 0; i < TargetPending; i++)
            if (!FeedOne()) break;
    }

    public void Update()
    {
        if (_disposed || Instance.IsDisposed) return;

        while (!_eof && Instance.PendingBufferCount < TargetPending)
            if (!FeedOne()) break;

        if (_eof && Instance.PendingBufferCount == 0 && Instance.State == SoundState.Playing)
            Instance.Stop();
    }

    private bool FeedOne()
    {
        int bytes = _decoder.Read(_pcm);

        if (bytes == 0)
        {
            if (!IsLooped) { _eof = true; return false; }

            _decoder.Rewind();
            bytes = _decoder.Read(_pcm);
            if (bytes == 0) { _eof = true; return false; }
        }

        Instance.SubmitBuffer(_pcm, 0, bytes);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (!Instance.IsDisposed)
        {
            if (Instance.State != SoundState.Stopped) Instance.Stop();
            Instance.Dispose();
        }
        _decoder.Dispose();
    }
}

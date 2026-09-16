using System;

namespace PixelCore.Runtime.Audio;

internal sealed class OggDecoder : IDisposable
{
    private IntPtr _vorbis;
    private readonly float[] _floats;
    private bool _disposed;

    public int SampleRate { get; }

    public int Channels { get; }

    public int FramesPerChunk { get; }

    public int ChunkBytes => FramesPerChunk * Channels * 2;

    private OggDecoder(IntPtr vorbis, int sampleRate, int channels, int framesPerChunk)
    {
        _vorbis = vorbis;
        SampleRate = sampleRate;
        Channels = channels;
        FramesPerChunk = framesPerChunk;
        _floats = new float[framesPerChunk * channels];
    }

    internal static int OpenAttempts;

    public static OggDecoder? Open(string fullPath, float chunkSeconds)
    {
        OpenAttempts++;
        IntPtr handle = IntPtr.Zero;
        try
        {
            handle = FAudio.stb_vorbis_open_filename(fullPath, out int error, IntPtr.Zero);
            if (handle == IntPtr.Zero)
            {
                Console.WriteLine($"[OggDecoder] failed to open (stb_vorbis error {error}): {fullPath}");
                return null;
            }

            var info = FAudio.stb_vorbis_get_info(handle);
            if (info.sample_rate == 0 || info.channels <= 0)
            {
                Console.WriteLine($"[OggDecoder] suspicious header ({info.sample_rate}Hz {info.channels}ch): {fullPath}");
                FAudio.stb_vorbis_close(handle);
                return null;
            }

            int channels = Math.Min(info.channels, 2);
            int frames = Math.Max(1, (int)(info.sample_rate * chunkSeconds));

            return new OggDecoder(handle, (int)info.sample_rate, channels, frames);
        }
        catch (Exception ex)
        {
            if (handle != IntPtr.Zero) FAudio.stb_vorbis_close(handle);
            Console.WriteLine($"[OggDecoder] failed to open: {fullPath} - {ex.Message}");
            return null;
        }
    }

    public int Read(byte[] pcm)
    {
        if (_disposed || _vorbis == IntPtr.Zero) return 0;

        int frames = FAudio.stb_vorbis_get_samples_float_interleaved(
            _vorbis, Channels, _floats, _floats.Length);
        if (frames <= 0) return 0;

        int samples = frames * Channels;
        for (int i = 0; i < samples; i++)
        {
            int v = (int)(Math.Clamp(_floats[i], -1f, 1f) * short.MaxValue);
            pcm[i * 2] = (byte)(v & 0xFF);
            pcm[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return samples * 2;
    }

    public void Rewind()
    {
        if (_disposed || _vorbis == IntPtr.Zero) return;
        FAudio.stb_vorbis_seek_start(_vorbis);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_vorbis != IntPtr.Zero)
        {
            FAudio.stb_vorbis_close(_vorbis);
            _vorbis = IntPtr.Zero;
        }
    }
}

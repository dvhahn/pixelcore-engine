using System;
using System.Collections.Generic;
using System.IO;

namespace PixelCore.Runtime.Audio;

public static class AudioFileInfo
{
    private static readonly Dictionary<string, (DateTime Stamp, int Channels)> _cache = new();

    public static int Channels(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath)) return 0;

        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(fullPath); }
        catch { return 0; }

        if (_cache.TryGetValue(fullPath, out var hit) && hit.Stamp == stamp) return hit.Channels;

        int channels = Path.GetExtension(fullPath).ToLowerInvariant() switch
        {
            ".ogg" => OggChannels(fullPath),
            ".wav" => WavChannels(fullPath),
            _      => 0,
        };

        _cache[fullPath] = (stamp, channels);
        return channels;
    }

    public static bool IsStereo(string fullPath) => Channels(fullPath) >= 2;

    private static int OggChannels(string fullPath)
    {
        try
        {
            using var fs = File.OpenRead(fullPath);
            Span<byte> head = stackalloc byte[64];
            if (fs.Read(head) < 28) return 0;
            if (head[0] != (byte)'O' || head[1] != (byte)'g' ||
                head[2] != (byte)'g' || head[3] != (byte)'S') return 0;

            int segCount = head[26];
            int packet = 27 + segCount;
            fs.Position = packet;

            Span<byte> id = stackalloc byte[12];
            if (fs.Read(id) < 12) return 0;
            if (id[0] != 0x01 || id[1] != (byte)'v' || id[2] != (byte)'o' || id[3] != (byte)'r' ||
                id[4] != (byte)'b' || id[5] != (byte)'i' || id[6] != (byte)'s') return 0;

            return id[11];
        }
        catch { return 0; }
    }

    private static int WavChannels(string fullPath)
    {
        try
        {
            using var fs = File.OpenRead(fullPath);
            using var br = new BinaryReader(fs);
            if (br.ReadUInt32() != 0x46464952u) return 0;
            br.ReadUInt32();
            if (br.ReadUInt32() != 0x45564157u) return 0;

            while (fs.Position + 8 <= fs.Length)
            {
                uint id = br.ReadUInt32();
                uint size = br.ReadUInt32();
                if (id == 0x20746D66u)
                {
                    br.ReadUInt16();
                    return br.ReadUInt16();
                }
                fs.Position += size + (size & 1);
            }
        }
        catch {  }
        return 0;
    }
}

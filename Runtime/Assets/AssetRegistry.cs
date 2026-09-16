using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Assets;

public class AssetEntry
{
    [JsonPropertyName("Path")]
    public string Path { get; set; } = "";
    [JsonPropertyName("Hash")]
    public string Hash { get; set; } = "";
}

public class AssetRegistry
{
    private static AssetRegistry? _instance;
    public static AssetRegistry Instance => _instance ??= new AssetRegistry();

    private Dictionary<string, AssetEntry> _entries = new();
    private Dictionary<string, string> _pathToId = new();
    private string _registryPath = "Content/assets.json";
    private bool _isDirty;

    public IReadOnlyDictionary<string, AssetEntry> Entries => _entries;

    private AssetRegistry()
    {
        Load();
    }

#if DEBUG
    private AssetRegistry(string registryPath)
    {
        _registryPath = registryPath;
        Load();
    }

    internal static IDisposable UseTemporary(string registryPath)
    {
        var previous = _instance;
        _instance = new AssetRegistry(registryPath);
        return new InstanceScope(previous);
    }

    private sealed class InstanceScope : IDisposable
    {
        private readonly AssetRegistry? _previous;
        public InstanceScope(AssetRegistry? previous) => _previous = previous;
        public void Dispose() => _instance = _previous;
    }
#endif

    public string? GetPath(string id)
    {
        return _entries.TryGetValue(id, out var e) ? e.Path : null;
    }

    public string? GetHash(string id)
    {
        return _entries.TryGetValue(id, out var e) ? e.Hash : null;
    }

    public void SetHash(string id, string hash)
    {
        if (_entries.TryGetValue(id, out var e) && e.Hash != hash)
        {
            e.Hash = hash;
            _isDirty = true;
        }
    }

    public string GetOrCreateId(string path)
    {
        var normalizedPath = NormalizePath(path);

        if (_pathToId.TryGetValue(normalizedPath, out var existingId))
            return existingId;

        var newId = GenerateId();
        _entries[newId] = new AssetEntry { Path = normalizedPath };
        _pathToId[normalizedPath] = newId;
        _isDirty = true;

        return newId;
    }

    public string? GetId(string path)
    {
        var normalizedPath = NormalizePath(path);
        return _pathToId.TryGetValue(normalizedPath, out var id) ? id : null;
    }

    public static readonly string[] AudioExtensions = { ".ogg", ".wav", ".mp3" };

    private static readonly string[] PlayableAudioExtensions = { ".ogg", ".wav" };

    public string? GetAudioIdByLegacyPath(string? legacyPath)
    {
        if (string.IsNullOrWhiteSpace(legacyPath)) return null;

        if (GetId(legacyPath) is { } direct) return direct;

        foreach (var ext in PlayableAudioExtensions)
            if (GetId(legacyPath + ext) is { } id) return id;

        return null;
    }

    public static bool LooksLikeId(string? value)
    {
        if (value == null || value.Length != IdLength) return false;
        foreach (var c in value)
            if (c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) return false;
        return true;
    }

    public string? FindSceneIdByName(string? sceneName, out IReadOnlyList<string> candidates)
    {
        var found = new List<string>();
        candidates = found;
        if (string.IsNullOrWhiteSpace(sceneName)) return null;

        foreach (var (id, entry) in _entries)
        {
            var p = entry.Path;
            if (!p.EndsWith(".scene", System.StringComparison.OrdinalIgnoreCase)) continue;

            int slash = p.LastIndexOfAny(new[] { '/', '\\' });
            var bare = p.Substring(slash + 1, p.Length - slash - 1 - ".scene".Length);
            if (string.Equals(bare, sceneName, System.StringComparison.OrdinalIgnoreCase))
                found.Add(id);
        }

        found.Sort(System.StringComparer.Ordinal);
        return found.Count == 1 ? found[0] : null;
    }

    public string? FindSceneIdByName(string? sceneName) => FindSceneIdByName(sceneName, out _);

    public string? FindAudioIdByName(string? name, out IReadOnlyList<string> candidates)
    {
        var found = new List<string>();
        candidates = found;
        if (string.IsNullOrWhiteSpace(name)) return null;

        foreach (var (id, entry) in _entries)
        {
            var p = entry.Path;

            int dot = p.LastIndexOf('.');
            if (dot < 0) continue;
            var ext = p.Substring(dot);
            bool isAudio = false;
            foreach (var a in PlayableAudioExtensions)
                if (string.Equals(ext, a, StringComparison.OrdinalIgnoreCase)) { isAudio = true; break; }
            if (!isAudio) continue;

            int slash = p.LastIndexOfAny(new[] { '/', '\\' });
            var bare = p.Substring(slash + 1, dot - slash - 1);
            if (string.Equals(bare, name, StringComparison.OrdinalIgnoreCase))
                found.Add(id);
        }

        found.Sort(StringComparer.Ordinal);
        return found.Count == 1 ? found[0] : null;
    }

    public void Register(string id, string path)
    {
        var p = NormalizePath(path);
        if (_pathToId.TryGetValue(p, out var other) && other != id)
            _entries.Remove(other);
        _entries[id] = new AssetEntry { Path = p, Hash = GetHash(id) ?? "" };
        _pathToId[p] = id;
        _isDirty = true;
    }

    public void UpdatePath(string id, string newPath)
    {
        if (!_entries.TryGetValue(id, out var entry))
            return;

        var normalizedNewPath = NormalizePath(newPath);

        _pathToId.Remove(entry.Path);

        entry.Path = normalizedNewPath;
        _pathToId[normalizedNewPath] = id;
        _isDirty = true;
    }

    public void Remove(string id)
    {
        if (_entries.TryGetValue(id, out var entry))
        {
            _entries.Remove(id);
            _pathToId.Remove(entry.Path);
            _isDirty = true;
        }
    }

    public void RemoveByPath(string path)
    {
        var normalizedPath = NormalizePath(path);
        if (_pathToId.TryGetValue(normalizedPath, out var id))
        {
            _entries.Remove(id);
            _pathToId.Remove(normalizedPath);
            _isDirty = true;
        }
    }

    public bool Save()
    {
        if (!_isDirty) return true;

        try
        {
            var directory = Path.GetDirectoryName(_registryPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var ordered = new SortedDictionary<string, AssetEntry>(_entries, StringComparer.Ordinal);
            var json = JsonSerializer.Serialize(ordered, AssetJsonContext.Default.SortedDictionaryStringAssetEntry);
            AtomicFile.WriteAllText(_registryPath, json);
            _isDirty = false;
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AssetRegistry] registry save failed: {_registryPath} - {ex.Message}");
            return false;
        }
    }

    public bool IsDirty => _isDirty;

    public void Load()
    {
        try
        {
            if (!File.Exists(_registryPath))
            {
                _entries = new Dictionary<string, AssetEntry>();
                _pathToId = new Dictionary<string, string>();
                return;
            }

            var json = File.ReadAllText(_registryPath);
            try
            {
                _entries = JsonSerializer.Deserialize(json, AssetJsonContext.Default.DictionaryStringAssetEntry)
                           ?? new Dictionary<string, AssetEntry>();
            }
            catch (JsonException)
            {
                var flat = JsonSerializer.Deserialize(json, AssetJsonContext.Default.DictionaryStringString)
                           ?? new Dictionary<string, string>();
                _entries = new Dictionary<string, AssetEntry>();
                foreach (var kvp in flat)
                    _entries[kvp.Key] = new AssetEntry { Path = kvp.Value };
                _isDirty = true;
            }

            _pathToId = new Dictionary<string, string>();
            foreach (var kvp in _entries)
            {
                _pathToId[kvp.Value.Path] = kvp.Key;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AssetRegistry] Load failed: {ex.Message}");
            _entries = new Dictionary<string, AssetEntry>();
            _pathToId = new Dictionary<string, string>();
        }
    }

    private string NormalizePath(string path)
    {
        var p = path.Replace('\\', '/');
        if (Path.IsPathRooted(p))
        {
            var cwd = Path.GetFullPath(Environment.CurrentDirectory).Replace('\\', '/').TrimEnd('/') + "/";
            var full = Path.GetFullPath(p).Replace('\\', '/');
            p = full.StartsWith(cwd, StringComparison.Ordinal) ? full.Substring(cwd.Length) : full;
            if (Path.IsPathRooted(p)) return p;
        }
        p = p.TrimStart('/');
        if (p.StartsWith(ContentPrefix, StringComparison.OrdinalIgnoreCase))
            p = p.Substring(ContentPrefix.Length);
        return p;
    }

    private const string ContentPrefix = "Content/";

    public const int IdLength = 8;

    public string NewId() => NewId(() => Guid.NewGuid().ToString("N")[..IdLength]);

    internal string NewId(Func<string> candidateSource)
    {
        while (true)
        {
            var candidate = candidateSource();
            if (!_entries.ContainsKey(candidate)) return candidate;
        }
    }

    private string GenerateId() => NewId();
}

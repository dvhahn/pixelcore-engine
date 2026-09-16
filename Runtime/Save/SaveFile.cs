using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace PixelCore.Runtime.Save;

public enum SaveSource
{
    None,
    Primary,
    Old,
    Tmp,
}

public class SaveReadResult
{
    public SaveData? Data { get; set; }
    public SaveSource Source { get; set; } = SaveSource.None;

    public List<string> Problems { get; } = new();

    public bool AutoRecovered => Data != null && Source != SaveSource.Primary;

    public bool NotFound { get; set; }

    public bool Ok => Data != null;
}

public class SaveWriteResult
{
    public bool Ok { get; set; }
    public string Path { get; set; } = "";
    public string Error { get; set; } = "";
}

public static class SaveFile
{
    public const string AutoSlot = "auto";

    private const string Extension = ".save";
    private const string OldSuffix = "_old";
    private const string TmpSuffix = "_tmp";

    private static string? _dirOverride;

    private static readonly JsonSerializerOptions JsonOptions = new(SaveJsonContext.Default.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static JsonTypeInfo<SaveData> TypeInfo
        => (JsonTypeInfo<SaveData>)JsonOptions.GetTypeInfo(typeof(SaveData));

    public static string Dir
    {
        get
        {
            if (_dirOverride != null) return _dirOverride;

            var env = Environment.GetEnvironmentVariable("PIXELCORE_SAVE_DIR");
            if (!string.IsNullOrEmpty(env)) return env;

            string root = OperatingSystem.IsMacOS()
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support")
                : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            return Path.Combine(root, "PixelCore", "Saves");
        }
    }

    public static IDisposable UseTemporary(string dir)
    {
        var previous = _dirOverride;
        _dirOverride = dir;
        return new DirScope(previous);
    }

    private sealed class DirScope : IDisposable
    {
        private readonly string? _previous;
        public DirScope(string? previous) => _previous = previous;
        public void Dispose() => _dirOverride = _previous;
    }

    public static string PathOf(string slot) => Path.Combine(Dir, slot + Extension);
    public static string OldPathOf(string slot) => PathOf(slot) + OldSuffix;
    public static string TmpPathOf(string slot) => PathOf(slot) + TmpSuffix;

    public static bool Exists(string slot)
        => File.Exists(PathOf(slot)) || File.Exists(OldPathOf(slot)) || File.Exists(TmpPathOf(slot));

    public static SaveWriteResult Write(string slot, SaveData data)
    {
        var result = new SaveWriteResult { Path = PathOf(slot) };
        string primary = PathOf(slot), old = OldPathOf(slot), tmp = TmpPathOf(slot);

        try
        {
            Directory.CreateDirectory(Dir);

            string json = JsonSerializer.Serialize(data, TypeInfo);
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }

            if (File.Exists(primary)) File.Move(primary, old, overwrite: true);

            File.Move(tmp, primary, overwrite: true);

            result.Ok = true;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            Console.WriteLine($"[Save] ✘ write failed: {primary} - {ex.Message}");
        }

        return result;
    }

    public static SaveReadResult Read(string slot)
    {
        var result = new SaveReadResult();

        var candidates = new (string Path, SaveSource Source, string Label)[]
        {
            (PathOf(slot),    SaveSource.Primary, "the primary file"),
            (OldPathOf(slot), SaveSource.Old,     "the previous backup (_old)"),
            (TmpPathOf(slot), SaveSource.Tmp,     "a partially written file (_tmp)"),
        };

        bool anyFileSeen = false;

        foreach (var (path, source, label) in candidates)
        {
            if (!File.Exists(path)) continue;
            anyFileSeen = true;

            var data = TryParse(path, label, result.Problems);
            if (data == null) continue;

            result.Data = data;
            result.Source = source;

            if (source != SaveSource.Primary)
            {
                string note = $"The primary save could not be read; recovered from {label}.";
                result.Problems.Add(note);
                Console.WriteLine($"[Save] ⚠ {note}");
            }
            break;
        }

        if (result.Data == null)
        {
            result.NotFound = !anyFileSeen;
            if (anyFileSeen)
                Console.WriteLine($"[Save] ✘ cannot open slot '{slot}' - files exist but none of the three are readable");
        }
        else
        {
            Migrate(result);
        }

        return result;
    }

    private static SaveData? TryParse(string path, string label, List<string> problems)
    {
        try
        {
            string json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize(json, TypeInfo);
            if (data == null)
            {
                problems.Add($"{label}: the contents are empty ({System.IO.Path.GetFileName(path)})");
                return null;
            }
            return data;
        }
        catch (Exception ex)
        {
            problems.Add($"{label}: read failed - {ex.Message}");
            Console.WriteLine($"[Save] ⚠ {label} read failed: {ex.Message}");
            return null;
        }
    }

    private static void Migrate(SaveReadResult result)
    {
        var data = result.Data!;

        if (data.Version > SaveSchema.CurrentVersion)
        {
            string note = $"This save was made by a newer version (file v{data.Version} > build v{SaveSchema.CurrentVersion}) and cannot be opened.";
            result.Problems.Add(note);
            result.Data = null;
            result.Source = SaveSource.None;
            Console.WriteLine($"[Save] ✘ {note}");
            return;
        }

        int before = data.LastAppliedFix;
        SaveFixes.ApplyAll(data, result.Problems);
        if (data.LastAppliedFix != before)
            Console.WriteLine($"[Save] migrations applied: fix {before} -> {data.LastAppliedFix}");

        data.Version = SaveSchema.CurrentVersion;
    }

    public static void Delete(string slot)
    {
        foreach (var p in new[] { PathOf(slot), OldPathOf(slot), TmpPathOf(slot) })
        {
            try { if (File.Exists(p)) File.Delete(p); }
            catch (Exception ex) { Console.WriteLine($"[Save] ⚠ delete failed: {p} - {ex.Message}"); }
        }
    }
}

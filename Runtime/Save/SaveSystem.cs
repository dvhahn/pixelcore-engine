using System;
using System.Collections.Generic;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Cutscenes;
using PixelCore.Runtime.Story;

namespace PixelCore.Runtime.Save;

public readonly struct SaveNotice
{
    public string Checkpoint { get; init; }
    public bool Ok { get; init; }
    public string Error { get; init; }
}

public class SaveLoadResult
{
    public bool Ok { get; set; }
    public bool NotFound { get; set; }
    public bool AutoRecovered { get; set; }
    public string Checkpoint { get; set; } = "";
    public List<string> Problems { get; } = new();
}

public class SaveSystem
{
    private GameContext? _ctx;

    public double PlaySeconds { get; private set; }

    public List<string> Achievements { get; } = new();

    public string LastCheckpoint { get; private set; } = "";

    public Action<SaveData>? CaptureGameState;

    public Func<SaveData, bool>? ApplyGameState;

    public Func<string?>? GameBusyReason;

    public event Action<SaveNotice>? Saved;

    public void Bind(GameContext ctx) => _ctx = ctx;

    public void Update(float unscaledDeltaTime) => PlaySeconds += unscaledDeltaTime;

    public bool HasSave => SaveFile.Exists(SaveFile.AutoSlot);

    public string? BusyReason()
    {
        if (CutsceneDirector.IsPlaying)
            return $"a cutscene is playing ({CutsceneDirector.CurrentId})";
        if (DialogueRunner.Active)
            return $"dialogue is running ({DialogueRunner.CurrentBlockId})";

        string? game = GameBusyReason?.Invoke();
        return string.IsNullOrEmpty(game) ? null : game;
    }

    public bool CanCheckpoint => BusyReason() == null;

    public bool Checkpoint(string checkpointId)
    {
        if (_ctx == null)
        {
            Console.WriteLine("[Save] ✘ Checkpoint called without SaveSystem.Bind");
            return false;
        }

        string? busy = BusyReason();
        if (busy != null)
        {
            Console.WriteLine($"[Save] ✘ checkpoint '{checkpointId}' refused - {busy}. " +
                              "Checkpoints are taken only between cutscenes, dialogue and transitions.");
#if DEBUG
            Console.WriteLine("[Save]   call site:\n" + Environment.StackTrace);
#endif
            Saved?.Invoke(new SaveNotice { Checkpoint = checkpointId, Ok = false, Error = busy });
            return false;
        }

        var data = Capture(checkpointId);
        var write = SaveFile.Write(SaveFile.AutoSlot, data);

        if (write.Ok)
        {
            LastCheckpoint = checkpointId;
            Console.WriteLine($"[Save] checkpoint '{checkpointId}' saved " +
                              $"({data.Flags.Count} flags, {data.Expiring.Count} expiry bookings)");
        }

        Saved?.Invoke(new SaveNotice { Checkpoint = checkpointId, Ok = write.Ok, Error = write.Error });
        return write.Ok;
    }

    public SaveData Capture(string checkpointId)
    {
        var data = new SaveData
        {
            Checkpoint = checkpointId,
            SavedAtUtc = DateTime.UtcNow.ToString("o"),
            LastAppliedFix = SaveFixes.MaxFix,
            Flags = CaptureFlags(_ctx!.Blackboard),
            Expiring = _ctx.FlagExpiry.Capture(),
            Stats = new StatsSave
            {
                PlaySeconds = PlaySeconds,
                Achievements = new List<string>(Achievements),
            },
        };

        CaptureGameState?.Invoke(data);
        return data;
    }

    private static FlagsSave CaptureFlags(Blackboard blackboard)
    {
        var flags = new FlagsSave();
        List<string>? unsupported = null;

        foreach (var (key, value) in blackboard.All)
        {
            switch (value)
            {
                case bool b: flags.Bools[key] = b; break;
                case int i: flags.Ints[key] = i; break;
                case float f: flags.Floats[key] = f; break;
                case string s: flags.Strings[key] = s; break;
                default:
                    (unsupported ??= new List<string>()).Add($"{key} ({value?.GetType().Name ?? "null"})");
                    break;
            }
        }

        if (unsupported != null)
        {
            Console.WriteLine($"[Save] ⚠ {unsupported.Count} value(s) the save cannot hold - " +
                              $"they will disappear on load: {string.Join(", ", unsupported)}");
            Console.WriteLine("[Save]   put only bool, int, float and string into the blackboard.");
        }

        return flags;
    }

    public SaveLoadResult LoadLatest()
    {
        var result = new SaveLoadResult();

        if (_ctx == null)
        {
            result.Problems.Add("SaveSystem.Bind was never called");
            Console.WriteLine("[Save] ✘ LoadLatest called without SaveSystem.Bind");
            return result;
        }

        var read = SaveFile.Read(SaveFile.AutoSlot);
        result.Problems.AddRange(read.Problems);
        result.AutoRecovered = read.AutoRecovered;
        result.NotFound = read.NotFound;

        if (!read.Ok)
        {
            if (read.NotFound) Console.WriteLine("[Save] there is no saved game");
            return result;
        }

        var data = read.Data!;

        var undo = Snapshot();

        _ctx.Blackboard.LoadFrom(ToBlackboard(data.Flags, result.Problems));

        _ctx.FlagExpiry.LoadFrom(data.Expiring, result.Problems);

        PlaySeconds = data.Stats.PlaySeconds;
        Achievements.Clear();
        Achievements.AddRange(data.Stats.Achievements);
        LastCheckpoint = data.Checkpoint;
        result.Checkpoint = data.Checkpoint;

        if (ApplyGameState == null)
        {
            result.Problems.Add("the game-state apply hook is not wired (SaveSystem.ApplyGameState)");
            Console.WriteLine("[Save] ✘ ApplyGameState is not wired - the save was read but cannot be entered");
            Rollback(undo, ref result);
            return result;
        }

        if (!ApplyGameState(data))
        {
            result.Problems.Add("could not enter the saved state (check the room and phase)");
            Console.WriteLine("[Save] ✘ failed to enter the save");
            Rollback(undo, ref result);
            return result;
        }

        result.Ok = true;
        Console.WriteLine($"[Save] loaded '{data.Checkpoint}' " +
                          $"(day {data.Time.Day}, {data.Time.Phase}, {data.Flags.Count} flags)" +
                          (read.AutoRecovered ? " ⚠ recovered from a backup" : ""));
        return result;
    }

    private readonly record struct LoadUndo(
        Dictionary<string, object> Flags,
        List<ExpiringFlagSave> Expiring,
        double PlaySeconds,
        List<string> Achievements,
        string Checkpoint);

    private LoadUndo Snapshot() => new(
        new Dictionary<string, object>(_ctx!.Blackboard.All, StringComparer.Ordinal),
        _ctx.FlagExpiry.Capture(),
        PlaySeconds,
        new List<string>(Achievements),
        LastCheckpoint);

    private void Rollback(LoadUndo undo, ref SaveLoadResult result)
    {
        _ctx!.Blackboard.LoadFrom(undo.Flags);
        _ctx.FlagExpiry.LoadFrom(undo.Expiring);
        PlaySeconds = undo.PlaySeconds;
        Achievements.Clear();
        Achievements.AddRange(undo.Achievements);
        LastCheckpoint = undo.Checkpoint;
        result.Checkpoint = undo.Checkpoint;

        Console.WriteLine("[Save] entry failed, so the session state was restored "
                        + $"({undo.Flags.Count} flags, checkpoint '{undo.Checkpoint}'). "
                        + "The room on screen is unchanged, and pressing continue again is safe");
    }

    private static Dictionary<string, object> ToBlackboard(FlagsSave flags, List<string> problems)
    {
        var values = new Dictionary<string, object>();

        AddAll(values, flags.Bools, "bool", problems);
        AddAll(values, flags.Ints, "int", problems);
        AddAll(values, flags.Floats, "float", problems);
        AddAll(values, flags.Strings, "string", problems);

        return values;
    }

    private static void AddAll<T>(Dictionary<string, object> into, Dictionary<string, T> from,
                                  string typeName, List<string> problems) where T : notnull
    {
        foreach (var (key, value) in from)
        {
            if (into.TryGetValue(key, out var existing))
            {
                string note = $"flag '{key}' is stored under two types " +
                              $"(keeping {existing.GetType().Name}, ignoring {typeName})";
                problems.Add(note);
                Console.WriteLine($"[Save] ⚠ {note}");
                continue;
            }
            into[key] = value;
        }
    }

    public void ResetForNewGame()
    {
        PlaySeconds = 0;
        Achievements.Clear();
        LastCheckpoint = "";
        _ctx?.Blackboard.Clear();
        _ctx?.FlagExpiry.LoadFrom(Array.Empty<ExpiringFlagSave>());
    }

    public void DeleteSave() => SaveFile.Delete(SaveFile.AutoSlot);
}

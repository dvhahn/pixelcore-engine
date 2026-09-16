#if DEBUG
using System;
using System.Collections.Generic;
using PixelCore.Gameplay.Player;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Save;

namespace PixelCore.Gameplay.Systems;

public static class SaveHarness
{
    private static readonly string? Env = Environment.GetEnvironmentVariable("PIXELCORE_SAVETEST");
    public static bool Enabled => !string.IsNullOrEmpty(Env);

    public static bool ExitRequested { get; private set; }

    public static int ExitCode { get; private set; }

    private const float StartDelay = 1.0f;
    private const float StepInterval = 0.25f;

    private static float _elapsed;
    private static int _step;
    private static int _pass, _fail;

    private static IDisposable? _saveDirScope;
    private static string? _tempSaveDir;

    private static readonly FlagKey TestFlag = new("test.harness.phase", FlagLifetime.Phase);

    public static void Update(float deltaTime, GameContext ctx)
    {
        if (!Enabled || ExitRequested) return;

        _elapsed += deltaTime;
        if (_elapsed < StartDelay + _step * StepInterval) return;

        switch (_step++)
        {
            case 0: NewGame(ctx); break;
            case 1: MakeState(ctx); break;
            case 2: Checkpoint(ctx); break;
            case 3: Disturb(ctx); break;
            case 4: Load(ctx); break;
            case 5: Verify(ctx); break;
        }
    }

    private static void NewGame(GameContext ctx)
    {
        Console.WriteLine("=== Save harness (real round trip) ===");
        IsolateSaveFolder();
        ctx.Save.ResetForNewGame();

        Check("new game: StartDay(1, Morning) succeeds", GameFlow.StartDay(1, DayPhase.Morning));
        Check("a room really opened", !string.IsNullOrEmpty(ctx.Scene?.Name), ctx.Scene?.Name);
        Check("start room = the room in the table", ctx.Scene?.Name == Rooms.Village.Name, ctx.Scene?.Name);
        Check("the phase decides the clock (morning, 8h)", Math.Abs(ctx.Clock.Hour - 8f) < 0.01f, ctx.Clock.Hour.ToString());
        Check("input locks start released",
              !Runtime.Cutscenes.FreezeState.IsFrozen(Runtime.Cutscenes.FreezeFlags.PlayerInputSoft)
              && !Runtime.Cutscenes.FreezeState.IsFrozen(Runtime.Cutscenes.FreezeFlags.PlayerInput));
    }

    private static void MakeState(GameContext ctx)
    {
        ctx.Blackboard.SetBool(Flags.Village.StoneTouched, true);
        ctx.Blackboard.SetBool(TestFlag, true);
        ctx.Blackboard.SetInt("test.harness.count", 3);
        PlayerController.Of(ctx.Scene)!.Facing = new Microsoft.Xna.Framework.Vector2(-1f, 0f);

        Check("two expiry reservations were taken (permanent keys take none)", ctx.FlagExpiry.PendingCount == 2,
            ctx.FlagExpiry.PendingCount.ToString());
    }

    private static void Checkpoint(GameContext ctx)
    {
        Check("a checkpoint is taken (in the safe state)", ctx.Save.Checkpoint("harness.day1.morning"));
        Check("the save file was created", ctx.Save.HasSave);
    }

    private static void Disturb(GameContext ctx)
    {
        Check("move to a different phase and a different room",
              GameFlow.StartDay(1, DayPhase.Afternoon, Rooms.House.SceneId, "Spawn_FromVillage"));
        Check("a different room really opened", ctx.Scene?.Name == Rooms.House.Name, ctx.Scene?.Name);

        ctx.Blackboard.Remove(Flags.Village.StoneTouched);
        ctx.Blackboard.SetInt("test.harness.count", 99);
        PlayerController.Of(ctx.Scene)!.Facing = new Microsoft.Xna.Framework.Vector2(0f, -1f);
        GameFlow.Costume = "scrambled";

        Check("scrambling confirmed: the flag was cleared", !ctx.Blackboard.GetBool(Flags.Village.StoneTouched));
    }

    private static void Load(GameContext ctx)
    {
        var result = ctx.Save.LoadLatest();
        Check("load succeeds", result.Ok, string.Join(" / ", result.Problems));
        Check("opened from the primary file (not the backup fallback)", !result.AutoRecovered);
        Check("the checkpoint label comes back", result.Checkpoint == "harness.day1.morning", result.Checkpoint);
    }

    private static void Verify(GameContext ctx)
    {
        Check("* back in the room that was saved", ctx.Scene?.Name == Rooms.Village.Name, ctx.Scene?.Name);
        Check("* the time came back (day 1, morning)",
            GameFlow.Day == 1 && GameFlow.Phase == DayPhase.Morning, $"{GameFlow.Day}/{GameFlow.Phase}");
        Check("* the clock follows too (morning, 8h)", Math.Abs(ctx.Clock.Hour - 8f) < 0.01f, ctx.Clock.Hour.ToString());
        Check("* the flag came back", ctx.Blackboard.GetBool(Flags.Village.StoneTouched));
        Check("* the scrambled value returned to its saved state", ctx.Blackboard.GetInt("test.harness.count") == 3,
            ctx.Blackboard.GetInt("test.harness.count").ToString());
        Check("* the expiry reservations were restored too", ctx.FlagExpiry.PendingCount == 2,
            ctx.FlagExpiry.PendingCount.ToString());
        Check("* no expiry fires in the same phase right after loading",
            ctx.Blackboard.GetBool(TestFlag));
        var facing = PlayerController.Of(ctx.Scene)?.Facing ?? Microsoft.Xna.Framework.Vector2.Zero;
        Check("the facing is restored (staging)", facing.X < 0f, facing.ToString());
        Check("the costume is restored too", GameFlow.Costume == "default", GameFlow.Costume);
        Check("locks are never saved and always start released (soft and hard)",
              !Runtime.Cutscenes.FreezeState.IsFrozen(Runtime.Cutscenes.FreezeFlags.PlayerInputSoft)
              && !Runtime.Cutscenes.FreezeState.IsFrozen(Runtime.Cutscenes.FreezeFlags.PlayerInput)
              && !Runtime.UI.DialogueBox.Active);

        var player = ctx.Scene?.FindPlayer();
        var spawn = ctx.Scene?.FindEntity("Spawn_Start");
        if (player != null && spawn != null)
        {
            var pp = player.GetComponent<Runtime.Components.Transform>()!.Position;
            var sp = spawn.GetComponent<Runtime.Components.Transform>()!.Position;
            Check("* the player was placed at the saved spawn point",
                Math.Abs(pp.X - sp.X) < 24f && Math.Abs(pp.Y - sp.Y) < 24f, $"{pp} vs {sp}");
        }
        else
        {
            Check("* the player was placed at the saved spawn point", false,
                player == null ? "no player" : "no Spawn_Start");
        }

        Console.WriteLine($"=== {_pass} passed, {_fail} failed ===");

        ReleaseSaveFolder();
        ExitCode = _fail;
        ExitRequested = true;
    }

    private static void IsolateSaveFolder()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PIXELCORE_SAVE_DIR")))
        {
            Console.WriteLine($"[Harness] save folder: {SaveFile.Dir} (from env)");
            return;
        }

        _tempSaveDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "pixelcore_savetest_" + Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(_tempSaveDir);
        _saveDirScope = SaveFile.UseTemporary(_tempSaveDir);
        Console.WriteLine($"[Harness] save folder: {_tempSaveDir} (temporary - the real save is untouched)");
    }

    private static void ReleaseSaveFolder()
    {
        _saveDirScope?.Dispose();
        _saveDirScope = null;

        if (_tempSaveDir == null) return;
        try { System.IO.Directory.Delete(_tempSaveDir, true); }
        catch (Exception ex) { Console.WriteLine($"[Harness] temp folder cleanup failed: {ex.Message}"); }
        _tempSaveDir = null;
    }

    private static void Check(string label, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"  ✔ {label}"); }
        else { _fail++; Console.WriteLine($"  ✘ {label}" + (detail != null ? $"  ← {detail}" : "")); }
    }
}
#endif

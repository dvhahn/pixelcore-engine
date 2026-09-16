#if !DEBUG
System.Environment.CurrentDirectory = System.AppContext.BaseDirectory;
#endif

PixelCore.Runtime.Core.CrashHandler.Install();

#if DEBUG
if (System.Environment.GetEnvironmentVariable("PIXELCORE_CRASH_TEST") == "1")
    throw new System.InvalidOperationException("deliberate crash (PIXELCORE_CRASH_TEST=1) - verifies the hook");
#endif

#if DEBUG
PixelCore.Editor.EditorConsole.Install();
#endif

System.Environment.SetEnvironmentVariable("FNA_GRAPHICS_ENABLE_HIGHDPI", "1");

System.Environment.SetEnvironmentVariable("FNA_KEYBOARD_USE_SCANCODES", "1");

#if DEBUG
if (System.Environment.GetEnvironmentVariable("PIXELCORE_STAMP") is string stampMode && stampMode.Length > 0)
{
    System.Environment.ExitCode = PixelCore.Gameplay.Systems.LocStampHarness.Run(stampMode);
    return;
}
#endif

if (PixelCore.Runtime.Audio.AudioNoDeviceHarness.Enabled)
{
    System.Environment.ExitCode = PixelCore.Runtime.Audio.AudioNoDeviceHarness.Run();
    return;
}

if (System.Environment.GetEnvironmentVariable("PIXELCORE_SELFTEST") == "1")
{
    PixelCore.Runtime.Assets.TextureLoader.Headless = true;

    PixelCore.Runtime.Audio.AudioSelfTest.PreloadSDL();

    PixelCore.Runtime.Serialization.SerializerSelfTest.Run();
    PixelCore.Runtime.Physics.PhysicsSelfTest.Run();
    PixelCore.Runtime.Core.CameraSelfTest.Run();
    PixelCore.Runtime.Nav.NavSelfTest.Run();
    PixelCore.Runtime.Tilemap.TilemapSelfTest.Run();
    PixelCore.Runtime.Tilemap.TerrainSelfTest.Run();
    PixelCore.Runtime.Animation.AnimationSelfTest.Run();
    PixelCore.Runtime.Core.CrashSelfTest.Run();
    PixelCore.Runtime.Rendering.LightingSelfTest.Run();
    PixelCore.Runtime.Rendering.PostFxSelfTest.Run();
    PixelCore.Runtime.Rendering.SkySelfTest.Run();
    PixelCore.Runtime.Particles.ParticleSelfTest.Run();
    PixelCore.Runtime.Audio.AudioSelfTest.Run();
    PixelCore.Runtime.Audio.FootstepSelfTest.Run();
    PixelCore.Runtime.Text.LocSelfTest.Run();
    PixelCore.Runtime.Story.StorySelfTest.Run();
    PixelCore.Runtime.UI.BubbleSelfTest.Run();
    PixelCore.Runtime.Cutscenes.CutsceneSelfTest.Run();
    PixelCore.Runtime.Save.SaveSelfTest.Run();
#if DEBUG
    PixelCore.Runtime.Core.InputSelfTest.Run();
    PixelCore.Runtime.Core.GamepadSelfTest.Run();
    PixelCore.Runtime.Core.SettingsSelfTest.Run();
    PixelCore.Runtime.Assets.AssetScannerSelfTest.Run();
    PixelCore.Editor.CommandSelfTest.Run();
    PixelCore.Editor.GameLoopGuardSelfTest.Run();
    PixelCore.Editor.PlayHereSelfTest.Run();
    PixelCore.Editor.DoorRefSelfTest.Run();
    PixelCore.Editor.AssetPingSelfTest.Run();
    PixelCore.Editor.ComponentCatalogSelfTest.Run();
    PixelCore.Runtime.Systems.InteractionHighlightSelfTest.Run();
    PixelCore.Editor.ShadowGizmoSelfTest.Run();
    PixelCore.Editor.PlayRoomRestoreSelfTest.Run();
    PixelCore.Editor.InspectorLayoutSelfTest.Run();
    PixelCore.Editor.TilemapEditSelfTest.Run();
    PixelCore.Runtime.UI.TitleSelfTest.Run();
    PixelCore.Runtime.UI.PauseSelfTest.Run();
    PixelCore.Runtime.UI.MonologueSelfTest.Run();
    PixelCore.Gameplay.Systems.PauseMenuSelfTest.Run();
    PixelCore.Gameplay.Player.PlayerAnimSelfTest.Run();
    PixelCore.Gameplay.Combat.CombatSelfTest.Run();
    PixelCore.Gameplay.Systems.FlowSelfTest.Run();
    PixelCore.Gameplay.Systems.LocStampSelfTest.Run();
    PixelCore.Runtime.Core.AtomicFileSelfTest.Run();
    PixelCore.Runtime.Core.GlobalStateSelfTest.Run();
#endif
    System.Console.WriteLine("=== SELFTEST END ===");
    return;
}

{
    string? auditScene = System.Environment.GetEnvironmentVariable("PIXELCORE_AUDIT_AUDIO");
    if (!string.IsNullOrEmpty(auditScene))
    {
        PixelCore.Runtime.Audio.AudioSceneAudit.Run(auditScene);
        return;
    }
}

using var game = new PixelCore.Game1();

PixelCore.Runtime.Platform.MacWindow.ApplyDarkTitlebar(game.Window.Handle);

game.Run();

#if DEBUG
if (PixelCore.Gameplay.Systems.SaveHarness.Enabled)
    System.Environment.ExitCode = PixelCore.Gameplay.Systems.SaveHarness.ExitCode;
#endif

if (PixelCore.Runtime.Cutscenes.CutsceneRegression.Enabled)
    System.Environment.ExitCode = PixelCore.Runtime.Cutscenes.CutsceneRegression.ExitCode;

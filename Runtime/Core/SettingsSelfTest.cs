#if DEBUG
using System;
using System.IO;
using PixelCore.Runtime.Audio;

namespace PixelCore.Runtime.Core;

public static class SettingsSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        Console.WriteLine("=== Settings self-test ===");
        _pass = _fail = 0;

        var dir = Path.Combine(Path.GetTempPath(), "pixelcore_set_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        using var scope = Runtime.Save.SaveFile.UseTemporary(dir);
        try
        {
            TestRoundTrip(dir);
            TestBrokenFile(dir);
            TestApply();
            TestSeparateFromSave(dir);
        }
        finally
        {
            ResetCurrent();
            try { Directory.Delete(dir, true); } catch { }
        }
        Console.WriteLine($"=== Settings: {_pass} passed, {_fail} failed ===");
    }

    private static void TestRoundTrip(string dir)
    {
        ResetCurrent();
        Check("premise: the file does not exist yet", !File.Exists(GameSettings.PathOf()));
        Check("no file means defaults", Math.Abs(GameSettings.Current.MasterVolume - 1f) < 0.001f);

        GameSettings.Current.MasterVolume = 0.5f;
        GameSettings.SaveIfDirty();
        Check("★ it does not save without a mark (MarkDirty)", !File.Exists(GameSettings.PathOf()));

        GameSettings.MarkDirty();
        GameSettings.SaveIfDirty();
        Check("premise: marking creates the file", File.Exists(GameSettings.PathOf()));

        GameSettings.Current.MusicVolume = 0.25f;
        GameSettings.Current.Fullscreen = false;
        GameSettings.Current.RumbleEnabled = false;
        GameSettings.MarkDirty();
        GameSettings.SaveIfDirty();

        ResetCurrent();
        var s = GameSettings.Current;
        Check("★ round trip: master volume", Math.Abs(s.MasterVolume - 0.5f) < 0.001f, s.MasterVolume.ToString());
        Check("★ round trip: music volume", Math.Abs(s.MusicVolume - 0.25f) < 0.001f, s.MusicVolume.ToString());
        Check("★ round trip: window mode (without remembering it you would press Alt+Enter every time)", !s.Fullscreen);
        Check("★ round trip: rumble", !s.RumbleEnabled);

        File.WriteAllText(GameSettings.PathOf(), "{\"masterVolume\":5.0,\"musicVolume\":-2.0}");
        ResetCurrent();
        Check("★ out-of-range values are folded into 0-1",
              GameSettings.Current.MasterVolume == 1f && GameSettings.Current.MusicVolume == 0f,
              $"{GameSettings.Current.MasterVolume}/{GameSettings.Current.MusicVolume}");
    }

    private static void TestBrokenFile(string dir)
    {
        File.WriteAllText(GameSettings.PathOf(), "this is not JSON {{{");
        ResetCurrent();

        var prev = Console.Out; var log = new StringWriter();
        Console.SetOut(log);
        var s = GameSettings.Current;
        Console.SetOut(prev);

        Check("★ a broken file still starts with defaults", Math.Abs(s.MasterVolume - 1f) < 0.001f);
        Check("★ it is not passed over quietly (a value the person chose has been lost)",
              log.ToString().Contains("[Settings]"), log.ToString());
        File.Delete(GameSettings.PathOf());
    }

    private static void TestApply()
    {
        ResetCurrent();
        var audio = AudioManager.Instance;
        float savedMaster = audio.MasterVolume;
        bool savedRumble = Rumble.Enabled;
        try
        {
            GameSettings.Current.MasterVolume = 0.3f;
            GameSettings.Current.MusicVolume = 0.2f;
            GameSettings.Current.SfxVolume = 0.1f;
            GameSettings.Current.RumbleEnabled = false;
            GameSettings.Current.Apply();

            Check("★ Apply reaches the master volume", Math.Abs(audio.MasterVolume - 0.3f) < 0.001f,
                  audio.MasterVolume.ToString());
            Check("★ Apply reaches the music bus",
                  Math.Abs(audio.GetBusVolume(AudioBus.Music) - 0.2f) < 0.001f);
            Check("★ one sound-effect value moves SFX, ambience and UI together (people know 'sound effects' as one thing)",
                  Math.Abs(audio.GetBusVolume(AudioBus.SFX) - 0.1f) < 0.001f
                  && Math.Abs(audio.GetBusVolume(AudioBus.Ambient) - 0.1f) < 0.001f
                  && Math.Abs(audio.GetBusVolume(AudioBus.UI) - 0.1f) < 0.001f);
            Check("★ Apply reaches the rumble switch", !Rumble.Enabled);
        }
        finally
        {
            audio.MasterVolume = savedMaster;
            audio.SetBusVolume(AudioBus.Music, 1f);
            audio.SetBusVolume(AudioBus.SFX, 1f);
            audio.SetBusVolume(AudioBus.Ambient, 1f);
            audio.SetBusVolume(AudioBus.UI, 1f);
            Rumble.Enabled = savedRumble;
        }
    }

    private static void TestSeparateFromSave(string dir)
    {
        ResetCurrent();
        GameSettings.Current.MasterVolume = 0.42f;
        GameSettings.MarkDirty();
        GameSettings.SaveIfDirty();

        Check("★ the settings file has a different name from the save file",
              Path.GetFileName(GameSettings.PathOf()) == "settings.json"
              && !GameSettings.PathOf().EndsWith(".save"), GameSettings.PathOf());

        foreach (var f in Directory.GetFiles(dir))
            if (!f.EndsWith("settings.json")) File.Delete(f);
        ResetCurrent();
        Check("★ settings survive deleting the saves",
              Math.Abs(GameSettings.Current.MasterVolume - 0.42f) < 0.001f,
              GameSettings.Current.MasterVolume.ToString());
    }

    private static void ResetCurrent()
    {
        var fi = typeof(GameSettings).GetField("_current",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        fi?.SetValue(null, null);
        var fd = typeof(GameSettings).GetField("_dirty",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        fd?.SetValue(null, false);
    }

    private static void Check(string label, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"  [pass] {label}"); }
        else { _fail++; Console.WriteLine($"  [FAIL] {label}{(detail != null ? $" - {detail}" : "")}"); }
    }
}
#endif

using System;
using System.IO;
using System.Linq;

namespace PixelCore.Runtime.Audio;

public static class AudioNoDeviceHarness
{
    public static bool Enabled => Environment.GetEnvironmentVariable("PIXELCORE_AUDIO_NODEVICE") == "1";

    private static int _pass, _fail;

    public static int Run()
    {
        Console.WriteLine("=== NoDevice harness ===");
        _pass = _fail = 0;
        AudioSelfTest.PreloadSDL();
        var am = AudioManager.Instance;

        string audioRoot = Path.Combine(am.ContentPath, "Audio");
        string? ogg = Directory.Exists(audioRoot)
            ? Directory.EnumerateFiles(audioRoot, "*.ogg", SearchOption.AllDirectories).FirstOrDefault() : null;
        string? wav = Directory.Exists(audioRoot)
            ? Directory.EnumerateFiles(audioRoot, "*.wav", SearchOption.AllDirectories).FirstOrDefault() : null;
        Check("prerequisite: a real .ogg and .wav exist (this dies here if LFS was not pulled)", ogg != null && wav != null,
              $"Audio folder: {audioRoot}");
        if (ogg == null || wav == null) return Finish();
        string oggRel = Path.GetRelativePath(am.ContentPath, ogg);
        string wavRel = Path.GetRelativePath(am.ContentPath, wav);

        var prev = Console.Out;
        var log = new StringWriter();
        int made = OggStream.ConstructedCount;
        Console.SetOut(log);
        try { am.PlayBGM(oggRel, fade: 0f); }
        finally { Console.SetOut(prev); }
        Console.Write(log.ToString());
        Check("1. no device: the probe reports first and enters silence",
              am.SilentMode && log.ToString().Contains("device probe"),
              am.SilentMode ? log.ToString()
                            : "SilentMode=false - this environment has a device. Run with SDL_AUDIODRIVER=bogus");
        Check("1. no device: not a single stream was created", OggStream.ConstructedCount == made,
              $"created {made}->{OggStream.ConstructedCount}");

        bool threw = false; string why = "";
        try
        {
            am.PlayLoop(oggRel, AudioBus.Ambient, volume: 0f, fadeIn: 0f);
            am.PlayAmbient(oggRel, 0f, 0f);
            am.PlaySFX(wavRel);
            am.Preload(wavRel);
            am.PlayBGM(oggRel, fade: 0f);
            am.Update(1f / 60f);
            am.StopAllAmbient(0f); am.StopBGM(0f); am.StopAllSFX();
        }
        catch (Exception ex) { threw = true; why = ex.GetType().Name + ": " + ex.Message; }
        Check("2. no device: every playback request is a no-op with no exception", !threw, why);

        for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        Check("3. no device: the process survives running every finalizer", true);

        return Finish();
    }

    private static int Finish()
    {
        Console.WriteLine($"=== NoDevice: {_pass} passed, {_fail} failed ===");
        Console.WriteLine("=== NODEVICE END ===");
        return _fail;
    }

    private static void Check(string label, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"  [pass] {label}"); }
        else { _fail++; Console.WriteLine($"  [FAIL] {label}{(detail != null ? $" — {detail}" : "")}"); }
    }
}

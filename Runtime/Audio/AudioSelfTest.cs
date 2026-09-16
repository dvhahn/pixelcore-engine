using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using PixelCore.Runtime.Components;

namespace PixelCore.Runtime.Audio;

public static class AudioSelfTest
{
    private static int _pass, _fail;

    private const string FixtureId = "fedcba98";

    private const int SampleRate = 44100;
    private const int Freq = 440;
    private const float DurationSec = 1f;

    public static void Run()
    {
        Console.WriteLine("=== Audio self-test ===");
        _pass = _fail = 0;

        PreloadSDL();

        TestEmitterFalloff();
        TestSilentFallback();

        string dir = Path.Combine(Path.GetTempPath(), "pixelcore-audio-selftest");
        string ogg = Path.Combine(dir, "sine440.ogg");

        if (!EnsureFixture(dir, ogg))
        {
            Console.WriteLine("  [skip] no OGG encoder, so the rest is skipped (ffmpeg required)");
            Console.WriteLine($"=== Audio: {_pass} passed, {_fail} failed (partly skipped) ===");
            return;
        }

        TestHeader(ogg);
        TestWaveform(ogg);
        TestRewindAndEof(ogg);
        TestPathResolution(dir);
        TestDeviceProbeGate(dir);
        TestMissingGuard(dir);
        TestSpatialIsolation(dir);
        TestBGMFadeVolume(dir);
        TestBusFade(dir);
        TestEmitterIntegration(dir);
        TestAudioRefIds(dir);
        TestPlayback(ogg);
        TestRealAssets();

        Console.WriteLine($"=== Audio: {_pass} passed, {_fail} failed ===");
    }

    private static void TestSilentFallback()
    {
        var audio = AudioManager.Instance;
        var prev = Console.Out;
        try
        {
            Check("1. normally it is not in silent mode", !audio.SilentMode);

            const string probe = "missing_file_silent_check.ogg";
            ClearMissingCache();
            var before = new StringWriter();
            Console.SetOut(before);
            audio.PlayBGM(probe, fade: 0f);
            Console.SetOut(prev);
            Check("* 1. control: in the normal state a missing file leaves a log line",
                before.ToString().Length > 0, "(if it is silent, the checks below are vacuous)");

            var first = new StringWriter();
            Console.SetOut(first);
            audio.ReportDeviceFailure("injected by the check");
            Console.SetOut(prev);

            Check("2. it switches to silent mode", audio.SilentMode);
            int firstLines = first.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            Check($"2. the switch is reported in one line (measured {firstLines})", firstLines == 1);

            var again = new StringWriter();
            Console.SetOut(again);
            for (int i = 0; i < 5; i++) audio.ReportDeviceFailure("repeat");
            Console.SetOut(prev);
            Check("* 2. reporting again says nothing more (latched)", again.ToString().Length == 0,
                again.ToString());

            ClearMissingCache();
            var quiet = new StringWriter();
            Console.SetOut(quiet);
            audio.PlayBGM(probe, fade: 0f);
            audio.PlaySFX(probe);
            audio.PlayLoop(probe, AudioBus.Ambient);
            Console.SetOut(prev);
            Check("* 3. in silence a playback request is a quiet no-op", quiet.ToString().Length == 0,
                quiet.ToString());
            Check("* 3. the BGM state does not turn on either", !audio.IsBGMPlaying);
        }
        finally
        {
            Console.SetOut(prev);
            ClearSilentMode();
            Check("4. cleanup: silent mode was released", !audio.SilentMode);
        }
    }

    private static void TestDeviceProbeGate(string dir)
    {
        var am = AudioManager.Instance;
        var prev = Console.Out;
        string savedPath = am.ContentPath;
        try
        {
            am.ContentPath = dir;
            ClearMissingCache(); ClearSilentMode();
            am.DeviceProbeOverride = null;
            int before = OggStream.ConstructedCount;
            var ctrl = am.PlayLoop("sine440", AudioBus.Ambient, volume: 0f, fadeIn: 0f);
            Check("* control: the real probe passes, so a stream is created (the counter sees creation; needs a device)",
                  ctrl != null && OggStream.ConstructedCount == before + 1,
                  $"ctrl={(ctrl == null ? "null" : "ok")} created {before}->{OggStream.ConstructedCount}");
            am.StopLoop("sine440", AudioBus.Ambient, 0f);

            ClearMissingCache(); ClearSilentMode();
            am.DeviceProbeOverride = () => throw new NoAudioHardwareException();
            int before2 = OggStream.ConstructedCount;
            var log = new StringWriter();
            Console.SetOut(log);
            var r = am.PlayLoop("sine440", AudioBus.Ambient, volume: 0f, fadeIn: 0f);
            am.PlayBGM("sine440", fade: 0f);
            am.PlaySFX("sine440");
            am.Preload("sine440.wav");
            Console.SetOut(prev);
            Check("* no device: the probe reports first (\"device probe\")", log.ToString().Contains("device probe"), log.ToString());
            Check("* no device: enters silent mode", am.SilentMode);
            Check("* no device: not a single stream is created (zero zombie finalizers)",
                  r == null && OggStream.ConstructedCount == before2,
                  $"created {before2}->{OggStream.ConstructedCount}");
            int lines = log.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            Check($"no device: four requests produce one log line (measured {lines})", lines == 1, log.ToString());
        }
        finally
        {
            Console.SetOut(prev);
            am.DeviceProbeOverride = null;
            ClearSilentMode();
            ClearMissingCache();
            am.ContentPath = savedPath;
        }
    }

    private static void ResetDeviceProbe()
    {
        var fi = typeof(AudioManager).GetField("_deviceProbed",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        fi?.SetValue(AudioManager.Instance, false);
    }

    private static void ClearMissingCache()
    {
        var fi = typeof(AudioManager).GetField("_missing",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        (fi?.GetValue(AudioManager.Instance) as System.Collections.Generic.HashSet<string>)?.Clear();
    }

    private static void ClearSilentMode()
    {
        var pi = typeof(AudioManager).GetProperty("SilentMode",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        pi?.SetValue(AudioManager.Instance, false);

        ResetDeviceProbe();
    }

    private static void TestEmitterFalloff()
    {
        const float R = 100f;

        Check("maximum at the centre (1.0)", Near(SoundEmitter.Attenuation(0f, R), 1f));
        Check("silent at the radius (0.0)", Near(SoundEmitter.Attenuation(R, R), 0f));
        Check("silent beyond the radius too", Near(SoundEmitter.Attenuation(R * 2f, R), 0f));
        Check("half at half the distance (the smoothstep symmetry point)", Near(SoundEmitter.Attenuation(R / 2f, R), 0.5f));

        bool monotonic = true;
        float prev = SoundEmitter.Attenuation(0f, R);
        for (int i = 1; i <= 100; i++)
        {
            float cur = SoundEmitter.Attenuation(i * R / 100f, R);
            if (cur > prev + 1e-6f) { monotonic = false; break; }
            prev = cur;
        }
        Check("monotonically decreasing with distance", monotonic);

        float edgeSlope = (SoundEmitter.Attenuation(R * 0.98f, R)
                         - SoundEmitter.Attenuation(R, R)) / (R * 0.02f);
        float midSlope = MathF.Abs((SoundEmitter.Attenuation(R * 0.49f, R)
                                  - SoundEmitter.Attenuation(R * 0.51f, R)) / (R * 0.02f));
        Check($"the slope is gentle at the radius boundary (edge {edgeSlope:F4}, middle {midSlope:F4})",
              MathF.Abs(edgeSlope) < midSlope * 0.15f);

        Check("a radius of 0 is silent (division-by-zero guard)", Near(SoundEmitter.Attenuation(10f, 0f), 0f));

        Check("a source to the right gives a positive pan", SoundEmitter.PanFor(50f, R, 1f) > 0f);
        Check("a source to the left gives a negative pan", SoundEmitter.PanFor(-50f, R, 1f) < 0f);
        Check("directly above or below gives a pan of 0", Near(SoundEmitter.PanFor(0f, R, 1f), 0f));
        Check("fully to one side at the edge of the radius (+/-1)", Near(SoundEmitter.PanFor(R, R, 1f), 1f));
        Check("it never exceeds +/-1 beyond the radius either", Near(SoundEmitter.PanFor(R * 10f, R, 1f), 1f));
        Check("PanStrength 0 gives no positioning", Near(SoundEmitter.PanFor(R, R, 0f), 0f));
        Check("PanStrength 0.5 gives half", Near(SoundEmitter.PanFor(R, R, 0.5f), 0.5f));
    }

    private static bool Near(float a, float b) => MathF.Abs(a - b) < 1e-4f;

    private static void TestHeader(string ogg)
    {
        using var d = OggDecoder.Open(ogg, OggStream.ChunkSeconds);
        if (d == null) { Check("opening the decoder", false); return; }

        Check("opening the decoder", true);
        Check($"read at {SampleRate}Hz (actual {d.SampleRate})", d.SampleRate == SampleRate);
        Check($"read as stereo (actual {d.Channels}ch)", d.Channels == 2);
        Check("chunk size = frames x channels x 2 bytes",
              d.ChunkBytes == d.FramesPerChunk * d.Channels * 2);
    }

    private static void TestWaveform(string ogg)
    {
        using var d = OggDecoder.Open(ogg, OggStream.ChunkSeconds);
        if (d == null) { Check("waveform check (opening the decoder)", false); return; }

        var pcm = new byte[d.ChunkBytes];
        int totalFrames = 0, peak = 0, cycles = 0;

        const int Threshold = 8000;
        bool above = false;

        int bytes;
        while ((bytes = d.Read(pcm)) > 0)
        {
            int samples = bytes / 2;
            totalFrames += samples / d.Channels;

            for (int i = 0; i < samples; i += d.Channels)
            {
                short s = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
                if (Math.Abs((int)s) > peak) peak = Math.Abs((int)s);

                if (!above && s > Threshold) above = true;
                else if (above && s < -Threshold) { above = false; cycles++; }
            }
        }

        int expectedFrames = (int)(SampleRate * DurationSec);
        float lenErr = Math.Abs(totalFrames - expectedFrames) / (float)expectedFrames;
        Check($"length about {DurationSec}s (actual {totalFrames} frames, expected {expectedFrames}, error {lenErr:P1})",
              lenErr < 0.05f);

        Check($"not silent (peak {peak} of 32767)", peak > 16000);

        int expectedCycles = (int)(Freq * DurationSec);
        float freqErr = Math.Abs(cycles - expectedCycles) / (float)expectedCycles;
        Check($"frequency about {Freq}Hz (actual {cycles} cycles, expected {expectedCycles}, error {freqErr:P1})",
              freqErr < 0.05f);
    }

    private static void TestRewindAndEof(string ogg)
    {
        using var d = OggDecoder.Open(ogg, OggStream.ChunkSeconds);
        if (d == null) { Check("rewind check (opening the decoder)", false); return; }

        var pcm = new byte[d.ChunkBytes];

        int guard = 0;
        while (d.Read(pcm) > 0 && ++guard < 10000) { }
        Check("reading to the end does not spin forever", guard < 10000);
        Check("Read returns 0 at end of file", d.Read(pcm) == 0);

        d.Rewind();
        Check("rewinding reads again (the basis of looping)", d.Read(pcm) > 0);
    }

    private static void TestPathResolution(string dir)
    {
        var am = AudioManager.Instance;
        string saved = am.ContentPath;
        try
        {
            am.ContentPath = dir;
            Check("it finds .ogg without an extension", am.ResolveOggPath("sine440") != null);
            Check("it finds it with .ogg written out", am.ResolveOggPath("sine440.ogg") != null);
            Check("an explicit .wav is not an OGG path (it must go to resident loading)",
                  am.ResolveOggPath("sine440.wav") == null);
            Check("a missing file gives null", am.ResolveOggPath("nope") == null);
        }
        finally { am.ContentPath = saved; }
    }

    private static void TestMissingGuard(string dir)
    {
        var am = AudioManager.Instance;
        string saved = am.ContentPath;
        string bogus = Path.Combine(dir, "bogus.ogg");
        try
        {
            File.WriteAllText(bogus, "this is not an OGG");
            am.ContentPath = dir;
            am.ClearCache();

            int before = OggDecoder.OpenAttempts;
            am.PlayLoop("bogus", AudioBus.Ambient);
            int first = OggDecoder.OpenAttempts;
            am.PlayLoop("bogus", AudioBus.Ambient);
            int second = OggDecoder.OpenAttempts;

            Check($"a broken .ogg is opened once and fails ({first - before} attempts)", first == before + 1);
            Check($"a second request does not reopen natively - SyncAmbients calls every frame ({second - first} further attempts)",
                  second == first);
        }
        finally
        {
            am.ContentPath = saved;
            try { File.Delete(bogus); } catch { }
            am.ClearCache();
        }
    }

    private static void TestSpatialIsolation(string dir)
    {
        var am = AudioManager.Instance;
        string saved = am.ContentPath;
        try
        {
            am.ContentPath = dir;
            am.StopAllSFX();

            var emitter = am.PlayLoop("sine440", AudioBus.Ambient, volume: 0f, fadeIn: 0f, spatial: true);
            if (emitter == null) { Check("creating a spatial loop", false); return; }
            Check("creating a spatial loop", true);

            am.SyncAmbients(null, 0.5f);
            Check("SyncAmbients does not stop an emitter", !am.IsFadingOut(emitter));

            var bed = am.PlayLoop("sine440", AudioBus.Ambient, volume: 0f, fadeIn: 0f, spatial: false);
            am.SyncAmbients(null, 0.5f);
            Check("ordinary ambience is still stopped (the guard is not too broad)", am.IsFadingOut(bed));
            Check("the emitter is untouched by the same call", !am.IsFadingOut(emitter));

            am.StopAllSFX();
            var e2 = am.PlayLoop("sine440", AudioBus.Ambient, volume: 0f, fadeIn: 0f, spatial: true);
            int before = am.TrackedCount;
            am.PlayAmbient("sine440", 0f, 0f);
            Check($"PlayAmbient on the same path creates its own rather than hijacking the emitter ({before} to {am.TrackedCount})",
                  am.TrackedCount == before + 1);

            am.StopAllAmbient(0.5f);
            Check("StopAllAmbient does not stop an emitter either", !am.IsFadingOut(e2));

            am.StopLoop("sine440", AudioBus.Ambient, 0.5f);
            Check("a key-based StopLoop does not stop an emitter either", !am.IsFadingOut(e2));
        }
        finally { am.ContentPath = saved; am.StopAllSFX(); }
    }

    private static void TestBusFade(string dir)
    {
        var am = AudioManager.Instance;
        string saved = am.ContentPath;
        float savedMaster = am.MasterVolume;
        float savedAmb = am.GetBusVolume(AudioBus.Ambient);
        try
        {
            am.ContentPath = dir;
            am.StopAllSFX();
            am.SoloKey = null;
            am.Muted = false;
            am.MasterVolume = 0f;
            am.SetBusVolume(AudioBus.Ambient, 1f);

            am.FadeBusVolume(AudioBus.Ambient, 0f, 1f);
            am.Update(0.25f);
            Check($"the bus tween advances (0.25s gives {am.GetBusVolume(AudioBus.Ambient):F2}, about 0.75)",
                  Near2(am.GetBusVolume(AudioBus.Ambient), 0.75f));
            Check("it can be told that a tween is running", am.IsBusFading(AudioBus.Ambient));

            am.Update(0.75f);
            Check($"it lands exactly on the target (actual {am.GetBusVolume(AudioBus.Ambient):F3})",
                  am.GetBusVolume(AudioBus.Ambient) == 0f);
            Check("the tween turns off when it finishes", !am.IsBusFading(AudioBus.Ambient));

            const float Quiet = 0.004f;
            am.MasterVolume = Quiet;
            am.SetBusVolume(AudioBus.Ambient, 1f);

            var loop = am.PlayLoop("sine440", AudioBus.Ambient, volume: 0f);
            if (loop == null) { Check("creating a loop for the tween check", false); return; }
            am.SetLoopSpatial(loop, gain: 1f, pan: 0f);
            Check($"volume before the tween = master x bus (actual {loop.Volume:F4})", Near(loop.Volume, Quiet));

            am.FadeBusVolume(AudioBus.Ambient, 0.25f, 1f);
            am.Update(0.5f);
            float bus = am.GetBusVolume(AudioBus.Ambient);
            Check($"the tween reaches a playing instance (bus {bus:F3} gives volume {loop.Volume:F4})",
                  Near(loop.Volume, Quiet * bus) && bus < 0.99f);
            am.MasterVolume = 0f;

            am.SetBusVolume(AudioBus.Ambient, 0.9f);
            Check("SetBusVolume cancels a running tween", !am.IsBusFading(AudioBus.Ambient));
            am.Update(0.5f);
            Check($"after cancelling, the tween does not overwrite (actual {am.GetBusVolume(AudioBus.Ambient):F2})",
                  Near2(am.GetBusVolume(AudioBus.Ambient), 0.9f));

            am.SetBusVolume(AudioBus.Ambient, 1f);
            am.FadeBusVolume(AudioBus.Ambient, 0f, 1f);
            am.Update(0.5f);
            am.FadeBusVolume(AudioBus.Ambient, 1f, 1f);
            am.Update(0.01f);
            Check($"switching target continues from the current value (actual {am.GetBusVolume(AudioBus.Ambient):F2}, about 0.5)",
                  Near2(am.GetBusVolume(AudioBus.Ambient), 0.505f));

            am.SetBusVolume(AudioBus.Ambient, 1f);
            int frames = 0;
            do
            {
                am.FadeBusVolume(AudioBus.Ambient, 0f, 1f);
                am.Update(1f / 60f);
            }
            while (am.IsBusFading(AudioBus.Ambient) && ++frames < 300);

            Check($"calling every frame with the same target still finishes ({frames} frames, about {frames / 60f:F2}s, "
                + $"actual {am.GetBusVolume(AudioBus.Ambient):F3})",
                  frames < 70 && am.GetBusVolume(AudioBus.Ambient) == 0f);

            am.SetBusVolume(AudioBus.Ambient, 1f);
            am.FadeBusVolume(AudioBus.Ambient, 0f, 1f);
            am.Update(0.25f);
            am.FadeBusVolume(AudioBus.Ambient, 0.5f, 1f);
            am.Update(1f);
            Check($"a different target switches over (actual {am.GetBusVolume(AudioBus.Ambient):F2} = 0.5)",
                  am.GetBusVolume(AudioBus.Ambient) == 0.5f);

            am.FadeBusVolume(AudioBus.Ambient, 0.5f, 1f);
            Check("already at that value starts no tween", !am.IsBusFading(AudioBus.Ambient));

            am.FadeBusVolume(AudioBus.Ambient, 0.3f, 0f);
            Check("a duration of 0 applies immediately", am.GetBusVolume(AudioBus.Ambient) == 0.3f
                                          && !am.IsBusFading(AudioBus.Ambient));

            am.SetBusVolume(AudioBus.Ambient, 1f);
            am.FadeBusVolume(AudioBus.Ambient, 0f, 1f);
            am.Update(0.4f);
            am.CancelBusFade(AudioBus.Ambient);
            am.Update(0.5f);
            Check($"CancelBusFade stops at the current value (actual {am.GetBusVolume(AudioBus.Ambient):F2}, about 0.6)",
                  Near2(am.GetBusVolume(AudioBus.Ambient), 0.6f));
        }
        finally
        {
            am.CancelBusFade(AudioBus.Ambient);
            am.StopAllSFX();
            am.MasterVolume = savedMaster;
            am.SetBusVolume(AudioBus.Ambient, savedAmb);
            am.ContentPath = saved;
        }
    }

    private static void TestEmitterIntegration(string dir)
    {
        var am = AudioManager.Instance;
        string saved = am.ContentPath;
        float savedMaster = am.MasterVolume;
        try
        {
            am.ContentPath = dir;
            am.StopAllSFX();
            am.SoloKey = null;
            am.Muted = false;
            am.MasterVolume = 0f;
            am.ListenerActive = true;

            Assets.AssetRegistry.Instance.Register(FixtureId, "sine440");
            Check("premise: the fixture id resolves to 'sine440' in the registry",
                  Assets.AssetRegistry.Instance.GetPath(FixtureId) == "sine440");

            var scene = new Core.Scene("EmitterTest");
            var e = scene.CreateEntity("Vent");
            e.GetComponent<Components.Transform>()!.Position = new Vector2(100f, 0f);

            var se = e.AddComponent<Components.SoundEmitter>();
            se.SoundId = FixtureId;
            se.Radius = 100f;
            se.Volume = 1f;
            se.FadeIn = 0f;
            se.FadeOut = 0f;

            am.ListenerPosition = new Vector2(300f, 0f);
            scene.Update(1f / 60f);
            Check("beyond the radius (200px against 100px) it does not play at all", !se.IsPlaying);

            am.ListenerPosition = new Vector2(150f, 0f);
            scene.Update(1f / 60f);
            Check("coming inside the radius starts playback", se.IsPlaying);

            float half = GainOf(am, "sine440");
            Check($"gain about 0.5 at half the distance (actual {half:F2})", Near2(half, 0.5f));

            am.ListenerPosition = new Vector2(115f, 0f);
            scene.Update(1f / 60f);
            float near = GainOf(am, "sine440");
            Check($"approaching makes it louder ({half:F2} to {near:F2})", near > half + 0.1f);

            Check("a source to the right of the listener gives a negative pan", PanOf(am, "sine440") < 0f);

            am.ListenerPosition = new Vector2(400f, 0f);
            scene.Update(1f / 60f);
            am.Update(1f / 60f);
            Check("leaving the radius stops it", !se.IsPlaying);

            am.ListenerPosition = new Vector2(100f, 0f);
            scene.Update(1f / 60f);
            Check("listener active plus centred gives playback", se.IsPlaying);
            am.ListenerActive = false;
            scene.Update(1f / 60f);
            am.Update(1f / 60f);
            Check("ListenerActive=false stops it (silence while editing)", !se.IsPlaying);

            am.ListenerActive = true;
            am.ListenerPosition = new Vector2(100f, 0f);
            scene.Update(1f / 60f);
            int before = am.TrackedCount;
            Check($"the emitter is alive before the scene swap (tracked {before})", before > 0 && se.IsPlaying);

            scene.Clear();
            am.Update(1f / 60f);
            Check($"clearing the scene cleans up the emitter too - nothing accumulates over room round-trips ({before} to {am.TrackedCount})",
                  am.TrackedCount == 0);

            var scene2 = new Core.Scene("TabSwapTest");
            var e2 = scene2.CreateEntity("Signpost");
            e2.GetComponent<Components.Transform>()!.Position = new Vector2(100f, 0f);
            var se2 = e2.AddComponent<Components.SoundEmitter>();
            se2.SoundId = FixtureId; se2.Radius = 100f; se2.Volume = 1f; se2.FadeIn = 0f; se2.FadeOut = 0f;

            am.ListenerPosition = new Vector2(100f, 0f);
            scene2.Update(1f / 60f);
            Check("the emitter is sounding before the tab switch", se2.IsPlaying);

            Components.SoundEmitter.StopAllIn(scene2);
            am.Update(1f / 60f);
            Check($"leaving the tab goes quiet while the scene stays alive (tracked {am.TrackedCount})",
                  !se2.IsPlaying && am.TrackedCount == 0);
            Check("the scene and entities are untouched (stopped, not deleted)",
                  scene2.Entities.Count == 1 && e2.GetComponent<Components.SoundEmitter>() != null);

            scene2.Update(1f / 60f);
            Check("returning to the tab makes it sound again", se2.IsPlaying);
            scene2.Clear();
            am.Update(1f / 60f);
        }
        finally
        {
            Assets.AssetRegistry.Instance.Remove(FixtureId);
            am.ListenerActive = true;
            am.MasterVolume = savedMaster;
            am.ContentPath = saved;
            am.StopAllSFX();
        }
    }

    private static void TestAudioRefIds(string dir)
    {
        var am = AudioManager.Instance;
        var reg = Assets.AssetRegistry.Instance;
        string saved = am.ContentPath;
        float savedMaster = am.MasterVolume;
        try
        {
            am.ContentPath = dir;
            am.StopAllSFX();
            am.SoloKey = null;
            am.Muted = false;
            am.MasterVolume = 0f;
            am.ListenerActive = true;

            reg.Register(FixtureId, "sine440");

            var wanted = new System.Collections.Generic.List<AmbientTrack>
            {
                new AmbientTrack(FixtureId, 1f),
            };
            am.SyncAmbients(wanted, 0f);
            am.Update(1f / 60f);

            am.SnapshotPlaying(_snap);
            string keys = string.Join(", ", _snap.ConvertAll(i => i.Key));
            Check("* an ambience id passes through the registry to become the playback key (a path) - the id does not leak as the key",
                  _snap.Exists(i => i.Key == "sine440") && !_snap.Exists(i => i.Key == FixtureId),
                  keys);

            int before = am.TrackedCount;
            Check($"premise: the ambience really is tracked ({before})", before > 0);

            am.SyncAmbients(wanted, 0f);
            am.Update(1f / 60f);
            am.SnapshotPlaying(_snap);
            Check($"* syncing to the same list again does not cut it (tracked {before} to {am.TrackedCount})",
                  before > 0 && am.TrackedCount == before && !_snap.Exists(i => i.FadingOut));

            am.SyncAmbients(null, 0f);
            am.SnapshotPlaying(_snap);
            Check("* control: dropping out of the list fades it out (idempotent is not the same as never stopping)",
                  _snap.Count > 0 ? _snap.TrueForAll(i => i.FadingOut) : am.TrackedCount == 0);

            am.StopAllSFX();

            var ghost = new System.Collections.Generic.List<AmbientTrack>
            {
                new AmbientTrack("00000000", 1f),
            };
            string ambBark = CaptureStderr(() =>
            {
                for (int i = 0; i < 5; i++) { am.SyncAmbients(ghost, 0f); am.Update(1f / 60f); }
            });
            Check("* an ambience id missing from the registry is reported (no silent silence)",
                  ambBark.Contains("00000000"), ambBark.Trim());
            Check("* five frames report exactly once (latched - sixty lines a second would bury the first)",
                  CountBarks(ambBark) == 1, $"{CountBarks(ambBark)} lines");
            Check("missing from the registry means it does not play either", am.TrackedCount == 0);

            ghost[0].SoundId = "11111111";
            string ambBark2 = CaptureStderr(() =>
            {
                for (int i = 0; i < 3; i++) { am.SyncAmbients(ghost, 0f); am.Update(1f / 60f); }
            });
            Check("* changing the ambience id to another (also broken) id reports again - the latch is per id",
                  CountBarks(ambBark2) == 1 && ambBark2.Contains("11111111"), ambBark2.Trim());

            am.SyncAmbients(null, 0f);
            am.StopAllSFX();

            var scene = new Core.Scene("GhostEmitter");
            var e = scene.CreateEntity("Ghost");
            e.GetComponent<Components.Transform>()!.Position = new Vector2(0f, 0f);
            var se = e.AddComponent<Components.SoundEmitter>();
            se.SoundId = "00000000";
            se.Radius = 100f; se.Volume = 1f; se.FadeIn = 0f; se.FadeOut = 0f;
            am.ListenerPosition = new Vector2(0f, 0f);

            string emBark = CaptureStderr(() =>
            {
                for (int i = 0; i < 5; i++) scene.Update(1f / 60f);
            });
            Check("* an emitter id missing from the registry is reported", emBark.Contains("00000000"), emBark.Trim());
            Check("* an emitter also reports once over five frames (latched)",
                  CountBarks(emBark) == 1, $"{CountBarks(emBark)} lines");
            Check("* it names the entity (in a room with several emitters this is the only thing that locates it)",
                  emBark.Contains("Ghost"));
            Check("a failed resolution does not play at all", !se.IsPlaying && am.TrackedCount == 0);

            se.SoundId = "22222222";
            string emBark2 = CaptureStderr(() =>
            {
                for (int i = 0; i < 3; i++) scene.Update(1f / 60f);
            });
            Check("* changing the emitter id to another (also broken) id reports again - the latch is per id",
                  CountBarks(emBark2) == 1 && emBark2.Contains("22222222"), emBark2.Trim());

            string emBark3 = CaptureStderr(() =>
            {
                for (int i = 0; i < 3; i++) scene.Update(1f / 60f);
            });
            Check("* control: the same id stays silent (the latch was not lost)",
                  CountBarks(emBark3) == 0, emBark3.Trim());

            scene.Clear();
        }
        finally
        {
            reg.Remove(FixtureId);
            am.SyncAmbients(null, 0f);
            am.ListenerActive = true;
            am.MasterVolume = savedMaster;
            am.ContentPath = saved;
            am.StopAllSFX();
        }
    }

    private static int CountBarks(string text)
    {
        int n = 0;
        foreach (var line in text.Split('\n'))
            if (line.Contains("✘")) n++;
        return n;
    }

    private static string CaptureStderr(Action body)
    {
        var prev = Console.Error;
        var sw = new StringWriter();
        Console.SetError(sw);
        try { body(); }
        finally { Console.SetError(prev); }
        return sw.ToString();
    }

    private static readonly System.Collections.Generic.List<AudioManager.PlayingInfo> _snap = new();

    private static float GainOf(AudioManager am, string key)
    {
        am.SnapshotPlaying(_snap);
        foreach (var p in _snap) if (p.Key == key) return p.Gain;
        return -1f;
    }

    private static float PanOf(AudioManager am, string key)
    {
        am.SnapshotPlaying(_snap);
        foreach (var p in _snap) if (p.Key == key) return p.Pan;
        return 0f;
    }

    private static void TestBGMFadeVolume(string dir)
    {
        var am = AudioManager.Instance;
        string saved = am.ContentPath;
        float savedMaster = am.MasterVolume;
        float savedMusic = am.GetBusVolume(AudioBus.Music);
        try
        {
            am.ContentPath = dir;
            am.StopBGM(0f);
            am.MasterVolume = 0f;
            am.SetBusVolume(AudioBus.Music, 1f);

            am.PlayBGM("sine440", fade: 2f);
            am.Update(0.5f);
            Check($"the fade in advances (factor {am.BGMGain:F2}, about 0.25)", Near2(am.BGMGain, 0.25f));

            am.MasterVolume = 1f;
            am.SetBusVolume(AudioBus.Music, 0.4f);
            Check($"a bus change during a fade applies immediately (actual {am.BGMOutputVolume:F3}, expected {0.25f * 0.4f:F3})",
                  Near2(am.BGMOutputVolume, 0.25f * 0.4f));

            am.Update(1.5f);
            Check($"the new bus value holds after the fade ends (actual {am.BGMOutputVolume:F3}, expected 0.400)",
                  Near2(am.BGMOutputVolume, 0.4f));

            am.SetBusVolume(AudioBus.Music, 0.7f);
            Check($"a bus change after the fade applies too (actual {am.BGMOutputVolume:F3}, expected 0.700)",
                  Near2(am.BGMOutputVolume, 0.7f));

            am.StopBGM(1f);
            am.Update(0.5f);
            Check($"a stop fade continues from the current factor (factor {am.BGMGain:F2}, about 0.50)",
                  Near2(am.BGMGain, 0.5f));
        }
        finally
        {
            am.StopBGM(0f);
            am.MasterVolume = savedMaster;
            am.SetBusVolume(AudioBus.Music, savedMusic);
            am.ContentPath = saved;
        }
    }

    private static bool Near2(float a, float b) => MathF.Abs(a - b) < 0.01f;

    private static void TestPlayback(string ogg)
    {
        OggStream? s;
        try { s = OggStream.TryOpen(ogg, loop: false); }
        catch { s = null; }

        if (s == null)
        {
            Console.WriteLine("  [skip] the audio device could not be opened, so the playback check is skipped");
            return;
        }

        try
        {
            s.Instance.Volume = 0f;
            s.Instance.Play();
            Check("starting playback gives Playing", s.Instance.State == SoundState.Playing);

            var sw = Stopwatch.StartNew();
            bool stopped = false;
            while (sw.Elapsed.TotalSeconds < DurationSec * 4)
            {
                FrameworkDispatcher.Update();
                s.Update();
                if (s.Instance.State == SoundState.Stopped) { stopped = true; break; }
                Thread.Sleep(5);
            }
            double elapsed = sw.Elapsed.TotalSeconds;

            Check($"the track stops itself when it ends (measured {elapsed:F2}s)", stopped);
            Check($"it really took as long as the track - an instant end would mean starvation, not playback ({elapsed:F2}s of {DurationSec}s)",
                  stopped && elapsed > DurationSec * 0.8);
        }
        finally { s.Dispose(); }
    }

    private static void TestRealAssets()
    {
        const string root = "Content/Audio";
        Check($"{root} exists (the premise of the real-audio spec check)", Directory.Exists(root));
        if (!Directory.Exists(root)) return;

        var oggs = Directory.GetFiles(root, "*.ogg", SearchOption.AllDirectories);
        var wavs = Directory.GetFiles(root, "*.wav", SearchOption.AllDirectories);
        Check($"real audio files are present (ogg {oggs.Length}, wav {wavs.Length})",
              oggs.Length > 0 || wavs.Length > 0);
        if (oggs.Length == 0 && wavs.Length == 0) return;

        Array.Sort(oggs, StringComparer.Ordinal);
        Array.Sort(wavs, StringComparer.Ordinal);
        Console.WriteLine($"  - real assets: {oggs.Length} ogg, {wavs.Length} wav");

        foreach (var path in oggs)
        {
            string name = Path.GetRelativePath(root, path);
            using var d = OggDecoder.Open(path, OggStream.ChunkSeconds);
            if (d == null) { Check($"{name} - the decoder cannot open it", false); continue; }

            var pcm = new byte[d.ChunkBytes];
            int bytes = d.Read(pcm);

            bool anyNonZero = false;
            for (int i = 0; i < bytes && !anyNonZero; i++) if (pcm[i] != 0) anyNonZero = true;

            Check($"{name} ({d.SampleRate}Hz {d.Channels}ch) opened and the first chunk decoded",
                  bytes > 0 && anyNonZero);
        }

        foreach (var path in wavs)
        {
            string name = Path.GetRelativePath(root, path);
            var (bits, rate, ch) = ReadWavFormat(path);

            Check($"{name} ({bits}bit {rate}Hz {ch}ch) - SFX must be 16-bit PCM", bits == 16);
        }
    }

    private static (int Bits, int Rate, int Channels) ReadWavFormat(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);
            if (new string(r.ReadChars(4)) != "RIFF") return (0, 0, 0);
            r.ReadInt32();
            if (new string(r.ReadChars(4)) != "WAVE") return (0, 0, 0);

            while (fs.Position < fs.Length - 8)
            {
                string id = new string(r.ReadChars(4));
                int size = r.ReadInt32();
                if (id == "fmt ")
                {
                    r.ReadInt16();
                    int channels = r.ReadInt16();
                    int rate = r.ReadInt32();
                    r.ReadInt32(); r.ReadInt16();
                    int bits = r.ReadInt16();
                    return (bits, rate, channels);
                }
                fs.Position += size + (size & 1);
            }
        }
        catch { }
        return (0, 0, 0);
    }

    private static bool EnsureFixture(string dir, string ogg)
    {
        try
        {
            Directory.CreateDirectory(dir);
            if (File.Exists(ogg) && new FileInfo(ogg).Length > 0) return true;

            string filter = $"sine=frequency={Freq}:duration={DurationSec}:sample_rate={SampleRate}";

            const string gain = "volume=10";

            string[][] recipes =
            {
                new[] { "-y", "-loglevel", "error", "-f", "lavfi", "-i", filter,
                        "-af", gain, "-ac", "2", "-c:a", "libvorbis", "-q:a", "5", ogg },
                new[] { "-y", "-loglevel", "error", "-f", "lavfi", "-i", filter,
                        "-af", gain, "-ac", "2", "-c:a", "vorbis", "-strict", "-2", ogg },
            };

            foreach (var args in recipes)
            {
                if (RunTool("ffmpeg", args) && File.Exists(ogg) && new FileInfo(ogg).Length > 0)
                    return true;
            }
            return false;
        }
        catch { return false; }
    }

    internal static void PreloadSDL()
    {
        FNALoggerEXT.LogInfo  ??= s => Console.WriteLine("[FNA] " + s);
        FNALoggerEXT.LogWarn  ??= s => Console.WriteLine("[FNA] ⚠ " + s);
        FNALoggerEXT.LogError ??= s => Console.Error.WriteLine("[FNA] ✘ " + s);

        string[] candidates = RuntimeFeature.IsDynamicCodeCompiled
            ? new[] { "libSDL3.0.dylib", "libSDL3.so.0" }
            : new[] { "libSDL3.dylib", "SDL3.dll", "libSDL3.so" };

        foreach (var name in candidates)
        {
            var path = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out _)) return;
        }

        NativeLibrary.TryLoad("SDL3", out _);
    }

    private static bool RunTool(string exe, string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi);
            if (p == null) return false;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            return p.WaitForExit(30_000) && p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static void Check(string label, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"  [pass] {label}"); }
        else { _fail++; Console.WriteLine($"  [FAIL] {label}{(detail != null ? $" — {detail}" : "")}"); }
    }
}

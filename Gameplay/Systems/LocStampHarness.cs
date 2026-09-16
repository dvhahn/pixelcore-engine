#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using PixelCore.Runtime.Story;
using PixelCore.Runtime.Text;

namespace PixelCore.Gameplay.Systems;

public static class LocStampHarness
{
    public static int Run(string mode)
    {
        Console.WriteLine($"=== translation number stamping ({mode}) ===");

        Cutscenes.VillageCutscenes.Register();

        var lib = StoryLibrary.Load(StoryLibrary.DefaultRoot);
        StoryLibrary.UseLibrary(lib);
        if (lib.Errors.Count > 0)
        {
            foreach (var e in lib.Errors) Console.Error.WriteLine($"[Stamp] ✘ .story {e}");
            Console.Error.WriteLine("[Stamp] ✘ with errors in the scripts the key space cannot be trusted - fix them first");
            return lib.Errors.Count;
        }

        bool require = Environment.GetEnvironmentVariable("PIXELCORE_STAMP_REQUIRE") == "1";

        switch (mode)
        {
            case "write":
            {
                var report = LocStamp.Stamp();
                Console.WriteLine($"[Stamp] {report}");
                foreach (var d in report.Details) Console.WriteLine($"[Stamp]   {d}");
                int after = LocStamp.Verify(lib, requireStamped: require);
                Console.WriteLine(after == 0 ? "[Stamp] ✓ gates passed" : $"[Stamp] ✘ {after} gate failures");
                return after;
            }

            case "export":
            {
                int problems = LocStamp.Verify(lib, requireStamped: require);
                if (problems > 0)
                {
                    Console.Error.WriteLine($"[Stamp] ✘ {problems} gate failures - not exporting " +
                        "(writing a CSV over a broken key space attaches a translator's work to the wrong lines)");
                    return problems;
                }
                string lang = Environment.GetEnvironmentVariable("PIXELCORE_STAMP_LANG") ?? "en";
                var extra = new List<LocEntry>();
                extra.AddRange(LocStamp.CollectStory(lib));
                extra.AddRange(LocStamp.ToEntries(LocStamp.Scan()));
                Loc.Load(Loc.DefaultRoot);
                Loc.ExportCsv(lang, extra);
                return 0;
            }

            default:
            {
                int problems = LocStamp.Verify(lib, requireStamped: require);
                var lines = LocStamp.Scan();
                int stamped = 0;
                foreach (var l in lines) if (l.Number > 0) stamped++;
                Console.WriteLine($"[Stamp] {lines.Count} inline lines, {stamped} stamped, {lines.Count - stamped} unstamped");

                int storyStamped = LocStamp.CollectStory(lib).Count;
                int storyTotal = 0;
                foreach (var sc in lib.Scripts)
                    foreach (var b in sc.Blocks)
                        foreach (var v in b.Variants)
                            foreach (var n in v.Nodes)
                                if (n is DialogueLineNode) storyTotal++;
                Console.WriteLine($"[Stamp] {storyTotal} .story lines, {storyStamped} stamped, " +
                    $"{storyTotal - storyStamped} unstamped (unstamped lines do not reach the CSV - .story stamping does not exist yet)");
                Console.WriteLine(problems == 0 ? "[Stamp] ✓ all three gates passed" : $"[Stamp] ✘ {problems} gate failures");
                return problems;
            }
        }
    }
}
#endif

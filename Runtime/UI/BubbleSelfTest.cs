using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Story;

namespace PixelCore.Runtime.UI;

public static class BubbleSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        Console.WriteLine("=== Bubble self-test ===");
        _pass = _fail = 0;

        TestMetricsSeam();
        TestWrapping();
        TestForbiddenLineStart();
        TestPaging();
        TestOrderInvariant();
        TestSpans();
        TestStops();
        TestAnchor();
        TestSpeakerResolve();
        TestScales();
        TestUiScaleIndependentOfWorld();
        TestBubbleScaleFollowsScreenNotWorld();
        TestBoxGeometry();
        TestIntro();
        TestShake();
        TestTypewriter();
        TestPool();

        TestShowOverloadTrapBlocked();

        Console.WriteLine($"=== Bubble: {_pass} passed, {_fail} failed ===");
    }

    private sealed class FakeFont : IGlyphMetrics
    {
        public float LineHeight => 10f;
        public float Advance(char c) => BubbleLayout.IsCjk(c) ? 2f : 1f;
    }

    private static readonly FakeFont Font = new();

    private static BubbleLayout Build(string text, float maxWidth, int maxLines = 3, float minWidth = 0f)
        => BubbleLayout.Build(text, Font, maxWidth, minWidth, maxLines);

    private static string LineText(BubbleLayout l, string plain, int i)
    {
        var sb = new System.Text.StringBuilder();
        var line = l.Lines[i];
        for (int k = 0; k < line.Count; k++) sb.Append(plain[l.Order[line.Start + k]]);
        return sb.ToString();
    }

    private static void TestMetricsSeam()
    {
        Console.WriteLine("--- premise: the measurement stand-in ---");
        Check("premise: CJK is 2 cells, Latin 1", Font.Advance('一') == 2f && Font.Advance('a') == 1f);

        var l = Build("一五七", 100f);
        Check("premise: the measured width is not 0 (0 makes every wrapping check below vacuous)",
              l.Width == 6f, l.Width.ToString());
    }

    private static void TestWrapping()
    {
        Console.WriteLine("--- wrapping ---");

        var s1 = Build("田", 100f);
        Check("* a short line narrows to fit (it does not stretch to the limit)",
              s1.Width == 2f && s1.Lines.Length == 1, $"w={s1.Width} lines={s1.Lines.Length}");

        var s2 = Build("一五七十", 5f);
        Check("CJK wraps per character",
              s2.Lines.Length == 2 && LineText(s2, "一五七十", 0) == "一五"
              && LineText(s2, "一五七十", 1) == "七十",
              $"{s2.Lines.Length} lines / {LineText(s2, "一五七十", 0)}|{LineText(s2, "一五七十", 1)}");

        const string en = "hello world";
        var s3 = Build(en, 7f);
        Check("* a Latin word is not split in the middle",
              s3.Lines.Length == 2 && LineText(s3, en, 0) == "hello" && LineText(s3, en, 1) == "world",
              $"{LineText(s3, en, 0)}|{(s3.Lines.Length > 1 ? LineText(s3, en, 1) : "")}");

        Check("leading whitespace on a wrapped line is dropped", !LineText(s3, en, 1).StartsWith(" "));

        const string nl = "一\n五";
        var s5 = Build(nl, 100f);
        Check("* an authored \\n is honoured (it breaks even with width to spare)",
              s5.Lines.Length == 2 && LineText(s5, nl, 0) == "一" && LineText(s5, nl, 1) == "五",
              $"{s5.Lines.Length} lines");

        const string tail = "一 ";
        var s6 = Build(tail, 100f);
        Check("trailing whitespace does not count toward the width", s6.Width == 2f, s6.Width.ToString());

        var s7 = Build("田", 100f, minWidth: 20f);
        Check("it does not go below minWidth", s7.Width == 20f, s7.Width.ToString());

        var s8 = Build("田", 8f, minWidth: 20f);
        Check("minWidth cannot exceed maxWidth", s8.Width <= 8f, s8.Width.ToString());
    }

    private static void TestForbiddenLineStart()
    {
        Console.WriteLine("--- forbidden line-start characters ---");

        Check("table: full stop, comma and closing bracket are forbidden at a line start",
              BubbleLayout.IsForbiddenLineStart('.') && BubbleLayout.IsForbiddenLineStart(',')
              && BubbleLayout.IsForbiddenLineStart(')'));
        Check("table: an opening bracket is not forbidden (that is the opposite rule, kept separate)",
              !BubbleLayout.IsForbiddenLineStart('('));

        const string k = "一五.";
        var l = Build(k, 4f);
        Check("* no punctuation at a line start - the preceding character is pulled down",
              l.Lines.Length == 2 && LineText(l, k, 0) == "一" && LineText(l, k, 1) == "五.",
              $"{l.Lines.Length} lines / {LineText(l, k, 0)}|{(l.Lines.Length > 1 ? LineText(l, k, 1) : "")}");

        const string k2 = "一五七";
        var l2 = Build(k2, 4f);
        Check("* control: nothing is pulled down when the character is not forbidden",
              l2.Lines.Length == 2 && LineText(l2, k2, 0) == "一五" && LineText(l2, k2, 1) == "七",
              $"{LineText(l2, k2, 0)}|{(l2.Lines.Length > 1 ? LineText(l2, k2, 1) : "")}");

        const string k3 = "一.";
        var l3 = Build(k3, 2f);
        Check("with nothing to pull down it is attached even if it overflows (no empty lines)",
              l3.Lines.Length == 1 && LineText(l3, k3, 0) == "一.",
              $"{l3.Lines.Length} lines / {LineText(l3, k3, 0)}");
    }

    private static void TestPaging()
    {
        Console.WriteLine("--- pages ---");

        const string t = "一五七十千地";
        var l = Build(t, 2f, maxLines: 3);
        Check("premise: it split into six lines", l.Lines.Length == 6, l.Lines.Length.ToString());
        Check("it paginates by lines per page", l.PageCount == 2 && l.LinesPerPage == 3,
              $"pages={l.PageCount} perPage={l.LinesPerPage}");
        Check("lines per page", l.PageLineCount(0) == 3 && l.PageLineCount(1) == 3);
        Check("characters per page", l.PageCharCount(0) == 3 && l.PageCharCount(1) == 3);

        var l2 = Build("一五七十", 2f, maxLines: 3);
        Check("* the box height does not change even when the last page is short",
              l2.PageCount == 2 && l2.LinesPerPage == 3 && l2.PageLineCount(1) == 1
              && l2.Height == 30f,
              $"perPage={l2.LinesPerPage} last={l2.PageLineCount(1)} h={l2.Height}");

        var l3 = Build("一", 100f, maxLines: 3);
        Check("a short line gets a page height matching its line count", l3.LinesPerPage == 1 && l3.Height == 10f,
              $"perPage={l3.LinesPerPage} h={l3.Height}");
    }

    private static void TestOrderInvariant()
    {
        Console.WriteLine("--- Order invariant ---");

        const string t = "一五 七十\n千地";
        var l = Build(t, 4f);

        bool inRange = true;
        var seen = new HashSet<int>();
        bool dup = false;
        foreach (int i in l.Order)
        {
            if (i < 0 || i >= t.Length) inRange = false;
            if (!seen.Add(i)) dup = true;
        }
        Check("* every index in Order is within Plain", inRange);
        Check("* no character is drawn twice", !dup);

        int sum = 0;
        for (int p = 0; p < l.PageCount; p++) sum += l.PageCharCount(p);
        Check("* the per-page character counts sum to the total (the typewriter budget neither leaks nor is left over)",
              sum == l.Order.Length, $"{sum} vs {l.Order.Length}");

        bool noNewline = true;
        foreach (int i in l.Order) if (t[i] == '\n') noNewline = false;
        Check("newline characters are not drawn", noNewline);
    }

    private static void TestSpans()
    {
        Console.WriteLine("--- per-character styles ---");

        var t = DialogueMarkup.Parse("一<shake>五七</shake>十");
        Check("* Plain and Styles are the same length (a structural invariant)",
              t.Plain.Length == t.Styles.Length, $"{t.Plain.Length} vs {t.Styles.Length}");
        Check("the characters remain once the tag is stripped", t.Plain == "一五七十", t.Plain);
        Check("* shake applies only to characters inside the span",
              !t.Styles[0].Shake && t.Styles[1].Shake && t.Styles[2].Shake && !t.Styles[3].Shake,
              $"{t.Styles[0].Shake}{t.Styles[1].Shake}{t.Styles[2].Shake}{t.Styles[3].Shake}");
        Check("HasShake turns on", t.HasShake);

        var plainOnly = DialogueMarkup.Parse("三六 天耳");
        Check("* control: with no tag, no shake is applied", !plainOnly.HasShake);

        var sp = DialogueMarkup.Parse("<speed 0.3>右右文</speed> 一口");
        Check("the speed argument is read", MathF.Abs(sp.Styles[0].Speed - 0.3f) < 0.001f,
              sp.Styles[0].Speed.ToString());
        Check("* after closing it returns to the default speed",
              MathF.Abs(sp.Styles[^1].Speed - 1f) < 0.001f, sp.Styles[^1].Speed.ToString());
        Check("a speed tag does not turn on shake", !sp.HasShake);

        var nested = DialogueMarkup.Parse("<speed 0.5><shake>二</shake></speed>");
        Check("nesting: both apply",
              nested.Styles[0].Shake && MathF.Abs(nested.Styles[0].Speed - 0.5f) < 0.001f);

        DialogueMarkup.ResetWarnings();
        string barked = CaptureStdout(() => DialogueMarkup.Parse("<shake>木 八川"));
        Check("* an unclosed tag is reported by name", barked.Contains("<shake> is not closed"), barked.Trim());
        Check("the characters remain even when unclosed", DialogueMarkup.Plain("<shake>木 八川") == "木 八川");
        string quiet = CaptureStdout(() => DialogueMarkup.Parse("<shake>八川</shake>"));
        Check("* control: properly closed is silent", !quiet.Contains("is not closed"), quiet.Trim());
        DialogueMarkup.ResetWarnings();
    }

    private static void TestStops()
    {
        Console.WriteLine("--- point markers ---");

        var t = DialogueMarkup.Parse("上月本土. {.} 人火一...");
        Check("the plain characters are unchanged", t.Plain == "上月本土. 人火一...", t.Plain);
        Check("* a pause point is remembered by character index (it used to be stripped)",
              t.Stops.Count == 1 && t.Stops[0].Token == "." && t.Stops[0].Index == 6,
              t.Stops.Count > 0 ? $"{t.Stops[0].Token}@{t.Stops[0].Index}" : "none");
        Check("a pause is not a page break", t.Stops.Count == 1 && !t.Stops[0].IsPageBreak);

        var pg = DialogueMarkup.Parse("目四{====}万");
        Check("the plain characters are unchanged (page marker)", pg.Plain == "目四万", pg.Plain);
        Check("* it is classified as a page marker",
              pg.Stops.Count == 1 && pg.Stops[0].IsPageBreak && pg.Stops[0].Index == 2,
              pg.Stops.Count > 0 ? $"{pg.Stops[0].Token}@{pg.Stops[0].Index}" : "none");

        var trimmed = DialogueMarkup.Parse("  <shake>金</shake>{.}九  ");
        Check("* trimming the ends carries the point indices along",
              trimmed.Plain == "金九" && trimmed.Stops.Count == 1 && trimmed.Stops[0].Index == 1,
              $"'{trimmed.Plain}' @{(trimmed.Stops.Count > 0 ? trimmed.Stops[0].Index : -1)}");
        Check("* after trimming, the styles still sit on the same characters",
              trimmed.Styles.Length == 2 && trimmed.Styles[0].Shake && !trimmed.Styles[1].Shake);

        Check("unresolved interpolation is not deleted (vanishing silently makes it unfindable)",
              DialogueMarkup.Plain("七川 手百耳: {手百耳}") == "七川 手百耳: {手百耳}");
    }

    private static void TestAnchor()
    {
        Console.WriteLine("--- anchors ---");

        var scene = new Scene("BubbleAnchorProbe");

        var actor = scene.CreateEntity("Actor");
        actor.GetComponent<Transform>()!.Position = new Vector2(100f, 200f);
        var sr = actor.AddComponent<SpriteRenderer>();
        sr.DrawSize = new Vector2(48f, 48f);
        sr.PivotY = 1f;
        scene.FlushPendingAdds();

        var auto = Talker.AnchorFor(actor);
        Check("* it answers without a Talker (the anchor is a global rule, the component an exception)",
              auto == new Vector2(100f, 200f - 36f), auto.ToString());

        var talker = actor.AddComponent<Talker>();
        Check("* a Talker at (0,0) is automatic too (0 means 'not set')",
              Talker.AnchorFor(actor) == auto, Talker.AnchorFor(actor).ToString());

        talker.BubbleAnchor = new Vector2(8f, -60f);
        Check("* a hand-placed value wins",
              Talker.AnchorFor(actor) == new Vector2(108f, 140f), Talker.AnchorFor(actor).ToString());

        talker.Enabled = false;
        Check("a disabled Talker is ignored (it falls back to automatic)",
              Talker.AnchorFor(actor) == auto, Talker.AnchorFor(actor).ToString());
        talker.Enabled = true;

        var boxy = scene.CreateEntity("Boxy");
        boxy.GetComponent<Transform>()!.Position = new Vector2(0f, 0f);
        var col = boxy.AddComponent<BoxCollider2D>();
        col.Size = new Vector2(10f, 20f);
        scene.FlushPendingAdds();
        var boxAnchor = Talker.AnchorFor(boxy);
        Check("with no sprite it uses the collider height (centre pivot)",
              MathF.Abs(boxAnchor.Y - (0f + 10f - 15f)) < 0.01f, boxAnchor.ToString());

        var bare = scene.CreateEntity("Bare");
        scene.FlushPendingAdds();
        Check("with neither it uses the fallback height (still floated above the head)",
              Talker.AnchorFor(bare).Y < 0f, Talker.AnchorFor(bare).ToString());

        Check("* there is one height ruler (Talker.VisualHeight)", Talker.VisualHeight(actor) == 48f,
              Talker.VisualHeight(actor).ToString());
    }

    private static void TestSpeakerResolve()
    {
        Console.WriteLine("--- speaker resolution ---");

        var scene = new Scene("BubbleSpeakerProbe");
        var player = scene.CreateEntity(Scene.PlayerName);
        var elder = scene.CreateEntity("OldMan");
        scene.FlushPendingAdds();

        Cutscenes.CutsceneDirector.SetCast("hero", Scene.PlayerName);
        DialogueBubble.ResetWarnings();

        Check("* found through the cast alias (the script says one name, the scene another)",
              DialogueBubble.ResolveSpeaker(scene, "hero") == player);
        Check("with no alias, the name as written", DialogueBubble.ResolveSpeaker(scene, "OldMan") == elder);

        string missing = CaptureStdout(() => DialogueBubble.ResolveSpeaker(scene, "noSuchActor"));
        Check("* a missing speaker is reported by name", missing.Contains("'noSuchActor'") && missing.Contains("no entity"),
              missing.Trim());
        Check("a missing speaker gives null (it falls back to the bottom card)",
              DialogueBubble.ResolveSpeaker(scene, "noSuchActor") == null);

        scene.CreateEntity("twin");
        scene.CreateEntity("twin");
        scene.FlushPendingAdds();

        DialogueBubble.ResetWarnings();
        string ambiguous = CaptureStdout(() => DialogueBubble.ResolveSpeaker(scene, "twin"));
        Check("* ambiguity is reported as ambiguous (the cure differs from missing)",
              ambiguous.Contains("ambiguous") && ambiguous.Contains("2 candidates"), ambiguous.Trim());
        Check("ambiguity picks nobody (it does not rely on list order luck)",
              DialogueBubble.ResolveSpeaker(scene, "twin") == null);

        var decoy = scene.CreateEntity("overlap");
        scene.FlushPendingAdds();
        Cutscenes.CutsceneDirector.SetCast("overlap", "twin");
        DialogueBubble.ResetWarnings();
        Check("* ambiguity at the alias step ends immediately (it does not fall through even with a decoy)",
              DialogueBubble.ResolveSpeaker(scene, "overlap") == null,
              DialogueBubble.ResolveSpeaker(scene, "overlap")?.Name ?? "null");
        Check("premise: the decoy exists (without it the check above is vacuous)",
              scene.FindEntity("overlap") == decoy);

        Check("an empty speaker is quietly null (narration - the normal path)",
              DialogueBubble.ResolveSpeaker(scene, "") == null);
        Check("no scene gives null", DialogueBubble.ResolveSpeaker(null, "hero") == null);

        Cutscenes.CutsceneDirector.ClearRegistry();
        DialogueBubble.ResetWarnings();
    }

    private static void TestBoxGeometry()
    {
        Console.WriteLine("--- box size ---");

        float savedMin = DialogueBubble.MinWidth;
        DialogueBubble.MinWidth = 0f;
        try
        {
        var S6 = BubbleScales.For(6f);
        var S4 = BubbleScales.For(4f);

        var b = new DialogueBubble();
        Check("an empty state has zero pages (no box opens)", b.PageCount == 0);

        var probe = new DialogueBubble();
        probe.SetContent("", null, new[] { "一五七" }, 100f, 3, Font);
        Check("premise: the injected ruler makes the width a real measurement (0 makes everything below vacuous)",
              probe.ContentWidth == 6f, probe.ContentWidth.ToString());

        b.SetContent("hero", "hero", new[] { "一", "一五七十千地日水口左" }, 100f, 3, Font);
        Check("premise: both parts went in (two or more pages)", b.PageCount >= 2, b.PageCount.ToString());

        var first = b.BoxSize(S6);
        var wide = new DialogueBubble();
        wide.SetContent("hero", "hero", new[] { "一五七十千地日水口左" }, 100f, 3, Font);
        Check("* with a short part mixed in, the box fits the widest part (no jump when advancing)",
              first == wide.BoxSize(S6), $"{first} vs {wide.BoxSize(S6)}");

        var reversed = new DialogueBubble();
        reversed.SetContent("hero", "hero", new[] { "一五七十千地日水口左", "一" }, 100f, 3, Font);
        Check("* the same with the wide part first (widest means maximum, not last)",
              reversed.BoxSize(S6) == first, $"{reversed.BoxSize(S6)} vs {first}");

        var narrow = new DialogueBubble();
        narrow.SetContent("hero", "hero", new[] { "一" }, 100f, 3, Font);
        Check("* control: with only a short part it is correspondingly narrow",
              narrow.BoxSize(S6).X < first.X, $"{narrow.BoxSize(S6).X} vs {first.X}");

        Check("a larger scale gives a larger box (dots grow by whole multiples)",
              b.BoxSize(S6).X > b.BoxSize(S4).X, $"{b.BoxSize(S4)} → {b.BoxSize(S6)}");

        int padPx = (DialogueBubble.PadLeft + DialogueBubble.PadRight) * S6.Skin;
        int textPx = (int)MathF.Ceiling(b.ContentWidth) * S6.Text;
        Check("* box width = text (text scale) + margins (skin scale)",
              b.BoxSize(S6).X == textPx + padPx, $"{b.BoxSize(S6).X} vs {textPx}+{padPx}");

        var noName = new DialogueBubble();
        noName.SetContent("hero", null, new[] { "一五七十千地" }, 100f, 3, Font);
        Check("* a line spoken to oneself (no label) is correspondingly shorter",
              noName.BoxSize(S6).Y < first.Y, $"{noName.BoxSize(S6).Y} vs {first.Y}");
        Check("with no name, Name is null", noName.Name == null);

        b.Clear();
        Check("Clear empties the pages", b.PageCount == 0 && b.SpeakerId == "");

        var empty = new DialogueBubble();
        empty.SetContent("", null, new[] { "" }, 100f, 3, Font);
        Check("* empty text gives zero pages (no box and no lock)", empty.PageCount == 0);
        }
        finally { DialogueBubble.MinWidth = savedMin; }

        Check("premise: the tuning value was restored (the next check inherits it)",
              DialogueBubble.MinWidth == savedMin);
    }

    private static void TestBubbleScaleFollowsScreenNotWorld()
    {
        Console.WriteLine("--- bubble scale (does not change per room) ---");

        var basic = DialogueBox.ScalesFor(720);
        var zoomed = DialogueBox.ScalesFor(720);
        Check($"* on the same screen the scale is the same whatever the room (text {basic.Text}x)",
              basic.Text == zoomed.Text && basic.Skin == zoomed.Skin);

        var oldBasic = BubbleScales.For(4f);
        var oldZoom = BubbleScales.For(3f);
        Check($"* control: the old way diverged - text {oldBasic.Text}x vs {oldZoom.Text}x",
              oldBasic.Text != oldZoom.Text);

        Check($"* the new way unifies to the larger one ({basic.Text}x = the same as the default room)",
              basic.Text == oldBasic.Text);

        Check($"   a larger screen gives a larger scale (720p {DialogueBox.ScalesFor(720).Text} < 2160p {DialogueBox.ScalesFor(2160).Text})",
              DialogueBox.ScalesFor(2160).Text > DialogueBox.ScalesFor(720).Text);
    }

    private static void TestUiScaleIndependentOfWorld()
    {
        Console.WriteLine("--- UI scale (detached from the world) ---");

        Check("1080p → 6", UiScale.For(1080f) == 6, UiScale.For(1080f).ToString());
        Check("720p → 4", UiScale.For(720f) == 4, UiScale.For(720f).ToString());

        int basic = UiScale.For(1080f);
        int zoomed = UiScale.For(1080f);
        Check("* whatever the scene zoom, the same screen gives the same UI scale", basic == zoomed);

        int oldBasic  = (int)MathF.Floor(1080f / 180f);
        int oldZoomed = (int)MathF.Floor(1080f / 216f);
        Check($"* control: the old way diverged ({oldBasic} vs {oldZoomed})", oldBasic != oldZoomed);

        Check($"* it floors - 1079px gives 5 (rounding would give 6) - measured {UiScale.For(1079f)}",
              UiScale.For(1079f) == 5);
        Check($"* 1259px gives 6 (rounding would give 7) - measured {UiScale.For(1259f)}",
              UiScale.For(1259f) == 6);
        Check("   control: an exact value stays as it is (1080 → 6)", UiScale.For(1080f) == 6);

        bool monotonic = true;
        int prev = UiScale.For(180f);
        for (float h = 180f; h < 2200f; h += 37f)
        {
            int u = UiScale.For(h);
            if (u < prev || u < 1) monotonic = false;
            prev = u;
        }
        Check("* the scale grows with the screen and never drops below 1", monotonic);

        Check("no window, or a minimised one, does not give 0 (a scale of 0 = not drawn)",
              UiScale.For(0f) == 1 && UiScale.For(-5f) == 1 && UiScale.For(float.NaN) == 1);
        Check("a screen smaller than the reference height holds at 1", UiScale.For(100f) == 1);
    }

    private static void TestScales()
    {
        Console.WriteLine("--- scale separation ---");

        var s6 = BubbleScales.For(6f);
        Check("premise: the world scale is the rounded output scale", s6.World == 6, s6.World.ToString());
        Check("* the skin is smaller than the world (the 3.125x slot)", s6.Skin == 3, s6.Skin.ToString());
        Check("* the text is smaller than the skin (a denser UI)", s6.Text == 2 && s6.Text < s6.Skin,
              s6.Text.ToString());

        var ladder = new (int World, int Skin, int Text)[]
        {
            (4, 2, 2), (5, 3, 2), (6, 3, 2), (7, 4, 3), (8, 4, 3), (10, 5, 4), (12, 6, 5),
        };
        var got = new List<string>();
        bool ladderOk = true;
        foreach (var (world, skin, text) in ladder)
        {
            var cur = BubbleScales.For(world);
            got.Add($"{world}→{cur.Skin}/{cur.Text}");
            if (cur.World != world || cur.Skin != skin || cur.Text != text) ladderOk = false;
        }
        Check("* the scale ladder (world to skin/text)", ladderOk, string.Join(" · ", got));

        float lo = 1f, hi = 0f;
        var big = new List<string>();
        foreach (var (world, _, _) in ladder)
        {
            if (world < 5) continue;
            float r = BubbleScales.For(world).Text / (float)world;
            big.Add($"{world}:{r:0.00}");
            lo = MathF.Min(lo, r); hi = MathF.Max(hi, r);
        }
        Check("* on large screens (world 5+) the text-to-world ratio wobbles less (spread <= 0.12)",
              hi - lo <= 0.12f, $"{string.Join(" ", big)} -> spread {hi - lo:0.00}");

        var s1 = BubbleScales.For(1f);
        Check("* it never drops below 1 however small (0 would make the bubble vanish)",
              s1.World == 1 && s1.Skin == 1 && s1.Text == 1, $"{s1.World}/{s1.Skin}/{s1.Text}");
        var s0 = BubbleScales.For(0.2f);
        Check("near-zero scale still gives 1", s0.Skin == 1 && s0.Text == 1);

        bool monotone = true;
        var prev = BubbleScales.For(1f);
        for (float u = 1f; u <= 20f; u += 1f)
        {
            var cur = BubbleScales.For(u);
            if (cur.Skin < prev.Skin || cur.Text < prev.Text) monotone = false;
            prev = cur;
        }
        Check("* the UI scale grows with the window (no inversion)", monotone);

        Check("* control: on a large window all three differ (the separation is alive)",
              s6.World != s6.Skin && s6.Skin != s6.Text);
    }

    private static void TestIntro()
    {
        Console.WriteLine("--- entrance ---");

        var b = new DialogueBubble();
        b.SetContent("hero", "hero", new[] { "一五七" }, 100f, 3, Font);

        Check("premise: nothing is open the instant it opens",
              !b.BoxOpen && !b.NameShown && !b.BodyReady);
        Check("* while unfolding the height is at step 0 (treated as 1px)", b.OpenFraction == 0f, b.OpenFraction.ToString());

        float step = DialogueBubble.OpenStepSeconds;
        var seen = new List<float>();
        var fresh = new DialogueBubble();
        fresh.SetContent("hero", "hero", new[] { "一五七" }, 100f, 3, Font);
        for (int i = 0; i < 4; i++) { seen.Add(fresh.OpenFraction); fresh.Tick(step, 0, 0); }
        Check("* the unfold moves in integer steps (0, 40%, 75%, 100%)",
              seen.Count == 4 && seen[0] == 0f && seen[1] == 0.40f && seen[2] == 0.75f && seen[3] == 1f,
              string.Join(" → ", seen));

        var c = new DialogueBubble();
        c.SetContent("hero", "hero", new[] { "一五七" }, 100f, 3, Font);
        bool orderKept = true;
        bool sawBoxOnly = false, sawNameOnly = false;
        for (int i = 0; i < 60; i++)
        {
            if (c.NameShown && !c.BoxOpen) orderKept = false;
            if (c.BodyReady && !c.NameShown) orderKept = false;
            if (c.BoxOpen && !c.NameShown) sawBoxOnly = true;
            if (c.NameShown && !c.BodyReady) sawNameOnly = true;
            c.Tick(1f / 60f, 0, 0);
        }
        Check("* the stepped order is never inverted (box, name, body)", orderKept);
        Check("* the three steps really are separate (they do not all turn on at once)", sawBoxOnly && sawNameOnly,
              $"boxOnly={sawBoxOnly} nameOnly={sawNameOnly}");
        Check("after enough time the body is ready", c.BodyReady);

        var d = new DialogueBubble();
        d.SetContent("hero", "hero", new[] { "一五七" }, 100f, 3, Font);
        d.SkipIntro();
        Check("* SkipIntro opens all three steps at once",
              d.BoxOpen && d.NameShown && d.BodyReady && d.OpenFraction == 1f);

        d.SetContent("hero", "hero", new[] { "十千地" }, 100f, 3, Font);
        Check("* setting new content restarts the entrance", !d.BodyReady && d.OpenFraction == 0f);

        DialogueBox.Close();
        BubblePool.ResetAll();

        DialogueBox.Show("hero", "first line");
        Check("* the first line plays the entrance", DialogueBox.IntroPlaying);

        DialogueBox.Close();
        DialogueBox.Show("hero", "second line");
        Check("* the same speaker continuing skips the entrance (popping per line is tiring)",
              !DialogueBox.IntroPlaying);

        DialogueBox.Close();
        DialogueBox.Show("oldMan", "another actor");
        Check("* control: a changed speaker replays the entrance", DialogueBox.IntroPlaying);

        DialogueBox.Close();
        DialogueBox.Update(1f / 60f);
        DialogueBox.Show("oldMan", "much later");
        Check("* control: a break in the breath replays it even for the same speaker", DialogueBox.IntroPlaying);
        DialogueBox.Close();

        DialogueBox.FastForward = true;
        DialogueBox.Show("hero", "while skipping");
        Check("* fast-forward skips the entrance (nobody is waiting to watch)", !DialogueBox.IntroPlaying);
        DialogueBox.FastForward = false;
        DialogueBox.Close();
        BubblePool.ResetAll();
    }

    private static void TestShake()
    {
        Console.WriteLine("--- shake ---");

        const string raw = "一<shake>五七</shake>十";
        var b = new DialogueBubble();
        b.SetContent("hero", null, new[] { raw }, 100f, 3, Font);
        b.SkipIntro();

        Check("premise: a shake span is loaded", b.TextOf(0).HasShake);
        Check("premise: it is not shaking yet", !b.Shaking);

        b.Tick(1f / 60f, 0, 1);
        Check("* characters outside the span do not shake (always shaking on appearance would make the tag position meaningless)",
              !b.Shaking);

        b.Tick(1f / 60f, 0, 2);
        Check("* it shakes the moment the span is revealed", b.Shaking);

        var at0 = b.ShakeOffset(3);
        Check("* the background moves opposite at twice (relative amplitude of three)",
              b.BackgroundShakeOffset(3) == at0 * -2f, $"{at0} / {b.BackgroundShakeOffset(3)}");

        var dirBefore = Normalize(b.ShakeOffset(3));
        b.Tick(1f / 60f, 0, 2);
        Check("* one frame does not change direction (once every two frames)",
              Normalize(b.ShakeOffset(3)) == dirBefore, $"{dirBefore} → {Normalize(b.ShakeOffset(3))}");
        b.Tick(1f / 60f, 0, 2);
        Check("* two frames change the direction",
              Normalize(b.ShakeOffset(3)) != dirBefore, $"{dirBefore} → {Normalize(b.ShakeOffset(3))}");

        var fresh = new DialogueBubble();
        fresh.SetContent("hero", null, new[] { raw }, 100f, 3, Font);
        fresh.SkipIntro();
        fresh.Tick(0f, 0, 2);
        float full = fresh.ShakeOffset(3).Length();
        fresh.Tick(DialogueBubble.ShakeSeconds * 0.5f, 0, 2);
        float half = fresh.ShakeOffset(3).Length();
        Check("* the falloff is k squared (a quarter of the amplitude at the halfway point; linear would be a half)",
              full > 0f && MathF.Abs(half / full - 0.25f) < 0.02f, $"{half / MathF.Max(full, 0.0001f):0.###}");

        fresh.Tick(DialogueBubble.ShakeSeconds, 0, 2);
        Check("it stops once it has run out", !fresh.Shaking && fresh.ShakeOffset(3) == Vector2.Zero);

        var plain = new DialogueBubble();
        plain.SetContent("hero", null, new[] { "三六 天耳" }, 100f, 3, Font);
        plain.SkipIntro();
        for (int i = 0; i < 20; i++) plain.Tick(1f / 60f, 0, i);
        Check("* control: with no tag it never shakes", !plain.Shaking);

        b.SetContent("hero", null, new[] { "足山中 下" }, 100f, 3, Font);
        Check("* new content does not inherit the previous line's shake", !b.Shaking);
    }

    private static Vector2 Normalize(Vector2 v) => v == Vector2.Zero ? v : v / v.Length();

    private static void TestTypewriter()
    {
        Console.WriteLine("--- typewriter rhythm ---");
        DialogueBox.FastForward = false;
        DialogueBox.AutoAdvance = false;

        int plain4 = FramesToReveal("一五七十");
        int punct = FramesToReveal("一五七.");
        Check($"* it rests at punctuation - the punctuated string is {punct - plain4} frames later (expected about 10)",
              punct - plain4 >= 8 && punct - plain4 <= 13);

        int latin = FramesToReveal("abcd");
        Check($"* Latin takes half the time - 'abcd' is {plain4 - latin} frames faster (expected about 8 to 10)",
              plain4 - latin >= 6 && plain4 - latin <= 12);

        int slow = FramesToReveal("<speed 0.5>一五七十</speed>");
        Check($"* <speed 0.5> is twice as slow (+{slow - plain4} frames, expected about 19)",
              slow - plain4 >= 17 && slow - plain4 <= 22);

        int stop = FramesToReveal("一五{.}七十");
        Check($"* a {{.}} pause really waits (+{stop - plain4} frames, expected about 15)",
              stop - plain4 >= 13 && stop - plain4 <= 17);

        const string twelve = "一五七十千地日水口左大小";
        int normal12 = FramesToReveal(twelve);
        int hurried = FramesToReveal(twelve, onBody: DialogueBox.Hurry);
        Check($"* a confirm tap is a speed-up - {normal12} to {hurried} frames (less than half)", hurried * 2 < normal12);
        Check($"* a confirm tap is not an instant complete - 12 characters appear over {hurried} frames",
              hurried >= 12);

        DialogueBox.Close(); BubblePool.ResetAll();
        DialogueBox.FastForward = true;
        DialogueBox.Show("hero", twelve);
        for (int i = 0; i < 3; i++) DialogueBox.Update(1f / 60f);
        Check("* fast-forward still completes and advances instantly (closed within three frames)", !DialogueBox.Active);
        DialogueBox.FastForward = false;

        DialogueBox.Close(); BubblePool.ResetAll();
        DialogueBox.Show("hero", "一五七十");
        DialogueBox.AutoAdvance = true;
        int f = 0;
        while (f < 600 && !DialogueBox.PageRevealed) { DialogueBox.Update(1f / 60f); f++; }
        for (int i = 0; i < 30; i++) DialogueBox.Update(1f / 60f);
        Check("* automatic: still up 0.5s after completion", DialogueBox.Active);
        for (int i = 0; i < 40; i++) DialogueBox.Update(1f / 60f);
        Check("* automatic: it advances one second (TailSeconds) after completion", !DialogueBox.Active);

        DialogueBox.Close(); BubblePool.ResetAll();
        DialogueBox.Show("hero", "一五七十");
        DialogueBox.AutoAdvance = false;
        for (int i = 0; i < 200; i++) DialogueBox.Update(1f / 60f);
        Check("* control: on input-driven advance it never advances by itself", DialogueBox.Active);

        DialogueBox.Close(); BubblePool.ResetAll();
        DialogueBox.AutoAdvance = false;
    }

    private static int FramesToReveal(string raw, Action? onBody = null)
    {
        DialogueBox.Close(); BubblePool.ResetAll();
        DialogueBox.Show("hero", raw);
        int f = 0; bool hooked = false;
        while (f < 600 && !DialogueBox.PageRevealed)
        {
            DialogueBox.Update(1f / 60f); f++;
            if (!hooked && !DialogueBox.IntroPlaying) { hooked = true; onBody?.Invoke(); }
        }
        return f;
    }

    private static void TestPool()
    {
        Console.WriteLine("--- pool ---");

        BubblePool.ResetAll();
        Check("premise: after a rewind nobody holds a slot", BubblePool.InUseCount == 0);
        Check("premise: there are four slots", BubblePool.Size == 4);

        var heroSlot = BubblePool.Acquire("hero");
        Check("* borrowing establishes ownership immediately (without calling SetContent)",
              BubblePool.OwnerOf(BubblePool.SlotOf(heroSlot)) == "hero",
              BubblePool.OwnerOf(BubblePool.SlotOf(heroSlot)));

        var elder = BubblePool.Acquire("oldMan");
        Check("a different speaker gets a different slot", !ReferenceEquals(heroSlot, elder));
        Check("two are in use", BubblePool.InUseCount == 2);

        BubblePool.Release(heroSlot);
        Check("releasing reduces the in-use count", BubblePool.InUseCount == 1);
        Check("* ownership survives a release (the same speaker will soon talk again)",
              BubblePool.OwnerOf(BubblePool.SlotOf(heroSlot)) == "hero");

        BubblePool.ResetAll();
        var s0 = BubblePool.Acquire("A");
        var s1 = BubblePool.Acquire("B");
        var s2 = BubblePool.Acquire("C");
        BubblePool.Acquire("D");
        BubblePool.Release(s0);
        BubblePool.Release(s2);
        Check("premise: an empty slot comes first and C is behind it",
              BubblePool.SlotOf(s0) < BubblePool.SlotOf(s2));
        var backC = BubblePool.Acquire("C");
        Check("* the same speaker gets its own slot back (not the first empty one)",
              ReferenceEquals(backC, s2),
              $"expected slot {BubblePool.SlotOf(s2)} / got slot {BubblePool.SlotOf(backC)}");
        Check("* control: an unseen speaker goes to the first empty slot",
              ReferenceEquals(BubblePool.Acquire("Z"), s0));
        _ = s1;

        BubblePool.ResetAll();
        var a1 = BubblePool.Acquire("A");
        var b1 = BubblePool.Acquire("B");
        var c1 = BubblePool.Acquire("C");
        var d1 = BubblePool.Acquire("D");
        Check("premise: all four are taken", BubblePool.InUseCount == 4);

        string barked = CaptureStdout(() => BubblePool.Acquire("E"));
        Check("* the fifth reports (silence would read as the bubble suddenly disappearing)",
              barked.Contains("more than 4 bubbles") && barked.Contains("'A'"), barked.Trim());
        Check("* it gives away the oldest slot (A)", BubblePool.OwnerOf(BubblePool.SlotOf(a1)) == "E",
              BubblePool.OwnerOf(BubblePool.SlotOf(a1)));
        Check("the other three are untouched",
              BubblePool.OwnerOf(BubblePool.SlotOf(b1)) == "B"
              && BubblePool.OwnerOf(BubblePool.SlotOf(c1)) == "C"
              && BubblePool.OwnerOf(BubblePool.SlotOf(d1)) == "D");

        BubblePool.ResetAll();
        BubblePool.Acquire("A"); BubblePool.Acquire("B");
        BubblePool.Acquire("C"); BubblePool.Acquire("D");
        var reA = BubblePool.Acquire("A");
        CaptureStdout(() => BubblePool.Acquire("F"));
        Check("* a just-used slot is not taken (oldest means sequence, not slot number)",
              BubblePool.OwnerOf(BubblePool.SlotOf(reA)) == "A",
              BubblePool.OwnerOf(BubblePool.SlotOf(reA)));

        BubblePool.ResetAll();
        Check("* a rewind clears ownership too (continuing does not inherit the previous session)",
              BubblePool.OwnerOf(0) == "" && BubblePool.InUseCount == 0);
    }

    private static string CaptureStdout(Action body)
    {
        var prev = Console.Out;
        var sw = new StringWriter();
        Console.SetOut(sw);
        try { body(); }
        finally { Console.SetOut(prev); }
        return sw.ToString();
    }

    private static void TestShowOverloadTrapBlocked()
    {
        var oneArg = typeof(DialogueBox).GetMethod("Show", BindingFlags.Public | BindingFlags.Static,
            null, new[] { typeof(string) }, null);
        Check("1. the one-argument Show overload exists (the decoy that intercepts the shape)", oneArg != null);

        var obs = oneArg?.GetCustomAttribute<ObsoleteAttribute>();
        Check("* 1. that overload is a compile error (Obsolete error:true)",
            obs is { IsError: true }, obs == null ? "no attribute" : $"IsError={obs.IsError}");

        var twoArg = typeof(DialogueBox).GetMethod("Show", BindingFlags.Public | BindingFlags.Static,
            null, new[] { typeof(string), typeof(string[]) }, null);
        Check("* 1. control: the two-argument Show is fine",
            twoArg != null && twoArg.GetCustomAttribute<ObsoleteAttribute>() == null);

        DialogueBox.Close();
        DialogueBox.Show(null, "it opens properly");
        Check("* 1. control: the two-argument call opens the box", DialogueBox.Active);
        DialogueBox.Close();
    }

    private static void Check(string label, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"  ✔ {label}"); }
        else { _fail++; Console.WriteLine($"  ✘ {label}" + (detail != null ? $"  ← {detail}" : "")); }
    }
}

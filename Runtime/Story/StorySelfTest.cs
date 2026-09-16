using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PixelCore.Runtime.Story;

public static class StorySelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        Console.WriteLine("=== Story self-test ===");

        TestGoldenSample();
        TestBlocksAndVariants();
        TestChoices();
        TestLineDecorations();
        TestErrors();
        TestLibrary();
        TestSingleSpeakerFile();
        TestAttributeVocabulary();
        TestMarkupStrip();
        TestRunner();
        TestChoicePlayback();
        TestEvents();
        TestHotReload();
        TestInlineSay();
        TestRealContent();

        Console.WriteLine($"=== {_pass} passed, {_fail} failed ===");
    }

    private static void TestGoldenSample()
    {
        Console.WriteLine("--- golden sample ---");

        var errors = new List<string>();
        var s = StoryParser.Parse("""
            # store.night

            owner: Late again.                          #1
            hero: Sorry. {.} The bus...                #2
            owner: <shake>Excuses.</shake>   [offset:-8,0]  #3
            hero: What now.                           #4
            > Apologise
                hero: It will not happen again.                 #5
            > Say nothing
                narration: The silence stretched.              #6
            """, "night.story", errors);

        Check("no errors", errors.Count == 0, string.Join(" / ", errors));
        Check("namespace", s.Namespace == "store.night", s.Namespace);
        Check("one block (root)", s.Blocks.Count == 1 && s.Blocks[0].Name == "");
        Check("root block ID = namespace", s.Blocks[0].Id == "store.night");

        var nodes = s.Blocks[0].Variants[0].Nodes;
        Check("five nodes (4 lines + 1 choice group)", nodes.Count == 5, $"actual {nodes.Count}");

        var l1 = nodes[0] as DialogueLineNode;
        Check("speaker split", l1?.Speaker == "owner" && l1.Text == "Late again.", $"{l1?.Speaker}/{l1?.Text}");
        Check("translation number", l1?.Number == 1);

        var l2 = nodes[1] as DialogueLineNode;
        Check("{point} markup stays in the text",
            l2?.Text == "Sorry. {.} The bus...", l2?.Text);

        var l3 = nodes[2] as DialogueLineNode;
        Check("span markup stays too", l3?.Text == "<shake>Excuses.</shake>", l3?.Text);
        Check("line attributes are split out of the text",
            l3 != null && l3.TryGetAttribute("offset", out var off) && off == "-8,0");
        Check("attribute and number together", l3?.Number == 3);

        var choice = nodes[4] as DialogueChoiceNode;
        Check("two choices", choice?.Options.Count == 2);
        Check("choice text", choice?.Options[0].Text == "Apologise", choice?.Options[0].Text);
        Check("a choice result is the indented lines", choice?.Options[0].Nodes.Count == 1);
        Check("narration is just a reserved speaker",
            (choice?.Options[1].Nodes[0] as DialogueLineNode)?.Speaker == "narration");
    }

    private static void TestBlocksAndVariants()
    {
        Console.WriteLine("--- blocks and per-visit variants ---");

        var errors = new List<string>();
        var s = StoryParser.Parse("""
            # examine.store

            # everything from here is a comment

            @signpost
            hero: An old signpost.
            hero: No coins on me.
            -- visit 2
            hero: ...still old, even now.
            -- after
            hero: Never mind.

            @bin
            hero: ...I should empty this.
            """, "store.story", errors);

        Check("no errors", errors.Count == 0, string.Join(" / ", errors));
        Check("a '#' after the header is a comment", s.Blocks.Count == 2, $"{s.Blocks.Count} blocks");
        Check("block ID = namespace.block", s.Blocks[0].Id == "examine.store.signpost", s.Blocks[0].Id);

        var vend = s.Blocks[0];
        Check("three variants", vend.Variants.Count == 3, $"actual {vend.Variants.Count}");
        Check("starts at visit 1 / 2 / 3 ('after' = one past the previous)",
            vend.Variants[0].FromVisit == 1 && vend.Variants[1].FromVisit == 2 && vend.Variants[2].FromVisit == 3,
            string.Join(",", vend.Variants.ConvertAll(v => v.FromVisit)));
        Check("first visit = two lines", vend.Variants[0].Nodes.Count == 2);

        Check("VariantFor(1) gives visit 1", vend.VariantFor(1).FromVisit == 1);
        Check("VariantFor(2) gives visit 2", vend.VariantFor(2).FromVisit == 2);
        Check("VariantFor(9) keeps the last one", vend.VariantFor(9).FromVisit == 3);

        errors.Clear();
        var s2 = StoryParser.Parse("""
            # examine.room

            @bed
            hero: I want to sleep.
            -- visit 2
            hero: I really want to sleep.
            """, "room.story", errors);
        var bed = s2.Blocks[0];
        Check("with no 'after', the last variant repeats",
            bed.VariantFor(2).FromVisit == 2 && bed.VariantFor(50).FromVisit == 2);
    }

    private static void TestChoices()
    {
        Console.WriteLine("--- choices ---");

        var errors = new List<string>();
        var s = StoryParser.Parse("""
            # store.day

            hero: Should I buy it?
            > Buy
            	hero: One, please.
            	owner: Twelve hundred.
            > Do not buy
                hero: Next time.
            hero: Outside again.
            """, "day.story", errors);

        Check("no errors", errors.Count == 0, string.Join(" / ", errors));
        var nodes = s.Blocks[0].Variants[0].Nodes;
        Check("a line after a choice returns outside the group (line, choice, line)",
            nodes.Count == 3 && nodes[0] is DialogueLineNode && nodes[1] is DialogueChoiceNode
            && nodes[2] is DialogueLineNode, $"{nodes.Count} nodes");

        var ch = (DialogueChoiceNode)nodes[1];
        Check("tab indentation is treated the same", ch.Options[0].Nodes.Count == 2);
        Check("space indentation", ch.Options[1].Nodes.Count == 1);
        Check("a line outside the group is not pulled into the choice",
            (nodes[2] as DialogueLineNode)?.Text == "Outside again.");

        errors.Clear();
        var nested = StoryParser.Parse("""
            # test.nested

            hero: What now.
            > Ask
                hero: Excuse me.
                > Ask more
                    hero: Just one more.
            """, "nested.story", errors);
        var outer = (DialogueChoiceNode)nested.Blocks[0].Variants[0].Nodes[1];
        Check("nested choices", errors.Count == 0 && outer.Options[0].Nodes.Count == 2
            && outer.Options[0].Nodes[1] is DialogueChoiceNode, string.Join(" / ", errors));
    }

    private static void TestLineDecorations()
    {
        Console.WriteLine("--- line attributes and numbers ---");

        var errors = new List<string>();
        var s = StoryParser.Parse("""
            # test.decoration

            hero: Two attributes.  [offset:-8,0] [light:dim]
            hero: Number only.  #7
            hero: No decoration.
            hero: Brackets in the body [not an attribute]
            hero: A colon in the body: meet at 3:30.
            """, "decoration.story", errors);

        Check("no errors", errors.Count == 0, string.Join(" / ", errors));
        var n = s.Blocks[0].Variants[0].Nodes;

        var a = (DialogueLineNode)n[0];
        Check("two attributes, in order", a.Attributes.Count == 2
            && a.Attributes[0].Key == "offset" && a.Attributes[1].Key == "light",
            string.Join(",", a.Attributes.ConvertAll(x => x.Key)));
        Check("only the body remains once attributes are stripped", a.Text == "Two attributes.", a.Text);

        Check("number only", ((DialogueLineNode)n[1]).Number == 7 && ((DialogueLineNode)n[1]).Text == "Number only.");
        Check("no decoration means number 0 (not yet stamped)",
            ((DialogueLineNode)n[2]).Number == 0 && ((DialogueLineNode)n[2]).Attributes.Count == 0);
        Check("anything not shaped like 'key:value' stays body text",
            ((DialogueLineNode)n[3]).Text == "Brackets in the body [not an attribute]",
            ((DialogueLineNode)n[3]).Text);
        Check("the speaker splits only at the first colon",
            ((DialogueLineNode)n[4]).Speaker == "hero"
            && ((DialogueLineNode)n[4]).Text == "A colon in the body: meet at 3:30.");
    }

    private static void TestErrors()
    {
        Console.WriteLine("--- error detection (planted failures) ---");

        Bad("no header", "hero: Hi.", "no namespace declaration");
        Bad("whitespace in the namespace", "# store night\nhero: Hi.", "whitespace");
        Bad("a line with no speaker", "# test.a\nHello there.", "no speaker");
        Bad("has an empty line", "# test.a\nhero:", "has an empty line");
        Bad("duplicate block", "# test.a\n@door\nhero: 1.\n@door\nhero: 2.", "duplicate block");
        Bad("empty block", "# test.a\n@door\n\n@window\nhero: 2.", "empty block");

        Bad("a mistyped visit divider", "# test.a\n@door\nhero: 1.\n-- second\nhero: 2.", "a visit divider must be");
        Bad("visits going backwards", "# test.a\n@door\nhero: 1.\n-- visit 3\nhero: 3.\n-- visit 2\nhero: 2.", "goes backwards");
        Bad("an empty section before a variant", "# test.a\n@door\n-- visit 2\nhero: 2.", "nothing would play on the first visit");

        Bad("unknown tag", "# test.a\nhero: <wave>wobble</wave>", "unknown tag");
        Bad("an unclosed tag", "# test.a\nhero: <shake>wobble", "is not closed");
        Bad("a mismatched close", "# test.a\nhero: <shake>wobble</speed>", "does not match");
        Bad("an unclosed brace", "# test.a\nhero: next {stop", "unclosed");

        Bad("a duplicate translation number", "# test.a\nhero: One. #4\nhero: Two. #4", "duplicate translation number #4");
        Bad("empty choice text", "# test.a\nhero: What is it.\n> ", "the choice text is empty");
        Bad("a line attribute on a choice", "# test.a\nhero: What is it.\n> Buy [offset:0,0]", "a choice takes no line attributes");
        Bad("a second namespace declaration", "# test.a\nhero: One.\n# test.b\nhero: Two.", "one namespace declaration per file");

        var errors = new List<string>();
        StoryParser.Parse("""
            # test.many

            Hello there.
            hero: <wave>One</wave>
            hero: Two. #1
            hero: Three. #1
            """, "many.story", errors);
        Check($"four errors collected at once (actual {errors.Count})", errors.Count == 4,
            string.Join(" / ", errors));
        Check("errors carry file:line", errors.Count > 0 && errors[0].StartsWith("many.story:3", StringComparison.Ordinal),
            errors.Count > 0 ? errors[0] : "");
    }

    private static void Bad(string label, string source, string expect)
    {
        var errors = new List<string>();
        StoryParser.Parse(source, "t.story", errors);
        bool hit = false;
        foreach (var e in errors) if (e.Contains(expect, StringComparison.Ordinal)) { hit = true; break; }
        Check(label, hit, errors.Count == 0 ? "no errors (missed it)" : string.Join(" / ", errors));
    }

    private static void TestLibrary()
    {
        Console.WriteLine("--- StoryLibrary ---");

        string dir = Path.Combine(Path.GetTempPath(), "pixelcore_story_selftest");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(Path.Combine(dir, "Examine", "Ch1"));

        Write(Path.Combine(dir, "night.story"), "# store.night\n\nowner: Late again.\nhero: Sorry.\n");
        Write(Path.Combine(dir, "Examine", "Ch1", "store.story"),
            "# examine.store\n\n@signpost\nhero: An old signpost.\n");

        var lib = StoryLibrary.Load(dir);
        Check("no load errors", lib.Errors.Count == 0, string.Join(" / ", lib.Errors));
        Check("two files, two blocks", lib.Scripts.Count == 2 && lib.BlockCount == 2);
        Check("even a deeply nested file takes its ID from the header (path irrelevant)",
            lib.TryGetBlock("examine.store.signpost", out _));
        Check("root block lookup", lib.TryGetBlock("store.night", out var night) && night.Variants[0].Nodes.Count == 2);
        Check("a missing block", !lib.TryGetBlock("examine.store.missing", out _));

        int lines = 0, options = 0;
        foreach (var (_, line, opt) in lib.EnumerateText())
        { if (line != null) lines++; if (opt != null) options++; }
        Check("walking every line (three lines)", lines == 3 && options == 0, $"{lines} lines, {options} choices");

        Write(Path.Combine(dir, "Examine", "copy.story"), "# store.night\n\nowner: Again?\n");
        var lib2 = StoryLibrary.Load(dir);
        bool dup = false;
        foreach (var e in lib2.Errors) if (e.Contains("duplicate namespace", StringComparison.Ordinal)) dup = true;
        Check("a duplicate namespace is an error even across folders", dup, string.Join(" / ", lib2.Errors));
        Check("only one block survives a duplicate (lookup stays unambiguous)",
            lib2.Scripts.Count == 2 && lib2.BlockCount == 2 && lib2.TryGetBlock("store.night", out _),
            $"{lib2.Scripts.Count} files, {lib2.BlockCount} blocks");
    }

    private static void TestSingleSpeakerFile()
    {
        Console.WriteLine("--- single-speaker file ---");

        var errors = new List<string>();
        var s = StoryParser.Parse("""
            # examine.house [speaker:hero]

            @bed
            A bed
            I want to sleep

            @sign
            A sign. No entry: staff only
            """, "house.story", errors);

        Check("no errors", errors.Count == 0, string.Join(" / ", errors));
        Check("the namespace survives having the header attribute stripped", s.Namespace == "examine.house", s.Namespace);
        Check("default speaker", s.DefaultSpeaker == "hero", s.DefaultSpeaker);

        var bed = s.Blocks[0].Variants[0].Nodes;
        Check("lines stand without writing a speaker", bed.Count == 2);
        Check("the speaker is the declared one", (bed[0] as DialogueLineNode)?.Speaker == "hero");
        Check("the body is taken whole", (bed[0] as DialogueLineNode)?.Text == "A bed");

        var sign = (DialogueLineNode)s.Blocks[1].Variants[0].Nodes[0];
        Check("a body colon is not misparsed as a speaker (a single-speaker file ignores colons)",
            sign.Speaker == "hero" && sign.Text == "A sign. No entry: staff only",
            $"{sign.Speaker} / {sign.Text}");
        Check("a long prefix does not even warn (it is a sentence)", s.Warnings.Count == 0,
            string.Join(" / ", s.Warnings));

        errors.Clear();
        var s2 = StoryParser.Parse("""
            # examine.house [speaker:hero]

            @counter
            owner: Busy?
            """, "house.story", errors);
        Check("body text that looks like a speaker is not an error", errors.Count == 0, string.Join(" / ", errors));
        Check("a warning is left instead (so 'owner: Busy?' does not silently reach the screen)",
            s2.Warnings.Count == 1 && s2.Warnings[0].Contains("looks like a speaker", StringComparison.Ordinal),
            string.Join(" / ", s2.Warnings));
        Check("the body still goes in whole",
            (s2.Blocks[0].Variants[0].Nodes[0] as DialogueLineNode)?.Text == "owner: Busy?");

        errors.Clear();
        StoryParser.Parse("# store.night\n\nLate again.", "night.story", errors);
        Check("a file with no declaration still requires a speaker",
            errors.Count > 0 && errors[0].Contains("no speaker", StringComparison.Ordinal),
            string.Join(" / ", errors));

        Bad("an empty header speaker is an error", "# examine.house [speaker:]\nhero: One.", "[speaker:...] is empty");
        Bad("whitespace in the header speaker", "# examine.house [speaker:mr owner]\nOne.", "whitespace in the speaker name");
        Bad("unknown header attribute", "# examine.house [speakr:hero]\nhero: One.", "unknown header attribute");
        Bad("a translation number on the header", "# examine.house #3\nhero: One.", "the header takes no translation number");
    }

    private static void TestAttributeVocabulary()
    {
        Console.WriteLine("--- line attribute vocabulary ---");

        var errors = new List<string>();
        StoryParser.Parse("# test.a\nhero: One. [offset:-8,0] [light:dim] [event:doorOpen]", "t.story", errors);
        Check("the three known keys pass", errors.Count == 0, string.Join(" / ", errors));

        Bad("a mistyped key is caught (so it does not silently become an inert attribute)",
            "# test.a\nhero: One. [offst:-8,0]", "unknown line attribute");
        Bad("a header-only key used on a line", "# test.a\nhero: One. [speaker:owner]", "is a header-only attribute");
        Bad("a line-only key used on the header", "# test.a [offset:0,0]\nhero: One.", "unknown header attribute");
    }

    private static void TestMarkupStrip()
    {
        Console.WriteLine("--- markup into plain characters ---");

        Check("a span tag is stripped and the characters inside remain",
            DialogueMarkup.Plain("<shake>Excuses.</shake>") == "Excuses.",
            DialogueMarkup.Plain("<shake>Excuses.</shake>"));
        Check("a tag with an argument too",
            DialogueMarkup.Plain("<speed 0.3>slowly</speed> we go") == "slowly we go",
            DialogueMarkup.Plain("<speed 0.3>slowly</speed> we go"));
        Check("timing points are stripped and spaces fold to one",
            DialogueMarkup.Plain("Sorry. {.} The bus...") == "Sorry. The bus...",
            DialogueMarkup.Plain("Sorry. {.} The bus..."));

        DialogueMarkup.ResetWarnings();
        string barked = CaptureStdout(() =>
            Check("an unknown tag still keeps its characters (playback continues)",
                DialogueMarkup.Plain("<shkae>emph</shkae>") == "emph",
                DialogueMarkup.Plain("<shkae>emph</shkae>")));
        Check("* an unknown tag is reported by name", barked.Contains("unknown tag <shkae>"), barked.Trim());

        string again = CaptureStdout(() => DialogueMarkup.Plain("<shkae>again</shkae>"));
        Check("the same tag is not reported twice", !again.Contains("unknown tag"), again.Trim());
        string other = CaptureStdout(() => DialogueMarkup.Plain("<blur>another typo</blur>"));
        Check("* a different tag is reported separately (the latch is per name)",
            other.Contains("unknown tag <blur>"), other.Trim());

        string known = CaptureStdout(() => DialogueMarkup.Plain("<shake>real</shake>"));
        Check("a known tag is silent", !known.Contains("unknown tag"), known.Trim());
        DialogueMarkup.ResetWarnings();
        Check("{====} as well",
            DialogueMarkup.Plain("pau{====}se") == "pause");
        Check("no markup means unchanged", DialogueMarkup.Plain("just a sentence") == "just a sentence");

        Check("an unresolved {interpolation} stays visible",
            DialogueMarkup.Plain("Next stop: {stop}") == "Next stop: {stop}",
            DialogueMarkup.Plain("Next stop: {stop}"));
    }

    private static void TestRunner()
    {
        Console.WriteLine("--- runner ---");

        string dir = Path.Combine(Path.GetTempPath(), "pixelcore_story_runner");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        Write(Path.Combine(dir, "house.story"), """
            # examine.house

            @bed
            hero: A bed.
            hero: I want to sleep.
            -- visit 2
            hero: The bed again.

            @calendar
            hero: <shake>2017</shake>, December 21. {.} Already.

            @fork
            hero: What now.
            > Lie down
                hero: Lying down.
            """);

        var lib = StoryLibrary.Load(dir);
        StoryLibrary.UseLibrary(lib);

        var bb = new Core.Blackboard();
        DialogueRunner.Bind(bb);
        DialogueRunner.Stop();
        UI.DialogueBox.Close();

        string? finishedId = null;
        void OnFinished(string id) => finishedId = id;
        DialogueRunner.Finished += OnFinished;

        Check("playback starts", DialogueRunner.Play("examine.house.bed"));
        Check("the first line is up in the box", UI.DialogueBox.Active);
        Check("the block ID is exposed", DialogueRunner.CurrentBlockId == "examine.house.bed");
        Check("visit 1 recorded (the blackboard is what the save takes wholesale)",
            DialogueRunner.VisitCount("examine.house.bed") == 1);

        UI.DialogueBox.Close();
        DialogueRunner.Update(1f / 60f);
        Check("closing opens the next line on the same frame", UI.DialogueBox.Active);
        Check("still playing", DialogueRunner.Active);

        UI.DialogueBox.Close();
        DialogueRunner.Update(1f / 60f);
        Check("it ends after the last line", !DialogueRunner.Active && !UI.DialogueBox.Active);
        Check("the finished event", finishedId == "examine.house.bed");

        Check("second playback", DialogueRunner.Play("examine.house.bed"));
        Check("visit 2", DialogueRunner.VisitCount("examine.house.bed") == 2);
        UI.DialogueBox.Close();
        DialogueRunner.Update(1f / 60f);
        Check("the second visit has one line (the variant switched)", !DialogueRunner.Active);

        lib.TryGetBlock("examine.house.calendar", out var cal);
        string shown = DialogueRunner.LineText(cal, (DialogueLineNode)cal.Variants[0].Nodes[0]);
        Check("the characters that reach the screen carry no markup", shown == "2017, December 21. Already.", shown);

        Check("a missing block returns false", !DialogueRunner.Play("examine.house.missing"));
        Check("a missing block does not open the box", !UI.DialogueBox.Active);

        Check("plays up to the line before the choice", DialogueRunner.Play("examine.house.fork"));
        UI.DialogueBox.Close();
        DialogueRunner.Update(1f / 60f);
        Check("* the choice appears (this used to cut the block short)", UI.DialogueBox.ChoiceActive);
        Check("* and it is still playing (not cut short)", DialogueRunner.Active);
        DialogueRunner.Stop();
        UI.DialogueBox.Close();

        Check("with no display name, the identifier", DialogueRunner.SpeakerLabel("nobody") == "nobody");
        var table = new PixelCore.Runtime.Text.StringTable();
        string labelFile = Path.Combine(dir, "name.strings");
        Write(labelFile, "name.owner = Boss\n");
        table.LoadFile(labelFile);
        PixelCore.Runtime.Text.Loc.UseTable(table);
        Check("with a display name, from the label table", DialogueRunner.SpeakerLabel("owner") == "Boss");
        Check("an unknown speaker passes through (it does not silently blank)", DialogueRunner.SpeakerLabel("passerby") == "passerby");

        DialogueRunner.Finished -= OnFinished;
        DialogueRunner.Stop();
        PixelCore.Runtime.Text.Loc.Load();
    }

    private static void TestChoicePlayback()
    {
        Console.WriteLine("--- choice playback ---");

        var dir = Path.Combine(Path.GetTempPath(), "pixelcore_choice_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var prevLibrary = StoryLibrary.Current;
        try
        {
            Write(Path.Combine(dir, "fork.story"), """
                # check.fork

                @doorway
                hero: The door is locked.
                > Knock
                    hero: Knock knock.
                    hero: No answer. [event:knock]
                > Walk away
                    hero: Turned away.
                > Use the key
                    > Front door
                        hero: Opened the front door.
                    > Back door
                        hero: Opened the back door.
                hero: And then morning came.

                @plain
                hero: First line.
                hero: Second line.
                """);

            var lib = StoryLibrary.Load(dir);
            StoryLibrary.UseLibrary(lib);

            var bb = new Core.Blackboard();
            DialogueRunner.Bind(bb);
            DialogueRunner.Stop();
            UI.DialogueBox.Close();

            DialogueEvents.Clear();

            Check("1. playback starts", DialogueRunner.Play("check.fork.doorway"));
            Check("1. the first line comes up as body (not a choice)",
                  UI.DialogueBox.Active && !UI.DialogueBox.ChoiceActive);

            Advance();
            Check("1. * dismissing the previous line brings up the choice", UI.DialogueBox.ChoiceActive);
            Check("1. it stays modal (no walking around while choosing)",
                  UI.DialogueBox.Active
                  && Cutscenes.FreezeState.IsFrozen(Cutscenes.FreezeFlags.PlayerInputSoft));

#if DEBUG
            string fired = "";
            DialogueEvents.Register("knock", () => fired = "knock");

            Press(Core.GameAction.Confirm);
            UI.DialogueBox.Update(1f / 60f);
            Check("2. * input on the frame it appears is ignored (nothing is chosen sight unseen)",
                  UI.DialogueBox.ChoiceActive);

            Tick(); Press(Core.GameAction.MoveDown); UI.DialogueBox.Update(1f / 60f);
            Tick(); Press(Core.GameAction.MoveDown); UI.DialogueBox.Update(1f / 60f);
            Tick(); Press(Core.GameAction.MoveUp);   UI.DialogueBox.Update(1f / 60f);
            Tick(); Press(Core.GameAction.Confirm);  UI.DialogueBox.Update(1f / 60f);
            DialogueRunner.Update(1f / 60f);

            Check("3. * choosing brings up that branch's line",
                  UI.DialogueBox.Active && !UI.DialogueBox.ChoiceActive);
            Check("3. * it is the chosen branch (not just a moved index)",
                  UI.DialogueBox.CurrentText == "Turned away.",
                  UI.DialogueBox.CurrentText);
            Check("3. * the unchosen branch's event does not fire", fired == "", fired);

            Advance();
            Check("4. * when the branch ends it returns to the line after the choice (frame stack)",
                  UI.DialogueBox.CurrentText == "And then morning came.",
                  UI.DialogueBox.CurrentText);
            Advance();
            Check("4. and the block ends", !DialogueRunner.Active);

            fired = "";
            Check("5. second playback", DialogueRunner.Play("check.fork.doorway"));
            Advance();
            Check("5. premise: the choice is up again", UI.DialogueBox.ChoiceActive);
            ArmChoice();
            Tick(); Press(Core.GameAction.Confirm); UI.DialogueBox.Update(1f / 60f);
            DialogueRunner.Update(1f / 60f);
            Check("5. the first line of the first option", UI.DialogueBox.CurrentText == "Knock knock.",
                  UI.DialogueBox.CurrentText);
            Advance();
            Check("5. * the chosen branch's [event:] fires - this is the door for recording a choice",
                  fired == "knock", fired);

            Check("6. * the visit count accumulates on the blackboard (the save carries it as is)",
                  DialogueRunner.VisitCount("check.fork.doorway") == 2 &&
                  bb.GetInt(DialogueRunner.VisitKeyPrefix + "check.fork.doorway") == 2,
                  DialogueRunner.VisitCount("check.fork.doorway").ToString());

            DialogueRunner.Stop();
            UI.DialogueBox.Close();

            DialogueRunner.Play("check.fork.doorway");
            Advance();
            ArmChoice();
            Tick(); Press(Core.GameAction.MoveUp); UI.DialogueBox.Update(1f / 60f);
            Tick(); Press(Core.GameAction.Confirm); UI.DialogueBox.Update(1f / 60f);
            DialogueRunner.Update(1f / 60f);
            Check("7. * a choice inside a choice appears (recursion is free with a frame stack)",
                  UI.DialogueBox.ChoiceActive);
            ArmChoice();
            Tick(); Press(Core.GameAction.MoveDown); UI.DialogueBox.Update(1f / 60f);
            Tick(); Press(Core.GameAction.Confirm); UI.DialogueBox.Update(1f / 60f);
            DialogueRunner.Update(1f / 60f);
            Check("7. * the inner branch runs", UI.DialogueBox.CurrentText == "Opened the back door.",
                  UI.DialogueBox.CurrentText);
            Advance();
            Check("7. * it unwinds two levels back to the outer line",
                  UI.DialogueBox.CurrentText == "And then morning came.",
                  UI.DialogueBox.CurrentText);

            DialogueRunner.Stop();
            UI.DialogueBox.Close();

#endif
            DialogueRunner.Stop();
            UI.DialogueBox.Close();
            DialogueRunner.Play("check.fork.doorway");
            Advance();
            Check("7b. premise: a choice is up", UI.DialogueBox.ChoiceActive);
            DialogueRunner.Play("check.fork.plain");
            Check("7b. * opening new dialogue leaves choice mode", !UI.DialogueBox.ChoiceActive);
            Check("7b. * and the body is actually visible", UI.DialogueBox.CurrentText == "First line.",
                  UI.DialogueBox.CurrentText);
            DialogueRunner.Stop();
            UI.DialogueBox.Close();

            Check("8. a block with no choices", DialogueRunner.Play("check.fork.plain"));
            Check("8. first line", UI.DialogueBox.CurrentText == "First line.");
            Advance();
            Check("8. second line", UI.DialogueBox.CurrentText == "Second line.");
            Advance();
            Check("8. it ends", !DialogueRunner.Active);

#if DEBUG
            UI.DialogueBox.FastForward = true;
            DialogueRunner.Play("check.fork.doorway");
            Advance();
            Check("9. * fast-forward does not auto-choose (choices are not auto-advanced)",
                  UI.DialogueBox.ChoiceActive);
            Tick();
            ArmChoice();
            UI.DialogueBox.Update(1f / 60f);
            UI.DialogueBox.Update(1f / 60f);
            Check("9. * it stays put over several frames (fast-forward does not page the list)",
                  UI.DialogueBox.ChoiceActive);

            DialogueRunner.Stop();
            UI.DialogueBox.Close();
            UI.DialogueBox.FastForward = false;
#endif
        }
        finally
        {
            DialogueEvents.Clear();
            StoryLibrary.UseLibrary(prevLibrary);
            DialogueRunner.Stop();
            UI.DialogueBox.Close();
#if DEBUG
            foreach (var k in new[] { Core.GameAction.Confirm, Core.GameAction.MoveUp, Core.GameAction.MoveDown })
                ReleaseAll(k);
#endif
            try { Directory.Delete(dir, true); } catch {  }
        }
    }

    private static void ArmChoice() => UI.DialogueBox.Update(1f / 60f);

    private static void Advance()
    {
        UI.DialogueBox.Close();
        DialogueRunner.Update(1f / 60f);
    }

#if DEBUG
    private static void Tick()
    {
        foreach (var a in new[] { Core.GameAction.Confirm, Core.GameAction.MoveUp, Core.GameAction.MoveDown })
            ReleaseAll(a);
        Core.Input.DebugTick();
    }
#endif

#if DEBUG
    private static void Press(Core.GameAction action)
    {
        foreach (var k in Core.InputMap.ScancodesOf(action)) Core.Input.DebugSetKey(k, true);
    }

    private static void ReleaseAll(Core.GameAction action)
    {
        foreach (var k in Core.InputMap.ScancodesOf(action)) Core.Input.DebugSetKey(k, false);
    }
#endif

    private static void TestEvents()
    {
        Console.WriteLine("--- event hooks ---");

        string dir = Path.Combine(Path.GetTempPath(), "pixelcore_story_events");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        Write(Path.Combine(dir, "store" + StoryLibrary.Extension), """
            # store.night

            @counter
            owner: Take this.    [event:doorOpen]
            hero: ...thank you.
            owner: Goodbye.          [event:doorClose] [event:lightsOff]
            """);

        var lib = StoryLibrary.Load(dir);
        StoryLibrary.UseLibrary(lib);
        DialogueEvents.Clear();

        Check("parsing itself passes (event values are unknown at parse time)",
            lib.Errors.Count == 0, string.Join(" / ", lib.Errors));

        var before = lib.ValidateEvents();
        Check("before registration, three unregistered names are caught", before.Count == 3, string.Join(" / ", before));

        int opened = 0, closed = 0, lights = 0;
        DialogueEvents.Register("doorOpen", () => opened++);
        DialogueEvents.Register("doorClose", () => closed++);
        DialogueEvents.Register("lightsOff", () => lights++);
        Check("after registration, none", lib.ValidateEvents().Count == 0);

        var bb = new Core.Blackboard();
        DialogueRunner.Bind(bb);
        DialogueRunner.Stop();
        UI.DialogueBox.Close();

        DialogueRunner.Play("store.night.counter");
        Check("it fires on the first line (not on close)", opened == 1 && closed == 0);

        UI.DialogueBox.Close();
        DialogueRunner.Update(1f / 60f);
        Check("a line with no event is silent", opened == 1 && closed == 0 && lights == 0);

        UI.DialogueBox.Close();
        DialogueRunner.Update(1f / 60f);
        Check("several on one line all fire (looking at the first alone kills the second silently)",
            closed == 1 && lights == 1, $"close {closed}, lights {lights}");

        DialogueEvents.Clear();
        DialogueEvents.Register("doorOpen", () => throw new InvalidOperationException("deliberate"));
        DialogueRunner.Stop();
        UI.DialogueBox.Close();
        DialogueRunner.Play("store.night.counter");
        Check("a throwing handler does not stop the dialogue (a failed effect is not a halted script)",
            UI.DialogueBox.Active && DialogueRunner.Active);

        DialogueEvents.Clear();
        DialogueRunner.Stop();
        UI.DialogueBox.Close();
        Check("it still plays when unregistered", DialogueRunner.Play("store.night.counter")
            && UI.DialogueBox.Active);

        DialogueRunner.Stop();
        UI.DialogueBox.Close();
        DialogueEvents.Clear();
    }

    private static void TestHotReload()
    {
#if !DEBUG
        Console.WriteLine("--- hot reload (absent in release builds - skipped) ---");
#else
        Console.WriteLine("--- hot reload ---");

        string dir = Path.Combine(Path.GetTempPath(), "pixelcore_hotreload");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);

        string file = Path.Combine(dir, "house" + StoryLibrary.Extension);
        Write(file, "# examine.house\n\n@bed\nhero: A bed\n");

        int reloads = 0;
        Core.ContentHotReload.Clear();
        Core.ContentHotReload.Watch(dir, "*" + StoryLibrary.Extension, "dialogue",
            () => { StoryLibrary.UseLibrary(StoryLibrary.Load(dir)); reloads++; });

        Check("registering alone does not trigger a reload", Core.ContentHotReload.CheckNow() == 0);

        Write(file, "# examine.house\n\n@bed\nhero: now it says something else\n");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(5));
        Check("editing reloads", Core.ContentHotReload.CheckNow() == 1 && reloads == 1);

        StoryLibrary.Current.TryGetBlock("examine.house.bed", out var bed);
        Check("the new text is in",
            (bed.Variants[0].Nodes[0] as DialogueLineNode)?.Text == "now it says something else",
            (bed.Variants[0].Nodes[0] as DialogueLineNode)?.Text);

        Check("unchanged means no reload (it does not reparse every tick)",
            Core.ContentHotReload.CheckNow() == 0 && reloads == 1);

        string file2 = Path.Combine(dir, "houseFront" + StoryLibrary.Extension);
        Write(file2, "# examine.houseFront\n\n@signpost\nhero: A signpost\n");
        Check("adding a file is caught too", Core.ContentHotReload.CheckNow() == 1
            && StoryLibrary.Current.BlockCount == 2);

        File.Delete(file2);
        Check("deleting a file is caught too (timestamps alone would miss it)",
            Core.ContentHotReload.CheckNow() == 1 && StoryLibrary.Current.BlockCount == 1);

        Core.ContentHotReload.Clear();
#endif
    }

    private static void TestInlineSay()
    {
        DialogueRunner.Bind(new Core.Blackboard());
        DialogueRunner.Stop();
        UI.DialogueBox.Close();

        Check("inline playback starts",
            DialogueRunner.PlayInline("world.oldMan", "oldMan", "Ha... it is tough."));
        Check("* the body is that sentence", UI.DialogueBox.CurrentText == "Ha... it is tough.",
            UI.DialogueBox.CurrentText);
        Check("* the speaker plate is that actor", UI.DialogueBox.CurrentSpeaker == "oldMan",
            UI.DialogueBox.CurrentSpeaker ?? "(null)");
        Check("block Id = cutscene Id (the key namespace comes from here)",
            DialogueRunner.CurrentBlockId == "world.oldMan");

        UI.DialogueBox.Close();
        DialogueRunner.Update(1f / 60f);
        Check("* playback ends after one utterance (Say waits on this)",
            !DialogueRunner.Active && !UI.DialogueBox.Active);

        Check("* inline leaves no visit count",
            DialogueRunner.VisitCount("world.oldMan") == 0);

        UI.DialogueBox.Close();
        Check("an empty line is rejected", !DialogueRunner.PlayInline("world.oldMan", "oldMan", ""));
        Check("rejected means no box either", !UI.DialogueBox.Active);

        DialogueRunner.Stop(); UI.DialogueBox.Close();
        DialogueRunner.PlayInline("world.oldMan", "hero", "Sorry. {.} The bus...");
        Check("* timing marks are stripped on the way up (the same handling as .story)",
            UI.DialogueBox.CurrentText == "Sorry. The bus...", UI.DialogueBox.CurrentText);

        string locDir = Path.Combine(Path.GetTempPath(), "pixelcore_inline_loc");
        Directory.CreateDirectory(locDir);
        File.WriteAllText(Path.Combine(locDir, "en.csv"),
            "Key,Speaker,Original,Translated,Notes\n" +
            "world.oldMan.7,oldMan,\"Ha... it is tough.\",\"Heh... it is hard.\",\n");
        Text.Loc.Load(locDir);
        Text.Loc.SetLanguage("en");

        DialogueRunner.Stop(); UI.DialogueBox.Close();
        DialogueRunner.PlayInline("world.oldMan", "oldMan", "Ha... it is tough.", 7);
        Check("* a stamped line switches to the translation (key = cutsceneId.number)",
            UI.DialogueBox.CurrentText == "Heh... it is hard.", UI.DialogueBox.CurrentText);

        DialogueRunner.Stop(); UI.DialogueBox.Close();
        DialogueRunner.PlayInline("world.oldMan", "oldMan", "Ha... it is tough.");
        Check("* unstamped (number 0) has no key, so the source text stands - the normal path before anything is translated",
            UI.DialogueBox.CurrentText == "Ha... it is tough.", UI.DialogueBox.CurrentText);

        Text.Loc.SetLanguage(Text.Loc.SourceLanguage);
        try { Directory.Delete(locDir, true); } catch { }
        DialogueRunner.Stop();
        UI.DialogueBox.Close();
    }

    private static void TestRealContent()
    {
        Console.WriteLine("--- real Content/Story ---");

        Check($"{StoryLibrary.DefaultRoot} exists (the premise of linting real scripts)",
            Directory.Exists(StoryLibrary.DefaultRoot));
        if (!Directory.Exists(StoryLibrary.DefaultRoot)) return;

        var lib = StoryLibrary.Load();
        Check($"no errors ({lib.Scripts.Count} files, {lib.BlockCount} blocks)",
            lib.Errors.Count == 0, string.Join(" / ", lib.Errors));

        const string sceneDir = "Content/Scenes";
        Check($"{sceneDir} exists (the premise of the scene-to-block reference check)", Directory.Exists(sceneDir));
        if (!Directory.Exists(sceneDir)) return;

        var dangling = new List<string>();
        int wired = 0;
        var files = Directory.GetFiles(sceneDir, "*.scene", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);

        foreach (var path in files)
        {
            var data = Serialization.SceneSerializer.LoadFromFile(path);
            if (data == null) continue;
            foreach (var e in data.Entities)
                foreach (var c in e.Components)
                {
                    if (c is not Serialization.InteractableData it) continue;
                    if (string.IsNullOrEmpty(it.Block)) continue;
                    wired++;
                    if (!lib.TryGetBlock(it.Block, out _))
                        dangling.Add($"{Path.GetFileName(path)} '{e.Name}' → {it.Block}");
                }
        }

        Check($"every dialogue block a scene points at exists ({wired} sites)",
            dangling.Count == 0, string.Join(" / ", dangling));
    }

    private static void Write(string path, string content) =>
        File.WriteAllText(path, content, new UTF8Encoding(false));

    private static string CaptureStdout(Action body)
    {
        var prev = Console.Out;
        var sw = new StringWriter();
        Console.SetOut(sw);
        try { body(); }
        finally { Console.SetOut(prev); }
        return sw.ToString();
    }

    private static void Check(string label, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"  ✔ {label}"); }
        else { _fail++; Console.WriteLine($"  ✘ {label}" + (detail != null ? $"  ← {detail}" : "")); }
    }
}

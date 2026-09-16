#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PixelCore.Runtime.Core;

public static class GlobalStateSelfTest
{
    private static readonly string[] ScanRoots = { "Runtime", "Gameplay" };
    private static readonly string[] ToolSuffixes = { "SelfTest.cs", "Regression.cs", "Audit.cs", "Harness.cs" };

    private static int _pass, _fail;

    public static void Run()
    {
        Console.WriteLine("=== Global mutable state baseline self-test ===");
        _pass = _fail = 0;

        foreach (var root in ScanRoots)
            Check($"premise: {root}/ is actually scanned (otherwise everything below is vacuous)", Directory.Exists(root));

        var found = Scan();
        Check($"premise: the scan found globals ({found.Count}; zero would mean the scanner is dead)",
              found.Count > 0);

        CheckOwner("class Foo", "public sealed class Foo : Bar", "Foo");
        CheckOwner("struct Foo", "public readonly struct Foo", "Foo");
        CheckOwner("interface IFoo", "public interface IFoo", "IFoo");
        CheckOwner("class Foo (has a body, so it owns)", "public partial class Foo", "Foo");
        CheckOwner("★ record struct Foo(...) (real code was bitten by this shape)",
            "public readonly record struct Foo(int A, int B);", null);
        CheckOwner("★ record class Foo(...)", "public record class Foo(int A);", null);
        CheckOwner("★ record Foo(...)", "public record Foo(int A);", null);
        CheckOwner("positional parameters continuing over several lines, likewise",
            "public readonly record struct Foo(string A, string B,", null);

        CheckSymbols("★ a tuple-array field is visible (real code was bitten by this shape)",
            "private static readonly (string Key, string Where)[] MovedKeys =", "MovedKeys");
        CheckSymbols("★ a single tuple field too", "static (int A, int B) _pair;", "_pair");
        CheckSymbols("   control: a method is still a method (parenthesis attached to the name)",
            "public static Vector2 GrainCellsFor(Rectangle dest, float cells)", null);
        CheckSymbols("   control: a generic dictionary is one name",
            "private static readonly Dictionary<string, int> _byId = new();", "_byId");

        Check("★ real code: FootstepEmitter._rng's owner is not swallowed by a record struct",
            found.Contains("FootstepEmitter._rng"));
        Check("★ real code: PostData.MovedKeys (a tuple array) is visible",
            found.Contains("PostData.MovedKeys"));

        var baseline = new HashSet<string>(Baseline, StringComparer.Ordinal);
        var seen = new HashSet<string>(found, StringComparer.Ordinal);
        var added = found.Where(f => !baseline.Contains(f)).Distinct(StringComparer.Ordinal).ToList();

        Check(BuildMessage(added, found.Count), added.Count == 0);

        var multi = FindMultiDeclarators();
        Check("★ no line declares more than one static (the scanner cannot see the later ones)"
              + (multi.Count == 0 ? "" : "\n         " + string.Join("\n         ", multi)),
            multi.Count == 0);

        var missing = baseline.Where(b => !seen.Contains(b)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var msg = new StringBuilder($"★ every baseline name exists in the source ({Baseline.Length} entries)");
        if (missing.Count > 0)
        {
            msg.Append($"\n         {missing.Count} name(s) not found in the source:");
            foreach (var m in missing) msg.Append("\n           · " + m);
            msg.Append("\n         1. If it really was deleted, delete the baseline line (no judgement needed; shrinking is normal).");
            msg.Append("\n         2. If nothing was deleted, the scanner is misreading a name. Fix that instead.");
        }
        Check(msg.ToString(), missing.Count == 0);

        Console.WriteLine($"=== GlobalState: {_pass} passed, {_fail} failed ===");
    }

    private static string BuildMessage(List<string> added, int total)
    {
        var sb = new StringBuilder();
        sb.Append($"★ global mutable state has not grown (measured {total}, baseline {Baseline.Length})");
        if (added.Count == 0) return sb.ToString();

        sb.Append($"\n         {added.Count} new global(s):");
        foreach (var a in added) sb.Append("\n           · " + a);
        sb.Append("\n");
        sb.Append("\n         1. Is this state that must not survive a day boundary? (Changes during play and would be harmful carried into a new game or a continue.)");
        sb.Append("\n            If so, add a rewind to Gameplay/Systems/GameFlow.ResetRuntimeStatics.");
        sb.Append("\n              For \"loading is a new game plus a delta\" to hold, that new game has to be genuinely new.");
        sb.Append("\n         2. If not, classify it: tuning, configuration, resource, wiring, or tooling.");
        sb.Append("\n            See the global static audit for what the classifications mean and how to decide on rewinding.");
        sb.Append("\n         3. Only then add a line to GlobalStateSelfTest.Baseline.");
        sb.Append("\n            In the other order, this check becomes a rubber stamp.");
        return sb.ToString();
    }

    private static void CheckOwner(string name, string line, string? expected)
    {
        var m = TypeDecl.Match(line);
        bool positional = m.Success && m.Groups[2].Value == "(";
        var got = !m.Success ? "(no match)" : positional ? null : m.Groups[1].Value;
        Check($"owner name: {name} → {expected ?? "(unchanged)"}"
                + (got == expected ? "" : $"   [read {got ?? "(unchanged)"}]"),
            got == expected);
    }

    private static List<string> FindMultiDeclarators()
    {
        var hits = new List<string>();
        var re = new Regex(@"^\s*(?:private|public|internal|protected)[^=;()]*\bstatic\b[^=;()]*\s\w+\s*=[^;]*,\s*\w+\s*;");
        foreach (var root in ScanRoots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(file);
                if (ToolSuffixes.Any(sfx => name.EndsWith(sfx, StringComparison.Ordinal))) continue;
                bool inBlock = false;
                int n = 0;
                foreach (var raw in File.ReadAllLines(file))
                {
                    n++;
                    var t = SourceScan.StripComments(raw, ref inBlock);
                    if (re.IsMatch(t)) hits.Add($"{file}:{n}");
                }
            }
        }
        return hits;
    }

    private static readonly Regex TypeDecl = new(
        @"\b(?:(?:class|struct|interface)|record(?:\s+(?:class|struct))?)\s+(\w+)\s*(\(?)",
        RegexOptions.Compiled);

    public static List<string> Scan()
    {
        var found = new List<string>();
        foreach (var root in ScanRoots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                                          .OrderBy(f => f, StringComparer.Ordinal))
            {
                var name = Path.GetFileName(file);
                if (ToolSuffixes.Any(s => name.EndsWith(s, StringComparison.Ordinal))) continue;

                string owner = Path.GetFileNameWithoutExtension(file);
                bool inBlockComment = false;
                foreach (var raw in File.ReadAllLines(file))
                {
                    var t = SourceScan.StripComments(raw, ref inBlockComment).Trim();
                    if (t.Length == 0) continue;

                    var td = TypeDecl.Match(t);
                    if (td.Success && Regex.IsMatch(t, @"^(public|internal|private|protected|abstract|sealed|static|partial)\b"))
                    {
                        if (td.Groups[2].Value != "(") owner = td.Groups[1].Value;
                        continue;
                    }

                    foreach (var sym in SymbolsIn(t)) found.Add(owner + "." + sym);
                }
            }
        }
        found.Sort(StringComparer.Ordinal);
        return found;
    }

    private static IEnumerable<string> SymbolsIn(string t)
    {
        if (!Regex.IsMatch(t, @"\bstatic\b")) yield break;
        if (Regex.IsMatch(t, @"\bconst\b")) yield break;
        if (TypeDecl.IsMatch(t)) yield break;
        if (t.Contains("=>")) yield break;
        if (t.StartsWith("//") || t.StartsWith("///") || t.StartsWith("*")) yield break;

        int eq = t.IndexOf('=');
        var decl = (eq >= 0 ? t.Substring(0, eq) : t).Trim();
        if (LooksLikeMethod(decl)) yield break;
        decl = decl.TrimEnd(';', '{').Trim();
        if (decl.EndsWith("]")) yield break;

        int brace = decl.IndexOf('{');
        if (brace >= 0) decl = decl.Substring(0, brace).Trim();
        var masked = MaskGenerics(decl);

        var parts = masked.Split(',');
        var firstTokens = parts[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (firstTokens.Length >= 2)
        {
            var n = firstTokens[firstTokens.Length - 1];
            if (IsIdentifier(n)) yield return n;
        }
        for (int i = 1; i < parts.Length; i++)
        {
            var n = parts[i].Trim();
            if (IsIdentifier(n)) yield return n;
        }
    }

    private static void CheckSymbols(string name, string line, string? expected)
    {
        var got = SymbolsIn(line.Trim()).ToList();
        bool ok = expected == null
            ? got.Count == 0
            : got.Count == 1 && got[0] == expected;
        Check($"{name} → {(got.Count == 0 ? "(none)" : string.Join(",", got))}"
              + (ok ? "" : $"   [expected {expected ?? "(none)"}]"), ok);
    }

    private static bool LooksLikeMethod(string decl)
    {
        int angle = 0;
        for (int i = 0; i < decl.Length; i++)
        {
            char c = decl[i];
            if (c == '<') angle++;
            else if (c == '>') angle = Math.Max(0, angle - 1);
            else if (c == '(' && angle == 0)
            {
                char prev = i > 0 ? decl[i - 1] : ' ';
                if (char.IsLetterOrDigit(prev) || prev == '_' || prev == '>' || prev == ']')
                    return true;
            }
        }
        return false;
    }

    private static string MaskGenerics(string s)
    {
        var sb = new StringBuilder(s.Length);
        int depth = 0;
        foreach (var c in s)
        {
            if (c == '<' || c == '(') depth++;
            else if (c == '>' || c == ')') depth = Math.Max(0, depth - 1);
            sb.Append(depth > 0 && c == ',' ? '|' : c);
        }
        return sb.ToString();
    }

    private static bool IsIdentifier(string s) =>
        s.Length > 0 && (char.IsLetter(s[0]) || s[0] == '_') && s.All(c => char.IsLetterOrDigit(c) || c == '_');

    private static void Check(string what, bool ok)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what);
        if (ok) _pass++; else _fail++;
    }

    public static readonly string[] Baseline =
    {
        "AnimClipCache.ContentRoot",
        "AnimClipCache._byId",
        "AnimClipCache._byName",
        "AnimClipCache._warned",
        "AnimationData._nameNoted",
        "AssetEvents.Changed",
        "AssetRegistry._instance",
        "AssetScanner.ForeignExtensions",
        "AudioFileInfo._cache",
        "AudioManager._instance",
        "Camera.StateRoundingInDeadzone",
        "CinematicBars.Amount",
        "CrashHandler.LastLogPath",
        "CrashHandler._installed",
        "CrashHandler._tail",
        "CodeHotReload.Applied",
        "CodeHotReload.AppliedCount",
        "CodeHotReload._pending",
        "ComponentDataRegistry._capturers",
        "ComponentDataRegistry._unregisteredWarned",
        "ComponentTypes._factories",
        "ContentHotReload.Interval",
        "ContentHotReload.Reloaded",
        "CutsceneDirector.FacingHookWarned",
        "CutsceneDirector.Finished",
        "CutsceneDirector.PersistFacing",
        "CutsceneDirector.RoomLoader",
        "CutsceneDirector.SkipHoldProgress",
        "CutsceneDirector.Skipping",
        "CutsceneDirector.SuppressTriggers",
        "CutsceneDirector._cast",
        "CutsceneDirector._ctx",
        "CutsceneDirector._current",
        "CutsceneDirector._declaredRooms",
        "CutsceneDirector._dampingScopes",
        "CutsceneDirector._handle",
        "CutsceneDirector._muteBeforeSkip",
        "CutsceneDirector._order",
        "CutsceneDirector._owningCamera",
        "CutsceneDirector._registry",
        "CutsceneDirector._scaleBeforeSkip",
        "CutsceneDirector._skipHeld",
        "CutsceneLog.ArgCount",
        "CutsceneLog.HaltCount",
        "CutsceneLog.LastMessage",
        "CutsceneLog.SkipCount",
        "DialogueBox.Active",
        "DialogueBox.AutoAdvance",
        "DialogueBox.BorderColor",
        "DialogueBox.BoxMarginBottom",
        "DialogueBox.BoxPadding",
        "DialogueBox._closedSpeakerId",
        "DialogueBox._bubble",
        "DialogueBox.FastForward",
        "DialogueBox.FillColor",
        "DialogueBox.JustClosed",
        "DialogueBox.MaxLinesPerPage",
        "DialogueBox.SpeakerColor",
        "DialogueBox.TextColor",
        "DialogueBox.VirtualHeight",
        "DialogueBox.VirtualWidth",
        "DialogueBox._blinkTimer",
        "DialogueBox._choiceIndex",
        "DialogueBox._choices",
        "DialogueBox._chosen",
        "DialogueBox._charClock",
        "DialogueBox._doneTime",
        "DialogueBox._pageIndex",
        "DialogueBox._hurry",
        "DialogueBox._uiScale",
        "DialogueBox._revealed",
        "DialogueBox._speaker",
        "DialogueBox._suppressInputOnce",
        "DialogueEvents._handlers",
        "DialogueMarkup._warnedTags",
        "DialogueRunner.Finished",
        "DialogueRunner._blackboard",
        "DialogueRunner._block",
        "DialogueRunner._flow",
        "DialogueRunner._pendingChoice",
        "DialogueRunner._stack",
        "BubblePool._clock",
        "BubblePool._inUse",
        "BubblePool._owner",
        "BubblePool._slots",
        "DialogueBubble.BodyDelaySeconds",
        "DialogueBubble.FillColor",
        "DialogueBubble.FontPx",
        "DialogueBubble.LineHeight",
        "DialogueBubble.MaxWidthRatio",
        "DialogueBubble.MinWidth",
        "DialogueBubble.NameColor",
        "DialogueBubble.NameDelaySeconds",
        "DialogueBubble.NameGap",
        "DialogueBubble.OpenStepSeconds",
        "DialogueBubble.PadBottom",
        "DialogueBubble.PadLeft",
        "DialogueBubble.PadRight",
        "DialogueBubble.PadTop",
        "DialogueBubble.ShadowColor",
        "DialogueBubble.ShakeAmplitude",
        "DialogueBubble.ShakeSeconds",
        "DialogueBubble.ShakeStepSeconds",
        "DialogueBubble.TextColor",
        "DialogueBubble._skinWarned",
        "DialogueBubble._warnedSpeakers",
        "DialogueSpan.Default",
        "BubbleLayout.Empty",
        "DialogueText.Empty",
        "DialogueTiming.CjkSeconds",
        "DialogueTiming.LatinSeconds",
        "DialogueTiming.PunctuationSeconds",
        "DialogueTiming.TailSeconds",
        "Entity._nextId",
        "Entry._entries",
        "Entry._timer",
        "FlagRegistry._lifetimes",
        "FrameOutput.SpriteBatch",
        "FreezeState.Changed",
        "FreezeState._counts",
        "FreezeState._soft",
        "GameActions._actions",
        "GameActions._order",
        "GameFlow.StartRooms",
        "GameFlow.Costume",
        "GameFlow.Day",
        "GameFlow.DayStarted",
        "GameFlow.Phase",
        "GameFlow.PhaseHours",
        "GameFlow._camera",
        "GameFlow._ctx",
        "PauseMenu.FullscreenRequested",
        "PauseMenu.ReturnToTitleRequested",
        "PauseScreen.Active",
        "PauseScreen.CaretGap",
        "PauseScreen.DimColor",
        "PauseScreen.DisabledColor",
        "PauseScreen.HintBottom",
        "PauseScreen.HintColor",
        "PauseScreen.ItemColor",
        "PauseScreen.MenuLineHeight",
        "PauseScreen.MenuCenterY",
        "PauseScreen.PanelWidth",
        "PauseScreen.SelectedColor",
        "PauseScreen.TitleColor",
        "PauseScreen.TitleTop",
        "PauseScreen.ValueColor",
        "PauseScreen._stack",
        "PauseScreen._warnedFont",
        "GameSettings._current",
        "GameSettings._dirty",
        "Gamepad.DebugInjected",
        "Gamepad.IsConnected",
        "Gamepad._curr",
        "Gamepad._prev",
        "Rumble.DeviceTouched",
        "Rumble.Enabled",
        "Rumble.LastApplied",
        "Rumble.SuppressDevice",
        "Rumble._applied",
        "Rumble._enabled",
        "Rumble._remaining",
        "Rumble._strength",
        "Rumble._total",
        "GameSystems._dialogueTestShown",
        "GameSystems._player",
        "Input._currState",
        "Input._keyboardState",
        "Input._numKeys",
        "Input._prevState",
        "InstanceOverrides._computing",
        "InstanceScope.AudioExtensions",
        "InstanceScope.PlayableAudioExtensions",
        "Interactable.DefaultHandler",
        "InteractionPrompt.Enabled",
        "InteractionPrompt.ForceNearest",
        "Interactor.GlobalLock",
        "InteractionHighlight.Mode",
        "InteractionHighlight.OutlineColor",
        "InteractionHighlight.TintColor",
        "InteractionHighlight._painted",
        "PlayerClips.Dirs",
        "GameFlow.PhaseTags",
        "GameFlow.PhaseWindows",
        "GameFlow.Weather",
        "GameFlow._missingRoomWarned",
        "LightData._warnedTextureRefs",
        "LightingBalance.ReadabilityFloor",
        "InputMap.Bindings",
        "TimeOfDay.AmbientStops",
        "FxPresets.Battle",
        "FxPresets.Nausea",
        "FxPresets.Drowsy",
        "FxPresets.Sinking",
        "FxPresets.CutIn",
        "FxPresets.MemoryCollapse",
        "PostData.MovedKeys",
        "PostData._movedNoted",
        "PostProcessor._lutWarned",
        "FxStack._nextId",
        "FxStack._pushed",
        "FxStack._weather",
        "LightingProfiles.ContentRoot",
        "LightingProfiles.Current",
        "LightingProfiles.Neutral",
        "LightingProfiles._blendDuration",
        "LightingProfiles._blendT",
        "LightingProfiles._from",
        "LightingProfiles._lastKey",
        "LightingProfiles._table",
        "LightingProfiles._to",
        "LightingProfiles._warnedTags",
        "LightingRenderer.Multiply",
        "LightingRenderer.AddKeepAlpha",
        "LightingRenderer.Subtract",
        "LightingRenderer.MultiplyOnly",
        "LightingRenderer.MultiplyThenAdditive",
        "LightingRenderer.NoPasses",
        "LocStamp.BodyStart",
        "LocStamp.SayCall",
        "LocStamp.SayCallLoose",
        "Loc.Language",
        "Loc.Ready",
        "Loc.Root",
        "Loc._missing",
        "Loc._overlay",
        "Loc._seenMissing",
        "Loc._seenProblem",
        "Loc._table",
        "LocCsv.Header",
        "Nav.LastResult",
        "Nav._grid",
        "Nav._scene",
        "NavGrid.Footprint",
        "NavPathfinder.Neighbours",
        "NavPathfinder.DebugExpandedCount",
        "OggDecoder.OpenAttempts",
        "OggStream.ConstructedCount",
        "ParticleAsset.JsonOptions",
        "ParticleAsset.TypeInfo",
        "ParticleAsset._nameNoted",
        "ParticleEmitter._builtins",
        "ParticleEmitter._missingPresetWarned",
        "ParticleEmitter._missingTextureWarned",
        "ParticlePresetCache.ContentRoot",
        "ParticlePresetCache._byId",
        "PostAsset.JsonOptions",
        "PostAsset.TypeInfo",
        "PostAsset._nameNoted",
        "PostAssetCache.ContentRoot",
        "PostAssetCache._byId",
        "SkyAsset._nameNoted",
        "SkyAsset.JsonOptions",
        "SkyAsset.TypeInfo",
        "SkyPresetCache.ContentRoot",
        "SkyPresetCache._byId",
        "SkyNoise.GradX",
        "SkyNoise.GradY",
        "PostProcessor.Multiply",
        "Respawn._deadSeconds",
        "RoomFlow.LastSpawn",
        "RoomFlow.LoadRoomImpl",
        "RoomFlow.RoomChangedDuringPlay",
        "RoomFlow._phase",
        "RoomFlow._sceneId",
        "Rooms.Village",
        "Rooms.House",
        "SaveFile.Dir",
        "SaveFile.JsonOptions",
        "SaveFile.TypeInfo",
        "SaveFile._dirOverride",
        "SceneInstance.ContentRoot",
        "SceneInstance._loadingPaths",
        "SceneInstance._schemaWarned",
        "SceneSerializer.JsonOptions",
        "SceneSerializer.NodeTypeInfo",
        "SceneSerializer.SceneTypeInfo",
        "SceneSerializer._nameNoted",
        "ScreenFader.Alpha",
        "ScreenFader._speed",
        "ScreenFader._target",
        "ShadowRenderer.QuadIndices",
        "SpriteData._warnedRefs",
        "StoryLibrary.Current",
        "StoryParser.HeaderAttributeKeys",
        "StoryParser.KnownTags",
        "StoryParser.LineAttributeKeys",
        "SurfaceAsset._nameNoted",
        "SurfaceLibrary.ContentRoot",
        "SurfaceLibrary._byId",
        "TextService._renderer",
        "TextService._slotWarned",
        "TextService._slots",
        "GlyphMetrics._cached",
        "TextureLoader.Headless",
        "TextureLoader._instance",
        "TextureLoader._warnedUninitialized",

        "TitleScreen.Active",
        "TitleScreen._items",
        "TitleScreen._index",
        "TitleScreen._title",
        "TitleScreen._subtitle",
        "TitleScreen._warnedFont",
        "TitleScreen.TitleTop",
        "TitleScreen.MenuTop",
        "TitleScreen.MenuLineHeight",
        "TitleScreen.HintBottom",
        "TitleScreen.CaretGap",
        "TitleScreen.BackColor",
        "TitleScreen.TitleColor",
        "TitleScreen.SubtitleColor",
        "TitleScreen.ItemColor",
        "TitleScreen.SelectedColor",
        "TitleScreen.DisabledColor",
        "TitleScreen.HintColor",
        "TitleMenu.QuitRequested",

        "MonologueScreen.Active",
        "MonologueScreen.Backdrop",
        "MonologueScreen.Text",
        "MonologueScreen.TextAlpha",
        "MonologueScreen.AcceptInput",
        "MonologueScreen._advance",
        "MonologueScreen._prevConfirm",
        "MonologueScreen._prevMouse",
        "MonologueScreen._warnedFont",
        "MonologueScreen.BackColor",
        "MonologueScreen.TextColor",
        "MonologueScreen.CenterY",

        "UIDraw._pixel",
        "FootstepEmitter._rng",
        "Village.StoneTouched",
    };
}
#endif

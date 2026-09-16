using System;
using System.Collections.Generic;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Text;
using PixelCore.Runtime.UI;

namespace PixelCore.Runtime.Story;

public enum DialogueFlow
{
    Input,
    Auto,
}

public static class DialogueRunner
{
    public const string VisitKeyPrefix = "story.visit.";

    public const string EventAttribute = "event";

    private static Blackboard _blackboard = new();

    private static readonly List<(List<DialogueNode> Nodes, int Index)> _stack = new();
    private static DialogueBlock? _block;
    private static DialogueChoiceNode? _pendingChoice;
    private static DialogueFlow _flow = DialogueFlow.Input;

    public static string CurrentBlockId => _block?.Id ?? "";

    public static bool Active => _block != null;

    public static event Action<string>? Finished;

    public static void Bind(Blackboard blackboard) => _blackboard = blackboard;

    public static int VisitCount(string blockId) => _blackboard.GetInt(VisitKeyPrefix + blockId);

    public static bool Play(string blockId, DialogueFlow flow = DialogueFlow.Input)
    {
        if (string.IsNullOrEmpty(blockId)) return false;
        _flow = flow;

        if (!StoryLibrary.Current.TryGetBlock(blockId, out var block))
        {
            Console.WriteLine($"[Story] ⚠ no such dialogue block: '{blockId}'");
            return false;
        }

        int visit = VisitCount(blockId) + 1;
        _blackboard.SetInt(VisitKeyPrefix + blockId, visit);

        var variant = block.VariantFor(visit);
        _stack.Clear();
        _pendingChoice = null;
        _block = block;
        _stack.Add((variant.Nodes, 0));

        if (!Advance())
        {
            Console.WriteLine($"[Story] ⚠ '{blockId}' has no lines to play on visit {visit}");
            Stop();
            return false;
        }
        return true;
    }

    public static bool PlayInline(string cutsceneId, string speaker, string text,
                                  int number = 0, DialogueFlow flow = DialogueFlow.Auto)
    {
        if (string.IsNullOrEmpty(text))
        {
            Console.WriteLine($"[Story] ⚠ empty inline line - '{cutsceneId}' ({speaker})");
            return false;
        }

        var block = new DialogueBlock { Name = "", Id = cutsceneId };
        var variant = new DialogueVariant { FromVisit = 1 };
        variant.Nodes.Add(new DialogueLineNode { Speaker = speaker, Text = text, Number = number });
        block.Variants.Add(variant);

        _flow = flow;
        _stack.Clear();
        _pendingChoice = null;
        _block = block;
        _stack.Add((variant.Nodes, 0));

        if (!Advance()) { Stop(); return false; }
        return true;
    }

    public static void Update(float deltaTime)
    {
        if (_block == null) return;

        if (_pendingChoice != null)
        {
            if (!DialogueBox.TryTakeChoice(out int picked)) return;

            var options = _pendingChoice.Options;
            _pendingChoice = null;
            if (picked < 0 || picked >= options.Count) { Finish(); return; }

            _stack.Add((options[picked].Nodes, 0));
            if (!Advance()) Finish();
            return;
        }

        if (DialogueBox.Active) return;
        if (!Advance()) Finish();
    }

    public static void Stop()
    {
        _block = null;
        _stack.Clear();
        _pendingChoice = null;
        _flow = DialogueFlow.Input;
    }

    private static void Finish()
    {
        string finished = _block!.Id;
        Stop();
        Finished?.Invoke(finished);
    }

    private static bool Advance()
    {
        while (_stack.Count > 0)
        {
            var (nodes, index) = _stack[^1];
            if (index >= nodes.Count) { _stack.RemoveAt(_stack.Count - 1); continue; }

            _stack[^1] = (nodes, index + 1);

            switch (nodes[index])
            {
                case DialogueLineNode line:
                    ShowLine(line);
                    return true;

                case DialogueChoiceNode choice:
                    if (ShowChoice(choice)) return true;
                    continue;
            }
        }
        return false;
    }

    private static bool ShowChoice(DialogueChoiceNode choice)
    {
        if (choice.Options.Count == 0)
        {
            Console.WriteLine($"[Story] ⚠ '{_block!.Id}' has an empty choice group - skipped");
            return false;
        }

        Cutscenes.CutsceneDirector.CancelSkip();

        var labels = new string[choice.Options.Count];
        for (int i = 0; i < labels.Length; i++)
            labels[i] = OptionText(_block!, choice.Options[i]);

        _pendingChoice = choice;
        DialogueBox.ShowChoices(null, labels);
        return true;
    }

    private static void ShowLine(DialogueLineNode line)
    {
        DialogueBox.AutoAdvance = _flow == DialogueFlow.Auto;
        DialogueBox.ShowLine(line.Speaker, SpeakerLabel(line.Speaker), LineRaw(_block!, line));

        foreach (var (key, value) in line.Attributes)
            if (key == EventAttribute) DialogueEvents.Fire(value, _block!.Id);
    }

    public static string LineText(DialogueBlock block, DialogueLineNode line)
        => DialogueMarkup.Plain(LineRaw(block, line));

    public static string LineRaw(DialogueBlock block, DialogueLineNode line)
    {
        string key = line.Number > 0 ? block.Id + "." + line.Number : "";
        return Loc.Line(key, line.Text);
    }

    public static string SpeakerLabel(string speaker)
    {
        if (string.IsNullOrEmpty(speaker)) return "";
        string key = "name." + speaker;
        return Loc.Has(key) ? Loc.T(key) : speaker;
    }

    public static string OptionText(DialogueBlock block, DialogueOption option)
    {
        string key = option.Number > 0 ? block.Id + "." + option.Number : "";
        return DialogueMarkup.Plain(Loc.Line(key, option.Text));
    }
}

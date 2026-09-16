using System;
using System.Collections.Generic;

namespace PixelCore.Runtime.Story;

public sealed class DialogueScript
{
    public string Namespace = "";

    public string FileName = "";

    public string DefaultSpeaker = "";

    public readonly List<string> Warnings = new();

    public readonly List<DialogueBlock> Blocks = new();

    public bool TryGetBlock(string name, out DialogueBlock block)
    {
        foreach (var b in Blocks)
            if (string.Equals(b.Name, name, StringComparison.Ordinal)) { block = b; return true; }
        block = null!;
        return false;
    }
}

public sealed class DialogueBlock
{
    public string Name = "";
    public string Id = "";
    public int Line;

    public readonly List<DialogueVariant> Variants = new();

    public DialogueVariant VariantFor(int visit)
    {
        var picked = Variants[0];
        foreach (var v in Variants)
            if (v.FromVisit <= visit) picked = v;
        return picked;
    }
}

public sealed class DialogueVariant
{
    public int FromVisit = 1;
    public int Line;
    public readonly List<DialogueNode> Nodes = new();
}

public abstract class DialogueNode
{
    public int Line;
}

public sealed class DialogueLineNode : DialogueNode
{
    public string Speaker = "";

    public string Text = "";

    public int Number;

    public readonly List<(string Key, string Value)> Attributes = new();

    public bool TryGetAttribute(string key, out string value)
    {
        foreach (var (k, v) in Attributes)
            if (string.Equals(k, key, StringComparison.Ordinal)) { value = v; return true; }
        value = "";
        return false;
    }
}

public sealed class DialogueChoiceNode : DialogueNode
{
    public readonly List<DialogueOption> Options = new();
}

public sealed class DialogueOption
{
    public string Text = "";
    public int Line;
    public int Number;
    public readonly List<DialogueNode> Nodes = new();
}

using System;
using System.Collections.Generic;
using PixelCore.Runtime.Text;

namespace PixelCore.Runtime.Story;

public static class StoryParser
{
    public static readonly string[] KnownTags = { "shake", "speed" };

    public static readonly string[] LineAttributeKeys = { "offset", "light", "event" };

    public static readonly string[] HeaderAttributeKeys = { "speaker" };

    public static DialogueScript Parse(string text, string fileName, List<string> errors)
        => new Impl(text, fileName, errors).Run();

    private enum Kind { Blank, Header, Comment, Block, Variant, Choice, Line }

    private readonly struct Tok
    {
        public readonly Kind Kind;
        public readonly int Indent, Line;
        public readonly string Text;
        public Tok(Kind kind, int indent, int line, string text)
        { Kind = kind; Indent = indent; Line = line; Text = text; }
    }

    private sealed class Impl
    {
        private readonly List<Tok> _toks = new();
        private readonly List<string> _errors;
        private readonly string _file;
        private readonly DialogueScript _script = new();
        private int _i;

        public Impl(string text, string fileName, List<string> errors)
        {
            _errors = errors;
            _file = fileName;
            _script.FileName = fileName;
            Tokenize(text);
        }

        private void Tokenize(string text)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n');
            for (int n = 0; n < lines.Length; n++)
            {
                string raw = lines[n];
                int indent = 0;
                int p = 0;
                while (p < raw.Length && (raw[p] == ' ' || raw[p] == '\t'))
                { indent += raw[p] == '\t' ? 4 : 1; p++; }

                string body = raw[p..].TrimEnd();
                int line = n + 1;

                if (body.Length == 0) { _toks.Add(new Tok(Kind.Blank, indent, line, "")); continue; }

                Kind kind = body[0] switch
                {
                    '#' => Kind.Header,
                    '@' => Kind.Block,
                    '>' => Kind.Choice,
                    _ => body.StartsWith("--", StringComparison.Ordinal) ? Kind.Variant : Kind.Line,
                };
                _toks.Add(new Tok(kind, indent, line, body));
            }
        }

        public DialogueScript Run()
        {
            SkipBlanks();

            if (_i >= _toks.Count || _toks[_i].Kind != Kind.Header)
            {
                Error(_i < _toks.Count ? _toks[_i].Line : 1,
                    "no namespace declaration on the first line - one per file, in the form `# store.night` (never inferred from the path)");
                return _script;
            }

            var head = _toks[_i++];

            string ns = StripTrailing(head.Text[1..].Trim(), head.Line, out int headNumber, out var headAttrs);
            if (headNumber != 0) Error(head.Line, "the header takes no translation number");

            foreach (var (key, value) in headAttrs)
            {
                if (Array.IndexOf(HeaderAttributeKeys, key) < 0)
                {
                    Error(head.Line, $"unknown header attribute [{key}:...] - supported: {string.Join(" ", HeaderAttributeKeys)}");
                    continue;
                }
                if (value.Length == 0) Error(head.Line, "[speaker:...] is empty");
                else if (HasWhitespace(value)) Error(head.Line, $"whitespace in the speaker name - '{value}'");
                else _script.DefaultSpeaker = value;
            }

            if (ns.Length == 0) Error(head.Line, "the namespace is empty");
            else if (HasWhitespace(ns)) Error(head.Line, $"whitespace in the namespace - '{ns}'");
            else _script.Namespace = ns;

            DialogueBlock? block = null;
            DialogueVariant? variant = null;

            while (_i < _toks.Count)
            {
                var t = _toks[_i];
                switch (t.Kind)
                {
                    case Kind.Blank:
                        _i++;
                        break;

                    case Kind.Header:
                        _i++;
                        if (LooksLikeNamespace(t.Text[1..].Trim()))
                            Error(t.Line, $"one namespace declaration per file - '{t.Text}' was treated as a comment");
                        break;

                    case Kind.Block:
                        _i++;
                        block = StartBlock(t);
                        variant = block.Variants[0];
                        break;

                    case Kind.Variant:
                        _i++;
                        variant = StartVariant(t, block, variant);
                        break;

                    default:
                        block ??= StartRootBlock(t);
                        variant ??= block.Variants[0];
                        variant.Nodes.AddRange(ParseNodes(0));
                        break;
                }
            }

            Validate();
            return _script;
        }

        private DialogueBlock StartRootBlock(Tok t)
        {
            var b = new DialogueBlock { Name = "", Id = _script.Namespace, Line = t.Line };
            b.Variants.Add(new DialogueVariant { FromVisit = 1, Line = t.Line });
            _script.Blocks.Add(b);
            return b;
        }

        private DialogueBlock StartBlock(Tok t)
        {
            string name = t.Text[1..].Trim();
            if (name.Length == 0) Error(t.Line, "the block name is empty - use the form `@signpost`");
            else if (HasWhitespace(name)) Error(t.Line, $"whitespace in the block name - '{name}'");

            if (_script.TryGetBlock(name, out var prev))
                Error(t.Line, $"duplicate block '@{name}' - already at {_file}:{prev.Line}");

            var b = new DialogueBlock
            {
                Name = name,
                Id = name.Length == 0 ? _script.Namespace : _script.Namespace + "." + name,
                Line = t.Line,
            };
            b.Variants.Add(new DialogueVariant { FromVisit = 1, Line = t.Line });
            _script.Blocks.Add(b);
            return b;
        }

        private DialogueVariant? StartVariant(Tok t, DialogueBlock? block, DialogueVariant? current)
        {
            string spec = t.Text[2..].Trim();
            if (block == null)
            {
                Error(t.Line, $"no block or line precedes '{t.Text}'");
                return current;
            }
            if (current != null && current.Nodes.Count == 0)
            {
                Error(t.Line, $"the section before '{t.Text}' is empty - nothing would play on the first visit");
                return current;
            }

            int prevFrom = block.Variants[^1].FromVisit;
            int from;

            if (spec == "after") from = prevFrom + 1;
            else if (spec.StartsWith("visit ", StringComparison.Ordinal)
                     && int.TryParse(spec[6..].Trim(), out int n) && n >= 2) from = n;
            else
            {
                Error(t.Line, $"a visit divider must be `-- visit 2` or `-- after` - '{t.Text}'");
                return current;
            }

            if (from <= prevFrom)
            {
                Error(t.Line, $"the visit count goes backwards - the previous section starts at {prevFrom} but '{t.Text}'");
                return current;
            }

            var v = new DialogueVariant { FromVisit = from, Line = t.Line };
            block.Variants.Add(v);
            return v;
        }

        private List<DialogueNode> ParseNodes(int baseIndent)
        {
            var nodes = new List<DialogueNode>();
            while (_i < _toks.Count)
            {
                var t = _toks[_i];
                if (t.Kind is Kind.Block or Kind.Variant or Kind.Header) break;
                if (t.Kind == Kind.Blank) { _i++; continue; }
                if (t.Indent < baseIndent) break;

                if (t.Kind == Kind.Choice) { nodes.Add(ParseChoiceGroup(t.Indent)); continue; }

                _i++;
                var line = ParseLine(t);
                if (line != null) nodes.Add(line);
            }
            return nodes;
        }

        private DialogueChoiceNode ParseChoiceGroup(int indent)
        {
            var node = new DialogueChoiceNode { Line = _toks[_i].Line };
            while (_i < _toks.Count && _toks[_i].Kind == Kind.Choice && _toks[_i].Indent == indent)
            {
                var t = _toks[_i++];
                string body = StripTrailing(t.Text[1..].Trim(), t.Line, out int number, out var attrs);
                if (body.Length == 0) Error(t.Line, "the choice text is empty");
                LintMarkup(body, t.Line);

                var opt = new DialogueOption { Text = body, Line = t.Line, Number = number };
                if (attrs.Count > 0)
                    Error(t.Line, "a choice takes no line attributes (it is a button, not a bubble)");

                opt.Nodes.AddRange(ParseNodes(indent + 1));
                node.Options.Add(opt);
            }
            return node;
        }

        private DialogueLineNode? ParseLine(Tok t)
        {
            string speaker, rest;

            if (_script.DefaultSpeaker.Length > 0)
            {
                speaker = _script.DefaultSpeaker;
                rest = t.Text;
                WarnIfLooksLikeSpeaker(rest, t.Line);
            }
            else
            {
                int colon = t.Text.IndexOf(':');
                if (colon <= 0)
                {
                    Error(t.Line, $"no speaker - the form is `speaker: line` (narration is `narration:`)  <- {Clip(t.Text)}");
                    return null;
                }
                speaker = t.Text[..colon].Trim();
                rest = t.Text[(colon + 1)..].Trim();
                if (speaker.Length == 0) { Error(t.Line, "the speaker is empty"); return null; }
            }

            string text = StripTrailing(rest, t.Line, out int number, out var attrs);
            CheckAttributeKeys(attrs, t.Line);
            if (text.Length == 0) Error(t.Line, $"'{speaker}' has an empty line");
            LintMarkup(text, t.Line);

            var node = new DialogueLineNode { Speaker = speaker, Text = text, Number = number, Line = t.Line };
            node.Attributes.AddRange(attrs);
            return node;
        }

        private string StripTrailing(string s, int line, out int number,
            out List<(string Key, string Value)> attrs)
        {
            number = 0;
            attrs = new List<(string, string)>();
            s = s.TrimEnd();

            while (s.Length > 0)
            {
                if (s[^1] >= '0' && s[^1] <= '9')
                {
                    int d = s.Length - 1;
                    while (d >= 0 && s[d] >= '0' && s[d] <= '9') d--;
                    if (d >= 0 && s[d] == '#' && (d == 0 || char.IsWhiteSpace(s[d - 1])))
                    {
                        if (number != 0) Error(line, "more than one translation number");
                        number = int.Parse(s[(d + 1)..]);
                        s = s[..d].TrimEnd();
                        continue;
                    }
                }

                if (s[^1] == ']')
                {
                    int open = s.LastIndexOf('[');
                    if (open < 0) break;
                    string inner = s[(open + 1)..^1];
                    int c = inner.IndexOf(':');
                    if (c <= 0) break;
                    string key = inner[..c].Trim();
                    if (key.Length == 0 || HasWhitespace(key)) break;
                    attrs.Insert(0, (key, inner[(c + 1)..].Trim()));
                    s = s[..open].TrimEnd();
                    continue;
                }

                break;
            }
            return s;
        }

        private void LintMarkup(string text, int line)
        {
            var problems = new List<string>();
            TextFormat.Lint(text, problems);
            foreach (var p in problems) Error(line, p);

            var stack = new List<string>();
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '<') continue;
                int end = text.IndexOf('>', i + 1);
                if (end < 0) { Error(line, "unclosed '<'"); return; }

                string inner = text[(i + 1)..end].Trim();
                i = end;
                if (inner.Length == 0) { Error(line, "empty tag <>"); continue; }

                bool closing = inner[0] == '/';
                if (closing) inner = inner[1..].Trim();

                int sp = inner.IndexOf(' ');
                string name = sp > 0 ? inner[..sp] : inner;

                if (Array.IndexOf(KnownTags, name) < 0)
                {
                    Error(line, $"unknown tag <{name}> - supported: {string.Join(" ", KnownTags)}");
                    continue;
                }

                if (!closing) stack.Add(name);
                else if (stack.Count == 0 || stack[^1] != name)
                    Error(line, $"</{name}> does not match");
                else stack.RemoveAt(stack.Count - 1);
            }
            foreach (var open in stack) Error(line, $"<{open}> is not closed");
        }

        private void Validate()
        {
            foreach (var b in _script.Blocks)
            {
                var numbers = new Dictionary<int, int>();

                foreach (var v in b.Variants)
                {
                    if (v.Nodes.Count == 0)
                        Error(v.Line, b.Variants.Count == 1
                            ? $"empty block '@{b.Name}'"
                            : $"the section from visit {v.FromVisit} is empty");
                    CheckNumbers(v.Nodes, numbers);
                }
            }
        }

        private void CheckNumbers(List<DialogueNode> nodes, Dictionary<int, int> numbers)
        {
            foreach (var n in nodes)
            {
                switch (n)
                {
                    case DialogueLineNode ln:
                        Note(ln.Number, ln.Line, numbers);
                        break;
                    case DialogueChoiceNode ch:
                        foreach (var o in ch.Options)
                        {
                            Note(o.Number, o.Line, numbers);
                            CheckNumbers(o.Nodes, numbers);
                        }
                        break;
                }
            }
        }

        private void Note(int number, int line, Dictionary<int, int> numbers)
        {
            if (number == 0) return;
            if (numbers.TryGetValue(number, out int prev))
                Error(line, $"duplicate translation number #{number} - also at {_file}:{prev} (numbers are never reused)");
            else numbers[number] = line;
        }

        private void CheckAttributeKeys(List<(string Key, string Value)> attrs, int line)
        {
            foreach (var (key, _) in attrs)
            {
                if (Array.IndexOf(LineAttributeKeys, key) >= 0) continue;
                Error(line, Array.IndexOf(HeaderAttributeKeys, key) >= 0
                    ? $"[{key}:...] is a header-only attribute"
                    : $"unknown line attribute [{key}:...] - supported: {string.Join(" ", LineAttributeKeys)}");
            }
        }

        private void WarnIfLooksLikeSpeaker(string text, int line)
        {
            int colon = text.IndexOf(':');
            if (colon <= 0 || colon > 12) return;
            string head = text[..colon];
            if (HasWhitespace(head)) return;
            foreach (char c in head) if (c is '.' or ',' or '!' or '?') return;

            Warn(line, $"'{head}:' looks like a speaker, but this file is single-speaker ({_script.DefaultSpeaker}) "
                + "- it becomes body text (to mix speakers, remove [speaker:...] from the header)");
        }

        private void SkipBlanks() { while (_i < _toks.Count && _toks[_i].Kind == Kind.Blank) _i++; }
        private void Error(int line, string message) => _errors.Add($"{_file}:{line} {message}");
        private void Warn(int line, string message) => _script.Warnings.Add($"{_file}:{line} {message}");

        private static bool LooksLikeNamespace(string s) =>
            s.Length > 0 && !HasWhitespace(s) && s.Contains('.');

        private static bool HasWhitespace(string s)
        {
            foreach (char c in s) if (char.IsWhiteSpace(c)) return true;
            return false;
        }

        private static string Clip(string s) => s.Length <= 40 ? s : s[..40] + "…";
    }
}

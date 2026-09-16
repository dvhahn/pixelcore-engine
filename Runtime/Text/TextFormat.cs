using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PixelCore.Runtime.Text;

public static class TextFormat
{
    public const char OpenMark = '⟨';
    public const char CloseMark = '⟩';

    public static string Apply(string? template, (string name, object? value)[]? args,
        Action<string>? onProblem = null)
    {
        if (string.IsNullOrEmpty(template)) return template ?? "";
        if (template.IndexOf('{') < 0) return template;

        var sb = new StringBuilder(template.Length + 16);
        int i = 0;
        while (i < template.Length)
        {
            char c = template[i];

            if (c == '{')
            {
                if (i + 1 < template.Length && template[i + 1] == '{') { sb.Append('{'); i += 2; continue; }

                int end = template.IndexOf('}', i + 1);
                if (end < 0)
                {
                    onProblem?.Invoke($"unclosed '{{': ...{template[i..]}");
                    sb.Append(template[i..]);
                    break;
                }

                string token = template[(i + 1)..end];
                AppendToken(sb, token, args, onProblem);
                i = end + 1;
                continue;
            }

            if (c == '}' && i + 1 < template.Length && template[i + 1] == '}') { sb.Append('}'); i += 2; continue; }

            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    private static void AppendToken(StringBuilder sb, string token,
        (string name, object? value)[]? args, Action<string>? onProblem)
    {
        if (IsTimingToken(token)) { sb.Append('{').Append(token).Append('}'); return; }

        int colon = token.IndexOf(':');
        string name = colon >= 0 ? token[..colon] : token;
        string? fmt = colon >= 0 ? token[(colon + 1)..] : null;

        if (args != null)
        {
            foreach (var (argName, value) in args)
            {
                if (!string.Equals(argName, name, StringComparison.Ordinal)) continue;
                sb.Append(Render(value, fmt));
                return;
            }
        }

        onProblem?.Invoke($"no value supplied for argument {{{name}}}");
        sb.Append(OpenMark).Append('?').Append(name).Append(CloseMark);
    }

    private static string Render(object? value, string? fmt) => value switch
    {
        null => "",
        string s => s,
        IFormattable f => f.ToString(fmt, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    public static bool IsTimingToken(string token)
    {
        if (token.Length == 0) return false;
        foreach (char c in token)
            if (c != '.' && c != '=') return false;
        return true;
    }

    public static void Lint(string? template, List<string> problems)
    {
        if (string.IsNullOrEmpty(template)) return;

        int i = 0;
        while (i < template.Length)
        {
            char c = template[i];
            if (c != '{') { i++; continue; }
            if (i + 1 < template.Length && template[i + 1] == '{') { i += 2; continue; }

            int end = template.IndexOf('}', i + 1);
            if (end < 0) { problems.Add($"unclosed '{{': ...{template[i..]}"); return; }

            string token = template[(i + 1)..end];
            if (token.Length == 0) problems.Add("empty token {}");
            i = end + 1;
        }
    }
}

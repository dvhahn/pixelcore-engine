using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PixelCore.Runtime.Core;

namespace PixelCore.Editor;

public static class InspectorLayoutSelfTest
{
    private static int _pass, _fail;

    private const string ScanRoot = "Editor";

    public static void Run()
    {
        _pass = _fail = 0;
        Console.WriteLine("=== InspectorLayout (FieldLabel after SameLine) self-test ===");

        var (files, labelCalls, violations) = ScanDir(ScanRoot);
        Check($"premise: scanned the {ScanRoot}/ source ({files} files)", files >= 10);
        Check($"premise: actually saw FieldLabel calls ({labelCalls} sites)", labelCalls >= 50);

        Check("★ 0 places where FieldLabel follows SameLine" + Detail(violations), violations.Count == 0);

        Check("★ control: catches FieldLabel on the next line", Hits(@"
            ImGui.SameLine();
            EditorWidgets.FieldLabel(""Flip Y"");
        ") == 1);
        Check("★ control: catches it written on the same line too", Hits(@"
            ImGui.SameLine(); EditorWidgets.FieldLabel(""Flip Y"");
        ") == 1);
        Check("control: catches a SameLine with an offset too (SameLine(0,6) also stays on the same row)", Hits(@"
            ImGui.SameLine(0, 6);
            EditorWidgets.FieldLabel(""Flip Y"");
        ") == 1);
        Check("control: catches it with a blank line in between (a blank line does not end an ImGui row)", Hits(@"
            ImGui.SameLine();

            EditorWidgets.FieldLabel(""Flip Y"");
        ") == 1);
        Check("★ control: catches it with an ordinary C# line in between (the shape of the real bug)", Hits(@"
            ImGui.SameLine();

            var flipY = sr.FlipY;
            EditorWidgets.FieldLabel(""Flip Y"");
        ") == 1);
        Check("★ control: does not catch it with another widget in between (that widget closed the row)", Hits(@"
            ImGui.SameLine();
            ImGui.TextDisabled(""(native: 48×48)"");

            var draw = sr.DrawSize;
            EditorWidgets.FieldLabel(""Draw Size"");
        ") == 0);
        Check("★ control: catches it with PushID in between (Push does not create an item)", Hits(@"
            ImGui.SameLine();
            ImGui.PushID(""y"");
            EditorWidgets.FieldLabel(""Flip Y"");
        ") == 1);
        Check("★ control: catches it with BeginDisabled in between", Hits(@"
            ImGui.SameLine();
            ImGui.BeginDisabled(locked);
            EditorWidgets.FieldLabel(""Flip Y"");
        ") == 1);
        Check("control: catches it with SetNextItemWidth in between (the shape of the real SpriteEditorPanel code)", Hits(@"
            ImGui.SameLine(); ImGui.SetNextItemWidth(42);
            EditorWidgets.FieldLabel(""Flip Y"");
        ") == 1);
        Check("★ control: when a widget is submitted after SameLine on the same line, the next line is not caught", Hits(@"
            ImGui.SameLine(); ImGui.Text(""Gap"");
            EditorWidgets.FieldLabel(""Draw Size"");
        ") == 0);
        Check("★ control: catches it with SetKeyboardFocusHere in between (every Set* is non-submitting)", Hits(@"
            ImGui.SameLine();
            ImGui.SetKeyboardFocusHere();
            EditorWidgets.FieldLabel(""Flip Y"");
        ") == 1);
        Check("★ control: a Get inside the arguments does not make a submitting call read as non-submitting", Hits(@"
            ImGui.SameLine(); ImGui.Button(""x"", new Vector2(ImGui.GetContentRegionAvail().X, 0));
            EditorWidgets.FieldLabel(""Draw Size"");
        ") == 0);
        Check("★ control: a Get at the head of the statement reads as non-submitting and keeps the row armed", Hits(@"
            ImGui.SameLine();
            var h = ImGui.GetFrameHeight();
            EditorWidgets.FieldLabel(""Flip Y"");
        ") == 1);
        Check("★ control: the FieldLabel declaration line is not caught even when the code before it ends in SameLine", Hits(@"
            ImGui.SameLine();
            }

            public static void FieldLabel(string label)
            {
        ") == 0);
        Check("control: not caught across a method boundary (a row never spans methods)", Hits(@"
            ImGui.SameLine();
            }

            private void DrawNext(SpriteRenderer sr)
            {
            EditorWidgets.FieldLabel(""Color"");
        ") == 0);

        Check("★ control: a quote inside a line comment is not caught (the shape of the real CapsuleCollider comment)", Hits(@"
            // In the checkbox days, after SameLine
            // FieldLabel(with its internal SameLine(108f)) was called and they overlapped - the cause of that bug.
        ") == 0);
        Check("control: a quote inside a block comment is not caught (even across several lines)", Hits(@"
            /* ImGui.SameLine();
               EditorWidgets.FieldLabel(""X""); */
        ") == 0);
        Check("control: a quote inside a doc comment is not caught", Hits(@"
            /// <c>ImGui.SameLine()</c> followed by
            /// <c>FieldLabel(</c> makes them overlap.
        ") == 0);
        Check("★ control: code inside a multi-line verbatim string is not caught (this file's own fixtures have that shape)",
            Hits("var s = @\"\n    ImGui.SameLine();\n    EditorWidgets.FieldLabel(\"\"X\"\");\n\";") == 0);
        Check("★ control: code after a verbatim string closes is visible again (missing the close blinds the rest of the file)",
            Hits("var s = @\"\n  text\n\";\nImGui.SameLine();\nEditorWidgets.FieldLabel(\"Y\");") == 1);

        Check("control: // inside a string literal is not a comment (the code after it must stay alive)", Hits(@"
            var url = ""http://example.com"";  ImGui.SameLine();
            EditorWidgets.FieldLabel(""Y"");
        ") == 1);
        Check("control: a FieldLabel that starts its own line is not caught (the normal 90-odd sites)", Hits(@"
            EditorWidgets.FieldLabel(""Color"");
            ImGui.ColorEdit4(""##Color"", ref color);
            EditorWidgets.FieldLabel(""Emissive"");
        ") == 0);
        Check("control: relative placement after SameLine is the correct answer and is not caught (the Flip row after the fix)", Hits(@"
            EditorWidgets.FieldLabel(""Flip"");
            EditorWidgets.TinyCheckbox(""##Flip X"", ref flipX);
            ImGui.SameLine(0, 6);
            ImGui.TextDisabled(""X"");
        ") == 0);

        Console.WriteLine($"=== InspectorLayout: {_pass} passed, {_fail} failed ===");
    }

    public static List<string> ScanText(string path, string text)
    {
        var hits = new List<string>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        bool inBlock = false, inVerbatim = false;
        bool armed = false;
        int armedLine = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var t = SourceScan.StripCommentsAndStrings(lines[i], ref inBlock, ref inVerbatim).Trim();
            if (t.Length == 0) continue;

            if (IsMemberDecl(t)) armed = false;

            foreach (var raw in t.Split(';'))
            {
                var s = raw.Trim();
                if (s.Length == 0) continue;

                int label = s.IndexOf("FieldLabel(", StringComparison.Ordinal);
                int same = s.IndexOf("SameLine(", StringComparison.Ordinal);

                if (label >= 0 && (armed || (same >= 0 && same < label)))
                    hits.Add(armedLine == i + 1 || same >= 0
                        ? $"{path}:{i + 1}"
                        : $"{path}:{armedLine} → {i + 1}");

                if (same >= 0 && (label < 0 || same > label))
                    { armed = true; armedLine = i + 1; }
                else if (Submits(s))
                    armed = false;
            }
        }
        return hits;
    }

    private static bool Submits(string s)
    {
        int a = s.IndexOf("ImGui.", StringComparison.Ordinal);
        int b = s.IndexOf("EditorWidgets.", StringComparison.Ordinal);
        int i = a < 0 ? b : (b < 0 ? a : Math.Min(a, b));
        if (i < 0) return false;

        foreach (var p in NonSubmitting)
            if (s.AsSpan(i).StartsWith(p, StringComparison.Ordinal)) return false;
        return true;
    }

    private static readonly string[] NonSubmitting =
    {
        "ImGui.Get", "ImGui.Is", "ImGui.Push", "ImGui.Pop", "ImGui.Calc", "ImGui.Set",
        "ImGui.BeginDisabled", "ImGui.EndDisabled", "ImGui.AlignTextToFramePadding",
        "ImGui.OpenPopup", "ImGui.CloseCurrentPopup", "ImGui.Indent", "ImGui.Unindent",
        "ImGui.BeginGroup",
        "ImGui.BeginDragDrop", "ImGui.EndDragDrop", "ImGui.AcceptDragDropPayload",
        "ImGui.TableSet",
        "EditorWidgets.HasGlyph", "EditorWidgets.Ellipsize",
    };

    private static bool IsMemberDecl(string t) =>
        (t.StartsWith("private ", StringComparison.Ordinal)
         || t.StartsWith("public ", StringComparison.Ordinal)
         || t.StartsWith("internal ", StringComparison.Ordinal)
         || t.StartsWith("protected ", StringComparison.Ordinal)
         || t.StartsWith("static ", StringComparison.Ordinal))
        && t.Contains('(', StringComparison.Ordinal);

    public static (int Files, int LabelCalls, List<string> Violations) ScanDir(string root)
    {
        var hits = new List<string>();
        int files = 0, labels = 0;
        if (!Directory.Exists(root)) return (0, 0, hits);

        foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                                      .OrderBy(f => f, StringComparer.Ordinal))
        {
            files++;
            var text = File.ReadAllText(file);
            bool inBlock = false, inVerbatim = false;
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
                if (SourceScan.StripCommentsAndStrings(raw, ref inBlock, ref inVerbatim)
                              .Contains("FieldLabel(", StringComparison.Ordinal))
                    labels++;
            hits.AddRange(ScanText(file.Replace('\\', '/'), text));
        }
        return (files, labels, hits);
    }

    private static int Hits(string snippet) => ScanText("<control>", snippet).Count;

    private static string Detail(List<string> v) =>
        v.Count == 0 ? "" : "\n         violations: " + string.Join("\n               ", v)
            + "\n         → FieldLabel once per row. Attach the rest with a relative SameLine(0, n)."
            + "\n           (see the EditorWidgets.FieldLabel doc - SameLine(108f) is an absolute X relative to the window)";

    private static void Check(string name, bool ok)
    {
        if (ok) { _pass++; Console.WriteLine($"  PASS  {name}"); }
        else { _fail++; Console.WriteLine($"  FAIL  {name}"); }
    }
}

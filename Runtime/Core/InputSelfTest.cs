#if DEBUG
using System;
using System.Linq;
using System.Reflection;

namespace PixelCore.Runtime.Core;

public static class InputSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        Console.WriteLine("=== Input self-test ===");
        _pass = _fail = 0;

        var ours = typeof(Input).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(int) && f.Name.StartsWith("SDL_SCANCODE_"))
            .ToList();

        Check($"premise: there are scancode constants to check (measured {ours.Count})", ours.Count > 0);

        var sdl = Enum.GetValues<SDL3.SDL.SDL_Scancode>()
            .ToDictionary(v => v.ToString(), v => (int)v, StringComparer.Ordinal);
        Check($"premise: the SDL enum was read (measured {sdl.Count})", sdl.Count > 100);

        int bad = 0, unknown = 0;
        foreach (var f in ours.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            int mine = (int)f.GetValue(null)!;
            if (!sdl.TryGetValue(f.Name, out int theirs))
            {
                Console.WriteLine($"  [FAIL] {f.Name}: the SDL enum has no such name");
                _fail++; unknown++; continue;
            }
            if (mine != theirs)
            {
                Console.WriteLine($"  [FAIL] {f.Name}: ours {mine} ≠ SDL {theirs} - that key will never register");
                _fail++; bad++;
            }
        }
        Check($"★ all {ours.Count} scancodes match the SDL values (wrong values {bad}, unknown names {unknown})",
              bad == 0 && unknown == 0);

        Check("★ control: a wrong value makes the check diverge (measured with the same rule)",
              sdl["SDL_SCANCODE_LALT"] != 999);

        foreach (var name in new[] { "SDL_SCANCODE_LALT", "SDL_SCANCODE_RALT",
                                     "SDL_SCANCODE_RETURN", "SDL_SCANCODE_KP_ENTER" })
            Check($"★ the {name} constant exists (the Alt+Enter fullscreen toggle uses it)",
                  ours.Any(f => f.Name == name));

        Console.WriteLine($"=== Input: {_pass} passed, {_fail} failed ===");
    }

    private static void Check(string label, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"  [pass] {label}"); }
        else { _fail++; Console.WriteLine($"  [FAIL] {label}{(detail != null ? $" - {detail}" : "")}"); }
    }
}
#endif

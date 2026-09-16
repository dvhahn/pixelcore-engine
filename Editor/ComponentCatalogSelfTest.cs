using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Serialization;

namespace PixelCore.Editor;

public static class ComponentCatalogSelfTest
{
    private static int _pass, _fail;

    public sealed class MarkerProbe : Component { }

    public sealed class FieldProbe : Component { public float Speed { get; set; } = 1f; }

    public sealed class DerivedProbe : Runtime.Components.Interactable { }

    public static void Run()
    {
        Console.WriteLine("=== ComponentCatalog self-test ===");
        var all = ComponentCatalog.All;

        Check("premise: there are registered components", all.Count >= 10, all.Count.ToString());
        Check("no duplicate names",
              all.Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == all.Count);
        Check("no duplicate types", all.Select(e => e.Type).Distinct().Count() == all.Count);
        Check("* every one derives from Component", all.All(e => typeof(Component).IsAssignableFrom(e.Type)));
        Check("* eligibility - all public, non-nested, concrete, non-generic, default constructor, outside Editor", all.All(e => Eligible(e.Type)));
        Check("control: the nested test probe (PhysicsSelfTest.TickCounter) is not in the list",
              all.All(e => e.Type.Name != "TickCounter"));
        Check("control: a component in the Editor namespace (MarkerProbe) is not in the list - the type does not exist in Release",
              all.All(e => e.Type != typeof(MarkerProbe)) && !Eligible(typeof(MarkerProbe)));

        var other = all.Where(e => e.Category == ComponentCategories.Other).Select(e => e.Name).ToList();
        Check("* Other is empty (anything there means a forgotten classification)", other.Count == 0, string.Join(", ", other));
        Check("* a game component lands in Game with no attribute (PlayerController)",
              ComponentCatalog.CategoryOf(typeof(Gameplay.Player.PlayerController)) == ComponentCategories.Game);
        Check("* anything outside the engine is Game whatever its namespace - the closed set is the engine (contrasted with a type outside Gameplay.*)",
              ComponentCatalog.CategoryOf(typeof(ComponentCatalogSelfTest)) == ComponentCategories.Game
              && !ComponentCatalog.IsEngine(typeof(ComponentCatalogSelfTest)));
        Check("control: an engine component is classified by its attribute (Talker goes to Interaction)",
              ComponentCatalog.CategoryOf(typeof(Runtime.Components.Talker)) == ComponentCategories.Interaction);
        Check("control: an engine type (PixelCore.Runtime.*) with no attribute is Other (the rule is alive - contrasted with Entity)",
              ComponentCatalog.IsEngine(typeof(Runtime.Core.Entity))
              && ComponentCatalog.CategoryOf(typeof(Runtime.Core.Entity)) == ComponentCategories.Other);
        Check("* Hidden does not appear in the list (SceneInstance - prefab instances come from a drag)",
              all.All(e => e.Type != typeof(Runtime.Components.SceneInstance))
              && ComponentCatalog.CategoryOf(typeof(Runtime.Components.SceneInstance)) == ComponentCategories.Hidden);

        Check("display name: BoxCollider2D gives 'Box Collider 2D'", ComponentCatalog.DisplayName("BoxCollider2D") == "Box Collider 2D",
              ComponentCatalog.DisplayName("BoxCollider2D"));
        Check("display name: Light2D gives 'Light 2D' (no space before a capital after a digit)", ComponentCatalog.DisplayName("Light2D") == "Light 2D");
        Check("display name: SpriteRenderer gives 'Sprite Renderer'", ComponentCatalog.DisplayName("SpriteRenderer") == "Sprite Renderer");
        Check("display name: Talker stays 'Talker' (a single word is unchanged)", ComponentCatalog.DisplayName("Talker") == "Talker");

        var groups = ComponentCatalog.Grouped("");
        var ranks = groups.Select(g => ComponentCatalog.Rank(g.Category)).ToList();
        Check("* the groups come out in Order order", ranks.SequenceEqual(ranks.OrderBy(r => r)),
              string.Join(" → ", groups.Select(g => g.Category)));
        Check("there are no empty groups", groups.All(g => g.Items.Count > 0));
        Check("within a group it is by name", groups.All(g => g.Items.Select(i => i.Name)
              .SequenceEqual(g.Items.Select(i => i.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))));
        Check("Basic (Transform) is at the top",
              groups.Count > 0 && groups[0].Category == ComponentCategories.Basic
              && groups[0].Items.Count > 0 && groups[0].Items[0].Type == typeof(Runtime.Components.Transform));

        var col = ComponentCatalog.Search("col");
        Check("* 'col' returns the three colliders", col.Count == 3 && col.All(e => e.Name.Contains("Collider")),
              string.Join(", ", col.Select(e => e.Name)));
        Check("* spaces and case are ignored ('sortinggroup' = 'SORTING GROUP' = 'Sorting Group')",
              ComponentCatalog.Search("sortinggroup").Count == 1 && ComponentCatalog.Search("SORTING GROUP").Count == 1);
        Check("* the group name finds them too ('Physics' returns the body plus three colliders)", ComponentCatalog.Search("Physics").Count == 4,
              ComponentCatalog.Search("Physics").Count.ToString());
        Check("the type name finds it too ('light2d', whose display name is 'Light 2D')", ComponentCatalog.Search("light2d").Count == 1);
        Check("something absent gives zero", ComponentCatalog.Search("zzznone").Count == 0);
        Check("an empty query returns everything", ComponentCatalog.Search("").Count == all.Count && ComponentCatalog.Search(null).Count == all.Count);
        var first2d = ComponentCatalog.Search("2d").FirstOrDefault();
        Check("* the first search result (what Enter picks) follows group order too ('2d' puts Render's Light 2D before Physics)",
              first2d.Type != null && first2d.Category == ComponentCategories.Render, first2d.Name);

        var scanned = ScanAssembly().Select(t => t.FullName!).ToHashSet(StringComparer.Ordinal);
        var table = ComponentTypes.Names.ToHashSet(StringComparer.Ordinal);
        Check("premise: reflection found Component subclasses (zero would mean the scan is dead)", scanned.Count >= 10, scanned.Count.ToString());
        Check("* the creation table equals the reflection scan (nothing missed)", scanned.IsSubsetOf(table),
              "missing from the table: " + string.Join(", ", scanned.Except(table)));
        Check("* the creation table equals the reflection scan (nothing extra)", table.IsSubsetOf(scanned),
              "only in the table: " + string.Join(", ", table.Except(scanned)));
        Check("* the list equals the table minus Hidden", all.Select(e => e.Type).ToHashSet().SetEquals(
                  ComponentTypes.Types.Where(t => ComponentCatalog.CategoryOf(t) != ComponentCategories.Hidden)));
        Check("every type in the table really constructs an instance (name, typeof and constructor agree)",
              ComponentTypes.Names.All(n => ComponentTypes.Create(n)?.GetType().FullName == n));

        TestMarkerRoundTrip();

        TestBespokeCreateFor();

        TestMarkerLossGuards();

        Console.WriteLine($"=== ComponentCatalog: {_pass} passed, {_fail} failed ===");
    }

    private static void TestMarkerLossGuards()
    {
        Console.WriteLine("--- marker loss guards (components with values, concrete inheritance) ---");

        var exempt = new Dictionary<Type, string>
        {
            [typeof(Runtime.Components.TilemapRenderer)] = "saved as a scene section (tilemapLayers) - SceneSerializer.ToData stores the whole entity separately",
        };
        Check("the exemption list is real and has no dedicated data (the exemption is not a dead line)",
              exempt.Keys.All(t => ComponentTypes.IsRegistered(t) && !ComponentDataRegistry.HasBespokeData(t)));

        var lossy = new List<string>();
        var derived = new List<string>();
        int measured = 0;
        foreach (var name in ComponentTypes.Names.OrderBy(n => n, StringComparer.Ordinal))
        {
            var t = ComponentTypes.Create(name)?.GetType();
            if (t == null) continue;
            measured++;
            if (DerivesFromConcrete(t)) derived.Add($"{t.Name} : {t.BaseType!.Name}");
            if (ComponentDataRegistry.HasBespokeData(t) || exempt.ContainsKey(t)) continue;
            var members = ValueMembersLostByMarker(t);
            if (members.Count > 0) lossy.Add($"{t.Name}({string.Join(",", members)})");
        }
        Check("premise: the table types were measured", measured >= 10, measured.ToString());
        Check("* a component with values (a public writable member) is never saved as a marker - if one is, write a dedicated SceneData class",
              lossy.Count == 0, string.Join(", ", lossy));
        Check("control: the probe with values is caught (FieldProbe.Speed)",
              ValueMembersLostByMarker(typeof(FieldProbe)).SequenceEqual(new[] { "Speed" }));
        Check("control: the probe with no values is not caught (MarkerProbe) - the inherited Enabled is not its own declaration",
              ValueMembersLostByMarker(typeof(MarkerProbe)).Count == 0);

        Check("* zero types inherit a concrete component - saving would pick the base DTO and loading would demote it (splitting the saving comes first)",
              derived.Count == 0, string.Join(", ", derived));
        Check("control: the concrete-inheritance probe is caught (DerivedProbe : Interactable)", DerivesFromConcrete(typeof(DerivedProbe)));
        Check("control: an abstract base is not caught (BoxCollider2D : Collider2D)",
              !DerivesFromConcrete(typeof(Runtime.Components.BoxCollider2D)));
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "an editor-only (DEBUG) check - Release does not compile Editor")]
    internal static List<string> ValueMembersLostByMarker(Type t)
    {
        const BindingFlags F = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        return t.GetProperties(F).Where(p => p.CanWrite && p.SetMethod is { IsPublic: true }).Select(p => p.Name)
                .Concat(t.GetFields(F).Where(f => !f.IsInitOnly).Select(f => f.Name))
                .OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    internal static bool DerivesFromConcrete(Type t)
        => t.BaseType is { } b && b != typeof(Component) && !b.IsAbstract;

    private static void TestBespokeCreateFor()
    {
        Console.WriteLine("--- dedicated data versus CreateFor ---");
        var wrong = new List<string>();
        int measured = 0;
        foreach (var name in ComponentTypes.Names.OrderBy(n => n, StringComparer.Ordinal))
        {
            var c = ComponentTypes.Create(name);
            if (c == null || c is Runtime.Components.SceneInstance) continue;
            if (!ComponentDataRegistry.HasBespokeData(c.GetType())) continue;
            measured++;
            var d = ComponentDataRegistry.CreateFor(c);
            if (d == null || d is GenericComponentData) wrong.Add($"{c.GetType().Name} -> {(d == null ? "null" : "marker")}");
        }
        Check("premise: types with dedicated data were measured", measured >= 10, measured.ToString());
        Check("* a type with dedicated data also gets a dedicated DTO from CreateFor (the marker fallback hides no missing arm)",
              wrong.Count == 0, string.Join(", ", wrong));
        Check("control: a type with no dedicated data gets a marker", ComponentDataRegistry.CreateFor(new MarkerProbe()) is GenericComponentData);
        Check("control: SceneInstance is null (no per-component copy - it goes by whole entity)",
              ComponentDataRegistry.CreateFor(new Runtime.Components.SceneInstance()) == null);
    }

    private static void TestMarkerRoundTrip()
    {
        Console.WriteLine("--- marker round trip (a component with no dedicated data) ---");
        string name = typeof(MarkerProbe).FullName!;
        ComponentTypes.Register(name, typeof(MarkerProbe), () => new MarkerProbe());
        Check("premise: the probe is in the creation table", ComponentTypes.IsRegistered(typeof(MarkerProbe)));
        Check("premise: the probe has no dedicated data (with any, this check would never touch a marker)",
              !ComponentDataRegistry.HasBespokeData(typeof(MarkerProbe)));

        var scene = new Scene("MarkerTest");
        var e = scene.CreateEntity("Probe");
        scene.FlushPendingAdds();
        e.AddComponent<MarkerProbe>();

        var captured = ComponentDataRegistry.CaptureAll(e);
        var marker = captured.OfType<GenericComponentData>().FirstOrDefault();
        Check("* CaptureAll includes the marker", marker != null);
        Check("the marker name is Type.FullName", marker?.Name == name, marker?.Name);
        Check("CreateFor (the inspector copy and reset path) also gives a marker",
              ComponentDataRegistry.CreateFor(e.GetComponent<MarkerProbe>()!) is GenericComponentData);
        Check("WriteTo is true only for the same type", marker != null && marker.WriteTo(e.GetComponent<MarkerProbe>()!)
              && !marker.WriteTo(e.GetComponent<Runtime.Components.Transform>()!));

        string dir = Path.Combine(Path.GetTempPath(), "pixelcore-marker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "marker.scene");
            SceneSerializer.SaveToFile(SceneSerializer.ToData(scene), path);
            string json = File.ReadAllText(path);
            Check("* the file keeps the \"component\" discriminator plus the name",
                  json.Contains("\"component\"") && json.Contains(name), json.Length > 400 ? json[..400] : json);

            var loaded = SceneSerializer.LoadFromFile(path);
            var scene2 = new Scene("MarkerTest2");
            Check("premise: the file reads back", loaded != null);
            if (loaded != null)
            {
                SceneSerializer.FromData(scene2, loaded);
                var back = scene2.FindEntity("Probe");
                Check("* the probe comes back alive on the loaded entity (marker -> creation table -> instance)",
                      back?.GetComponent<MarkerProbe>() != null);
                Check("exactly one (no duplicate creation)", back != null && back.GetComponents<MarkerProbe>().Count() == 1);
            }
        }
        finally { try { Directory.Delete(dir, true); } catch {  } }

        var e2 = scene.CreateEntity("Unknown");
        scene.FlushPendingAdds();
        int before = e2.Components.Count;
        new GenericComponentData { Name = "PixelCore.Nope.Missing" }.Apply(e2);
        Check("* a name absent from the creation table barks and is skipped (no exception, and the component count is unchanged)", e2.Components.Count == before);

        Check("control: IsRegistered is false for an unregistered type", !ComponentTypes.IsRegistered("PixelCore.Nope.Missing"));
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "an editor-only (DEBUG) check - Release does not compile Editor")]
    internal static bool Eligible(Type t)
        => t.IsClass && !t.IsAbstract && t.IsPublic && !t.IsNested && !t.ContainsGenericParameters
           && typeof(Component).IsAssignableFrom(t)
           && t.GetConstructor(Type.EmptyTypes) != null
           && !(t.Namespace ?? "").StartsWith("PixelCore.Editor", StringComparison.Ordinal);

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "an editor-only (DEBUG) check - Release does not compile Editor")]
    private static IEnumerable<Type> ScanAssembly() => typeof(Component).Assembly.GetTypes().Where(Eligible);

    private static void Check(string name, bool ok, string? detail = null)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name + (!ok && !string.IsNullOrEmpty(detail) ? $" — {detail}" : ""));
        if (ok) _pass++; else _fail++;
    }
}

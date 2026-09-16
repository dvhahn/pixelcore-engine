using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Animation;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Serialization;
using PixelCore.Editor.Commands;

namespace PixelCore.Editor;

public static class CommandSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        TestPrefabFileIsCurrentSchema();
        Console.WriteLine("=== Command (Undo/Redo) self-test ===");
        var state = new EditorState();
        var scene = new Scene("t");
        state.CurrentScene = scene;

        var a = scene.CreateEntity("A");
        a.GetComponent<Transform>()!.Position = new Vector2(33, 44);
        var rb = a.AddComponent<Rigidbody2D>(); rb.UseGravity = false; rb.GravityScale = 2.5f;
        var col = a.AddComponent<BoxCollider2D>(); col.IsTrigger = true; col.Offset = new Vector2(1, 2);
        scene.Update(0f);
        state.Select(a);

        state.ExecuteCommand(new DeleteEntityCommand(scene, a, state));
        scene.Update(0f);
        Check("Delete: A is gone", scene.FindEntity("A") == null);
        Check("Delete: the selection was cleared", state.SelectedEntity == null);

        state.Undo();
        scene.Update(0f);
        var a2 = scene.FindEntity("A");
        Check("Undo: A is back", a2 != null);
        Check("Undo: Position restored (33,44)", a2 != null && a2.GetComponent<Transform>()!.Position == new Vector2(33, 44));
        Check("Undo: Rigidbody restored (gravity off, scale 2.5)",
            a2?.GetComponent<Rigidbody2D>() is { UseGravity: false, GravityScale: 2.5f });
        Check("Undo: Collider restored (trigger, offset 1,2)",
            a2?.GetComponent<BoxCollider2D>() is { IsTrigger: true } c && c.Offset == new Vector2(1, 2));
        Check("Undo: the selection is the restored A", state.SelectedEntity == a2);

        state.Redo();
        scene.Update(0f);
        Check("Redo: A is gone again", scene.FindEntity("A") == null);

        var b = scene.CreateEntity("B");
        b.GetComponent<Transform>()!.Position = new Vector2(7, 8);
        scene.Update(0f);
        state.Select(b);
        state.CommandHistory.AddExecuted(new SpawnEntityCommand(scene, b, "Add", state));

        state.Undo();
        scene.Update(0f);
        Check("Spawn undo: B is gone", scene.FindEntity("B") == null);
        Check("Spawn undo: the selection was cleared", state.SelectedEntity == null);

        state.Redo();
        scene.Update(0f);
        var b2 = scene.FindEntity("B");
        Check("Spawn redo: B is back", b2 != null);
        Check("Spawn redo: Position restored (7,8)", b2 != null && b2.GetComponent<Transform>()!.Position == new Vector2(7, 8));
        Check("Spawn redo: the selection is the restored B", state.SelectedEntity == b2);

        var c0 = scene.CreateEntity("C");
        c0.GetComponent<Transform>()!.Position = new Vector2(10, 20);
        var crb = c0.AddComponent<Rigidbody2D>(); crb.UseGravity = false; crb.GravityScale = 1.5f;
        scene.Update(0f);

        var dup = EntitySnapshot.Duplicate(scene, c0, state);
        scene.Update(0f);
        Check("Duplicate: the copy is named 'C (1)'", dup.Name == "C (1)");
        Check("Duplicate: copied in place (10,20)", dup.GetComponent<Transform>()!.Position == new Vector2(10, 20));
        Check("Duplicate: Rigidbody values copied too (gravity off, scale 1.5)",
            dup.GetComponent<Rigidbody2D>() is { UseGravity: false, GravityScale: 1.5f });
        Check("Duplicate: the selection is the copy", state.SelectedEntity == dup);

        var dup2 = EntitySnapshot.Duplicate(scene, dup, state);
        scene.Update(0f);
        Check("Duplicate x2: 'C (1)' → 'C (2)'", dup2.Name == "C (2)");

        state.Undo();
        state.Undo();
        scene.Update(0f);
        Check("Duplicate undo x2: the copies are gone and the original remains",
            scene.FindEntity("C (1)") == null && scene.FindEntity("C (2)") == null && scene.FindEntity("C") != null);

        var parent = scene.CreateEntity("P");
        var child = scene.CreateEntity("Ch");
        child.SetParent(parent);
        scene.Update(0f);

        state.ExecuteCommand(new DeleteEntityCommand(scene, child, state));
        scene.Update(0f);
        Check("Child delete: removed from _entities", scene.FindEntity("Ch") == null);
        Check("Child delete: detached from the parent's Children too (no ghost node)", parent.Children.Count == 0);

        state.Undo();
        scene.Update(0f);
        Check("Child delete undo: restored under its parent",
            scene.FindEntity("Ch") is { } ch2 && ch2.Parent == parent && parent.Children.Count == 1);
        state.Redo();
        scene.Update(0f);

        var p2 = scene.CreateEntity("P2");
        var ch3 = scene.CreateEntity("Ch3");
        ch3.SetParent(p2);
        scene.Update(0f);

        scene.DestroyEntity(p2);
        scene.Update(0f);
        Check("Parent delete: the children leave _entities too (no zombies)",
            scene.FindEntity("P2") == null && scene.FindEntity("Ch3") == null);

        TestSubtreeDeleteUndo();
        TestSubtreeDuplicateAndPaste();
        TestMergedHostSubtree();
        TestPivotSpread();
        TestComponentClipboard();
        TestPlayLifetimeRewind();
        TestPlayLifetimeRewindRender();
        TestRoomChangeMarksStop();
        TestSpriteSwapUndo();
        TestRendererSlotGate();
        TestAnimAtlasSwap();
        TestAtlasIdOnSave();
        TestBulkRetarget();
        TestClipListByFolder();
        TestOutOfRangeWarning();
        TestBulkCacheInvalidation();
        TestClipIdOnSave();
        TestClipNameIsFileName();
        TestAssetEvents();
        TestDefaultClipApplies();
        TestDeleteGuards();
        TestContentRootShape();
        TestCommandIdentity();
        TestConvertToPrefabWorker();

        Console.WriteLine($"=== {_pass} passed, {_fail} failed ===");
    }

    private static void TestSubtreeDeleteUndo()
    {
        var scene = new Scene("sub1");
        var state = new EditorState { CurrentScene = scene };

        var p = scene.CreateEntity("SP");
        p.GetComponent<Transform>()!.Position = new Vector2(10, 10);
        var c = scene.CreateEntity("SC");
        c.SetParent(p);
        c.GetComponent<Transform>()!.Position = new Vector2(20, 20);
        var g = scene.CreateEntity("SG");
        g.SetParent(c);
        var grb = g.AddComponent<Rigidbody2D>(); grb.UseGravity = false; grb.GravityScale = 3.25f;
        scene.Update(0f);

        p.SerializedId = 11; c.SerializedId = 22; g.SerializedId = 33;
        state.Select(p);

        state.ExecuteCommand(new DeleteEntityCommand(scene, p, state));
        scene.Update(0f);
        Check("Subtree delete: the parent, child and grandchild all disappear",
              scene.FindEntity("SP") == null && scene.FindEntity("SC") == null && scene.FindEntity("SG") == null);

        state.Undo();
        scene.Update(0f);
        var p2 = scene.FindEntity("SP");
        var c2 = scene.FindEntity("SC");
        var g2 = scene.FindEntity("SG");
        Check("★ Delete undo: the child and grandchild come back (they used to be lost for good)", c2 != null && g2 != null);
        Check("Delete undo: the parent links are restored (P>C>G)",
              p2 != null && c2?.Parent == p2 && g2?.Parent == c2);
        Check("Delete undo: the child's Transform value is restored (20,20)",
              c2?.GetComponent<Transform>()?.Position == new Vector2(20, 20));
        Check("Delete undo: the grandchild's component values are restored (gravity off, scale 3.25)",
              g2?.GetComponent<Rigidbody2D>() is { UseGravity: false, GravityScale: 3.25f });
        Check("★ Delete undo: the whole subtree preserves its SerializedId (prefab override keys stay attached)",
              p2?.SerializedId == 11 && c2?.SerializedId == 22 && g2?.SerializedId == 33);

        state.Redo();
        scene.Update(0f);
        Check("Delete redo: the whole thing disappears again",
              scene.FindEntity("SP") == null && scene.FindEntity("SG") == null);

        state.Undo();
        scene.Update(0f);
        var g3 = scene.FindEntity("SG");
        Check("Second delete undo: the grandchild, the parent links and the ids are unchanged",
              g3 != null && g3.SerializedId == 33 && g3.Parent?.Name == "SC");
    }

    private static void TestSubtreeDuplicateAndPaste()
    {
        var scene = new Scene("sub2");
        var state = new EditorState { CurrentScene = scene };

        var p = scene.CreateEntity("SP");
        p.GetComponent<Transform>()!.Position = new Vector2(10, 10);
        var c = scene.CreateEntity("SC");
        c.SetParent(p);
        c.GetComponent<Transform>()!.Position = new Vector2(20, 20);
        var g = scene.CreateEntity("SG");
        g.SetParent(c);
        g.GetComponent<Transform>()!.Position = new Vector2(30, 30);
        scene.Update(0f);
        p.SerializedId = 11; c.SerializedId = 22; g.SerializedId = 33;

        var dup = EntitySnapshot.Duplicate(scene, p, state);
        scene.Update(0f);
        var dupChild = dup.Children.Count > 0 ? dup.Children[0] : null;
        var dupGrand = dupChild != null && dupChild.Children.Count > 0 ? dupChild.Children[0] : null;

        Check("★ Duplicate: the child and grandchild come along", dupChild != null && dupGrand != null);
        Check("Duplicate: only the root is renamed to 'SP (1)'; child names are unchanged",
              dup.Name == "SP (1)" && dupChild?.Name == "SC" && dupGrand?.Name == "SG");
        Check("★ Duplicate: SerializedId is zero across the subtree (a copy inherits nobody else's file numbers)",
              dup.SerializedId == 0 && dupChild?.SerializedId == 0 && dupGrand?.SerializedId == 0);
        Check("Duplicate: in place (the offset belongs to paste only)",
              dup.GetComponent<Transform>()!.Position == new Vector2(10, 10)
              && dupChild?.GetComponent<Transform>()?.Position == new Vector2(20, 20));
        Check("Duplicate: ordered right after the original (it does not jump to the end of the list)",
              scene.GetSiblingIndex(dup) == scene.GetSiblingIndex(p) + 1);

        EntityClipboard.Copy(new[] { p, c });
        Check("★ Copy: a simultaneous parent and child selection captures only the top level (dedupe - without it there would be two copies)",
              EntityClipboard.Count == 1);

        var pasted = EntityClipboard.Paste(scene, null, state);
        scene.Update(0f);
        Check("Paste: one root", pasted.Count == 1);

        var pr = pasted.Count > 0 ? pasted[0] : null;
        var prChild = pr != null && pr.Children.Count > 0 ? pr.Children[0] : null;
        var prGrand = prChild != null && prChild.Children.Count > 0 ? prChild.Children[0] : null;
        Check("★ Paste: the child and grandchild come along (symptom 1 - only the parent used to be pasted)",
              prChild != null && prGrand != null);

        var off = new Vector2(state.GridSize, state.GridSize);
        Check("★ Paste: the child and grandchild Transforms move by the same delta (Transform is in world coordinates)",
              prChild?.GetComponent<Transform>()?.Position == new Vector2(20, 20) + off
              && prGrand?.GetComponent<Transform>()?.Position == new Vector2(30, 30) + off);
        Check("Paste: every copy's SerializedId is zero",
              pr?.SerializedId == 0 && prChild?.SerializedId == 0 && prGrand?.SerializedId == 0);

        state.Undo();
        scene.Update(0f);
        Check("Paste undo: the whole subtree (no orphaned children)",
              pr != null && scene.FindEntity(pr.Name) == null && scene.Entities.Count(e => e.Name == "SG") == 2);
    }

    private static void TestMergedHostSubtree()
    {
        Runtime.Assets.AnimClipCache.ContentRoot = System.IO.Path.Combine(AppContext.BaseDirectory, "Content");

        {
            var scene = new Scene("sub3a");
            var state = new EditorState { CurrentScene = scene };

            var rig = scene.CreateEntity(Runtime.Core.Scene.PlayerName);
            var inst = rig.AddComponent<Runtime.Components.SceneInstance>();
            inst.SceneId = Gameplay.Systems.LevelSetup.PlayerPrefabId;
            inst.Load();
            scene.Update(0f);
            Check("Premise: the player prefab loaded root-merged",
                  rig.GetComponent<Animator>() != null && inst.RootMerged);

            state.ExecuteCommand(new DeleteEntityCommand(scene, rig, state));
            scene.Update(0f);
            Check("Host delete: gone", scene.FindEntity(Runtime.Core.Scene.PlayerName) == null);

            state.Undo();
            scene.Update(0f);
            var back = scene.FindEntity(Runtime.Core.Scene.PlayerName);
            Check("Host undo: back again", back != null);
            Check("★ Host undo: one Animator (not the base's plus the snapshot's = two)",
                  back != null && back.GetComponents<Animator>().Count() == 1);
            Check("★ Host undo: one capsule",
                  back != null && back.GetComponents<Runtime.Components.CapsuleCollider2D>().Count() == 1);
            Check("★ Host undo: the root-merged state is restored (RootMerged)",
                  back?.GetComponent<Runtime.Components.SceneInstance>()?.RootMerged == true);
        }

        {
            var scene = new Scene("sub3b");
            var state = new EditorState { CurrentScene = scene };

            var host = scene.CreateEntity("BG");
            host.GetComponent<Transform>()!.Position = new Vector2(64, 48);
            var inst = host.AddComponent<Runtime.Components.SceneInstance>();
            inst.SceneId = BackgroundPrefabId;
            inst.Load();
            scene.Update(0f);
            Check("Premise: a prefab with children loaded root-merged (2 instance children)",
                  inst.RootMerged && host.Children.Count == 2);

            EntityClipboard.Copy(new[] { host });
            var pasted = EntityClipboard.Paste(scene, null, state);
            scene.Update(0f);
            var copy = pasted.Count > 0 ? pasted[0] : null;

            Check("Host paste: one copy plus regenerated instance children",
                  copy != null && copy.Children.Count == 2);
            Check("★ Host copy: not two of each component (one SpriteRenderer)",
                  copy != null && copy.GetComponents<SpriteRenderer>().Count() == 1);

            var off = new Vector2(state.GridSize, state.GridSize);
            bool childrenMoved = copy != null;
            if (copy != null)
                foreach (var child in copy.Children)
                {
                    var orig = host.Children.FirstOrDefault(x => x.Name == child.Name);
                    var cd = child.GetComponent<Transform>();
                    var od = orig?.GetComponent<Transform>();
                    if (orig == null || cd == null || od == null || cd.Position - od.Position != off)
                    { childrenMoved = false; break; }
                }
            Check("★ Host copy: the instance children move by the same delta as the host (they do not stay put)",
                  childrenMoved);
            Check("Host copy: the host itself moves one step",
                  copy?.GetComponent<Transform>()?.Position == new Vector2(64, 48) + off);
        }
    }

    private const string BackgroundPrefabId = "7c0e2b51";

    private static void TestRendererSlotGate()
    {
        var scene = new Scene("gate");

        var withSprite = scene.CreateEntity("WithSprite");
        withSprite.AddComponent<SpriteRenderer>();

        var withAnimator = scene.CreateEntity("WithAnimator");
        withAnimator.AddComponent<Animator>();

        var bare = scene.CreateEntity("Bare");
        scene.Update(0f);

        Check("An empty entity accepts a SpriteRenderer",
              !Panels.InspectorPanel.HasComponentOfType(bare, typeof(SpriteRenderer)));
        Check("An empty entity accepts an Animator",
              !Panels.InspectorPanel.HasComponentOfType(bare, typeof(Animator)));

        Check("With a SpriteRenderer, another SpriteRenderer is blocked",
              Panels.InspectorPanel.HasComponentOfType(withSprite, typeof(SpriteRenderer)));
        Check("With an Animator, another Animator is blocked",
              Panels.InspectorPanel.HasComponentOfType(withAnimator, typeof(Animator)));

        Check("★ With a SpriteRenderer, an Animator is still allowed (coexistence is standard)",
              !Panels.InspectorPanel.HasComponentOfType(withSprite, typeof(Animator)));
        Check("★ With an Animator, a SpriteRenderer is still allowed (something has to draw)",
              !Panels.InspectorPanel.HasComponentOfType(withAnimator, typeof(SpriteRenderer)));

        Check("Control: with a renderer, a Rigidbody2D is still allowed",
              !Panels.InspectorPanel.HasComponentOfType(withSprite, typeof(Rigidbody2D)));
        Check("Control: Transform is blocked by exact type",
              Panels.InspectorPanel.HasComponentOfType(bare, typeof(Transform)));

        var state = new EditorState { CurrentScene = scene };
        var target = scene.CreateEntity("NewAnim");
        scene.Update(0f);

        state.ExecuteCommand(Panels.InspectorPanel.BuildAddCommandForTest(target, typeof(Animator)));
        scene.Update(0f);
        Check("★ Paired attach: adding an Animator adds a SpriteRenderer too (the RequireComponent convention)",
              target.GetComponent<Animator>() != null && target.GetComponent<SpriteRenderer>() != null);

        state.Undo();
        scene.Update(0f);
        Check("★ The paired attach undoes as one step - both disappear (half of it remaining is an edit nobody asked for)",
              target.GetComponent<Animator>() == null && target.GetComponent<SpriteRenderer>() == null);

        var hasSprite = scene.CreateEntity("HasSprite");
        hasSprite.AddComponent<SpriteRenderer>();
        scene.Update(0f);
        state.ExecuteCommand(Panels.InspectorPanel.BuildAddCommandForTest(hasSprite, typeof(Animator)));
        scene.Update(0f);
        Check("Control: with a renderer already present, no partner is attached (one SpriteRenderer remains)",
              hasSprite.GetComponents<SpriteRenderer>().Count() == 1);
    }

    private static void TestSpriteSwapUndo()
    {
        const string atlasRel = "Sprites/Tileset/NinjaAdventure/TilesetNature.atlas";
        var atlasFull = System.IO.Path.Combine("Content", atlasRel);
        Check($"The real atlas exists - {atlasRel} (the premise of the mode transition check)", System.IO.File.Exists(atlasFull));
        if (!System.IO.File.Exists(atlasFull)) return;

        var sr = new SpriteRenderer();

        sr.SetSprite(atlasRel, "Tree_Round");
        Check("Premise: it stands in sheet mode (atlas, slice, SourceRect)",
              sr.AtlasPath == atlasRel && sr.SliceName == "Tree_Round" && sr.SourceRect.HasValue);
        var sheet = SpriteState.Capture(sr);

        sr.AtlasPath = null;
        sr.SliceName = null;
        sr.SourceRect = null;
        sr.TexturePath = "Sprites/FX/RainDrop.png";
        sr.PivotX = 0.25f;
        sr.PivotY = 0.75f;
        var single = SpriteState.Capture(sr);

        SpriteState.Restore(sr, sheet);
        Check("Undo: the atlas comes back", sr.AtlasPath == atlasRel);
        Check("Undo: the slice name comes back", sr.SliceName == "Tree_Round");
        Check("Undo: the SourceRect comes back", sr.SourceRect.Equals(sheet.SourceRect));
        Check("★ Undo: the whole-texture remnant is cleared (TexturePath null - no half restores)",
              sr.TexturePath == null);
        Check("★ Undo: the pivot comes back too (without it only the art returns and the feet are off)",
              sr.PivotX == sheet.PivotX && sr.PivotY == sheet.PivotY);

        const string otherRel = "Sprites/Characters/Boy/Walk.atlas";
        var otherFull = System.IO.Path.Combine("Content", otherRel);
        Check($"The second atlas exists - {otherRel} (the premise of the cache check)", System.IO.File.Exists(otherFull));
        if (System.IO.File.Exists(otherFull))
        {
            sr.SetSprite(otherRel, "Walk_R_0");
            Check("Premise: it switched to the other atlas", sr.AtlasPath == otherRel);

            SpriteState.Restore(sr, sheet);
            Check("Undo: the atlas comes back even from another sheet", sr.AtlasPath == atlasRel);

            sr.SetSlice("Tree_Pine");
            Check("★ After undo the slice dropdown is alive (the atlas cache was rebuilt)",
                  sr.SourceRect.HasValue && !sr.SourceRect.Equals(sheet.SourceRect));

            SpriteState.Restore(sr, sheet);
        }

        SpriteState.Restore(sr, single);
        Check("Redo: back to the whole texture", sr.TexturePath == "Sprites/FX/RainDrop.png");
        Check("Redo: the atlas remnants are cleared", sr.AtlasPath == null && sr.SliceName == null);
        Check("Redo: the whole-texture pivot is restored", sr.PivotX == 0.25f && sr.PivotY == 0.75f);

        Check("Identical states compare as SameAs (no wasted undo steps)", single.SameAs(SpriteState.Capture(sr)));
        Check("Different states are not SameAs", !sheet.SameAs(single));
    }

    private static void TestComponentClipboard()
    {
        var state = new EditorState();
        var scene = new Scene("clip");
        state.CurrentScene = scene;

        var src = scene.CreateEntity("Src");
        var srcCol = src.AddComponent<BoxCollider2D>();
        srcCol.Size = new Vector2(24, 12); srcCol.Offset = new Vector2(3, -4); srcCol.IsTrigger = true;
        var dst = scene.CreateEntity("Dst");
        var dstCol = dst.AddComponent<BoxCollider2D>();
        dstCol.Size = new Vector2(99, 99);
        scene.Update(0f);

        Check("Copy: a collider can be copied", ComponentClipboard.Copy(srcCol));
        Check("Copy: the clipboard type is BoxCollider2D", ComponentClipboard.Type == typeof(BoxCollider2D));
        Check("Copy: pasting values is allowed only onto the same type",
            ComponentClipboard.CanPasteValues(dstCol) && !ComponentClipboard.CanPasteValues(dst.GetComponent<Transform>()!));

        state.ExecuteCommand(new WriteComponentValuesCommand(dstCol, ComponentClipboard.Data!, "Paste values"));
        Check("Paste values: every value follows (24x12, offset 3,-4, trigger)",
            dstCol.Size == new Vector2(24, 12) && dstCol.Offset == new Vector2(3, -4) && dstCol.IsTrigger);
        Check("Paste values: the instance is unchanged (not swapped out)",
            dst.GetComponent<BoxCollider2D>() == dstCol && dst.GetComponents<BoxCollider2D>().Count() == 1);

        state.Undo();
        Check("Paste values undo: the old values return (99x99, not a trigger)",
            dstCol.Size == new Vector2(99, 99) && !dstCol.IsTrigger);

        var addCmd = new AddComponentDataCommand(dst, ComponentClipboard.Data!, "Paste BoxCollider2D");
        state.ExecuteCommand(addCmd);
        scene.Update(0f);
        Check("Paste as new: two colliders", dst.GetComponents<BoxCollider2D>().Count() == 2);
        Check("Paste as new: the values are on the new instance",
            addCmd.AddedComponent is BoxCollider2D nb && nb.Size == new Vector2(24, 12) && nb.IsTrigger);
        state.Undo();
        scene.Update(0f);
        Check("Paste as new undo: back to one (the original is kept)",
            dst.GetComponents<BoxCollider2D>().Count() == 1 && dst.GetComponent<BoxCollider2D>() == dstCol);

        state.ExecuteCommand(new WriteComponentValuesCommand(srcCol, new ColliderData(), "Reset"));
        Check("Reset: the offset and trigger go back to their defaults",
            srcCol.Offset == Vector2.Zero && !srcCol.IsTrigger);
        state.Undo();
        Check("Reset undo: back to THAT COMPONENT'S old values, not the copied ones",
            srcCol.Offset == new Vector2(3, -4) && srcCol.IsTrigger);

        state.ExecuteCommand(new RemoveComponentCommand(src, srcCol));
        scene.Update(0f);
        Check("Remove: no collider", src.GetComponent<BoxCollider2D>() == null);

        state.Undo();
        scene.Update(0f);
        var restored = src.GetComponent<BoxCollider2D>();
        Check("Remove undo: the values are restored too (24x12, offset 3,-4, trigger)",
            restored != null && restored.Size == new Vector2(24, 12)
            && restored.Offset == new Vector2(3, -4) && restored.IsTrigger);

        state.Redo();
        scene.Update(0f);
        Check("Remove redo: it deletes the newly restored instance (no swinging at the old reference)",
            src.GetComponent<BoxCollider2D>() == null);

        {
            Runtime.Assets.AnimClipCache.ContentRoot =
                System.IO.Path.Combine(AppContext.BaseDirectory, "Content");
            var uScene = new Scene("UnpackTest");
            var rig = uScene.CreateEntity(Runtime.Core.Scene.PlayerName);
            var uInst = rig.AddComponent<Runtime.Components.SceneInstance>();
            uInst.SceneId = Gameplay.Systems.LevelSetup.PlayerPrefabId;
            uInst.Load();
            uScene.Update(0f);

            bool loadedOk = rig.GetComponent<Runtime.Components.Animator>() != null;
            Check("Unpack premise: the prefab loaded root-merged", loadedOk);

            var ustate = new EditorState { CurrentScene = uScene };
            ustate.ExecuteCommand(new UnpackSceneInstanceCommand(rig));
            uScene.Update(0f);

            Check("★ After unpacking, the host still has the prefab components (it is not a shell)",
                  rig.GetComponent<Runtime.Components.Animator>() != null
                  && rig.GetComponent<Runtime.Components.Rigidbody2D>() != null
                  && rig.GetComponent<Runtime.Components.CapsuleCollider2D>() != null
                  && rig.GetComponent<Gameplay.Player.PlayerController>() != null);
            Check("After unpacking the SceneInstance is gone",
                  rig.GetComponent<Runtime.Components.SceneInstance>() == null);

            var upPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pc_unpack.scene");
            SceneSerializer.SaveToFile(SceneSerializer.ToData(uScene), upPath);
            var upRaw = System.IO.File.ReadAllText(upPath);
            try { System.IO.File.Delete(upPath); } catch { }
            Check("★ After unpacking it saves as scene content (animator and rigidbody present)",
                  upRaw.Contains("\"animator\"") && upRaw.Contains("\"rigidbody\""));

            ustate.Undo();
            uScene.Update(0f);
            var back = rig.GetComponent<Runtime.Components.SceneInstance>();
            Check("Undo: the SceneInstance is restored", back != null);
            Check("★ Undo: the root-merged state is restored too (RootMerged)", back?.RootMerged == true);

            var reRaw = SceneSerializer.ToData(uScene);
            var hostComps = reRaw.Entities.Find(e => e.Name == Runtime.Core.Scene.PlayerName)?.Components;
            Check("★ After undo the save filters the prefab share out again (scene content only)",
                  hostComps != null
                  && !hostComps.Exists(c => c is AnimatorData)
                  && hostComps.Exists(c => c is SceneInstanceData));
            Check("No duplicate components after undo (one capsule)",
                  System.Linq.Enumerable.Count(rig.GetComponents<Runtime.Components.CapsuleCollider2D>()) == 1);
        }

        var animEnt = scene.CreateEntity("Anim");
        var anim = animEnt.AddComponent<Animator>();
        scene.Update(0f);
        Check("An Animator can now be copied (it is serialised)", ComponentClipboard.CanCopy(anim));
        Check("Copying puts the Animator on the clipboard",
            ComponentClipboard.Copy(anim) && ComponentClipboard.Type == typeof(Animator));

        var instEnt = scene.CreateEntity("Inst");
        var inst = instEnt.AddComponent<Runtime.Components.SceneInstance>();
        scene.Update(0f);
        Check("The copy lock remains on SceneInstance (child lifetimes cannot be carried)",
            !ComponentClipboard.CanCopy(inst));
    }

    private static void TestPivotSpread()
    {
        var atlas = new Runtime.Assets.SpriteAtlas();
        atlas.Slices.Add(new Runtime.Assets.SpriteSlice { Name = "f0", Width = 48, Height = 48, PivotX = 0.5f, PivotY = 0.9375f });
        atlas.Slices.Add(new Runtime.Assets.SpriteSlice { Name = "f1", Width = 48, Height = 48, PivotX = 0.5f, PivotY = 1f });
        atlas.Slices.Add(new Runtime.Assets.SpriteSlice { Name = "f2", Width = 48, Height = 48, PivotX = 0.25f, PivotY = 0.5f });
        atlas.Slices.Add(new Runtime.Assets.SpriteSlice { Name = "f3", Width = 48, Height = 48, PivotX = 0.5f, PivotY = 0.9375f });

        var source = atlas.Slices[0];

        Check("Pivot spread: it counts only the slices that need fixing (excluding equal ones and itself)",
            PivotSpread.CountDiffering(atlas, source) == 2);

        var cmds = PivotSpread.Build(atlas, source);
        Check($"Pivot spread: exactly as many commands as slices to fix ({cmds.Length})", cmds.Length == 2);

        var history = new CommandHistory();
        history.Execute(new CompositeCommand("Apply Pivot to Sheet", cmds));

        bool allSame = true;
        foreach (var s in atlas.Slices)
            if (Math.Abs(s.PivotX - 0.5f) > 0.0001f || Math.Abs(s.PivotY - 0.9375f) > 0.0001f) allSame = false;
        Check("Pivot spread: the whole sheet ends on the same pivot", allSame);

        Check("Pivot spread: undo is one step (24 would be unrecoverable)", history.CanUndo);
        history.Undo();
        Check("Pivot spread: one undo reverts everything", !history.CanUndo);

        Check("Pivot spread: undo returns each slice to its own old value (0.5/1)",
            Math.Abs(atlas.Slices[1].PivotX - 0.5f) < 0.0001f
            && Math.Abs(atlas.Slices[1].PivotY - 1f) < 0.0001f);
        Check("Pivot spread: undo returns each slice to its own old value (0.25/0.5)",
            Math.Abs(atlas.Slices[2].PivotX - 0.25f) < 0.0001f
            && Math.Abs(atlas.Slices[2].PivotY - 0.5f) < 0.0001f);

        foreach (var s in atlas.Slices) { s.PivotX = 0.5f; s.PivotY = 0.9375f; }
        Check("Pivot spread: nothing to do when they already match",
            PivotSpread.CountDiffering(atlas, source) == 0 && PivotSpread.Build(atlas, source).Length == 0);
    }

    private static void TestPlayLifetimeRewind()
    {
        var state = new EditorState { CurrentScene = new Scene("rewind") };
        var bb = new Blackboard();
        state.Blackboard = bb;

        bb.SetBool("editing.preexisting", true);

        state.SetMode(EditorMode.Play);

        bb.SetBool(Runtime.Cutscenes.CutsceneDirector.DoneKeyPrefix + "house.wakeUp", true);
        bb.SetInt(Runtime.Story.DialogueRunner.VisitKeyPrefix + "examine.house.bed", 2);
        Check("During play: the cutscene completion is recorded", bb.GetBool("cutscene.done.house.wakeUp"));

        state.SetMode(EditorMode.Edit);
        Check("★ Stop: the cutscene completion is rewound (it fires again on replay)",
            !bb.GetBool("cutscene.done.house.wakeUp"));
        Check("★ Stop: the visit count is rewound too (replay starts from the first visit)",
            bb.GetInt("story.visit.examine.house.bed") == 0);
        Check("Stop: values that existed during editing remain (play did not create them)",
            bb.GetBool("editing.preexisting"));

        state.SetMode(EditorMode.Play);
        bb.SetBool("cutscene.done.house.wakeUp", true);
        state.SetMode(EditorMode.Edit);
        Check("The second cycle is rewound as well", !bb.GetBool("cutscene.done.house.wakeUp"));

        var bare = new EditorState { CurrentScene = new Scene("bare") };
        var bb2 = new Blackboard();
        bb2.SetBool("game.progress", true);
        bare.SetMode(EditorMode.Play);
        bare.SetMode(EditorMode.Edit);
        Check("With no blackboard injected there is no rewind (the game path is unaffected)", bb2.GetBool("game.progress"));
    }

    private static void TestPlayLifetimeRewindRender()
    {
        var scene = new Scene("rewindRender");

        var door = scene.CreateEntity("Door");
        var doorSprite = door.AddComponent<SpriteRenderer>();
        doorSprite.SourceRect = new Rectangle(0, 0, 25, 36);
        var doorAnim = door.AddComponent<Animator>();
        doorAnim.AddClip(OneFrameClip("Door_Open", new Rectangle(75, 0, 25, 36)));

        var heroEntity = scene.CreateEntity("hero");
        var heroSprite = heroEntity.AddComponent<SpriteRenderer>();
        var heroAnim = heroEntity.AddComponent<Animator>();
        heroAnim.AddClip(OneFrameClip("Idle_D", new Rectangle(0, 0, 48, 48)));

        scene.Update(0f);

        heroAnim.Play("Idle_D");
        heroAnim.Update(1f);

        var state = new EditorState { CurrentScene = scene };
        Check("Premise: while editing, the door slot is empty", doorSprite.FrameOverride == null);
        Check("Premise: while editing, the character slot is full (DefaultClip is the art)", heroSprite.FrameOverride != null);

        state.SetMode(EditorMode.Play);

        doorAnim.Play("Door_Open");
        doorAnim.Update(1f);
        Check("During play: the door holds its opened frame",
              doorSprite.FrameOverride != null
              && doorSprite.EffectiveSourceRect == new Rectangle(75, 0, 25, 36));

        state.SetMode(EditorMode.Edit);

        Check("★ Stop: the door slot is emptied (it does not stay open)",
              doorSprite.FrameOverride == null);
        Check("★ Stop: the authored slice is visible again",
              doorSprite.EffectiveSourceRect == doorSprite.SourceRect);
        Check("★ Stop: the playback state is rewound too (the next play does not start holding the old clip)",
              doorAnim.CurrentClip == null && !doorAnim.IsPlaying);

        Check("★ Stop: the character slot is unchanged (forcing it would make the character invisible)",
              heroSprite.FrameOverride != null);
        Check("★ Stop: the character playback state is unchanged too",
              heroAnim.CurrentClip?.Name == "Idle_D");

        for (int i = 0; i < 5; i++) doorAnim.Update(0.1f);
        Check("★ Ticking after the stop does not reopen the door",
              doorSprite.FrameOverride == null);

        state.SetMode(EditorMode.Play);
        doorAnim.Play("Door_Open");
        doorAnim.Update(1f);
        state.SetMode(EditorMode.Edit);
        Check("The second cycle is rewound too", doorSprite.FrameOverride == null);
    }

    private static void TestRoomChangeMarksStop()
    {
        var scene = new Scene("roomflag");
        var cam = new Camera(320, 180);

        var prevImpl = Gameplay.Systems.RoomFlow.LoadRoomImpl;
        bool prevFlag = Gameplay.Systems.RoomFlow.RoomChangedDuringPlay;

        Gameplay.Systems.RoomFlow.RoomChangedDuringPlay = false;
        Gameplay.Systems.RoomFlow.LoadRoomImpl = _ => true;
        bool ok = Gameplay.Systems.RoomFlow.LoadImmediate("anywhere", "", scene, cam);
        Check("LoadImmediate: a successful loader returns success", ok);
        Check("★ Changing rooms raises the 'room changed during play' flag (without it, stopping lays the old room's snapshot on top)",
              Gameplay.Systems.RoomFlow.RoomChangedDuringPlay);

        Gameplay.Systems.RoomFlow.RoomChangedDuringPlay = false;
        Gameplay.Systems.RoomFlow.LoadRoomImpl = _ => false;
        bool failed = Gameplay.Systems.RoomFlow.LoadImmediate("deadbeef", "", scene, cam);
        Check("★ A failed load does not raise the flag (a false flag makes a perfectly good scene be reread from disk)",
              !failed && !Gameplay.Systems.RoomFlow.RoomChangedDuringPlay);

        Gameplay.Systems.RoomFlow.LoadRoomImpl = null;
        Check("With no loader injected it returns failure (not a silent success)",
              !Gameplay.Systems.RoomFlow.LoadImmediate("deadbeef", "", scene, cam));

        string prevSpawn = Gameplay.Systems.RoomFlow.LastSpawn;
        Gameplay.Systems.RoomFlow.LoadRoomImpl = _ => true;
        Gameplay.Systems.RoomFlow.LoadImmediate(Gameplay.Rooms.Village.SceneId, "Spawn_FromHouse", scene, cam);
        Check("The spawn name is recorded as the resume point (LastSpawn) - the save carries it",
              Gameplay.Systems.RoomFlow.LastSpawn == "Spawn_FromHouse");
        Gameplay.Systems.RoomFlow.LoadImmediate(Gameplay.Rooms.Village.SceneId, "", scene, cam);
        Check("★ An empty spawn overwrites the resume point = the contamination is real (the ground for c.GoToRoom's empty-spawn guard)",
              Gameplay.Systems.RoomFlow.LastSpawn == "");

        Gameplay.Systems.RoomFlow.LoadRoomImpl = prevImpl;
        Gameplay.Systems.RoomFlow.RoomChangedDuringPlay = prevFlag;
        Gameplay.Systems.RoomFlow.PlaceAtSpawn(scene, cam, prevSpawn);
    }

    private static Runtime.Animation.AnimationClip OneFrameClip(string name, Rectangle rect)
    {
        var c = new Runtime.Animation.AnimationClip(name) { Loop = false, SourceId = "fake-" + name };
        c.Frames.Add(new Runtime.Animation.AnimationFrame(rect, 0.1f));
        return c;
    }

    private static void TestAnimAtlasSwap()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pixelcore_anim_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var animPath = Path.Combine(dir, "Walk_D.anim");
            var seed = new Runtime.Animation.AnimationData { Id = "clip0001", Name = "Walk_D", Loop = true };
            seed.Frames.Add(new Runtime.Animation.FrameData { SliceIndex = 2, Duration = 0.1f });
            seed.Frames.Add(new Runtime.Animation.FrameData { SliceIndex = 5, Duration = 0.2f, EventName = "step_l" });
            seed.Save(animPath);

            var panel = new Panels.AnimationEditorPanel(null!);
            panel.Open(animPath);
            Check("Sheet swap: the .anim loaded (2 frames)", panel.SelfTestAnimation.Frames.Count == 2);

            var sheetA = Sheet("sheetAAA", "A.png", 8);
            panel.SetAtlas(sheetA, "Sprites/A.png", markEdited: false);
            Check("First sheet attach: markEdited=false leaves no dirty dot (so an empty new clip is never asked about saving)",
                !panel.IsDirty);
            Check("First sheet attach: AtlasId and AtlasPath are recorded",
                panel.SelfTestAnimation.AtlasId == "sheetAAA"
                && panel.SelfTestAnimation.AtlasPath == "Sprites/A.png");

            panel.SelfTestFrameBoundary();
            var sheetB = Sheet("sheetBBB", "B.png", 8);
            panel.SetAtlas(sheetB, "Sprites/B.png");

            Check("★ Swap: the clip id is preserved (zero reconnections for scene and prefab animators)",
                panel.SelfTestAnimation.Id == "clip0001");
            Check("★ Swap: the frames' cell numbers are preserved (on the same grid the new art plays with no reselection)",
                panel.SelfTestAnimation.Frames.Count == 2
                && panel.SelfTestAnimation.Frames[0].SliceIndex == 2
                && panel.SelfTestAnimation.Frames[1].SliceIndex == 5);
            Check("Swap: the frames' extras (duration, events) are preserved too",
                panel.SelfTestAnimation.Frames.Count == 2
                && Math.Abs(panel.SelfTestAnimation.Frames[1].Duration - 0.2f) < 1e-6f
                && panel.SelfTestAnimation.Frames[1].EventName == "step_l");
            Check("Swap: only AtlasId takes the new value",
                panel.SelfTestAnimation.AtlasId == "sheetBBB"
                && panel.SelfTestAnimation.AtlasPath == "Sprites/B.png");
            Check("Swap: the name and loop flag are unchanged",
                panel.SelfTestAnimation.Name == "Walk_D" && panel.SelfTestAnimation.Loop);
            Check("Swap: the unsaved dot", panel.IsDirty);
            Check("Swap: the slice list is the new sheet's too", panel.SelfTestAtlas == sheetB);

            panel.SelfTestUndo();
            Check("★ Undo: the sheet goes back to the previous one",
                panel.SelfTestAtlas == sheetA
                && panel.SelfTestAnimation.AtlasId == "sheetAAA"
                && panel.SelfTestAnimation.AtlasPath == "Sprites/A.png");
            Check("Undo: the frames are unchanged (the swap never touched them)",
                panel.SelfTestAnimation.Frames.Count == 2
                && panel.SelfTestAnimation.Frames[0].SliceIndex == 2);

            panel.SelfTestRedo();
            Check("Redo: the new sheet again",
                panel.SelfTestAtlas == sheetB && panel.SelfTestAnimation.AtlasId == "sheetBBB");

            panel.SelfTestSave();
            var reloaded = Runtime.Animation.AnimationData.Load(animPath)!;
            Check("Save: the file keeps the clip id", reloaded.Id == "clip0001");
            Check("Save: only the file's AtlasId is new", reloaded.AtlasId == "sheetBBB");
            Check("Save: the file's frames are preserved",
                reloaded.Frames.Count == 2 && reloaded.Frames[1].SliceIndex == 5);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch {  }
        }
    }

    private static Runtime.Assets.SpriteAtlas Sheet(string id, string texture, int slices)
    {
        var a = new Runtime.Assets.SpriteAtlas { Id = id, TexturePath = texture };
        for (int i = 0; i < slices; i++)
            a.Slices.Add(new Runtime.Assets.SpriteSlice { Name = $"sprite_{i}", X = i * 16, Width = 16, Height = 16 });
        return a;
    }

    private static void TestAtlasIdOnSave()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pixelcore_atlas_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        using var scope = Runtime.Assets.AssetRegistry.UseTemporary(Path.Combine(dir, "assets.json"));
        try
        {
            var panel = new Panels.SpriteEditorPanel(null!);
            var pngPath = Path.Combine(dir, "Sheet.png");
            var atlasPath = Path.ChangeExtension(pngPath, ".atlas");

            var fresh = new Runtime.Assets.SpriteAtlas { TexturePath = "Sheet.png" };
            Check("Premise: a newly created sheet has no id", fresh.Id.Length == 0);

            panel.SelfTestSaveAtlas(fresh, pngPath);

            Check("★ Save: the in-memory sheet was issued an id", fresh.Id.Length > 0);
            var onDisk = Runtime.Assets.SpriteAtlas.Load(atlasPath);
            Check("★ Save: that id is written into the file too (it does not wait for the boot scan)",
                onDisk != null && onDisk.Id.Length > 0 && onDisk.Id == fresh.Id);
            Check("Save: registered under that number (a .anim can map its path back to an id)",
                Runtime.Assets.AssetRegistry.Instance.GetPath(fresh.Id) == "Sheet.atlas");

            var id1 = fresh.Id;
            panel.SelfTestSaveAtlas(fresh, pngPath);
            Check("Resave: the same number is kept", id1.Length > 0 && fresh.Id == id1);

            var kept = new Runtime.Assets.SpriteAtlas { Id = "keepthis", TexturePath = "Other.png" };
            panel.SelfTestSaveAtlas(kept, Path.Combine(dir, "Other.png"));
            Check("An existing number is left as it is", kept.Id == "keepthis");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch {  }
        }
    }

    private static void TestClipIdOnSave()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pixelcore_clipid_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        using var scope = Runtime.Assets.AssetRegistry.UseTemporary(Path.Combine(dir, "assets.json"));
        var prevRoot = Runtime.Assets.AnimClipCache.ContentRoot;
        Panels.AnimationEditorPanel.SelfTestContentRoot = dir;
        try
        {
            Sheet("sheetAAA", "A.png", 8).Save(Path.Combine(dir, "A.atlas"));
            var seed = WriteClip(dir, "Seed", "clipSeed", "sheetAAA", "A.png", 0, 1);
            var reg = Runtime.Assets.AssetRegistry.Instance;
            reg.Register("sheetAAA", "A.atlas");
            reg.Register("clipSeed", "Seed.anim");

            Runtime.Assets.AnimClipCache.ContentRoot = dir;
            Runtime.Assets.AnimClipCache.Clear();

            Check("Premise: the name index is already built (1 seeded)",
                Runtime.Assets.AnimClipCache.FindByName("Seed").Count == 1);
            Check("Premise: WakeUp does not exist yet",
                Runtime.Assets.AnimClipCache.FindByName("WakeUp").Count == 0);

            var panel = new Panels.AnimationEditorPanel(null!);
            var (_, openLog) = CaptureConsole(() => { panel.Open(seed); return 0; });
            _ = openLog;
            panel.SelfTestAnimation.Name = "WakeUp";
            panel.SelfTestAnimation.Id = "";
            panel.SelfTestSave();

            var newId = panel.SelfTestAnimation.Id;
            Check("★ Save: the in-memory clip was issued an id", newId.Length > 0);
            var onDisk = Runtime.Animation.AnimationData.Load(Path.Combine(dir, "WakeUp.anim"));
            Check("★ Save: that id is written into the file too", onDisk != null && onDisk.Id == newId);
            Check($"★ Save: registered in the registry (it does not wait for the boot scan) - {reg.GetPath(newId) ?? "(none)"}",
                reg.GetPath(newId) == "WakeUp.anim");

            var found = Runtime.Assets.AnimClipCache.FindByName("WakeUp");
            Check($"★★ The global fallback finds the new clip without a reboot (registered plus the index invalidated) - {found.Count} matches",
                found.Count == 1 && found[0].Id == newId);

            panel.SelfTestSave();
            Check("Resave: the same number is kept", panel.SelfTestAnimation.Id == newId);
            Check("Resave: the registry is unchanged too", reg.GetPath(newId) == "WakeUp.anim");

            panel.SelfTestAnimation.Name = "Yawn";
            panel.SelfTestSave();
            Check($"Rename: the number is unchanged and only the path follows - {reg.GetPath(newId) ?? "(none)"}",
                panel.SelfTestAnimation.Id == newId && reg.GetPath(newId) == "Yawn.anim");
            Check($"★ Rename: the old path no longer points at that number - {reg.GetId("WakeUp.anim") ?? "(null)"}",
                reg.GetId("WakeUp.anim") == null);
        }
        finally
        {
            Panels.AnimationEditorPanel.SelfTestContentRoot = null;
            Runtime.Assets.AnimClipCache.Clear();
            Runtime.Assets.AnimClipCache.ContentRoot = prevRoot;
            try { Directory.Delete(dir, true); } catch {  }
        }
    }

    private static void TestClipNameIsFileName()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pixelcore_animname_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        using var scope = Runtime.Assets.AssetRegistry.UseTemporary(Path.Combine(dir, "assets.json"));
        var prevRoot = Runtime.Assets.AnimClipCache.ContentRoot;
        try
        {
            Sheet("sheetAAA", "A.png", 8).Save(Path.Combine(dir, "A.atlas"));
            var reg = Runtime.Assets.AssetRegistry.Instance;
            reg.Register("sheetAAA", "A.atlas");
            Runtime.Assets.AnimClipCache.ContentRoot = dir;

            var written = WriteClip(dir, "Walk_D", "clipW001", "sheetAAA", "A.png", 2, 3);
            var renamed = Path.Combine(dir, "Test.anim");
            File.Move(written, renamed);
            var (loaded, log) = CaptureConsole(() => Runtime.Animation.AnimationData.Load(renamed));

            Check($"(a) ★ Loading uses the file name as the name - '{loaded?.Name}'", loaded?.Name == "Test");
            Check("(a) The id, atlas and frames are unchanged (references hang off the id)",
                loaded?.Id == "clipW001" && loaded.AtlasId == "sheetAAA" && loaded.Frames.Count == 2);
            Check("(a) It reports that the in-file name is stale (it does not swap silently)",
                log.Contains("Walk_D") && log.Contains("Test"));
            var (_, log2) = CaptureConsole(() => Runtime.Animation.AnimationData.Load(renamed));
            Check("(a) It does not complain twice about the same file (loading runs every time a scene opens)",
                !log2.Contains("in-file name"));

            reg.Register("clipW001", "Walk_D.anim");
            Runtime.Assets.AnimClipCache.Clear();
            Check("(b) Premise: an index built from the old path gives the old name",
                Runtime.Assets.AnimClipCache.FindByName("Walk_D").Count == 1);

            reg.UpdatePath("clipW001", "Test.anim");
            Runtime.Assets.AnimClipCache.Clear();
            var found = Runtime.Assets.AnimClipCache.FindByName("Test");
            Check("(b) ★ It is found under the new name", found.Count == 1 && found[0].Id == "clipW001");
            Check("(b) ★ The old name is gone",
                Runtime.Assets.AnimClipCache.FindByName("Walk_D").Count == 0);
            var clip = Runtime.Assets.AnimClipCache.Get("clipW001");
            Check($"(b) The clip the cache builds is named after the file too - '{clip?.Name}'", clip?.Name == "Test");

            File.WriteAllText(Path.Combine(dir, "Broken.anim"), "{ this is not JSON");
            reg.Register("clipBrok", "Broken.anim");
            Runtime.Assets.AnimClipCache.Clear();
            var (broken, buildLog) = CaptureConsole(() =>
                (Runtime.Assets.AnimClipCache.FindByName("Broken").Count,
                 Runtime.Assets.AnimClipCache.FindByName("Test").Count));
            Check("(c) ★ The index stands with a broken file in the mix", broken.Item2 == 1);
            Check("(c) ★ The broken file is still found by name (only the path was read)", broken.Item1 == 1);
            Check("(c) ★ Building the index opens no files - no parse failure happens here",
                !buildLog.Contains("Load failed"));
            var (dead, getLog) = CaptureConsole(() => Runtime.Assets.AnimClipCache.Get("clipBrok"));
            Check("(c) It complains WHEN PLAYED instead (diagnostics gather in one place)",
                dead == null && getLog.Contains("Load failed"));
        }
        finally
        {
            Runtime.Assets.AnimClipCache.Clear();
            Runtime.Assets.AnimClipCache.ContentRoot = prevRoot;
            try { Directory.Delete(dir, true); } catch {  }
        }
    }

    private static void TestBulkRetarget()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pixelcore_bulk_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        Panels.AnimationEditorPanel.SelfTestContentRoot = dir;
        try
        {
            var open = WriteClip(dir, "Walk_D", "clipD001", "sheetAAA", "Sprites/A.png", 2, 5);
            var sibA = WriteClip(dir, "Walk_L", "clipL001", "sheetAAA", "Sprites/A.png", 1, 3);
            var sibB = WriteClip(dir, "Walk_R", "clipR001", "sheetAAA", "Sprites/A.png", 4, 6);
            var other = WriteClip(dir, "Door", "clipX001", "otherXXX", "Sprites/Door.png", 0, 1);

            string sibABefore = File.ReadAllText(sibA);
            string otherBefore = File.ReadAllText(other);

            var panel = new Panels.AnimationEditorPanel(null!);
            panel.Open(open);
            Check("Premise: the clip just opened is clean (no dot)", !panel.IsDirty);

            panel.SelfTestFrameBoundary();
            panel.SetAtlas(Sheet("sheetBBB", "B.png", 8), "Sprites/B.png");

            Check("★ Bulk: it selects the 2 siblings on the same old sheet (excluding the open clip and the unrelated one)",
                panel.SelfTestPendingBulkCount == 2);

            panel.SelfTestCancelBulk();
            Check("★ Cancel: the sheet returns to what it was before the swap",
                panel.SelfTestAnimation.AtlasId == "sheetAAA"
                && panel.SelfTestAnimation.AtlasPath == "Sprites/A.png");
            Check("★ Cancel: the sibling files are unchanged byte for byte", File.ReadAllText(sibA) == sibABefore);
            Check("★ Cancel: the unsaved dot returns to its pre-swap state too - 'never happened' must not prompt to save",
                !panel.IsDirty);
            Check("Cancel: the pending question disappears", panel.SelfTestPendingBulkCount == 0);

            panel.SelfTestRedo();
            Check("Cancel: Cmd+Shift+Z can 'cancel the cancel' (the swap stays on the redo stack)",
                panel.SelfTestAnimation.AtlasId == "sheetBBB");
            Check("Cancel: redo does not raise the sibling dialogue again (going back must not ask a question)",
                panel.SelfTestPendingBulkCount == 0);
            panel.SelfTestUndo();
            Check("Premise: back on the old sheet", panel.SelfTestAnimation.AtlasId == "sheetAAA");

            panel.SelfTestFrameBoundary();
            panel.SetAtlas(Sheet("sheetBBB", "B.png", 8), "Sprites/B.png");
            panel.SelfTestKeepOpenClipOnly();
            Check("★ Only this clip: the sibling files are unchanged byte for byte (the confirmation must not be decoration)",
                File.ReadAllText(sibA) == sibABefore);
            Check("Only this clip: the open clip keeps the new sheet", panel.SelfTestAnimation.AtlasId == "sheetBBB");
            Check("Only this clip: the unsaved dot stays (the swap is a live edit)", panel.IsDirty);

            panel.SelfTestFrameBoundary();
            panel.SetAtlas(Sheet("sheetAAA", "A.png", 8), "Sprites/A.png");
            panel.SelfTestFrameBoundary();
            panel.SetAtlas(Sheet("sheetBBB", "B.png", 8), "Sprites/B.png");
            Check("Bulk: swapping again selects the same 2", panel.SelfTestPendingBulkCount == 2);
            panel.SelfTestFrameBoundary();
            panel.SetAtlas(Sheet("sheetAAA", "A.png", 8), "Sprites/A.png");
            Check("★ Back to the start (A to B to A): the pending question folds itself away (the reference did not change after all)",
                panel.SelfTestPendingBulkCount == 0);

            panel.SelfTestFrameBoundary();
            panel.SetAtlas(Sheet("sheetBBB", "B.png", 8), "Sprites/B.png");
            Check("Bulk: the same 2 are still selected right before confirming", panel.SelfTestPendingBulkCount == 2);

            panel.SelfTestApplyBulk();

            var afterA = Runtime.Animation.AnimationData.Load(sibA)!;
            var afterB = Runtime.Animation.AnimationData.Load(sibB)!;
            Check("★ Bulk: the siblings point at the new sheet",
                afterA.AtlasId == "sheetBBB" && afterB.AtlasId == "sheetBBB");
            Check("Bulk: AtlasPath follows too", afterA.AtlasPath == "Sprites/B.png");
            Check("★ Bulk: SliceIndex is untouched (a frame is an integer cell number)",
                afterA.Frames.Count == 2 && afterA.Frames[0].SliceIndex == 1 && afterA.Frames[1].SliceIndex == 3
                && afterB.Frames[0].SliceIndex == 4 && afterB.Frames[1].SliceIndex == 6);
            Check("Bulk: the clip ids are preserved (scene and prefab references stay attached)",
                afterA.Id == "clipL001" && afterB.Id == "clipR001");
            Check("★ Bulk: a clip on an unrelated sheet is not touched at all (bytes identical)",
                File.ReadAllText(other) == otherBefore);

            var beforeLines = sibABefore.Replace("\r\n", "\n").Split('\n');
            var afterLines = File.ReadAllText(sibA).Replace("\r\n", "\n").Split('\n');
            Check("★ Bulk: the line count does not change (no field materialisation)", beforeLines.Length == afterLines.Length);
            int diff = 0;
            for (int i = 0; i < Math.Min(beforeLines.Length, afterLines.Length); i++)
                if (beforeLines[i] != afterLines[i]) diff++;
            Check("★ Bulk: exactly 2 lines differ (AtlasId and AtlasPath)", diff == 2);

            int again = AtlasRetarget.Retarget(new[] { sibA, sibB }, "sheetBBB", "Sprites/B.png");
            Check("Bulk: already on that sheet, nothing is rewritten (0 changes)", again == 0);

            Check("★ Bulk: the open clip itself is not written to disk (saving is the user's call - it is undoable)",
                Runtime.Animation.AnimationData.Load(open)!.AtlasId == "sheetAAA");

            panel.SelfTestSave();

            Check("Selection: once saved, not one clip uses the old sheet (one swap covered them all)",
                AtlasRetarget.FindClipsUsing(dir, "sheetAAA", null).Count == 0);
            Check("Selection: 3 clips use the new sheet (the open one plus 2 siblings)",
                AtlasRetarget.FindClipsUsing(dir, "sheetBBB", null).Count == 3);
            Check("Selection: the file passed as except is left out",
                AtlasRetarget.FindClipsUsing(dir, "sheetBBB", open).Count == 2);
            Check("Selection: an empty id selects nobody (it does not scrape the path fallback)",
                AtlasRetarget.FindClipsUsing(dir, "", null).Count == 0);

            panel.SelfTestFrameBoundary();
            panel.SetAtlas(Sheet("sheetCCC", "C.png", 8), "Sprites/C.png");
            Check("Premise: the pending question is up again", panel.SelfTestPendingBulkCount == 2);
            panel.SelfTestMarkBulkPopupOpen();
            panel.SelfTestKeepOpenClipOnly();
            Check("★ Exit: the popup flag is folded (otherwise the next dialogue cancels itself)",
                !panel.SelfTestBulkPopupOpen);

            panel.SelfTestFrameBoundary();
            panel.SetAtlas(Sheet("sheetBBB", "B.png", 8), "Sprites/B.png");
            panel.SelfTestFrameBoundary();
            panel.SetAtlas(Sheet("sheetCCC", "C.png", 8), "Sprites/C.png");
            Check("Premise: the pending question is up once more", panel.SelfTestPendingBulkCount == 2);
            panel.Open(sibA);
            Check("★ Document switch: the pending question folds (a cancel must not revert the wrong document)",
                panel.SelfTestPendingBulkCount == 0);
        }
        finally
        {
            Panels.AnimationEditorPanel.SelfTestContentRoot = null;
            try { Directory.Delete(dir, true); } catch {  }
        }
    }

    private static void TestClipListByFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pixelcore_cliplist_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var open = WriteClip(dir, "Walk_D", "c1", "sheetAAA", "Sprites/A.png", 0, 1);
            WriteClip(dir, "Walk_L", "c2", "sheetBBB", "Sprites/B.png", 0, 1);
            WriteClip(dir, "Idle_D", "c3", "sheetCCC", "Sprites/C.png", 0, 1);
            var sub = Path.Combine(dir, "Sub");
            Directory.CreateDirectory(sub);
            WriteClip(sub, "Other", "c4", "sheetAAA", "Sprites/A.png", 0, 1);

            var panel = new Panels.AnimationEditorPanel(null!);
            panel.Open(open);

            Check("★ Switcher: it shows every clip in the folder (3, despite different sheets)",
                panel.SelfTestClipList().Count == 3);
            Check("Switcher: subfolders are not scanned (a character list must not become the project list)",
                !panel.SelfTestClipList().Exists(c => c.Name == "Other"));

            panel.SelfTestFrameBoundary();
            panel.SetAtlas(Sheet("sheetZZZ", "Z.png", 8), "Sprites/Z.png");
            panel.SelfTestKeepOpenClipOnly();
            Check("★ Switcher: swapping the sheet keeps all 3 (it used to shrink to 1)",
                panel.SelfTestClipList().Count == 3);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch {  }
        }
    }

    private static void TestOutOfRangeWarning()
    {
        var data = new Runtime.Animation.AnimationData { Id = "clipW001", Name = "Walk_D" };
        data.Frames.Add(new Runtime.Animation.FrameData { SliceIndex = 0 });
        data.Frames.Add(new Runtime.Animation.FrameData { SliceIndex = 1 });
        data.Frames.Add(new Runtime.Animation.FrameData { SliceIndex = 5 });

        var small = Sheet("sheetSmall", "S.png", 3);
        var (clip, log) = CaptureConsole(() => data.ToClip(small));

        Check("Out of range: the clip is built with the frame dropped (3 to 2)", clip.Frames.Count == 2);
        Check("★ Out of range: a warning is printed (no silent vanishing)", log.Contains("fall outside the atlas slice count"));
        Check("Out of range: the warning carries the clip name and the cell count", log.Contains("Walk_D") && log.Contains("(3)"));

        var big = Sheet("sheetBig", "B.png", 8);
        var (clip2, log2) = CaptureConsole(() => data.ToClip(big));
        Check("In range: all 3 frames survive", clip2.Frames.Count == 3);
        Check("In range: no warning is printed (a healthy clip complaining every time turns warnings into noise)",
            !log2.Contains("fall outside the atlas slice count"));
    }

    private static void TestContentRootShape()
    {
        Console.WriteLine("--- The shape of the editor Content root ---");

        Check("★ The editor root is an absolute path (the premise of the prefix arithmetic - breaking this caused the regression)",
              Path.IsPathRooted(EditorApp.ContentRoot), EditorApp.ContentRoot);
        Check("The root has no trailing separator (so the prefix computation is not off by one)",
              !EditorApp.ContentRoot.EndsWith("/"), EditorApp.ContentRoot);
        Check("Premise: that root exists", Directory.Exists(EditorApp.ContentRoot));

        Check("★ ScenesDir is absolute too (it is derived from ContentRoot and breaks with it)",
              Path.IsPathRooted(Path.Combine(EditorApp.ContentRoot, "Scenes")));

        var abs = Path.GetFullPath(Path.Combine(Runtime.Assets.ContentPaths.Root, "Scenes", "Village.scene"));
        Check("Premise: Village.scene exists (the subject of the round trip below)", File.Exists(abs), abs);

        var rel = EditorApp.ToContentRelative(abs);
        Check("★ Absolute to Content-relative (no parent folder mixed in)",
              rel == "Scenes/Village.scene", rel);
        Check("★ The result has no parent segments", !rel.Contains("..") && !rel.StartsWith("/"), rel);

        var noisy = Path.Combine(Runtime.Assets.ContentPaths.Root, "Scenes", "..", "Scenes", "Village.scene");
        Check("★ The query path is normalised (duplicate segments give the same answer)",
              EditorApp.ToContentRelative(noisy) == "Scenes/Village.scene",
              EditorApp.ToContentRelative(noisy));

        var sibling = EditorApp.ContentRoot + "Backup/x.png";
        Check("★ A sibling folder (ContentBackup/) is not read as being inside Content",
              EditorApp.ToContentRelative(sibling) == "x.png", EditorApp.ToContentRelative(sibling));
    }

    private static void TestDefaultClipApplies()
    {
        Console.WriteLine("--- DefaultClip application ---");

        var e = new Entity("Actor");
        e.AddComponent<Transform>();
        var r = e.AddComponent<SpriteRenderer>();
        var anim = e.AddComponent<Animator>();

        var idle = new AnimationClip("Idle_D");
        idle.AddFrame(new AnimationFrame(new Rectangle(0, 0, 8, 8), 0.1f));
        var walk = new AnimationClip("Walk_D");
        walk.AddFrame(new AnimationFrame(new Rectangle(8, 0, 8, 8), 0.1f));
        anim.AddClip(idle);
        anim.AddClip(walk);

        Check("Initial state: the slot is empty", r.FrameOverride == null);

        AnimatorClipsState.SetDefault(anim, "Idle_D");
        Check("★ Picking a default clip fills the screen slot at once (no scene reopen)",
            r.FrameOverride != null);
        Check("★ It is that clip's frame",
            r.FrameOverride?.SourceRect == new Rectangle(0, 0, 8, 8),
            r.FrameOverride?.SourceRect.ToString() ?? "(null)");

        AnimatorClipsState.SetDefault(anim, "Walk_D");
        Check("★ Switching to another clip switches the frame",
            r.FrameOverride?.SourceRect == new Rectangle(8, 0, 8, 8),
            r.FrameOverride?.SourceRect.ToString() ?? "(null)");

        AnimatorClipsState.SetDefault(anim, null);
        Check("★ Clearing it to (none) empties the slot and shows the authored slice",
            r.FrameOverride == null);

        anim.Update(0.016f);
        Check("★★ Still empty after a tick (pressing play does not revive it)",
            r.FrameOverride == null, r.FrameOverride?.SourceRect.ToString() ?? "(null)");
        Check("  the playback state was released too", !anim.IsPlaying && anim.CurrentClip == null);

        AnimatorClipsState.SetDefault(anim, "Idle_D");
        var snap = AnimatorClipsState.Capture(anim);

        AnimatorClipsState.SetDefault(anim, "Walk_D");
        AnimatorClipsState.Restore(anim, snap);
        Check("★ Undo restores the screen too (three doors, one application)",
            r.FrameOverride?.SourceRect == new Rectangle(0, 0, 8, 8),
            r.FrameOverride?.SourceRect.ToString() ?? "(null)");

        AnimatorClipsState.SetDefault(anim, "Idle_D");
        var beforeRemove = AnimatorClipsState.Capture(anim);

        AnimatorClipsState.Remove(anim, "Idle_D");
        var afterRemove = AnimatorClipsState.Capture(anim);
        bool doScreen = r.FrameOverride == null;
        Check("★★ Removing the default clip returns the screen to the authored art (do)",
            doScreen, r.FrameOverride?.SourceRect.ToString() ?? "(null)");

        AnimatorClipsState.Restore(anim, beforeRemove);
        Check("  undo brings that frame back",
            r.FrameOverride?.SourceRect == new Rectangle(0, 0, 8, 8),
            r.FrameOverride?.SourceRect.ToString() ?? "(null)");

        AnimatorClipsState.Restore(anim, afterRemove);
        Check("★★ Do and redo produce THE SAME SCREEN (it is the same state transition)",
            (r.FrameOverride == null) == doScreen,
            $"do={(doScreen ? "empty" : "kept")} redo={(r.FrameOverride == null ? "empty" : "kept")}");
    }

    private static void TestDeleteGuards()
    {
        Console.WriteLine("--- File deletion guards ---");

        var held = new System.Collections.Generic.List<(string Path, string What)>
        {
            ("/x/Content/Scenes/Village.scene", "open scene"),
            ("/x/Content/Sprites/A.png", "open in the sprite editor"),
        };

        Check("★ An open scene is refused", EditorApp.OpenDocReason("/x/Content/Scenes/Village.scene", held) == "open scene");
        Check("★ An open sprite is refused too",
              EditorApp.OpenDocReason("/x/Content/Sprites/A.png", held) == "open in the sprite editor");
        Check("★ A query with Windows separators is seen as the same file",
              EditorApp.OpenDocReason("\\x\\Content\\Sprites\\A.png", held) != null);
        Check("★ A held path with Windows separators is seen as the same file",
              EditorApp.OpenDocReason("/x/Content/Sprites/C.png",
                  new System.Collections.Generic.List<(string Path, string What)>
                  { ("\\x\\Content\\Sprites\\C.png", "open in the sprite editor") }) != null);
        Check("A file that is not open passes (the guard is not a blanket refusal)",
              EditorApp.OpenDocReason("/x/Content/Sprites/B.png", held) == null);

        var dir = Path.Combine(Path.GetTempPath(), "pixelcore_del_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var prevRoot = Runtime.Assets.AnimClipCache.ContentRoot;
        try
        {
            var png = Path.Combine(dir, "Sheet.png");
            var atlas = Path.Combine(dir, "Sheet.atlas");
            File.WriteAllText(png, "x");
            File.WriteAllText(atlas, "{}");

            var seen = new System.Collections.Generic.List<Runtime.Assets.AssetChangeKind>();
            void OnChange(Runtime.Assets.AssetChange c) => seen.Add(c.Kind);
            Runtime.Assets.AssetEvents.Changed += OnChange;
            int ok;
            try { ok = EditorApp.DeleteAssetsConfirmed(new[] { png }); }
            finally { Runtime.Assets.AssetEvents.Changed -= OnChange; }

            Check("Premise: one deletion succeeded", ok == 1, ok.ToString());
            Check("★ The png is gone", !File.Exists(png));
            Check("★ The .atlas sidecar went with it (being hidden, one left behind could never be deleted)",
                  !File.Exists(atlas));
            Check("★ A delete signal goes out - folder deletion raised one while file deletion did not, "
                + "so a deleted .anim survived in the cache",
                  seen.Contains(Runtime.Assets.AssetChangeKind.Deleted), string.Join(",", seen));
        }
        finally
        {
            Runtime.Assets.AnimClipCache.ContentRoot = prevRoot;
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static void TestAssetEvents()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pixelcore_evt_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        using var scope = Runtime.Assets.AssetRegistry.UseTemporary(Path.Combine(dir, "assets.json"));
        var prevAnimRoot = Runtime.Assets.AnimClipCache.ContentRoot;
        var prevSurfRoot = Runtime.Audio.SurfaceLibrary.ContentRoot;
        try
        {
            Runtime.Assets.AnimClipCache.ContentRoot = dir;
            Runtime.Audio.SurfaceLibrary.ContentRoot = dir;
            var reg = Runtime.Assets.AssetRegistry.Instance;

            Sheet("sheetEEE", "E.png", 8).Save(Path.Combine(dir, "E.atlas"));
            reg.Register("sheetEEE", "E.atlas");
            WriteClip(dir, "Walk_D", "clipE001", "sheetEEE", "E.png", 2, 3);
            reg.Register("clipE001", "Walk_D.anim");
            Runtime.Assets.AnimClipCache.Clear();

            Check("Premise: the index knows the old name",
                Runtime.Assets.AnimClipCache.FindByName("Walk_D").Count == 1);

            File.Move(Path.Combine(dir, "Walk_D.anim"), Path.Combine(dir, "Sprint.anim"));
            reg.UpdatePath("clipE001", "Sprint.anim");
            Runtime.Assets.AssetEvents.RaiseRenamed("Walk_D.anim", "Sprint.anim");

            Check("1. ★ The signal alone finds the new name (a mutation point never names a cache)",
                Runtime.Assets.AnimClipCache.FindByName("Sprint").Count == 1);
            Check("1. ★ The old name is gone", Runtime.Assets.AnimClipCache.FindByName("Walk_D").Count == 0);

            Runtime.Assets.AssetEvents.RaiseSaved("unwatched.txt");
            Check("2. Raising for an extension with no subscriber passes without an exception",
                Runtime.Assets.AnimClipCache.FindByName("Sprint").Count == 1);

            var surfPath = Path.Combine(dir, "Test.surface");
            WriteSurface(surfPath, "surfE001", 0.11f);
            reg.Register("surfE001", "Test.surface");
            Runtime.Audio.SurfaceLibrary.Clear();
            Check("3. Premise: the first value is read",
                Runtime.Audio.SurfaceLibrary.Get("surfE001")?.Volume == 0.11f);

            WriteSurface(surfPath, "surfE001", 0.77f);
            Check("3. Control: before the signal it is the old value (the cache really is holding on)",
                Runtime.Audio.SurfaceLibrary.Get("surfE001")?.Volume == 0.11f);

            Runtime.Assets.AssetEvents.RaiseSaved("Test.surface");
            Check($"3. ★ After the signal the new value is read ({Runtime.Audio.SurfaceLibrary.Get("surfE001")?.Volume})",
                Runtime.Audio.SurfaceLibrary.Get("surfE001")?.Volume == 0.77f);

            WriteSurface(surfPath, "surfE001", 0.44f);
            Runtime.Assets.AssetEvents.RaiseSaved("anything.png");
            Check($"4. ★ An unrelated extension does not clear another cache - the filter is alive "
                + $"({Runtime.Audio.SurfaceLibrary.Get("surfE001")?.Volume})",
                Runtime.Audio.SurfaceLibrary.Get("surfE001")?.Volume == 0.77f);

            Runtime.Assets.AssetEvents.RaiseDeleted("Sprites/SomeFolder");
            Check($"5. ★ A signal with no extension (folder move, delete, rescan) clears everything - when in doubt, clearing is cheap "
                + $"({Runtime.Audio.SurfaceLibrary.Get("surfE001")?.Volume})",
                Runtime.Audio.SurfaceLibrary.Get("surfE001")?.Volume == 0.44f);
        }
        finally
        {
            Runtime.Assets.AnimClipCache.ContentRoot = prevAnimRoot;
            Runtime.Audio.SurfaceLibrary.ContentRoot = prevSurfRoot;
            Runtime.Assets.AnimClipCache.Clear();
            Runtime.Audio.SurfaceLibrary.Clear();
            try { Directory.Delete(dir, true); } catch {  }
        }
    }

    private static void WriteSurface(string path, string id, float volume)
        => new Runtime.Audio.SurfaceAsset { Id = id, Name = "Test", Volume = volume }.Save(path);

    private static (T Result, string Log) CaptureConsole<T>(Func<T> body)
    {
        var prev = Console.Out;
        var buf = new System.IO.StringWriter();
        try { Console.SetOut(buf); return (body(), buf.ToString()); }
        finally { Console.SetOut(prev); }
    }

    private static string WriteClip(string dir, string name, string id, string atlasId, string atlasPath,
                                    int slice0, int slice1)
    {
        var data = new Runtime.Animation.AnimationData { Id = id, Name = name, AtlasId = atlasId, AtlasPath = atlasPath };
        data.Frames.Add(new Runtime.Animation.FrameData { SliceIndex = slice0, Duration = 0.1f });
        data.Frames.Add(new Runtime.Animation.FrameData { SliceIndex = slice1, Duration = 0.1f });
        var path = Path.Combine(dir, name + ".anim");
        data.Save(path);
        return path;
    }

    private static void TestBulkCacheInvalidation()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pixelcore_cache_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        using var scope = Runtime.Assets.AssetRegistry.UseTemporary(Path.Combine(dir, "assets.json"));
        var prevRoot = Runtime.Assets.AnimClipCache.ContentRoot;
        Panels.AnimationEditorPanel.SelfTestContentRoot = dir;
        try
        {
            SheetWithCell("sheetAAA", 16).Save(Path.Combine(dir, "A.atlas"));
            SheetWithCell("sheetBBB", 32).Save(Path.Combine(dir, "B.atlas"));
            var cached = WriteClip(dir, "Walk_D", "clipC001", "sheetAAA", "A.png", 2, 2);
            var edited = WriteClip(dir, "Walk_L", "clipE001", "sheetAAA", "A.png", 0, 1);

            var reg = Runtime.Assets.AssetRegistry.Instance;
            reg.Register("sheetAAA", "A.atlas");
            reg.Register("sheetBBB", "B.atlas");
            reg.Register("clipC001", "Walk_D.anim");
            reg.Register("clipE001", "Walk_L.anim");

            Runtime.Assets.AnimClipCache.ContentRoot = dir;
            Runtime.Assets.AnimClipCache.Clear();

            var (before, _) = CaptureConsole(() => Runtime.Assets.AnimClipCache.Get("clipC001"));
            Check("Cache: before the swap the clip uses the old sheet's cell (a 16px grid gives x=32)",
                before != null && before.Frames.Count == 2 && before.Frames[0].SourceRect.X == 32);

            var panel = new Panels.AnimationEditorPanel(null!);
            panel.Open(edited);
            panel.SelfTestFrameBoundary();
            panel.SetAtlas(Sheet("sheetBBB", "B.png", 8), "B.png");
            Check("Cache: one sibling is selected", panel.SelfTestPendingBulkCount == 1);
            panel.SelfTestApplyBulk();

            var (after, _) = CaptureConsole(() => Runtime.Assets.AnimClipCache.Get("clipC001"));
            Check("★ Cache: after the bulk retarget the new sheet's cell appears (a 32px grid gives x=64) - the cache was cleared",
                after != null && after.Frames.Count == 2 && after.Frames[0].SourceRect.X == 64);
        }
        finally
        {
            Panels.AnimationEditorPanel.SelfTestContentRoot = null;
            Runtime.Assets.AnimClipCache.Clear();
            Runtime.Assets.AnimClipCache.ContentRoot = prevRoot;
            try { Directory.Delete(dir, true); } catch {  }
        }
    }

    private static Runtime.Assets.SpriteAtlas SheetWithCell(string id, int cell)
    {
        var a = new Runtime.Assets.SpriteAtlas { Id = id, TexturePath = id + ".png", CellWidth = cell, CellHeight = cell };
        for (int i = 0; i < 8; i++)
            a.Slices.Add(new Runtime.Assets.SpriteSlice { Name = $"sprite_{i}", X = i * cell, Width = cell, Height = cell });
        return a;
    }

    private static void Check(string name, bool ok, string? detail = null)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name + (!ok && !string.IsNullOrEmpty(detail) ? $" — {detail}" : ""));
        if (ok) _pass++; else _fail++;
    }
    private static void TestPrefabFileIsCurrentSchema()
    {
        var scene = new Scene("PrefabSchema");
        var root = scene.CreateEntity("Body");
        root.GetComponent<Transform>()!.Position = new Microsoft.Xna.Framework.Vector2(100, 200);
        var sr = root.AddComponent<SpriteRenderer>();
        sr.DrawSize = new Microsoft.Xna.Framework.Vector2(48, 64);
        scene.Update(0f);

        var dir = Path.Combine(Path.GetTempPath(), "pixelcore_prefabver_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "MadePrefab.scene");
        try
        {
            using var reg = PixelCore.Runtime.Assets.AssetRegistry.UseTemporary(Path.Combine(dir, "assets.json"));
            new ConvertToPrefabCommand(scene, root, file).Execute();

            var data = SceneSerializer.LoadFromFile(file);
            Check("Premise: the prefab file really was written", data != null && data.Entities.Count > 0);
            var capturedSprite = data?.Entities[0].Components.Find(c => c is SpriteData) as SpriteData;
            Check($"Premise: the art size made it into the file (drawW/H = {capturedSprite?.DrawW}x{capturedSprite?.DrawH})",
                  capturedSprite?.DrawW == 48f && capturedSprite?.DrawH == 64f);
            Check($"★ A new prefab is on the latest schema (actually {data?.SchemaVersion}) - at v1 the art drops half a height on reopening",
                  data?.SchemaVersion == SceneData.CurrentSchemaVersion);

            var reopened = new Scene("Reopen");
            SceneSerializer.FromData(reopened, data!);
            var back = reopened.FindEntity("Body");
            Check("★ So reopening leaves the coordinates unchanged (normalised 0,0)",
                  back?.GetComponent<Transform>()?.Position == Microsoft.Xna.Framework.Vector2.Zero);

            data!.SchemaVersion = 1;
            var moved = new Scene("ReopenV1");
            SceneSerializer.FromData(moved, data);
            var m = moved.FindEntity("Body")?.GetComponent<Transform>()?.Position;
            Check($"★ Control: at v1 it really does move (0 to {m?.Y}, half the art height of 64) - proof this check knows how to fail",
                  m.HasValue && m.Value.Y == 32f);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static void TestCommandIdentity()
    {
        Console.WriteLine("--- Command identity ---");

        {
            var state = new EditorState();
            var scene = new Scene("identity");
            state.CurrentScene = scene;

            var a = scene.CreateEntity("A");
            a.GetComponent<Transform>()!.Position = new Vector2(10, 20);
            scene.Update(0f);
            int idBefore = a.Id;

            state.ExecuteCommand(new MoveEntityCommand(a, new Vector2(10, 20), new Vector2(99, 77)));
            Check("Premise: the move really took effect (99,77)",
                a.GetComponent<Transform>()!.Position == new Vector2(99, 77));

            state.ExecuteCommand(new DeleteEntityCommand(scene, a, state));
            scene.Update(0f);
            Check("Premise: it was deleted", scene.FindEntity("A") == null);

            state.Undo();
            scene.Update(0f);
            var a2 = scene.FindEntity("A");
            Check("Premise: it was revived", a2 != null);
            Check("★ Premise: a revival creates a new object - if it were the same object everything below would be vacuous",
                a2 != null && !ReferenceEquals(a2, a));
            Check($"★ The revived entity's runtime Id equals the original (actual {a2?.Id} / original {idBefore})",
                a2 != null && a2.Id == idBefore);

            state.Undo();
            scene.Update(0f);
            Check($"★ move, delete, undo, undo = back in place (actual {a2?.GetComponent<Transform>()?.Position})",
                a2?.GetComponent<Transform>()?.Position == new Vector2(10, 20));

            var fresh = scene.CreateEntity("Fresh");
            Check($"★ The next entity does not receive the revived number again (revived {a2?.Id} < new {fresh.Id})",
                a2 != null && fresh.Id > a2.Id);
        }

        {
            var state = new EditorState();
            var scene = new Scene("identity");
            state.CurrentScene = scene;
            var e = scene.CreateEntity("oldName");
            scene.Update(0f);

            state.ExecuteCommand(new RenameEntityCommand(e, "NewName"));
            Check("Premise: the name changed", scene.FindEntity("NewName") != null);

            state.ExecuteCommand(new DeleteEntityCommand(scene, e, state));
            scene.Update(0f);
            state.Undo(); scene.Update(0f);
            var e2 = scene.FindEntity("NewName");
            Check("Premise: it was revived (as NewName)", e2 != null && !ReferenceEquals(e2, e));

            state.Undo(); scene.Update(0f);
            Check($"★ rename, delete, undo, undo = original name (actual '{e2?.Name}')", e2?.Name == "oldName");
        }

        {
            var state = new EditorState();
            var scene = new Scene("identity");
            state.CurrentScene = scene;
            var p = scene.CreateEntity("P");
            var c = scene.CreateEntity("C");
            scene.Update(0f);

            state.ExecuteCommand(new SetParentCommand(c, p));
            Check("Premise: the parent became P", c.Parent == p);

            state.ExecuteCommand(new DeleteEntityCommand(scene, c, state));
            scene.Update(0f);
            state.Undo(); scene.Update(0f);
            var c2 = scene.FindEntity("C");
            Check("Premise: it was revived (a new object)", c2 != null && !ReferenceEquals(c2, c));
            Check("★ The revived child attaches under the live parent (found by Id, not by reference)",
                ReferenceEquals(c2?.Parent, p));

            state.Undo(); scene.Update(0f);
            Check($"★ SetParent, delete, undo, undo = root (actual parent '{c2?.Parent?.Name ?? "(root)"}')",
                c2 != null && c2.Parent == null);
        }

        {
            var state = new EditorState();
            var scene = new Scene("identity");
            state.CurrentScene = scene;
            var a = scene.CreateEntity("R0");
            var b = scene.CreateEntity("R1");
            var c = scene.CreateEntity("R2");
            scene.Update(0f);
            Check($"Premise: initial index of R0 = 0 (actual {scene.GetSiblingIndex(a)})", scene.GetSiblingIndex(a) == 0);

            state.ExecuteCommand(new ReorderEntityCommand(scene, a, null, 2));
            Check($"Premise: it moved back (actual {scene.GetSiblingIndex(a)})", scene.GetSiblingIndex(a) == 1);

            state.ExecuteCommand(new DeleteEntityCommand(scene, a, state));
            scene.Update(0f);
            state.Undo(); scene.Update(0f);
            var a2 = scene.FindEntity("R0");
            Check("Premise: it was revived (a new object)", a2 != null && !ReferenceEquals(a2, a));
            Check($"★ Premise: the revived entity attaches at the end of the list (actual {(a2 != null ? scene.GetSiblingIndex(a2) : -1)}) - " +
                  "if this equalled the expected value, the check below would be vacuous",
                a2 != null && scene.GetSiblingIndex(a2) == 2);

            state.Undo(); scene.Update(0f);
            Check($"★ Reorder, delete, undo, undo = original index 0 (actual {(a2 != null ? scene.GetSiblingIndex(a2) : -1)})",
                a2 != null && scene.GetSiblingIndex(a2) == 0);
        }

        {
            var state = new EditorState();
            var scene = new Scene("identity");
            state.CurrentScene = scene;
            var e = scene.CreateEntity("V");
            var col = e.AddComponent<BoxCollider2D>();
            scene.Update(0f);

            state.ExecuteCommand(new PropertyCommand<bool>(
                col, "IsTrigger", false, true, v => col.IsTrigger = v, "Change IsTrigger", e));
            Check("Premise: the value edit took effect", col.IsTrigger);

            state.ExecuteCommand(new DeleteEntityCommand(scene, e, state));
            scene.Update(0f);
            state.Undo(); scene.Update(0f);
            var e2 = scene.FindEntity("V");
            Check("Premise: it was revived (a new object - the revived collider is new too)",
                e2 != null && !ReferenceEquals(e2, e));

            var err = CaptureStderr(() => { state.Undo(); scene.Update(0f); });
            Check($"★ A value-edit undo across a delete barks (actual '{err.Trim()}')",
                err.Contains("[Undo] target is gone"));
            Check("★ And it does not write to the dead component (the revived value is unchanged)",
                e2?.GetComponent<BoxCollider2D>()?.IsTrigger == true);
            var state2 = new EditorState();
            var scene2 = new Scene("identity");
            state2.CurrentScene = scene2;
            var f = scene2.CreateEntity("V2");
            var col2 = f.AddComponent<BoxCollider2D>();
            scene2.Update(0f);
            state2.ExecuteCommand(new PropertyCommand<bool>(
                col2, "IsTrigger", false, true, v => col2.IsTrigger = v, "Change IsTrigger", f));
            var quiet = CaptureStderr(() => state2.Undo());
            Check($"★ Control: without a delete it reverts quietly (bark '{quiet.Trim()}')",
                quiet.Length == 0 && !col2.IsTrigger);
        }

        {
            var state = new EditorState();
            var scene = new Scene("identity");
            state.CurrentScene = scene;
            var src = scene.CreateEntity("Src");
            scene.Update(0f);
            var dup = EntitySnapshot.Duplicate(scene, src, state);
            scene.Update(0f);
            Check($"★ The duplicate has a new runtime Id (original {src.Id} / copy {dup.Id})", dup.Id != src.Id);
            Check("★ So looking up the original by Id still gives the original",
                ReferenceEquals(scene.FindEntityById(src.Id), src));
        }

        {
            var scene = new Scene("identity");
            var e = scene.CreateEntity("Q");
            Check("★ Found even before commit (_pendingAdd) - otherwise a composite undo loses its target",
                ReferenceEquals(scene.FindEntityById(e.Id), e));
            Check("Premise: a name lookup does not find it before commit (the two lookups have different rules)",
                scene.FindEntity("Q") == null);
            scene.Update(0f);
            Check("Found after commit too", ReferenceEquals(scene.FindEntityById(e.Id), e));

            scene.DestroyEntity(e);
            Check("★ Once in _pendingRemove it counts as dead (it is still in the list)",
                scene.FindEntityById(e.Id) == null);
            scene.Update(0f);
            Check("Still null after removal", scene.FindEntityById(e.Id) == null);
            Check("Premise: 0 and negatives are always null (stops an unassigned SerializedId value leaking in)",
                scene.FindEntityById(0) == null && scene.FindEntityById(-1) == null);
        }

        {
            var state = new EditorState();
            var scene = new Scene("identity");
            state.CurrentScene = scene;
            var p = scene.CreateEntity("Parent");
            var c = scene.CreateEntity("Child");
            c.SetParent(p);
            scene.Update(0f);

            var delChild = new DeleteEntityCommand(scene, c, state);
            state.ExecuteCommand(delChild);
            scene.Update(0f);
            state.ExecuteCommand(new DeleteEntityCommand(scene, p, state));
            scene.Update(0f);
            Check("Premise: parent and child are both gone",
                scene.FindEntity("Parent") == null && scene.FindEntity("Child") == null);

            var err = CaptureStderr(() => { delChild.Undo(); scene.Update(0f); });
            var c2 = scene.FindEntity("Child");
            Check($"★ With no parent it barks (actual '{err.Trim()}')", err.Contains("[Undo] parent is gone, restored at the root"));
            Check("★ But the entity is not lost - it comes back at the root", c2 != null && c2.Parent == null);
        }

        {
            var scene = new Scene("identity");
            var e = scene.CreateEntity("Dup");
            scene.Update(0f);
            var err = CaptureStderr(() => scene.CreateEntityWithId("Dup2", e.Id));
            Check($"★ Issuing a runtime Id that is still alive again barks (actual '{err.Trim()}')",
                err.Contains("duplicate runtime Id"));
            var quiet = CaptureStderr(() => scene.CreateEntityWithId("New", 999999));
            Check("★ Control: without an overlap it stays quiet", quiet.Length == 0);

            var after = scene.CreateEntity("AfterHighWater");
            Check($"★ Inheriting a large number raises the counter above it (999999 -> next {after.Id})",
                after.Id > 999999);
        }

        {
            var state = new EditorState();
            var scene = new Scene("identity");
            state.CurrentScene = scene;
            var e = scene.CreateEntity("K");
            var col = e.AddComponent<BoxCollider2D>();
            col.Offset = new Vector2(5, 6);
            scene.Update(0f);

            state.ExecuteCommand(new RemoveComponentCommand(e, col));
            Check("Premise: the component came off", e.GetComponent<BoxCollider2D>() == null);

            state.ExecuteCommand(new DeleteEntityCommand(scene, e, state));
            scene.Update(0f);
            state.Undo(); scene.Update(0f);
            var e2 = scene.FindEntity("K");
            Check("Premise: it was revived (without the collider)",
                e2 != null && !ReferenceEquals(e2, e) && e2.GetComponent<BoxCollider2D>() == null);

            state.Undo(); scene.Update(0f);
            var back = e2?.GetComponent<BoxCollider2D>();
            Check($"★ A RemoveComponent undo recovers the values too, even across a delete (actual offset {back?.Offset})",
                back != null && back.Offset == new Vector2(5, 6));
        }

        {
            var state = new EditorState();
            var scene = new Scene("identity");
            state.CurrentScene = scene;
            var e = scene.CreateEntity("L");
            scene.Update(0f);

            state.ExecuteCommand(new AddComponentCommand(e, typeof(BoxCollider2D)));
            Check("Premise: the component was attached", e.GetComponent<BoxCollider2D>() != null);

            state.ExecuteCommand(new DeleteEntityCommand(scene, e, state));
            scene.Update(0f);
            state.Undo(); scene.Update(0f);
            var e2 = scene.FindEntity("L");
            Check("Premise: it was revived (the collider came along in the snapshot)",
                e2 != null && !ReferenceEquals(e2, e) && e2.GetComponent<BoxCollider2D>() != null);

            var err = CaptureStderr(() => { state.Undo(); scene.Update(0f); });
            Check($"★ An AddComponent undo across a delete barks (actual '{err.Trim()}')",
                err.Contains("[Undo] target is gone"));
        }

        {
            var scene = new Scene("identity");
            var e = scene.CreateEntity("Gone");
            var p = scene.CreateEntity("GoneParent");
            scene.Update(0f);

            var rename = new RenameEntityCommand(e, "NewName");
            rename.Execute();
            var setParent = new SetParentCommand(e, p);

            scene.DestroyEntity(p);
            scene.Update(0f);

            var errParent = CaptureStderr(() => setParent.Execute());
            Check($"★ With the parent gone for good it barks and skips (actual '{errParent.Trim()}')",
                errParent.Contains("[Undo] parent is missing"));
            Check("★ And it does not quietly move to the root - the hierarchy must not flatten without a sound",
                e.Parent == null && scene.FindEntity("NewName") != null);

            scene.DestroyEntity(e);
            scene.Update(0f);
            var errSelf = CaptureStderr(() => rename.Undo());
            Check($"★ With the target gone for good it barks - carrying the description (actual '{errSelf.Trim()}')",
                errSelf.Contains("[Undo] target is gone") && errSelf.Contains("Rename"));

            var alive = scene.CreateEntity("Alive2");
            scene.Update(0f);
            var mv = new MoveEntityCommand(alive, Vector2.Zero, new Vector2(3, 4));
            var quiet = CaptureStderr(() => { mv.Execute(); mv.Undo(); });
            Check($"★ Control: with the target alive it stays quiet (bark '{quiet.Trim()}')",
                quiet.Length == 0 && alive.GetComponent<Transform>()!.Position == Vector2.Zero);
        }

        {
            var state = new EditorState();
            var scene = new Scene("identity");
            state.CurrentScene = scene;

            var create = new CreateEntityCommand(scene, "Born");
            state.ExecuteCommand(create);
            scene.Update(0f);
            var born = create.CreatedEntity!;
            int bornId = born.Id;

            state.ExecuteCommand(new MoveEntityCommand(born, Vector2.Zero, new Vector2(12, 34)));
            Check("Premise: created and moved (12,34)",
                born.GetComponent<Transform>()!.Position == new Vector2(12, 34));

            state.Undo(); scene.Update(0f);
            state.Undo(); scene.Update(0f);
            Check("Premise: it is gone", scene.FindEntity("Born") == null);

            var err = CaptureStderr(() =>
            {
                state.Redo(); scene.Update(0f);
                state.Redo(); scene.Update(0f);
            });
            var back = scene.FindEntity("Born");
            Check($"★ Redo revives with the same runtime Id (original {bornId} / actual {back?.Id})",
                back != null && back.Id == bornId);
            Check($"★ So the move redo that follows finds its target (actual {back?.GetComponent<Transform>()?.Position})",
                back?.GetComponent<Transform>()?.Position == new Vector2(12, 34));
            Check($"★ And it does not bark - a bark means the identity broke (actual '{err.Trim()}')",
                err.Length == 0);
        }
    }

    private static string CaptureStderr(Action body)
    {
        var prev = Console.Error;
        var sw = new StringWriter();
        Console.SetError(sw);
        try { body(); }
        finally { Console.SetError(prev); }
        return sw.ToString();
    }

    private static void TestConvertToPrefabWorker()
    {
        Console.WriteLine("--- Prefab conversion walker ---");
        Runtime.Assets.AnimClipCache.ContentRoot = Path.Combine(AppContext.BaseDirectory, "Content");

        var dir = Path.Combine(Path.GetTempPath(), "pixelcore_prefabconv_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var tempRegistry = Path.Combine(dir, "assets.json");
            var realRegistry = Path.Combine(Runtime.Assets.ContentPaths.Root, "assets.json");
            if (File.Exists(realRegistry)) File.Copy(realRegistry, tempRegistry);
            using var reg = Runtime.Assets.AssetRegistry.UseTemporary(tempRegistry);
            Check("Premise: the copy of the real registry knows both prefabs (without them everything below is vacuous)",
                Runtime.Assets.AssetRegistry.Instance.GetPath(BackgroundPrefabId) != null
                && Runtime.Assets.AssetRegistry.Instance.GetPath(Gameplay.Systems.LevelSetup.PlayerPrefabId) != null);

            {
                var scene = new Scene("conv1");
                var state = new EditorState { CurrentScene = scene };

                var group = scene.CreateEntity("Group");
                var bg = scene.CreateEntity("BG");
                bg.SetParent(group);
                var inst = bg.AddComponent<Runtime.Components.SceneInstance>();
                inst.SceneId = BackgroundPrefabId;
                inst.Load();
                scene.Update(0f);
                Check("Premise: the prefab instance created its children (2 hidden children)",
                    bg.Children.Count == 2 && bg.Children.All(c => c.HideFromSerialization));

                var instChild = bg.Children[0];
                var prop = scene.CreateEntity("LocalProp");
                prop.SetParent(instChild);
                prop.GetComponent<Transform>()!.Position = new Vector2(7, 9);
                scene.Update(0f);
                Check("Premise: the prop is a scene-owned entity under a hidden child (where the old walker lost it)",
                    !prop.HideFromSerialization && instChild.HideFromSerialization
                    && instChild.Children.Count == 1);

                var file = Path.Combine(dir, "Converted.scene");
                var cmd = new ConvertToPrefabCommand(scene, group, file, state);
                state.ExecuteCommand(cmd);
                scene.Update(0f);

                var saved = SceneSerializer.LoadFromFile(file);
                Check($"★ The local prop is in the prefab file ({saved?.Entities.Count} entities)",
                    saved != null && saved.Entities.Any(e => e.Name == "LocalProp"));

                var hostNow = cmd.InstanceEntity;
                Check("Premise: the original was replaced by an instance", hostNow != null && scene.FindEntity("Group") == null);
                Check("★ The local prop lives in the scene too (the instance revives it)",
                    scene.FindEntity("LocalProp") != null);

                state.Undo();
                scene.Update(0f);
                var backGroup = scene.FindEntity("Group");
                var backProp = scene.FindEntity("LocalProp");
                Check("★ Undo: the original group comes back", backGroup != null);
                Check($"★ Undo: the local prop comes back with its values (actual {backProp?.GetComponent<Transform>()?.Position})",
                    backProp?.GetComponent<Transform>()?.Position == new Vector2(7, 9));
                Check("★ Undo: there is only one prop (not two - the snapshot's plus the instance's recreation)",
                    scene.Entities.Count(e => e.Name == "LocalProp") == 1);
                Check($"★ Undo: the prop does not drop to the root - it is re-anchored (parent '{backProp?.Parent?.Name ?? "(root)"}')",
                    backProp?.Parent != null && backProp.Parent.Name == "BG");
            }

            {
                var scene = new Scene("conv2");
                var state = new EditorState { CurrentScene = scene };

                var rig = scene.CreateEntity("Rig");
                var inst = rig.AddComponent<Runtime.Components.SceneInstance>();
                inst.SceneId = Gameplay.Systems.LevelSetup.PlayerPrefabId;
                inst.Load();
                scene.Update(0f);
                Check("Premise: the player prefab loaded root-merged (the base components landed on the host)",
                    inst.RootMerged && rig.GetComponent<Animator>() != null);

                var file = Path.Combine(dir, "ConvertedRig.scene");
                state.ExecuteCommand(new ConvertToPrefabCommand(scene, rig, file, state));
                scene.Update(0f);
                state.Undo();
                scene.Update(0f);

                var back = scene.FindEntity("Rig");
                Check("Premise: it was revived", back != null);
                Check($"★ Undo: 1 Animator (actual {back?.GetComponents<Animator>().Count()}) - " +
                      "two (the base's plus the snapshot's) means the old walker",
                    back != null && back.GetComponents<Animator>().Count() == 1);
                Check($"★ Undo: 1 capsule (actual {back?.GetComponents<Runtime.Components.CapsuleCollider2D>().Count()})",
                    back != null && back.GetComponents<Runtime.Components.CapsuleCollider2D>().Count() == 1);
                Check("★ Undo: the root-merged state is restored (RootMerged)",
                    back?.GetComponent<Runtime.Components.SceneInstance>()?.RootMerged == true);
            }

            {
                var scene = new Scene("conv3");
                var state = new EditorState { CurrentScene = scene };

                var root = scene.CreateEntity("Plain");
                root.GetComponent<Transform>()!.Position = new Vector2(30, 40);
                var kid = scene.CreateEntity("Kid");
                kid.SetParent(root);
                kid.GetComponent<Transform>()!.Position = new Vector2(35, 44);
                var grand = scene.CreateEntity("Grand");
                grand.SetParent(kid);
                grand.GetComponent<Transform>()!.Position = new Vector2(36, 41);
                scene.Update(0f);

                var file = Path.Combine(dir, "Plain.scene");
                state.ExecuteCommand(new ConvertToPrefabCommand(scene, root, file, state));
                scene.Update(0f);
                var saved = SceneSerializer.LoadFromFile(file);
                Check($"★ Control: all three generations are in the file (actual {saved?.Entities.Count})",
                    saved?.Entities.Count == 3);

                state.Undo();
                scene.Update(0f);
                var r2 = scene.FindEntity("Plain");
                var k2 = scene.FindEntity("Kid");
                var g2 = scene.FindEntity("Grand");
                Check("★ Control: undo restores the hierarchy too (Grand under Kid under Plain)",
                    r2 != null && k2?.Parent == r2 && g2?.Parent == k2);
                Check($"★ Control: coordinates restored too (actual {r2?.GetComponent<Transform>()?.Position} / " +
                      $"{k2?.GetComponent<Transform>()?.Position} / {g2?.GetComponent<Transform>()?.Position})",
                    r2?.GetComponent<Transform>()?.Position == new Vector2(30, 40)
                    && k2?.GetComponent<Transform>()?.Position == new Vector2(35, 44)
                    && g2?.GetComponent<Transform>()?.Position == new Vector2(36, 41));
                Check("★ Control: no copies were made (one entity per name)",
                    scene.Entities.Count(e => e.Name == "Kid") == 1
                    && scene.Entities.Count(e => e.Name == "Grand") == 1);
            }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}

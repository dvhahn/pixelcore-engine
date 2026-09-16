using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Components;

namespace PixelCore.Runtime.Serialization;

public static class SerializerSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        Console.WriteLine("=== Serializer self-test ===");

        var src = new Scene("TestScene");
        var a = src.CreateEntity("A");
        var ta = a.GetComponent<Transform>()!;
        ta.Position = new Vector2(100, 50);
        var rb = a.AddComponent<Rigidbody2D>();
        rb.UseGravity = false;
        var col = a.AddComponent<BoxCollider2D>();
        col.IsTrigger = true;
        col.Size = new Vector2(12, 20);
        var ia = a.AddComponent<Interactable>();
        ia.Prompt = "open"; ia.Kind = InteractKind.Door; ia.Range = 20f;
        ia.TargetSceneId = "b83f0d4e"; ia.Spawn = "Spawn_Porch"; ia.SoundId = "5cf7fd14";
        ia.Facing = "Up";

        var b = src.CreateEntity("B");
        b.GetComponent<Transform>()!.Position = new Vector2(200, 80);
        b.SetParent(a);
        var bCircle = b.AddComponent<CircleCollider2D>();
        bCircle.Radius = 7f; bCircle.IsTrigger = true; bCircle.Offset = new Vector2(0, 3);

        var c = src.CreateEntity("C");
        var sc = c.AddComponent<SpriteRenderer>();
        sc.TexturePath = "Sprites/test.png";
        sc.RenderLayer = RenderLayers.AboveEntities;
        sc.FlipX = true;
        sc.DrawSize = new Vector2(118, 8);
        var cCap = c.AddComponent<CapsuleCollider2D>();
        cCap.Radius = 5f; cCap.Length = 14f; cCap.Horizontal = true;

        var shelf = src.CreateEntity("Shelf");
        shelf.GetComponent<Transform>()!.Position = new Vector2(300, 200);
        shelf.AddComponent<SpriteRenderer>().TexturePath = "Sprites/shelf.png";
        var grp = shelf.AddComponent<PixelCore.Runtime.Components.SortingGroup>();
        grp.SortOffset = 3f;
        var snack = src.CreateEntity("Snack");
        snack.SetParent(shelf);
        snack.GetComponent<Transform>()!.Position = new Vector2(300, 160);
        var snackSr = snack.AddComponent<SpriteRenderer>();
        snackSr.TexturePath = "Sprites/snack.png";
        snackSr.SortOffset = 1f;

        src.LightingEnabled = true;
        src.AmbientLight = new Microsoft.Xna.Framework.Color(80, 90, 120);
        src.BackColor = new Microsoft.Xna.Framework.Color(18, 12, 40);
        var d = src.CreateEntity("D");
        var dl = d.AddComponent<Light2D>();
        dl.Color = new Microsoft.Xna.Framework.Color(255, 200, 120);
        dl.Radius = 140f;
        dl.Intensity = 1.5f;

        var se = d.AddComponent<PixelCore.Runtime.Components.SoundEmitter>();
        se.SoundId = "aa11bb22";
        se.Radius = 96f;
        se.Volume = 0.7f;
        se.Bus = PixelCore.Runtime.Audio.AudioBus.SFX;
        se.PanStrength = 0.5f;
        se.FadeIn = 1.25f;
        se.FadeOut = 2.5f;

        var sa = d.AddComponent<PixelCore.Runtime.Components.SurfaceArea>();
        sa.SurfaceId = "surf_rug";
        sa.Priority = 10;
        src.DefaultSurfaceId = "surf_wood";

        var ctr = d.AddComponent<PixelCore.Runtime.Components.CutsceneTrigger>();
        ctr.CutsceneId = "house.wakeUp";
        ctr.Once = false;

        src.Exterior = true;

        src.CameraFixed = true;
        src.CameraFixedPos = new Vector2(160, -48);
        src.CameraBoundsEnabled = true;
        src.CameraBounds = new Microsoft.Xna.Framework.Rectangle(-32, -16, 640, 360);

        src.Post.Exposure = 0.3f;
        src.Post.Contrast = 10f;
        src.Post.ColorFilter = new Microsoft.Xna.Framework.Color(255, 220, 180);
        src.Post.Saturation = 15f;
        src.Post.Temperature = 15f;
        src.Post.Lift = new Microsoft.Xna.Framework.Vector4(1f, 0.99f, 0.99f, 0.023f);
        src.Post.Tonemap = PixelCore.Runtime.Rendering.ToneMode.Aces;
        src.Post.VignetteColor = new Microsoft.Xna.Framework.Color(40, 0, 0);
        src.Post.Vignette = 0.3f;
        src.Post.VignetteSmooth = 0.7f;
        src.Post.VignetteRounded = true;
        src.Post.GrainIntensity = 0.3f;
        src.Post.GrainCells = 540f;
        src.Post.BloomIntensity = 0.6f;
        src.Post.BloomThreshold = 0.5f;
        src.PostPresets.Add(new PixelCore.Runtime.Rendering.PostPreset
        {
            Key = "winter",
            Profile = new PixelCore.Runtime.Rendering.PostProfile { Temperature = -6f, Saturation = 18f },
        });
        var sharedAsset = new PixelCore.Runtime.Assets.PostAsset
        {
            Id = "testpost01",
            Name = "autumn",
            Profile = new PostData { Temperature = 15f, Saturation = -2f, BloomIntensity = 0.6f },
        };
        PixelCore.Runtime.Assets.PostAssetCache.Put(sharedAsset);
        src.PostPresets.Add(new PixelCore.Runtime.Rendering.PostPreset
        {
            Key = "autumn",
            AssetId = "testpost01",
            Profile = sharedAsset.Profile.ToProfile(),
        });
        sc.EmissiveIntensity = 0.8f;
        sc.CastShadow = true;
        sc.ShadowScale = 0.5f;
        var lightE = src.CreateEntity("E");
        var el = lightE.AddComponent<Light2D>();
        el.Shape = LightShape.Cone;
        el.Rotation = 35f; el.Length = 220f; el.SpreadAngle = 42f;
        el.FollowSun = true;
        var lightF = src.CreateEntity("F");
        var fl = lightF.AddComponent<Light2D>();
        fl.Shape = LightShape.Rect;
        fl.RectSize = new Vector2(120, 40); fl.Feather = 0.6f;

        {
            var animScene = new Scene("AnimCaptureTest");
            var animOnly = animScene.CreateEntity("AnimOnly");
            animOnly.AddComponent<Animator>();
            var captured = ComponentDataRegistry.CaptureAll(animOnly);
            bool ghost = false, asAnimator = false;
            foreach (var cd in captured)
            {
                if (cd.GetType() == typeof(SpriteData)) ghost = true;
                if (cd is AnimatorData) asAnimator = true;
            }
            Check("an entity with only an Animator: no phantom SpriteData (a double-render regression)", !ghost);
            Check("an entity with only an Animator: captured as AnimatorData", asAnimator);
        }

        int srcAId = a.Id;

        src.Update(0f);

        var path = Path.Combine(Path.GetTempPath(), "pc_selftest.scene");
        var data = SceneSerializer.ToData(src);
        SaveFixture(data, path);
        var loaded = SceneSerializer.LoadFromFile(path);
        Check("LoadFromFile != null", loaded != null);
        if (loaded == null) { Report(); return; }

        Check("schemaVersion equals the current one", loaded.SchemaVersion == SceneData.CurrentSchemaVersion);
        Check("entity count == 8", loaded.Entities.Count == 8);

        var dst = new Scene("dst");
        SceneSerializer.FromData(dst, loaded);
        dst.Update(0f);

        var ra = dst.FindEntity("A");
        var rbb = dst.FindEntity("B");
        var rc = dst.FindEntity("C");
        Check("A, B and C are restored", ra != null && rbb != null && rc != null);
        if (ra == null || rbb == null || rc == null) { Report(); return; }

        var rta = ra.GetComponent<Transform>();
        Check("A.Position restored (100,50)", rta != null && rta.Position == new Vector2(100, 50));
        Check("new format: no scaleX recorded", !File.ReadAllText(path).Contains("scaleX"));

        var rrb = ra.GetComponent<Rigidbody2D>();
        Check("A.Rigidbody.UseGravity == false", rrb != null && rrb.UseGravity == false);

        var rcol = ra.GetComponent<BoxCollider2D>();
        Check("A.Collider.IsTrigger == true", rcol != null && rcol.IsTrigger);
        Check("A.Collider.Size == (12,20)", rcol != null && rcol.Size == new Vector2(12, 20));

        Check("B's parent is A (restored by id)", rbb.Parent == ra);

        {
            var multi = new Scene("multi");
            var npc = multi.CreateEntity("Npc");
            var body = npc.AddComponent<BoxCollider2D>();
            body.Size = new Vector2(12, 8);
            var detect = npc.AddComponent<CircleCollider2D>();
            detect.Radius = 40f; detect.IsTrigger = true;

            multi.FlushPendingAdds();
            var mdata = SceneSerializer.ToData(multi);
            var back = new Scene("multi2");
            SceneSerializer.FromData(back, mdata);
            back.FlushPendingAdds();
            var rn = back.FindEntity("Npc");
            int boxes = 0, circles = 0; bool trig = false;
            foreach (var mc in rn!.GetComponents<Collider2D>())
            {
                if (mc is BoxCollider2D) boxes++;
                if (mc is CircleCollider2D cc) { circles++; trig = cc.IsTrigger && cc.Radius == 40f; }
            }
            Check("multiple colliders: both the box and the circle are restored", boxes == 1 && circles == 1);
            Check("multiple colliders: the trigger flag and radius survive", trig);
        }

        {
            var ord = new Scene("ord");
            var p1 = ord.CreateEntity("P");
            var c1 = ord.CreateEntity("C1");
            var c2 = ord.CreateEntity("C2");
            var c3 = ord.CreateEntity("C3");
            ord.FlushPendingAdds();
            c1.SetParent(p1); c2.SetParent(p1); c3.SetParent(p1);

            ord.ReorderSibling(c3, p1, 0);

            var odata = SceneSerializer.ToData(ord);
            var oback = new Scene("ord2");
            SceneSerializer.FromData(oback, odata);
            oback.FlushPendingAdds();
            var rp = oback.FindEntity("P")!;
            Check("reorder save and load: the C3, C1, C2 order survives",
                rp.Children.Count == 3 && rp.Children[0].Name == "C3"
                && rp.Children[1].Name == "C1" && rp.Children[2].Name == "C2");
        }

        var ria = ra.GetComponent<Interactable>();
        Check("A.Interactable restored (Door, prompt, 20)",
            ria != null && ria.Kind == InteractKind.Door && ria.Prompt == "open" && ria.Range == 20f);
        Check("A.Interactable sound id restored (empty means silent)",
            ria != null && ria.SoundId == "5cf7fd14");
        Check("A.Interactable door destination round-trips as a scene id",
            ria != null && ria.TargetSceneId == "b83f0d4e" && ria.Spawn == "Spawn_Porch");
        Check("Interactable: the door arrival facing round-trips", ria?.Facing == "Up", ria?.Facing);

        TestAnimatorPivot();
        TestPlayerNameContract();
        TestPlayerBodyAlwaysEnsured();
        TestCodeOwnedBodyRetired();
        TestAnimatorClipsRoundTrip();
        TestAnimatorAuthoredVsRuntime();
        TestAnimatorIdleHold();
        TestAnimatorWriteReplacesClips();
        TestAnimatorClearFrameSticks();
        TestSpriteDefaultsAgree();
        TestAnimatorClipRemoval();
        TestAnimatorPlayMissLatch();
        TestCodeOwnedBodyGuard();
        TestPlayerPrefab();
#if DEBUG
        TestUnmigratedPrefabBarks();
#endif
        TestAudioRefMigration();
        TestTilesetRefMigration();
        TestRealContentAssetRefs();
        TestAllContentSchemaCurrent();
        TestRealContentPromptKeys();
        TestFileKeysArePinned();
        TestEnumsGoOutAsNames();

        var rCircle = rbb.GetComponent<CircleCollider2D>();
        Check("B.CircleCollider restored (r7, trigger)",
            rCircle != null && rCircle.Radius == 7f && rCircle.IsTrigger && rCircle.Offset == new Vector2(0, 3));

        var rsc = rc.GetComponent<SpriteRenderer>();
        Check("C.Sprite.TexturePath restored", rsc != null && rsc.TexturePath == "Sprites/test.png");
        Check("C.Sprite.RenderLayer restored", rsc != null && rsc.RenderLayer == RenderLayers.AboveEntities);
        Check("C on a fixed layer: SortY equals SortOffset", rsc != null && rsc.SortY == rsc.SortOffset);
        Check("C.Sprite.FlipX == true", rsc != null && rsc.FlipX);
        Check("C.Sprite.DrawSize restored (118,8)", rsc != null && rsc.DrawSize == new Vector2(118, 8));
        var rCap = rc.GetComponent<CapsuleCollider2D>();
        Check("C.CapsuleCollider restored (r5, L14, horizontal)",
            rCap != null && rCap.Radius == 5f && rCap.Length == 14f && rCap.Horizontal);

        var rShelf = dst.FindEntity("Shelf");
        var rGrp = rShelf?.GetComponent<PixelCore.Runtime.Components.SortingGroup>();
        Check("Shelf.SortingGroup restored (Entities, offset 3)",
            rGrp != null && rGrp.RenderLayer == RenderLayers.Entities && rGrp.SortOffset == 3f);
        Check("group SortY equals foot Y plus offset (203)", rGrp != null && rGrp.SortY == 203f);
        var rSnack = dst.FindEntity("Snack");
        Check("Snack is restored as a child of Shelf", rSnack?.Parent == rShelf);
        Check("a member's Order inside the group survives (1)",
            rSnack?.GetComponent<SpriteRenderer>()?.SortOffset == 1f);
        Check("SortingGroup.Find: a member finds its group",
            PixelCore.Runtime.Components.SortingGroup.Find(rSnack) == rGrp);
        Check("SortingGroup.Find: the group root is a member too",
            PixelCore.Runtime.Components.SortingGroup.Find(rShelf) == rGrp);
        Check("SortingGroup.Find: outside a group gives null",
            PixelCore.Runtime.Components.SortingGroup.Find(rc) == null);

        Check("Scene.LightingEnabled restored", dst.LightingEnabled);
        Check("Scene.Ambient restored (80,90,120)",
            dst.AmbientLight == new Microsoft.Xna.Framework.Color(80, 90, 120));
        Check("Scene.BackColor restored (18,12,40)",
            dst.BackColor == new Microsoft.Xna.Framework.Color(18, 12, 40),
            dst.BackColor.ToString());
        var rd2 = dst.FindEntity("D");
        var rl = rd2?.GetComponent<Light2D>();
        Check("D.Light2D restored (colour, radius, intensity)",
            rl != null && rl.Color == new Microsoft.Xna.Framework.Color(255, 200, 120)
            && rl.Radius == 140f && rl.Intensity == 1.5f);
        Check("D.Light2D default shape is Point", rl != null && rl.Shape == LightShape.Point);

        var rse = rd2?.GetComponent<PixelCore.Runtime.Components.SoundEmitter>();
        Check("D.SoundEmitter restored (id, radius, volume)",
            rse != null && rse.SoundId == "aa11bb22"
            && rse.Radius == 96f && rse.Volume == 0.7f);
        Check("D.SoundEmitter bus restored (SFX rather than the Ambient default)",
            rse != null && rse.Bus == PixelCore.Runtime.Audio.AudioBus.SFX);
        Check("D.SoundEmitter pan and fade restored",
            rse != null && rse.PanStrength == 0.5f
            && rse.FadeIn == 1.25f && rse.FadeOut == 2.5f);

        var rsa = rd2?.GetComponent<PixelCore.Runtime.Components.SurfaceArea>();
        Check("D.SurfaceArea restored (surface id, priority)",
            rsa != null && rsa.SurfaceId == "surf_rug" && rsa.Priority == 10);
        Check("Scene.DefaultSurfaceId restored", dst.DefaultSurfaceId == "surf_wood");

        var rct = rd2?.GetComponent<PixelCore.Runtime.Components.CutsceneTrigger>();
        Check("D.CutsceneTrigger restored (cutscene id)", rct != null && rct.CutsceneId == "house.wakeUp");
        Check("D.CutsceneTrigger restored (Once=false, not the default)", rct != null && !rct.Once);

        Check("C.Sprite.Emissive restored (0.8)", rsc != null && rsc.EmissiveIntensity == 0.8f);
        var rel = dst.FindEntity("E")?.GetComponent<Light2D>();
        Check("E.Light2D cone restored (rotation, length, spread)",
            rel != null && rel.Shape == LightShape.Cone
            && rel.Rotation == 35f && rel.Length == 220f && rel.SpreadAngle == 42f);
        Check("E.Light2D FollowSun restored", rel != null && rel.FollowSun);
        Check("Scene.Exterior restored", dst.Exterior);
        Check("Scene room framing restored (fixed on, position, bounds)",
            dst.CameraFixed && dst.CameraFixedPos == new Vector2(160, -48)
            && dst.CameraBoundsEnabled
            && dst.CameraBounds == new Microsoft.Xna.Framework.Rectangle(-32, -16, 640, 360));
        Check("Scene.Post v2 colour group restored (exposure, contrast, filter, saturation, white balance, LGG, tonemapping)",
            dst.Post.Exposure == 0.3f && dst.Post.Contrast == 10f
            && dst.Post.ColorFilter == new Microsoft.Xna.Framework.Color(255, 220, 180)
            && dst.Post.Saturation == 15f && dst.Post.Temperature == 15f
            && dst.Post.Lift == new Microsoft.Xna.Framework.Vector4(1f, 0.99f, 0.99f, 0.023f)
            && dst.Post.Tonemap == PixelCore.Runtime.Rendering.ToneMode.Aces);
        Check("Scene.Post v2 screen group restored (vignette colour/smoothness/rounded, grain intensity/cell count)",
            dst.Post.VignetteColor == new Microsoft.Xna.Framework.Color(40, 0, 0)
            && dst.Post.Vignette == 0.3f && dst.Post.VignetteSmooth == 0.7f && dst.Post.VignetteRounded
            && dst.Post.GrainIntensity == 0.3f && dst.Post.GrainCells == 540f);
        Check("Scene.Post v2 bloom restored", dst.Post.BloomIntensity == 0.6f && dst.Post.BloomThreshold == 0.5f);
        Check("Scene.PostPresets local restored (key plus embedded values)",
            dst.PostPresets.Count == 2 && dst.PostPresets[0].Key == "winter"
            && !dst.PostPresets[0].IsShared
            && dst.PostPresets[0].Profile.Temperature == -6f
            && dst.PostPresets[0].Profile.Saturation == 18f);
        var shared = dst.PostPresets.Count > 1 ? dst.PostPresets[1] : null;
        Check("Scene.PostPresets shared restored (values filled from the .post asset)",
            shared != null && shared.Key == "autumn" && shared.IsShared && shared.AssetId == "testpost01"
            && shared.Profile.Temperature == 15f && shared.Profile.Saturation == -2f
            && shared.Profile.BloomIntensity == 0.6f);
        var sharedRaw = loaded.PostPresets != null && loaded.PostPresets.Count > 1 ? loaded.PostPresets[1] : null;
        var localRaw = loaded.PostPresets != null && loaded.PostPresets.Count > 0 ? loaded.PostPresets[0] : null;
        Check("a shared preset keeps only the id in the scene (no copy of the values)",
            sharedRaw != null && sharedRaw.AssetId == "testpost01" && sharedRaw.Profile == null);
        Check("a local preset embeds its values in the scene (no AssetId)",
            localRaw != null && localRaw.AssetId == null && localRaw.Profile != null);
        {
            src.ActivePostKey = "autumn";
            var actPath = Path.Combine(Path.GetTempPath(), "pc_selftest_active.scene");
            SaveFixture(SceneSerializer.ToData(src), actPath);
            var actLoaded = SceneSerializer.LoadFromFile(actPath)!;

            var dstActive = new Scene("dstActive");
            SceneSerializer.FromData(dstActive, actLoaded);
            Check("the active preset key is restored", dstActive.ActivePostKey == "autumn");
            Check("the active preset values are reapplied (from the .post)",
                dstActive.Post.Temperature == 15f && dstActive.Post.Saturation == -2f
                && dstActive.Post.BloomIntensity == 0.6f);

            actLoaded.ActivePost = "missingPreset";
            var dstMissing = new Scene("dstMissing");
            SceneSerializer.FromData(dstMissing, actLoaded);
            Check("a missing active preset clears the key and keeps the scene values",
                dstMissing.ActivePostKey == null && dstMissing.Post.BloomIntensity == 0.6f);

            dstActive.ClearPost();
            Check("ClearPost clears the key and goes neutral (zero cost)",
                dstActive.ActivePostKey == null && dstActive.Post.IsNeutral);
        }
        Check("C.Sprite.CastShadow restored", rsc != null && rsc.CastShadow);
        Check("C.Sprite.ShadowScale restored", rsc != null && rsc.ShadowScale == 0.5f);
        var rfl = dst.FindEntity("F")?.GetComponent<Light2D>();
        Check("F.Light2D rectangle restored (size, feather)",
            rfl != null && rfl.Shape == LightShape.Rect
            && rfl.RectSize == new Vector2(120, 40) && rfl.Feather == 0.6f);

        var legacyData = new SceneData();
        legacyData.Entities.Add(new EntityData
        {
            Id = 1,
            Name = "LegacyBox",
            Components = new System.Collections.Generic.List<ComponentData>
            {
                new TransformData { X = 10, Y = 20, ScaleX = 118, ScaleY = 8 },
                new ColliderData(),
                new SpriteData { Texture = "no-such-guid" },
            }
        });
        var lpath = Path.Combine(Path.GetTempPath(), "pc_selftest_legacy.scene");
        SaveFixture(legacyData, lpath);
        var lloaded = SceneSerializer.LoadFromFile(lpath);
        Check("the old format parses (scaleX is read)", lloaded != null);
        if (lloaded != null)
        {
            var lscene = new Scene("legacy");
            SceneSerializer.FromData(lscene, lloaded);
            lscene.Update(0f);
            var lb = lscene.FindEntity("LegacyBox");
            var lcol = lb?.GetComponent<BoxCollider2D>();
            var lsr = lb?.GetComponent<SpriteRenderer>();
            Check("old format: the collider Size is inherited (118,8)", lcol != null && lcol.Size == new Vector2(118, 8));
            Check("old format: the sprite DrawSize is inherited (118,8)", lsr != null && lsr.DrawSize == new Vector2(118, 8));
        }

        TestPrefabInstance();

        TestLocalEntityReanchor();

        TestPivotAnchorMigration();

        TestSerializedIdStability();
        TestEntityIdNoReuse();
        TestUnresolvedRefsSurvive();
        TestSameTypeOverrides();
        TestLegacyKeysDontLeakToOverrides();

        TestNonAsciiNotEscaped();
        TestNameFollowsFilename();
        TestDerivedValuesNotSerialized();
        TestSceneBackColor();
#if DEBUG
        TestSpriteRefMissingBarks();
        TestSpriteOverrideOnInstance();
#endif

        var real = "Content/Scenes/Village.scene";
        Check("Village.scene exists (the subject of the real-file load check)", File.Exists(real));
        if (File.Exists(real))
        {
            Console.WriteLine("--- real file load: " + real + " ---");
            var rd = SceneSerializer.LoadFromFile(real);
            Check("Village.scene parses", rd != null);
            if (rd != null)
            {
                Console.WriteLine("  file entities: " + rd.Entities.Count + "");
                var realScene = new Scene("real");
                SceneSerializer.FromData(realScene, rd);
                realScene.Update(0f);
                Console.WriteLine("  scene entity count after load: " + realScene.Entities.Count);
                Check("the Player entity loads", realScene.FindPlayer() != null);
            }
        }

        TestSortingGroupOrder();

        Report();
    }

    private static void TestSortingGroupOrder()
    {
        Console.WriteLine("--- sorting group draw order ---");

        var scene = new Scene("SortTest");

        var shelf = scene.CreateEntity("Shelf");
        shelf.GetComponent<Transform>()!.Position = new Vector2(0, 200);
        var shelfSr = shelf.AddComponent<SpriteRenderer>();

        var snack = scene.CreateEntity("Snack");
        snack.SetParent(shelf);
        snack.GetComponent<Transform>()!.Position = new Vector2(0, 160);
        var snackSr = snack.AddComponent<SpriteRenderer>();
        snackSr.SortOffset = 1f;

        var walker = scene.CreateEntity("Walker");
        var walkerSr = walker.AddComponent<SpriteRenderer>();

        scene.FlushPendingAdds();

        walker.GetComponent<Transform>()!.Position = new Vector2(0, 999);
        var order = scene.BuildRenderOrder();
        Check("with no group the snack draws before the shelf (behind it - the original bug)",
            order.IndexOf(snackSr) < order.IndexOf(shelfSr));

        var group = shelf.AddComponent<PixelCore.Runtime.Components.SortingGroup>();
        order = scene.BuildRenderOrder();
        Check("a group collapses to one buffer entry (not two for the shelf and the snack)", order.Count == 2);

        var folded = order.FirstOrDefault(r => Scene.GroupMembersOf(r) != null);
        var members = folded == null ? null : Scene.GroupMembersOf(folded);
        Check("two collapsed members", members != null && members.Count == 2);
        Check("the order inside a group is by Order (shelf then snack), not by foot Y",
            members != null && members.Count == 2
            && ReferenceEquals(members[0], shelfSr) && ReferenceEquals(members[1], snackSr));

        walker.GetComponent<Transform>()!.Position = new Vector2(0, 195);
        order = scene.BuildRenderOrder();
        Check("a person at foot 195 is behind the group (below the snack at 160 but above the shelf at 200)",
            order.IndexOf(walkerSr) < order.IndexOf(folded!));

        walker.GetComponent<Transform>()!.Position = new Vector2(0, 205);
        order = scene.BuildRenderOrder();
        Check("a person at foot 205 is in front of the group (the whole mass is passed)",
            order.IndexOf(walkerSr) > order.IndexOf(folded!));

        walker.GetComponent<Transform>()!.Position = new Vector2(0, 200);
        order = scene.BuildRenderOrder();
        int wi = order.IndexOf(walkerSr), gi = order.IndexOf(folded!);
        Check("an equal foot value cannot wedge into a group (either in front of or behind the mass)",
            wi >= 0 && gi >= 0 && Math.Abs(wi - gi) == 1);

        group.RenderLayer = RenderLayers.BelowEntities;
        order = scene.BuildRenderOrder(RenderLayers.Entities, int.MaxValue);
        Check("moving a group to BelowEntities removes it entirely from the Entities range draw",
            order.Count == 1 && ReferenceEquals(order[0], walkerSr));

        group.RenderLayer = RenderLayers.Entities;
        var tray = scene.CreateEntity("Tray");
        tray.SetParent(shelf);
        tray.GetComponent<Transform>()!.Position = new Vector2(0, 150);
        var trayGroup = tray.AddComponent<PixelCore.Runtime.Components.SortingGroup>();
        trayGroup.SortOffset = 2f;
        tray.AddComponent<SpriteRenderer>();
        var cup = scene.CreateEntity("Cup");
        cup.SetParent(tray);
        cup.AddComponent<SpriteRenderer>().SortOffset = 1f;
        scene.FlushPendingAdds();

        order = scene.BuildRenderOrder();
        folded = order.FirstOrDefault(r => Scene.GroupMembersOf(r) is { Count: 3 });
        members = folded == null ? null : Scene.GroupMembersOf(folded);
        Check("nesting: the outer group has three members (shelf, snack, tray mass)", members != null);
        Check("nesting: the tray goes in as an inner mass (two including the cup)",
            members != null && Scene.GroupMembersOf(members[2]) is { Count: 2 });

        TestDrawListPickOrder();
    }

    private static void TestDrawListPickOrder()
    {
        Console.WriteLine("--- expanded draw order (the editor picking key) ---");

        var scene = new Scene("PickOrder");

        var shelf = scene.CreateEntity("Shelf");
        shelf.GetComponent<Transform>()!.Position = new Vector2(0, 200);
        var shelfSr = shelf.AddComponent<SpriteRenderer>();

        var snack = scene.CreateEntity("Snack");
        snack.SetParent(shelf);
        snack.GetComponent<Transform>()!.Position = new Vector2(0, 160);
        var snackSr = snack.AddComponent<SpriteRenderer>();
        snackSr.SortOffset = 1f;

        var walker = scene.CreateEntity("Walker");
        walker.GetComponent<Transform>()!.Position = new Vector2(0, 195);
        var walkerSr = walker.AddComponent<SpriteRenderer>();

        scene.FlushPendingAdds();

        var flat = scene.BuildDrawList();
        Check("with no group there are three separate items", flat.Count == 3);
        Check("with no group the order is by own Y (snack 160, person 195, shelf 200)",
            flat.IndexOf(snackSr) < flat.IndexOf(walkerSr) &&
            flat.IndexOf(walkerSr) < flat.IndexOf(shelfSr));

        shelf.AddComponent<PixelCore.Runtime.Components.SortingGroup>();
        flat = scene.BuildDrawList();
        Check("expanded, a group still gives three separate items (it is not counted as a mass)", flat.Count == 3);
        Check("a group prop (the snack) follows the mass and draws in front of the person",
            flat.IndexOf(snackSr) > flat.IndexOf(walkerSr));
        Check("the order inside the mass survives (shelf then snack)",
            flat.IndexOf(shelfSr) < flat.IndexOf(snackSr));
        Check("guard check: sorted by own Y the snack would have been behind the person (160 < 195)",
            snackSr.SortY < walkerSr.SortY);

        var tray = scene.CreateEntity("Tray");
        tray.SetParent(shelf);
        tray.AddComponent<PixelCore.Runtime.Components.SortingGroup>().SortOffset = 2f;
        var traySr = tray.AddComponent<SpriteRenderer>();
        var cup = scene.CreateEntity("Cup");
        cup.SetParent(tray);
        var cupSr = cup.AddComponent<SpriteRenderer>();
        cupSr.SortOffset = 1f;
        scene.FlushPendingAdds();

        flat = scene.BuildDrawList();
        Check("a nested group expands down to separate items too (five)", flat.Count == 5);
        Check("a nested prop (the cup) is in the list - without it, a click is pushed to the back forever",
            flat.Contains(cupSr) && flat.Contains(traySr));
        Check("nested order: shelf, snack, tray, cup",
            flat.IndexOf(shelfSr) < flat.IndexOf(snackSr) &&
            flat.IndexOf(snackSr) < flat.IndexOf(traySr) &&
            flat.IndexOf(traySr) < flat.IndexOf(cupSr));

        tray.Active = false;
        flat = scene.BuildDrawList();
        Check("a disabled group drops out of the expanded list (both the tray and the cup)",
            !flat.Contains(traySr) && !flat.Contains(cupSr));
    }

    private static void TestPrefabInstance()
    {
        Console.WriteLine("--- prefab instances and sparse overrides ---");

        var prefabData = new SceneData { SchemaVersion = SceneData.CurrentSchemaVersion, Name = "TestPrefab", Kind = PixelCore.Runtime.Core.SceneKind.Prefab };
        prefabData.Entities.Add(new EntityData
        {
            Id = 1,
            Name = "PRoot",
            Components = new System.Collections.Generic.List<ComponentData>
            {
                new TransformData { X = 0, Y = 0 },
                new InteractableData { Prompt = "door", Kind = "Door", Range = 10f },
            }
        });
        prefabData.Entities.Add(new EntityData
        {
            Id = 2,
            ParentId = 1,
            Name = "PChild",
            Components = new System.Collections.Generic.List<ComponentData>
            {
                new TransformData { X = 8, Y = 4 },
                new CircleColliderData { Radius = 5f },
            }
        });
        var prefabPath = Path.Combine(Path.GetTempPath(), "pc_selftest_prefab.scene");
        SaveFixture(prefabData, prefabPath);
        Check("the prefab kind round-trips",
            SceneSerializer.LoadFromFile(prefabPath)?.Kind == PixelCore.Runtime.Core.SceneKind.Prefab);

        var host = new Scene("HostScene");
        var door = host.CreateEntity("Door1");
        door.GetComponent<Transform>()!.Position = new Vector2(100, 50);
        var inst = door.AddComponent<SceneInstance>();
        inst.ScenePath = prefabPath;
        inst.Load();
        host.Update(0f);

        var pRoot = door;
        var pChild = host.FindEntity("PChild");
        Check("root attachment: the base root is applied onto the host (no separate entity)",
              host.FindEntity("PRoot") == null && pRoot.GetComponent<Interactable>() != null);
        Check("children are still created (PChild)", pChild != null);
        if (pChild == null) return;
        Check("the host position keeps the scene-authored value (100,50)", pRoot.GetComponent<Transform>()!.Position == new Vector2(100, 50));
        Check("host offset: PChild is (108,54)", pChild.GetComponent<Transform>()!.Position == new Vector2(108, 54));
        Check("the prefab's internal hierarchy is restored (PChild.Parent is the host)", pChild.Parent == pRoot);
        Check("only the children are excluded from serialization (the host is scene-authored)",
              !pRoot.HideFromSerialization && pChild.HideFromSerialization);

        pRoot.GetComponent<Interactable>()!.Prompt = "openIt";
        pChild.GetComponent<Transform>()!.Position = new Vector2(120, 60);
        pChild.GetComponent<CircleCollider2D>()!.Radius = 9f;
        pRoot.AddComponent<Light2D>().Radius = 77f;

        var hostData = SceneSerializer.ToData(host);
        Check("host save: instance children are excluded (one entity)", hostData.Entities.Count == 1);
        SceneInstanceData? sid = null;
        foreach (var cd in hostData.Entities[0].Components)
            if (cd is SceneInstanceData s) sid = s;
        Check("SceneInstanceData captured plus overrides on two entities", sid?.Overrides != null && sid!.Overrides!.Count == 2);
        if (sid == null) return;

        var ovJsonBefore = System.Text.Json.JsonSerializer.Serialize(sid, SceneJsonContext.Default.ComponentData);
        var shift = new Vector2(37, -13);
        door.GetComponent<Transform>()!.Position += shift;
        pChild.GetComponent<Transform>()!.Position += shift;
        var hostData2 = SceneSerializer.ToData(host);
        SceneInstanceData? sid2 = null;
        foreach (var cd in hostData2.Entities[0].Components)
            if (cd is SceneInstanceData s) sid2 = s;
        if (sid2 == null) { Check("SceneInstanceData captured after moving", false); return; }
        var ovJsonAfter = System.Text.Json.JsonSerializer.Serialize(sid2, SceneJsonContext.Default.ComponentData);
        Check("moving the whole instance leaves the override JSON identical (local normalisation)", ovJsonBefore == ovJsonAfter);

        var hostPath = Path.Combine(Path.GetTempPath(), "pc_selftest_host.scene");
        SaveFixture(hostData2, hostPath);
        var loadedHost = SceneSerializer.LoadFromFile(hostPath);
        Check("the host file parses", loadedHost != null);
        if (loadedHost == null) return;

        var dst = new Scene("dstHost");
        SceneSerializer.FromData(dst, loadedHost);
        dst.Update(0f);
        var rDoor = dst.FindEntity("Door1");
        var rChild = dst.FindEntity("PChild");
        Check("reload: root attachment plus recreated children", rDoor != null && rChild != null);
        if (rDoor == null || rChild == null) return;
        var rRoot = rDoor;

        var doorPos = rDoor.GetComponent<Transform>()!.Position;
        Check("reload: the overridden field (Prompt)", rRoot.GetComponent<Interactable>()?.Prompt == "openIt");
        Check("reload: fields kept from the base (Kind=Door, Range=10)",
            rRoot.GetComponent<Interactable>()?.Kind == InteractKind.Door
            && rRoot.GetComponent<Interactable>()?.Range == 10f);
        Check("reload: the added component (Light2D r77)", rRoot.GetComponent<Light2D>()?.Radius == 77f);
        Check("reload: the position override (local 20,10 plus the new host)",
            rChild.GetComponent<Transform>()!.Position == doorPos + new Vector2(20, 10));
        Check("reload: the collider override (r9)", rChild.GetComponent<CircleCollider2D>()?.Radius == 9f);
        Check("reload: the hierarchy survives (PChild.Parent is PRoot)", rChild.Parent == rRoot);

        dst.DestroyEntity(rChild);
        rRoot.RemoveComponent<Interactable>();
        dst.Update(0f);
        var hostData3 = SceneSerializer.ToData(dst);
        SaveFixture(hostData3, hostPath);
        var dst2 = new Scene("dstHost2");
        var loaded3 = SceneSerializer.LoadFromFile(hostPath);
        if (loaded3 != null) SceneSerializer.FromData(dst2, loaded3);
        dst2.Update(0f);
        Check("reload: the entity delete override (no PChild)", dst2.FindEntity("PChild") == null);
        var r2Root = dst2.FindEntity("Door1");
        Check("reload: the component removal override (no Interactable)",
            r2Root != null && r2Root.GetComponent<Interactable>() == null);
        Check("reload: a removal leaves the added component intact (Light2D)", r2Root?.GetComponent<Light2D>()?.Radius == 77f);
    }

    private static void TestLocalEntityReanchor()
    {
        Console.WriteLine("--- re-anchoring a local entity (duplicated under an instance child) ---");

        var prefabPath = Path.Combine(Path.GetTempPath(), "pc_selftest_prefab.scene");
        var host = new Scene("ReanchorHost");
        var door = host.CreateEntity("Door1");
        door.GetComponent<Transform>()!.Position = new Vector2(100, 50);
        var inst = door.AddComponent<SceneInstance>();
        inst.ScenePath = prefabPath;
        inst.Load();
        host.Update(0f);

        var pRoot = host.FindEntity("PChild");
        if (pRoot == null) { Check("re-anchor: the instance children load", false); return; }
        Check("re-anchor: the instance children load", true);

        var local = host.CreateEntity("LocalCopy");
        local.SetParent(pRoot);
        local.GetComponent<Transform>()!.Position = new Vector2(111, 55);
        local.AddComponent<CircleCollider2D>().Radius = 3f;
        host.Update(0f);

        var data = SceneSerializer.ToData(host);
        Check("re-anchor: the local entity is saved (two entities)", data.Entities.Count == 2);
        EntityData? localData = null;
        foreach (var ed in data.Entities) if (ed.Name == "LocalCopy") localData = ed;
        Check("re-anchor: ParentId is the host (the hidden PRoot is skipped)",
            localData != null && door.SerializedId != 0 && localData.ParentId == door.SerializedId);

        var path = Path.Combine(Path.GetTempPath(), "pc_selftest_reanchor.scene");
        SaveFixture(data, path);
        var dst = new Scene("ReanchorDst");
        var loaded = SceneSerializer.LoadFromFile(path);
        if (loaded == null) { Check("re-anchor: the file parses", false); return; }
        SceneSerializer.FromData(dst, loaded);
        dst.Update(0f);

        var rLocal = dst.FindEntity("LocalCopy");
        var rDoor = dst.FindEntity("Door1");
        Check("re-anchor reload: the local entity survives", rLocal != null && rDoor != null);
        if (rLocal == null || rDoor == null) return;
        Check("re-anchor reload: the parent is the host (no fall to the root)", rLocal.Parent == rDoor);
        Check("re-anchor reload: the world position survives (111,55)",
            rLocal.GetComponent<Transform>()!.Position == new Vector2(111, 55));
        Check("re-anchor reload: the component survives (r3)", rLocal.GetComponent<CircleCollider2D>()?.Radius == 3f);
    }

#if DEBUG
    private static void TestSpriteRefMissingBarks()
    {
        Console.WriteLine("--- does a lost sprite reference report ---");

        var root = Path.Combine(Path.GetTempPath(), "pixelcore_spriteref_" + System.Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            using var _ = Assets.AssetRegistry.UseTemporary(Path.Combine(root, "assets.json"));
            var liveAtlas = Assets.AssetRegistry.Instance.GetOrCreateId("Sprites/Characters/Hero/Walk.atlas");

            var bark = CaptureStderr(() => ApplySprite(new SpriteData { Atlas = "aaaa0001", Slice = "Idle" }));
            Check("* an unregistered atlas id is reported (this used to be zero log lines)",
                bark.Contains("atlas asset id 'aaaa0001'"), bark);

            bark = CaptureStderr(() => ApplySprite(new SpriteData { Texture = "aaaa0002" }));
            Check("* an unregistered texture id is reported", bark.Contains("texture asset id 'aaaa0002'"), bark);

            bark = CaptureStderr(() => ApplySprite(new SpriteData { Atlas = liveAtlas, Slice = "" }));
            Check("* an atlas found with an empty slice name is reported",
                bark.Contains("the slice name is empty"), bark);

            bark = CaptureStderr(() =>
            {
                ApplySprite(new SpriteData { Atlas = "aaaa0003", Slice = "Idle" });
                ApplySprite(new SpriteData { Atlas = "aaaa0003", Slice = "Idle" });
                ApplySprite(new SpriteData { Atlas = "aaaa0003", Slice = "Idle" });
            });
            Check("the same id reports once (three calls, one line)", CountOccurrences(bark, "aaaa0003") == 1, bark);

            bark = CaptureStderr(() => ApplySprite(new SpriteData { Atlas = "aaaa0004", Slice = "Idle" }));
            Check("* a different id reports again (the latch is per id)",
                bark.Contains("aaaa0004"), bark);

            bark = CaptureStderr(() => ApplySprite(new SpriteData { Atlas = liveAtlas, Slice = "Idle" }));
            Check("a reference that resolves says nothing", bark.Length == 0, bark);
            bark = CaptureStderr(() => ApplySprite(new SpriteData()));
            Check("an empty sprite with no image at all is silent too (a reset is legitimate authoring)",
                bark.Length == 0, bark);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    private static void TestSpriteOverrideOnInstance()
    {
        Console.WriteLine("--- instance image overrides (colliders stay bound to the base) ---");

        static string? NodeType(System.Text.Json.Nodes.JsonObject n)
            => n.TryGetPropertyValue("type", out var t) ? t?.GetValue<string>() : null;

        var root = Path.Combine(Path.GetTempPath(), "pixelcore_spriteov_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            using var _ = Assets.AssetRegistry.UseTemporary(Path.Combine(root, "assets.json"));
            var reg = Assets.AssetRegistry.Instance;
            var texA = reg.GetOrCreateId("Sprites/Test/DeskA.png");
            const string pathB = "Sprites/Test/DeskB.png";

            var prefabData = new SceneData { SchemaVersion = SceneData.CurrentSchemaVersion, Name = "Desk", Kind = SceneKind.Prefab };
            var baseCollider = new CircleColliderData { Radius = 5f };
            prefabData.Entities.Add(new EntityData
            {
                Id = 1, Name = "Desk",
                Components = new List<ComponentData>
                {
                    new TransformData { X = 0, Y = 0 },
                    new SpriteData { Texture = texA },
                    baseCollider,
                }
            });
            var prefabPath = Path.Combine(root, "Desk.scene");
            SaveFixture(prefabData, prefabPath);

            var host = new Scene("Room");
            var desk1 = host.CreateEntity("Desk");
            var si1 = desk1.AddComponent<SceneInstance>(); si1.ScenePath = prefabPath; si1.Load();
            var desk2 = host.CreateEntity("Desk");
            desk2.GetComponent<Transform>()!.Position = new Vector2(40, 0);
            var si2 = desk2.AddComponent<SceneInstance>(); si2.ScenePath = prefabPath; si2.Load();
            host.Update(0f);

            var sr2 = desk2.GetComponent<SpriteRenderer>();
            Check("premise: root attachment - a SpriteRenderer is applied to the host", si2.RootMerged && sr2 != null);
            if (sr2 == null) return;

            sr2.TexturePath = pathB; sr2.AtlasPath = null; sr2.SliceName = null; sr2.SourceRect = null;

            var ov2 = InstanceOverrides.Compute(si2);
            Check("* an instance with only the image changed: one override entity", ov2 != null && ov2.Count == 1,
                  ov2 == null ? "null" : ov2.Count.ToString());
            if (ov2 == null || ov2.Count != 1) return;
            var comps = ov2[0].Components;
            Check("* one sprite node only - no collider or Transform tags along, and no Removed",
                  comps != null && comps.Count == 1 && NodeType(comps[0]) == "sprite" && ov2[0].Removed == null,
                  comps == null ? "null" : string.Join(",", comps.Select(NodeType)) + $" removed={ov2[0].Removed?.Count ?? 0}");
            if (comps == null || comps.Count != 1) return;
            var keys = comps[0].Where(kv => kv.Key != "type").Select(kv => kv.Key).ToList();
            Check("* that node has one key, texture - the pivot, size and tint do not harden with it",
                  keys.Count == 1 && keys[0] == "texture", string.Join(",", keys));
            var idB = reg.GetId(pathB);
            Check("the texture value is the new image's id (not a path)",
                  idB != null && comps[0]["texture"]?.GetValue<string>() == idB,
                  comps[0]["texture"]?.ToJsonString());

            var ov1 = InstanceOverrides.Compute(si1);
            Check("control: an untouched instance has no overrides", ov1 == null,
                  ov1 == null ? null : string.Join(" | ", ov1.Select(o =>
                      $"e{o.EntityId}: " + string.Join(",", (o.Components ?? new()).Select(c => c.ToJsonString()))
                      + (o.Removed != null ? " removed=" + string.Join(",", o.Removed) : ""))));

            si2.Overrides = ov2;
            si1.Overrides = InstanceOverrides.Compute(si1);
            baseCollider.Radius = 9f;
            SaveFixture(prefabData, prefabPath);
            si1.Load(); si2.Load();
            host.Update(0f);
            var c1 = desk1.GetComponent<CircleCollider2D>(); var c2 = desk2.GetComponent<CircleCollider2D>();
            var s1 = desk1.GetComponent<SpriteRenderer>();  var s2 = desk2.GetComponent<SpriteRenderer>();
            Check("* a base collider change propagates to both instances (r5 to r9)",
                  c1?.Radius == 9f && c2?.Radius == 9f, $"{c1?.Radius}/{c2?.Radius}");
            Check("* the image override survives a reload (desk 2 is B)",
                  s2?.TexturePath == reg.GetPath(idB!), s2?.TexturePath);
            Check("control: desk 1 keeps the base image (A)",
                  s1?.TexturePath == reg.GetPath(texA), s1?.TexturePath);

            var again = InstanceOverrides.Compute(si2);
            var againKeys = again?.Count == 1 && again[0].Components?.Count == 1
                ? again[0].Components![0].Where(kv => kv.Key != "type").Select(kv => kv.Key).ToList()
                : null;
            Check("recomputing still gives one texture key (it does not grow per save)",
                  againKeys != null && againKeys.Count == 1 && againKeys[0] == "texture",
                  againKeys == null ? "shape differs" : string.Join(",", againKeys));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

#endif

    private static void ApplySprite(SpriteData data)
    {
        var scene = new Scene("spriteRef");
        var e = scene.CreateEntity("imageEntity");
        scene.Update(0f);
        data.Apply(e);
    }

    private static void TestDerivedValuesNotSerialized()
    {
        Console.WriteLine("--- derived values do not reach the file ---");

        var dir = Path.Combine(Path.GetTempPath(), "pixelcore_derived_" + System.Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var scene = new Scene("derived");
            var e = scene.CreateEntity("image");
            e.AddComponent<SpriteRenderer>().RenderLayer = RenderLayers.Entities;
            scene.Update(0f);

            var captured = SceneSerializer.ToData(scene);
            var sprite = captured.Entities[0].Components.OfType<SpriteData>().First();
            Check("* the live capture does not fill ySort (so no phantom override appears)",
                  sprite.YSort == null, sprite.YSort?.ToString() ?? "null");

            var scenePath = Path.Combine(dir, "derived.scene");
            SaveFixture(captured, scenePath);
            var rawScene = File.ReadAllText(scenePath);
            Check("* the saved scene bytes contain no ySort", !rawScene.Contains("\"ySort\""), rawScene);

            var legacy = new SceneData { Name = "legacy" };
            legacy.Entities.Add(new EntityData
            {
                Id = 1, Name = "Backdrop",
                Components = { new SpriteData { YSort = false, RenderLayer = RenderLayers.Entities } },
            });
            var legacyPath = Path.Combine(dir, "legacy.scene");
            SceneSerializer.SaveToFile(legacy, legacyPath);
            File.WriteAllText(legacyPath, File.ReadAllText(legacyPath)
                .Replace("\"renderLayer\": 0", "\"renderLayer\": 0, \"ySort\": false"));
            var back = SceneSerializer.LoadFromFile(legacyPath);
            var backSprite = back?.Entities[0].Components.OfType<SpriteData>().First();
            Check("* an old file's ySort is still read (it is not written, but it is not unused)",
                  backSprite?.YSort == false, backSprite?.YSort?.ToString() ?? "null");

            var migrated = new Scene("mig");
            SceneSerializer.FromData(migrated, back!);
            migrated.Update(0f);
            Check("* the layer migration still runs from that value (Entities to BelowEntities)",
                  migrated.FindEntity("Backdrop")?.GetComponent<SpriteRenderer>()?.RenderLayer
                      == RenderLayers.BelowEntities);

            var ov = new SceneData { Name = "ov" };
            ov.Entities.Add(new EntityData
            {
                Id = 1, Name = "Host",
                Components = { new SceneInstanceData { SceneId = "aabbccdd", Overrides = new() { new InstanceOverrideData { EntityId = 7 } } } },
            });
            var ovPath = Path.Combine(dir, "ov.scene");
            SaveFixture(ov, ovPath);
            Check("* the saved scene bytes contain no isEmpty",
                  !File.ReadAllText(ovPath).Contains("\"isEmpty\""), File.ReadAllText(ovPath));

            var anim = new Animation.AnimationData { Name = "clip", Loop = false };
            anim.Frames.Add(new Animation.FrameData { Duration = 0.2f });
            anim.Frames.Add(new Animation.FrameData { Duration = 0.2f });
            var animPath = Path.Combine(dir, "clip.anim");
            anim.Save(animPath);
            Check("* the saved .anim bytes contain no TotalDuration",
                  !File.ReadAllText(animPath).Contains("TotalDuration"), File.ReadAllText(animPath));
            var animBack = Animation.AnimationData.Load(animPath);
            Check("derived values are alive after load through computation (not deleted, just not written)",
                  animBack != null && Math.Abs(animBack.TotalDuration - 0.4f) < 1e-6f,
                  animBack?.TotalDuration.ToString());
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static void TestSceneBackColor()
    {
        Console.WriteLine("--- scene background colour (border and letterbox) ---");

        var dir = Path.Combine(Path.GetTempPath(), "pixelcore_backcolor_" + System.Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var fresh = new Scene("fresh");
            Check("* a new scene's BackColor default is pure black",
                  fresh.BackColor == Microsoft.Xna.Framework.Color.Black, fresh.BackColor.ToString());
            Check("* control: it is not the old hardcoded (30,30,30)",
                  fresh.BackColor != new Microsoft.Xna.Framework.Color(30, 30, 30));

            var blackPath = Path.Combine(dir, "black.scene");
            SaveFixture(SceneSerializer.ToData(fresh), blackPath);
            var blackRaw = File.ReadAllText(blackPath);
            Check("* a black scene's saved bytes contain no backR/G/B",
                  !blackRaw.Contains("\"backR\"") && !blackRaw.Contains("\"backG\"") && !blackRaw.Contains("\"backB\""),
                  blackRaw);

            var tinted = new Scene("tinted");
            tinted.BackColor = new Microsoft.Xna.Framework.Color(200, 210, 190);
            var tintedPath = Path.Combine(dir, "tinted.scene");
            SaveFixture(SceneSerializer.ToData(tinted), tintedPath);
            var tintedRaw = File.ReadAllText(tintedPath);
            Check("* a non-black scene writes backR/G/B to the file",
                  tintedRaw.Contains("\"backR\": 200") && tintedRaw.Contains("\"backG\": 210")
                  && tintedRaw.Contains("\"backB\": 190"), tintedRaw);

            var backTinted = new Scene("x");
            SceneSerializer.FromData(backTinted, SceneSerializer.LoadFromFile(tintedPath)!);
            Check("* non-black round trip (200,210,190)",
                  backTinted.BackColor == new Microsoft.Xna.Framework.Color(200, 210, 190),
                  backTinted.BackColor.ToString());

            var oneCh = new Scene("onech");
            oneCh.BackColor = new Microsoft.Xna.Framework.Color(0, 0, 40);
            var oneChPath = Path.Combine(dir, "onech.scene");
            SaveFixture(SceneSerializer.ToData(oneCh), oneChPath);
            var oneChRaw = File.ReadAllText(oneChPath);
            Check("* with only one channel non-zero, only that channel is written",
                  oneChRaw.Contains("\"backB\": 40") && !oneChRaw.Contains("\"backR\""), oneChRaw);

            var backOneCh = new Scene("y");
            SceneSerializer.FromData(backOneCh, SceneSerializer.LoadFromFile(oneChPath)!);
            Check("* a partial omission round-trips exactly (0,0,40)",
                  backOneCh.BackColor == new Microsoft.Xna.Framework.Color(0, 0, 40),
                  backOneCh.BackColor.ToString());

            var legacyPath = Path.Combine(dir, "legacy.scene");
            SaveFixture(new SceneData { Name = "legacy" }, legacyPath);
            Check("premise: the old scene fixture has no backR/G/B",
                  !File.ReadAllText(legacyPath).Contains("\"back"));
            var legacyScene = new Scene("z");
            legacyScene.BackColor = new Microsoft.Xna.Framework.Color(99, 99, 99);
            SceneSerializer.FromData(legacyScene, SceneSerializer.LoadFromFile(legacyPath)!);
            Check("* an old scene (no field) loads as pure black",
                  legacyScene.BackColor == Microsoft.Xna.Framework.Color.Black,
                  legacyScene.BackColor.ToString());
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static void TestNameFollowsFilename()
    {
        Console.WriteLine("--- the file name is the truth for a name (.scene/.post/.surface) ---");

        var dir = Path.Combine(Path.GetTempPath(), "pixelcore_name_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var scenePath = Path.Combine(dir, "newName.scene");
            SceneSerializer.SaveToFile(new SceneData { Name = "oldName" }, scenePath);
            Check("saving writes the inner name as-is", File.ReadAllText(scenePath).Contains("oldName"));

            var loaded = SceneSerializer.LoadFromFile(scenePath);
            Check("* loading a .scene corrects it to the file name (the value FindSceneIdByName matches)",
                loaded?.Name == "newName", loaded?.Name);

            var postPath = Path.Combine(dir, "Sünset.post");
            File.WriteAllText(postPath, "{\"id\":\"aabbccdd\",\"name\":\"oldPreset\"}");
            var post = Assets.PostAsset.Load(postPath);
            Check("* loading a .post corrects it to the file name",
                post?.Name == "Sünset", post?.Name);

            var surfPath = Path.Combine(dir, "asphalt.surface");
            File.WriteAllText(surfPath, "{\"id\":\"11223344\",\"name\":\"oldSurface\"}");
            var surf = Audio.SurfaceAsset.Load(surfPath);
            Check("* loading a .surface corrects it to the file name", surf?.Name == "asphalt", surf?.Name);

            var quiet = Path.Combine(dir, "matchingName.scene");
            SceneSerializer.SaveToFile(new SceneData { Name = "matchingName" }, quiet);
            var saved = Console.Out;
            var buf = new System.Text.StringBuilder();
            try { Console.SetOut(new StringWriter(buf)); SceneSerializer.LoadFromFile(quiet); }
            finally { Console.SetOut(saved); }
            Check("a matching name says nothing", buf.Length == 0, buf.ToString());
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static void TestNonAsciiNotEscaped()
    {
        Console.WriteLine("--- non-ASCII escaping (file bytes) ---");

        var data = new SceneData { Name = "Vïllage" };
        data.Entities.Add(new EntityData
        {
            Id = 1,
            Name = "Anchor_bèd",
            Components = { new CutsceneTriggerData { CutsceneId = "Vïllage.wakeUp", Once = true } },
        });

        var path = Path.Combine(Path.GetTempPath(), "pixelcore_nonascii_" + System.Guid.NewGuid().ToString("N") + ".scene");
        try
        {
            SaveFixture(data, path);
            var raw = File.ReadAllText(path);

            Check("a non-ASCII entity name is written raw", raw.Contains("Anchor_bèd"));
            Check("a non-ASCII cutscene id is written raw", raw.Contains("Vïllage.wakeUp"));
            Check("the file contains no \\u escapes", !raw.Contains("\\u"));

            var back = SceneSerializer.LoadFromFile(path);
            var ct = back?.Entities[0].Components[0] as CutsceneTriggerData;
            Check("a raw file loads normally", back?.Entities[0].Name == "Anchor_bèd" && ct?.CutsceneId == "Vïllage.wakeUp");

            File.WriteAllText(path, raw.Replace("Vïllage.wakeUp", "V\\u00EFllage.wakeUp"));
            var legacy = SceneSerializer.LoadFromFile(path);
            var lct = legacy?.Entities[0].Components[0] as CutsceneTriggerData;
            Check("an old escaped file still reads (backward compatibility)", lct?.CutsceneId == "Vïllage.wakeUp");
        }
        finally { if (File.Exists(path)) File.Delete(path); }

        TestPostNonAsciiNotEscaped();
    }

    private static void TestPostNonAsciiNotEscaped()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pixelcore_post_nonascii_" + System.Guid.NewGuid().ToString("N"));
        var file = Path.Combine(dir, "Sünset.post");
        try
        {
            new Assets.PostAsset { Id = "abcd1234", Name = "Sünset" }.Save(file);
            var raw = File.ReadAllText(file);

            Check("* a non-ASCII .post name is written raw", raw.Contains("Sünset"), Head(raw));
            Check("* the .post file contains no \\u escapes", !raw.Contains("\\u"), Head(raw));

            Check("a raw .post file loads normally", Assets.PostAsset.Load(file)?.Name == "Sünset");

            var legacy = Path.Combine(dir, "old.post");
            File.WriteAllText(legacy, "{\"id\":\"abcd1234\",\"name\":\"S\\u00FCnset\"}");
            Check("* an old escaped .post still reads (so no rewrite is needed)",
                Assets.PostAsset.Load(legacy) != null);
        }
        finally { try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { } }
    }

    private static string Head(string raw)
        => raw.Length <= 90 ? raw.Replace("\n", " ") : raw.Substring(0, 90).Replace("\n", " ") + "…";

    private static void TestSerializedIdStability()
    {
        var src = new SceneData { SchemaVersion = SceneData.CurrentSchemaVersion };
        var a = new EntityData { Id = 77, Name = "Base" };
        a.Components.Add(new TransformData { X = 1, Y = 2 });
        src.Entities.Add(a);
        var b = new EntityData { Id = 78, ParentId = 77, Name = "Child" };
        b.Components.Add(new TransformData { X = 3, Y = 4 });
        src.Entities.Add(b);

        var scene = new Scene("IdStable");
        SceneSerializer.FromData(scene, src);
        var out1 = SceneSerializer.ToData(scene);
        int? idBase = null, idChild = null;
        foreach (var ed in out1.Entities)
        {
            if (ed.Name == "Base") idBase = ed.Id;
            if (ed.Name == "Child") { idChild = ed.Id; Check("stable ids: the parent link survives", ed.ParentId == 77); }
        }
        Check("stable ids: load then save leaves the id unchanged (77)", idBase == 77);
        Check("stable ids: the child id is unchanged (78)", idChild == 78);

        var scene2 = new Scene("IdStable2");
        SceneSerializer.FromData(scene2, out1);
        var out2 = SceneSerializer.ToData(scene2);
        bool same = out2.Entities.Count == out1.Entities.Count;
        for (int i = 0; same && i < out2.Entities.Count; i++)
            same = out2.Entities[i].Id == out1.Entities[i].Id
                && out2.Entities[i].ParentId == out1.Entities[i].ParentId;
        Check("stable ids: identical after two round trips", same);

        var fresh = scene.CreateEntity("New");
        scene.FlushPendingAdds();
        var out3 = SceneSerializer.ToData(scene);
        int newId = 0;
        foreach (var ed in out3.Entities) if (ed.Name == "New") newId = ed.Id;
        Check("stable ids: a new entity gets a new number (79)", newId == 79);
        Check("stable ids: the new number does not collide with an existing one", newId != 77 && newId != 78 && fresh.SerializedId == newId);
    }

    private static void TestEntityIdNoReuse()
    {
        var src = new SceneData { SchemaVersion = SceneData.CurrentSchemaVersion };
        foreach (var id in new[] { 77, 78 })
        {
            var ed = new EntityData { Id = id, Name = "E" + id };
            ed.Components.Add(new TransformData());
            src.Entities.Add(ed);
        }
        Check("premise: an old file has no mark (nextEntityId 0)", src.NextEntityId == 0);

        var scene = new Scene("NoReuse");
        SceneSerializer.FromData(scene, src);
        var out1 = SceneSerializer.ToData(scene);
        Check("* old file fallback: the first save recovers and writes the mark (78)", out1.NextEntityId == 78);

        var top = scene.FindEntity("E78");
        Check("premise: the highest-numbered entity was found", top != null);
        if (top != null) scene.DestroyEntity(top);
        scene.Update(0f);
        scene.CreateEntity("Fresh");
        scene.Update(0f);

        var out2 = SceneSerializer.ToData(scene);
        int freshId = 0;
        foreach (var ed in out2.Entities) if (ed.Name == "Fresh") freshId = ed.Id;
        Check($"* a deleted number is not reissued (79 rather than 78 - actual {freshId})", freshId == 79);
        Check("the mark follows upward (79)", out2.NextEntityId == 79);

        var fresh = scene.FindEntity("Fresh");
        if (fresh != null) scene.DestroyEntity(fresh);
        scene.Update(0f);
        var out3 = SceneSerializer.ToData(scene);
        Check("* the mark does not go down when an entity disappears (79 holds)", out3.NextEntityId == 79);
        scene.CreateEntity("Fresh2");
        scene.Update(0f);
        int fresh2 = 0;
        foreach (var ed in SceneSerializer.ToData(scene).Entities) if (ed.Name == "Fresh2") fresh2 = ed.Id;
        Check($"* so the next number is 80 (actual {fresh2})", fresh2 == 80);

        var pinned = new SceneData { SchemaVersion = SceneData.CurrentSchemaVersion, NextEntityId = 99 };
        foreach (var id in new[] { 77, 78 })
        {
            var ed = new EntityData { Id = id, Name = "P" + id };
            ed.Components.Add(new TransformData());
            pinned.Entities.Add(ed);
        }
        var scene4 = new Scene("Pinned");
        SceneSerializer.FromData(scene4, pinned);
        Check("* loading reads the mark from the file (99)", scene4.NextSerializedId == 99);
        scene4.CreateEntity("AfterLoad");
        scene4.Update(0f);
        int afterLoad = 0;
        foreach (var ed in SceneSerializer.ToData(scene4).Entities) if (ed.Name == "AfterLoad") afterLoad = ed.Id;
        Check($"* so a new number continues from the mark (100 rather than 79 - actual {afterLoad})", afterLoad == 100);

        var hand = new SceneData { SchemaVersion = SceneData.CurrentSchemaVersion, Name = "Hand" };
        foreach (var id in new[] { 5, 9 })
            hand.Entities.Add(new EntityData { Id = id, Name = "H" + id });
        Check("premise: hand-built data has a mark of 0", hand.NextEntityId == 0);

        var tmp = Path.Combine(Path.GetTempPath(), "pixelcore_idreuse_" + Guid.NewGuid().ToString("N") + ".scene");
        try
        {
            SceneSerializer.SaveToFile(hand, tmp);
            var reread = SceneSerializer.LoadFromFile(tmp);
            Check("* SaveToFile pins the mark (for doors like prefab conversion that skip ToData)",
                  reread != null && reread.NextEntityId == 9);
            Check("premise: that file was really written (two entities)", reread != null && reread.Entities.Count == 2);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    private static void TestUnresolvedRefsSurvive()
    {
        const string ghost = "deadbeef";
        Check("premise: this id is not in the registry (otherwise the rest is vacuous)",
              string.IsNullOrEmpty(Assets.AssetRegistry.Instance.GetPath(ghost)));

        var src = new SceneData { SchemaVersion = SceneData.CurrentSchemaVersion };
        var ed = new EntityData { Id = 1, Name = "Ghost" };
        ed.Components.Add(new TransformData());
        ed.Components.Add(new SpriteData { Texture = ghost });
        src.Entities.Add(ed);

        var scene = new Scene("GhostRef");
        CaptureStderr(() => SceneSerializer.FromData(scene, src));
        var sr = scene.FindEntity("Ghost")?.GetComponent<SpriteRenderer>();
        Check("premise: the image did not resolve so the path is empty (the condition of this check)",
              sr != null && string.IsNullOrEmpty(sr.TexturePath) && string.IsNullOrEmpty(sr.AtlasPath));

        var outData = SceneSerializer.ToData(scene);
        var back = outData.Entities[0].Components.Find(c => c is SpriteData) as SpriteData;
        Check($"* an unresolved image id survives the save (actual '{back?.Texture}')", back?.Texture == ghost);

        var scene2 = new Scene("GhostRef2");
        CaptureStderr(() => SceneSerializer.FromData(scene2, outData));
        var back2 = SceneSerializer.ToData(scene2).Entities[0].Components.Find(c => c is SpriteData) as SpriteData;
        Check("* it survives two round trips", back2?.Texture == ghost);

        var real = new Scene("RealRef");
        var re = real.CreateEntity("R");
        real.Update(0f);
        var rsr = re.AddComponent<SpriteRenderer>();
        rsr.UnresolvedSpriteRef = (null, ghost, null);
        new SpriteData { Texture = "", Atlas = "" }.WriteTo(rsr);
        Check("* control: an empty reference empties the holder too (an old value does not squat)",
              rsr.UnresolvedSpriteRef == null);

        var aScene = new Scene("GhostClip");
        var ae = aScene.CreateEntity("A");
        aScene.Update(0f);
        ae.AddComponent<SpriteRenderer>();
        var anim = ae.AddComponent<Animator>();
        var ad = new AnimatorData();
        ad.Clips.Add(ghost);
        CaptureStderr(() => ad.WriteTo(anim));
        Check("premise: the clips did not resolve so there are none (the condition of this check)", anim.ClipCount == 0);
        Check("* the unresolved clip ids are held", anim.UnresolvedClipIds.Contains(ghost));

        var cap = new AnimatorData();
        cap.Capture(ae);
        Check($"* unresolved clip ids survive the save (actual {cap.Clips.Count})",
              cap.Clips.Contains(ghost));

        anim.ClearClips();
        Check("* ClearClips empties the unresolved remnants too (the \"build from this data\" contract)",
              anim.UnresolvedClipIds.Count == 0);
    }

    private static void TestSameTypeOverrides()
    {
        var baseData = new SceneData { SchemaVersion = SceneData.CurrentSchemaVersion, Kind = Core.SceneKind.Prefab };
        var bed = new EntityData { Id = 1, Name = "TwoCols" };
        bed.Components.Add(new TransformData());
        bed.Components.Add(new ColliderData { SizeX = 10, SizeY = 10, IsTrigger = false });
        bed.Components.Add(new ColliderData { SizeX = 20, SizeY = 20, IsTrigger = true });
        baseData.Entities.Add(bed);

        var ov = new InstanceOverrideData { EntityId = 1 };
        var sparse = new System.Text.Json.Nodes.JsonObject
        {
            ["type"] = "collider", ["#"] = 1, ["sizeX"] = 99,
        };
        ov.Components = new System.Collections.Generic.List<System.Text.Json.Nodes.JsonObject> { sparse };

        var eff = InstanceOverrides.BuildEffective(bed, ov);
        var cols = eff.FindAll(c => c is ColliderData).ConvertAll(c => (ColliderData)c);
        Check($"premise: the effective list has two colliders (actual {cols.Count})", cols.Count == 2);
        Check($"* only the second collider changes (the first stays at 10 - actual {(cols.Count > 0 ? cols[0].SizeX : null)})",
              cols.Count == 2 && cols[0].SizeX == 10);
        Check($"* the second really becomes 99 (actual {(cols.Count > 1 ? cols[1].SizeX : null)})",
              cols.Count == 2 && cols[1].SizeX == 99);
        Check("* the second's other fields follow the base (isTrigger survives)",
              cols.Count == 2 && cols[1].IsTrigger);

        var ovDel = new InstanceOverrideData
        {
            EntityId = 1,
            Removed = new System.Collections.Generic.List<string> { "collider#1" },
        };
        var effDel = InstanceOverrides.BuildEffective(bed, ovDel);
        var colsDel = effDel.FindAll(c => c is ColliderData).ConvertAll(c => (ColliderData)c);
        Check($"* deleting only the second leaves the first (actual {colsDel.Count})", colsDel.Count == 1);
        Check("* what remains is the first (10x10)", colsDel.Count == 1 && colsDel[0].SizeX == 10);

        var ovOld = new InstanceOverrideData
        {
            EntityId = 1,
            Removed = new System.Collections.Generic.List<string> { "collider" },
        };
        var effOld = InstanceOverrides.BuildEffective(bed, ovOld);
        var colsOld = effOld.FindAll(c => c is ColliderData).ConvertAll(c => (ColliderData)c);
        Check($"* the old format (no ordinal) points at the first - the file does not break (actual {colsOld.Count} left)",
              colsOld.Count == 1 && colsOld[0].SizeX == 20);

        var twoPath = Path.Combine(Path.GetTempPath(), "pc_twocol_" + Guid.NewGuid().ToString("N") + ".scene");
        try
        {
            SceneSerializer.SaveToFile(baseData, twoPath);
            var host2 = new Scene("TwoHost");
            var he2 = host2.CreateEntity("H2");
            var inst2 = he2.AddComponent<SceneInstance>();
            inst2.ScenePath = twoPath;
            inst2.Load();
            host2.Update(0f);

            var live = new List<BoxCollider2D>(he2.GetComponents<BoxCollider2D>());
            Check($"premise: two colliders are applied to the host (actual {live.Count})", live.Count == 2);
            if (live.Count == 2)
            {
                live[1].Size = new Microsoft.Xna.Framework.Vector2(77, 77);

                var comp = InstanceOverrides.Compute(inst2);
                var nodes = comp?[0].Components ?? new List<System.Text.Json.Nodes.JsonObject>();
                var colNodes = nodes.FindAll(n => (string?)n["type"] == "collider");
                Check($"* the diff records only the one that was touched (actual {colNodes.Count})", colNodes.Count == 1);
                Check("* that one is recorded as the second (#=1)",
                      colNodes.Count == 1 && (int?)colNodes[0]["#"] == 1);
                Check($"* the value is the second one's too (77 - actual {(colNodes.Count == 1 ? (float?)colNodes[0]["sizeX"] : null)})",
                      colNodes.Count == 1 && (float?)colNodes[0]["sizeX"] == 77f);
            }
        }
        finally { if (File.Exists(twoPath)) File.Delete(twoPath); }

        var oneData = new SceneData { SchemaVersion = SceneData.CurrentSchemaVersion, Kind = Core.SceneKind.Prefab };
        var oed = new EntityData { Id = 1, Name = "One" };
        oed.Components.Add(new TransformData());
        oed.Components.Add(new ColliderData { SizeX = 10, SizeY = 10 });
        oneData.Entities.Add(oed);
        var onePath = Path.Combine(Path.GetTempPath(), "pc_onecol_" + Guid.NewGuid().ToString("N") + ".scene");
        try
        {
            SceneSerializer.SaveToFile(oneData, onePath);
            var host = new Scene("OneHost");
            var he = host.CreateEntity("H");
            var inst = he.AddComponent<SceneInstance>();
            inst.ScenePath = onePath;
            inst.Load();
            host.Update(0f);

            Check("premise: it stood up as root-attached (the host represents the base root)", inst.RootMerged);
            var col = he.GetComponent<BoxCollider2D>();
            Check("premise: the collider is applied to the host", col != null);
            if (col != null) col.Size = new Microsoft.Xna.Framework.Vector2(55, 55);

            var computed = InstanceOverrides.Compute(inst);
            var node = computed?[0].Components?[0];
            Check($"* one per type carries no ordinal key (actual keys: {(node == null ? "none" : string.Join(",", node.Select(k => k.Key)))})",
                  node != null && !node.ContainsKey("#"));
            Check("premise: an override was still captured (without it the check above is vacuous)",
                  node != null && node.ContainsKey("sizeX"));
        }
        finally { if (File.Exists(onePath)) File.Delete(onePath); }
    }

    private static void TestLegacyKeysDontLeakToOverrides()
    {
        var json = """
        {
          "schemaVersion": 2,
          "kind": "Prefab",
          "entities": [
            { "id": 1, "name": "Anim", "components": [
              { "type": "transform", "x": 0, "y": 0 },
              { "type": "sprite" },
              { "type": "animator", "clips": [], "pivotY": 0.5, "srcX": 7 } ] } ]
        }
        """;
        var path = Path.Combine(Path.GetTempPath(), "pc_legacyov_" + Guid.NewGuid().ToString("N") + ".scene");
        try
        {
            File.WriteAllText(path, json);
            var data = SceneSerializer.LoadFromFile(path);
            var animData = data?.Entities[0].Components.Find(c => c is AnimatorData) as AnimatorData;
            Check($"premise: the stale keys went into the extension bucket (actual {animData?.Legacy?.Count ?? 0})",
                  animData?.Legacy != null && animData.Legacy.Count == 2);

            var host = new Scene("LegacyHost");
            var he = host.CreateEntity("H");
            var inst = he.AddComponent<SceneInstance>();
            inst.ScenePath = path;
            CaptureStderr(() => inst.Load());
            host.Update(0f);
            Check("premise: the instance stood up (otherwise the rest is vacuous)", inst.IsLoaded);

            var ov = InstanceOverrides.Compute(inst);
            var nodes = ov?[0].Components ?? new List<System.Text.Json.Nodes.JsonObject>();
            var animNode = nodes.Find(n => (string?)n["type"] == "animator");
            var ghost = animNode == null
                ? ""
                : string.Join(",", animNode.Select(k => k.Key).Where(k => k is "pivotY" or "srcX"));
            Check($"* stale keys do not harden into overrides (phantom key: '{ghost}')", ghost.Length == 0);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void TestPivotAnchorMigration()
    {
        var v1 = new SceneData { SchemaVersion = 1 };

        var prop = new EntityData { Id = 1, Name = "Prop" };
        prop.Components.Add(new TransformData { X = 10, Y = 20 });
        prop.Components.Add(new SpriteData { DrawW = 32, DrawH = 16, PivotX = 0.5f, PivotY = 0.5f });
        prop.Components.Add(new ColliderData { OffsetY = 5, SizeX = 32, SizeY = 16 });
        v1.Entities.Add(prop);

        var player = new EntityData { Id = 2, Name = Core.Scene.PlayerName };
        player.Components.Add(new TransformData { X = 5, Y = 5 });
        player.Components.Add(new ColliderData { OffsetY = 14, SizeX = 12, SizeY = 8 });
        v1.Entities.Add(player);

        var s = new Scene("mig");
        SceneSerializer.FromData(s, v1);
        s.Update(0f);

        var p = s.FindEntity("Prop")!;
        var pt = p.GetComponent<Transform>()!;
        var psr = p.GetComponent<SpriteRenderer>()!;
        var pc = p.GetComponent<BoxCollider2D>()!;
        Check("migration: Position.Y plus h/2 (20 to 28)", pt.Position == new Vector2(10, 28));
        Check("migration: the pivot is unified to bottom-centre", psr.PivotX == 0.5f && psr.PivotY == 1f);
        Check("migration: the collider offset minus h/2 (5 to -3)", pc.Offset == new Vector2(0, -3));
        Check("migration: the world collider is unchanged (centre Y 25)", pt.Position.Y + pc.Offset.Y == 25f);

        var pl = s.FindPlayer()!;
        var plt = pl.GetComponent<Transform>()!;
        var plc = pl.GetComponent<BoxCollider2D>()!;
        Check("migration (player): the +24 special case (5 to 29)", plt.Position == new Vector2(5, 29));
        Check("migration (player): the collider goes 14 to -10", plc.Offset == new Vector2(0, -10));

        var bd = new EntityData { Id = 3, Name = "Backdrop" };
        bd.Components.Add(new TransformData { X = 0, Y = 0 });
        bd.Components.Add(new SpriteData { DrawW = 8, DrawH = 8, YSort = false, RenderLayer = RenderLayers.Entities, SortOffset = -968 });
        v1.Entities.Add(bd);
        var s3 = new Scene("mig3");
        SceneSerializer.FromData(s3, v1);
        s3.Update(0f);
        var bsr = s3.FindEntity("Backdrop")!.GetComponent<SpriteRenderer>()!;
        Check("an old YSort=false backdrop migrates to BelowEntities with its order preserved",
            bsr.RenderLayer == RenderLayers.BelowEntities && bsr.SortOffset == -968f && bsr.SortY == -968f);

        var v2 = new SceneData { SchemaVersion = 2 };
        var prop2 = new EntityData { Id = 1, Name = "Prop2" };
        prop2.Components.Add(new TransformData { X = 10, Y = 20 });
        prop2.Components.Add(new SpriteData { DrawW = 32, DrawH = 16, PivotX = 0.5f, PivotY = 1f });
        v2.Entities.Add(prop2);
        var s2 = new Scene("mig2");
        SceneSerializer.FromData(s2, v2);
        s2.Update(0f);
        Check("a v2 file is not corrected", s2.FindEntity("Prop2")!.GetComponent<Transform>()!.Position == new Vector2(10, 20));
    }

    private static void TestAnimatorPivot()
    {
        const float FootPivotY = 41f / 48f;

        var atlas = new Assets.SpriteAtlas();
        for (int i = 0; i < 3; i++)
            atlas.Slices.Add(new Assets.SpriteSlice
            {
                Name = $"f{i}", X = i * 48, Y = 0, Width = 48, Height = 48,
                PivotX = 0.5f, PivotY = FootPivotY,
            });

        var data = new Animation.AnimationData { Name = "Walk_D" };
        for (int i = 0; i < 3; i++)
            data.Frames.Add(new Animation.FrameData { SliceIndex = i, Duration = 0.1f });

        var clip = data.ToClip(atlas);
        Check($"the clip carries the atlas pivot (Y {clip.PivotY:F4} = 41/48)",
              Math.Abs(clip.PivotY - FootPivotY) < 1e-5f && Math.Abs(clip.PivotX - 0.5f) < 1e-5f);
        Check("the clip frame count is unchanged", clip.Frames.Count == 3);

        var scene = new Core.Scene("anim");
        var e = scene.CreateEntity("hero");
        var sr = e.AddComponent<Components.SpriteRenderer>();
        var animator = e.AddComponent<Components.Animator>();

        Check("1. no clip: the authored pivot is used (0.5, 1 = the bottom of the cell)",
              Math.Abs(sr.EffectivePivotY - 1f) < 1e-5f && Math.Abs(sr.EffectivePivotX - 0.5f) < 1e-5f);

        sr.PivotY = 0.25f;

        animator.AddClip(clip);
        animator.Play("Walk_D");
        Check($"2. Play moves the clip pivot to the render anchor (actual {sr.EffectivePivotY:F4})",
              Math.Abs(sr.EffectivePivotY - FootPivotY) < 1e-5f);
        Check("* 2. the authored pivot is unchanged (the animation never touches authored values)",
              Math.Abs(sr.PivotY - 0.25f) < 1e-5f);

        var idleAtlas = new Assets.SpriteAtlas();
        idleAtlas.Slices.Add(new Assets.SpriteSlice
        {
            Name = "i0", X = 0, Y = 0, Width = 48, Height = 48, PivotX = 0.5f, PivotY = 1f,
        });
        var idleData = new Animation.AnimationData { Name = "Idle_D" };
        idleData.Frames.Add(new Animation.FrameData { SliceIndex = 0, Duration = 0.1f });
        animator.AddClip(idleData.ToClip(idleAtlas));

        animator.Play("Idle_D");
        Check($"switching clips moves the anchor too (actual {sr.EffectivePivotY:F4} = 1)",
              Math.Abs(sr.EffectivePivotY - 1f) < 1e-5f);
        animator.Play("Walk_D");
        Check($"the same on the way back (actual {sr.EffectivePivotY:F4} = 41/48)",
              Math.Abs(sr.EffectivePivotY - FootPivotY) < 1e-5f);

        animator.Stop();
        Check("* 3. stopping keeps the clip anchor (a stopped image has to stand in place)",
              Math.Abs(sr.EffectivePivotY - FootPivotY) < 1e-5f);
        animator.ClearFrame();
        Check($"* 3. ClearFrame returns to the authored pivot (actual {sr.EffectivePivotY:F4} = 0.25)",
              Math.Abs(sr.EffectivePivotY - 0.25f) < 1e-5f);

        var empty = new Animation.AnimationData { Name = "Empty" }.ToClip(new Assets.SpriteAtlas());
        Check("a clip with an empty atlas keeps the default pivot (0.5, 1)",
              Math.Abs(empty.PivotX - 0.5f) < 1e-5f && Math.Abs(empty.PivotY - 1f) < 1e-5f);

        var lonelyScene = new Core.Scene("lonely");
        var lonely = lonelyScene.CreateEntity("NoRenderer");
        var solo = lonely.AddComponent<Components.Animator>();
        solo.AddClip(clip);
        solo.Play("Walk_D");
        solo.Update(0.1f);
        solo.ClearFrame();
        Check("* an Animator with no renderer: playback state runs normally without crashing",
              ReferenceEquals(solo.CurrentClip, clip) && solo.CurrentTime > 0f);
        Check("* an Animator with no renderer: the fact that there is no sibling to draw is unchanged (the predicate behind the inspector warning)",
              lonely.GetComponent<Components.SpriteRenderer>() == null);
    }

    private static void TestPlayerNameContract()
    {
        var scene = new Core.Scene("t");
        Check("with no player, FindPlayer gives null", scene.FindPlayer() == null);

        var p = scene.CreateEntity(Core.Scene.PlayerName);
        scene.Update(0f);
        Check("FindPlayer finds the entity made from the constant", ReferenceEquals(scene.FindPlayer(), p));
        Check("FindPlayer == FindEntity(PlayerName)",
              ReferenceEquals(scene.FindPlayer(), scene.FindEntity(Core.Scene.PlayerName)));

        p.Name = "NotPlayer";
        Check("a changed name is not found (the convention is the name)", scene.FindPlayer() == null);
    }

    private static void TestPlayerBodyAlwaysEnsured()
    {
        Assets.AnimClipCache.ContentRoot = Path.Combine(AppContext.BaseDirectory, "Content");

        var scene = new Core.Scene("body");
        var rig = Gameplay.Systems.LevelSetup.SpawnPlayer(scene, new Vector2(40, 24));
        scene.Update(0f);

        Check("SpawnPlayer creates the host (PlayerRig)",
              rig.Name == Gameplay.Systems.LevelSetup.PlayerRigName);

        var player = scene.FindPlayer();
        Check("* the instance child is called 'Player', so the naming convention holds", player != null);
        if (player == null) return;

        Check("the body comes from the prefab (capsule)", player.GetComponent<Components.CapsuleCollider2D>() != null);
        Check("the body comes from the prefab (rigidbody)", player.GetComponent<Components.Rigidbody2D>() != null);
        Check("the controller comes from the prefab too", player.GetComponent<Gameplay.Player.PlayerController>() != null);

        Check($"the host position is carried into the instance ({player.GetComponent<Transform>()!.Position})",
              player.GetComponent<Transform>()!.Position == new Vector2(40, 24));

        Check("no box collider is attached (the cause of shapes diverging per scene is gone)",
              player.GetComponent<Components.BoxCollider2D>() == null);
    }

    private static void TestCodeOwnedBodyRetired()
    {
        Check("* the code-owned body predicate is off (the body moved into the prefab)",
              !CodeOwnedBody.Owns(new Core.Scene("x").CreateEntity(Core.Scene.PlayerName)));

        var scene = new Core.Scene("retired");
        var e = scene.CreateEntity(Core.Scene.PlayerName);
        e.AddComponent<Components.Rigidbody2D>();
        e.AddComponent<Components.CapsuleCollider2D>();
        scene.Update(0f);

        var persisted = ComponentDataRegistry.CaptureAll(e, forPersist: true);
        Check("the body is now saved (rigidbody)", persisted.Exists(d => d is RigidbodyData));
        Check("the body is now saved (capsule)", persisted.Exists(d => d is CapsuleColliderData));

        var snapshot = ComponentDataRegistry.CaptureAll(e);
        Check("the undo capture still holds everything (unchanged)",
              snapshot.Exists(d => d is RigidbodyData) && snapshot.Exists(d => d is CapsuleColliderData));
    }
    private static Animation.AnimationClip FakeClip(string name, bool loop = false)
    {
        var c = new Animation.AnimationClip(name) { Loop = loop, SourceId = "fake-" + name };
        c.Frames.Add(new Animation.AnimationFrame(new Microsoft.Xna.Framework.Rectangle(0, 0, 8, 8), 0.1f));
        return c;
    }

    private static Animation.AnimationClip? GetClip(Components.Animator a, string name)
    {
        foreach (var c in a.Clips) if (c.Name == name) return c;
        return null;
    }

    private static string? ExtractJsonBlock(string json, string marker)
    {
        int at = json.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return null;
        int start = json.LastIndexOf('{', at);
        if (start < 0) return null;

        int depth = 0;
        for (int i = start; i < json.Length; i++)
        {
            if (json[i] == '{') depth++;
            else if (json[i] == '}' && --depth == 0) return json.Substring(start, i - start + 1);
        }
        return null;
    }

    private static void TestAnimatorAuthoredVsRuntime()
    {
        var scene = new Scene("authoredVsRuntime");
        var e = scene.CreateEntity("Door");
        var sr = e.AddComponent<Components.SpriteRenderer>();
        sr.SourceRect = new Microsoft.Xna.Framework.Rectangle(4, 8, 25, 36);
        sr.SliceName = "door_closed";
        sr.PivotY = 1f;
        var a = e.AddComponent<Components.Animator>();
        scene.Update(0f);

        Check("1. an Animator with no clips: the authored slice is drawn",
              sr.EffectiveSourceRect == sr.SourceRect && sr.FrameOverride == null);
        Check("1. the authored size is unchanged (25x36)",
              sr.GetNativeSize() == new Vector2(25, 36));

        var open = FakeClip("Open");
        open.PivotY = 0.5f;
        a.AddClip(open);
        a.Play("Open");

        Check("2. while playing: the output slot holds the current frame",
              sr.FrameOverride != null
              && sr.EffectiveSourceRect == new Microsoft.Xna.Framework.Rectangle(0, 0, 8, 8));
        Check("* 2. the authored SourceRect does not change while playing",
              sr.SourceRect == new Microsoft.Xna.Framework.Rectangle(4, 8, 25, 36));
        Check("* 2. the authored SliceName does not change while playing",
              sr.SliceName == "door_closed");
        Check("* 2. the authored pivot does not change while playing (even though the effective one went 1.0 to 0.5)",
              Math.Abs(sr.PivotY - 1f) < 1e-5f && Math.Abs(sr.EffectivePivotY - 0.5f) < 1e-5f);

        a.Update(1f);
        Check("3. premise: the non-looping clip finished (IsPlaying=false)", !a.IsPlaying);
        Check("* 3. the last frame remains after it finishes (otherwise a door closes the instant it opens)",
              sr.FrameOverride != null
              && sr.EffectiveSourceRect == new Microsoft.Xna.Framework.Rectangle(0, 0, 8, 8));
        a.Stop();
        Check("* 3. Stop() does not erase the image either (ClearFrame is the only door that clears)",
              sr.FrameOverride != null);
        a.RemoveClip("Open");
        Check("* 3. deleting the clip leaves the last frame on screen",
              sr.FrameOverride != null);
        a.ClearFrame();
        Check("3. ClearFrame returns to the authored slice",
              sr.FrameOverride == null && sr.EffectiveSourceRect == sr.SourceRect);

        a.AddClip(FakeClip("Open2"));
        a.Play("Open2");

        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pc_anim_authored.scene");
        SaveFixture(SceneSerializer.ToData(scene), path);
        var raw = System.IO.File.ReadAllText(path);
        try { System.IO.File.Delete(path); } catch {  }

        var animBlock = ExtractJsonBlock(raw, "\"type\": \"animator\"");
        Check("4. the saved file has an animator block", animBlock != null);
        foreach (var field in new[] { "srcX", "srcY", "srcW", "srcH", "pivotX", "pivotY",
                                      "colorR", "colorA", "renderLayer", "sortOffset",
                                      "flipX", "emissive", "castShadow", "shadowScale",
                                      "atlas", "slice", "texture" })
            Check($"* 4. the animator block has no render field '{field}'",
                  animBlock != null && !animBlock.Contains($"\"{field}\""));
        Check("4. the animator block holds the clips (it is not an empty shell)",
              animBlock != null && animBlock.Contains("\"clips\""));

        var spriteBlock = ExtractJsonBlock(raw, "\"type\": \"sprite\"");
        Check("* 4. saving while playing puts the authored rectangle in the sprite block (the frame is not baked)",
              spriteBlock != null && spriteBlock.Contains("\"srcW\": 25"));
        Check("* 4. the frame that was playing appears nowhere in the file (srcW/srcH 8)",
              !raw.Contains("\"srcW\": 8") && !raw.Contains("\"srcH\": 8"));

        var legacyJson = """
        {
          "schemaVersion": 2,
          "name": "LegacyAnim",
          "entities": [
            {
              "id": 1, "parentId": 0, "name": "OldDoor", "active": true,
              "components": [
                { "type": "transform", "x": 0, "y": 0, "rotation": 0 },
                { "type": "animator", "clips": [],
                  "castShadow": true, "pivotY": 0.5, "renderLayer": -500 }
              ]
            }
          ]
        }
        """;
        var legacyPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pc_anim_legacy.scene");
        System.IO.File.WriteAllText(legacyPath, legacyJson);

        var saved = Console.Out;
        var buf = new System.Text.StringBuilder();
        Scene? legacyScene = null;
        try
        {
            Console.SetOut(new System.IO.StringWriter(buf));
            var loaded = SceneSerializer.LoadFromFile(legacyPath);
            if (loaded != null)
            {
                legacyScene = new Scene("legacy");
                SceneSerializer.FromData(legacyScene, loaded);
                legacyScene.Update(0f);
            }
        }
        finally { Console.SetOut(saved); try { System.IO.File.Delete(legacyPath); } catch { } }

        var log = buf.ToString();
        Check("5. a scene with a stale animator block loads (a warning, not a failure)",
              legacyScene?.FindEntity("OldDoor")?.GetComponent<Components.Animator>() != null);
        Check("* 5. JsonExtensionData really catches the unknown render fields (their names appear in the warning)",
              log.Contains("castShadow") && log.Contains("renderLayer"));
        Check("* 5. the warning names the entity (in a large scene a nameless warning is unfindable)",
              log.Contains("OldDoor"));
    }

    private static void TestAnimatorWriteReplacesClips()
    {
        Assets.AnimClipCache.Clear();
        var scene = new Scene("clipWrite");

        var a = scene.CreateEntity("A").AddComponent<Components.Animator>();
        a.AddClip(FakeClip("Walk_D")); a.DefaultClip = "Walk_D";
        var b = scene.CreateEntity("B").AddComponent<Components.Animator>();
        b.AddClip(FakeClip("Idle_D")); b.DefaultClip = "Idle_D";
        scene.Update(0f);

        var fromA = new AnimatorData();
        fromA.Capture(a.Entity);
        Check("premise: A's data holds one clip", fromA.Clips.Count == 1);

        fromA.WriteTo(b);
        Check("* 1. pasting values overwrites - the target's old clips do not remain",
              !b.HasClip("Idle_D"));
        Check("1. after pasting the clip count matches the source (they are not merged)",
              b.ClipCount == 0);
        Check("1. the default clip switches to the source value too", b.DefaultClip == "Walk_D");

        var reset = new AnimatorData();
        var c = scene.CreateEntity("C").AddComponent<Components.Animator>();
        c.AddClip(FakeClip("Open")); c.AddClip(FakeClip("Close")); c.DefaultClip = "Open";
        scene.Update(0f);
        Check("premise: C has two clips", c.ClipCount == 2);

        reset.WriteTo(c);
        Check("* 1. a reset (empty data) really deletes the clips (not a zero-iteration foreach)",
              c.ClipCount == 0);
        Check("1. after a reset the default clip is cleared too", string.IsNullOrEmpty(c.DefaultClip));
        Check("1. after a reset the playback state is cleaned too (calling something absent from the list 'playing' would make the inspector lie)",
              c.CurrentClip == null && !c.IsPlaying);
    }

    private static void TestSpriteDefaultsAgree()
    {
        var dto = new SpriteData();
        var live = new Components.SpriteRenderer();

        Check($"* 3. the pivot Y defaults agree (DTO {dto.PivotY} = renderer {live.PivotY} = the feet)",
              Math.Abs(dto.PivotY - live.PivotY) < 1e-6f);
        Check($"3. the pivot X defaults agree (DTO {dto.PivotX} = renderer {live.PivotX})",
              Math.Abs(dto.PivotX - live.PivotX) < 1e-6f);
        Check("3. the shadow defaults agree (CastShadow, Scale)",
              dto.CastShadow == live.CastShadow
              && Math.Abs(dto.ShadowScale - live.ShadowScale) < 1e-6f);
        Check("3. the emissive, flip and sort offset defaults agree",
              Math.Abs(dto.Emissive - live.EmissiveIntensity) < 1e-6f
              && dto.FlipX == live.FlipX && dto.FlipY == live.FlipY
              && Math.Abs(dto.SortOffset - live.SortOffset) < 1e-6f);
        Check("3. the colour defaults agree (opaque white)",
              dto.ColorR == live.Color.R && dto.ColorG == live.Color.G
              && dto.ColorB == live.Color.B && dto.ColorA == live.Color.A);

        var json = """
        {
          "schemaVersion": 2,
          "name": "pc_pivot_omitted",
          "entities": [
            {
              "id": 1, "parentId": 0, "name": "NoPivot", "active": true,
              "components": [
                { "type": "transform", "x": 0, "y": 0, "rotation": 0 },
                { "type": "sprite", "castShadow": true }
              ]
            }
          ]
        }
        """;
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pc_pivot_omitted.scene");
        System.IO.File.WriteAllText(path, json);
        var loaded = SceneSerializer.LoadFromFile(path);
        try { System.IO.File.Delete(path); } catch { }

        var scene = new Scene("pivotOmitted");
        if (loaded != null) SceneSerializer.FromData(scene, loaded);
        scene.Update(0f);
        var sr = scene.FindEntity("NoPivot")?.GetComponent<Components.SpriteRenderer>();

        Check("3. premise: a sprite block with no pivot loads", sr != null);
        Check($"* 3. a scene that omits the pivot stands at the feet (1.0) (actual {sr?.PivotY:F2}) - it does not quietly move to the centre",
              sr != null && Math.Abs(sr.PivotY - 1f) < 1e-6f);
    }

    private static void TestAnimatorClearFrameSticks()
    {
        var scene = new Scene("clearSticks");
        var e = scene.CreateEntity("Door");
        var sr = e.AddComponent<Components.SpriteRenderer>();
        sr.SourceRect = new Microsoft.Xna.Framework.Rectangle(4, 8, 25, 36);
        var a = e.AddComponent<Components.Animator>();
        a.AddClip(FakeClip("Open"));
        scene.Update(0f);

        a.Play("Open");
        a.Update(1f);
        Check("premise: after completion the slot holds the last frame", sr.FrameOverride != null);
        Check("premise: the clip is still attached (there is material for a recovery to restore)", a.CurrentClip != null);

        a.ClearFrame();
        Check("the slot is empty right after ClearFrame", sr.FrameOverride == null);

        for (int i = 0; i < 5; i++) a.Update(0.1f);
        Check("* 2. ticking after ClearFrame does not bring it back (so 'one door clears' becomes true)",
              sr.FrameOverride == null);
        Check("2. once cleared, the authored slice is visible",
              sr.EffectiveSourceRect == sr.SourceRect);

        a.Play("Open", restart: true);
        Check("* 2. playing again brings the slot back (the latch does not block playback)",
              sr.FrameOverride != null);

        a.Update(1f);
        e.RemoveComponent(sr);
        var sr2 = e.AddComponent<Components.SpriteRenderer>();
        a.Update(0f);
        Check("* 2. with the latch in place, recovery on a renderer swap still works (the fix did not kill something else)",
              sr2.FrameOverride != null);
    }

    private static void TestAnimatorIdleHold()
    {
        var scene = new Scene("idleHold");
        var e = scene.CreateEntity("hero");
        var sr = e.AddComponent<Components.SpriteRenderer>();
        var a = e.AddComponent<Components.Animator>();
        scene.Update(0f);

        var rects = new System.Collections.Generic.Dictionary<string, Microsoft.Xna.Framework.Rectangle>
        {
            ["Idle_D"] = new(0, 0, 48, 48),
            ["Idle_U"] = new(48, 0, 48, 48),
            ["Idle_L"] = new(96, 0, 48, 48),
            ["Idle_R"] = new(144, 0, 48, 48),
        };
        foreach (var (name, rect) in rects)
        {
            var clip = new Animation.AnimationClip(name) { Loop = false, SourceId = "fake-" + name };
            clip.Frames.Add(new Animation.AnimationFrame(rect, 0.1f));
            a.AddClip(clip);
        }

        Check("premise: the idle clip is non-looping (as in the real character)",
              a.HasClip("Idle_D") && GetClip(a, "Idle_D")?.Loop == false);

        a.Play("Idle_D");
        a.Update(0.5f);
        Check("a. premise: the non-looping idle finished", !a.IsPlaying);
        Check("* a. the pose remains after it finishes (otherwise the character turns invisible on every stop)",
              sr.FrameOverride != null && sr.EffectiveSourceRect == rects["Idle_D"]);

        for (int i = 0; i < 10; i++) a.Update(0.1f);
        Check("* a. still there after ten more ticks (ruling out an implementation that clears on the next tick)",
              sr.EffectiveSourceRect == rects["Idle_D"]);

        a.Play("Idle_R", restart: true);
        a.Pause();
        Check("* b. the slot survives a Pause too (the PlayIdle fallback path uses it)",
              sr.FrameOverride != null && sr.EffectiveSourceRect == rects["Idle_R"]);

        foreach (var (name, rect) in rects)
        {
            a.Play(name);
            a.Update(0.5f);
            Check($"c. after switching to '{name}', the last frame of that direction is visible",
                  sr.EffectiveSourceRect == rect);
        }

        e.RemoveComponent(sr);
        var sr2 = e.AddComponent<Components.SpriteRenderer>();
        Check("d. premise: the new renderer's slot is empty", sr2.FrameOverride == null);
        Check("d. premise: playback has already finished (recovery must not depend on playback)", !a.IsPlaying);
        a.Update(0f);
        Check("* d. swapping the renderer brings the frame back on the next tick (the character does not turn invisible)",
              sr2.FrameOverride != null && sr2.EffectiveSourceRect == rects["Idle_R"]);
    }

    private static void TestAnimatorClipRemoval()
    {
        var scene = new Scene("animRemove");
        var e = scene.CreateEntity("Door");
        var a = e.AddComponent<Components.Animator>();
        scene.Update(0f);

        a.AddClip(FakeClip("Closed"));
        a.AddClip(FakeClip("Open"));
        a.AddClip(FakeClip("Spare"));
        a.DefaultClip = "Closed";
        a.Play("Open");

        Check("premise: three clips, Open playing, Closed as the default",
              a.ClipCount == 3 && a.CurrentClip?.Name == "Open" && a.DefaultClip == "Closed");

        Check("1. an ordinary removal succeeds", a.RemoveClip("Spare"));
        Check("1. an ordinary removal leaves playback and the default alone",
              a.CurrentClip?.Name == "Open" && a.DefaultClip == "Closed" && a.ClipCount == 2);

        Check("2. removing while playing succeeds", a.RemoveClip("Open"));
        Check("* 2. playback stops", !a.IsPlaying);
        Check("* 2. the CurrentClip reference is released (calling something absent from the list 'playing' would make the inspector lie)",
              a.CurrentClip == null);
        Check("2. the default clip is untouched", a.DefaultClip == "Closed");

        Check("3. removing the default clip succeeds", a.RemoveClip("Closed"));
        Check("* 3. DefaultClip is cleared (it does not linger as a phantom name)",
              string.IsNullOrEmpty(a.DefaultClip));
        Check("3. zero clips", a.ClipCount == 0);

        Check("removing a missing name gives false (not a quiet true)", !a.RemoveClip("noSuchClip"));
    }

    private static void TestAnimatorPlayMissLatch()
    {
        var scene = new Scene("animLatch");
        var e = scene.CreateEntity("Door");
        var a = e.AddComponent<Components.Animator>();
        scene.Update(0f);

        var saved = Console.Out;
        var buf = new System.Text.StringBuilder();
        try
        {
            Console.SetOut(new System.IO.StringWriter(buf));
            a.Play("missingClip");
            a.Play("missingClip");
            a.Play("missingClip");
        }
        finally { Console.SetOut(saved); }

        int warns = CountOccurrences(buf.ToString(), "missingClip");
        Check($"* the same name reports once (three calls, {warns} warnings)", warns == 1);
        Check("the warning includes the held list (it says what is there)",
              buf.ToString().Contains("holding:"));

        buf.Clear();
        a.AddClip(FakeClip("missingClip"));
        a.RemoveClip("missingClip");
        try
        {
            Console.SetOut(new System.IO.StringWriter(buf));
            a.Play("missingClip");
        }
        finally { Console.SetOut(saved); }

        Check("* AddClip releases the latch (otherwise that name is muted forever)",
              CountOccurrences(buf.ToString(), "missingClip") == 1);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    private static void TestAnimatorClipsRoundTrip()
    {
        var animRel = System.IO.Path.Combine("Animations", "Characters", "Boy", "Walk_D.anim");
        var animFull = System.IO.Path.Combine(AppContext.BaseDirectory, "Content", animRel);
        Check($"the real .anim exists - {animRel} (the only path for verifying the id round trip)",
              System.IO.File.Exists(animFull));
        if (!System.IO.File.Exists(animFull)) return;

        Assets.AnimClipCache.Clear();
        Assets.AnimClipCache.ContentRoot = System.IO.Path.Combine(AppContext.BaseDirectory, "Content");

        var data = Animation.AnimationData.Load(animFull);
        Check("the real .anim carries its own id", !string.IsNullOrEmpty(data?.Id));
        Check("the real .anim points at its atlas by id (not a path)", !string.IsNullOrEmpty(data?.AtlasId));

        var animId = Assets.AssetRegistry.Instance.GetOrCreateId(animRel);
        var viaCache = Assets.AnimClipCache.Get(animId);
        Check("AnimClipCache stands a clip up from an id", viaCache != null && viaCache.Frames.Count > 0);
        Check("the clip remembers its source (SourceId)", viaCache?.SourceId == data?.Id);

        var scene = new Scene("AnimRoundTrip");
        var e = scene.CreateEntity("NPC");
        var animator = e.AddComponent<Components.Animator>();
        if (viaCache != null) animator.AddClip(viaCache);
        animator.DefaultClip = viaCache?.Name;
        scene.Update(0f);

        var captured = ComponentDataRegistry.CaptureAll(e, forPersist: true);
        var ad = captured.Find(c => c is AnimatorData) as AnimatorData;
        Check("the save capture contains AnimatorData", ad != null);
        Check("clips are saved as a list of ids (not paths)",
              ad != null && ad.Clips.Count == 1 && ad.Clips[0] == data!.Id);
        Check("the default pose is saved", ad?.DefaultClip == viaCache?.Name);

        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pixelcore_animrt.scene");
        SaveFixture(SceneSerializer.ToData(scene), path);
        var json = System.IO.File.ReadAllText(path);
        Check("the animator discriminator is stamped into the scene JSON", json.Contains("\"animator\""));
        Check("the clip ids go into the file raw (not path strings)",
              json.Contains(data!.Id) && !json.Contains("Characters/Boy/Walk.png"));

        var back = SceneSerializer.LoadFromFile(path);
        try { System.IO.File.Delete(path); } catch {  }
        var scene2 = new Scene("AnimRoundTrip2");
        SceneSerializer.FromData(scene2, back!);
        scene2.Update(0f);

        var e2 = scene2.FindEntity("NPC");
        var a2 = e2?.GetComponent<Components.Animator>();
        Check("the Animator survives the round trip", a2 != null);
        Check("the clips are restored after the round trip (id, file, frames)",
              a2 != null && viaCache != null && a2.HasClip(viaCache.Name));
        Check("the default pose is playing after the round trip",
              a2?.CurrentClip?.Name == viaCache?.Name);
        Check("the frame count is the same after the round trip",
              a2?.CurrentClip?.Frames.Count == viaCache?.Frames.Count);

        Assets.AnimClipCache.Clear();
    }

    private static void TestCodeOwnedBodyGuard()
    {
        foreach (var (data, expected) in new (ComponentData, string)[]
                 {
                     (new RigidbodyData(), "rigidbody"),
                     (new ColliderData(), "collider"),
                     (new CapsuleColliderData(), "capsuleCollider"),
                 })
        {
            Check($"the discriminator copy matches the real one: {expected}",
                  CodeOwnedBody.IsPart(expected) && CodeOwnedBody.IsPart(data));
        }
        Check("a circle collider is not a body (untouched by code, so authored)",
              !CodeOwnedBody.IsPart(CodeOwnedBody.NotPartExample)
              && !CodeOwnedBody.IsPart(new CircleColliderData()));

        var prefab = new SceneData { SchemaVersion = SceneData.CurrentSchemaVersion, Kind = PixelCore.Runtime.Core.SceneKind.Prefab };
        prefab.Entities.Add(new EntityData
        {
            Id = 1, ParentId = 0, Name = Core.Scene.PlayerName,
            Components = new System.Collections.Generic.List<ComponentData>
            {
                new TransformData { X = 0, Y = 0 },
                new RigidbodyData(),
                new CapsuleColliderData { Radius = 3f, Length = 1f, Horizontal = true },
            }
        });
        prefab.Entities.Add(new EntityData
        {
            Id = 2, ParentId = 0, Name = "Lamp",
            Components = new System.Collections.Generic.List<ComponentData>
            {
                new TransformData { X = 30, Y = 0 },
                new CapsuleColliderData { Radius = 5f },
            }
        });
        var prefabPath = Path.Combine(Path.GetTempPath(), "pc_selftest_bodyguard.scene");
        SaveFixture(prefab, prefabPath);

        var host = new Scene("BodyGuardHost");
        var hostEnt = host.CreateEntity("Rig");
        var inst = hostEnt.AddComponent<SceneInstance>();
        inst.ScenePath = prefabPath;
        inst.Load();
        host.Update(0f);

        var live = host.FindEntity(Core.Scene.PlayerName);
        Check("the prefab instantiated a Player with a body", live?.GetComponent<CapsuleCollider2D>() != null);

        var hostData = SceneSerializer.ToData(host);
        SceneInstanceData? sid = null;
        foreach (var cd in hostData.Entities[0].Components)
            if (cd is SceneInstanceData s) sid = s;

        var playerOv = sid?.Overrides?.Find(o => o.EntityId == 1);
        bool bodyRemoved = playerOv?.Removed != null
                           && (playerOv.Removed.Contains("capsuleCollider") || playerOv.Removed.Contains("rigidbody"));
        Check("* a code-owned body does not harden into a delete override", !bodyRemoved);

        var lamp = host.FindEntity("Lamp");
        var lampCol = lamp?.GetComponent<CapsuleCollider2D>();
        if (lamp != null && lampCol != null) lamp.RemoveComponent(lampCol);
        host.Update(0f);

        var hostData2 = SceneSerializer.ToData(host);
        SceneInstanceData? sid2 = null;
        foreach (var cd in hostData2.Entities[0].Components)
            if (cd is SceneInstanceData s) sid2 = s;
        var lampOv = sid2?.Overrides?.Find(o => o.EntityId == 2);
        Check("control: a real deletion on a non-player entity is recorded (the guard did not kill the feature)",
              lampOv?.Removed != null && lampOv.Removed.Contains("capsuleCollider"));

        try { File.Delete(prefabPath); } catch {  }
    }

#if DEBUG
    private static void TestUnmigratedPrefabBarks()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "pixelcore_v1_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmpDir, "Scenes"));
        var prevRoot = Components.SceneInstance.ContentRoot;
        using var regScope = Assets.AssetRegistry.UseTemporary(Path.Combine(tmpDir, "assets.json"));
        try
        {
            Components.SceneInstance.ContentRoot = tmpDir;

            SceneData Fixture(int version, string name)
            {
                var d = new SceneData { SchemaVersion = version, Kind = Core.SceneKind.Prefab, Name = name };
                var ed = new EntityData { Id = 1, Name = "Root" };
                ed.Components.Add(new TransformData());
                d.Entities.Add(ed);
                return d;
            }

            var v1Path = Path.Combine(tmpDir, "Scenes", "V1Fixture.scene");
            SceneSerializer.SaveToFile(Fixture(1, "V1Fixture"), v1Path);
            Check("premise: the fixture really was saved as v1 (otherwise the rest is vacuous)",
                  SceneSerializer.LoadFromFile(v1Path)?.SchemaVersion == 1);

            var host = new Scene("V1Host");
            var e = host.CreateEntity("Host");
            var inst = e.AddComponent<Components.SceneInstance>();
            inst.ScenePath = v1Path;

            var err = CaptureStderr(() => inst.Load());
            Check("* loading a v1 prefab as an instance is reported", err.Contains("old schema"));
            Check("* it says what is being skipped (the migration)", err.Contains("migration"));
            Check("* it gives the cure too (open it as a scene and save to promote it)", err.Contains("Opening that scene"));
            Check("* it names the version number", err.Contains("schemaVersion 1"));
            Check("* it does not refuse - an old prefab still loads", inst.IsLoaded);

            var err2 = CaptureStderr(() => { inst.Unload(); inst.Load(); });
            Check("* the same path reports once (a latch - this path runs on every room change)",
                  !err2.Contains("old schema"));

            var v2Path = Path.Combine(tmpDir, "Scenes", "V2Fixture.scene");
            SceneSerializer.SaveToFile(Fixture(SceneData.CurrentSchemaVersion, "V2Fixture"), v2Path);
            var host2 = new Scene("V2Host");
            var e2 = host2.CreateEntity("Host2");
            var inst2 = e2.AddComponent<Components.SceneInstance>();
            inst2.ScenePath = v2Path;
            var err3 = CaptureStderr(() => inst2.Load());
            Check("* control: a current-schema prefab is silent (it does not report on every load)",
                  !err3.Contains("older schema"));
            Check("premise: the control really loaded too", inst2.IsLoaded);
        }
        finally
        {
            Components.SceneInstance.ContentRoot = prevRoot;
            try { Directory.Delete(tmpDir, true); } catch { }
        }
    }
#endif

    private static void TestPlayerPrefab()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Content", "Prefabs", "Hero.scene");
        Check("the character prefab exists (the only path for prefab verification)", File.Exists(path));
        if (!File.Exists(path)) return;

        var data = SceneSerializer.LoadFromFile(path);
        Check("the character prefab reads", data != null);
        Check("the kind is Prefab (so it does not open as a scene)",
              data?.Kind == PixelCore.Runtime.Core.SceneKind.Prefab);
        if (data == null) return;

        Assets.AnimClipCache.Clear();
        Assets.AnimClipCache.ContentRoot = Path.Combine(AppContext.BaseDirectory, "Content");

        var host = new Scene("PlayerPrefabHost");
        var rig = host.CreateEntity(Core.Scene.PlayerName);
        var inst = rig.AddComponent<SceneInstance>();
        inst.SceneId = Gameplay.Systems.LevelSetup.PlayerPrefabId;
        inst.Load();
        host.Update(0f);

        var resolved = inst.ScenePath;
        Check($"* an id-only instance's ScenePath really opens ({resolved})",
              !string.IsNullOrEmpty(resolved) && File.Exists(resolved));
        Check("* the base reads from that path (so 'open base' works)",
              !string.IsNullOrEmpty(resolved) && File.Exists(resolved)
              && SceneSerializer.LoadFromFile(resolved) != null);

        var player = host.FindPlayer();
        Check("* root attachment: the host is the Player (no wrapper)",
              ReferenceEquals(player, rig));
        if (player == null) return;

        {
            var fileAnim = data.Entities
                .SelectMany(en => en.Components)
                .OfType<AnimatorData>().FirstOrDefault();
            var live = player.GetComponent<Components.Animator>();
            if (fileAnim != null && live != null)
            {
                var recaptured = new AnimatorData();
                recaptured.Capture(player);

                Check($"* hand-written JSON against a recapture: the clip id sets match (file {fileAnim.Clips.Count}, recapture {recaptured.Clips.Count})",
                      new System.Collections.Generic.HashSet<string>(fileAnim.Clips).SetEquals(recaptured.Clips));
                Check($"* hand-written JSON against a recapture: the default clip matches ('{fileAnim.DefaultClip}')",
                      fileAnim.DefaultClip == recaptured.DefaultClip);
            }
            else Check("the hand-written JSON has an animator block (the premise of the equivalence check)", fileAnim != null && live != null);
        }

        Check("body: capsule", player.GetComponent<Components.CapsuleCollider2D>() != null);
        Check("body: rigidbody", player.GetComponent<Components.Rigidbody2D>() != null);
        Check("controller", player.GetComponent<Gameplay.Player.PlayerController>() != null);
        Check("interactor", player.GetComponent<Components.Interactor>() != null);
        Check("footsteps", player.GetComponent<Components.FootstepEmitter>() != null);

        var anim = player.GetComponent<Components.Animator>();
        Check("animator", anim != null);
        int declared = System.Linq.Enumerable.Count(Gameplay.Player.PlayerClips.Required());
        Check($"* every declared clip is restored by id (evidence the hand-written keys were right) - {anim?.ClipCount} of {declared}",
              anim != null && anim.ClipCount == declared);
        Check("   control: the declared list is more than walking and idling (attack and dead are in it)", declared == 13);
        foreach (var name in Gameplay.Player.PlayerClips.Required())
            Check($"clip '{name}' exists", anim?.HasClip(name) == true);
        Check("health", player.GetComponent<Gameplay.Combat.Health>() != null);
        Check("the default pose is playing (Idle_D)", anim?.CurrentClip?.Name == "Idle_D");

        var heroSprite = player.GetComponent<Components.SpriteRenderer>();
        Check("* an animated character has a sibling renderer too (something draws)", heroSprite != null);
        Check("the shadow setting is carried (castShadow, in the sprite block)", heroSprite?.CastShadow == true);
        Check("* right after loading, the renderer holds a frame (with no authored slice, an empty slot means invisible)",
              heroSprite?.FrameOverride != null);

        player.AddComponent<Components.Light2D>().Radius = 42f;
        host.Update(0f);
        var ov = InstanceOverrides.Compute(inst);
        var added = ov?.Find(o => o.EntityId == inst.RootSourceId)?.Components;
        Check("* the override computation really runs (a Light2D absent from the base is recorded)",
              added != null && added.Exists(n => n["type"]?.GetValue<string>() == "light"));
        var light = player.GetComponent<Components.Light2D>();
        if (light != null) player.RemoveComponent(light);
        host.Update(0f);

        {
            var savePath = Path.Combine(Path.GetTempPath(), "pc_rootmerge_save.scene");
            SaveFixture(SceneSerializer.ToData(host), savePath);
            var raw = File.ReadAllText(savePath);
            try { File.Delete(savePath); } catch { }

            Check("* A: the prefab's share is not baked into the scene file (no animator or rigidbody)",
                  !raw.Contains("\"animator\"") && !raw.Contains("\"rigidbody\"")
                  && !raw.Contains("\"playerController\""));
            Check("A: the scene-authored share remains (transform, sceneInstance)",
                  raw.Contains("\"transform\"") && raw.Contains("\"sceneInstance\""));
        }

        {
            var before = rig.GetComponent<Transform>()!.Position;
            rig.GetComponent<Transform>()!.Position = new Vector2(999, 888);
            host.Update(0f);
            var ovs = InstanceOverrides.Compute(inst);
            var rootOv = ovs?.Find(o => o.EntityId == inst.RootSourceId);
            bool hasTransform = rootOv?.Components?.Exists(
                n => n["type"]?.GetValue<string>() == "transform") == true;
            bool hasInstance = rootOv?.Components?.Exists(
                n => n["type"]?.GetValue<string>() == "sceneInstance") == true;
            Check("* B: moving the host creates no Transform override", !hasTransform);
            Check("B: SceneInstance does not become an override either (preventing endless growth)", !hasInstance);

            Check("* B: the root does not become a delete override",
                  rootOv?.Deleted != true);
            rig.GetComponent<Transform>()!.Position = before;
            host.Update(0f);
        }

        Check("* the declared clip set matches the real prefab (zero missing)",
              Gameplay.Player.PlayerClips.Verify(host) == 0);

        var stripped = new Scene("StrippedPlayer");
        var rig2 = stripped.CreateEntity("PlayerRig2");
        var bare = stripped.CreateEntity(Core.Scene.PlayerName);
        bare.AddComponent<Components.Animator>();
        stripped.Update(0f);
        Check("* with no clips, the validation names every declared clip",
              Gameplay.Player.PlayerClips.Verify(stripped) == declared);

        Assets.AnimClipCache.Clear();
    }

    private static void TestAudioRefMigration()
    {
        var reg = PixelCore.Runtime.Assets.AssetRegistry.Instance;
        const string FixtureId = "cafe0001";
        const string FixturePath = "Audio/Test/Fixture.ogg";
        var path = Path.Combine(Path.GetTempPath(), "pc_audioref.scene");

        try
        {
            reg.Register(FixtureId, FixturePath);

            WriteLegacyAudioScene(path, "Audio/Test/Fixture", "Audio/Test/Fixture");

            string bark = CaptureStderr(() => { });
            SceneData? loaded = null;
            bark = CaptureStderr(() => loaded = SceneSerializer.LoadFromFile(path));

            Check("premise: the old-format scene opens", loaded != null);
            if (loaded == null) return;

            Check("* an old ambience path becomes an id (extension-less notation searches for .ogg)",
                  loaded.Ambients is { Count: 1 } && loaded.Ambients[0].SoundId == FixtureId);
            Check("* an old emitter path becomes an id too",
                  FindEmitter(loaded)?.SoundId == FixtureId);
            Check("a normal migration reports nothing (common warnings go unread)",
                  !bark.Contains("✘"), bark.Trim());

            SaveFixture(loaded, path);
            var text = File.ReadAllText(path);
            Check("* the re-saved file has no old \"path\" key (the migration reaches the file)",
                  !text.Contains("\"path\":"));
            Check("* the re-saved file has the new \"soundId\" key",
                  text.Contains($"\"soundId\": \"{FixtureId}\""));

            WriteLegacyAudioScene(path, "Audio/Test/missingSound", "Audio/Test/missingSound");
            SceneData? lost = null;
            string lostBark = CaptureStderr(() => lost = SceneSerializer.LoadFromFile(path));

            Check("* an unresolvable old path preserves its value (clearing it silently makes the sound disappear)",
                  lost?.Ambients is { Count: 1 } && lost.Ambients[0].SoundId == "Audio/Test/missingSound");
            Check("* an unresolvable old path is reported, with the value in hand",
                  lostBark.Contains("✘") && lostBark.Contains("missingSound"), lostBark.Trim());
            Check("* it says which file and which slot",
                  lostBark.Contains("pc_audioref.scene") && lostBark.Contains("ambience")
                  && lostBark.Contains("emitter"), lostBark.Trim());

            var scene = new Scene("lost");
            string applyBark = CaptureStderr(() => SceneSerializer.FromData(scene, lost!));
            Check("* a preserved path is reported again on the way into the scene (the shape-check net)",
                  applyBark.Contains("✘"), applyBark.Trim());
        }
        finally
        {
            reg.Remove(FixtureId);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void TestFileKeysArePinned()
    {
        var control = new List<string>();
        int controlKeys = CollectPolicySensitiveKeys(
            UnpinnedProbeContext.Default.Options, new[] { typeof(UnpinnedProbe) }, control);
        Check($"premise: keys were measured in the control ({controlKeys})", controlKeys > 0);
        Check("* control: with no labels, flipping the policy moves the keys (the check knows how to catch)",
              control.Count > 0);

#if DEBUG
        {
            var notFiles = new HashSet<string> { "PrefsJsonContext", "UnpinnedProbeContext" };
            var contexts = typeof(FileFormats).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(JsonSerializerContext).IsAssignableFrom(t))
                .Select(t => t.Name)
                .Where(n => !notFiles.Contains(n))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
            var covered = FileFormats.All
                .Select(f => f.Options.TypeInfoResolver?.GetType().Name ?? "?")
                .ToHashSet();
            Check($"premise: there are serialization contexts to walk ({contexts.Count})", contexts.Count > 0);
            var missing = contexts.Where(n => !covered.Contains(n)).ToList();
            Check("* every context that writes to a file is in FileFormats.All"
                  + (missing.Count == 0 ? "" : $" - not in the table: {string.Join(", ", missing)}"),
                  missing.Count == 0);
        }
#endif

        foreach (var f in FileFormats.All)
        {
            var (fmt, moved) = (f.Name, new List<string>());
            int keys = CollectPolicySensitiveKeys(f.Options, f.Roots, moved);
            Check($"premise: keys were measured in {fmt} ({keys}; zero makes the rest vacuous)", keys > 0);
            Check($"* {fmt}: all {keys} file keys are pinned by name"
                  + (moved.Count == 0 ? "" : $" - {moved.Count} not pinned: {string.Join(", ", moved.Take(8))}"),
                  moved.Count == 0);
        }
    }

    private static int CollectPolicySensitiveKeys(
        JsonSerializerOptions real, Type[] roots, List<string> moved)
    {
        var flipped = new JsonSerializerOptions(real)
        {
            PropertyNamingPolicy = JsonNamingPolicy.KebabCaseUpper,
        };

        var seen = new HashSet<Type>();
        int count = 0;

        void Walk(Type t)
        {
            if (!seen.Add(t)) return;
            JsonTypeInfo a, b;
            try { a = real.GetTypeInfo(t); b = flipped.GetTypeInfo(t); }
            catch { return; }

            if (a.Kind == JsonTypeInfoKind.Object)
            {
                for (int i = 0; i < a.Properties.Count && i < b.Properties.Count; i++)
                {
                    var pa = a.Properties[i];
                    if (pa.Get == null && pa.Set == null) continue;
                    count++;
                    if (!string.Equals(pa.Name, b.Properties[i].Name, StringComparison.Ordinal))
                        moved.Add($"{t.Name}.{pa.Name}");
                    Walk(pa.PropertyType);
                }
            }
            if (t.IsArray && t.GetElementType() is { } el) Walk(el);
            if (t.IsGenericType) foreach (var g in t.GetGenericArguments()) Walk(g);
            if (a.PolymorphismOptions != null)
                foreach (var d in a.PolymorphismOptions.DerivedTypes) Walk(d.DerivedType);
        }

        foreach (var r in roots) Walk(r);
        return count;
    }

    private static void TestEnumsGoOutAsNames()
    {
        var ti = PixelCore.Runtime.Assets.AssetJsonContext.Default.SpriteAtlas;

        var atlas = new PixelCore.Runtime.Assets.SpriteAtlas
        {
            Id = "deadbeef", TexturePath = "x.png", Mode = PixelCore.Runtime.Assets.SliceMode.Manual,
        };
        var json = JsonSerializer.Serialize(atlas, ti);
        Check("* the .atlas Mode is written by name (\"Manual\")", json.Contains("\"Mode\": \"Manual\""));
        Check("* it is not written as a number", !json.Contains("\"Mode\": 2"));

        PixelCore.Runtime.Assets.SpriteAtlas? Read(string mode)
            => JsonSerializer.Deserialize($"{{\"Id\":\"a\",\"TexturePath\":\"x.png\",\"Mode\":{mode}}}", ti);

        Check("* an old numeric 1 reads as Grid (files from before the rewrite still open)",
              Read("1")?.Mode == PixelCore.Runtime.Assets.SliceMode.Grid);
        Check("* an old numeric 2 reads as Manual",
              Read("2")?.Mode == PixelCore.Runtime.Assets.SliceMode.Manual);
        Check("* an old numeric 0 reads as Single",
              Read("0")?.Mode == PixelCore.Runtime.Assets.SliceMode.Single);
        Check("* the new name reads too (\"Grid\")",
              Read("\"Grid\"")?.Mode == PixelCore.Runtime.Assets.SliceMode.Grid);

        Check("* the SliceMode member order is unchanged (Single=0, Grid=1, Manual=2 - the contract with old numeric files)",
              (int)PixelCore.Runtime.Assets.SliceMode.Single == 0
              && (int)PixelCore.Runtime.Assets.SliceMode.Grid == 1
              && (int)PixelCore.Runtime.Assets.SliceMode.Manual == 2);

        var found = new List<string>();
        foreach (var f in FileFormats.All)
        {
            var seen = new HashSet<Type>();
            void Walk(Type t)
            {
                if (!seen.Add(t)) return;
                JsonTypeInfo a;
                try { a = f.Options.GetTypeInfo(t); } catch { return; }
                if (a.Kind == JsonTypeInfoKind.Object)
                    foreach (var pa in a.Properties)
                    {
                        if (pa.Get == null && pa.Set == null) continue;
                        var pt = Nullable.GetUnderlyingType(pa.PropertyType) ?? pa.PropertyType;
                        if (pt.IsEnum) found.Add($"{f.Name}:{t.Name}.{pa.Name}");
                        Walk(pa.PropertyType);
                    }
                if (t.IsArray && t.GetElementType() is { } el) Walk(el);
                if (t.IsGenericType) foreach (var g in t.GetGenericArguments()) Walk(g);
                if (a.PolymorphismOptions != null)
                    foreach (var d in a.PolymorphismOptions.DerivedTypes) Walk(d.DerivedType);
            }
            foreach (var r in f.Roots) Walk(r);
        }
        found.Sort(StringComparer.Ordinal);
        var expected = new[]
        {
            ".atlas·assets.json:SpriteAtlas.Mode",
            ".particle:EmitterShape.kind",
            ".particle:ParticlePreset.noiseMode",
            ".post:PostData.tonemap",
            ".scene:LightData.activeWhen",
            ".scene:LightData.shape",
            ".scene:PostData.tonemap",
            ".scene:SceneData.kind",
            ".scene:SoundEmitterData.bus",
            "settings:GameSettings.highlight",
        };
        Check($"premise: enums that write to files were found ({found.Count})", found.Count > 0);
        Check("* the list of file-bound enums is unchanged - if it grew, check that the string converter is on for that context"
              + (found.SequenceEqual(expected) ? "" : $" - now: {string.Join(", ", found)}"),
              found.SequenceEqual(expected));
    }

    private static void TestRealContentPromptKeys()
    {
        PixelCore.Runtime.Text.Loc.Load();
        Check($"premise: the label table is loaded ({PixelCore.Runtime.Text.Loc.Count} lines; zero makes the rest vacuous)",
              PixelCore.Runtime.Text.Loc.Count > 0);

        var bad = new List<string>();
        int prompts = 0;

        var sceneFiles = new List<string>();
        foreach (var dir in new[] { Path.Combine("Content", "Scenes"), Path.Combine("Content", "Prefabs") })
            if (Directory.Exists(dir))
                sceneFiles.AddRange(Directory.GetFiles(dir, "*.scene", SearchOption.AllDirectories));

        foreach (var full in sceneFiles)
        {
            var name = Path.GetFileName(full);
            if (System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(full))
                is not System.Text.Json.Nodes.JsonObject root) continue;
            if (root["entities"] is not System.Text.Json.Nodes.JsonArray ents) continue;

            foreach (var e in ents)
            {
                if (e is not System.Text.Json.Nodes.JsonObject eo) continue;
                var who = (string?)eo["name"] ?? "?";
                if (eo["components"] is not System.Text.Json.Nodes.JsonArray comps) continue;

                foreach (var c in comps)
                {
                    if (c is not System.Text.Json.Nodes.JsonObject co) continue;
                    if ((string?)co["type"] != "interactable" || !co.ContainsKey("prompt")) continue;

                    prompts++;
                    var key = (string?)co["prompt"] ?? "";
                    if (string.IsNullOrEmpty(key))
                    { bad.Add($"{name} - '{who}'.prompt (empty)"); continue; }
                    if (!PixelCore.Runtime.Text.Loc.Has(key))
                        bad.Add($"{name} - '{who}'.prompt - \"{key}\" is not a key in the label table " +
                                "(a sentence was written directly, or it is a typo. Add it to Content/Strings/labels.strings)");
                }
            }
        }

        Check($"premise: prompts were walked ({prompts}; zero makes the rest vacuous)", prompts > 0);
        Check($"* every interaction prompt in real Content is a label key ({prompts})"
            + (bad.Count > 0 ? "\n         " + string.Join("\n         ", bad) : ""),
              bad.Count == 0);
    }

    private static void TestAllContentSchemaCurrent()
    {
        string root = Assets.ContentPaths.Root;
        Check("premise: the Content folder exists (without it everything below is vacuous)",
            System.IO.Directory.Exists(root), root);
        if (!System.IO.Directory.Exists(root)) return;

        var files = System.IO.Directory.GetFiles(root, "*.scene", System.IO.SearchOption.AllDirectories);
        Check($"premise: scene files were really found ({files.Length})", files.Length > 0);

        var stale = new List<string>();
        foreach (var f in files)
        {
            var d = SceneSerializer.LoadFromFile(f);
            if (d != null && d.SchemaVersion < SceneData.CurrentSchemaVersion)
                stale.Add($"{System.IO.Path.GetFileName(f)}(v{d.SchemaVersion})");
        }

        Check($"* every real scene and prefab is on the current schema (v{SceneData.CurrentSchemaVersion}, {files.Length})",
            stale.Count == 0,
            stale.Count == 0 ? null
                : string.Join(", ", stale) + " - used as an instance, the pivot and scale corrections are skipped " +
                  "(see the SceneInstance.WarnIfUnmigrated note). Opening and saving it once in the editor promotes it");
    }

    private static void TestRealContentAssetRefs()
    {
        var bad = new List<string>();
        int refs = 0, files = 0;

        void Ref(string file, string key, string? value)
        {
            refs++;
            if (string.IsNullOrEmpty(value)) { bad.Add($"{file} - {key} - (empty)"); return; }
            if (!PixelCore.Runtime.Assets.AssetRegistry.LooksLikeId(value))
            { bad.Add($"{file} - {key} - '{value}' (not id-shaped - a path or name remains)"); return; }

            var rel = PixelCore.Runtime.Assets.AssetRegistry.Instance.GetPath(value);
            if (string.IsNullOrEmpty(rel))
            { bad.Add($"{file} - {key} - '{value}' (an id not in the registry)"); return; }
            if (!File.Exists(Path.Combine("Content", rel)))
                bad.Add($"{file} - {key} - '{value}' to {rel} (the file is missing)");
        }

        void Old(string file, string key, bool present)
        {
            if (present) bad.Add($"{file} - {key} (an old key remains)");
        }

        var sceneFiles = new List<string>();
        foreach (var dir in new[] { Path.Combine("Content", "Scenes"), Path.Combine("Content", "Prefabs") })
        {
            Check($"{dir} exists (the premise of the tripwire)", Directory.Exists(dir));
            if (Directory.Exists(dir))
                sceneFiles.AddRange(Directory.GetFiles(dir, "*.scene", SearchOption.AllDirectories));
        }
        Check($"premise: there are scenes and prefabs to walk ({sceneFiles.Count}; zero makes the rest vacuous)",
              sceneFiles.Count > 0);

        foreach (var full in sceneFiles)
        {
            files++;
            var name = Path.GetFileName(full);
            var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(full)) as System.Text.Json.Nodes.JsonObject;
            if (root == null) { bad.Add($"{name} (JSON parse failed)"); continue; }

            if (root["ambients"] is System.Text.Json.Nodes.JsonArray ambs)
                foreach (var a in ambs)
                {
                    if (a is not System.Text.Json.Nodes.JsonObject o) continue;
                    Old(name, "ambients[].path", o.ContainsKey("path"));
                    Ref(name, "ambients[].soundId", (string?)o["soundId"]);
                }

            if (root["entities"] is System.Text.Json.Nodes.JsonArray ents)
                foreach (var e in ents)
                {
                    if (e is not System.Text.Json.Nodes.JsonObject eo) continue;
                    var who = (string?)eo["name"] ?? "?";
                    if (eo["components"] is not System.Text.Json.Nodes.JsonArray comps) continue;
                    foreach (var c in comps)
                    {
                        if (c is not System.Text.Json.Nodes.JsonObject co) continue;
                        switch ((string?)co["type"])
                        {
                            case "soundEmitter":
                                Old(name, $"'{who}'.soundEmitter.path", co.ContainsKey("path"));
                                Ref(name, $"'{who}'.soundEmitter.soundId", (string?)co["soundId"]);
                                break;

                            case "interactable" when co.ContainsKey("sound"):
                                Ref(name, $"'{who}'.interactable.sound", (string?)co["sound"]);
                                break;
                        }
                    }
                }

            if (root["tilemapLayers"] is System.Text.Json.Nodes.JsonArray layers)
                foreach (var l in layers)
                {
                    if (l is not System.Text.Json.Nodes.JsonObject lo) continue;
                    var who = (string?)lo["name"] ?? "?";
                    Old(name, $"tilemapLayers['{who}'].tilesetPath", lo.ContainsKey("tilesetPath"));
                    if (lo.ContainsKey("tileset")) Ref(name, $"tilemapLayers['{who}'].tileset", (string?)lo["tileset"]);
                }
        }

        var oldSchema = new List<string>();
        foreach (var full in sceneFiles)
        {
            var root2 = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(full)) as System.Text.Json.Nodes.JsonObject;
            int ver = (int?)root2?["schemaVersion"] ?? 1;
            if (ver != SceneData.CurrentSchemaVersion)
                oldSchema.Add($"{Path.GetFileName(full)} - schemaVersion {ver} (current {SceneData.CurrentSchemaVersion}) " +
                              "- opening and saving it once in the editor promotes it");
        }
        Check($"* every scene and prefab in real Content is on the current schema ({sceneFiles.Count})"
            + (oldSchema.Count > 0 ? "\n         " + string.Join("\n         ", oldSchema) : ""),
              oldSchema.Count == 0);

        var surfDir = Path.Combine("Content", "Surfaces");
        var surfFiles = Directory.Exists(surfDir) ? Directory.GetFiles(surfDir, "*.surface") : Array.Empty<string>();

        foreach (var full in surfFiles)
        {
            files++;
            var name = Path.GetFileName(full);
            var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(full)) as System.Text.Json.Nodes.JsonObject;
            if (root == null) { bad.Add($"{name} (JSON parse failed)"); continue; }

            foreach (var foot in new[] { "L", "R" })
            {
                Old(name, $"clips{foot}", root.ContainsKey($"clips{foot}"));
                if (root[$"clipIds{foot}"] is not System.Text.Json.Nodes.JsonArray arr) continue;
                for (int i = 0; i < arr.Count; i++) Ref(name, $"clipIds{foot}[{i}]", (string?)arr[i]);
            }
        }

        Check($"premise: references were really counted ({refs} across {files} files; zero makes the rest vacuous)", refs > 0);
        Check($"* every audio and tileset reference in real Content is an id and reaches a file ({refs})"
            + (bad.Count > 0 ? "\n         " + string.Join("\n         ", bad) : ""),
              bad.Count == 0);
    }

    private static void TestTilesetRefMigration()
    {
        var reg = PixelCore.Runtime.Assets.AssetRegistry.Instance;
        const string TileId = "d00d0001";
        const string TilePath = "Sprites/Tileset/probe16.png";
        var path = Path.Combine(Path.GetTempPath(), "pc_tileset.scene");

        try
        {
            reg.Register(TileId, TilePath);

            File.WriteAllText(path, $$"""
            {
              "name": "pc_tileset",
              "schemaVersion": {{SceneData.CurrentSchemaVersion}},
              "tilemapLayers": [
                { "name": "Ground", "width": 2, "height": 2, "tileSize": 16,
                  "tilesetPath": "{{TilePath}}", "tiles": [] }
              ]
            }
            """);

            SceneData? loaded = null;
            string bark = CaptureStderr(() => loaded = SceneSerializer.LoadFromFile(path));
            Check("premise: the old-format tilemap scene opens", loaded is { TilemapLayers.Count: 1 });
            if (loaded is not { TilemapLayers.Count: 1 }) return;

            Check("* an old tilesetPath becomes an id",
                  loaded.TilemapLayers[0].Tileset == TileId, loaded.TilemapLayers[0].Tileset ?? "(none)");
            Check("a normal migration reports nothing", !bark.Contains("✘"), bark.Trim());

            SaveFixture(loaded, path);
            var text = File.ReadAllText(path);
            Check("* the re-saved file has no old \"tilesetPath\" key", !text.Contains("\"tilesetPath\""));
            Check("* the re-saved file has the new \"tileset\" key as an id",
                  text.Contains($"\"tileset\": \"{TileId}\""));

            File.WriteAllText(path, $$"""
            {
              "name": "pc_tileset",
              "schemaVersion": {{SceneData.CurrentSchemaVersion}},
              "tilemapLayers": [
                { "name": "Ground", "width": 2, "height": 2, "tileSize": 16,
                  "tilesetPath": "Sprites/Tileset/missingTile.png", "tiles": [] }
              ]
            }
            """);
            SceneData? lost = null;
            string lostBark = CaptureStderr(() => lost = SceneSerializer.LoadFromFile(path));
            Check("* an unresolvable tileset path preserves its value",
                  lost?.TilemapLayers[0].Tileset == "Sprites/Tileset/missingTile.png");
            Check("* an unresolvable tileset path is reported with the layer name",
                  lostBark.Contains("✘") && lostBark.Contains("Ground")
                  && lostBark.Contains("missingTile"), lostBark.Trim());

            int before = reg.Entries.Count;
            Check("* the capture stores an id rather than a path",
                  SceneSerializer.TilesetRefFor(TilePath) == TileId,
                  SceneSerializer.TilesetRefFor(TilePath) ?? "(null)");
            Check("* an existing file is not issued a new id (one file must not end up with two)",
                  reg.Entries.Count == before);
            Check("procedural generation (no path) gives null - no id is issued for a file that does not exist",
                  SceneSerializer.TilesetRefFor(null) == null
                  && SceneSerializer.TilesetRefFor("") == null);

            var fresh = SceneSerializer.TilesetRefFor("Sprites/Tileset/justAdded.png");
            Check("* an unscanned tileset is issued an id too (the reference does not break)",
                  fresh != null && PixelCore.Runtime.Assets.AssetRegistry.LooksLikeId(fresh),
                  fresh ?? "(null)");

            var probe = new TilemapLayerData { Name = "Procedural" };
            Check("a procedural layer has no id (not restored, as today)", probe.Tileset == null);
        }
        finally
        {
            reg.Remove(TileId);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void WriteLegacyAudioScene(string path, string ambientPath, string emitterPath)
        => File.WriteAllText(path, $$"""
        {
          "name": "pc_audioref",
          "schemaVersion": {{SceneData.CurrentSchemaVersion}},
          "ambients": [ { "path": "{{ambientPath}}", "volume": 0.5 } ],
          "entities": [
            {
              "id": 1,
              "name": "Vent",
              "components": [
                { "type": "transform", "x": 0, "y": 0 },
                { "type": "soundEmitter", "path": "{{emitterPath}}", "radius": 90 }
              ]
            }
          ]
        }
        """);

    private static SoundEmitterData? FindEmitter(SceneData data)
    {
        foreach (var e in data.Entities)
            foreach (var c in e.Components)
                if (c is SoundEmitterData se) return se;
        return null;
    }

    private static void SaveFixture(SceneData data, string path)
    {
        data.Name = Path.GetFileNameWithoutExtension(path);
        SceneSerializer.SaveToFile(data, path);
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

    private static void Check(string name, bool ok, string? detail = null)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name + (!ok && detail != null ? $" — {detail}" : ""));
        if (ok) _pass++; else _fail++;
    }

    private static void Report()
    {
        Console.WriteLine($"=== {_pass} passed, {_fail} failed ===");
    }
}

internal sealed class UnpinnedProbe
{
    public string SomeValue { get; set; } = "";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(UnpinnedProbe))]
internal partial class UnpinnedProbeContext : JsonSerializerContext { }

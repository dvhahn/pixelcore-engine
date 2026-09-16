#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using PixelCore.Gameplay.Player;
using PixelCore.Gameplay.Systems;
using PixelCore.Runtime.Animation;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Cutscenes;
using PixelCore.Runtime.Systems;

namespace PixelCore.Gameplay.Combat;

public static class CombatSelfTest
{
    private const float Dt = 1f / 60f;
    private static int _pass, _fail;

    private static readonly string[] Dirs = { "D", "U", "L", "R" };

    private static readonly int[] UsedKeys =
    {
        Input.SDL_SCANCODE_J, Input.SDL_SCANCODE_SPACE,
        Input.SDL_SCANCODE_W, Input.SDL_SCANCODE_A, Input.SDL_SCANCODE_S, Input.SDL_SCANCODE_D,
    };

    private const int J = Input.SDL_SCANCODE_J;
    private const int KeyA = Input.SDL_SCANCODE_A;
    private const int KeyS = Input.SDL_SCANCODE_S;
    private const int KeyD = Input.SDL_SCANCODE_D;

    public static void Run()
    {
        Console.WriteLine("=== Combat self-test ===");
        _pass = _fail = 0;

        try
        {
            TestHealth();
            TestTint();
            TestHud();
            TestInput();
            TestAttack();
            TestAttackMovement();
            TestAttackGate();
            TestSlimeChase();
            TestContact();
            TestSlimeDeath();
            TestDeath();
            TestRespawn();
            TestNavRebake();
            TestContent();
        }
        finally
        {
            ReleaseKeys();
            FreezeState.SetSoft(FreezeSource.Dialogue, false);
            RoomFlow.Abort();
            RoomFlow.RoomChangedDuringPlay = false;
            Respawn.DebugReset();
        }

        Console.WriteLine($"=== Combat: {_pass} passed, {_fail} failed ===");
    }

    private static void TestHealth()
    {
        Console.WriteLine("--- health ---");
        var scene = new Scene("HealthTest");
        var target = scene.CreateEntity("Target");
        scene.FlushPendingAdds();
        var health = target.AddComponent<Health>();
        int damaged = 0;
        health.Damaged += _ => damaged++;

        Check("a fresh health is full (three hearts of quarters)", health.Current == 12 && health.Max == Health.PlayerMax);
        Check("a hit lands", health.TakeHit(2, new Vector2(-10f, 0f), 100f) && health.Current == 10, health.Current.ToString());
        Check("* a second hit inside the invulnerable window is ignored",
              !health.TakeHit(2, Vector2.Zero, 100f) && health.Current == 10, health.Current.ToString());
        Tick(target, health.InvulnerableTime + 0.05f);
        Check("   control: once the window has passed the next hit lands",
              health.TakeHit(2, Vector2.Zero, 100f) && health.Current == 8, health.Current.ToString());
        Check("Damaged fires once per landed hit", damaged == 2, damaged.ToString());
        Check("zero damage is not a hit", !new Health().TakeHit(0, Vector2.Zero, 0f));

        var pushed = scene.CreateEntity("Pushed");
        scene.FlushPendingAdds();
        pushed.GetComponent<Transform>()!.Position = new Vector2(50f, 0f);
        var ph = pushed.AddComponent<Health>();
        ph.TakeHit(1, new Vector2(40f, 0f), 120f);
        Check("* knockback points away from the source (hit from the left, pushed right)",
              ph.KnockbackVelocity.X > 100f && MathF.Abs(ph.KnockbackVelocity.Y) < 0.01f, ph.KnockbackVelocity.ToString());
        Tick(pushed, Health.KnockbackTime + Dt);
        Check("   the push ends after its time", !ph.IsKnockedBack && ph.KnockbackVelocity == Vector2.Zero);

        var dies = scene.CreateEntity("Dies");
        scene.FlushPendingAdds();
        var dh = dies.AddComponent<Health>();
        dh.Configure(3, 0f);
        int deaths = 0;
        dh.Died += _ => deaths++;
        dh.TakeHit(2, Vector2.Zero, 0f);
        Check("   control: alive after a hit that leaves health", !dh.IsDead && deaths == 0);
        dh.TakeHit(2, Vector2.Zero, 0f);
        Check("health never goes below zero", dh.Current == 0 && dh.IsDead, dh.Current.ToString());
        Check("* Died fires exactly once", deaths == 1, deaths.ToString());
        Check("a dead target takes no more hits", !dh.TakeHit(1, Vector2.Zero, 0f) && deaths == 1);
    }

    private static void TestTint()
    {
        Console.WriteLine("--- hit flash and blink ---");
        var scene = new Scene("TintTest");
        var e = scene.CreateEntity("Tinted");
        scene.FlushPendingAdds();
        var sprite = e.AddComponent<SpriteRenderer>();
        var health = e.AddComponent<Health>();

        health.TakeHit(1, Vector2.Zero, 0f);
        Check("a hit flashes the sprite", sprite.ColorOverride == Health.FlashColor, sprite.ColorOverride?.ToString());

        bool blink = false, clear = false;
        for (float t = 0f; t < health.InvulnerableTime; t += Dt)
        {
            e.Update(Dt);
            if (sprite.ColorOverride == Health.BlinkColor) blink = true;
            else if (sprite.ColorOverride == null) clear = true;
        }
        Tick(e, 0.1f);
        Check("* it blinks while invulnerable (both states were seen)", blink && clear);
        Check("the tint is released when the window ends", sprite.ColorOverride == null, sprite.ColorOverride?.ToString());

        var foreign = new Color(10, 200, 30);
        sprite.ColorOverride = foreign;
        health.TakeHit(1, Vector2.Zero, 0f);
        Tick(e, 0.3f);
        Check("* someone else's tint is not overwritten or cleared", sprite.ColorOverride == foreign, sprite.ColorOverride?.ToString());
        sprite.ColorOverride = null;
    }

    private static void TestHud()
    {
        Console.WriteLine("--- hearts ---");
        Check("full health: three full hearts", Frames(12) == "4,4,4", Frames(12));
        Check("half a heart gone", Frames(10) == "4,4,2", Frames(10));
        Check("* the quarter frames follow the sheet order (5 quarters: full, a quarter, empty)", Frames(5) == "4,1,0", Frames(5));
        Check("dead: three empty hearts", Frames(0) == "0,0,0", Frames(0));
        Check("   control: a negative value clamps rather than reading off the sheet", Frames(-3) == "0,0,0", Frames(-3));
        Check("the heart count rounds up (13 quarters is 4 hearts, 12 is 3)",
              HealthHud.HeartCount(13) == 4 && HealthHud.HeartCount(12) == 3);
    }

    private static void TestInput()
    {
        Console.WriteLine("--- attack binding ---");
        Check("Attack is on J first, with Space too", InputMap.KeyLabel(GameAction.Attack) == "J"
              && InputMap.ScancodesOf(GameAction.Attack).Contains(Input.SDL_SCANCODE_SPACE));
        Check("Attack has a pad button (X)", InputMap.PadButtonsOf(GameAction.Attack).Contains(Gamepad.Pad.X));
    }

    private static void TestAttack()
    {
        Console.WriteLine("--- sword ---");
        var (scene, physics, player) = NewWorld(new Vector2(100f, 100f));
        var front = AddDummy(scene, new Vector2(100f, 112f), "Front");
        var behind = AddDummy(scene, new Vector2(100f, 88f), "Behind");
        var fh = front.GetComponent<Health>()!;
        var bh = behind.GetComponent<Health>()!;
        var anim = player.GetComponent<Animator>()!;

        Step(scene, physics);
        Step(scene, physics, J);
        Check("* the swing hits the target in front", fh.Current == fh.Max - 1, fh.Current.ToString());
        Check("   control: the target behind is not hit", bh.Current == bh.Max, bh.Current.ToString());
        Check("the attack clip plays for the facing", anim.CurrentClip?.Name == "Attack_D", anim.CurrentClip?.Name);
        Check("the controller reports the swing", player.GetComponent<PlayerController>()!.IsAttacking);
        Check("the swing leaves a slash effect in the scene",
              scene.Entities.Any(e => e.Name == CombatAssets.FxName) || PendingFx(scene));

        for (int i = 0; i < 40; i++) Step(scene, physics, J);
        Check("* holding the key does not swing again (a swing needs a fresh press)", fh.Current == fh.Max - 1, fh.Current.ToString());
        Step(scene, physics);
        Step(scene, physics, J);
        Check("   control: a fresh press after the swing hits again", fh.Current == fh.Max - 2, fh.Current.ToString());

        var box = Melee.HitBox(new Vector2(100f, 100f), new Vector2(1f, 0f));
        Check("a sideways swing reaches to the side, not below",
              box.Center.X > 100f && Melee.Touches(AddDummy(scene, new Vector2(112f, 100f), "Side"), box)
              && !Melee.Touches(front, box));
    }

    private static void TestAttackMovement()
    {
        Console.WriteLine("--- a swing plants the feet ---");
        var (scene, physics, player) = NewWorld(new Vector2(100f, 100f));
        var rb = player.GetComponent<Rigidbody2D>()!;
        var pc = player.GetComponent<PlayerController>()!;

        for (int i = 0; i < 20; i++) Step(scene, physics, KeyS);
        Check("premise: walking down at speed", rb.Velocity.Y > 50f, rb.Velocity.ToString());

        Step(scene, physics, KeyD, J);
        for (int i = 0; i < 9; i++) Step(scene, physics, KeyD, J);
        Check("* the player stops to swing even with a direction held", rb.Velocity.Length() < 0.01f, rb.Velocity.ToString());
        Check("* the facing stays put during the swing", pc.Facing == new Vector2(0f, 1f), pc.Facing.ToString());

        for (int i = 0; i < 30; i++) Step(scene, physics, KeyD, J);
        Check("   control: after the swing the held direction walks and turns again",
              pc.Facing == new Vector2(1f, 0f) && rb.Velocity.X > 50f, $"{pc.Facing} {rb.Velocity}");
    }

    private static void TestAttackGate()
    {
        Console.WriteLine("--- no swinging through dialogue ---");
        var (scene, physics, player) = NewWorld(new Vector2(100f, 100f));
        var fh = AddDummy(scene, new Vector2(100f, 112f), "Front").GetComponent<Health>()!;

        FreezeState.SetSoft(FreezeSource.Dialogue, true);
        Step(scene, physics);
        Step(scene, physics, J);
        Check("* a dialogue freeze blocks the swing", fh.Current == fh.Max, fh.Current.ToString());
        FreezeState.SetSoft(FreezeSource.Dialogue, false);
        Step(scene, physics);
        Step(scene, physics, J);
        Check("   control: once the dialogue ends the same press swings", fh.Current == fh.Max - 1, fh.Current.ToString());
    }

    private static void TestSlimeChase()
    {
        Console.WriteLine("--- slime chase ---");
        var (scene, physics, player) = NewWorld(new Vector2(100f, 100f));
        var far = AddSlime(scene, new Vector2(300f, 100f), "Far");
        for (int i = 0; i < 60; i++) Step(scene, physics);
        var farSlime = far.GetComponent<Slime>()!;
        Check("a slime far from the player does not chase", !farSlime.IsChasing);
        Check("   and stays near its home", Vector2.Distance(Pos(far), new Vector2(300f, 100f)) <= farSlime.WanderRadius + 4f,
              Pos(far).ToString());
        Check("   control: a slime that is not chasing holds no path", farSlime.Path.Count == 0, farSlime.Path.Count.ToString());

        var near = AddSlime(scene, new Vector2(150f, 100f), "Near");
        float before = Vector2.Distance(Pos(near), Pos(player));
        for (int i = 0; i < 30; i++) Step(scene, physics);
        float after = Vector2.Distance(Pos(near), Pos(player));
        Check("* a slime in sight chases (the gap closes)", near.GetComponent<Slime>()!.IsChasing && after < before - 10f,
              $"{before:0.0} -> {after:0.0}");

        FreezeState.SetSoft(FreezeSource.Dialogue, true);
        var held = Pos(near);
        for (int i = 0; i < 20; i++) Step(scene, physics);
        Check("* slimes hold still while dialogue freezes the player", Vector2.Distance(held, Pos(near)) < 0.5f,
              $"{held} -> {Pos(near)}");
        FreezeState.SetSoft(FreezeSource.Dialogue, false);
        for (int i = 0; i < 10; i++) Step(scene, physics);
        Check("   control: after the freeze it moves again", Vector2.Distance(held, Pos(near)) > 2f);

        var wall = new Scene("NavWall");
        var wallPhysics = new PhysicsSystem(wall);
        var hunter = AddSlime(wall, new Vector2(40f, 60f), "Hunter");
        var prey = wall.CreateEntity(Scene.PlayerName);
        prey.GetComponent<Transform>()!.Position = new Vector2(96f, 60f);
        var block = wall.CreateEntity("Wall");
        block.GetComponent<Transform>()!.Position = new Vector2(68f, 64f);
        block.AddComponent<BoxCollider2D>().Size = new Vector2(8f, 48f);
        var edge = wall.CreateEntity("Edge");
        edge.GetComponent<Transform>()!.Position = new Vector2(68f, 16f);
        edge.AddComponent<BoxCollider2D>().Size = new Vector2(120f, 4f);
        wall.FlushPendingAdds();
        Runtime.Nav.Nav.Invalidate();
        int waypoints = 0;
        for (int i = 0; i < 240 && Vector2.Distance(Pos(hunter), Pos(prey)) > 12f; i++)
        {
            Step(wall, wallPhysics);
            waypoints = Math.Max(waypoints, hunter.GetComponent<Slime>()!.Path.Count);
        }
        Check("* a wall between them is walked around (the path comes from the nav grid)",
              Vector2.Distance(Pos(hunter), Pos(prey)) <= 12f, $"{Pos(hunter)} vs {Pos(prey)}");
        Check("* it followed real waypoints, not a straight line (this is what the overlay draws)",
              waypoints > 0, waypoints.ToString());
        Runtime.Nav.Nav.Invalidate();
    }

    private static void TestContact()
    {
        Console.WriteLine("--- slime contact ---");
        var (scene, physics, player) = NewWorld(new Vector2(100f, 100f));
        var ph = player.GetComponent<Health>()!;
        var prb = player.GetComponent<Rigidbody2D>()!;

        FreezeState.SetSoft(FreezeSource.Dialogue, true);
        AddSlime(scene, new Vector2(92f, 100f), "Toucher");
        Step(scene, physics);
        Check("* no contact damage while dialogue freezes the scene", ph.Current == ph.Max, ph.Current.ToString());
        FreezeState.SetSoft(FreezeSource.Dialogue, false);

        Step(scene, physics);
        Check("   control: unfrozen, touching a slime costs half a heart", ph.Current == ph.Max - 2, ph.Current.ToString());
        Check("the player is pushed away from the slime (to the right)", prb.Velocity.X > 50f, prb.Velocity.ToString());

        for (int i = 0; i < 3; i++) Step(scene, physics, KeyA);
        Check("* the push wins over input held toward the slime", prb.Velocity.X > 0f, prb.Velocity.ToString());

        for (int i = 0; i < 20; i++) Step(scene, physics, KeyA);
        Check("invulnerability stops a touching slime draining every frame", ph.Current == ph.Max - 2, ph.Current.ToString());
    }

    private static void TestSlimeDeath()
    {
        Console.WriteLine("--- slime death ---");
        var (scene, physics, player) = NewWorld(new Vector2(100f, 100f));
        var slime = AddSlime(scene, new Vector2(220f, 220f), "Doomed");
        var sh = slime.GetComponent<Health>()!;
        Check("premise: the slime configured its own health", sh.Max == Slime.MaxHp && sh.Current == Slime.MaxHp, sh.Max.ToString());
        Check("three sword hits kill a slime", Slime.MaxHp / player.GetComponent<PlayerController>()!.AttackDamage == 3);

        var around = new Runtime.Physics.AABB(212f, 204f, 16f, 20f);
        bool aliveBeforeLast = false;
        for (int i = 0; i < Slime.MaxHp; i++)
        {
            if (i == Slime.MaxHp - 1) aliveBeforeLast = scene.Entities.Contains(slime);
            Melee.Strike(scene, player, around, 1, 0f);
            if (i < Slime.MaxHp - 1)
                for (int f = 0; f < 15; f++) Step(scene, physics);
        }
        bool puff = PendingFx(scene);
        scene.Update(Dt);
        Check("   control: before the last hit the slime was still there", aliveBeforeLast);
        Check("* the last hit removes the slime from the scene", !scene.Entities.Contains(slime));
        Check("a smoke puff is spawned where it died", puff);
        Check("a hit on a dead slime counts nothing", Melee.Strike(scene, player, around, 1, 0f) == 0);
    }

    private static void TestDeath()
    {
        Console.WriteLine("--- player death ---");
        var (scene, physics, player) = NewWorld(new Vector2(100f, 100f));
        var ph = player.GetComponent<Health>()!;
        var anim = player.GetComponent<Animator>()!;
        var start = Pos(player);

        ph.Configure(2, 0f);
        ph.TakeHit(2, new Vector2(100f, 90f), 0f);
        for (int i = 0; i < 20; i++) Step(scene, physics, KeyD);
        Check("* a dead player does not move with a direction held", Vector2.Distance(start, Pos(player)) < 0.01f, Pos(player).ToString());
        Check("the dead clip plays", anim.CurrentClip?.Name == "Dead", anim.CurrentClip?.Name);
        Check("the interactor is off while dead", player.GetComponent<Interactor>() is { Enabled: false });
        Step(scene, physics);
        Step(scene, physics, J);
        Check("the attack key does nothing while dead", anim.CurrentClip?.Name == "Dead", anim.CurrentClip?.Name);

        var slime = AddSlime(scene, new Vector2(130f, 100f), "Watcher");
        for (int i = 0; i < 20; i++) Step(scene, physics);
        Check("slimes lose interest in a dead player", !slime.GetComponent<Slime>()!.IsChasing);
    }

    private static void TestRespawn()
    {
        Console.WriteLine("--- respawn ---");
        Check("premise: the start room is in the registry (Transition refuses unknown rooms)",
              AssetRegistry.Instance.GetPath(LevelSetup.StartRoom.SceneId) != null);

        var camera = new Camera(320, 180);
        RoomFlow.Abort();
        Respawn.DebugReset();

        var (alive, _, _) = NewWorld(new Vector2(100f, 100f));
        for (float t = 0f; t < Respawn.Delay + 0.5f; t += Dt) GameSystems.Update(alive, camera, Dt);
        Check("   control: a living player is never sent back", !RoomFlow.IsTransitioning);

        var (scene, _, player) = NewWorld(new Vector2(100f, 100f));
        var ph = player.GetComponent<Health>()!;
        ph.Configure(1, 0f);
        ph.TakeHit(1, Vector2.Zero, 0f);

        bool early = false;
        for (float t = 0f; t < Respawn.Delay - 0.1f; t += Dt)
        {
            GameSystems.Update(scene, camera, Dt);
            early |= RoomFlow.IsTransitioning;
        }
        Check("no respawn before the delay", !early);
        for (int i = 0; i < 12; i++) GameSystems.Update(scene, camera, Dt);
        Check("* after the delay the start room reloads (through GameSystems.Update, the path the game runs)", RoomFlow.IsTransitioning);
        Check("the landing point exists in the start room",
              File.ReadAllText(Path.Combine(ContentPaths.Root, AssetRegistry.Instance.GetPath(LevelSetup.StartRoom.SceneId) ?? ""))
                  .Contains($"\"{Respawn.SpawnPoint}\""));
        RoomFlow.Abort();
        Respawn.DebugReset();
    }

    private static void TestNavRebake()
    {
        Console.WriteLine("--- nav grid follows the room ---");
        var scene = new Scene("NavRoom");
        var camera = new Camera(320, 180);
        var first = Runtime.Nav.Nav.For(scene);
        Check("premise: the grid is cached for the same scene", ReferenceEquals(first, Runtime.Nav.Nav.For(scene)));

        var previous = RoomFlow.LoadRoomImpl;
        RoomFlow.LoadRoomImpl = _ => true;
        try { RoomFlow.LoadImmediate(LevelSetup.StartRoom.SceneId, "", scene, camera); }
        finally { RoomFlow.LoadRoomImpl = previous; RoomFlow.RoomChangedDuringPlay = false; }

        Check("* loading a room rebakes the grid (the scene object is reused, so the cache would serve the old room)",
              !ReferenceEquals(first, Runtime.Nav.Nav.For(scene)));
        Runtime.Nav.Nav.Invalidate();
    }

    private static void TestContent()
    {
        Console.WriteLine("--- sample content ---");

        var hero = Json("Prefabs/Hero.scene");
        var heroComponents = RootComponents(hero);
        Check("* the hero prefab carries Health (without it slimes cannot hurt and the hearts draw nothing)",
              heroComponents.Any(c => Marker(c) == typeof(Health).FullName));
        var clipNames = heroComponents.Where(c => Type(c) == "animator")
            .SelectMany(c => c.GetProperty("clips").EnumerateArray().Select(id => id.GetString()))
            .Select(id => AssetRegistry.Instance.GetPath(id!))
            .Where(p => p != null)
            .Select(p => Json(p!).GetProperty("Name").GetString())
            .ToHashSet();
        var missing = PlayerClips.Required().Where(n => !clipNames.Contains(n)).ToList();
        Check("* every clip the controller plays is in the hero prefab", missing.Count == 0, string.Join(", ", missing));
        Check("   control: the required list names the attack and dead clips",
              PlayerClips.Required().Contains("Attack_D") && PlayerClips.Required().Contains("Dead"));

        var slimePath = AssetRegistry.Instance.GetPath(CombatAssets.SlimePrefabId);
        Check("the slime prefab is registered", slimePath != null);
        if (slimePath != null)
        {
            var parts = RootComponents(Json(slimePath)).Select(c => Marker(c) ?? Type(c)).ToList();
            Check("the slime prefab has a body, a round collider, health and the brain",
                  parts.Contains("rigidbody") && parts.Contains("circleCollider")
                  && parts.Contains(typeof(Health).FullName) && parts.Contains(typeof(Slime).FullName), string.Join(", ", parts));
            Check("* Health comes before Slime (Slime configures it when it is added)",
                  parts.IndexOf(typeof(Health).FullName) < parts.IndexOf(typeof(Slime).FullName));
        }

        var villagePath = AssetRegistry.Instance.GetPath(LevelSetup.StartRoom.SceneId);
        int slimes = villagePath == null ? 0 : Json(villagePath).GetProperty("entities").EnumerateArray()
            .SelectMany(e => e.GetProperty("components").EnumerateArray())
            .Count(c => Type(c) == "sceneInstance" && c.GetProperty("sceneId").GetString() == CombatAssets.SlimePrefabId);
        Check("the village has slimes to fight", slimes >= 3, slimes.ToString());

        foreach (var (id, clips) in new[] { (CombatAssets.SlashFxPrefabId, Dirs.Select(d => $"Slash_{d}").ToArray()),
                                           (CombatAssets.SmokeFxPrefabId, new[] { "Smoke" }) })
        {
            var path = AssetRegistry.Instance.GetPath(id);
            Check($"effect prefab {id} is registered", path != null);
            if (path == null) continue;
            var comps = RootComponents(Json(path));
            Check($"{Path.GetFileName(path)} removes itself (OneShotFx)", comps.Any(c => Marker(c) == typeof(OneShotFx).FullName));
            var anims = comps.Where(c => Type(c) == "animator")
                .SelectMany(c => c.GetProperty("clips").EnumerateArray().Select(x => AssetRegistry.Instance.GetPath(x.GetString()!)))
                .Where(p => p != null).Select(p => Json(p!)).ToList();
            Check($"* {Path.GetFileName(path)} clips never loop (a looping clip would never remove itself)",
                  anims.Count == clips.Length && anims.All(a => !a.GetProperty("Loop").GetBoolean()), anims.Count.ToString());
            Check($"{Path.GetFileName(path)} has the clips the code plays",
                  clips.All(n => anims.Any(a => a.GetProperty("Name").GetString() == n)));
        }

        foreach (var sound in new[] { CombatAssets.SwingSound, CombatAssets.HitSound, CombatAssets.HurtSound, CombatAssets.PopSound })
            Check($"sound {sound} exists", File.Exists(Path.Combine(ContentPaths.Root, sound + ".wav")));

        var heart = Path.Combine(ContentPaths.Root, CombatAssets.HeartTexture);
        var (w, h) = PngSize(heart);
        Check("the heart sheet is five 16px frames (empty to full)", w == 5 * HealthHud.HeartSize && h == HealthHud.HeartSize, $"{w}x{h}");
    }

    private static (Scene, PhysicsSystem, Entity) NewWorld(Vector2 at)
    {
        var scene = new Scene("CombatTest");
        var physics = new PhysicsSystem(scene);
        var player = scene.CreateEntity(Scene.PlayerName);
        player.GetComponent<Transform>()!.Position = at;
        var capsule = player.AddComponent<CapsuleCollider2D>();
        capsule.Radius = 3f; capsule.Length = 2f; capsule.Horizontal = true; capsule.Offset = new Vector2(0f, -2f);
        player.AddComponent<Rigidbody2D>().UseGravity = false;
        player.AddComponent<PlayerController>();
        player.AddComponent<Health>();
        player.AddComponent<Interactor>();
        var anim = player.AddComponent<Animator>();
        foreach (var d in Dirs)
        {
            anim.AddClip(new AnimationClip($"Walk_{d}").AddFramesFromRow(0, 0, 4, 16, 16));
            anim.AddClip(new AnimationClip($"Idle_{d}").AddFramesFromRow(0, 0, 1, 16, 16));
            var attack = new AnimationClip($"Attack_{d}").AddFramesFromRow(0, 0, 1, 16, 16, 0.28f);
            attack.Loop = false;
            anim.AddClip(attack);
        }
        var dead = new AnimationClip("Dead").AddFramesFromRow(0, 0, 1, 16, 16);
        dead.Loop = false;
        anim.AddClip(dead);
        scene.FlushPendingAdds();

        ReleaseKeys();
        Input.DebugTick();
        return (scene, physics, player);
    }

    private static Entity AddDummy(Scene scene, Vector2 at, string name)
    {
        var e = scene.CreateEntity(name);
        e.GetComponent<Transform>()!.Position = at;
        var circle = e.AddComponent<CircleCollider2D>();
        circle.Radius = 5f; circle.Offset = new Vector2(0f, -4f);
        e.AddComponent<Health>().Configure(12, 0f);
        scene.FlushPendingAdds();
        return e;
    }

    private static Entity AddSlime(Scene scene, Vector2 at, string name)
    {
        var e = scene.CreateEntity(name);
        e.GetComponent<Transform>()!.Position = at;
        var circle = e.AddComponent<CircleCollider2D>();
        circle.Radius = 5f; circle.Offset = new Vector2(0f, -4f);
        e.AddComponent<Rigidbody2D>().UseGravity = false;
        e.AddComponent<Health>();
        e.AddComponent<Slime>();
        var anim = e.AddComponent<Animator>();
        foreach (var d in Dirs) anim.AddClip(new AnimationClip($"Walk_{d}").AddFramesFromRow(0, 0, 4, 16, 16));
        scene.FlushPendingAdds();
        return e;
    }

    private static void Step(Scene scene, PhysicsSystem physics, params int[] keys)
    {
        Input.DebugTick();
        ReleaseKeys();
        foreach (var k in keys) Input.DebugSetKey(k, true);
        scene.Update(Dt);
        scene.FixedTick(Dt);
        physics.Update(Dt);
    }

    private static void Tick(Entity e, float seconds)
    {
        for (float t = 0f; t < seconds; t += Dt)
        {
            e.Update(Dt);
            e.FixedTick(Dt);
        }
    }

    private static void ReleaseKeys()
    {
        foreach (var k in UsedKeys) Input.DebugSetKey(k, false);
    }

    private static bool PendingFx(Scene scene)
    {
        scene.FlushPendingAdds();
        return scene.Entities.Any(e => e.Name == CombatAssets.FxName);
    }

    private static Vector2 Pos(Entity e) => e.GetComponent<Transform>()!.Position;

    private static string Frames(int current)
        => string.Join(",", Enumerable.Range(0, 3).Select(i => HealthHud.FrameFor(current, i)));

    private static JsonElement Json(string contentRelative)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(ContentPaths.Root, contentRelative)));
        return doc.RootElement.Clone();
    }

    private static List<JsonElement> RootComponents(JsonElement scene)
        => scene.GetProperty("entities").EnumerateArray()
            .Where(e => e.GetProperty("parentId").GetInt32() == 0)
            .Take(1)
            .SelectMany(e => e.GetProperty("components").EnumerateArray())
            .ToList();

    private static string? Type(JsonElement component)
        => component.TryGetProperty("type", out var t) ? t.GetString() : null;

    private static string? Marker(JsonElement component)
        => Type(component) == "component" && component.TryGetProperty("name", out var n) ? n.GetString() : null;

    private static (int, int) PngSize(string path)
    {
        if (!File.Exists(path)) return (0, 0);
        var b = File.ReadAllBytes(path);
        if (b.Length < 24) return (0, 0);
        return ((b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]);
    }

    private static void Check(string name, bool ok, string? detail = null)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name + (ok || detail == null ? "" : $"  ({detail})"));
        if (ok) _pass++; else _fail++;
    }
}
#endif

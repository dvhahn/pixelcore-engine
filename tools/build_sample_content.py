#!/usr/bin/env python3
"""
Builds the sample game's content from the Ninja Adventure pack (CC0): character atlases and clips, the
player and slime prefabs, the sword and smoke effects, prop atlases, particle presets, the dialogue bubble skin,
and the Village and House scenes.

Run from the repository root after copying the source PNGs and sounds into Content (see ASSETS.md):

    python3 tools/build_sample_content.py

It is idempotent. Files that already carry an id keep it, and the registry (Content/assets.json) is only
extended, so running it again rewrites the same bytes. Ground rows are autotiled with the same rules as the
editor's terrain brush (Runtime/Tilemap/TerrainBrush.cs), reading the .tileset sidecar that this script writes
from the tile pixels.

After running, boot the editor once: the asset scan should report nothing promoted and nothing moved.
"""
import hashlib
import json
import os
import random
import uuid

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
C = os.path.join(ROOT, "Content")
TS = 16

# Ids the code refers to directly (Gameplay/Rooms.cs, Gameplay/Systems/LevelSetup.cs).
VILLAGE_ID = "5a1c7e20"
HOUSE_ID = "b83f0d4e"
HERO_ID = "3e9d61a7"
COTTAGE_ID = "7c0e2b51"
SLIME_ID = "51a3e0c4"        # Gameplay/Combat/CombatAssets.cs
SLASH_FX_ID = "f7a2c913"
SMOKE_FX_ID = "0d6b8e25"

REG_PATH = os.path.join(C, "assets.json")
reg_text = open(REG_PATH, encoding="utf-8").read()
reg = json.loads(reg_text)


def dump(obj):
    return json.dumps(normalise(obj), indent=2, ensure_ascii=False)


def normalise(o):
    """Whole floats are written as integers, the way the engine's serializer writes them."""
    if isinstance(o, float) and o.is_integer():
        return int(o)
    if isinstance(o, dict):
        return {k: normalise(v) for k, v in o.items()}
    if isinstance(o, list):
        return [normalise(v) for v in o]
    return o


def write(rel, obj):
    path = os.path.join(C, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        f.write(dump(obj))


def id_of_path(rel):
    for k, v in reg.items():
        if v["Path"] == rel:
            return k
    return None


def fresh_id():
    while True:
        i = uuid.uuid4().hex[:8]
        if i not in reg:
            return i


def register(rel, embedded, want=None):
    """Registry id for a Content-relative path. Binary files get the scanner's SHA1 fingerprint."""
    i = id_of_path(rel)
    if i is None:
        i = want or fresh_id()
        if i in reg:
            raise SystemExit(f"id {i} for {rel} already belongs to {reg[i]['Path']}")
        reg[i] = {"Path": rel, "Hash": ""}
    if not embedded:
        with open(os.path.join(C, rel), "rb") as f:
            reg[i]["Hash"] = hashlib.sha1(f.read()).hexdigest().upper()
    return i


def embedded(rel, want=None):
    """Id for a file that stores its own id: the one in the file wins, then the registry, then a new one."""
    path = os.path.join(C, rel)
    if os.path.exists(path):
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        current = data.get("Id") or data.get("id")
        if current:
            return register(rel, True, current)
    return register(rel, True, want)


# ── Images derived from the pack ────────────────────────────────────────────

def derive_images():
    fx = os.path.join(C, "Sprites/FX")
    # The pack animates rain as three frames; particles take one texture, so the long streak and the wide
    # splash are kept.
    for src, dst in (("Rain.png", "RainDrop.png"), ("RainOnFloor.png", "RainSplash.png")):
        if os.path.exists(os.path.join(fx, src)):
            Image.open(os.path.join(fx, src)).crop((8, 0, 16, 8)).save(os.path.join(fx, dst))
            os.remove(os.path.join(fx, src))

    # Dialogue bubble mask, tinted by DialogueBubble.FillColor. The 9-slice borders and the tail tip are the
    # constants in Runtime/UI/DialogueBubble.cs: left 9, top 5, right 5, bottom 8, tail tip at x = 5.
    skin = Image.new("RGBA", (31, 27), (0, 0, 0, 0))
    px = skin.load()
    body = {0: (3, 27), 1: (1, 29), 2: (1, 29), 19: (1, 29), 20: (1, 29), 21: (3, 27)}
    for y in range(22):
        x0, x1 = body.get(y, (0, 30))
        for x in range(x0, x1 + 1):
            px[x, y] = (255, 255, 255, 255)
    for y, (x0, x1) in {22: (3, 8), 23: (4, 7), 24: (4, 6), 25: (5, 6), 26: (5, 5)}.items():
        for x in range(x0, x1 + 1):
            px[x, y] = (255, 255, 255, 255)
    skin.save(os.path.join(C, "Sprites/UI/DialogueUI.png"))

    # The pack's slash sweeps across five 26x32 frames facing down. Each frame is centred in a 32px cell and turned
    # for the other directions, one row per direction.
    src = os.path.join(fx, "SpriteSheetSlash01.png")
    if os.path.exists(src):
        sheet = Image.open(src).convert("RGBA")
        out = Image.new("RGBA", (5 * 32, len(DIRS) * 32), (0, 0, 0, 0))
        turns = {"D": 0, "U": 180, "L": -90, "R": 90}
        for f in range(5):
            cell = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
            cell.paste(sheet.crop((f * 26, 0, (f + 1) * 26, 32)), (3, 0))
            for row, d in enumerate(DIRS):
                out.paste(cell.rotate(turns[d]), (f * 32, row * 32))
        out.save(os.path.join(fx, "Slash.png"))
        os.remove(src)


# ── Characters ──────────────────────────────────────────────────────────────

DIRS = ["D", "U", "L", "R"]   # the pack's column order


def opaque_width(rel):
    im = Image.open(os.path.join(C, rel)).convert("RGBA")
    a = im.load()
    widest = 0
    for cy in range(im.height // TS):
        for cx in range(im.width // TS):
            xs = [x for x in range(TS) for y in range(TS) if a[cx * TS + x, cy * TS + y][3] > 0]
            if xs:
                widest = max(widest, max(xs) - min(xs) + 1)
    return widest


# (sheet, frames, seconds per frame, frame events, loops). A sheet one cell wide has no directions.
WALK = ("Walk", 4, 0.15, {1: "step_l", 3: "step_r"}, True)
IDLE = ("Idle", 1, 0.5, {}, True)
ATTACK = ("Attack", 1, 0.28, {}, False)
DEAD = ("Dead", 1, 0.5, {}, False)
CRAWL = ("Walk", 4, 0.15, {}, True)


def character(ch, sheets=(WALK, IDLE)):
    """An atlas per sheet plus a clip per direction (named Sheet_D) or one clip (named Sheet). Returns name -> ids."""
    clips = {}
    for sheet, frames, duration, events, loop in sheets:
        png = f"Sprites/Characters/{ch}/{sheet}.png"
        register(png, False)
        dirs = DIRS if Image.open(os.path.join(C, png)).width >= len(DIRS) * TS else [None]
        atlas_rel = f"Sprites/Characters/{ch}/{sheet}.atlas"
        atlas_id = embedded(atlas_rel)
        slices = []
        for f in range(frames):
            for x, d in enumerate(dirs):
                slices.append({"Name": f"{sheet}_{d}_{f}" if d else f"{sheet}_{f}", "X": x * TS, "Y": f * TS,
                               "Width": TS, "Height": TS, "PivotX": 0.5, "PivotY": 1.0})
        write(atlas_rel, {
            "Id": atlas_id, "TexturePath": f"{sheet}.png", "Mode": "Grid",
            "CellWidth": TS, "CellHeight": TS, "OffsetX": 0, "OffsetY": 0, "SpacingX": 0, "SpacingY": 0,
            "OpaqueWidth": opaque_width(png), "Slices": slices,
        })
        for d in dirs:
            name = f"{sheet}_{d}" if d else sheet
            rel = f"Animations/Characters/{ch}/{name}.anim"
            clip_id = embedded(rel)
            write(rel, {
                "Id": clip_id, "Name": name, "AtlasId": atlas_id, "AtlasPath": png,
                "Frames": [{"SliceIndex": f * len(dirs) + dirs.index(d), "Duration": duration,
                            "EventName": events.get(f)} for f in range(frames)],
                "Loop": loop,
            })
            clips[name] = (clip_id, atlas_id)
    return clips


def fx_clips(name, png, cell, frames, rows, duration, pivot_y):
    """Equal cells, frames left to right, one non-looping clip per row (Name_D for a direction row, Name otherwise)."""
    register(png, False)
    atlas_rel = png[:-len(".png")] + ".atlas"
    atlas_id = embedded(atlas_rel)
    write(atlas_rel, {
        "Id": atlas_id, "TexturePath": os.path.basename(png), "Mode": "Grid",
        "CellWidth": cell, "CellHeight": cell, "OffsetX": 0, "OffsetY": 0, "SpacingX": 0, "SpacingY": 0,
        "OpaqueWidth": 0,
        "Slices": [{"Name": f"{name}_{row}_{f}" if row else f"{name}_{f}", "X": f * cell, "Y": r * cell,
                    "Width": cell, "Height": cell, "PivotX": 0.5, "PivotY": pivot_y}
                   for r, row in enumerate(rows) for f in range(frames)],
    })
    clips = {}
    for r, row in enumerate(rows):
        clip = f"{name}_{row}" if row else name
        rel = f"Animations/FX/{clip}.anim"
        clip_id = embedded(rel)
        write(rel, {
            "Id": clip_id, "Name": clip, "AtlasId": atlas_id, "AtlasPath": png,
            "Frames": [{"SliceIndex": r * frames + f, "Duration": duration, "EventName": None} for f in range(frames)],
            "Loop": False,
        })
        clips[clip] = (clip_id, atlas_id)
    return clips


def sprite(atlas, slice_, rect, cast_shadow=True, layer=0):
    x, y, w, h = rect
    return {"type": "sprite", "texture": None, "atlas": atlas, "slice": slice_,
            "srcX": x, "srcY": y, "srcW": w, "srcH": h,
            "colorR": 255, "colorG": 255, "colorB": 255, "colorA": 255,
            "renderLayer": layer, "sortOffset": 0, "flipX": False, "flipY": False,
            "pivotX": 0.5, "pivotY": 1.0, "emissive": 0,
            "castShadow": cast_shadow, "shadowStyle": "Soft", "shadowScale": 1}


def transform(x, y):
    return {"type": "transform", "x": x, "y": y, "rotation": 0}


def box(w, h, oy=None, trigger=False):
    return {"type": "collider", "offsetX": 0, "offsetY": -h / 2 if oy is None else oy, "sizeX": w, "sizeY": h,
            "isTrigger": trigger, "isOneWay": False}


SCENE_HEADER = {
    "lightingEnabled": False, "ambientR": 255, "ambientG": 255, "ambientB": 255, "exterior": False,
    "viewZoom": 1, "cameraFixed": False, "cameraFixedX": 0, "cameraFixedY": 0,
    "cameraBoundsEnabled": False, "cameraDampingEnabled": False, "cameraDampingX": 2.3, "cameraDampingY": 2.3,
    "cameraBoundsX": 0, "cameraBoundsY": 0, "cameraBoundsW": 0, "cameraBoundsH": 0,
    "postTintR": 255, "postTintG": 255, "postTintB": 255, "postTintStrength": 0,
    "postFogR": 185, "postFogG": 195, "postFogB": 215, "postFogOpacity": 0, "postVignette": 0,
}


def scene(rel, scene_id, name, kind, entities, layers, **header):
    head = dict(SCENE_HEADER)
    head.update(header)
    for i, e in enumerate(entities, start=1):
        e["id"] = i
    data = {"schemaVersion": 2, "nextEntityId": len(entities) + 1, "id": scene_id, "name": name, "kind": kind}
    data.update(head)
    data["entities"] = entities
    data["tilemapLayers"] = layers
    data["tilemapActive"] = True
    register(rel, True, scene_id)
    write(rel, data)


def entity(name, *components):
    return {"id": 0, "parentId": 0, "name": name, "active": True, "components": list(components)}


def hero_prefab(clips):
    order = [f"Walk_{d}" for d in DIRS] + [f"Idle_{d}" for d in DIRS] + [f"Attack_{d}" for d in DIRS] + ["Dead"]
    idle_atlas = clips["Idle_D"][1]
    player = entity(
        "Player",
        transform(0, 0),
        sprite(idle_atlas, "Idle_D_0", (0, 0, TS, TS)),
        {"type": "animator", "clips": [clips[n][0] for n in order], "defaultClip": "Idle_D"},
        {"type": "playerController"},
        {"type": "component", "name": "PixelCore.Gameplay.Combat.Health"},
        {"type": "interactor"},
        {"type": "footstepEmitter"},
        {"type": "rigidbody", "useGravity": False, "gravityScale": 1, "isKinematic": False, "maxFallSpeed": 800},
        {"type": "capsuleCollider", "offsetX": 0, "offsetY": -2, "radius": 3, "length": 2, "horizontal": True,
         "isTrigger": False},
    )
    scene("Prefabs/Hero.scene", HERO_ID, "Hero", "Prefab", [player], [],
          ambientR=110, ambientG=110, ambientB=130)


def slime_prefab(clips):
    """Health comes before Slime: Slime configures the health it finds when it is added."""
    body = entity(
        "Slime",
        transform(0, 0),
        sprite(clips["Walk_D"][1], "Walk_D_0", (0, 0, TS, TS)),
        {"type": "animator", "clips": [clips[f"Walk_{d}"][0] for d in DIRS], "defaultClip": "Walk_D"},
        {"type": "rigidbody", "useGravity": False, "gravityScale": 1, "isKinematic": False, "maxFallSpeed": 800},
        {"type": "circleCollider", "offsetX": 0, "offsetY": -4, "radius": 5, "isTrigger": False},
        {"type": "component", "name": "PixelCore.Gameplay.Combat.Health"},
        {"type": "component", "name": "PixelCore.Gameplay.Combat.Slime"},
    )
    scene("Prefabs/Slime.scene", SLIME_ID, "Slime", "Prefab", [body], [],
          ambientR=110, ambientG=110, ambientB=130)


def fx_prefab(rel, scene_id, name, clips, default, layer):
    """A one-shot effect: plays a clip and removes itself (OneShotFx). The code picks the clip for a direction."""
    root = entity(
        name,
        transform(0, 0),
        sprite(clips[default][1], f"{default}_0", (0, 0, 32, 32), cast_shadow=False, layer=layer),
        {"type": "animator", "clips": [c[0] for c in clips.values()], "defaultClip": default},
        {"type": "component", "name": "PixelCore.Gameplay.Combat.OneShotFx"},
    )
    scene(rel, scene_id, name, "Prefab", [root], [], ambientR=110, ambientG=110, ambientB=130)


# ── Props ───────────────────────────────────────────────────────────────────

HOUSE_SLICES = {"House_Orange": (0, 0, 64, 48), "House_Beige": (64, 0, 64, 48), "House_Red": (192, 0, 64, 48)}
NATURE_SLICES = {
    "Tree_Round": (0, 0, 32, 32), "Tree_Pine": (32, 0, 32, 32),
    "Tree_BigPine": (0, 32, 64, 48), "Tree_Big": (64, 32, 64, 48),
    "Tree_Small": (96, 128, 32, 32), "Stump": (0, 128, 32, 32), "Rock": (208, 128, 32, 32),
    "Pebbles": (256, 288, 16, 16),
}


def cottage_prefab(house_atlas, nature_atlas):
    """The house as a prefab: the building, its door, and the pebbles on the doorstep.

    The pebbles are floor decoration (BelowEntities) that still cast a shadow, so the shadow renderer's Below band
    has real content to draw."""
    root = entity("Cottage", transform(0, 0),
                  sprite(house_atlas, "House_Orange", HOUSE_SLICES["House_Orange"], cast_shadow=False),
                  box(60, 18))
    door = entity("Door", transform(0, 4),
                  {"type": "interactable", "prompt": "verb.enter", "kind": "Door", "target": HOUSE_ID,
                   "spawn": "Spawn_FromVillage", "range": 10, "facing": "Up"})
    step = entity("Doorstep", transform(0, 14),
                  sprite(nature_atlas, "Pebbles", NATURE_SLICES["Pebbles"], layer=-500))
    door["parentId"] = step["parentId"] = 1      # the root is written first, so it gets id 1
    scene("Prefabs/Cottage.scene", COTTAGE_ID, "Cottage", "Prefab", [root, door, step], [],
          ambientR=110, ambientG=110, ambientB=130)


def prop_atlas(png_name, slices):
    png = f"Sprites/Tileset/NinjaAdventure/{png_name}.png"
    png_id = register(png, False)
    rel = f"Sprites/Tileset/NinjaAdventure/{png_name}.atlas"
    atlas_id = embedded(rel)
    write(rel, {
        "Id": atlas_id, "TexturePath": f"{png_name}.png", "Mode": "Manual",
        "CellWidth": TS, "CellHeight": TS, "OffsetX": 0, "OffsetY": 0, "SpacingX": 0, "SpacingY": 0,
        "OpaqueWidth": 0,
        "Slices": [{"Name": n, "X": r[0], "Y": r[1], "Width": r[2], "Height": r[3], "PivotX": 0.5, "PivotY": 1.0}
                   for n, r in slices.items()],
    })
    return atlas_id, png_id


# ── Particles ───────────────────────────────────────────────────────────────

def particle(rel, name, preset):
    pid = embedded(rel)
    write(rel, {"id": pid, "name": name, "preset": preset})
    return pid


def presets():
    drop = register("Sprites/FX/RainDrop.png", False)
    splash = register("Sprites/FX/RainSplash.png", False)
    common = {"burstMin": 0, "burstMax": 0, "gravityX": 0, "gravityY": 0, "swayAmplitude": 0, "swayFrequency": 0,
              "scale": {"min": 1, "max": 1}, "rotation": {"min": 0, "max": 0},
              "rotationSpeed": {"min": 0, "max": 0}, "tintR": 210, "tintG": 225, "tintB": 255,
              "sortYOffset": 0, "followEmitter": False, "additive": False}
    rain = particle("Particles/Rain.particle", "Rain", dict(common, **{
        "shape": {"kind": "Box", "boxW": 820, "boxH": 4, "radius": 0},
        "rate": 220, "maxParticles": 600, "speed": {"min": 230, "max": 270},
        "direction": 112, "spread": 2, "lifetime": {"min": 1.7, "max": 2.0},
        "alpha": {"min": 0.55, "max": 0.8}, "texture": drop, "renderLayer": 500,
        "prewarmSeconds": 2, "snapToPixels": False,
    }))
    splash_id = particle("Particles/RainSplash.particle", "RainSplash", dict(common, **{
        "shape": {"kind": "Box", "boxW": 640, "boxH": 416, "radius": 0},
        "rate": 70, "maxParticles": 60, "speed": {"min": 0, "max": 0},
        "direction": 90, "spread": 0, "lifetime": {"min": 0.18, "max": 0.26},
        "alpha": {"min": 0.7, "max": 0.9}, "texture": splash, "renderLayer": -500,
        "prewarmSeconds": 0,
    }))
    return rain, splash_id


# ── Tilemaps ────────────────────────────────────────────────────────────────

PEERS = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)]


def autotile(grid, tileset_rel):
    """Terrain ids -> tile ids, the TerrainBrush rules: corners follow their sides, an empty side matches any side
    that is not the tile's own terrain, fuller tiles win ties, then a stable per-cell variant."""
    with open(os.path.join(C, tileset_rel), encoding="utf-8") as f:
        ts = json.load(f)
    by = {}
    for t in ts["tiles"]:
        by.setdefault(t["terrain"], []).append(t)
    h, w = len(grid), len(grid[0])

    def norm(T, want):
        want = list(want)
        for c in (1, 3, 5, 7):
            a, b = c - 1, (c + 1) % 8
            if not (want[a] == T and want[b] == T):
                want[c] = want[a] if want[a] != T else want[b]
        return want

    rows = []
    for y in range(h):
        cells = []
        for x in range(w):
            T = grid[y][x]
            want = norm(T, [grid[y + dy][x + dx] if 0 <= x + dx < w and 0 <= y + dy < h else T for dx, dy in PEERS])
            best, ties = None, []
            for t in by[T]:
                score = sum(1 for i in range(8) if (t["peering"][i] != T if want[i] < 0 else t["peering"][i] == want[i]))
                key = (score, sum(1 for p in t["peering"] if p == T))
                if best is None or key > best:
                    best, ties = key, [t]
                elif key == best:
                    ties.append(t)
            variant = ((x * 73856093) ^ (y * 19349663)) & 0xFFFFFFFF
            cells.append(str(ties[variant % len(ties)]["id"]))
        rows.append(",".join(cells))
    return rows


def id_rows(cells):
    return [",".join("." if c is None else str(c) for c in row) for row in cells]


def layer(name, rows, tileset, width, height, collision=False, order=0, render=-1000):
    return {"name": name, "width": width, "height": height, "tileSize": TS, "renderLayer": render,
            "hasCollision": collision, "drawOrder": order, "rows": rows, "tileset": tileset}


# ── Terrain sidecar ─────────────────────────────────────────────────────────

# The grass and dirt block of TilesetFloor.png, as (row, first column, last column).
FLOOR_TERRAIN_BLOCK = [(7, 0, 9), (8, 0, 9), (9, 0, 10), (10, 0, 10), (11, 0, 8), (12, 0, 4)]


def floor_terrain_sidecar():
    """Writes TilesetFloor.tileset by reading the tile pixels: each edge and corner is dirt when it is redder than it
    is green. A corner counts only when both of its sides are dirt, and a tile belongs to dirt when any side, or its
    centre, is dirt. Peering order: top, top-right, right, bottom-right, bottom, bottom-left, left, top-left."""
    png = os.path.join(C, "Sprites/Tileset/NinjaAdventure/TilesetFloor.png")
    im = Image.open(png).convert("RGBA")
    px = im.load()
    cols = im.width // TS

    def dirt(tx, ty, x0, y0, x1, y1):
        r = g = n = 0
        for y in range(y0, y1):
            for x in range(x0, x1):
                c = px[tx * TS + x, ty * TS + y]
                if c[3]:
                    r += c[0]; g += c[1]; n += 1
        return 1 if n and r > g else 0

    lines = []
    for row, first, last in FLOOR_TERRAIN_BLOCK:
        for col in range(first, last + 1):
            top, bottom = dirt(col, row, 7, 0, 9, 1), dirt(col, row, 7, 15, 9, 16)
            left, right = dirt(col, row, 0, 7, 1, 9), dirt(col, row, 15, 7, 16, 9)
            peering = [top, int(top and right and dirt(col, row, 15, 0, 16, 1)), right,
                       int(bottom and right and dirt(col, row, 15, 15, 16, 16)), bottom,
                       int(bottom and left and dirt(col, row, 0, 15, 1, 16)), left,
                       int(top and left and dirt(col, row, 0, 0, 1, 1))]
            terrain = 1 if any(peering) or dirt(col, row, 7, 7, 9, 9) else 0
            lines.append(f'    {{ "id": {row * cols + col}, "terrain": {terrain}, "peering": [{", ".join(map(str, peering))}] }}')
    text = ('{\n  "tileWidth": 16,\n  "tileHeight": 16,\n  "terrains": ["grass", "dirt"],\n  "tiles": [\n'
            + ",\n".join(lines) + "\n  ]\n}\n")
    with open(os.path.join(C, "Sprites/Tileset/NinjaAdventure/TilesetFloor.tileset"), "w", encoding="utf-8") as f:
        f.write(text)


# ── Village ─────────────────────────────────────────────────────────────────

def village(clips_npc, house_atlas, nature_atlas, rain, splash, rain_sound):
    W, H = 40, 26
    floor_png = register("Sprites/Tileset/NinjaAdventure/TilesetFloor.png", False)
    nature_png = register("Sprites/Tileset/NinjaAdventure/TilesetNature.png", False)

    GRASS, DIRT = 0, 1
    ground = [[GRASS] * W for _ in range(H)]

    def dirt(x0, y0, x1, y1):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                ground[y][x] = DIRT

    dirt(1, 16, 38, 17)     # the road across
    dirt(18, 8, 21, 9)      # the yard in front of the door
    dirt(19, 10, 20, 15)    # the path down to the road
    dirt(30, 11, 31, 15)    # the path up to the stone

    # A hedge all the way round - the edge of the world is a colliding layer, not an invisible wall.
    nature_cols = 384 // TS
    rnd = random.Random(7)
    hedge = [[None] * W for _ in range(H)]
    for y in range(H):
        for x in range(W):
            if x in (0, W - 1) or y in (0, H - 1):
                hedge[y][x] = 10 * nature_cols + rnd.choice((0, 1, 2))

    # Flowers and grass tufts on the grass, away from paths and the hedge.
    flowers = [[None] * W for _ in range(H)]
    near_dirt = {(x + dx, y + dy) for y in range(H) for x in range(W) if ground[y][x] == DIRT
                 for dx in (-1, 0, 1) for dy in (-1, 0, 1)}
    blocked = set()
    for x in range(16, 24):          # under the house
        for y in range(4, 9):
            blocked.add((x, y))
    for y in range(2, H - 2):
        for x in range(2, W - 2):
            if (x, y) in near_dirt or (x, y) in blocked or rnd.random() > 0.07:
                continue
            flowers[y][x] = rnd.choice([11 * nature_cols + i for i in range(6)] + [10 * nature_cols + i for i in (3, 4, 5)])

    layers = [
        layer("Ground", autotile(ground, "Sprites/Tileset/NinjaAdventure/TilesetFloor.tileset"), floor_png, W, H),
        layer("Flowers", id_rows(flowers), nature_png, W, H, order=1),
        layer("Hedge", id_rows(hedge), nature_png, W, H, collision=True, order=2),
    ]

    ents = []

    def tree(slice_name, x, y):
        _, _, w, h = NATURE_SLICES[slice_name]
        collider = box(min(w - 20, 40) if w > 32 else 12, 6 if w <= 32 else 10)
        ents.append(entity(slice_name.replace("_", ""), transform(x, y),
                           sprite(nature_atlas, slice_name, NATURE_SLICES[slice_name]), collider))

    # The tree line behind the hedge, then the scattered trees.
    for x in range(40, 640, 80):
        tree("Tree_BigPine", x, 40)
    for y in range(104, 400, 60):
        tree("Tree_Pine", 24, y)
        tree("Tree_Pine", 616, y)
    for x in range(56, 620, 72):
        tree("Tree_Round", x, 412)
    for name, x, y in (("Tree_Big", 104, 136), ("Tree_Round", 200, 104), ("Tree_Pine", 232, 200),
                       ("Tree_Small", 440, 120), ("Tree_Big", 560, 216), ("Stump", 152, 232),
                       ("Tree_Round", 104, 344), ("Tree_Pine", 200, 368), ("Tree_Small", 264, 336),
                       ("Tree_Big", 440, 376), ("Tree_Round", 560, 352)):
        tree(name, x, y)

    ents.append(entity("Rock", transform(352, 344),
                       sprite(nature_atlas, "Rock", NATURE_SLICES["Rock"]), box(20, 8)))

    # The house, placed as a prefab instance. Its door is the only way in: the collider covers the whole front wall.
    # The landing point stays in the scene, where the door check (DoorRefs) reads it without opening prefabs.
    ents.append(entity("Cottage", transform(320, 128), {"type": "sceneInstance", "sceneId": COTTAGE_ID}))
    ents.append(entity("Spawn_FromHouse", transform(320, 148)))
    ents.append(entity("Spawn_Start", transform(320, 232)))

    # The stump and the shrine stone.
    for e in ents:
        if e["name"] == "Stump":
            e["components"].append({"type": "interactable", "prompt": "verb.examine", "kind": "Read",
                                    "block": "village.stump", "range": 14})
    ents.append(entity("ShrineStone", transform(496, 176),
                       sprite(nature_atlas, "Rock", NATURE_SLICES["Rock"]), box(20, 8),
                       {"type": "interactable", "prompt": "verb.touch", "kind": "Event", "event": "village.stone",
                        "range": 14}))

    # The old man by the road.
    npc_idle = clips_npc["Idle_D"][1]
    ents.append(entity("OldMan", transform(392, 248),
                       sprite(npc_idle, "Idle_D_0", (0, 0, TS, TS)),
                       {"type": "animator", "clips": [clips_npc[f"Idle_{d}"][0] for d in DIRS], "defaultClip": "Idle_D"},
                       box(10, 6),
                       {"type": "interactable", "prompt": "verb.talk", "kind": "Read", "block": "village.oldMan",
                        "range": 14},
                       {"type": "talker", "anchorX": 0, "anchorY": 0}))

    # Rain: the drops over the whole map, the splashes on the ground, and the sound. RainEmitter keeps all three
    # in step with GameFlow.Weather.
    ents.append(entity("Rain", transform(380, -8),
                       {"type": "component", "name": "PixelCore.Gameplay.Village.RainEmitter"},
                       {"type": "particleEmitter", "preset": rain, "emitting": False, "rateScale": 1},
                       {"type": "soundEmitter", "soundId": rain_sound, "radius": 2000, "volume": 0.5,
                        "bus": "Ambient", "panStrength": 0, "fadeIn": 1.5, "fadeOut": 1.5}))
    ents.append(entity("RainSplash", transform(320, 208),
                       {"type": "component", "name": "PixelCore.Gameplay.Village.RainEmitter"},
                       {"type": "particleEmitter", "preset": splash, "emitting": False, "rateScale": 1}))

    # Slimes out in the field, away from the start, the old man and the stone.
    for i, (x, y) in enumerate(((136, 296), (248, 280), (520, 304), (584, 152)), start=1):
        ents.append(entity(f"Slime{i}", transform(x, y), {"type": "sceneInstance", "sceneId": SLIME_ID}))

    # The player last, so it is easy to find at the bottom of the hierarchy.
    ents.append(entity("Player", transform(320, 232), {"type": "sceneInstance", "sceneId": HERO_ID}))

    scene("Scenes/Village.scene", VILLAGE_ID, "Village", "Level", ents, layers,
          lightingEnabled=True, exterior=True,
          cameraBoundsEnabled=True, cameraBoundsW=W * TS, cameraBoundsH=H * TS)


# ── House ───────────────────────────────────────────────────────────────────

def house():
    W, H = 12, 9
    floor_png = register("Sprites/Tileset/NinjaAdventure/TilesetInteriorFloor.png", False)
    wall_png = register("Sprites/Tileset/NinjaAdventure/TilesetWallSimple.png", False)
    fc = 352 // TS

    floor = [[None] * W for _ in range(H)]
    for y in range(1, H - 1):
        for x in range(1, W - 1):
            floor[y][x] = fc + 1          # plain boards
    for x in (5, 6):
        floor[H - 1][x] = fc + 1          # the doorway
    # A rug: the bordered pattern's corners and edges around plain boards.
    rx0, ry0, rx1, ry1 = 4, 3, 7, 5
    for y in range(ry0, ry1 + 1):
        for x in range(rx0, rx1 + 1):
            col = 0 if x == rx0 else 2 if x == rx1 else 1
            row = 0 if y == ry0 else 2 if y == ry1 else 1
            floor[y][x] = row * fc + col

    walls = [[None] * W for _ in range(H)]
    for x in range(1, W - 1):
        walls[0][x] = 3
        walls[H - 1][x] = 43
    for y in range(1, H - 1):
        walls[y][0] = 10
        walls[y][W - 1] = 14
    walls[0][0], walls[0][W - 1], walls[H - 1][0], walls[H - 1][W - 1] = 0, 4, 40, 44
    walls[H - 1][4], walls[H - 1][7] = 41, 42     # the pieces either side of the doorway end in caps
    walls[H - 1][5] = walls[H - 1][6] = None

    layers = [
        layer("Floor", id_rows(floor), floor_png, W, H),
        layer("Walls", id_rows(walls), wall_png, W, H, collision=True, order=1),
    ]
    cx, cy = W * TS / 2, H * TS / 2
    ents = [
        entity("Hearth", transform(cx, cy - 8),
               {"type": "light", "r": 255, "g": 206, "b": 150, "radius": 96, "intensity": 1.1, "shape": "Point",
                "rotation": 0, "length": 160, "spreadAngle": 50, "rectW": 96, "rectH": 64, "feather": 0.35,
                "followSun": False, "texScaleX": 1, "texScaleY": 1}),
        entity("DoorStop", transform(96, 152), box(32, 8)),
        entity("ExitDoor", transform(96, 140),
               {"type": "interactable", "prompt": "verb.exit", "kind": "Door", "target": VILLAGE_ID,
                "spawn": "Spawn_FromHouse", "range": 10, "facing": "Down"}),
        entity("Spawn_FromVillage", transform(96, 124)),
        entity("Player", transform(96, 124), {"type": "sceneInstance", "sceneId": HERO_ID}),
    ]
    scene("Scenes/House.scene", HOUSE_ID, "House", "Level", ents, layers,
          lightingEnabled=True, ambientR=120, ambientG=110, ambientB=140,
          cameraFixed=True, cameraFixedX=cx, cameraFixedY=cy)


def main():
    # The registry is rewritten in the engine's own format; refuse to touch it if that format has drifted.
    if dump(dict(sorted(reg.items()))) != reg_text:
        raise SystemExit("Content/assets.json is not in the expected format - boot the editor once to rewrite it")

    derive_images()
    register("Sprites/UI/DialogueUI.png", False)
    register("Sprites/UI/Heart.png", False)
    floor_terrain_sidecar()
    hero = character("Boy", (WALK, IDLE, ATTACK, DEAD))
    npc = character("OldMan")
    hero_prefab(hero)
    slime_prefab(character("Slime", (CRAWL,)))
    fx_prefab("Prefabs/SlashFx.scene", SLASH_FX_ID, "SlashFx",
              fx_clips("Slash", "Sprites/FX/Slash.png", 32, 5, DIRS, 0.05, 0.5), "Slash_D", 500)
    fx_prefab("Prefabs/SmokeFx.scene", SMOKE_FX_ID, "SmokeFx",
              fx_clips("Smoke", "Sprites/FX/Smoke.png", 32, 6, [None], 0.07, 0.85), "Smoke", 0)
    for sound in ("Swing", "Hit", "Hurt", "Pop"):
        register(f"Audio/SFX/Combat/{sound}.wav", False)
    house_atlas, _ = prop_atlas("TilesetHouse", HOUSE_SLICES)
    nature_atlas, _ = prop_atlas("TilesetNature", NATURE_SLICES)
    cottage_prefab(house_atlas, nature_atlas)
    rain, splash = presets()
    rain_sound = register("Audio/AMB/Village/Rain.wav", False)
    register("Audio/BGM/CalmVillage.ogg", False)
    village(npc, house_atlas, nature_atlas, rain, splash, rain_sound)
    house()

    with open(REG_PATH, "w", encoding="utf-8") as f:
        f.write(dump(dict(sorted(reg.items()))))
    print("sample content written")


if __name__ == "__main__":
    main()

# Sample game assets

All art, tiles and audio in the sample game come from the
**[Ninja Adventure asset pack](https://pixel-boy.itch.io/ninja-adventure-asset-pack)** by
[Pixel-boy](https://pixel-boy.itch.io/) and [AAA](https://www.instagram.com/challenger.aaa/),
released under [CC0 1.0](licenses/NinjaAdventure-CC0.txt). Attribution is not required by the license; it is given
here because the authors asked for a link and the pack deserves one.

## Used as is

| File in this repository | From the pack |
|---|---|
| `Content/Sprites/Characters/Boy/Walk.png`, `Idle.png`, `Attack.png`, `Dead.png` | `Actor/Character/Boy/SeparateAnim/` |
| `Content/Sprites/Characters/OldMan/Walk.png`, `Idle.png` | `Actor/Character/OldMan/SeparateAnim/` |
| `Content/Sprites/Characters/Slime/Walk.png` | `Actor/Monster/Slime/Slime.png` |
| `Content/Sprites/FX/Smoke.png` | `FX/Smoke/Smoke/SpriteSheet.png` |
| `Content/Sprites/UI/Heart.png` | `Ui/Receptacle/Heart.png` |
| `Content/Sprites/Tileset/NinjaAdventure/TilesetFloor.png` | `Backgrounds/Tilesets/TilesetFloor.png` |
| `Content/Sprites/Tileset/NinjaAdventure/TilesetNature.png` | `Backgrounds/Tilesets/TilesetNature.png` |
| `Content/Sprites/Tileset/NinjaAdventure/TilesetHouse.png` | `Backgrounds/Tilesets/TilesetHouse.png` |
| `Content/Sprites/Tileset/NinjaAdventure/TilesetInteriorFloor.png` | `Backgrounds/Tilesets/Interior/TilesetInteriorFloor.png` |
| `Content/Sprites/Tileset/NinjaAdventure/TilesetWallSimple.png` | `Backgrounds/Tilesets/Interior/TilesetWallSimple.png` |
| `Content/Audio/AMB/Village/Rain.wav` | `Audio/Sounds/Ambient/Rain.wav` |
| `Content/Audio/BGM/CalmVillage.ogg` | `Audio/Musics/33 - Calm Village.ogg` |
| `Content/Audio/SFX/Combat/Swing.wav` | `Audio/Sounds/Whoosh & Slash/Slash.wav` |
| `Content/Audio/SFX/Combat/Hit.wav` | `Audio/Sounds/Hit & Impact/Hit1.wav` |
| `Content/Audio/SFX/Combat/Hurt.wav` | `Audio/Sounds/Hit & Impact/Impact.wav` |
| `Content/Audio/SFX/Combat/Pop.wav` | `Audio/Sounds/Hit & Impact/Hit5.wav` |

## Derived

| File | How it was made |
|---|---|
| `Content/Sprites/FX/RainDrop.png` | The second frame of `FX/Particle/Rain.png` |
| `Content/Sprites/FX/RainSplash.png` | The second frame of `FX/Particle/RainOnFloor.png` |
| `Content/Sprites/FX/Slash.png` | The five frames of `FX/Slash/SpriteSheetSlash01.png`, centred in 32px cells and turned for each direction |
| `Icon.bmp`, `Icon.ico` | The Boy idle frame, scaled up on a coloured plate |
| `Content/Sprites/Tileset/NinjaAdventure/TilesetFloor.tileset` | Grass and dirt edge rules for the terrain brush, read from the tile pixels |

Everything generated from these files (atlases, animation clips, prefabs, scenes, particle presets and the terrain
rules) is produced by [`tools/build_sample_content.py`](tools/build_sample_content.py).
The dialogue bubble frame (`Content/Sprites/UI/DialogueUI.png`) is drawn by the same script.

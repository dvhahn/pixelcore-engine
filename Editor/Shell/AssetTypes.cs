using System.Numerics;

namespace PixelCore.Editor;

public enum AssetKind
{
    Scene,
    Prefab,
    Sprite,
    Animation,
    Audio,
    Font,
    Data,
    Story,
    Surface,
    Post,
    Sky,
    Strings,
    Shader,
    Doc,
    Other
}

public sealed class AssetEntry
{
    public string Name = "";
    public string FullPath = "";
    public string RelPath = "";
    public string RelDir = "";
    public string Extension = "";
    public AssetKind Kind;
}

public static class AssetKinds
{
    public static AssetKind Classify(string ext) => ext switch
    {
        ".scene" => AssetKind.Scene,
        ".png" or ".jpg" or ".jpeg" or ".bmp" => AssetKind.Sprite,
        ".anim" => AssetKind.Animation,
        ".wav" or ".ogg" or ".mp3" => AssetKind.Audio,
        ".ttf" or ".otf" => AssetKind.Font,
        ".json" => AssetKind.Data,
        ".story" => AssetKind.Story,
        ".surface" => AssetKind.Surface,
        ".post" => AssetKind.Post,
        ".sky" => AssetKind.Sky,
        ".strings" => AssetKind.Strings,
        ".fx" or ".fxb" => AssetKind.Shader,
        ".md" => AssetKind.Doc,
        _ => AssetKind.Other
    };

    public static string Label(AssetKind k) => k switch
    {
        AssetKind.Scene => "ROOMS",
        AssetKind.Prefab => "PREFABS",
        AssetKind.Sprite => "SPRITES",
        AssetKind.Animation => "ANIMS",
        AssetKind.Audio => "AUDIO",
        AssetKind.Font => "FONTS",
        AssetKind.Data => "DATA",
        AssetKind.Story => "STORY",
        AssetKind.Surface => "SURFACES",
        AssetKind.Post => "POST",
        AssetKind.Sky => "SKY",
        AssetKind.Strings => "STRINGS",
        AssetKind.Shader => "SHADERS",
        AssetKind.Doc => "DOCS",
        _ => "OTHER"
    };

    public static string Icon(AssetKind k) => k switch
    {
        AssetKind.Scene => Icons.Cube,
        AssetKind.Prefab => Icons.Cubes,
        AssetKind.Sprite => Icons.Image,
        AssetKind.Animation => Icons.Film,
        AssetKind.Audio => Icons.Play,
        AssetKind.Font => Icons.File,
        AssetKind.Data => Icons.File,
        AssetKind.Story => Icons.Chat,
        AssetKind.Surface => Icons.Footprints,
        AssetKind.Post => Icons.Palette,
        AssetKind.Sky => Icons.Sun,
        AssetKind.Strings => Icons.Translate,
        AssetKind.Shader => Icons.Code,
        AssetKind.Doc => Icons.FileText,
        _ => Icons.File
    };

    public static Vector4 Color(AssetKind k)
        => EditorTheme.Mode == ThemeMode.Light ? LightColor(k) : DarkColor(k);

    private static Vector4 DarkColor(AssetKind k) => k switch
    {
        AssetKind.Scene => new Vector4(0.42f, 0.65f, 0.88f, 1f),
        AssetKind.Prefab => new Vector4(0.45f, 0.80f, 0.62f, 1f),
        AssetKind.Sprite => new Vector4(0.79f, 0.56f, 0.80f, 1f),
        AssetKind.Animation => new Vector4(0.89f, 0.64f, 0.36f, 1f),
        AssetKind.Audio => new Vector4(0.85f, 0.47f, 0.54f, 1f),
        AssetKind.Font => new Vector4(0.60f, 0.62f, 0.68f, 1f),
        AssetKind.Data => new Vector4(0.56f, 0.82f, 0.78f, 1f),
        AssetKind.Story => new Vector4(0.50f, 0.72f, 0.91f, 1f),
        AssetKind.Surface => new Vector4(0.62f, 0.84f, 0.71f, 1f),
        AssetKind.Post => new Vector4(0.79f, 0.65f, 0.84f, 1f),
        AssetKind.Sky => new Vector4(0.55f, 0.80f, 0.90f, 1f),
        AssetKind.Strings => new Vector4(0.88f, 0.79f, 0.54f, 1f),
        AssetKind.Shader => new Vector4(0.56f, 0.82f, 0.78f, 1f),
        AssetKind.Doc => new Vector4(0.60f, 0.62f, 0.68f, 1f),
        _ => new Vector4(0.55f, 0.55f, 0.58f, 1f)
    };

    private static Vector4 LightColor(AssetKind k) => k switch
    {
        AssetKind.Scene => new Vector4(0.142f, 0.445f, 0.748f, 1f),
        AssetKind.Prefab => new Vector4(0.125f, 0.498f, 0.305f, 1f),
        AssetKind.Sprite => new Vector4(0.696f, 0.237f, 0.712f, 1f),
        AssetKind.Animation => new Vector4(0.628f, 0.379f, 0.094f, 1f),
        AssetKind.Audio => new Vector4(0.838f, 0.148f, 0.274f, 1f),
        AssetKind.Font => new Vector4(0.380f, 0.433f, 0.594f, 1f),
        AssetKind.Data => new Vector4(0.154f, 0.487f, 0.437f, 1f),
        AssetKind.Story => new Vector4(0.287f, 0.454f, 0.583f, 1f),
        AssetKind.Surface => new Vector4(0.270f, 0.480f, 0.358f, 1f),
        AssetKind.Post => new Vector4(0.578f, 0.344f, 0.662f, 1f),
        AssetKind.Sky => new Vector4(0.122f, 0.435f, 0.541f, 1f),
        AssetKind.Strings => new Vector4(0.475f, 0.443f, 0.024f, 1f),
        AssetKind.Shader => new Vector4(0.154f, 0.487f, 0.437f, 1f),
        AssetKind.Doc => new Vector4(0.380f, 0.433f, 0.594f, 1f),
        _ => new Vector4(0.435f, 0.435f, 0.462f, 1f)
    };

    public static bool Monochrome;

    private static Vector4 Neutral => EditorTheme.Mode == ThemeMode.Light
        ? new Vector4(0.435f, 0.435f, 0.462f, 1f)
        : new Vector4(0.62f, 0.63f, 0.66f, 1f);

    public static Vector4 IconColor(AssetKind k) => Monochrome ? Neutral : Color(k);
}

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PixelCore.Runtime.Rendering;

public sealed class SkyRenderer : IDisposable
{
    public const string ShaderPath = "Content/Shaders/Sky.fxb";

    private Effect? _effect;
    private bool _effectTried;
    private bool _warnedMissing;

    private Texture2D? _noise;
    private int _noiseSeed = int.MinValue;
    private Texture2D? _white;

    private float _time;

    public int NoiseSize { get; set; } = SkyNoise.DefaultSize;

    public float Time => _time;

    public void Update(float dt) => _time += dt;

    public static float SteppedTime(float time, float tickHz)
        => tickHz > 0f ? MathF.Floor(time * tickHz) / tickHz : time;

    public static Vector2 DirFromDegrees(float degrees)
    {
        float r = MathHelper.ToRadians(degrees);
        return new Vector2(MathF.Cos(r), -MathF.Sin(r));
    }

    public static Vector4 Layer(SkyProfile p, int i, float steppedTime)
    {
        float f = MathF.Pow(MathF.Max(p.LayerFalloff, 0.05f), i);
        float scale = MathF.Max(8f, p.NearScale * f);
        float speed = p.WindSpeed * f;
        var w = DirFromDegrees(p.WindAngle);
        float ox = MathF.Floor(-w.X * speed * steppedTime + i * 37f);
        float oy = MathF.Floor(-w.Y * speed * steppedTime + i * 59f);
        float morph = MathF.Floor(p.Morph * speed * steppedTime);
        return new Vector4(scale, ox, oy, morph);
    }

    public static Vector2 ParallaxOrigin(SkyProfile p, Vector2 cameraPos)
        => new(MathF.Floor(cameraPos.X * p.Parallax), MathF.Floor(cameraPos.Y * p.Parallax));

    public void Draw(GraphicsDevice gd, SpriteBatch sb, SkyProfile p, int rtW, int rtH, float artScale, Vector2 cameraPos)
    {
        if (rtW <= 0 || rtH <= 0) return;
        EnsureEffect(gd);
        EnsureNoise(gd, p.Seed);

        if (_effect == null || _noise == null)
        {
            DrawFallback(gd, sb, p, rtW, rtH);
            return;
        }

        float st = SteppedTime(_time, p.TickHz);
        var fx = _effect;
        fx.Parameters["MatrixTransform"]?.SetValue(Matrix.CreateOrthographicOffCenter(0, rtW, rtH, 0, 0, 1));
        fx.Parameters["RtSize"]?.SetValue(new Vector2(rtW, rtH));
        fx.Parameters["ArtScale"]?.SetValue(MathF.Max(artScale, 0.01f));
        fx.Parameters["Origin"]?.SetValue(ParallaxOrigin(p, cameraPos));

        fx.Parameters["SkyTop"]?.SetValue(p.SkyTop.ToVector4());
        fx.Parameters["SkyBottom"]?.SetValue(p.SkyBottom.ToVector4());
        fx.Parameters["SkyBands"]?.SetValue((float)(p.SkyBands <= 0 ? 0 : Math.Clamp(p.SkyBands, 2, 64)));
        fx.Parameters["Dither"]?.SetValue(p.Dither ? 1f : 0f);

        fx.Parameters["CloudLight"]?.SetValue(p.CloudLight.ToVector4());
        fx.Parameters["CloudBody"]?.SetValue(p.CloudBody.ToVector4());
        fx.Parameters["CloudShadow"]?.SetValue(p.CloudShadow.ToVector4());

        fx.Parameters["SunDir"]?.SetValue(DirFromDegrees(p.LightAngle));
        fx.Parameters["Relief"]?.SetValue(MathF.Max(p.Relief, 0f));
        fx.Parameters["Stretch"]?.SetValue(MathF.Max(p.Stretch, 0.25f));
        fx.Parameters["Smooth"]?.SetValue(Math.Clamp(p.Smooth, 0f, 1f));
        fx.Parameters["FlatBase"]?.SetValue(Math.Clamp(p.FlatBase, 0f, 1f));
        fx.Parameters["RowHeight"]?.SetValue(MathF.Max(p.RowHeight, 8f));
        fx.Parameters["Lumpy"]?.SetValue(Math.Clamp(p.Lumpy, 0f, 1f));
        fx.Parameters["Shape"]?.SetValue((float)Math.Clamp(p.Shape, 0, 1));
        fx.Parameters["Fluff"]?.SetValue(MathF.Max(p.Fluff, 0f));
        fx.Parameters["Shade"]?.SetValue((float)Math.Clamp(p.Shade, 0, 2));
        fx.Parameters["ShadowDepth"]?.SetValue(MathF.Max(p.ShadowDepth, 1f));
        fx.Parameters["Coverage"]?.SetValue(Math.Clamp(p.Coverage, 0f, 1f));
        fx.Parameters["Detail"]?.SetValue(Math.Clamp(p.Detail, 0f, 1f));
        fx.Parameters["Haze"]?.SetValue(Math.Clamp(p.Haze, 0f, 1f));
        fx.Parameters["LayerCount"]?.SetValue((float)Math.Clamp(p.Layers, 1, 3));
        fx.Parameters["LayerA"]?.SetValue(Layer(p, 0, st));
        fx.Parameters["LayerB"]?.SetValue(Layer(p, 1, st));
        fx.Parameters["LayerC"]?.SetValue(Layer(p, 2, st));

        sb.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.LinearWrap,
            DepthStencilState.None, RasterizerState.CullNone, fx);
        sb.Draw(_noise, new Rectangle(0, 0, rtW, rtH), Color.White);
        sb.End();
    }

    private void DrawFallback(GraphicsDevice gd, SpriteBatch sb, SkyProfile p, int rtW, int rtH)
    {
        if (!_warnedMissing)
        {
            _warnedMissing = true;
            Console.Error.WriteLine($"[Sky] {ShaderPath} missing or failed to load - drawing the sky as a solid colour. Run scripts/compile-shaders.sh");
        }
        if (_white == null)
        {
            _white = new Texture2D(gd, 1, 1);
            _white.SetData(new[] { Color.White });
        }
        sb.Begin(SpriteSortMode.Deferred, BlendState.Opaque);
        sb.Draw(_white, new Rectangle(0, 0, rtW, rtH), p.SkyBottom);
        sb.End();
    }

    private void EnsureEffect(GraphicsDevice gd)
    {
        if (_effectTried) return;
        _effectTried = true;
        if (!System.IO.File.Exists(ShaderPath)) return;
        try
        {
            _effect = new Effect(gd, System.IO.File.ReadAllBytes(ShaderPath));
            Console.WriteLine("[Sky] Sky.fxb loaded - procedural sky active");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Sky] load failed: {ex.Message}");
        }
    }

    private void EnsureNoise(GraphicsDevice gd, int seed)
    {
        if (_noise != null && _noiseSeed == seed && _noise.Width == NoiseSize) return;
        _noise?.Dispose();
        int size = Math.Clamp(NoiseSize, 32, 1024);
        var px = SkyNoise.Bake(size, seed);
        _noise = new Texture2D(gd, size, size, false, SurfaceFormat.Color);
        _noise.SetData(px);
        _noiseSeed = seed;
    }

    public void Dispose()
    {
        _effect?.Dispose(); _effect = null;
        _noise?.Dispose(); _noise = null;
        _white?.Dispose(); _white = null;
    }
}

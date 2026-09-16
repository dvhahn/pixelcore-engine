using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PixelCore.Runtime.Rendering;

public class PostProcessor : IDisposable
{
    private Effect? _compose;
    private Effect? _bloom;
    private bool _loadTried;

    private RenderTarget2D? _source;
    private readonly RenderTarget2D?[] _bloomChain = new RenderTarget2D?[4];
    private int _bloomW, _bloomH;

    private Texture2D? _lutBakedA, _lutBakedB;
    private int _lutHashA, _lutHashB;
    private readonly Color[] _lutScratch = new Color[LutBaker.Size * LutBaker.Size * LutBaker.Size];

    private Texture2D? _lutBoundA, _lutBoundB;
    private float _lutBlend;
    private float _lutAmount;

    private Texture2D? _pixel;
    private Texture2D? _vignetteTexture;
    private const int VignetteSize = 256;

    public bool ShaderReady => _compose != null;

    private static readonly BlendState Multiply = new()
    {
        ColorSourceBlend = Blend.DestinationColor,
        ColorDestinationBlend = Blend.Zero,
        ColorBlendFunction = BlendFunction.Add,
        AlphaSourceBlend = Blend.DestinationAlpha,
        AlphaDestinationBlend = Blend.Zero,
        AlphaBlendFunction = BlendFunction.Add,
    };

    public bool UseShaderPath(GraphicsDevice gd, PostProfile p)
        => NeedsPost(p, FxStack.Current) && EnsureEffects(gd);

    public static bool NeedsPost(PostProfile p, FxLayer fx) => !p.IsNeutral || !fx.IsNeutral;

    public const float GrainHz = 8f;

    public static Vector2 GrainCellsFor(Rectangle dest, float grainCells)
    {
        float cx = MathF.Max(grainCells, 1f);
        float w = MathF.Max(dest.Width, 1), h = MathF.Max(dest.Height, 1);
        return new Vector2(cx, MathF.Max(cx * (h / w), 1f));
    }

    public static float GrainSeedFor(float wallSeconds)
        => MathF.Floor(wallSeconds * GrainHz) / GrainHz % 64f;

    private bool EnsureEffects(GraphicsDevice gd)
    {
        if (_loadTried) return _compose != null;
        _loadTried = true;
        _gd = gd;
        try
        {
            var dir = System.IO.Path.Combine("Content", "Shaders");
            var composePath = System.IO.Path.Combine(dir, "PostCompose.fxb");
            var bloomPath = System.IO.Path.Combine(dir, "Bloom.fxb");
            if (System.IO.File.Exists(composePath))
            {
                _compose = new Effect(gd, System.IO.File.ReadAllBytes(composePath));
                if (System.IO.File.Exists(bloomPath))
                    _bloom = new Effect(gd, System.IO.File.ReadAllBytes(bloomPath));
                Console.WriteLine("[Post] PostCompose.fxb loaded - shader post path active");
            }
            else
            {
                Console.WriteLine("[Post] PostCompose.fxb missing - falling back to the blend path. Run scripts/compile-shaders.sh");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Post] shader load failed - using the fallback path: {e.Message}");
            _compose = null;
        }
        return _compose != null;
    }

    private GraphicsDevice? _gd;

    public RenderTarget2D GetSourceTarget(GraphicsDevice gd, int width, int height)
    {
        _gd = gd;
        if (_source == null || _source.Width != width || _source.Height != height)
        {
            _source?.Dispose();
            _source = new RenderTarget2D(gd, width, height);
        }
        return _source;
    }

    public void Prepare(GraphicsDevice gd, SpriteBatch sb, PostProfile p, RenderTarget2D source,
        PostProfile? blendFrom = null, PostProfile? blendTo = null, float blend = 0f)
    {
        if (!EnsureEffects(gd)) return;

        EnsurePixel(gd);

        bool crossfade = blendFrom != null && blendTo != null;
        var a = crossfade ? blendFrom! : p;
        var b = crossfade ? blendTo! : p;

        _lutBoundA = ResolveLut(gd, a, ref _lutBakedA, ref _lutHashA);
        _lutBoundB = crossfade ? ResolveLut(gd, b, ref _lutBakedB, ref _lutHashB) : _lutBoundA;
        _lutBlend = crossfade ? MathHelper.Clamp(blend, 0f, 1f) : 0f;
        _lutAmount = (a.HasColorGrading || b.HasColorGrading) ? 1f : 0f;

        if (p.HasBloom && _bloom != null)
            RenderBloomChain(gd, sb, p, source);
    }

    private Texture2D ResolveLut(GraphicsDevice gd, PostProfile p, ref Texture2D? baked, ref int hash)
        => LoadLutTexture(p.LutTextureId) ?? LutBaker.Ensure(gd, p, ref baked, ref hash, _lutScratch);

    private static readonly System.Collections.Generic.HashSet<string> _lutWarned = new();

    private Texture2D? LoadLutTexture(string? id)
    {
        if (string.IsNullOrEmpty(id) || _gd == null) return null;

        var rel = Assets.AssetRegistry.Instance.GetPath(id);
        if (string.IsNullOrEmpty(rel))
        {
            if (_lutWarned.Add(id))
                Console.Error.WriteLine($"[Post] LUT id {id} not found in the registry - using the baked LUT");
            return null;
        }

        var tex = Assets.TextureLoader.Instance.Load(rel);
        if (tex == null)
        {
            if (_lutWarned.Add(id))
                Console.Error.WriteLine($"[Post] could not read LUT '{rel}' - using the baked LUT");
            return null;
        }

        if (tex.Width != LutBaker.Size * LutBaker.Size || tex.Height != LutBaker.Size)
        {
            if (_lutWarned.Add(id))
                Console.Error.WriteLine(
                    $"[Post] LUT '{rel}' is {tex.Width}×{tex.Height} - " +
                    $"it must be a {LutBaker.Size * LutBaker.Size}×{LutBaker.Size} strip " +
                    "(x = blue slice*32 + red, y = green). Using the baked LUT");
            return null;
        }
        return tex;
    }

    public static void ResetLutWarnings() => _lutWarned.Clear();

    public void Compose(SpriteBatch sb, PostProfile p, RenderTarget2D source,
        Rectangle dest, float timeSeconds, RasterizerState? rasterizer = null)
    {
        if (_compose == null) return;

        float srcW = source.Width, srcH = source.Height;
        var fx = _compose;

        fx.Parameters["MatrixTransform"]?.SetValue(
            Matrix.CreateOrthographicOffCenter(0, srcW, srcH, 0, 0, 1));
        fx.Parameters["DestOrigin"]?.SetValue(new Vector2(dest.X / srcW, dest.Y / srcH));
        fx.Parameters["DestSize"]?.SetValue(new Vector2(
            MathF.Max(dest.Width, 1) / srcW, MathF.Max(dest.Height, 1) / srcH));

        fx.Parameters["LutTexture"]?.SetValue(_lutBoundA);
        fx.Parameters["LutTextureB"]?.SetValue(_lutBoundB ?? _lutBoundA);
        fx.Parameters["LutSize"]?.SetValue((float)LutBaker.Size);
        fx.Parameters["LutAmount"]?.SetValue(_lutAmount);
        fx.Parameters["LutBlend"]?.SetValue(_lutBlend);

        float aspect = dest.Height > 0 ? dest.Width / (float)dest.Height : 1f;
        fx.Parameters["VignetteColor"]?.SetValue(p.VignetteColor.ToVector4());
        fx.Parameters["VignetteParams"]?.SetValue(new Vector4(
            p.Vignette * 3f,
            MathF.Max(p.VignetteSmooth, 0.01f) * 5f,
            p.VignetteRounded ? aspect : 1f,
            p.Vignette > 0f ? 1f : 0f));

        var stack = FxStack.Current;

        var fog = stack.FogColor.ToVector4();
        fog.W = MathHelper.Clamp(stack.FogOpacity, 0f, 1f);
        fx.Parameters["FogColor"]?.SetValue(fog);

        var cells = GrainCellsFor(dest, p.GrainCells);
        fx.Parameters["GrainParams"]?.SetValue(new Vector4(p.GrainIntensity, 0f, cells.X, cells.Y));
        fx.Parameters["GrainSeed"]?.SetValue(p.GrainIntensity > 0f ? GrainSeedFor(timeSeconds) : 0f);

        fx.Parameters["DestPixels"]?.SetValue(new Vector2(
            MathF.Max(dest.Width, 1), MathF.Max(dest.Height, 1)));

        fx.Parameters["ChromAb"]?.SetValue(stack.ChromAb);
        fx.Parameters["Distortion"]?.SetValue(stack.LensDistortion);

        bool bloomOn = p.HasBloom && _bloomChain[0] != null;
        fx.Parameters["BloomTexture"]?.SetValue(bloomOn ? _bloomChain[0] : _pixel);
        fx.Parameters["BloomIntensity"]?.SetValue(bloomOn ? p.BloomIntensity : 0f);
        fx.Parameters["BloomTint"]?.SetValue(p.BloomTint.ToVector4());

        sb.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp, null, rasterizer, fx);
        sb.Draw(source, new Rectangle(0, 0, (int)srcW, (int)srcH), Color.White);
        sb.End();
    }

    private void RenderBloomChain(GraphicsDevice gd, SpriteBatch sb, PostProfile p, RenderTarget2D source)
    {
        int w = Math.Max(source.Width / 2, 1);
        int h = Math.Max(source.Height / 2, 1);
        if (_bloomW != w || _bloomH != h)
        {
            for (int i = 0; i < _bloomChain.Length; i++)
            {
                _bloomChain[i]?.Dispose();
                int lw = Math.Max(w >> i, 4), lh = Math.Max(h >> i, 4);
                _bloomChain[i] = new RenderTarget2D(gd, lw, lh, false,
                    SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            }
            _bloomW = w; _bloomH = h;
        }

        float threshold = MathF.Min(p.BloomThreshold, 0.95f);
        var c0 = _bloomChain[0]!;
        _bloom!.Parameters["MatrixTransform"]?.SetValue(
            Matrix.CreateOrthographicOffCenter(0, c0.Width, c0.Height, 0, 0, 1));
        _bloom.Parameters["Threshold"]?.SetValue(threshold);
        _bloom.Parameters["Knee"]?.SetValue(threshold * 0.5f + 1e-4f);

        gd.SetRenderTarget(c0);
        sb.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp, null, null, _bloom);
        sb.Draw(source, new Rectangle(0, 0, c0.Width, c0.Height), Color.White);
        sb.End();

        for (int i = 1; i < _bloomChain.Length; i++)
        {
            var dst = _bloomChain[i]!;
            gd.SetRenderTarget(dst);
            sb.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp, null, null);
            sb.Draw(_bloomChain[i - 1], new Rectangle(0, 0, dst.Width, dst.Height), Color.White);
            sb.End();
        }

        float scatter = MathHelper.Clamp(p.BloomScatter, 0f, 1f);
        for (int i = _bloomChain.Length - 2; i >= 0; i--)
        {
            var dst = _bloomChain[i]!;
            gd.SetRenderTarget(dst);
            sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null);
            sb.Draw(_bloomChain[i + 1], new Rectangle(0, 0, dst.Width, dst.Height),
                new Color(1f, 1f, 1f, scatter));
            sb.End();
        }

        gd.SetRenderTarget(null);
    }

    public void ApplyFallback(GraphicsDevice gd, SpriteBatch sb, PostProfile profile,
        Rectangle dest, RasterizerState? rasterizer = null)
    {
        if (profile.IsNeutral && FxStack.Current.IsNeutral) return;

        _gd = gd;
        EnsurePixel(gd);
        _vignetteTexture ??= CreateVignetteTexture(gd);

        if (profile.ColorFilter != Color.White)
        {
            sb.Begin(SpriteSortMode.Deferred, Multiply, SamplerState.PointClamp, null, rasterizer);
            sb.Draw(_pixel, dest, profile.ColorFilter);
            sb.End();
        }

        var stack = FxStack.Current;
        if (stack.FogOpacity > 0f || profile.Vignette > 0f)
        {
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, null, rasterizer);
            if (stack.FogOpacity > 0f)
                sb.Draw(_pixel, dest, stack.FogColor * MathHelper.Clamp(stack.FogOpacity, 0f, 1f));
            if (profile.Vignette > 0f)
                sb.Draw(_vignetteTexture, dest, Color.White * MathHelper.Clamp(profile.Vignette, 0f, 1f));
            sb.End();
        }
    }

    private void EnsurePixel(GraphicsDevice gd)
    {
        if (_pixel == null)
        {
            _pixel = new Texture2D(gd, 1, 1);
            _pixel.SetData(new[] { Color.Black });
        }
    }

    private static Texture2D CreateVignetteTexture(GraphicsDevice gd)
    {
        var tex = new Texture2D(gd, VignetteSize, VignetteSize);
        var pixels = new Color[VignetteSize * VignetteSize];
        float half = VignetteSize / 2f;

        for (int y = 0; y < VignetteSize; y++)
            for (int x = 0; x < VignetteSize; x++)
            {
                float dx = (x + 0.5f - half) / half;
                float dy = (y + 0.5f - half) / half;
                float d = MathF.Sqrt(dx * dx + dy * dy) / 1.4142f;
                float s = MathHelper.Clamp((d - 0.55f) / 0.45f, 0f, 1f);
                float a = s * s * (3f - 2f * s);
                a *= a;
                pixels[y * VignetteSize + x] = new Color(0f, 0f, 0f, a);
            }

        tex.SetData(pixels);
        return tex;
    }

    public void Dispose()
    {
        _compose?.Dispose();
        _bloom?.Dispose();
        _source?.Dispose();
        foreach (var rt in _bloomChain) rt?.Dispose();
        _lutBakedA?.Dispose();
        _lutBakedB?.Dispose();
        _pixel?.Dispose();
        _vignetteTexture?.Dispose();
    }
}

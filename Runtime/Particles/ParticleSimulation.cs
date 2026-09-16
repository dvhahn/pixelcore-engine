using System;
using Microsoft.Xna.Framework;

namespace PixelCore.Runtime.Particles;

public struct ParticleState
{
    public Vector2 Position;

    public Vector2 Velocity;

    public float Age;

    public float Lifetime;

    public float Rotation;
    public float RotationSpeed;

    public float SwayPhase;

    public float ScaleSeed;

    public float AlphaSeed;

    public float SwayOffset;

    public float NoiseSeed;

    public Vector2 NoiseOffset;

    public Vector2 NoiseVelocity;

    public readonly float Delta => Lifetime <= 0f ? 1f : Math.Clamp(Age / Lifetime, 0f, 1f);

    public readonly Vector2 RenderPosition =>
        new(Position.X + SwayOffset + NoiseOffset.X, Position.Y + NoiseOffset.Y);
}

public sealed class ParticleSimulation
{
    private ParticleState[] _pool = Array.Empty<ParticleState>();
    private int _alive;
    private float _spawnAccumulator;
    private Random _rng;

    private float _noiseTime;
    private Vector2 _lastOrigin;
    private bool _hasLastOrigin;

    public int PoolAllocations { get; private set; }

    public int AliveCount => _alive;

    public int Capacity => _pool.Length;

    public ReadOnlySpan<ParticleState> Alive => _pool.AsSpan(0, _alive);

    public ParticleSimulation(int? seed = null)
        => _rng = seed.HasValue ? new Random(seed.Value) : new Random();

    public void Reseed(int seed)
    {
        _rng = new Random(seed);
        Clear();
    }

    public void Configure(int maxParticles)
    {
        int want = Math.Max(0, maxParticles);
        if (_pool.Length >= want) return;

        var grown = new ParticleState[want];
        Array.Copy(_pool, grown, _alive);
        _pool = grown;
        PoolAllocations++;
    }

    public void Clear()
    {
        _alive = 0;
        _spawnAccumulator = 0f;
            _hasLastOrigin = false;
        _noiseTime = 0f;
    }

    private void FollowOrigin(Vector2 origin, bool follow)
    {
        if (follow && _hasLastOrigin)
        {
            var delta = origin - _lastOrigin;
            if (delta != Vector2.Zero)
                for (int i = 0; i < _alive; i++) _pool[i].Position += delta;
        }
        _lastOrigin = origin;
        _hasLastOrigin = true;
    }

    public int Prewarm(Vector2 origin, in ParticlePreset preset, bool emitting, float rateScale = 1f)
    {
        float seconds = MathF.Min(preset.PrewarmSeconds, ParticlePreset.MaxPrewarmSeconds);
        if (seconds <= 0f) return 0;

        const float step = 1f / 30f;
        int steps = (int)(seconds / step);
        for (int i = 0; i < steps; i++)
            Update(step, origin, preset, emitting, rateScale);
        return steps;
    }

    public void Update(float dt, Vector2 origin, in ParticlePreset preset, bool emitting, float rateScale = 1f)
    {
        Configure(preset.MaxParticles);
        FollowOrigin(origin, preset.FollowEmitter);

        if (emitting && preset.Rate > 0f && rateScale > 0f && dt > 0f)
        {
            _spawnAccumulator += preset.Rate * rateScale * dt;
            while (_spawnAccumulator >= 1f)
            {
                _spawnAccumulator -= 1f;
                if (!TrySpawn(origin, preset)) { _spawnAccumulator = 0f; break; }
            }
        }

        Step(dt, preset);
        Reap();
    }

    public int Burst(Vector2 origin, in ParticlePreset preset)
    {
        int min = Math.Max(0, preset.BurstMin);
        int max = Math.Max(min, preset.BurstMax);
        int count = min == max ? min : _rng.Next(min, max + 1);
        return Emit(count, origin, preset);
    }

    public int Emit(int count, Vector2 origin, in ParticlePreset preset)
    {
        Configure(preset.MaxParticles);

        int made = 0;
        for (int i = 0; i < count; i++)
        {
            if (!TrySpawn(origin, preset)) break;
            made++;
        }
        return made;
    }

    private bool TrySpawn(Vector2 origin, in ParticlePreset preset)
    {
        if (_alive >= _pool.Length) return false;

        float speed = preset.Speed.SampleAtSpawn(_rng);
        float half = preset.Spread * 0.5f;
        float angleDeg = preset.Direction + ((float)_rng.NextDouble() * 2f - 1f) * half;
        float angleRad = angleDeg * (MathF.PI / 180f);

        ref var p = ref _pool[_alive];
        p.Position = origin + preset.Shape.RandomPointIn(_rng);
        p.Velocity = new Vector2(MathF.Cos(angleRad) * speed, MathF.Sin(angleRad) * speed);
        p.Age = 0f;
        p.Lifetime = preset.Lifetime.SampleAtSpawn(_rng);
        p.Rotation = preset.Rotation.SampleAtSpawn(_rng);
        p.RotationSpeed = preset.RotationSpeed.SampleAtSpawn(_rng);
        p.SwayPhase = (float)_rng.NextDouble() * MathF.PI * 2f;
        p.SwayOffset = 0f;
        p.NoiseSeed = (float)_rng.NextDouble() * 1024f;
        p.NoiseOffset = Vector2.Zero;
        p.NoiseVelocity = Vector2.Zero;
        p.ScaleSeed = (float)_rng.NextDouble();
        p.AlphaSeed = (float)_rng.NextDouble();

        _alive++;
        return true;
    }

    private void Step(float dt, in ParticlePreset preset)
    {
        if (dt <= 0f) return;

        var gravity = new Vector2(preset.GravityX, preset.GravityY);
        float swayAmp = preset.SwayAmplitude;
        float swayW = preset.SwayFrequency * MathF.PI * 2f;

        float noiseAmp = preset.NoiseStrength;
        float noiseFreq = preset.NoiseFrequency;
        int noiseOct = Math.Clamp(preset.NoiseOctaves, 1, 4);
        _noiseTime += preset.NoiseScroll * dt;

        bool velocityNoise = preset.NoiseMode == NoiseMode.Velocity;
        float follow = velocityNoise && preset.NoiseResponse > 1e-4f
            ? MathF.Exp(-dt / preset.NoiseResponse)
            : 0f;

        for (int i = 0; i < _alive; i++)
        {
            ref var p = ref _pool[i];

            p.Age += dt;
            p.Velocity += gravity * dt;
            p.Position += p.Velocity * dt;
            p.Rotation += p.RotationSpeed * dt;

            p.SwayPhase += swayW * dt;
            p.SwayOffset = swayAmp == 0f ? 0f : MathF.Sin(p.SwayPhase) * swayAmp;

            if (noiseAmp == 0f)
            {
                p.NoiseOffset = Vector2.Zero;
                p.NoiseVelocity = Vector2.Zero;
            }
            else if (velocityNoise)
            {
                var target = Curl(p.Position + p.NoiseOffset, noiseFreq, _noiseTime, noiseOct) * noiseAmp;
                p.NoiseVelocity = target + (p.NoiseVelocity - target) * follow;
                p.NoiseOffset += p.NoiseVelocity * dt;
            }
            else
            {
                p.NoiseOffset = new Vector2(
                    Perlin(p.NoiseSeed + _noiseTime, 0.5f, noiseFreq, noiseOct) * noiseAmp,
                    Perlin(p.NoiseSeed + _noiseTime, 37.5f, noiseFreq, noiseOct) * noiseAmp);
            }
        }
    }

    private void Reap()
    {
        for (int i = _alive - 1; i >= 0; i--)
        {
            if (_pool[i].Age < _pool[i].Lifetime) continue;
            _pool[i] = _pool[_alive - 1];
            _alive--;
        }
    }

    private static float Hash(int x, int y)
    {
        int h = x * 374761393 + y * 668265263;
        h = (h ^ (h >> 13)) * 1274126177;
        h ^= h >> 16;
        return (h & 0xFFFF) / 32767.5f - 1f;
    }

    private static float Fade(float t) => t * t * (3f - 2f * t);

    private static float Octave(float x, float y)
    {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
        float fx = Fade(x - xi), fy = Fade(y - yi);
        float a = Hash(xi, yi), b = Hash(xi + 1, yi);
        float c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
        return MathHelper.Lerp(MathHelper.Lerp(a, b, fx), MathHelper.Lerp(c, d, fx), fy);
    }

    internal static Vector2 Curl(Vector2 worldPos, float frequency, float scroll, int octaves)
    {
        const float eps = 0.5f;
        float u = worldPos.X * frequency + scroll;
        float v = worldPos.Y * frequency;

        float dpdu = (Perlin(u + eps, v, 1f, octaves) - Perlin(u - eps, v, 1f, octaves)) / (2f * eps);
        float dpdv = (Perlin(u, v + eps, 1f, octaves) - Perlin(u, v - eps, 1f, octaves)) / (2f * eps);

        var curl = new Vector2(dpdv, -dpdu) * CurlGain;
        float len2 = curl.LengthSquared();
        return len2 > 1f ? curl / MathF.Sqrt(len2) : curl;
    }

    internal const float CurlGain = 1.94f;

    internal static float Perlin(float x, float y, float frequency, int octaves)
    {
        float sum = 0f, amp = 1f, norm = 0f, f = MathF.Max(frequency, 1e-4f);
        for (int o = 0; o < octaves; o++)
        {
            sum += Octave(x * f, y * f) * amp;
            norm += amp;
            f *= 2f;
            amp *= 0.5f;
        }
        return norm > 0f ? sum / norm : 0f;
    }
}

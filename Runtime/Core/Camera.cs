using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PixelCore.Runtime.Core;

public class Camera
{
    public Vector2 Position { get; set; } = Vector2.Zero;
    public float Zoom { get; set; } = 1f;
    public float Rotation { get; set; } = 0f;

    private Entity? _target;
    private bool _snapPending;

    public Entity? Target
    {
        get => _target;
        set
        {
            if (_target == null && value != null) _snapPending = true;
            _target = value;
        }
    }

    public void SetTarget(Entity? target, bool snap)
    {
        _target = target;
        _snapPending = snap && target != null;
    }

    public static Vector2 DefaultFollowDamping => new(6f, 6f);

    public Vector2 FollowDamping { get; set; } = DefaultFollowDamping;

    public float FollowSpeed
    {
        get => FollowDamping.X;
        set => FollowDamping = new Vector2(value, value);
    }

    public float SettleDeadband { get; set; } = 0f;

    public Vector2 FollowOffset { get; set; } = new(0f, -18f);

    public static bool StateRoundingInDeadzone { get; set; }

    private Vector2 _prevTargetPos;
    private float _targetStillTime;

    public Vector2 Deadzone { get; set; } = Vector2.Zero;

    public Rectangle? Bounds { get; set; } = null;

    public Vector2 ShakeOffset => _shakeOffset;

    public bool PixelPerfect { get; set; } = false;
    public int GameWidth { get; set; } = 320;
    public int GameHeight { get; set; } = 180;

    private float _shakeIntensity;
    private float _shakeDuration;
    private float _shakeTimer;
    private Vector2 _shakeOffset;
    private Random _random = new();

    private int _viewportWidth;
    private int _viewportHeight;

    public Camera(GraphicsDevice graphicsDevice)
        : this(graphicsDevice.Viewport.Width, graphicsDevice.Viewport.Height)
    {
    }

    public Camera(int viewportWidth, int viewportHeight)
    {
        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;
    }

    public void UpdateViewport(int width, int height)
    {
        _viewportWidth = width;
        _viewportHeight = height;
    }

    public Matrix GetTransform()
    {
        var pos = Position;

        if (PixelPerfect)
        {
            pos = new Vector2(MathF.Floor(pos.X + 0.5f), MathF.Floor(pos.Y + 0.5f));
        }

        return Matrix.CreateTranslation(new Vector3(-pos - _shakeOffset, 0f)) *
               Matrix.CreateRotationZ(Rotation) *
               Matrix.CreateScale(Zoom, Zoom, 1f) *
               Matrix.CreateTranslation(new Vector3(MathF.Floor(_viewportWidth / 2f), MathF.Floor(_viewportHeight / 2f), 0f));
    }

    public void Update(float deltaTime)
    {
        if (_snapPending && _target != null)
        {
            var snapTransform = _target.GetComponent<PixelCore.Runtime.Components.Transform>();
            if (snapTransform != null)
            {
                Position = snapTransform.Position + FollowOffset;
                _targetStillTime = 0f;
                _snapPending = false;
            }
        }

        if (Target != null)
        {
            var transform = Target.GetComponent<PixelCore.Runtime.Components.Transform>();
            if (transform != null)
            {
                var targetPos = transform.Position + FollowOffset;

                if (targetPos == _prevTargetPos)
                    _targetStillTime += deltaTime;
                else
                    _targetStillTime = 0f;
                _prevTargetPos = targetPos;

                bool targetStill = _targetStillTime > 0.1f;
                if (targetStill)
                    targetPos = new Vector2(MathF.Floor(targetPos.X + 0.5f), MathF.Floor(targetPos.Y + 0.5f));

                float tx = SmoothFactor(FollowDamping.X, deltaTime);
                float ty = SmoothFactor(FollowDamping.Y, deltaTime);

                var delta = targetPos - Position;
                Position = new Vector2(
                    FollowAxis(Position.X, targetPos.X, delta.X, Deadzone.X, targetStill, tx),
                    FollowAxis(Position.Y, targetPos.Y, delta.Y, Deadzone.Y, targetStill, ty));
            }
        }

        ClampToBounds();

        if (_shakeTimer > 0)
        {
            _shakeTimer -= deltaTime;
            _shakeIntensity = _shakeIntensity * (_shakeTimer / _shakeDuration);
            _shakeOffset = new Vector2(
                (float)(_random.NextDouble() * 2 - 1) * _shakeIntensity,
                (float)(_random.NextDouble() * 2 - 1) * _shakeIntensity
            );
        }
        else
        {
            _shakeOffset = Vector2.Zero;
        }
    }

    private static float SmoothFactor(float k, float deltaTime)
        => k <= 0f ? 1f : 1f - MathF.Exp(-k * deltaTime);

    private float FollowAxis(float pos, float target, float delta, float deadzone, bool targetStill, float t)
    {
        float outside = MathF.Abs(delta) - deadzone;
        if (outside <= 0f) return StateRoundingInDeadzone ? MathF.Floor(pos + 0.5f) : pos;
        if (targetStill && outside < SettleDeadband) return pos;
        return LerpSnap(pos, target - MathF.Sign(delta) * deadzone, t);
    }

    private static float LerpSnap(float origin, float target, float factor, float threshold = 0.01f)
    {
        return MathF.Abs(target - origin) < threshold
            ? target
            : origin + (target - origin) * factor;
    }

    private void ClampToBounds()
    {
        if (Bounds == null) return;

        var b = Bounds.Value;

        float halfW = (PixelPerfect ? GameWidth : _viewportWidth / Zoom) * 0.5f;
        float halfH = (PixelPerfect ? GameHeight : _viewportHeight / Zoom) * 0.5f;
        var pos = Position;

        if (b.Width >= halfW * 2f)
            pos.X = MathHelper.Clamp(pos.X, b.Left + halfW, b.Right - halfW);
        else
            pos.X = b.Center.X;

        if (b.Height >= halfH * 2f)
            pos.Y = MathHelper.Clamp(pos.Y, b.Top + halfH, b.Bottom - halfH);
        else
            pos.Y = b.Center.Y;

        Position = pos;
    }

    public void Shake(float intensity, float duration)
    {
        _shakeIntensity = intensity;
        _shakeDuration = duration;
        _shakeTimer = duration;
    }

    public Vector2 ScreenToWorld(Vector2 screenPosition)
    {
        return Vector2.Transform(screenPosition, Matrix.Invert(GetTransform()));
    }

    public Vector2 WorldToScreen(Vector2 worldPosition)
    {
        return Vector2.Transform(worldPosition, GetTransform());
    }

    public int GetPixelScale(int screenWidth, int screenHeight)
    {
        int scaleX = screenWidth / GameWidth;
        int scaleY = screenHeight / GameHeight;
        return Math.Max(1, Math.Min(scaleX, scaleY));
    }

    public Rectangle GetPixelPerfectDestination(int screenWidth, int screenHeight)
    {
        int scale = GetPixelScale(screenWidth, screenHeight);
        int width = GameWidth * scale;
        int height = GameHeight * scale;
        int x = (screenWidth - width) / 2;
        int y = (screenHeight - height) / 2;
        return new Rectangle(x, y, width, height);
    }
}

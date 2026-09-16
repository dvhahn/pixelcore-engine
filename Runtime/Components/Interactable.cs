using System;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Components;

public enum InteractKind
{
    Read,
    Door,
    Event,
}

[ComponentCategory(ComponentCategories.Interaction)]
public class Interactable : Component
{
    public string Prompt { get; set; } = "verb.examine";

    public InteractKind Kind { get; set; } = InteractKind.Read;

    public string Block { get; set; } = "";

    public string Text { get; set; } = "";

    public string Event { get; set; } = "";

    public string TargetSceneId { get; set; } = "";

    public string Spawn { get; set; } = "";

    public string Facing { get; set; } = "";

    public string SoundId { get; set; } = "";

    private bool _soundWarned;

    public Action<Entity>? OnInteract { get; set; }

    public static Action<Interactable, Entity>? DefaultHandler;

    public float Range { get; set; } = 8f;

    public Color HighlightColor { get; set; } = new Color(255, 236, 180);

    public bool Focused { get; internal set; }

    private float _flash;

    private SpriteRenderer? _sprite;

    public virtual bool CanInteract(Entity actor) => Enabled;

    public virtual void Interact(Entity actor)
    {
        Console.WriteLine($"[Interact] {Entity.Name} — {Prompt} ({Kind})");
        _flash = 1f;

        PlayInteractSound();

        if (OnInteract != null) OnInteract(actor);
        else DefaultHandler?.Invoke(this, actor);
    }

    private void PlayInteractSound()
    {
        if (string.IsNullOrEmpty(SoundId)) return;

        var path = Assets.AssetRegistry.Instance.GetPath(SoundId);
        if (string.IsNullOrEmpty(path))
        {
            if (!_soundWarned)
            {
                _soundWarned = true;
                Console.Error.WriteLine(
                    $"[Interact] ✘ audio asset id '{SoundId}' is not in the registry ({Entity.Name}) - " +
                    "the file may have been deleted, or assets.json may not have been rescanned. " +
                    "Pick it again in the inspector.");
            }
            return;
        }

        Audio.AudioManager.Instance.PlaySFX(path);
    }

    public override void Update(float deltaTime)
    {
        _sprite ??= Entity.GetComponent<SpriteRenderer>();
        if (_sprite == null) return;

        if (_flash > 0f) _flash = MathF.Max(0f, _flash - deltaTime * 4f);

        float lift = MathF.Max(Focused ? 0.5f : 0f, _flash);
        _sprite.ColorOverride = lift > 0f ? Color.Lerp(_sprite.Color, HighlightColor, lift) : null;
    }

    public override void OnDisable()
    {
        if (_sprite != null) _sprite.ColorOverride = null;
        Focused = false;
    }
}

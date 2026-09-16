using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Cutscenes;

public readonly struct Actor
{
    private readonly Cutscene _c;

    public readonly string Name;

    internal Actor(Cutscene c, string name) { _c = c; Name = name; }

    public Act Say(string text, int number = 0) => _c.SayInline(Name, text, number);

    public Act Anim(string clip) => _c.Anim(Name, clip);

    public Act Walk(string anchor, float speed = 0f) => _c.Walk(Name, anchor, speed);

    public Act WalkBy(float dx, float dy, float speed = 0f) => _c.WalkBy(Name, dx, dy, speed);

    public Act Face(Dir dir) => _c.Face(Name, dir);

    public Act Put(string anchor) => _c.Put(Name, anchor);

    public Act Show() => _c.Show(Name);

    public Act Hide() => _c.Hide(Name);
}

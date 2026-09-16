using System;
using System.Collections.Generic;
using PixelCore.Runtime.Assets;

namespace PixelCore.Editor.Commands;

public class CreateSliceCommand : ICommand
{
    private readonly SpriteAtlas _atlas;
    private readonly SpriteSlice _slice;
    private int _insertedIndex = -1;

    public string Description => $"Create Slice '{_slice.Name}'";

    public CreateSliceCommand(SpriteAtlas atlas, SpriteSlice slice)
    {
        _atlas = atlas;
        _slice = slice;
    }

    public void Execute()
    {
        _atlas.Slices.Add(_slice);
        _insertedIndex = _atlas.Slices.Count - 1;
    }

    public void Undo()
    {
        if (_insertedIndex >= 0 && _insertedIndex < _atlas.Slices.Count)
        {
            _atlas.Slices.RemoveAt(_insertedIndex);
        }
    }

    public int InsertedIndex => _insertedIndex;
}

public class DeleteSliceCommand : ICommand
{
    private readonly SpriteAtlas _atlas;
    private readonly int _index;
    private SpriteSlice? _deletedSlice;

    public string Description => $"Delete Slice '{_deletedSlice?.Name}'";

    public DeleteSliceCommand(SpriteAtlas atlas, int index)
    {
        _atlas = atlas;
        _index = index;
        if (_index >= 0 && _index < _atlas.Slices.Count)
        {
            _deletedSlice = _atlas.Slices[_index];
        }
    }

    public void Execute()
    {
        if (_index >= 0 && _index < _atlas.Slices.Count)
        {
            _deletedSlice = _atlas.Slices[_index];
            _atlas.Slices.RemoveAt(_index);
        }
    }

    public void Undo()
    {
        if (_deletedSlice != null && _index >= 0)
        {
            int insertAt = Math.Min(_index, _atlas.Slices.Count);
            _atlas.Slices.Insert(insertAt, _deletedSlice);
        }
    }
}

public class MoveSliceCommand : ICommand
{
    private readonly SpriteSlice _slice;
    private readonly int _oldX, _oldY;
    private readonly int _newX, _newY;

    public string Description => $"Move Slice '{_slice.Name}'";

    public MoveSliceCommand(SpriteSlice slice, int oldX, int oldY, int newX, int newY)
    {
        _slice = slice;
        _oldX = oldX;
        _oldY = oldY;
        _newX = newX;
        _newY = newY;
    }

    public void Execute()
    {
        _slice.X = _newX;
        _slice.Y = _newY;
    }

    public void Undo()
    {
        _slice.X = _oldX;
        _slice.Y = _oldY;
    }
}

public class ResizeSliceCommand : ICommand
{
    private readonly SpriteSlice _slice;
    private readonly int _oldX, _oldY, _oldW, _oldH;
    private readonly int _newX, _newY, _newW, _newH;

    public string Description => $"Resize Slice '{_slice.Name}'";

    public ResizeSliceCommand(SpriteSlice slice,
        int oldX, int oldY, int oldW, int oldH,
        int newX, int newY, int newW, int newH)
    {
        _slice = slice;
        _oldX = oldX;
        _oldY = oldY;
        _oldW = oldW;
        _oldH = oldH;
        _newX = newX;
        _newY = newY;
        _newW = newW;
        _newH = newH;
    }

    public void Execute()
    {
        _slice.X = _newX;
        _slice.Y = _newY;
        _slice.Width = _newW;
        _slice.Height = _newH;
    }

    public void Undo()
    {
        _slice.X = _oldX;
        _slice.Y = _oldY;
        _slice.Width = _oldW;
        _slice.Height = _oldH;
    }
}

public class ClearSlicesCommand : ICommand
{
    private readonly SpriteAtlas _atlas;
    private readonly List<SpriteSlice> _deletedSlices = new();

    public string Description => "Clear All Slices";

    public ClearSlicesCommand(SpriteAtlas atlas)
    {
        _atlas = atlas;
    }

    public void Execute()
    {
        _deletedSlices.Clear();
        _deletedSlices.AddRange(_atlas.Slices);
        _atlas.Slices.Clear();
    }

    public void Undo()
    {
        _atlas.Slices.AddRange(_deletedSlices);
    }
}

public static class PivotSpread
{
    private const float Epsilon = 0.0001f;

    public static bool Differs(SpriteSlice a, SpriteSlice b)
        => MathF.Abs(a.PivotX - b.PivotX) > Epsilon || MathF.Abs(a.PivotY - b.PivotY) > Epsilon;

    public static int CountDiffering(SpriteAtlas atlas, SpriteSlice source)
    {
        int n = 0;
        foreach (var s in atlas.Slices)
            if (!ReferenceEquals(s, source) && Differs(s, source)) n++;
        return n;
    }

    public static ICommand[] Build(SpriteAtlas atlas, SpriteSlice source)
    {
        var cmds = new List<ICommand>();
        foreach (var s in atlas.Slices)
        {
            if (ReferenceEquals(s, source) || !Differs(s, source)) continue;
            cmds.Add(new SetPivotCommand(s, s.PivotX, s.PivotY, source.PivotX, source.PivotY));
        }
        return cmds.ToArray();
    }
}

public class SetPivotCommand : ICommand
{
    private readonly SpriteSlice _slice;
    private readonly float _oldX, _oldY;
    private readonly float _newX, _newY;

    public string Description => $"Set Pivot '{_slice.Name}'";

    public SetPivotCommand(SpriteSlice slice, float oldX, float oldY, float newX, float newY)
    {
        _slice = slice;
        _oldX = oldX;
        _oldY = oldY;
        _newX = newX;
        _newY = newY;
    }

    public void Execute()
    {
        _slice.PivotX = _newX;
        _slice.PivotY = _newY;
    }

    public void Undo()
    {
        _slice.PivotX = _oldX;
        _slice.PivotY = _oldY;
    }
}

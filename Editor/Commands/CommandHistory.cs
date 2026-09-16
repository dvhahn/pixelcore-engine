using System;
using System.Collections.Generic;

namespace PixelCore.Editor.Commands;

public class CommandHistory
{
    private readonly Stack<ICommand> _undoStack = new();
    private readonly Stack<ICommand> _redoStack = new();
    private readonly int _maxHistory;

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    public string? NextUndoDescription => CanUndo ? _undoStack.Peek().Description : null;

    public string? NextRedoDescription => CanRedo ? _redoStack.Peek().Description : null;

    public event Action? OnHistoryChanged;

    public CommandHistory(int maxHistory = 100)
    {
        _maxHistory = maxHistory;
    }

    public void Execute(ICommand command)
    {
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear();

        if (_undoStack.Count > _maxHistory)
        {
            TrimStack(_undoStack, _maxHistory);
        }

        OnHistoryChanged?.Invoke();
    }

    public void AddExecuted(ICommand command)
    {
        _undoStack.Push(command);
        _redoStack.Clear();

        if (_undoStack.Count > _maxHistory)
        {
            TrimStack(_undoStack, _maxHistory);
        }

        OnHistoryChanged?.Invoke();
    }

    public void Undo()
    {
        if (!CanUndo) return;

        var command = _undoStack.Pop();
        command.Undo();
        _redoStack.Push(command);

        OnHistoryChanged?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo) return;

        var command = _redoStack.Pop();
        command.Execute();
        _undoStack.Push(command);

        OnHistoryChanged?.Invoke();
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        OnHistoryChanged?.Invoke();
    }

    private static void TrimStack(Stack<ICommand> stack, int maxSize)
    {
        if (stack.Count <= maxSize) return;

        var temp = new List<ICommand>();
        while (stack.Count > 0 && temp.Count < maxSize)
        {
            temp.Add(stack.Pop());
        }
        stack.Clear();
        for (int i = temp.Count - 1; i >= 0; i--)
        {
            stack.Push(temp[i]);
        }
    }
}

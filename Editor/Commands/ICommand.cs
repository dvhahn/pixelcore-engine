namespace PixelCore.Editor.Commands;

public interface ICommand
{
    string Description { get; }

    void Execute();

    void Undo();
}

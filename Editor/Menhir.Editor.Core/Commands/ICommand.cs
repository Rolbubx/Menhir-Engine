namespace Menhir.Editor.Commands;

/// <summary>
/// An undoable edit. <see cref="Do"/> is called when the command is executed and again on
/// redo, so it must work from the state <see cref="Undo"/> leaves behind.
/// </summary>
public interface ICommand
{
    /// <summary>Shown in the Edit menu, e.g. "Rename Entity".</summary>
    string Name { get; }

    void Do();

    void Undo();

    /// <summary>
    /// Absorbs <paramref name="next"/> (already done) into this command when they edit the
    /// same thing, e.g. the frames of one slider drag. Returns false to keep them separate.
    /// </summary>
    bool TryMerge(ICommand next);
}

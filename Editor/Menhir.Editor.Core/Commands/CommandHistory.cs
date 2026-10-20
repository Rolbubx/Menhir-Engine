namespace Menhir.Editor.Commands;

/// <summary>
/// Undo and redo stacks. While a merge window is open (e.g. the mouse is still dragging a
/// field), commands that edit the same thing are merged into a single undo step; call
/// <see cref="EndMerge"/> when the interaction ends.
/// Also tracks whether the document changed since it was last saved.
/// </summary>
public sealed class CommandHistory
{
    private readonly List<Entry> _undo = new();
    private readonly List<Entry> _redo = new();
    private long _nextId = 1;
    private long _savedId; // id of the top undo entry when saved, 0 = empty stack
    private bool _mergeOpen;

    public CommandHistory(int maxSteps = 500)
    {
        MaxSteps = maxSteps;
    }

    public int MaxSteps { get; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoName => CanUndo ? _undo[^1].Command.Name : null;

    public string? RedoName => CanRedo ? _redo[^1].Command.Name : null;

    public int UndoCount => _undo.Count;

    /// <summary>True when the state differs from the one last passed to <see cref="MarkSaved"/>.</summary>
    public bool IsDirty => TopId != _savedId;

    private long TopId => _undo.Count > 0 ? _undo[^1].Id : 0;

    /// <summary>Runs the command and records it, merging it into the previous one when possible.</summary>
    public void Execute(ICommand command)
    {
        command.Do();
        _redo.Clear();

        // Never merge into the saved step, or the change would not make the document dirty.
        if (_mergeOpen && _undo.Count > 0 && TopId != _savedId && _undo[^1].Command.TryMerge(command))
            return;

        _undo.Add(new Entry(command, _nextId++));
        if (_undo.Count > MaxSteps)
            _undo.RemoveAt(0);
        _mergeOpen = true;
    }

    /// <summary>Ends the current merge window: the next command starts a new undo step.</summary>
    public void EndMerge() => _mergeOpen = false;

    public void Undo()
    {
        EndMerge();
        if (!CanUndo)
            return;
        Entry entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        entry.Command.Undo();
        _redo.Add(entry);
    }

    public void Redo()
    {
        EndMerge();
        if (!CanRedo)
            return;
        Entry entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        entry.Command.Do();
        _undo.Add(entry);
    }

    public void MarkSaved()
    {
        EndMerge();
        _savedId = TopId;
    }

    /// <summary>Forgets every step, e.g. when another scene is opened. The new state counts as saved.</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _mergeOpen = false;
        _savedId = 0;
    }

    private readonly record struct Entry(ICommand Command, long Id);
}

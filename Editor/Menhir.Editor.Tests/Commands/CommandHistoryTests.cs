using Menhir.Editor.Commands;

namespace Menhir.Editor.Tests.Commands;

public class CommandHistoryTests
{
    /// <summary>Adds to a counter; commands with the same key merge.</summary>
    private sealed class AddCommand(Box box, int amount, string key = "") : ICommand
    {
        private int _amount = amount;

        public string Name => $"Add {key}";
        public void Do() => box.Value += _amount;
        public void Undo() => box.Value -= _amount;

        public bool TryMerge(ICommand next)
        {
            if (next is not AddCommand other || key == "" || other.Key != key)
                return false;
            _amount += other._amount;
            return true;
        }

        private string Key => key;
    }

    private sealed class Box
    {
        public int Value;
    }

    [Fact]
    public void UndoAndRedo_RevertAndReapplyInOrder()
    {
        var box = new Box();
        var history = new CommandHistory();
        history.Execute(new AddCommand(box, 1));
        history.Execute(new AddCommand(box, 10));

        history.Undo();
        Assert.Equal(1, box.Value);
        history.Undo();
        Assert.Equal(0, box.Value);
        Assert.False(history.CanUndo);

        history.Redo();
        history.Redo();
        Assert.Equal(11, box.Value);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Execute_ClearsRedo()
    {
        var box = new Box();
        var history = new CommandHistory();
        history.Execute(new AddCommand(box, 1));
        history.Undo();

        history.Execute(new AddCommand(box, 5));

        Assert.False(history.CanRedo);
        Assert.Equal(5, box.Value);
    }

    [Fact]
    public void Merging_HappensOnlyInsideOneInteraction()
    {
        var box = new Box();
        var history = new CommandHistory();
        history.Execute(new AddCommand(box, 1, "drag"));
        history.Execute(new AddCommand(box, 2, "drag"));
        history.Execute(new AddCommand(box, 3, "drag"));
        history.EndMerge();
        history.Execute(new AddCommand(box, 4, "drag"));

        Assert.Equal(2, history.UndoCount);
        history.Undo();
        Assert.Equal(6, box.Value);
        history.Undo();
        Assert.Equal(0, box.Value);
    }

    [Fact]
    public void Merging_NeedsTheSameTarget()
    {
        var box = new Box();
        var history = new CommandHistory();
        history.Execute(new AddCommand(box, 1, "a"));
        history.Execute(new AddCommand(box, 1, "b"));

        Assert.Equal(2, history.UndoCount);
    }

    [Fact]
    public void IsDirty_FollowsTheSavedStep()
    {
        var box = new Box();
        var history = new CommandHistory();
        Assert.False(history.IsDirty);

        history.Execute(new AddCommand(box, 1));
        Assert.True(history.IsDirty);

        history.MarkSaved();
        Assert.False(history.IsDirty);

        history.Undo();
        Assert.True(history.IsDirty);

        history.Redo();
        Assert.False(history.IsDirty);

        history.Execute(new AddCommand(box, 1));
        history.Undo();
        Assert.False(history.IsDirty);
    }

    [Fact]
    public void IsDirty_EditAfterSaveIsNotMergedIntoTheSavedStep()
    {
        var box = new Box();
        var history = new CommandHistory();
        history.Execute(new AddCommand(box, 1, "drag"));
        history.MarkSaved();

        history.Execute(new AddCommand(box, 1, "drag"));

        Assert.True(history.IsDirty);
        Assert.Equal(2, history.UndoCount);
    }

    [Fact]
    public void Clear_ForgetsEverythingAndIsClean()
    {
        var box = new Box();
        var history = new CommandHistory();
        history.Execute(new AddCommand(box, 1));
        history.Execute(new AddCommand(box, 1));
        history.Undo();

        history.Clear();

        Assert.False(history.CanUndo || history.CanRedo || history.IsDirty);
    }

    [Fact]
    public void OldestStepsAreDroppedPastTheLimit()
    {
        var box = new Box();
        var history = new CommandHistory(maxSteps: 3);
        for (int i = 0; i < 5; i++)
        {
            history.Execute(new AddCommand(box, 1));
            history.EndMerge();
        }

        Assert.Equal(3, history.UndoCount);
        while (history.CanUndo)
            history.Undo();
        Assert.Equal(2, box.Value);
    }
}

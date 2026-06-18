using System;
using System.Collections.Generic;

/// <summary>
/// Command Pattern for the Level Editor's undo/redo (Systems Architecture, Section 11) --
/// every mutation to the in-memory EditableBlueprint goes through ExecuteCommandStack.Run()
/// instead of editing the model directly, so it can be undone/redone.
///
/// One generic ActionCommand (built from a pair of closures) stands in for what would
/// otherwise be a separate command class per operation -- placing a tile, erasing a tile,
/// editing a tile's properties, and adding/removing a world object all have the same
/// "apply this, reverse that" shape, so they share one implementation rather than five
/// near-identical classes.
/// </summary>
public interface IEditorCommand
{
    void Execute();
    void Undo();
}

public class ActionCommand : IEditorCommand
{
    private readonly Action _execute;
    private readonly Action _undo;

    public ActionCommand(Action execute, Action undo)
    {
        _execute = execute;
        _undo = undo;
    }

    public void Execute() => _execute();
    public void Undo() => _undo();
}

public class EditorCommandStack
{
    private readonly Stack<IEditorCommand> _undoStack = new();
    private readonly Stack<IEditorCommand> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>Executes a new command and pushes it onto the undo stack. Clears redo history.</summary>
    public void Run(IEditorCommand command)
    {
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear();
    }

    public void Undo()
    {
        if (_undoStack.Count == 0) return;

        var command = _undoStack.Pop();
        command.Undo();
        _redoStack.Push(command);
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;

        var command = _redoStack.Pop();
        command.Execute();
        _undoStack.Push(command);
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}

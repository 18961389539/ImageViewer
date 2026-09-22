using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ImageViewer.ViewModels
{
    /// <summary>
    /// 撤销/重做命令接口
    /// Chinese: 定义撤销/重做命令所需的 Execute 与 Undo 方法。
    /// English: Interface for undo/redo commands exposing Execute and Undo methods.
    /// </summary>
    public interface IUndoRedoCommand
    {
        void Execute();
        void Undo();
    }

    /// <summary>
    /// 撤销/重做管理器
    /// Chinese: 记录执行的命令并支持 Undo/Redo 操作；撤销历史有深度上限，超限时淘汰最旧命令，
    /// 避免长时间标注导致内存无限增长。
    /// English: Tracks executed commands and supports Undo/Redo; undo history is depth-limited and
    /// evicts the oldest command when the limit is exceeded to bound memory growth.
    /// </summary>
    public class UndoRedoManager : INotifyPropertyChanged
    {
        /// <summary>默认撤销历史深度上限。</summary>
        public const int DefaultMaxDepth = 200;

        private readonly List<IUndoRedoCommand> _undoStack;
        private readonly Stack<IUndoRedoCommand> _redoStack = new Stack<IUndoRedoCommand>();

        public event PropertyChangedEventHandler? PropertyChanged;

        public UndoRedoManager(int maxDepth = DefaultMaxDepth)
        {
            MaxDepth = maxDepth > 0 ? maxDepth : DefaultMaxDepth;
            _undoStack = new List<IUndoRedoCommand>(MaxDepth);
        }

        /// <summary>撤销历史最大深度；超过后最旧的命令会被丢弃。</summary>
        public int MaxDepth { get; }

        /// <summary>
        /// 执行命令并将其推入撤销栈。
        /// Chinese: 执行给定的 IUndoRedoCommand，然后将其推入撤销栈，同时清空重做栈；
        /// 若超出深度上限，淘汰最旧的命令。
        /// English: Executes the given command, pushes it onto the undo stack and clears the redo stack;
        /// evicts the oldest command when the depth limit is exceeded.
        /// </summary>
        /// <param name="command">要执行的命令 / Command to execute</param>
        public void Execute(IUndoRedoCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);
            command.Execute();
            _undoStack.Add(command);
            if (_undoStack.Count > MaxDepth)
            {
                _undoStack.RemoveAt(0);
            }

            _redoStack.Clear();
            RaiseStateChanged();
        }

        /// <summary>
        /// 撤销上一个命令（如果存在）。
        /// Chinese: 从撤销栈弹出并调用 Undo，然后将其放入重做栈。
        /// English: Undoes the most recently executed command if available.
        /// </summary>
        public void Undo()
        {
            if (_undoStack.Count > 0)
            {
                IUndoRedoCommand command = _undoStack[^1];
                _undoStack.RemoveAt(_undoStack.Count - 1);
                // 先放入重做栈再执行，避免 Undo 抛异常时命令从两个栈中同时丢失
                _redoStack.Push(command);
                command.Undo();
                RaiseStateChanged();
            }
        }

        /// <summary>
        /// 重做上一个被撤销的命令（如果存在）。
        /// Chinese: 从重做栈弹出并重新执行命令，然后将其放回撤销栈。
        /// English: Redoes the most recently undone command if available.
        /// </summary>
        public void Redo()
        {
            if (_redoStack.Count > 0)
            {
                var command = _redoStack.Pop();
                // 先放回撤销栈再执行，避免 Execute 抛异常时命令从两个栈中同时丢失
                _undoStack.Add(command);
                if (_undoStack.Count > MaxDepth)
                {
                    _undoStack.RemoveAt(0);
                }

                command.Execute();
                RaiseStateChanged();
            }
        }

        /// <summary>
        /// 清空撤销与重做栈。
        /// Chinese: 移除所有记录的命令，重置历史。
        /// English: Clears both undo and redo stacks, resetting history.
        /// </summary>
        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            RaiseStateChanged();
        }

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        private void RaiseStateChanged()
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

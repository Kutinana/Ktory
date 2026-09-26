using System;
using System.Collections.Generic;

namespace Ktory.Core.Runtime
{
    [Flags]
    public enum ExecutionTraceKind
    {
        Node = 1, Tag = 2, Choice = 4, Call = 8, Return = 16,
        Jump = 32, End = 64, Error = 128,
        All = Node | Tag | Choice | Call | Return | Jump | End | Error
    }

    /// <summary>A value-only diagnostic event. Tag events describe dispatch attempts, not host completion.</summary>
    public sealed class ExecutionTrace
    {
        public ExecutionTraceKind Kind { get; }
        public string Block { get; }
        public int LineNumber { get; }
        public string Message { get; }

        public ExecutionTrace(ExecutionTraceKind kind, string block, int lineNumber, string message)
        {
            Kind = kind;
            Block = block;
            LineNumber = lineNumber;
            Message = message;
        }
    }

    public sealed class CallFrameInfo
    {
        public string Block { get; }
        public int ResumeLine { get; }
        public CallFrameType Type { get; }
        public bool ReturnsToContainer { get; }

        internal CallFrameInfo(CallFrame frame)
        {
            Block = frame.Block.Label;
            Type = frame.FrameType;
            ReturnsToContainer = frame.ReturnToContainer;
            ResumeLine = frame.ReturnToContainer ? frame.SourceContainer?.LineNumber ?? 0
                : frame.StepIndex < frame.Steps.Count ? frame.Steps[frame.StepIndex].LineNumber : 0;
        }
    }

    public sealed class LoopInfo
    {
        public string Block { get; }
        public int LineNumber { get; }
        public int Iteration { get; }
        public int Limit { get; }

        internal LoopInfo(string block, LoopContext loop)
        {
            Block = block;
            LineNumber = loop.TargetNode.LineNumber;
            Iteration = loop.IterationCount;
            Limit = loop.MaxIterations;
        }
    }

    public partial class KtorySequencer
    {
        public string CurrentBlockLabel => _currentBlock.Label;
        public int CurrentLineNumber => _currentStep?.LineNumber ?? 0;
        public string CurrentNodeType => _currentStep?.GetType().Name ?? "None";

        /// <summary>
        /// Optional synchronous observation. Subscribe before Start to include initial events.
        /// Observers must not mutate playback; observer exceptions cannot interrupt execution.
        /// No trace history is retained by the sequencer.
        /// </summary>
        public event Action<ExecutionTrace>? OnTrace;

        public IReadOnlyList<CallFrameInfo> GetCallStackSnapshot()
        {
            var result = new List<CallFrameInfo>();
            foreach (var frame in CallStack) result.Add(new CallFrameInfo(frame));
            return result.AsReadOnly();
        }

        public IReadOnlyList<LoopInfo> GetActiveLoopsSnapshot()
        {
            var result = new List<LoopInfo>();
            foreach (var entry in _activeLoops) result.Add(new LoopInfo(entry.ResumeFrame.Block.Label, entry.Loop));
            return result.AsReadOnly();
        }

        private void Trace(ExecutionTraceKind kind, string message, int? line = null)
        {
            var observers = OnTrace;
            if (observers == null) return;
            var entry = new ExecutionTrace(kind, CurrentBlockLabel, line ?? CurrentLineNumber, message);
            foreach (Action<ExecutionTrace> observer in observers.GetInvocationList())
            {
                try { observer(entry); }
                catch { /* Diagnostics must never change narrative execution. */ }
            }
        }

        private void DispatchTags(IReadOnlyList<Ast.TagData> tags, int line)
        {
            if (OnTrace != null)
                foreach (var tag in tags) Trace(ExecutionTraceKind.Tag, tag.ToString(), line);
            OnTagsDispatched?.Invoke(tags);
        }

        private void TraceError(Exception error)
        {
            int line = error is Common.KtoryException ktory && ktory.Line > 0 ? ktory.Line : CurrentLineNumber;
            Trace(ExecutionTraceKind.Error, error.Message, line);
        }
    }
}

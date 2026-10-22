using System;
using System.Collections.Generic;

namespace Ktory.Core.Runtime
{
    [Flags]
    public enum ExecutionTraceKind
    {
        Node = 1, Tag = 2, Choice = 4, Call = 8, Return = 16,
        Jump = 32, End = 64, Error = 128, Warning = 256, InputIgnored = 512,
        All = Node | Tag | Choice | Call | Return | Jump | End | Error | Warning | InputIgnored
    }

    /// <summary>A value-only diagnostic event. Tag events describe dispatch attempts, not host completion.</summary>
    public sealed class ExecutionTrace
    {
        public ExecutionTraceKind Kind { get; }
        public string Block { get; }
        public int LineNumber { get; }
        public string Message { get; }
        public string LaneId { get; }
        public string LaneName { get; }

        public ExecutionTrace(ExecutionTraceKind kind, string block, int lineNumber, string message, string laneId = "root", string laneName = "root")
        {
            Kind = kind;
            Block = block;
            LineNumber = lineNumber;
            Message = message;
            LaneId = laneId;
            LaneName = laneName;
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

        private bool _isTracing;

        private void Trace(ExecutionTraceKind kind, string message, int? line = null)
        {
            var observers = OnTrace;
            if (observers == null || _isTracing) return;
            var entry = new ExecutionTrace(kind, CurrentBlockLabel, line ?? CurrentLineNumber, message, _lane.Id, _lane.Name);
            _isTracing = true;
            try
            {
                foreach (Action<ExecutionTrace> observer in observers.GetInvocationList())
                {
                    try { observer(entry); }
                    catch { /* Diagnostics must never change narrative execution. */ }
                }
            }
            finally { _isTracing = false; }
        }

        private void DispatchTags(IReadOnlyList<Ast.TagData> tags, int line)
        {
            if (OnTrace != null)
                foreach (var tag in tags) Trace(ExecutionTraceKind.Tag, tag.ToString(), line);
            OnLaneTagsDispatched?.Invoke(new LaneTagDispatch(CurrentSessionId, _lane, line, tags));
            OnTagsDispatched?.Invoke(tags);
            // A host tag may start/seek a media source. The next gate must sample its new value.
            _sampledClockSources.Clear();
        }

        private Exception? _lastTracedError;

        private void TraceError(Exception error)
        {
            if (ReferenceEquals(_lastTracedError, error)) return;
            _lastTracedError = error;
            int line = error is Common.KtoryException ktory && ktory.Line > 0 ? ktory.Line : CurrentLineNumber;
            Trace(ExecutionTraceKind.Error, error.Message, line);
        }
    }
}

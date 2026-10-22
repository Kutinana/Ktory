using System.Collections.Generic;
using Ktory.Core.Ast;

namespace Ktory.Core.Runtime
{
    // Execution state only: world state and real clocks remain with the host.
    internal sealed class LaneExecutionState
    {
        internal string Id = "root";
        internal string Name = "root";
        internal bool IsMain;
        internal LaneExecutionState? PendingTrack;
        internal StepNode? PendingTrackStep;
        internal int PendingTagIndex;
        internal ExecutionStatus Status = ExecutionStatus.Ready;
        internal TextPayload? Payload;
        internal ChoicePayload? Choice;
        internal AutoPolicy? AutoPolicy;
        internal long PresentationId;
        internal readonly Stack<CallFrame> CallStack = new Stack<CallFrame>();
        internal readonly Stack<(LoopContext Loop, CallFrame ResumeFrame, int CallStackDepth)> ActiveLoops =
            new Stack<(LoopContext, CallFrame, int)>();
        internal readonly HashSet<string> FallbackWarnings = new HashSet<string>(System.StringComparer.Ordinal);
        internal KtoryBlock Block;
        internal List<StepNode> Steps;
        internal int StepIndex;
        internal StepNode? Step;
        internal StepNode? PendingClockStep;
        internal ClockAnchor? ClockAnchor;
        internal ClockState? DefaultClock;
        internal ClockState? PendingClock;

        internal LaneExecutionState(KtoryBlock block, List<StepNode>? steps = null)
        {
            Block = block;
            Steps = steps ?? block.Steps;
        }
    }

    /// <summary>An active output and its execution context. Reading it never dispatches tags.</summary>
    public sealed class LanePresentation
    {
        public string LaneId { get; }
        public string LaneName { get; }
        public bool IsMain { get; }
        public PresentationToken Token { get; }
        public ExecutionStatus Status { get; }
        public TextPayload? Payload { get; }
        public ChoicePayload? Choice { get; }
        public AutoPolicyData? AutoPolicy { get; }
        public bool RequiresInput { get; }

        internal LanePresentation(string sessionId, LaneExecutionState lane, TextPayload? retainedPayload = null)
        {
            LaneId = lane.Id;
            LaneName = lane.Name;
            IsMain = lane.IsMain;
            Token = new PresentationToken(sessionId, retainedPayload?.PresentationId ?? lane.PresentationId);
            Status = lane.Status;
            Payload = retainedPayload ?? lane.Payload;
            Choice = retainedPayload == null ? lane.Choice : null;
            AutoPolicy = lane.AutoPolicy;
            RequiresInput = (retainedPayload == null || retainedPayload.PresentationId == lane.PresentationId) && KtorySequencer.RequiresInteractiveInput(lane);
        }
    }

    /// <summary>A tag dispatch with its origin, not evidence of successful host execution.</summary>
    public sealed class LaneTagDispatch
    {
        public string SessionId { get; }
        public string LaneId { get; }
        public string LaneName { get; }
        public bool IsMain { get; }
        public int LineNumber { get; }
        public IReadOnlyList<TagData> Tags { get; }
        internal LaneTagDispatch(string sessionId, LaneExecutionState lane, int line, IReadOnlyList<TagData> tags)
        {
            SessionId = sessionId;
            LaneId = lane.Id;
            LaneName = lane.Name;
            IsMain = lane.IsMain;
            LineNumber = line;
            Tags = tags;
        }
    }
}

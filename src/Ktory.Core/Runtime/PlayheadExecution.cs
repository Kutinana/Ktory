using System;
using System.Collections.Generic;
using Ktory.Core.Ast;
using Ktory.Core.Common;

namespace Ktory.Core.Runtime
{
    // A session has one input owner and independently scheduled auxiliary playheads.
    public partial class KtorySequencer
    {
        private LaneExecutionState _lane = null!;
        private LaneExecutionState _rootLane = null!;
        private readonly List<LaneExecutionState> _lanes = new List<LaneExecutionState>();
        private readonly Queue<LaneExecutionState> _readyLanes = new Queue<LaneExecutionState>();
        // A null value is a terminal record: named declarations never restart in this session.
        private readonly Dictionary<string, LaneExecutionState?> _namedTracks = new Dictionary<string, LaneExecutionState?>(StringComparer.Ordinal);
        private int _operationDepth;
        private int _batchInstructions;
        private long _laneCounter;
        private bool _sessionEnded;

        public int MaxInstructionBudgetPerBatch { get; set; } = 10000;
        public int MaxActiveLanes { get; set; } = 128;
        public event Action<LaneTagDispatch>? OnLaneTagsDispatched;
        private ExecutionStatus SessionStatus => _rootLane.Status;
        private LaneExecutionState VisibleLane => _operationDepth > 0 ? _lane : _rootLane;

        private static bool IsActivePresentation(LaneExecutionState lane) => lane.IsMain && lane.PresentationId > 0 &&
            (lane.Status == ExecutionStatus.SuspendedAtBeat || lane.Status == ExecutionStatus.AwaitingChoice);

        internal static bool RequiresInteractiveInput(LaneExecutionState lane) => lane.IsMain &&
            (lane.Status == ExecutionStatus.AwaitingChoice ||
            (lane.Status == ExecutionStatus.SuspendedAtBeat && lane.AutoPolicy?.Enabled != true &&
             lane.Payload?.StepType == StepType.Text && !HasNext(lane.Payload)));

        private static bool HasNext(TextPayload payload)
        {
            foreach (var tag in payload.Tags)
                if (string.Equals(tag.Name, "next", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public IReadOnlyList<LanePresentation> GetActivePresentations()
        {
            var result = new List<LanePresentation>();
            if (IsActivePresentation(_rootLane)) result.Add(new LanePresentation(CurrentSessionId, _rootLane));
            return result.AsReadOnly();
        }

        public LanePresentation? GetLanePresentation(string laneId) =>
            _rootLane.Id == laneId && IsActivePresentation(_rootLane) ? new LanePresentation(CurrentSessionId, _rootLane) : null;

        public LanePresentation? GetPresentation(PresentationToken token)
        {
            var lane = FindPresentationLane(token);
            return lane == null ? null : new LanePresentation(CurrentSessionId, lane);
        }

        private LaneExecutionState? FindPresentationLane(PresentationToken token) =>
            !string.IsNullOrEmpty(CurrentSessionId) && token.SessionId == CurrentSessionId &&
            token.PresentationId == _rootLane.PresentationId && IsActivePresentation(_rootLane) ? _rootLane : null;

        private void ResetExecutionLanes()
        {
            _lanes.Clear(); _readyLanes.Clear(); _namedTracks.Clear();
            CleanupSessionClocks();
            _clockCounter = 0; _elapsedSeconds = 0;
            _rootLane = new LaneExecutionState(File.RootBlock) { IsMain = true };
            _lane = _rootLane; _lanes.Add(_rootLane);
            _laneCounter = 0; _sessionEnded = false;
            _retainedOutputs.Clear(); _lastOutputs.Clear(); _outputSequence = 0;
        }

        private void ExecuteStart(Action start)
        {
            EnsureNotExecuting();
            ResetExecutionLanes();
            ExecuteLaneOperation(_rootLane, start);
        }

        private void EnsureNotExecuting()
        {
            if (_operationDepth != 0 || _isTracing)
                throw new KtoryControlFlowException("Reentrant narrative execution is not supported. Queue host inputs until the current operation returns.");
        }

        private void ExecuteInput(long? presentationId, Action input)
        {
            EnsureNotExecuting();
            if (presentationId.HasValue && (!IsActivePresentation(_rootLane) || _rootLane.PresentationId != presentationId.Value))
            {
                TraceIgnoredInput("input", new PresentationToken(CurrentSessionId, presentationId.Value));
                return;
            }
            ExecuteLaneOperation(_rootLane, input);
        }

        private void ExecuteLaneOperation(LaneExecutionState target, Action action)
        {
            _operationDepth++; _batchInstructions = 0; _lastOutputs.Clear();
            _sampledClockSources.Clear(); _lane = target;
            try
            {
                action();
                SettleCompletedLane(target);
                while (!_sessionEnded)
                {
                    while (_readyLanes.Count > 0 && !_sessionEnded)
                    {
                        _lane = _readyLanes.Dequeue();
                        if (_lane.Status != ExecutionStatus.Ready) continue;
                        if (_lane.PendingTrackStep == null || !ProcessBeatStep(_lane.PendingTrackStep, resumeTrackWait: true))
                            Advance();
                        SettleCompletedLane(_lane);
                    }
                    if (_sessionEnded || !ReleaseClockGates()) break;
                    // Releasing a final timed output can complete a head immediately.
                    foreach (var lane in _lanes.ToArray()) SettleCompletedLane(lane);
                }
            }
            catch (Exception error)
            {
                TraceError(error);
                if (_lanes.Count > 1)
                {
                    foreach (var lane in _lanes)
                    {
                        lane.Status = ExecutionStatus.Error; lane.PresentationId = 0;
                        lane.Payload = null; lane.Choice = null;
                    }
                    _rootLane.Status = ExecutionStatus.Error; _readyLanes.Clear();
                }
                throw;
            }
            finally { _operationDepth--; _lane = _rootLane; }
        }

        private LaneExecutionState? ApplyTrackOperation(TrackOperation operation, int line, LaneExecutionState? createdTrack)
        {
            if (!_lane.IsMain) return null;
            if (operation.Kind == TrackOperationKind.AwaitCreated)
            {
                WaitForTrack(createdTrack);
                return null;
            }
            if (operation.Kind == TrackOperationKind.Create)
            {
                if (operation.Name != null && _namedTracks.TryGetValue(operation.Name, out var existing)) return existing;
                if (!File.TryGetBlock(operation.Source!, out var block))
                {
                    Trace(ExecutionTraceKind.Warning, $"Track source '{operation.Source}' is unavailable.", line);
                    return null;
                }
                if (_lanes.Count >= MaxActiveLanes)
                    throw new KtoryException($"Active playhead budget of {MaxActiveLanes} exceeded.", line, 1);
                var auxiliary = new LaneExecutionState(block)
                {
                    Id = "track-" + ++_laneCounter, Name = operation.Name ?? string.Empty, IsMain = false
                };
                _lanes.Add(auxiliary);
                if (operation.Name != null) _namedTracks.Add(operation.Name, auxiliary);
                _readyLanes.Enqueue(auxiliary);
                return auxiliary;
            }
            if (!_namedTracks.TryGetValue(operation.Name!, out var target))
            {
                if (operation.Kind == TrackOperationKind.Await)
                    Trace(ExecutionTraceKind.Warning, $"Track '{operation.Name}' has not been started; awaitTrack does not wait for future creation.", line);
                return null;
            }
            if (target == null) return null;
            if (operation.Kind == TrackOperationKind.Stop)
            {
                target.Status = ExecutionStatus.Completed;
                SettleCompletedLane(target);
            }
            else WaitForTrack(target);
            return null;
        }

        private void WaitForTrack(LaneExecutionState? target)
        {
            if (target == null) return;
            _rootLane.PendingTrack = target;
            _rootLane.Status = ExecutionStatus.WaitingForTrack;
            _rootLane.PresentationId = 0;
        }

        private void SettleCompletedLane(LaneExecutionState lane)
        {
            if (lane.Status != ExecutionStatus.Completed) return;
            if (lane.IsMain)
            {
                if (_sessionEnded) return;
                _sessionEnded = true;
                foreach (var auxiliary in _lanes.ToArray())
                {
                    if (auxiliary.IsMain) continue;
                    auxiliary.Status = ExecutionStatus.Completed;
                    SettleCompletedLane(auxiliary);
                }
                _readyLanes.Clear();
                ReleaseLaneClocks(lane);
                CleanupSessionClocks();
                PublishOutput(StoryOutputKind.SessionEnded, lane);
                return;
            }
            if (!_lanes.Remove(lane)) return;
            if (lane.Name.Length > 0) _namedTracks[lane.Name] = null;
            ReleaseLaneClocks(lane);
            lane.CallStack.Clear(); lane.ActiveLoops.Clear();
            PublishOutput(StoryOutputKind.TrackEnded, lane);
            if (!_sessionEnded && _rootLane.Status == ExecutionStatus.WaitingForTrack && ReferenceEquals(_rootLane.PendingTrack, lane))
            {
                _rootLane.PendingTrack = null;
                _rootLane.Status = ExecutionStatus.Ready;
                _readyLanes.Enqueue(_rootLane);
            }
        }
    }
}

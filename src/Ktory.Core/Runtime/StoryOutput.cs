using System;
using System.Collections.Generic;
using Ktory.Core.Ast;

namespace Ktory.Core.Runtime
{
    public enum StoryOutputKind { Content, LanguageRefresh, TrackEnded, SessionEnded }

    /// <summary>An ordered delivery, independent of whether its playhead still exists after this operation.</summary>
    public sealed class StoryOutput
    {
        public StoryOutputKind Kind { get; }
        public long Sequence { get; }
        public string SessionId { get; }
        public string TrackId { get; }
        public string? TrackName { get; }
        public bool IsMain { get; }
        public PresentationToken Token { get; }
        public TextPayload? Payload { get; }
        public ChoicePayload? Choice { get; }

        internal StoryOutput(StoryOutputKind kind, long sequence, string sessionId, LaneExecutionState lane,
            long presentationId, TextPayload? payload = null, ChoicePayload? choice = null)
        {
            Kind = kind; Sequence = sequence; SessionId = sessionId; TrackId = lane.Id;
            TrackName = lane.Name.Length == 0 ? null : lane.Name; IsMain = lane.IsMain;
            Token = new PresentationToken(sessionId, presentationId);
            Payload = payload == null ? null : Snapshot(payload);
            Choice = choice == null ? null : Snapshot(choice);
        }

        private static LocalizedTextOutput Copy(LocalizedTextOutput value) => new LocalizedTextOutput
        {
            Primary = Copy(value.Primary), Secondary = value.Secondary == null ? null : Copy(value.Secondary)
        };
        private static ResolvedLocalizedValue Copy(ResolvedLocalizedValue value) => new ResolvedLocalizedValue
        {
            Content = value.Content, RequestedLanguage = value.RequestedLanguage,
            ActualLanguage = value.ActualLanguage, Segments = value.Segments
        };
        private static TextPayload Snapshot(TextPayload value) => new TextPayload
        {
            StepId = value.StepId, PresentationId = value.PresentationId, StepType = value.StepType,
            LineNumber = value.LineNumber, Text = Copy(value.Text),
            SpeakerName = value.SpeakerName == null ? null : Copy(value.SpeakerName), SpeakerId = value.SpeakerId,
            Tags = value.Tags, AutoPolicy = value.AutoPolicy
        };
        private static ChoicePayload Snapshot(ChoicePayload value)
        {
            var options = new List<ChoiceOption>();
            foreach (var item in value.Options) options.Add(new ChoiceOption
            {
                Id = item.Id, LineNumber = item.LineNumber, Text = Copy(item.Text), Marker = item.Marker,
                IsConsumed = item.IsConsumed, CanSelect = item.CanSelect, Tags = item.Tags
            });
            return new ChoicePayload { LineNumber = value.LineNumber, PresentationId = value.PresentationId,
                ContainerName = value.ContainerName, IsLoop = value.IsLoop, Tags = value.Tags, Options = options.AsReadOnly() };
        }
    }

    internal sealed class RetainedOutput
    {
        internal LaneExecutionState Lane = null!;
        internal TextStep Step = null!;
        internal TextPayload Payload = null!;
    }

    public partial class KtorySequencer
    {
        private readonly List<StoryOutput> _lastOutputs = new List<StoryOutput>();
        private readonly Dictionary<string, RetainedOutput> _retainedOutputs = new Dictionary<string, RetainedOutput>(StringComparer.Ordinal);
        private long _outputSequence;
        /// <summary>Subscribe before Start. Observers cannot reenter execution; enqueue host work instead.</summary>
        public event Action<StoryOutput>? OnOutput;
        /// <summary>The latest public operation's deliveries. Subscribe to OnOutput across multiple operations.</summary>
        public IReadOnlyList<StoryOutput> LastOutputs => _lastOutputs.AsReadOnly();

        private void PublishContent()
        {
            // A playhead has one current statement, regardless of how its host routes displays.
            // Waiting at the next statement's clock gate does not publish it or retire this one.
            _retainedOutputs.Remove(_lane.Id);
            if (_lane.Step is TextStep text && _lane.Payload != null && _lane.Payload.Content.Length > 0)
                _retainedOutputs[_lane.Id] = new RetainedOutput { Lane = _lane, Step = text, Payload = _lane.Payload };
            PublishOutput(StoryOutputKind.Content, _lane, _lane.Payload, _lane.Choice);
        }

        private void PublishOutput(StoryOutputKind kind, LaneExecutionState lane, TextPayload? payload = null, ChoicePayload? choice = null)
        {
            var output = new StoryOutput(kind, ++_outputSequence, CurrentSessionId, lane,
                payload?.PresentationId ?? choice?.PresentationId ?? 0, payload, choice);
            _lastOutputs.Add(output);
            var observers = OnOutput;
            if (observers == null) return;
            foreach (Action<StoryOutput> observer in observers.GetInvocationList())
            {
                try { observer(output); }
                catch (Exception error) { Trace(ExecutionTraceKind.Warning, "Output observer failed: " + error.Message); }
            }
        }

        /// <summary>
        /// Returns the current text per playhead and the current Main choice.
        /// Retained display content is not input authority. Ended auxiliary text remains refreshable.
        /// </summary>
        public IReadOnlyList<LanePresentation> GetRetainedPresentations()
        {
            var result = new List<LanePresentation>();
            foreach (var retained in _retainedOutputs.Values)
                result.Add(new LanePresentation(CurrentSessionId, retained.Lane, retained.Payload));
            if (_rootLane.Status == ExecutionStatus.AwaitingChoice)
                result.Add(new LanePresentation(CurrentSessionId, _rootLane));
            return result.AsReadOnly();
        }

        /// <summary>
        /// A host may release content after removing it from its last display. It will no longer refresh.
        /// Call after the current operation returns, not synchronously from an output observer.
        /// </summary>
        public void ReleaseOutput(PresentationToken token)
        {
            EnsureNotExecuting();
            if (token.SessionId != CurrentSessionId) return;
            string? laneId = null;
            foreach (var entry in _retainedOutputs)
                if (entry.Value.Payload.PresentationId == token.PresentationId) { laneId = entry.Key; break; }
            if (laneId != null) _retainedOutputs.Remove(laneId);
        }

        private void RefreshAllLanguageOutputs()
        {
            EnsureNotExecuting();
            _operationDepth++; _lastOutputs.Clear();
            try
            {
                bool refreshedMainPayload = false;
                foreach (var retained in _retainedOutputs.Values)
                {
                    _lane = retained.Lane;
                    if (ReferenceEquals(retained.Payload, _rootLane.Payload)) refreshedMainPayload = true;
                    RefreshTextPayload(retained.Step, retained.Payload);
                    PublishOutput(StoryOutputKind.LanguageRefresh, _lane, retained.Payload);
                }
                _lane = _rootLane;
                if (_rootLane.Status == ExecutionStatus.AwaitingChoice)
                {
                    RefreshCurrentLanguageOutput();
                    PublishOutput(StoryOutputKind.LanguageRefresh, _rootLane, null, _rootLane.Choice);
                }
                else if (_rootLane.Status == ExecutionStatus.SuspendedAtBeat &&
                    _rootLane.Payload?.StepType == StepType.Text && !refreshedMainPayload)
                {
                    // A released (hidden) Main sentence still owns timing/input state. Refresh that
                    // payload, but do not republish display content which the host already removed.
                    RefreshCurrentLanguageOutput();
                    if (_rootLane.Payload.Content.Length == 0)
                        PublishOutput(StoryOutputKind.LanguageRefresh, _rootLane, _rootLane.Payload);
                }
            }
            finally { _operationDepth--; _lane = _rootLane; }
        }
    }
}

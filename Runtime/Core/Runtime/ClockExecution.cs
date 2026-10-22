using System;
using System.Collections.Generic;
using Ktory.Core.Ast;
using Ktory.Core.Common;

namespace Ktory.Core.Runtime
{
    /// <summary>Optional host ownership of a source-less clock. Null samples mean temporarily unavailable.</summary>
    public sealed class ClockSourceBinding
    {
        public Func<double?> Read { get; }
        public Action? Reset { get; }
        public ClockSourceBinding(Func<double?> read, Action? reset = null)
        { Read = read ?? throw new ArgumentNullException(nameof(read)); Reset = reset; }
    }

    public sealed class ClockCreationRequest
    {
        public string? Name { get; }
        public string LaneId { get; }
        public int LineNumber { get; }
        internal ClockCreationRequest(string? name, string laneId, int line)
        { Name = name; LaneId = laneId; LineNumber = line; }
    }

    internal sealed class ClockState
    {
        internal long Id;
        internal string? Name;
        internal string? SourceName;
        internal string OwnerLaneId = string.Empty;
        internal double Position;
        internal double DeclaredAt;
        internal int Line;
        internal ClockSourceBinding? OverrideBinding;
        internal bool CreationFailed;
        internal double? OverridePosition;
        internal bool IsExternal => SourceName != null || OverrideBinding != null || CreationFailed;
    }

    public sealed class ClockSnapshot
    {
        public long ClockId { get; }
        public string? Name { get; }
        public string? SourceName { get; }
        public string OwnerLaneId { get; }
        public bool IsExternal { get; }
        public double? Position { get; }
        public double DeclaredAtElapsedSeconds { get; }
        public int LineNumber { get; }
        internal ClockSnapshot(ClockState clock, double? position)
        {
            ClockId = clock.Id; Name = clock.Name; SourceName = clock.SourceName;
            OwnerLaneId = clock.OwnerLaneId; IsExternal = clock.IsExternal;
            Position = position; DeclaredAtElapsedSeconds = clock.DeclaredAt; LineNumber = clock.Line;
        }
    }

    public sealed class LaneClockGate
    {
        public string LaneId { get; }
        public string LaneName { get; }
        public PresentationToken Token { get; }
        public string? ClockName { get; }
        public long ClockId { get; }
        public string? SourceName { get; }
        public double? ClockPosition { get; }
        public double Position { get; }
        public int LineNumber { get; }
        internal LaneClockGate(string sessionId, LaneExecutionState lane, ClockState clock, double? position)
        {
            LaneId = lane.Id; LaneName = lane.Name;
            Token = new PresentationToken(sessionId, lane.PresentationId);
            ClockName = clock.Name; ClockId = clock.Id; SourceName = clock.SourceName; ClockPosition = position;
            Position = lane.ClockAnchor!.Position; LineNumber = lane.PendingClockStep!.LineNumber;
        }
    }

    public partial class KtorySequencer
    {
        private readonly Dictionary<string, ClockState> _clocks = new Dictionary<string, ClockState>(StringComparer.Ordinal);
        private readonly List<ClockState> _anonymousClocks = new List<ClockState>();
        private readonly Dictionary<string, double> _clockSourcePositions = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly Dictionary<string, Func<double?>> _clockSources = new Dictionary<string, Func<double?>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Action> _clockSourceResetters = new Dictionary<string, Action>(StringComparer.Ordinal);
        // String keys share one source sample; ClockState keys are independent host-created instances.
        private readonly HashSet<object> _sampledClockSources = new HashSet<object>();
        private readonly HashSet<string> _clockDiagnostics = new HashSet<string>(StringComparer.Ordinal);
        private Func<ClockCreationRequest, ClockSourceBinding?>? _clockFactory;
        private double _elapsedSeconds;
        private long _clockCounter;
        public double ElapsedSeconds => _elapsedSeconds;

        /// <summary>Invoked only for declarations without source. Return null to use the built-in delta timer.</summary>
        public Func<ClockCreationRequest, ClockSourceBinding?>? ClockFactory
        {
            get => _clockFactory;
            set { EnsureNotExecuting(); _clockFactory = value; }
        }

        public void BindClock(string name, Func<double> source) => BindClock(name, source, null);
        public void BindClock(string name, Func<double> source, Action? reset)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            BindOptionalClock(name, () => source(), reset);
        }

        /// <summary>Registers an opaque source. A null sample preserves the entry gate without guessing a value.</summary>
        public void BindOptionalClock(string name, Func<double?> source, Action? reset = null)
        {
            EnsureNotExecuting();
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("A source name is required.", nameof(name));
            _clockSources[name] = source ?? throw new ArgumentNullException(nameof(source));
            if (reset == null) _clockSourceResetters.Remove(name);
            else _clockSourceResetters[name] = reset;
            _clockSourcePositions.Remove(name);
            _sampledClockSources.Remove(name);
        }

        public void UnbindClock(string name)
        {
            EnsureNotExecuting();
            _clockSources.Remove(name); _clockSourceResetters.Remove(name);
            _clockSourcePositions.Remove(name); _sampledClockSources.Remove(name);
        }

        /// <summary>Observation only: never calls a host getter or executes script.</summary>
        public IReadOnlyList<ClockSnapshot> GetClocks()
        {
            var clocks = AllClocks();
            clocks.Sort((left, right) => left.Id.CompareTo(right.Id));
            var result = new List<ClockSnapshot>();
            foreach (var clock in clocks) result.Add(new ClockSnapshot(clock, ReadClockPosition(clock, false)));
            return result.AsReadOnly();
        }

        public IReadOnlyList<LaneClockGate> GetPendingClockGates()
        {
            var result = new List<LaneClockGate>();
            foreach (var lane in _lanes)
                if (lane.Status == ExecutionStatus.WaitingForClock && lane.ClockAnchor != null && lane.PendingClock != null)
                    result.Add(new LaneClockGate(CurrentSessionId, lane, lane.PendingClock, ReadClockPosition(lane.PendingClock, false)));
            return result.AsReadOnly();
        }

        public void UpdateClocks(double deltaTime, string expectedSessionId)
        {
            EnsureNotExecuting();
            if (!AcceptClockInput(expectedSessionId)) return;
            if (!IsFinite(deltaTime) || deltaTime < 0) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            ValidateClockBatch(deltaTime, null);
            ExecuteLaneOperation(_rootLane, () => SampleClocks(deltaTime));
        }

        /// <summary>Validates the whole input batch before publishing time or source samples.</summary>
        public void UpdateClocks(double deltaTime, IReadOnlyDictionary<string, double> sourcePositions, string expectedSessionId)
        {
            EnsureNotExecuting();
            if (!AcceptClockInput(expectedSessionId)) return;
            if (sourcePositions == null) throw new ArgumentNullException(nameof(sourcePositions));
            var values = new Dictionary<string, double?>(StringComparer.Ordinal);
            foreach (var source in sourcePositions) values.Add(source.Key, source.Value);
            UpdateClocks(deltaTime, values, expectedSessionId);
        }

        /// <summary>
        /// Publishes source samples atomically. Null clears that source's last sample, so a loading,
        /// stopped, or unavailable host resource cannot satisfy a gate with its previous value.
        /// Omitted sources retain their existing sample or are sampled from their bound getter.
        /// </summary>
        public void UpdateClocks(double deltaTime, IReadOnlyDictionary<string, double?> sourcePositions, string expectedSessionId)
        {
            EnsureNotExecuting();
            if (!AcceptClockInput(expectedSessionId)) return;
            if (!IsFinite(deltaTime) || deltaTime < 0) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (sourcePositions == null) throw new ArgumentNullException(nameof(sourcePositions));
            ValidateClockBatch(deltaTime, sourcePositions);
            ExecuteLaneOperation(_rootLane, () => SampleClocks(deltaTime, sourcePositions));
        }

        public void PollClocks(string expectedSessionId) => UpdateClocks(0, expectedSessionId);

        public void PublishClockPosition(string sourceName, double position, string expectedSessionId)
        {
            EnsureNotExecuting();
            if (!AcceptClockInput(expectedSessionId)) return;
            if (string.IsNullOrEmpty(sourceName)) throw new ArgumentException("A source name is required.", nameof(sourceName));
            if (!IsFinite(position)) throw new ArgumentOutOfRangeException(nameof(position));
            ExecuteLaneOperation(_rootLane, () => PublishSourceSample(sourceName, position));
        }

        private bool AcceptClockInput(string sessionId)
        {
            if (!string.IsNullOrEmpty(sessionId) && sessionId == CurrentSessionId && Status != ExecutionStatus.Error && Status != ExecutionStatus.Completed) return true;
            TraceIgnoredInput(nameof(UpdateClocks), new PresentationToken(sessionId, 0));
            return false;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private List<ClockState> AllClocks()
        {
            var result = new List<ClockState>(_clocks.Values);
            result.AddRange(_anonymousClocks);
            return result;
        }

        private void ValidateClockBatch(double deltaTime, IReadOnlyDictionary<string, double?>? sourcePositions)
        {
            if (!IsFinite(_elapsedSeconds + deltaTime)) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            foreach (var clock in AllClocks())
                if (!clock.IsExternal && !IsFinite(clock.Position + deltaTime))
                    throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (sourcePositions != null)
                foreach (var value in sourcePositions)
                    if (string.IsNullOrEmpty(value.Key) || (value.Value.HasValue && !IsFinite(value.Value.Value)))
                        throw new ArgumentException("Source samples require non-empty names and finite values or null.", nameof(sourcePositions));
        }

        private void SampleClocks(double deltaTime, IReadOnlyDictionary<string, double?>? sourcePositions = null)
        {
            double elapsed = _elapsedSeconds + deltaTime;
            if (!IsFinite(elapsed)) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            var values = new Dictionary<string, double?>(StringComparer.Ordinal);
            if (sourcePositions != null)
                foreach (var value in sourcePositions)
                {
                    if (string.IsNullOrEmpty(value.Key) || (value.Value.HasValue && !IsFinite(value.Value.Value)))
                        throw new ArgumentException("Source samples require non-empty names and finite values or null.", nameof(sourcePositions));
                    values.Add(value.Key, value.Value);
                }
            var clocks = AllClocks();
            foreach (var clock in clocks)
                if (!clock.IsExternal && !IsFinite(clock.Position + deltaTime))
                    throw new ArgumentOutOfRangeException(nameof(deltaTime));
            // Host provider faults affect that source, not independent clocks or other playheads.
            foreach (var clock in clocks)
            {
                if (clock.SourceName != null && !values.ContainsKey(clock.SourceName) && _clockSources.TryGetValue(clock.SourceName, out var getter))
                    values.Add(clock.SourceName, SampleClockSource(clock.SourceName, getter, clock.Line));
                else if (clock.OverrideBinding != null)
                {
                    clock.OverridePosition = SampleClockSource(ClockDescription(clock), clock.OverrideBinding.Read, clock.Line);
                    _sampledClockSources.Add(clock);
                }
            }
            _elapsedSeconds = elapsed;
            foreach (var clock in clocks)
                if (!clock.IsExternal) clock.Position += deltaTime;
            foreach (var value in values) PublishSourceSample(value.Key, value.Value);
        }

        private void PublishSourceSample(string name, double? value)
        {
            if (value.HasValue) _clockSourcePositions[name] = value.Value;
            else _clockSourcePositions.Remove(name);
            _sampledClockSources.Add(name);
        }

        private double? SampleClockSource(string description, Func<double?> getter, int line)
        {
            try
            {
                var value = getter();
                if (value.HasValue && !IsFinite(value.Value))
                    throw new InvalidOperationException("returned a non-finite value");
                return value;
            }
            catch (Exception error)
            {
                ReportClockFailure(description, "read", error, line);
                return null;
            }
        }

        private void ReportClockFailure(string description, string operation, Exception error, int line)
        {
            var message = $"Clock source '{description}' {operation} failed: {error.Message}. No value or fallback was substituted.";
            if (_clockDiagnostics.Add(message)) Trace(ExecutionTraceKind.Warning, message, line);
        }

        private static string ClockDescription(ClockState clock) => clock.SourceName ?? clock.Name ?? $"anonymous clock {clock.Id}";

        private double? ReadClockPosition(ClockState clock, bool sample)
        {
            if (!clock.IsExternal) return clock.Position;
            if (clock.SourceName != null)
            {
                var name = clock.SourceName;
                if (sample && !_sampledClockSources.Contains(name) && _clockSources.TryGetValue(name, out var getter))
                    PublishSourceSample(name, SampleClockSource(name, getter, clock.Line));
                return _clockSourcePositions.TryGetValue(name, out var value) ? value : (double?)null;
            }
            if (sample && clock.OverrideBinding != null && _sampledClockSources.Add(clock))
                clock.OverridePosition = SampleClockSource(ClockDescription(clock), clock.OverrideBinding.Read, clock.Line);
            return clock.OverridePosition;
        }

        private ClockState FindClock(string? name, int line)
        {
            if (name == null)
            {
                if (_lane.DefaultClock != null) return _lane.DefaultClock;
                throw ClockError("This playhead has no default clock. Create an anonymous .clock() or select .defaultClock(name=...) first.", line);
            }
            if (_clocks.TryGetValue(name, out var clock)) return clock;
            throw ClockError($"Clock '{name}' has not been created. Execute .clock(name=\"{name}\") before referencing it.", line);
        }

        private KtoryException ClockError(string message, int line)
        {
            Status = ExecutionStatus.Error;
            return new KtoryException(message, line, 1);
        }

        private void ApplyClockOperation(ClockOperation operation, int line)
        {
            if (operation.Kind == ClockOperationKind.SelectDefault)
            {
                SelectDefaultClock(FindClock(operation.Name, line));
                return;
            }
            if (operation.Kind == ClockOperationKind.Reset)
            {
                ResetClock(FindClock(operation.Name, line), line);
                return;
            }
            if (operation.Name != null && _clocks.ContainsKey(operation.Name)) return;
            var clock = new ClockState
            {
                Id = ++_clockCounter, Name = operation.Name, SourceName = operation.SourceName,
                OwnerLaneId = _lane.Id, DeclaredAt = _elapsedSeconds, Line = line
            };
            if (clock.SourceName == null && _clockFactory != null)
            {
                try { clock.OverrideBinding = _clockFactory(new ClockCreationRequest(clock.Name, _lane.Id, line)); }
                catch (Exception error)
                {
                    clock.CreationFailed = true;
                    ReportClockFailure(ClockDescription(clock), "creation", error, line);
                }
            }
            if (clock.Name == null)
            {
                _anonymousClocks.Add(clock);
                SelectDefaultClock(clock);
            }
            else _clocks.Add(clock.Name, clock);
            if (clock.SourceName != null) _sampledClockSources.Remove(clock.SourceName);
        }

        private void SelectDefaultClock(ClockState clock)
        {
            var previous = _lane.DefaultClock;
            if (previous != null && previous.Name == null && !ReferenceEquals(previous, clock))
            {
                _anonymousClocks.Remove(previous);
                _sampledClockSources.Remove(previous);
            }
            _lane.DefaultClock = clock;
        }

        private void ResetClock(ClockState clock, int line)
        {
            if (!clock.IsExternal) { clock.Position = 0; return; }
            Action? reset = clock.OverrideBinding?.Reset;
            if (clock.SourceName != null) _clockSourceResetters.TryGetValue(clock.SourceName, out reset);
            if (reset == null) return;
            try { reset(); }
            catch (Exception error) { ReportClockFailure(ClockDescription(clock), "reset", error, line); }
            finally
            {
                if (clock.SourceName != null) _clockSourcePositions.Remove(clock.SourceName);
                clock.OverridePosition = null;
                // A reset can change multiple host views of the same underlying resource.
                _sampledClockSources.Clear();
            }
        }

        private void ReleaseLaneClocks(LaneExecutionState lane)
        {
            if (lane.DefaultClock?.Name == null && lane.DefaultClock != null)
            {
                _anonymousClocks.Remove(lane.DefaultClock);
                _sampledClockSources.Remove(lane.DefaultClock);
            }
            lane.DefaultClock = null; lane.PendingClock = null;
            lane.ClockAnchor = null; lane.PendingClockStep = null;
        }

        private void CleanupSessionClocks()
        {
            _clocks.Clear(); _anonymousClocks.Clear();
            _clockSourcePositions.Clear(); _sampledClockSources.Clear(); _clockDiagnostics.Clear();
        }

        private int DispatchBeatTags(IReadOnlyList<TagData> tags, int line, int startIndex = 0)
        {
            var hostTags = new List<TagData>();
            LaneExecutionState? createdTrack = null;
            for (int index = startIndex; index < tags.Count; index++)
            {
                var tag = tags[index];
                var clock = ClockOperation.FromTag(tag, line);
                var track = TrackOperation.FromTag(tag, line);
                if (clock == null && track == null) { hostTags.Add(tag); continue; }
                if (hostTags.Count > 0) { DispatchTags(hostTags.ToArray(), line); hostTags.Clear(); }
                Trace(ExecutionTraceKind.Tag, tag.ToString(), line);
                if (clock != null) ApplyClockOperation(clock, line);
                else createdTrack = ApplyTrackOperation(track!, line, createdTrack);
                if (_lane.Status == ExecutionStatus.WaitingForTrack) return index + 1;
            }
            if (hostTags.Count > 0) DispatchTags(hostTags.ToArray(), line);
            return tags.Count;
        }

        private static bool IsClockOperationTag(TagData tag) =>
            string.Equals(tag.Name, "clock", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tag.Name, "defaultClock", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tag.Name, "resetClock", StringComparison.OrdinalIgnoreCase);

        private bool TryWaitForClock(StepNode step)
        {
            var anchor = ClockAnchor.FromTags(step.Tags, step.LineNumber);
            if (anchor == null) return false;
            var clock = FindClock(anchor.ClockName, step.LineNumber);
            var position = ReadClockPosition(clock, true);
            if (position.HasValue && position.Value >= anchor.Position) return false;
            _lane.ClockAnchor = anchor; _lane.PendingClockStep = step; _lane.PendingClock = clock;
            CurrentPresentationId = ++_presentationCounter;
            Status = ExecutionStatus.WaitingForClock;
            return true;
        }

        private bool ReleaseClockGates()
        {
            bool released = false;
            foreach (var lane in _lanes.ToArray())
            {
                if (lane.Status != ExecutionStatus.WaitingForClock || lane.ClockAnchor == null || lane.PendingClockStep == null || lane.PendingClock == null) continue;
                _lane = lane;
                var position = ReadClockPosition(lane.PendingClock, true);
                if (!position.HasValue || position.Value < lane.ClockAnchor.Position) continue;
                var step = lane.PendingClockStep;
                lane.PendingClockStep = null; lane.ClockAnchor = null; lane.PendingClock = null;
                released = true;
                if (!ProcessBeatStep(step)) _readyLanes.Enqueue(lane);
            }
            return released;
        }
    }
}

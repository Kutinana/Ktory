using System;
using System.Collections.Generic;

namespace Ktory.Core.Runtime
{
    /// <summary>
    /// Optional host presentation policy for the Main playhead only. Feed real frame deltas and
    /// printing notifications from the host. Automatic requests are processed in bounded FIFO
    /// batches, rather than letting one lane recursively advance the whole session.
    /// </summary>
    public sealed class MultiTrackPresentationController
    {
        private readonly KtorySequencer _sequencer;
        private readonly Dictionary<string, PresentationController> _controllers = new Dictionary<string, PresentationController>();
        private readonly Queue<PresentationToken> _pending = new Queue<PresentationToken>();
        private readonly HashSet<PresentationToken> _queued = new HashSet<PresentationToken>();
        private string _sessionId = string.Empty;
        private bool _processing;
        public int MaxAdvancesPerBatch { get; set; } = 64;
        public bool HasDeferredAdvance => _pending.Count > 0;
        public event Action? OnPresentationsChanged;
        public event Action<PresentationToken>? OnFastForwardRequested;

        public MultiTrackPresentationController(KtorySequencer sequencer)
        {
            _sequencer = sequencer ?? throw new ArgumentNullException(nameof(sequencer));
        }

        public PresentationController? GetController(string laneId) => _controllers.TryGetValue(laneId, out var controller) ? controller : null;

        /// <summary>Call after Start or a host-owned choice/clock operation; unchanged lanes retain their timers.</summary>
        public void Synchronize()
        {
            if (SynchronizeCore()) OnPresentationsChanged?.Invoke();
            Drain();
        }

        private bool SynchronizeCore()
        {
            bool changed = false;
            if (_sessionId != _sequencer.CurrentSessionId)
            {
                changed = true;
                _controllers.Clear();
                _pending.Clear();
                _queued.Clear();
                _sessionId = _sequencer.CurrentSessionId;
            }
            var active = _sequencer.GetActivePresentations();
            var remaining = new HashSet<string>(_controllers.Keys);
            // Only Main receives a printing/input policy. Auxiliary execution never waits for this controller.
            foreach (var presentation in active)
            {
                if (!presentation.IsMain) continue;
                remaining.Remove(presentation.LaneId);
                if (!_controllers.TryGetValue(presentation.LaneId, out var controller))
                {
                    controller = new PresentationController(_sequencer, presentation.LaneId) { AutoStepSequencer = false };
                    controller.OnAdvanceRequestedWithToken += Enqueue;
                    var captured = controller;
                    controller.OnFastForwardRequested += () => OnFastForwardRequested?.Invoke(captured.CurrentPresentationToken);
                    _controllers.Add(presentation.LaneId, controller);
                }
                if (controller.CurrentPresentationToken != presentation.Token)
                {
                    changed = true;
                    controller.SetupForCurrentBeat();
                }
            }
            foreach (var laneId in remaining) _controllers.Remove(laneId);
            return changed || remaining.Count > 0;
        }

        private void Enqueue(PresentationToken token)
        {
            if (_queued.Add(token)) _pending.Enqueue(token);
        }

        public void Update(double deltaTime)
        {
            if (double.IsNaN(deltaTime) || double.IsInfinity(deltaTime) || deltaTime < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            // Existing presentations each receive this delta once; newly released/created beats start at zero.
            var active = _sequencer.GetActivePresentations();
            if (!string.IsNullOrEmpty(_sequencer.CurrentSessionId) && _sequencer.Status != ExecutionStatus.Completed && _sequencer.Status != ExecutionStatus.Error)
                _sequencer.UpdateClocks(deltaTime, _sequencer.CurrentSessionId);
            if (SynchronizeCore()) OnPresentationsChanged?.Invoke();
            foreach (var presentation in active)
                if (_controllers.TryGetValue(presentation.LaneId, out var controller)) controller.Update(deltaTime, presentation.Token);
            Drain();
        }

        public void NotifyPrintingFinished(PresentationToken token)
        {
            var presentation = _sequencer.GetPresentation(token);
            if (presentation == null || !_controllers.TryGetValue(presentation.LaneId, out var controller))
            {
                _sequencer.TraceIgnoredInput(nameof(NotifyPrintingFinished), token);
                return;
            }
            if (controller.CurrentPresentationToken != token || controller.Phase != PresentationPhase.Printing) return;
            controller.NotifyPrintingFinished(token);
            Drain();
        }

        public void HandleUserClick(PresentationToken token)
        {
            var presentation = _sequencer.GetPresentation(token);
            if (presentation == null || !_controllers.TryGetValue(presentation.LaneId, out var controller))
            {
                _sequencer.TraceIgnoredInput(nameof(HandleUserClick), token);
                return;
            }
            if (controller.CurrentPresentationToken != token || !controller.CanHandleUserClick) return;
            controller.HandleUserClick(token);
            Drain();
        }

        public void RefreshLanguage(bool secondaryOnly = false, bool completePrintingOnLanguageSwitch = true)
        {
            foreach (var presentation in _sequencer.GetActivePresentations())
            {
                if (!_controllers.TryGetValue(presentation.LaneId, out var controller)) continue;
                if (secondaryOnly) controller.RefreshSecondaryLanguage();
                else controller.RefreshLanguage(completePrintingOnLanguageSwitch);
            }
            OnPresentationsChanged?.Invoke();
        }

        private void Drain()
        {
            if (_processing) return;
            if (MaxAdvancesPerBatch <= 0) throw new ArgumentOutOfRangeException(nameof(MaxAdvancesPerBatch));
            _processing = true;
            try
            {
                int processed = 0;
                while (_pending.Count > 0 && processed++ < MaxAdvancesPerBatch)
                {
                    var token = _pending.Dequeue();
                    _queued.Remove(token);
                    if (_sequencer.GetPresentation(token) == null) continue;
                    _sequencer.Step(token);
                    SynchronizeCore();
                    OnPresentationsChanged?.Invoke();
                    if (token.SessionId != _sequencer.CurrentSessionId) break;
                }
            }
            finally { _processing = false; }
        }
    }
}

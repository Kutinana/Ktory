using System;
using Ktory.Core.Ast;

namespace Ktory.Core.Runtime
{
    public enum PresentationPhase
    {
        Idle,
        Printing,   // Typing / fading in text
        Holding,    // Display complete, holding before next advance
        Completed   // Ready for next Step
    }

    /// <summary>
    /// Standard presentation timing controller complying with Ktory Specification §2.6.
    /// Manages .skippable, .next(t), and .wait(t) logic for both standalone runner and Unity host.
    /// </summary>
    public class PresentationController
    {
        private readonly KtorySequencer _sequencer;

        public PresentationPhase Phase { get; private set; } = PresentationPhase.Idle;
        public double ElapsedInPhase { get; private set; }
        public bool QueuedAdvance { get; private set; }

        public bool CanFastForward { get; private set; } = true;
        public double FastForwardLockDuration { get; private set; } = 0;

        public double HoldDuration { get; private set; } = 0;
        public bool AutoAdvanceOnHoldEnd { get; private set; } = false;
        public bool AllowClickInterruptHold { get; private set; } = true;

        /// <summary>
        /// Maximum number of immediate auto-advances processed in a single batch/frame.
        /// Prevents infinite zero-second directive loops (#do.loop.next) from blocking the host main thread.
        /// When exceeded in a single batch, remaining advances are deferred to the next Update() tick.
        /// Default is 64.
        /// </summary>
        public int MaxAutoAdvancesPerBatch { get; set; } = 64;

        /// <summary>
        /// True if the last batch of auto-advances hit MaxAutoAdvancesPerBatch and has more advances deferred to next Update().
        /// </summary>
        public bool HasDeferredAdvance => _hasDeferredAdvance;

        /// <summary>
        /// If true (default), PresentationController advances the sequencer automatically upon advance.
        /// If false, PresentationController only emits OnAdvanceRequested, and the host is responsible
        /// for invoking sequencer.Step() and calling SetupForCurrentBeat().
        /// </summary>
        public bool AutoStepSequencer { get; set; } = true;

        private int _pendingAdvances = 0;
        private bool _isProcessingAdvances = false;
        private bool _hasDeferredAdvance = false;

        /// <summary>
        /// Fired when an advance is requested (by user click, timer end, or auto advance).
        /// Note: When AutoStepSequencer is true (default), the controller will advance the sequencer automatically;
        /// subscribers must not call sequencer.Step() directly.
        /// When AutoStepSequencer is false, the host must queue and execute sequencer.Step().
        /// </summary>
        public event Action? OnAdvanceRequested;

        /// <summary>
        /// Fired after a beat transition has successfully advanced the sequencer and updated beat presentation state.
        /// </summary>
        public event Action? OnBeatChanged;

        /// <summary>
        /// Fired when fast-forward is triggered during the printing phase.
        /// UI should instantly reveal the full text.
        /// Note: The controller handles transition to the holding phase; subscribers do not need to call NotifyPrintingFinished().
        /// </summary>
        public event Action? OnFastForwardRequested;

        /// <summary>
        /// Fired when presentation language has been refreshed on the active beat via RefreshLanguage().
        /// </summary>
        public event Action? OnLanguageRefreshed;

        /// <summary>
        /// Fired when an auto-advance batch hits MaxAutoAdvancesPerBatch to report possible runaway zero-second directive loops.
        /// </summary>
        public event Action<int>? OnBatchAdvanceLimitReached;

        public PresentationController(KtorySequencer sequencer)
        {
            _sequencer = sequencer ?? throw new ArgumentNullException(nameof(sequencer));
        }

        public void SetupForCurrentBeat()
        {
            ApplyBeatState();
            ProcessAdvances();
        }

        /// <summary>
        /// Refreshes the presentation state following a language change on the active beat (via sequencer.SetLanguage),
        /// preserving active hold timers or reading time progress without restarting the beat presentation.
        /// </summary>
        /// <param name="completePrintingOnLanguageSwitch">
        /// If true (default), switching while in Printing phase immediately finishes text printing and enters the hold phase.
        /// If false and currently Printing, preserves elapsed printing progress and adjusts fast-forward duration if needed.
        /// </param>
        public void RefreshLanguage(bool completePrintingOnLanguageSwitch = true)
        {
            if (_sequencer.Status != ExecutionStatus.SuspendedAtBeat || _sequencer.CurrentPayload == null)
            {
                return;
            }

            var payload = _sequencer.CurrentPayload;

            // Directives have no localized text; preserve hold state
            if (payload.StepType == StepType.Directive)
            {
                OnLanguageRefreshed?.Invoke();
                return;
            }

            if (Phase == PresentationPhase.Printing)
            {
                if (completePrintingOnLanguageSwitch)
                {
                    Phase = PresentationPhase.Holding;
                    ElapsedInPhase = 0;
                    SetupHoldPhase(payload);
                }
            }
            else if (Phase == PresentationPhase.Holding)
            {
                var nextTag = FindTag(payload, "next");
                var waitTag = FindTag(payload, "wait");
                var autoPolicy = _sequencer.ActiveAutoPolicy;

                bool isEstimated = (waitTag != null && waitTag.PositionalArgs.Count == 0) ||
                                   (waitTag == null && nextTag == null && autoPolicy != null && autoPolicy.Enabled && autoPolicy.UseEstimatedReadingTime);

                if (isEstimated)
                {
                    double newDuration = EstimateReadingTime(payload.Content, payload.ActualLanguage);
                    double progressRatio = HoldDuration > 0 ? Math.Min(0.95, ElapsedInPhase / HoldDuration) : 0;
                    HoldDuration = Math.Max(0.5, newDuration);
                    ElapsedInPhase = progressRatio * HoldDuration;
                }
            }

            OnLanguageRefreshed?.Invoke();
        }

        public void NotifyPrintingFinished()
        {
            if (Phase != PresentationPhase.Printing) return;

            ElapsedInPhase = 0;
            if (_sequencer.CurrentPayload != null)
            {
                SetupHoldPhase(_sequencer.CurrentPayload);
            }
            else
            {
                Phase = PresentationPhase.Holding;
            }

            // If a click was queued during printing or wait, advance now if allowed,
            // or if auto-advance is active with zero hold duration (e.g. standard #AUTO).
            if (QueuedAdvance || (AutoAdvanceOnHoldEnd && HoldDuration <= 0))
            {
                RequestAdvance();
            }
        }

        public void HandleUserClick()
        {
            if (_sequencer.Status == ExecutionStatus.AwaitingChoice)
            {
                return; // Choices require specific item clicks
            }

            if (Phase == PresentationPhase.Printing)
            {
                // In printing phase: check if fast-forward is allowed
                if (CanFastForward && ElapsedInPhase >= FastForwardLockDuration)
                {
                    long startingPresId = _sequencer.CurrentPresentationId;
                    OnFastForwardRequested?.Invoke();

                    // Guard against duplicate notification: if the subscriber's UI callback already
                    // called NotifyPrintingFinished() and advanced to a new beat, do not notify again on the new beat.
                    if (Phase == PresentationPhase.Printing && _sequencer.CurrentPresentationId == startingPresId)
                    {
                        NotifyPrintingFinished();
                    }
                }
                // If not allowed, click is ignored
                return;
            }

            if (Phase == PresentationPhase.Holding)
            {
                if (AllowClickInterruptHold)
                {
                    // .next(t) / AUTO: click immediately triggers advance and cancels remaining hold timer
                    RequestAdvance();
                }
                else
                {
                    // .wait(t): queue advance until hold time expires
                    QueuedAdvance = true;
                }
            }
        }

        public void Update(double deltaTime)
        {
            if (_hasDeferredAdvance)
            {
                _hasDeferredAdvance = false;
                ProcessAdvances();
                return;
            }

            ElapsedInPhase += deltaTime;

            if (Phase == PresentationPhase.Printing)
            {
                // Check if skippable lock expired
                if (!CanFastForward && ElapsedInPhase >= FastForwardLockDuration)
                {
                    CanFastForward = true;
                }
                return;
            }

            if (Phase == PresentationPhase.Holding)
            {
                if (AutoAdvanceOnHoldEnd && ElapsedInPhase >= HoldDuration)
                {
                    RequestAdvance();
                }
            }
        }

        /// <summary>
        /// Requests an advance to the next beat. Uses iterative batch processing without recursion.
        /// </summary>
        public void RequestAdvance()
        {
            _pendingAdvances++;
            ProcessAdvances();
        }

        private void ApplyBeatState()
        {
            ElapsedInPhase = 0;
            QueuedAdvance = false;

            if (_sequencer.Status != ExecutionStatus.SuspendedAtBeat || _sequencer.CurrentPayload == null)
            {
                Phase = PresentationPhase.Idle;
                return;
            }

            var payload = _sequencer.CurrentPayload;

            // Directives do not print text, they go straight to holding or advance
            if (payload.StepType == StepType.Directive)
            {
                SetupHoldPhase(payload);
                return;
            }

            // Text step: start in Printing phase
            Phase = PresentationPhase.Printing;

            // Check .skippable(bool, [time])
            var skippableTag = FindTag(payload, "skippable");
            if (skippableTag != null)
            {
                if (skippableTag.PositionalArgs.Count > 0 && skippableTag.GetPositional<bool>(0) == false)
                {
                    CanFastForward = false;
                    FastForwardLockDuration = skippableTag.PositionalArgs.Count > 1 
                        ? skippableTag.GetPositional<double>(1, 0) 
                        : double.MaxValue;
                }
                else
                {
                    CanFastForward = true;
                    FastForwardLockDuration = 0;
                }
            }
            else
            {
                CanFastForward = true;
                FastForwardLockDuration = 0;
            }
        }

        private bool ShouldAutoAdvanceImmediately()
        {
            if (_sequencer.Status != ExecutionStatus.SuspendedAtBeat)
            {
                return false;
            }

            return Phase == PresentationPhase.Holding && AutoAdvanceOnHoldEnd && HoldDuration <= 0;
        }

        private void ProcessAdvances()
        {
            if (_isProcessingAdvances) return;
            _isProcessingAdvances = true;
            _hasDeferredAdvance = false;

            try
            {
                int advancesThisBatch = 0;

                while (_sequencer.Status == ExecutionStatus.SuspendedAtBeat)
                {
                    bool shouldAdvance = (_pendingAdvances > 0) || ShouldAutoAdvanceImmediately();
                    if (!shouldAdvance)
                    {
                        break;
                    }

                    if (advancesThisBatch >= MaxAutoAdvancesPerBatch)
                    {
                        _hasDeferredAdvance = true;
                        OnBatchAdvanceLimitReached?.Invoke(advancesThisBatch);
                        break;
                    }

                    if (_pendingAdvances > 0)
                    {
                        _pendingAdvances--;
                    }

                    Phase = PresentationPhase.Completed;
                    QueuedAdvance = false;

                    long presIdBefore = _sequencer.CurrentPresentationId;
                    OnAdvanceRequested?.Invoke();

                    if (AutoStepSequencer)
                    {
                        // If a subscriber to OnAdvanceRequested already invoked sequencer.Step(),
                        // CurrentPresentationId will have changed. Do not step twice!
                        if (_sequencer.CurrentPresentationId == presIdBefore && _sequencer.Status == ExecutionStatus.SuspendedAtBeat)
                        {
                            _sequencer.Step();
                        }
                        advancesThisBatch++;

                        ApplyBeatState();
                        OnBeatChanged?.Invoke();
                    }
                    else
                    {
                        // Host-managed advance mode: controller emitted OnAdvanceRequested and leaves stepping to host.
                        break;
                    }
                }

                if (_sequencer.Status != ExecutionStatus.SuspendedAtBeat)
                {
                    _pendingAdvances = 0;
                }
            }
            finally
            {
                _isProcessingAdvances = false;
            }
        }

        private void SetupHoldPhase(TextPayload payload)
        {
            Phase = PresentationPhase.Holding;
            ElapsedInPhase = 0;

            var nextTag = FindTag(payload, "next");
            var waitTag = FindTag(payload, "wait");

            if (nextTag != null)
            {
                AutoAdvanceOnHoldEnd = true;
                AllowClickInterruptHold = true;
                HoldDuration = nextTag.PositionalArgs.Count > 0 ? nextTag.GetPositional<double>(0, 0) : 0;
            }
            else if (waitTag != null)
            {
                AutoAdvanceOnHoldEnd = true;
                AllowClickInterruptHold = false; // queued advance only
                if (waitTag.PositionalArgs.Count > 0)
                {
                    HoldDuration = waitTag.GetPositional<double>(0, 0);
                }
                else
                {
                    // Estimate reading time from text length
                    HoldDuration = EstimateReadingTime(payload.Content, payload.ActualLanguage);
                }
            }
            else if (_sequencer.ActiveAutoPolicy != null && _sequencer.ActiveAutoPolicy.Enabled)
            {
                var autoPolicy = _sequencer.ActiveAutoPolicy;
                AutoAdvanceOnHoldEnd = true;
                AllowClickInterruptHold = true; // In AUTO mode, clicks can advance immediately

                if (autoPolicy.UseEstimatedReadingTime && payload.StepType == StepType.Text)
                {
                    HoldDuration = EstimateReadingTime(payload.Content, payload.ActualLanguage);
                }
                else
                {
                    HoldDuration = autoPolicy.DefaultWaitSeconds;
                }
            }
            else
            {
                // Standard mode: wait indefinitely for user input
                AutoAdvanceOnHoldEnd = false;
                AllowClickInterruptHold = true;
                HoldDuration = 0;
            }
        }

        private static double EstimateReadingTime(string text, string language)
        {
            if (string.IsNullOrEmpty(text)) return 1.0;
            // Chinese/Japanese: ~6-8 chars per second; English: ~3-4 words per second
            if (string.Equals(language, "zh", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(language, "ja", StringComparison.OrdinalIgnoreCase))
            {
                return Math.Max(1.0, text.Length / 7.0);
            }
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            return Math.Max(1.0, words / 3.5);
        }

        private static TagData? FindTag(TextPayload payload, string name)
        {
            foreach (var t in payload.Tags)
            {
                if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                    return t;
            }
            return null;
        }
    }
}

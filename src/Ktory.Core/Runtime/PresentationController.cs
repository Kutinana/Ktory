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

        private int _pendingAdvances = 0;
        private bool _isProcessingAdvances = false;
        private bool _hasDeferredAdvance = false;

        public event Action? OnAdvanceRequested;
        public event Action? OnFastForwardRequested;

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
                    OnFastForwardRequested?.Invoke();
                    NotifyPrintingFinished();
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
                    OnAdvanceRequested?.Invoke();

                    _sequencer.Step();
                    advancesThisBatch++;

                    ApplyBeatState();
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

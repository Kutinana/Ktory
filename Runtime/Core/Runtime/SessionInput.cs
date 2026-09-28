using System;

namespace Ktory.Core.Runtime
{
    public partial class KtorySequencer
    {
        public string CurrentSessionId { get; private set; } = string.Empty;
        public PresentationToken CurrentPresentationToken => new PresentationToken(CurrentSessionId, CurrentPresentationId);

        private void BeginSession() => CurrentSessionId = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Stops this session without entering nodes or dispatching tags. Call before replacing or
        /// closing a session so callbacks that still hold this instance cannot execute old story work.
        /// Repeated calls are harmless; Start establishes a new session with a different token.
        /// </summary>
        public void InvalidateSession()
        {
            CurrentSessionId = string.Empty;
            CurrentPresentationId = 0;
            CurrentPayload = null;
            CurrentChoice = null;
            ActiveAutoPolicy = null;
            _currentStep = null;
            CallStack.Clear();
            _activeLoops.Clear();
            VisitedItemIds.Clear();
            Status = ExecutionStatus.Completed;
        }

        /// <summary>Session-aware input. Invalid or expired tokens produce diagnostics only.</summary>
        public void Step(PresentationToken expected)
        {
            if (AcceptInput(expected, nameof(Step))) Step(expected.PresentationId);
        }

        public void SubmitChoice(string choiceIdentifier, PresentationToken expected)
        {
            if (AcceptInput(expected, nameof(SubmitChoice))) SubmitChoice(choiceIdentifier, expected.PresentationId);
        }

        public void Break(PresentationToken expected)
        {
            if (AcceptInput(expected, nameof(Break))) Break();
        }

        public void SetLanguage(string requestedLocale, PresentationToken expected)
        {
            // Language is a session preference, not a request to advance a particular beat.
            // A queued language change remains valid after a same-session step or natural completion.
            if (!string.IsNullOrEmpty(expected.SessionId) &&
                string.Equals(expected.SessionId, CurrentSessionId, StringComparison.Ordinal) &&
                Status != ExecutionStatus.Error)
            {
                SetLanguage(requestedLocale);
                return;
            }
            TraceIgnoredInput(nameof(SetLanguage), expected);
        }

        internal bool MatchesCurrentPresentation(PresentationToken expected) =>
            expected.PresentationId > 0 && !string.IsNullOrEmpty(expected.SessionId) &&
            expected == CurrentPresentationToken &&
            (Status == ExecutionStatus.SuspendedAtBeat || Status == ExecutionStatus.AwaitingChoice);

        internal void TraceIgnoredInput(string operation, PresentationToken expected)
        {
            Trace(ExecutionTraceKind.InputIgnored,
                $"Ignored {operation} for presentation {expected}; current presentation is {CurrentPresentationToken} ({Status}).");
        }

        private bool AcceptInput(PresentationToken expected, string operation)
        {
            if (MatchesCurrentPresentation(expected)) return true;
            TraceIgnoredInput(operation, expected);
            return false;
        }
    }
}

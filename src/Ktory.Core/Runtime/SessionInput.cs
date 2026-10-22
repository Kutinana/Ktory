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
            EnsureNotExecuting();
            ResetExecutionLanes();
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
            if (AcceptInput(expected, nameof(Break))) ExecuteInput(expected.PresentationId, BreakCore);
        }

        public void SetLanguage(string? requestedLocale, PresentationToken expected)
        {
            if (AcceptLanguageInput(expected, nameof(SetLanguage))) SetLanguage(requestedLocale);
        }

        public void SetSecondaryLanguage(string? requestedLocale, PresentationToken expected)
        {
            if (AcceptLanguageInput(expected, nameof(SetSecondaryLanguage))) SetSecondaryLanguage(requestedLocale);
        }

        public void SetLanguageOutput(string? primaryLocale, string? secondaryLocale, PresentationToken expected)
        {
            if (AcceptLanguageInput(expected, nameof(SetLanguageOutput))) SetLanguageOutput(primaryLocale, secondaryLocale);
        }

        private bool AcceptLanguageInput(PresentationToken expected, string operation)
        {
            // Language is a session preference, not a request to advance a particular beat.
            // A queued language change remains valid after a same-session step or natural completion.
            if (!string.IsNullOrEmpty(expected.SessionId) &&
                string.Equals(expected.SessionId, CurrentSessionId, StringComparison.Ordinal) &&
                Status != ExecutionStatus.Error)
            {
                return true;
            }
            TraceIgnoredInput(operation, expected);
            return false;
        }

        internal bool MatchesCurrentPresentation(PresentationToken expected) => FindPresentationLane(expected) != null;

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

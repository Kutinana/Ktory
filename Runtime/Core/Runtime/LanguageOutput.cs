using System;
using Ktory.Core.Ast;

namespace Ktory.Core.Runtime
{
    public partial class KtorySequencer
    {
        /// <summary>Optional additional output locale. Null disables secondary output.</summary>
        public string? SecondaryLanguage { get; private set; }

        /// <summary>Changes the primary locale while preserving the secondary configuration.</summary>
        public void SetLanguage(string? requestedLocale) => SetLanguageOutput(requestedLocale, SecondaryLanguage);

        /// <summary>
        /// Refreshes only localized values. Hosts must preserve printing and timing state when
        /// only this preference changes; secondary output never contributes to reading estimates.
        /// </summary>
        public void SetSecondaryLanguage(string? requestedLocale) => SetLanguageOutput(RequestedLanguage, requestedLocale);

        /// <summary>Atomically updates language preferences without executing story instructions.</summary>
        public void SetLanguageOutput(string? primaryLocale, string? secondaryLocale)
        {
            EnsureNotExecuting();
            try
            {
                var primary = primaryLocale ?? DefaultLanguage ?? "zh";
                var secondary = NormalizeSecondaryLanguage(secondaryLocale);
                if (string.Equals(RequestedLanguage, primary, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(SecondaryLanguage, secondary, StringComparison.OrdinalIgnoreCase)) return;

                RequestedLanguage = primary;
                SecondaryLanguage = secondary;
                RefreshAllLanguageOutputs();
            }
            catch (Exception error) { TraceError(error); throw; }
        }

        private static string? NormalizeSecondaryLanguage(string? locale) => string.IsNullOrWhiteSpace(locale) ? null : locale;

        private void RefreshCurrentLanguageOutput()
        {
            if (Status == ExecutionStatus.SuspendedAtBeat && _currentStep is TextStep textStep && CurrentPayload != null)
            {
                RefreshTextPayload(textStep, CurrentPayload);
            }
            else if (Status == ExecutionStatus.AwaitingChoice && _currentStep is ContainerStep container && CurrentChoice != null)
            {
                // Language refresh must not re-evaluate host conditions or alter the current menu.
                // Preserve payload/option identities, consumption, and selectability as well as the token.
                for (int i = 0; i < container.Items.Count; i++)
                    RefreshChoiceOptionLanguage(container.Items[i], CurrentChoice.Options[i]);
            }
        }
    }
}

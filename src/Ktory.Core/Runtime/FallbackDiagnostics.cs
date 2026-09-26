using System;
using System.Collections.Generic;
using Ktory.Core.Ast;

namespace Ktory.Core.Runtime
{
    public partial class KtorySequencer
    {
        // Refreshing the same presentation may discover a new missing locale, but does not
        // repeat a warning already reported for that presentation and locale combination.
        private readonly HashSet<string> _reportedFallbackWarnings = new HashSet<string>(StringComparer.Ordinal);

        private void WarnFallbackOnce(string message, int line)
        {
            if (_reportedFallbackWarnings.Add(line + ":" + message))
                Trace(ExecutionTraceKind.Warning, message, line);
        }

        private void WarnMissingTranslations(string subject, int line, string actualLocale)
        {
            WarnFallbackOnce(
                $"{subject} has no translation for requested language '{RequestedLanguage}' or default language '{DefaultLanguage}'; using available language '{actualLocale}'.",
                line);
        }

        private void RefreshTextPayload(TextStep step, TextPayload payload)
        {
            payload.Content = step.GetText(RequestedLanguage, DefaultLanguage, out var actualLocale, out var textFallback);
            payload.ActualLanguage = actualLocale;
            payload.RequestedLanguage = RequestedLanguage;
            if (textFallback) WarnMissingTranslations("Text", step.LineNumber, actualLocale);

            payload.SpeakerActualLanguage = null;
            if (!string.IsNullOrEmpty(step.Speaker) && File.SpeakersByAlias.TryGetValue(step.Speaker, out var speaker))
            {
                payload.Speaker = speaker.GetDisplayName(RequestedLanguage, DefaultLanguage, out var nameLocale, out var nameFallback);
                payload.SpeakerActualLanguage = nameLocale;
                if (nameFallback)
                    WarnMissingTranslations($"Speaker '{step.Speaker}' (declared at line {speaker.LineNumber})", step.LineNumber, nameLocale!);
            }
            else
            {
                payload.Speaker = File.ResolveSpeaker(step.Speaker, RequestedLanguage, DefaultLanguage);
            }
        }

        private bool EvaluateGuard(string expression, int line)
        {
            if (Evaluator is DefaultExpressionEvaluator standard)
            {
                if (standard.TryEvaluateCondition(expression, out var result)) return result;
                if (standard.IgnoreUnknownConditions) return true;
                WarnFallbackOnce($"Unknown condition '{expression}' is treated as false by the game evaluator. Provide a host evaluator or define the condition.", line);
                return false;
            }

            // A custom evaluator owns the meaning of its bool result; false is not a diagnosis.
            return Evaluator.EvaluateCondition(expression);
        }
    }
}

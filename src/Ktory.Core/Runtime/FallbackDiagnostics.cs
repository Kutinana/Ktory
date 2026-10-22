using System;
using System.Collections.Generic;
using Ktory.Core.Ast;

namespace Ktory.Core.Runtime
{
    public partial class KtorySequencer
    {
        // Refreshing the same presentation may discover a new missing locale, but does not
        // repeat a warning already reported for that presentation and locale combination.
        private HashSet<string> _reportedFallbackWarnings => _lane.FallbackWarnings;

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
            var content = step.GetText(RequestedLanguage, DefaultLanguage, out var actualLocale, out var textFallback);
            payload.Text = ResolveOutput(step.TextVariants, content, actualLocale, step.TextVariantSegments);
            if (textFallback) WarnMissingTranslations("Text", step.LineNumber, actualLocale ?? string.Empty);

            if (!string.IsNullOrEmpty(step.Speaker) && File.SpeakersByAlias.TryGetValue(step.Speaker, out var speaker))
            {
                var name = speaker.GetDisplayName(RequestedLanguage, DefaultLanguage, out var nameLocale, out var nameFallback);
                payload.SpeakerName = ResolveOutput(speaker.DisplayNames, name, nameLocale);
                payload.SpeakerId = speaker.Id ?? step.Speaker;
                if (nameFallback)
                    WarnMissingTranslations($"Speaker '{step.Speaker}' (declared at line {speaker.LineNumber})", step.LineNumber, nameLocale!);
            }
            else
            {
                var name = File.ResolveSpeaker(step.Speaker, RequestedLanguage, DefaultLanguage);
                payload.SpeakerId = step.Speaker;
                payload.SpeakerName = name == null ? null : new LocalizedTextOutput
                {
                    Primary = new ResolvedLocalizedValue { Content = name, RequestedLanguage = RequestedLanguage }
                };
            }
        }

        private void RefreshChoiceOptionLanguage(ContainerItem item, ChoiceOption option)
        {
            var label = item.GetLabel(RequestedLanguage, DefaultLanguage, out var actualLocale, out var fallback);
            option.Text = ResolveOutput(item.LabelVariants, label, actualLocale, item.LabelVariantSegments);
            if (fallback) WarnMissingTranslations("Choice label", item.LineNumber, actualLocale ?? string.Empty);
        }

        private LocalizedTextOutput ResolveOutput(Dictionary<string, string> variants, string content, string? actualLocale,
            Dictionary<string, IReadOnlyList<TextSegment>>? segments = null)
        {
            LocalizedValueSelector.Select(variants, RequestedLanguage, DefaultLanguage, out _, out _, out _, out var key);
            return new LocalizedTextOutput
            {
                Primary = new ResolvedLocalizedValue
                {
                    Content = content,
                    RequestedLanguage = RequestedLanguage,
                    ActualLanguage = actualLocale,
                    Segments = key != null && segments != null && segments.TryGetValue(key, out var pieces) ? pieces : null
                },
                Secondary = ResolveSecondary(variants, actualLocale, segments)
            };
        }

        private ResolvedLocalizedValue? ResolveSecondary(Dictionary<string, string> variants, string? primaryActualLocale,
            Dictionary<string, IReadOnlyList<TextSegment>>? segments)
        {
            var requested = SecondaryLanguage;
            if (requested == null || string.Equals(requested, primaryActualLocale, StringComparison.OrdinalIgnoreCase))
                return null;

            // Secondary output is an additional translation, never a fallback copy. A bare
            // value belongs to the file's default language only when one is defined.
            string key = requested;
            if (!variants.TryGetValue(key, out var content) || string.IsNullOrEmpty(content))
            {
                key = string.Empty;
                if (!(string.Equals(requested, DefaultLanguage, StringComparison.OrdinalIgnoreCase) &&
                      variants.TryGetValue(key, out content) && !string.IsNullOrEmpty(content))) return null;
            }

            return new ResolvedLocalizedValue
            {
                Content = content,
                RequestedLanguage = requested,
                ActualLanguage = requested,
                Segments = segments != null && segments.TryGetValue(key, out var pieces) ? pieces : null
            };
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

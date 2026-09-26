using System;
using System.Collections.Generic;

namespace Ktory.Core.Ast
{
    public class TextStep : StepNode
    {
        /// <summary>
        /// Speaker name or identifier. Null/empty represents anonymous narration.
        /// </summary>
        public string? Speaker { get; set; }

        /// <summary>
        /// Localized text variants mapped by locale code (e.g. "zh", "en", "ja").
        /// Content has been desugared (Markdown inline -> rich text, [base]{ruby} -> <ruby="ruby">base</ruby>).
        /// </summary>
        public Dictionary<string, string> TextVariants { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool IsNarration => string.IsNullOrWhiteSpace(Speaker);

        public string GetText(string requestedLocale, string defaultLocale, out string actualLocale)
        {
            if (TextVariants.TryGetValue(requestedLocale, out var text) && !string.IsNullOrEmpty(text))
            {
                actualLocale = requestedLocale;
                return text;
            }

            if (TextVariants.TryGetValue(defaultLocale, out var defaultText) && !string.IsNullOrEmpty(defaultText))
            {
                actualLocale = defaultLocale;
                return defaultText;
            }

            // If neither matches, pick the first available variant if any
            foreach (var kvp in TextVariants)
            {
                if (!string.IsNullOrEmpty(kvp.Value))
                {
                    actualLocale = kvp.Key;
                    return kvp.Value;
                }
            }

            actualLocale = defaultLocale;
            return string.Empty;
        }

        public override string ToString()
        {
            var sp = IsNarration ? ":" : $"{Speaker}:";
            return $"{sp} ({TextVariants.Count} variants) [{Tags.Count} tags]";
        }
    }
}

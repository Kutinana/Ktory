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

        public string GetText(string? requestedLocale, string? defaultLocale, out string? actualLocale)
            => GetText(requestedLocale, defaultLocale, out actualLocale, out _, out _);

        internal string GetText(string? requestedLocale, string? defaultLocale, out string? actualLocale, out bool usedAvailableFallback)
            => GetText(requestedLocale, defaultLocale, out actualLocale, out usedAvailableFallback, out _);

        internal string GetText(string? requestedLocale, string? defaultLocale, out string? actualLocale, out bool usedAvailableFallback, out bool usedUnlocalizedLiteral)
            => LocalizedValueSelector.Select(TextVariants, requestedLocale, defaultLocale, out actualLocale, out usedAvailableFallback, out usedUnlocalizedLiteral);

        public override string ToString()
        {
            var sp = IsNarration ? ":" : $"{Speaker}:";
            return $"{sp} ({TextVariants.Count} variants) [{Tags.Count} tags]";
        }
    }
}

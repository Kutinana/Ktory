using System.Collections.Generic;

namespace Ktory.Core.Ast
{
    internal static class LocalizedValueSelector
    {
        internal static string Select(
            Dictionary<string, string> variants,
            string requestedLocale,
            string defaultLocale,
            out string actualLocale,
            out bool usedAvailableFallback)
        {
            usedAvailableFallback = false;
            if (variants.TryGetValue(requestedLocale, out var requested) && !string.IsNullOrEmpty(requested))
            {
                actualLocale = requestedLocale;
                return requested;
            }

            if (variants.TryGetValue(defaultLocale, out var fallback) && !string.IsNullOrEmpty(fallback))
            {
                actualLocale = defaultLocale;
                return fallback;
            }

            // Keep the existing declaration/insertion order when both preferred locales are missing.
            foreach (var variant in variants)
            {
                if (!string.IsNullOrEmpty(variant.Value))
                {
                    actualLocale = variant.Key;
                    usedAvailableFallback = true;
                    return variant.Value;
                }
            }

            // Preserve the existing empty-content result; no translated value was selected.
            actualLocale = defaultLocale;
            return string.Empty;
        }
    }
}

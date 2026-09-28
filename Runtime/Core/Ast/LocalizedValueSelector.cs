using System.Collections.Generic;

namespace Ktory.Core.Ast
{
    internal static class LocalizedValueSelector
    {
        internal static string Select(
            Dictionary<string, string> variants,
            string? requestedLocale,
            string? defaultLocale,
            out string? actualLocale,
            out bool usedAvailableFallback)
            => Select(variants, requestedLocale, defaultLocale, out actualLocale, out usedAvailableFallback, out _);

        internal static string Select(
            Dictionary<string, string> variants,
            string? requestedLocale,
            string? defaultLocale,
            out string? actualLocale,
            out bool usedAvailableFallback,
            out bool usedUnlocalizedLiteral)
        {
            usedAvailableFallback = false;
            usedUnlocalizedLiteral = false;

            // 1. Requested locale exact match
            if (!string.IsNullOrEmpty(requestedLocale) &&
                variants.TryGetValue(requestedLocale, out var requested) &&
                !string.IsNullOrEmpty(requested))
            {
                actualLocale = requestedLocale;
                return requested;
            }

            // 2. Default locale exact match
            if (!string.IsNullOrEmpty(defaultLocale) &&
                variants.TryGetValue(defaultLocale, out var fallback) &&
                !string.IsNullOrEmpty(fallback))
            {
                actualLocale = defaultLocale;
                return fallback;
            }

            // 3. Unadorned / literal variant (stored under empty string key "")
            if (variants.TryGetValue("", out var unadorned) && !string.IsNullOrEmpty(unadorned))
            {
                if (!string.IsNullOrEmpty(defaultLocale))
                {
                    actualLocale = defaultLocale;
                    return unadorned;
                }
                else
                {
                    actualLocale = null;
                    usedUnlocalizedLiteral = !string.IsNullOrEmpty(requestedLocale);
                    return unadorned;
                }
            }

            // 4. Double-missing fallback: first available non-empty variant (excluding unadorned "")
            foreach (var variant in variants)
            {
                if (!string.IsNullOrEmpty(variant.Key) && !string.IsNullOrEmpty(variant.Value))
                {
                    actualLocale = variant.Key;
                    usedAvailableFallback = true;
                    return variant.Value;
                }
            }

            // 5. Preserve the existing empty-content result; no translated value was selected.
            actualLocale = defaultLocale;
            return string.Empty;
        }
    }
}

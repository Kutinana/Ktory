using System;
using System.Collections.Generic;

namespace Ktory.Core.Ast
{
    /// <summary>
    /// Represents a localized speaker entity definition with bidirectional alias resolution.
    /// </summary>
    public class SpeakerDefinition
    {
        /// <summary>
        /// Optional shorthand identifier or primary key (e.g. "alice").
        /// </summary>
        public string? Id { get; set; }

        public int LineNumber { get; set; }

        /// <summary>File-scoped defaults, expanded independently into each matching dialogue.</summary>
        public List<TagData> DefaultTags { get; } = new List<TagData>();

        /// <summary>
        /// Set of all valid alias tokens (including Id and localized names) that resolve to this speaker.
        /// Strictly case-sensitive.
        /// </summary>
        public HashSet<string> Aliases { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Localized display names mapped by locale code (e.g. "zh", "en", "ja").
        /// </summary>
        public Dictionary<string, string> DisplayNames { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string GetDisplayName(string requestedLocale, string defaultLocale)
            => GetDisplayName(requestedLocale, defaultLocale, out _, out _);

        public string GetDisplayName(string requestedLocale, string defaultLocale, out string? actualLocale)
            => GetDisplayName(requestedLocale, defaultLocale, out actualLocale, out _);

        internal string GetDisplayName(string requestedLocale, string defaultLocale, out string? actualLocale, out bool usedAvailableFallback)
        {
            var name = LocalizedValueSelector.Select(DisplayNames, requestedLocale, defaultLocale, out var selectedLocale, out usedAvailableFallback);
            actualLocale = string.IsNullOrEmpty(name) ? null : selectedLocale;
            return string.IsNullOrEmpty(name) ? Id ?? string.Empty : name;
        }

        public override string ToString()
        {
            var idPart = string.IsNullOrEmpty(Id) ? "" : $"{Id}: ";
            return $"@speaker {idPart}({DisplayNames.Count} locales, {Aliases.Count} aliases)";
        }
    }
}

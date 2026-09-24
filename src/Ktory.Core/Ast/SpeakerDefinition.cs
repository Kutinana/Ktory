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
        {
            if (DisplayNames.TryGetValue(requestedLocale, out var name) && !string.IsNullOrEmpty(name))
            {
                return name;
            }

            if (DisplayNames.TryGetValue(defaultLocale, out var defaultName) && !string.IsNullOrEmpty(defaultName))
            {
                return defaultName;
            }

            // Fallback to any available display name
            foreach (var kvp in DisplayNames)
            {
                if (!string.IsNullOrEmpty(kvp.Value))
                {
                    return kvp.Value;
                }
            }

            // Fallback to Id or empty
            return Id ?? string.Empty;
        }

        public override string ToString()
        {
            var idPart = string.IsNullOrEmpty(Id) ? "" : $"{Id}: ";
            return $"@speaker {idPart}({DisplayNames.Count} locales, {Aliases.Count} aliases)";
        }
    }
}

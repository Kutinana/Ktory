using System;
using System.Collections.Generic;
using Ktory.Core.Ast;

namespace Ktory.Core.Runtime
{
    public class TextPayload
    {
        public string StepId { get; set; } = Guid.NewGuid().ToString("N");
        public long PresentationId { get; set; }
        public StepType StepType { get; set; } = StepType.Text;
        public int LineNumber { get; set; }

        public string? Speaker { get; set; }

        /// <summary>
        /// The actual language of a declared speaker's localized display name.
        /// Null for narration, literal undeclared speakers, or an ID without a localized name.
        /// </summary>
        public string? SpeakerActualLanguage { get; set; }

        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// The actual language of the content (after fallback if requested was missing).
        /// Null for unlocalized content when script has no @defaultLang and no @locale.
        /// </summary>
        public string? ActualLanguage { get; set; }

        /// <summary>
        /// The locale that was originally requested by host/session.
        /// </summary>
        public string? RequestedLanguage { get; set; }

        public IReadOnlyList<TagData> Tags { get; set; } = Array.Empty<TagData>();

        public bool IsNarration => string.IsNullOrEmpty(Speaker);

        public AutoPolicyData? AutoPolicy { get; set; }
        public bool IsAuto => AutoPolicy != null && AutoPolicy.Enabled;

        public override string ToString()
        {
            if (StepType == StepType.Directive)
            {
                return $"[Directive #{Content}] ({Tags.Count} tags)";
            }
            var sp = IsNarration ? "Narration" : Speaker;
            var lang = ActualLanguage ?? "unlocalized";
            return $"[{lang}] {sp}: {Content}";
        }
    }
}

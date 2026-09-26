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
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// The actual language of the content (after fallback if requested was missing).
        /// </summary>
        public string ActualLanguage { get; set; } = string.Empty;

        /// <summary>
        /// The locale that was originally requested by host/session.
        /// </summary>
        public string RequestedLanguage { get; set; } = string.Empty;

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
            return $"[{ActualLanguage}] {sp}: {Content}";
        }
    }
}

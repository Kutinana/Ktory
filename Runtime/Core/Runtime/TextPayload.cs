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

        public LocalizedTextOutput Text { get; set; } = new LocalizedTextOutput();
        public LocalizedTextOutput? SpeakerName { get; set; }

        /// <summary>Stable declared speaker ID, or the source alias when no ID is declared.</summary>
        public string? SpeakerId { get; set; }

        /// <summary>Compatibility projection of the primary display name.</summary>
        public string? Speaker
        {
            get => SpeakerName?.Primary.Content;
            set
            {
                if (value == null) SpeakerName = null;
                else
                {
                    if (SpeakerName == null) SpeakerName = new LocalizedTextOutput();
                    SpeakerName.Primary.Content = value;
                }
            }
        }

        /// <summary>
        /// The actual language of a declared speaker's localized display name.
        /// Null for narration, literal undeclared speakers, or an ID without a localized name.
        /// </summary>
        public string? SpeakerActualLanguage
        {
            get => SpeakerName?.Primary.ActualLanguage;
            set
            {
                if (SpeakerName == null && value != null) SpeakerName = new LocalizedTextOutput();
                if (SpeakerName != null) SpeakerName.Primary.ActualLanguage = value;
            }
        }

        /// <summary>Compatibility projection of the primary content.</summary>
        public string Content { get => Text.Primary.Content; set => Text.Primary.Content = value; }

        /// <summary>
        /// The actual language of the content (after fallback if requested was missing).
        /// Null for unlocalized content when script has no @defaultLang and no @locale.
        /// </summary>
        public string? ActualLanguage { get => Text.Primary.ActualLanguage; set => Text.Primary.ActualLanguage = value; }

        /// <summary>
        /// The locale that was originally requested by host/session.
        /// </summary>
        public string? RequestedLanguage { get => Text.Primary.RequestedLanguage; set => Text.Primary.RequestedLanguage = value; }

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

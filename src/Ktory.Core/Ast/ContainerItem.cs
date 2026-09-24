using System;
using System.Collections.Generic;

namespace Ktory.Core.Ast
{
    public class ContainerItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public int LineNumber { get; set; }
        
        /// <summary>
        /// '*' for single-use item (consumed in session history); '+' for persistent repeatable item.
        /// </summary>
        public char Marker { get; set; } = '*';

        /// <summary>
        /// Display text variants mapped by locale code (e.g. "zh", "en").
        /// </summary>
        public Dictionary<string, string> LabelVariants { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Guard condition expression from ? {expr}
        /// </summary>
        public string? GuardCondition { get; set; }

        /// <summary>
        /// Tags dispatched immediately upon selection.
        /// </summary>
        public List<TagData> Tags { get; set; } = new List<TagData>();

        /// <summary>
        /// Explicit jump or call target if specified (-> Label or => Label or -> break).
        /// </summary>
        public ControlFlowStep? TargetJump { get; set; }

        /// <summary>
        /// Inline steps executed when selected (Case C inline convergence).
        /// </summary>
        public List<StepNode> InlineSteps { get; set; } = new List<StepNode>();

        public bool IsOneTime => Marker == '*';

        public string GetLabel(string requestedLocale, string defaultLocale)
        {
            if (LabelVariants.TryGetValue(requestedLocale, out var text) && !string.IsNullOrEmpty(text))
            {
                return text;
            }
            if (LabelVariants.TryGetValue(defaultLocale, out var defaultText) && !string.IsNullOrEmpty(defaultText))
            {
                return defaultText;
            }
            foreach (var kvp in LabelVariants)
            {
                if (!string.IsNullOrEmpty(kvp.Value)) return kvp.Value;
            }
            return string.Empty;
        }

        public override string ToString()
        {
            return $"{Marker} [{GetLabel("zh", "zh")}] [Tags: {Tags.Count}] [Inline: {InlineSteps.Count}]";
        }
    }
}

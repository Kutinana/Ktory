using System;
using System.Collections.Generic;
using Ktory.Core.Ast;

namespace Ktory.Core.Runtime
{
    public class ChoiceOption
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public char Marker { get; set; } = '*';
        public bool IsConsumed { get; set; }
        public bool CanSelect { get; set; } = true;
        public IReadOnlyList<TagData> Tags { get; set; } = Array.Empty<TagData>();

        public override string ToString()
        {
            var status = !CanSelect ? " [Disabled]" : (IsConsumed ? " [Visited]" : "");
            return $"{Marker} [{Label}]{status}";
        }
    }

    public class ChoicePayload
    {
        public string ContainerName { get; set; } = "choice";
        public bool IsLoop { get; set; }
        public IReadOnlyList<TagData> Tags { get; set; } = Array.Empty<TagData>();
        public IReadOnlyList<ChoiceOption> Options { get; set; } = Array.Empty<ChoiceOption>();

        public int SelectableCount
        {
            get
            {
                int count = 0;
                foreach (var opt in Options)
                {
                    if (opt.CanSelect) count++;
                }
                return count;
            }
        }
    }
}

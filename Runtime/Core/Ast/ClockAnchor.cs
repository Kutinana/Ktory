using System;
using System.Collections.Generic;

namespace Ktory.Core.Ast
{
    /// <summary>An entry threshold in source units. A null name selects the playhead default.</summary>
    public sealed class ClockAnchor
    {
        public string? ClockName { get; }
        public double Position { get; }
        private ClockAnchor(string? name, double position) { ClockName = name; Position = position; }

        internal static ClockAnchor? FromTags(IReadOnlyList<TagData> tags, int line)
        {
            ClockAnchor? result = null;
            foreach (var tag in tags)
            {
                if (!string.Equals(tag.Name, "at", StringComparison.OrdinalIgnoreCase)) continue;
                if (result != null) throw BuiltinArguments.Error(tag, line, "may occur only once on a beat");
                BuiltinArguments.Check(tag, line, "value", "clock");
                result = new ClockAnchor(BuiltinArguments.String(tag, "clock", line),
                    BuiltinArguments.Number(tag, "value", line, required: true)!.Value);
            }
            return result;
        }
    }
}

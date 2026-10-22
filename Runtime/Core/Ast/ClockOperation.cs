using System;
using System.Collections.Generic;

namespace Ktory.Core.Ast
{
    public enum ClockOperationKind { Declare, SelectDefault, Reset }

    /// <summary>Null names mean anonymous creation or the current playhead default.</summary>
    public sealed class ClockOperation
    {
        public ClockOperationKind Kind { get; }
        public string? Name { get; }
        public string? SourceName { get; }
        private ClockOperation(ClockOperationKind kind, string? name, string? sourceName)
        { Kind = kind; Name = name; SourceName = sourceName; }

        public static ClockOperation? FromTag(TagData tag, int line)
        {
            bool declare = string.Equals(tag.Name, "clock", StringComparison.OrdinalIgnoreCase);
            bool select = string.Equals(tag.Name, "defaultClock", StringComparison.OrdinalIgnoreCase);
            bool reset = string.Equals(tag.Name, "resetClock", StringComparison.OrdinalIgnoreCase);
            if (!declare && !select && !reset) return null;
            if (declare) BuiltinArguments.Check(tag, line, "name", "source");
            else BuiltinArguments.Check(tag, line, "name");
            var name = BuiltinArguments.String(tag, "name", line, required: select);
            var source = declare ? BuiltinArguments.String(tag, "source", line, allowEmpty: true) : null;
            if (source == string.Empty) source = null;
            return new ClockOperation(declare ? ClockOperationKind.Declare : select ? ClockOperationKind.SelectDefault : ClockOperationKind.Reset, name, source);
        }

        public static IReadOnlyList<ClockOperation> FromTags(IReadOnlyList<TagData> tags, int line)
        {
            if (tags == null) throw new ArgumentNullException(nameof(tags));
            var result = new List<ClockOperation>();
            foreach (var tag in tags)
            {
                var operation = FromTag(tag, line);
                if (operation != null) result.Add(operation);
            }
            return result.AsReadOnly();
        }
    }
}

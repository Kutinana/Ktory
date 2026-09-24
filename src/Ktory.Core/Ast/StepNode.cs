using System.Collections.Generic;

namespace Ktory.Core.Ast
{
    public abstract class StepNode
    {
        public int LineNumber { get; set; }
        public string? GuardCondition { get; set; }
        public List<TagData> Tags { get; set; } = new List<TagData>();

        public bool HasTag(string name)
        {
            return Tags.Exists(t => string.Equals(t.Name, name, System.StringComparison.OrdinalIgnoreCase));
        }

        public TagData? GetTag(string name)
        {
            return Tags.Find(t => string.Equals(t.Name, name, System.StringComparison.OrdinalIgnoreCase));
        }
    }
}

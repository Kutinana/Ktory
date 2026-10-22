using System.Collections.Generic;
using Ktory.Core.Runtime;

namespace Ktory.Core.Ast
{
    public abstract class StepNode
    {
        public int LineNumber { get; set; }
        public string? GuardCondition { get; set; }
        public List<TagData> Tags { get; set; } = new List<TagData>();
        public AutoPolicy? LexicalAutoPolicy { get; set; }

        public bool HasTag(string name)
        {
            return Tags.Exists(t => string.Equals(t.Name, name, System.StringComparison.OrdinalIgnoreCase));
        }

        public TagData? GetTag(string name)
        {
            return Tags.Find(t => string.Equals(t.Name, name, System.StringComparison.OrdinalIgnoreCase));
        }

        public bool IsLoop => HasTag("loop");

        public int GetLoopLimit()
        {
            var loopTag = GetTag("loop");
            if (loopTag == null) return 1;

            if (!loopTag.NamedArgs.ContainsKey("count"))
            {
                // .loop or .loop() without argument means infinite loop (-1 represents infinite)
                return -1;
            }

            var limit = loopTag.GetNamed<int>("count", 1);
            if (limit <= 1)
            {
                // Explicit counts <= 1 retain the existing execute-once behavior.
                return 1;
            }
            return limit;
        }
    }
}

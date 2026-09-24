using System;
using System.Collections.Generic;

namespace Ktory.Core.Ast
{
    public class ContainerStep : StepNode
    {
        public string Name { get; set; } = "choice";
        public List<ContainerItem> Items { get; set; } = new List<ContainerItem>();

        public bool IsLoop => HasTag("loop");

        public int GetLoopLimit()
        {
            var loopTag = GetTag("loop");
            if (loopTag == null) return 1;

            if (loopTag.PositionalArgs.Count == 0)
            {
                // .loop or .loop() without argument means infinite loop (0 represents infinite)
                return 0;
            }

            var limit = loopTag.GetPositional<int>(0, 0);
            return limit;
        }

        public override string ToString()
        {
            return $"#{Name} ({Items.Count} items) [{Tags.Count} tags]";
        }
    }
}

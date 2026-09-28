using System;
using System.Collections.Generic;

namespace Ktory.Core.Ast
{
    public class ContainerStep : StepNode
    {
        public string Name { get; set; } = "choice";
        public List<ContainerItem> Items { get; set; } = new List<ContainerItem>();

        public new bool IsLoop => base.IsLoop;
        public new int GetLoopLimit() => base.GetLoopLimit();

        public override string ToString()
        {
            return $"#{Name} ({Items.Count} items) [{Tags.Count} tags]";
        }
    }
}

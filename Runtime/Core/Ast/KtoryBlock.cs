using System.Collections.Generic;

namespace Ktory.Core.Ast
{
    public class KtoryBlock
    {
        public string Label { get; set; } = string.Empty;
        public bool IsRoot { get; set; }
        public int StartLineNumber { get; set; }
        public int EndLineNumber { get; set; }
        public List<StepNode> Steps { get; set; } = new List<StepNode>();

        public KtoryBlock() { }

        public KtoryBlock(string label, bool isRoot = false)
        {
            Label = label;
            IsRoot = isRoot;
        }

        public override string ToString()
        {
            return $"=== {Label} === ({Steps.Count} steps, IsRoot={IsRoot})";
        }
    }
}

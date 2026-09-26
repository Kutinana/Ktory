namespace Ktory.Core.Ast
{
    public enum ControlFlowType
    {
        Jump,       // -> Label
        Call,       // => Label
        Return,     // -> return
        Break,      // -> break
        End         // -> end
    }

    public class ControlFlowStep : StepNode
    {
        public ControlFlowType FlowType { get; set; }
        public string? TargetLabel { get; set; }

        public ControlFlowStep() { }

        public ControlFlowStep(ControlFlowType flowType, string? targetLabel = null)
        {
            FlowType = flowType;
            TargetLabel = targetLabel;
        }

        public override string ToString()
        {
            return FlowType switch
            {
                ControlFlowType.Jump => $"-> {TargetLabel}",
                ControlFlowType.Call => $"=> {TargetLabel}",
                ControlFlowType.Return => "-> return",
                ControlFlowType.Break => "-> break",
                ControlFlowType.End => "-> end",
                _ => base.ToString() ?? string.Empty
            };
        }
    }
}

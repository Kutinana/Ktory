using System.Collections.Generic;
using Ktory.Core.Ast;

namespace Ktory.Core.Runtime
{
    public enum CallFrameType
    {
        SectionCall,
        InlineBranch
    }

    public class CallFrame
    {
        public KtoryBlock Block { get; set; }
        public int StepIndex { get; set; }
        public List<StepNode> Steps { get; set; }
        public CallFrameType FrameType { get; set; } = CallFrameType.SectionCall;
        
        /// <summary>
        /// Optional reference to the container if this call frame was created from a container item
        /// </summary>
        public ContainerStep? SourceContainer { get; set; }
        public bool ReturnToContainer { get; set; }

        public CallFrame(KtoryBlock block, int stepIndex, List<StepNode>? steps = null, CallFrameType frameType = CallFrameType.SectionCall)
        {
            Block = block;
            StepIndex = stepIndex;
            Steps = steps ?? block.Steps;
            FrameType = frameType;
        }
    }

    public class LoopContext
    {
        public ContainerStep Container { get; }
        public int IterationCount { get; set; }
        public int MaxIterations { get; } // 0 = infinite

        public LoopContext(ContainerStep container)
        {
            Container = container;
            MaxIterations = container.GetLoopLimit();
            IterationCount = 1;
        }

        public bool CanLoopAgain()
        {
            if (MaxIterations <= 0) return true; // infinite loop
            return IterationCount <= MaxIterations;
        }
    }
}

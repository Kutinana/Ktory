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
        public AutoPolicy? SavedAutoPolicy { get; set; }
        public int LoopStackDepth { get; set; }

        public CallFrame(KtoryBlock block, int stepIndex, List<StepNode>? steps = null, CallFrameType frameType = CallFrameType.SectionCall, int loopStackDepth = 0)
        {
            Block = block;
            StepIndex = stepIndex;
            Steps = steps ?? block.Steps;
            FrameType = frameType;
            LoopStackDepth = loopStackDepth;
        }
    }

    public class LoopContext
    {
        public StepNode TargetNode { get; }
        public int IterationCount { get; set; }
        public int MaxIterations { get; } // -1 = infinite, >0 = fixed limit
        public bool IsInfinite => MaxIterations <= 0;

        /// <summary>
        /// Backward compatibility property for choice loop containers.
        /// </summary>
        public ContainerStep? Container => TargetNode as ContainerStep;

        public LoopContext(StepNode node)
        {
            TargetNode = node;
            MaxIterations = node.GetLoopLimit();
            IterationCount = 1;
        }

        public bool CanLoopAgain()
        {
            if (IsInfinite) return true;
            return IterationCount <= MaxIterations;
        }
    }
}

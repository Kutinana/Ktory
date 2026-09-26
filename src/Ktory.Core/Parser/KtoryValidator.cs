using System;
using System.Collections.Generic;
using Ktory.Core.Ast;
using Ktory.Core.Common;

namespace Ktory.Core.Parser
{
    /// <summary>
    /// Performs static semantic validation on a parsed Ktory AST before runtime execution.
    /// Checks for duplicate sections, unresolved jump/call targets, and conflicting block structures.
    /// </summary>
    public static class KtoryValidator
    {
        public static void Validate(KtoryFile file)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));

            var definedBlocks = new HashSet<string>(file.Blocks.Keys, StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in file.Blocks)
            {
                var block = kvp.Value;
                bool terminalEncountered = false;
                int terminalLine = 0;

                for (int i = 0; i < block.Steps.Count; i++)
                {
                    var step = block.Steps[i];

                    // Check unindented named section body capturing lines after terminal
                    if (!block.IsRoot)
                    {
                        if (terminalEncountered)
                        {
                            throw new KtoryException(
                                $"Unreachable code or conflicting section boundary in section '=== {block.Label} ===' at line {step.LineNumber}. " +
                                $"Step appears after terminal control flow statement at line {terminalLine}. " +
                                $"This usually occurs when a named section body is not indented, causing subsequent lines to be unexpectedly captured. Please indent the section body.",
                                step.LineNumber, 1);
                        }

                        if (step is ControlFlowStep terminalCf && 
                            (terminalCf.FlowType == ControlFlowType.Return || terminalCf.FlowType == ControlFlowType.End))
                        {
                            terminalEncountered = true;
                            terminalLine = step.LineNumber;
                        }
                    }

                    ValidateStep(step, definedBlocks, block.Label);
                }
            }
        }

        private static void ValidateStep(StepNode step, HashSet<string> definedBlocks, string blockLabel)
        {
            switch (step)
            {
                case ControlFlowStep cf:
                    ValidateControlFlow(cf, definedBlocks);
                    break;

                case ContainerStep container:
                    ValidateContainer(container, definedBlocks, blockLabel);
                    break;
            }
        }

        private static void ValidateControlFlow(ControlFlowStep cf, HashSet<string> definedBlocks)
        {
            if (cf.FlowType == ControlFlowType.Jump || cf.FlowType == ControlFlowType.Call)
            {
                string target = cf.TargetLabel ?? string.Empty;
                if (string.IsNullOrWhiteSpace(target))
                {
                    throw new KtoryException(
                        $"Control flow '{cf.FlowType.ToString().ToLower()}' at line {cf.LineNumber} has no target specified.",
                        cf.LineNumber, 1);
                }

                if (cf.FlowType == ControlFlowType.Jump)
                {
                    if (string.Equals(target, "return", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(target, "break", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(target, "end", StringComparison.OrdinalIgnoreCase))
                    {
                        return; // Valid built-in target
                    }
                }

                if (!definedBlocks.Contains(target))
                {
                    throw new KtoryException(
                        $"Target section '=== {target} ===' not found in file (referenced by {cf.FlowType.ToString().ToLower()} at line {cf.LineNumber}).",
                        cf.LineNumber, 1);
                }
            }
        }

        private static void ValidateContainer(ContainerStep container, HashSet<string> definedBlocks, string blockLabel)
        {
            foreach (var item in container.Items)
            {
                if (item.TargetJump != null)
                {
                    if (item.InlineSteps.Count > 0)
                    {
                        throw new KtoryException(
                            $"Option line jump shorthand '{item.TargetJump}' cannot be combined with an indented branch body at line {item.LineNumber}.",
                            item.LineNumber, 1);
                    }

                    ValidateControlFlow(item.TargetJump, definedBlocks);
                }

                foreach (var inlineStep in item.InlineSteps)
                {
                    ValidateStep(inlineStep, definedBlocks, blockLabel);
                }
            }
        }
    }
}

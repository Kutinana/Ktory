using System;
using System.Collections.Generic;
using Ktory.Core.Runtime;

namespace Ktory.Core.Ast
{
    public class KtoryFile
    {
        public string DefaultLang { get; set; } = "zh";
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, KtoryBlock> Blocks { get; set; } = new Dictionary<string, KtoryBlock>(StringComparer.OrdinalIgnoreCase);
        public KtoryBlock RootBlock { get; set; } = new KtoryBlock("root", isRoot: true);

        public List<SpeakerDefinition> Speakers { get; } = new List<SpeakerDefinition>();
        public Dictionary<string, SpeakerDefinition> SpeakersByAlias { get; } = new Dictionary<string, SpeakerDefinition>(StringComparer.Ordinal);

        public KtoryFile()
        {
            Blocks["root"] = RootBlock;
        }

        public void AddSpeaker(SpeakerDefinition speaker)
        {
            foreach (var alias in speaker.Aliases)
            {
                if (SpeakersByAlias.TryGetValue(alias, out var existing))
                {
                    throw new Ktory.Core.Common.KtoryException(
                        $"Speaker alias '{alias}' is already defined at line {existing.LineNumber}, duplicate or re-declaration at line {speaker.LineNumber} is forbidden.",
                        speaker.LineNumber, 1);
                }
            }

            foreach (var alias in speaker.Aliases)
            {
                SpeakersByAlias[alias] = speaker;
            }
            Speakers.Add(speaker);
        }

        public string? ResolveSpeaker(string? rawSpeaker, string requestedLocale, string defaultLocale)
        {
            if (string.IsNullOrEmpty(rawSpeaker)) return null;

            if (SpeakersByAlias.TryGetValue(rawSpeaker, out var speakerDef))
            {
                return speakerDef.GetDisplayName(requestedLocale, defaultLocale);
            }

            // Fallback to raw string verbatim if not registered
            return rawSpeaker;
        }

        public void AddBlock(KtoryBlock block)
        {
            if (string.Equals(block.Label, "root", StringComparison.OrdinalIgnoreCase) && !block.IsRoot)
            {
                throw new Ktory.Core.Common.KtoryException(
                    $"Section label 'root' is reserved for the root block (at line {block.StartLineNumber}).",
                    block.StartLineNumber, 1);
            }

            if (Blocks.TryGetValue(block.Label, out var existing))
            {
                throw new Ktory.Core.Common.KtoryException(
                    $"Duplicate section '=== {block.Label} ===' at line {block.StartLineNumber}. A section with this name is already defined at line {existing.StartLineNumber}.",
                    block.StartLineNumber, 1);
            }

            Blocks[block.Label] = block;
            if (block.IsRoot)
            {
                RootBlock = block;
            }
        }

        public bool TryGetBlock(string label, out KtoryBlock block)
        {
            return Blocks.TryGetValue(label, out block!);
        }

        public void BindLexicalAutoScopes()
        {
            BindBlockAutoScopes(RootBlock);
            foreach (var block in Blocks.Values)
            {
                if (!block.IsRoot)
                {
                    BindBlockAutoScopes(block);
                }
            }
        }

        private static void BindBlockAutoScopes(KtoryBlock block)
        {
            BindStepsAutoScopes(block.Steps, block, initialPolicy: null);
        }

        private static void BindStepsAutoScopes(List<StepNode> steps, KtoryBlock block, AutoPolicy? initialPolicy)
        {
            AutoPolicy? currentPolicy = initialPolicy;

            foreach (var step in steps)
            {
                if (step is DirectiveStep directive)
                {
                    if (string.Equals(directive.Name, "AUTO", StringComparison.OrdinalIgnoreCase))
                    {
                        var waitTag = directive.GetTag("wait");
                        bool useEstimated = false;
                        double defaultWait = 0;
                        if (waitTag != null)
                        {
                            if (waitTag.PositionalArgs.Count > 0)
                            {
                                defaultWait = waitTag.GetPositional<double>(0, 0);
                                useEstimated = false;
                            }
                            else
                            {
                                useEstimated = true;
                            }
                        }
                        currentPolicy = new AutoPolicy(block, useEstimated, defaultWait);
                        directive.LexicalAutoPolicy = currentPolicy;
                        continue;
                    }
                    else if (string.Equals(directive.Name, "AUTO_END", StringComparison.OrdinalIgnoreCase))
                    {
                        currentPolicy = null;
                        directive.LexicalAutoPolicy = null;
                        continue;
                    }
                }

                step.LexicalAutoPolicy = currentPolicy;

                if (step is ContainerStep container)
                {
                    foreach (var item in container.Items)
                    {
                        if (item.TargetJump != null)
                        {
                            item.TargetJump.LexicalAutoPolicy = currentPolicy;
                        }
                        if (item.InlineSteps != null && item.InlineSteps.Count > 0)
                        {
                            BindStepsAutoScopes(item.InlineSteps, block, currentPolicy);
                        }
                    }
                }
            }
        }
    }
}

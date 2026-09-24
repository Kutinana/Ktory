using System;
using System.Collections.Generic;

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
    }
}

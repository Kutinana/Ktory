using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Ktory.Core.Ast;
using Ktory.Core.Common;
using Ktory.Core.Desugar;

namespace Ktory.Core.Parser
{
    public class KtoryParser
    {
        private class SourceLine
        {
            public int LineNumber { get; set; }
            public int Indent { get; set; }
            public string Raw { get; set; } = string.Empty;
            public string Text { get; set; } = string.Empty;
        }

        private static readonly Regex SectionHeaderRegex = new Regex(@"^===\s*([a-zA-Z0-9_\-]+)\s*===$", RegexOptions.Compiled);
        private static readonly Regex DefaultLangRegex = new Regex(@"^@?defaultLang\s*[:=]\s*([a-zA-Z0-9_\-]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LocaleVariantRegex = new Regex(@"^@([a-zA-Z0-9_\-]+)\s*[:：]\s*(.*)$", RegexOptions.Compiled);
        private static readonly Regex ItemRegex = new Regex(@"^([\*\+])\s*(.*)$", RegexOptions.Compiled);
        private static readonly Regex GuardConditionRegex = new Regex(@"^\?\s*\{([^\{\}]+)\}\s*(.*)$", RegexOptions.Compiled);
        private static readonly Regex SpeakerDeclarationRegex = new Regex(@"^@speaker(?:\s+([a-zA-Z0-9_\-]+))?\s*[:：]\s*(.*)$", RegexOptions.Compiled);
        private static readonly Regex OptionVariantRegex = new Regex(@"^@([a-zA-Z0-9_\-]+)\s*[:：]\s*(.*)$", RegexOptions.Compiled);

        public static KtoryFile Parse(string source)
        {
            var parser = new KtoryParser();
            return parser.ParseInternal(source);
        }

        private KtoryFile ParseInternal(string source)
        {
            var file = new KtoryFile();
            var lines = PreprocessLines(source);

            int index = 0;
            // First check top lines for file metadata (e.g. defaultLang)
            while (index < lines.Count)
            {
                var match = DefaultLangRegex.Match(lines[index].Text);
                if (match.Success)
                {
                    file.DefaultLang = match.Groups[1].Value;
                    index++;
                }
                else
                {
                    break;
                }
            }

            // Parse blocks: Root section + named sections
            KtoryBlock currentBlock = file.RootBlock;
            int sectionIndent = -1;
            bool isIndentedSection = false;

            while (index < lines.Count)
            {
                var line = lines[index];

                // Check section end: If we are inside an indented named section and indent drops back to <= sectionIndent
                if (currentBlock != file.RootBlock && isIndentedSection && line.Indent <= sectionIndent)
                {
                    // Return to root section
                    currentBlock = file.RootBlock;
                    sectionIndent = -1;
                    isIndentedSection = false;
                }

                // Check @speaker declaration
                if (line.Text.StartsWith("@speaker"))
                {
                    ParseSpeakerDeclaration(line, file);
                    index++;
                    continue;
                }

                // Check if line is a Section Header === Label ===
                var secMatch = SectionHeaderRegex.Match(line.Text);
                if (secMatch.Success)
                {
                    var label = secMatch.Groups[1].Value;
                    var newBlock = new KtoryBlock(label, isRoot: false)
                    {
                        StartLineNumber = line.LineNumber
                    };
                    file.AddBlock(newBlock);
                    currentBlock = newBlock;

                    // Check if subsequent lines are indented relative to this header
                    if (index + 1 < lines.Count && lines[index + 1].Indent > line.Indent)
                    {
                        isIndentedSection = true;
                        sectionIndent = line.Indent;
                    }
                    else
                    {
                        isIndentedSection = false;
                        sectionIndent = -1;
                    }

                    index++;
                    continue;
                }

                // Parse step node within currentBlock
                var step = ParseStep(lines, ref index, file, currentBlock);
                if (step != null)
                {
                    currentBlock.Steps.Add(step);
                }
            }

            return file;
        }

        private StepNode? ParseStep(List<SourceLine> lines, ref int index, KtoryFile file, KtoryBlock currentBlock)
        {
            if (index >= lines.Count) return null;
            var line = lines[index];

            // 0. Check @speaker declaration
            if (line.Text.StartsWith("@speaker"))
            {
                ParseSpeakerDeclaration(line, file);
                index++;
                return null;
            }

            // 1. Check Decorator line standalone (.name(...))
            if (line.Text.StartsWith("."))
            {
                // Attach to previous step in current block
                var tags = TagParser.ParseTags(line.Text);
                if (currentBlock.Steps.Count > 0)
                {
                    var prev = currentBlock.Steps[currentBlock.Steps.Count - 1];
                    prev.Tags.AddRange(tags);
                }
                index++;
                return null;
            }

            // 2. Check Control Flow (-> Label, => Label, -> return, -> break, -> end)
            if (line.Text.StartsWith("->") || line.Text.StartsWith("=>"))
            {
                var cf = ParseControlFlow(line);
                index++;
                // Attach any trailing or next-line tags
                AttachFollowingTags(lines, ref index, cf);
                return cf;
            }

            // 3. Check Directive / Container (#)
            if (line.Text.StartsWith("#"))
            {
                return ParseDirectiveOrContainer(lines, ref index, file);
            }

            // 4. Text dialogue or narration (including ? {expr} guard)
            return ParseTextStep(lines, ref index, file.DefaultLang);
        }

        private static string Unquote(string str)
        {
            var trimmed = str.Trim();
            if (trimmed.Length >= 2 && ((trimmed.StartsWith("\"") && trimmed.EndsWith("\"")) || (trimmed.StartsWith("'") && trimmed.EndsWith("'"))))
            {
                return trimmed.Substring(1, trimmed.Length - 2).Trim();
            }
            return trimmed;
        }

        private static void ParseSpeakerDeclaration(SourceLine line, KtoryFile file)
        {
            var match = SpeakerDeclarationRegex.Match(line.Text);
            if (!match.Success)
            {
                throw new KtoryException(
                    $"Invalid @speaker declaration '{line.Text}'. Expected format: '@speaker: zh=\"...\" | en=\"...\"' or '@speaker id: zh=\"...\" | en=\"...\"'",
                    line.LineNumber, line.Indent);
            }

            string? id = match.Groups[1].Success ? match.Groups[1].Value : null;
            string mappings = match.Groups[2].Value;

            var speakerDef = new SpeakerDefinition
            {
                Id = id,
                LineNumber = line.LineNumber
            };

            if (!string.IsNullOrEmpty(id))
            {
                speakerDef.Aliases.Add(id!);
            }

            var segments = mappings.Split('|');
            foreach (var rawSeg in segments)
            {
                var seg = rawSeg.Trim();
                if (string.IsNullOrEmpty(seg)) continue;

                int eqIdx = seg.IndexOf('=');
                if (eqIdx < 0) eqIdx = seg.IndexOf(':');
                if (eqIdx < 0)
                {
                    throw new KtoryException(
                        $"Invalid speaker mapping segment '{seg}' in line '{line.Text}'. Expected format: lang=\"name\" or lang=name",
                        line.LineNumber, line.Indent);
                }

                string lang = seg.Substring(0, eqIdx).Trim();
                string val = seg.Substring(eqIdx + 1).Trim();
                val = Unquote(val);

                if (string.IsNullOrEmpty(lang) || string.IsNullOrEmpty(val))
                {
                    throw new KtoryException(
                        $"Speaker mapping '{seg}' contains empty language or name in line '{line.Text}'.",
                        line.LineNumber, line.Indent);
                }

                speakerDef.DisplayNames[lang] = val;
                speakerDef.Aliases.Add(val);
            }

            file.AddSpeaker(speakerDef);
        }

        private static void ParseOptionLabelVariant(string rawLabel, ContainerItem item, string defaultLang, int lineNumber)
        {
            var match = OptionVariantRegex.Match(rawLabel);
            if (match.Success)
            {
                string locale = match.Groups[1].Value.Trim();
                string text = match.Groups[2].Value.Trim();
                text = Unquote(text);
                item.LabelVariants[locale] = TextDesugarer.Desugar(text, lineNumber);
            }
            else
            {
                string text = Unquote(rawLabel);
                item.LabelVariants[defaultLang] = TextDesugarer.Desugar(text, lineNumber);
            }
        }

        private StepNode ParseDirectiveOrContainer(List<SourceLine> lines, ref int index, KtoryFile file)
        {
            var line = lines[index];
            int lineIndent = line.Indent;

            // Extract guard condition if present at start of line
            string text = line.Text;
            string? guard = null;
            var guardMatch = GuardConditionRegex.Match(text);
            if (guardMatch.Success)
            {
                guard = guardMatch.Groups[1].Value.Trim();
                text = guardMatch.Groups[2].Value.Trim();
            }

            // Parse directive name and tags on the # line
            // e.g. #do .camera_shake(0.5) or #choice.loop.timeout(0) or #
            string rest = text.Substring(1).TrimStart();
            string name = string.Empty;
            int tagStart = rest.IndexOf('.');

            if (tagStart >= 0)
            {
                name = rest.Substring(0, tagStart).Trim();
                rest = rest.Substring(tagStart);
            }
            else
            {
                // Might be "#do" or "#choice" without tags, or "#"
                int spaceIdx = rest.IndexOf(' ');
                if (spaceIdx >= 0)
                {
                    name = rest.Substring(0, spaceIdx).Trim();
                    rest = rest.Substring(spaceIdx);
                }
                else
                {
                    name = rest.Trim();
                    rest = string.Empty;
                }
            }

            var lineTags = TagParser.ParseTags(rest);
            index++;

            // Peek next lines to see if there are indented '*' or '+' items
            bool hasContainerItems = false;
            int peek = index;
            while (peek < lines.Count && lines[peek].Indent > lineIndent)
            {
                var peekText = lines[peek].Text;
                // If it's a decorator line, continue peeking
                if (peekText.StartsWith("."))
                {
                    peek++;
                    continue;
                }
                // Check if it's an item: * [label] or + [label]
                if (ItemRegex.IsMatch(peekText))
                {
                    hasContainerItems = true;
                    break;
                }
                break;
            }

            if (hasContainerItems)
            {
                // It's a ContainerStep!
                var container = new ContainerStep
                {
                    LineNumber = line.LineNumber,
                    Name = string.IsNullOrEmpty(name) ? "choice" : name,
                    GuardCondition = guard,
                    Tags = lineTags
                };

                // Read container items and any inline decorators
                while (index < lines.Count && lines[index].Indent > lineIndent)
                {
                    var childLine = lines[index];
                    if (childLine.Text.StartsWith("."))
                    {
                        container.Tags.AddRange(TagParser.ParseTags(childLine.Text));
                        index++;
                        continue;
                    }

                    if (ItemRegex.IsMatch(childLine.Text))
                    {
                        var item = ParseContainerItem(lines, ref index, lineIndent, file);
                        container.Items.Add(item);
                        continue;
                    }

                    break;
                }

                return container;
            }
            else
            {
                // It's an independent DirectiveStep
                var directive = new DirectiveStep
                {
                    LineNumber = line.LineNumber,
                    Name = name,
                    GuardCondition = guard,
                    Tags = lineTags
                };

                AttachFollowingTags(lines, ref index, directive);
                return directive;
            }
        }

        private ContainerItem ParseContainerItem(List<SourceLine> lines, ref int index, int containerIndent, KtoryFile file)
        {
            var line = lines[index];
            int itemIndent = line.Indent;
            var match = ItemRegex.Match(line.Text);
            char marker = match.Groups[1].Value[0];
            string content = match.Groups[2].Value.Trim();
            index++;

            var item = new ContainerItem
            {
                LineNumber = line.LineNumber,
                Marker = marker
            };

            // Check if there is a guard condition in the item: e.g. ? {expr} [label]
            var guardMatch = GuardConditionRegex.Match(content);
            if (guardMatch.Success)
            {
                item.GuardCondition = guardMatch.Groups[1].Value.Trim();
                content = guardMatch.Groups[2].Value.Trim();
            }

            // Extract all [label] variants from the marker line if present
            while (true)
            {
                int labelStart = content.IndexOf('[');
                int labelEnd = content.IndexOf(']');
                if (labelStart >= 0 && labelEnd > labelStart)
                {
                    string rawLabel = content.Substring(labelStart + 1, labelEnd - labelStart - 1).Trim();
                    ParseOptionLabelVariant(rawLabel, item, file.DefaultLang, line.LineNumber);
                    content = content.Substring(labelEnd + 1).Trim();
                }
                else
                {
                    break;
                }
            }

            // Check target jump on the same line: e.g. => Sub_OpenDrawer or -> break or -> Label
            if (content.StartsWith("->") || content.StartsWith("=>"))
            {
                item.TargetJump = ParseControlFlowString(content, line.LineNumber);
            }
            else if (content.StartsWith("."))
            {
                // Trailing tags on the item line
                item.Tags.AddRange(TagParser.ParseTags(content));
            }

            bool inHead = (item.TargetJump == null && item.InlineSteps.Count == 0);
            string? branchAnchor = item.TargetJump != null ? line.Text : null;

            // Parse children under this item (indent > itemIndent)
            while (index < lines.Count && lines[index].Indent > itemIndent)
            {
                var childLine = lines[index];

                if (inHead)
                {
                    // 1. Bracketed label variant: [@en: "Talk to Alice"] or [Talk to Alice]
                    if (childLine.Text.StartsWith("[") && childLine.Text.EndsWith("]"))
                    {
                        string inside = childLine.Text.Substring(1, childLine.Text.Length - 2).Trim();
                        ParseOptionLabelVariant(inside, item, file.DefaultLang, childLine.LineNumber);
                        index++;
                        continue;
                    }

                    // 2. Standalone decorator on option
                    if (childLine.Text.StartsWith("."))
                    {
                        item.Tags.AddRange(TagParser.ParseTags(childLine.Text));
                        index++;
                        continue;
                    }

                    // 3. Option TargetJump on child line: -> Label or => Label
                    if (childLine.Text.StartsWith("->") || childLine.Text.StartsWith("=>"))
                    {
                        item.TargetJump = ParseControlFlow(childLine);
                        inHead = false;
                        branchAnchor = childLine.Text;
                        index++;
                        continue;
                    }

                    // 4. Any other line begins the branch execution body!
                    inHead = false;
                    branchAnchor = childLine.Text;
                }

                // Now in body:
                // Check if an option label variant illegally appears after branch anchor
                if (childLine.Text.StartsWith("[") && childLine.Text.EndsWith("]") && childLine.Text.Contains("@"))
                {
                    throw new KtoryException(
                        $"Option text variant '{childLine.Text}' cannot appear after branch execution anchor '{branchAnchor}' at line {childLine.LineNumber}. All option text variants and decorators must appear before the first branch statement.",
                        childLine.LineNumber, childLine.Indent);
                }

                // Parse as inline step
                var dummyBlock = new KtoryBlock("inline");
                var step = ParseStep(lines, ref index, file, dummyBlock);
                if (step != null)
                {
                    item.InlineSteps.Add(step);
                }
            }

            return item;
        }

        private TextStep ParseTextStep(List<SourceLine> lines, ref int index, string defaultLang)
        {
            var line = lines[index];
            int lineIndent = line.Indent;
            string text = line.Text;
            string? guard = null;

            var guardMatch = GuardConditionRegex.Match(text);
            if (guardMatch.Success)
            {
                guard = guardMatch.Groups[1].Value.Trim();
                text = guardMatch.Groups[2].Value.Trim();
            }

            string? speaker = null;
            string? content = null;

            // First colon rule: find first ':' or '：'
            int halfColon = text.IndexOf(':');
            int fullColon = text.IndexOf('：');
            int colonIndex = -1;
            if (halfColon >= 0 && fullColon >= 0) colonIndex = Math.Min(halfColon, fullColon);
            else if (halfColon >= 0) colonIndex = halfColon;
            else if (fullColon >= 0) colonIndex = fullColon;

            if (colonIndex >= 0)
            {
                speaker = text.Substring(0, colonIndex).Trim();
                content = text.Substring(colonIndex + 1).Trim();
                if (string.IsNullOrEmpty(speaker)) speaker = null; // Narration
            }
            else
            {
                // Syntactic sugar narration: line without colon and without reserved macro prefix
                speaker = null;
                content = text;
            }

            var textStep = new TextStep
            {
                LineNumber = line.LineNumber,
                Speaker = speaker,
                GuardCondition = guard
            };

            // If content is present on the same line, check if it has trailing tags or is pure content
            if (!string.IsNullOrEmpty(content))
            {
                // Check if content ends with decorators, e.g. "Some text .emotion(smile)"
                // Note: decorators must be separated by whitespace and start with .tag
                ExtractTrailingTags(ref content, textStep.Tags);
                textStep.TextVariants[defaultLang] = TextDesugarer.Desugar(content, line.LineNumber);
            }

            index++;

            // Check subsequent indented lines for @locale: variants and .tags
            while (index < lines.Count && lines[index].Indent > lineIndent)
            {
                var nextLine = lines[index];
                if (nextLine.Text.StartsWith("."))
                {
                    textStep.Tags.AddRange(TagParser.ParseTags(nextLine.Text));
                    index++;
                    continue;
                }

                var locMatch = LocaleVariantRegex.Match(nextLine.Text);
                if (locMatch.Success)
                {
                    string locale = locMatch.Groups[1].Value.Trim();
                    string locContent = locMatch.Groups[2].Value.Trim();
                    ExtractTrailingTags(ref locContent, textStep.Tags);
                    textStep.TextVariants[locale] = TextDesugarer.Desugar(locContent, nextLine.LineNumber);
                    index++;
                    continue;
                }

                // If it is another unhandled line indented under dialogue, break out
                break;
            }

            return textStep;
        }

        private static void ExtractTrailingTags(ref string content, List<TagData> targetTags)
        {
            // Simple check: if content contains " ." followed by letters, split trailing tags
            int dotIdx = content.LastIndexOf(" .", StringComparison.Ordinal);
            if (dotIdx >= 0)
            {
                string tagPart = content.Substring(dotIdx + 1);
                var tags = TagParser.ParseTags(tagPart);
                if (tags.Count > 0)
                {
                    targetTags.AddRange(tags);
                    content = content.Substring(0, dotIdx).Trim();
                }
            }
        }

        private static ControlFlowStep ParseControlFlow(SourceLine line)
        {
            return ParseControlFlowString(line.Text, line.LineNumber);
        }

        private static ControlFlowStep ParseControlFlowString(string text, int lineNumber)
        {
            string trimmed = text.Trim();
            if (trimmed.StartsWith("=>"))
            {
                string dest = trimmed.Substring(2).Trim();
                return new ControlFlowStep(ControlFlowType.Call, dest) { LineNumber = lineNumber };
            }

            if (trimmed.StartsWith("->"))
            {
                string dest = trimmed.Substring(2).Trim();
                if (string.Equals(dest, "return", StringComparison.OrdinalIgnoreCase))
                    return new ControlFlowStep(ControlFlowType.Return) { LineNumber = lineNumber };
                if (string.Equals(dest, "break", StringComparison.OrdinalIgnoreCase))
                    return new ControlFlowStep(ControlFlowType.Break) { LineNumber = lineNumber };
                if (string.Equals(dest, "end", StringComparison.OrdinalIgnoreCase))
                    return new ControlFlowStep(ControlFlowType.End) { LineNumber = lineNumber };

                return new ControlFlowStep(ControlFlowType.Jump, dest) { LineNumber = lineNumber };
            }

            throw new KtoryException($"Unrecognized control flow statement: '{text}'", lineNumber, 1);
        }

        private static void AttachFollowingTags(List<SourceLine> lines, ref int index, StepNode step)
        {
            while (index < lines.Count && lines[index].Text.StartsWith("."))
            {
                step.Tags.AddRange(TagParser.ParseTags(lines[index].Text));
                index++;
            }
        }

        private static List<SourceLine> PreprocessLines(string source)
        {
            var result = new List<SourceLine>();
            using var reader = new StringReader(source);
            string? lineStr;
            int lineNum = 0;

            while ((lineStr = reader.ReadLine()) != null)
            {
                lineNum++;
                string raw = lineStr;

                // Strip comments
                string uncommented = StripComment(raw);
                if (string.IsNullOrWhiteSpace(uncommented))
                {
                    continue; // Skip blank lines and pure comments
                }

                // Calculate indentation: 1 tab = 2 spaces
                int indent = 0;
                for (int i = 0; i < raw.Length; i++)
                {
                    if (raw[i] == ' ') indent++;
                    else if (raw[i] == '\t') indent += 2;
                    else break;
                }

                result.Add(new SourceLine
                {
                    LineNumber = lineNum,
                    Indent = indent,
                    Raw = raw,
                    Text = uncommented.Trim()
                });
            }

            return result;
        }

        private static string StripComment(string line)
        {
            bool inQuote = false;
            char quoteChar = '\0';

            for (int i = 0; i < line.Length - 1; i++)
            {
                char c = line[i];
                if ((c == '"' || c == '\'') && (i == 0 || line[i - 1] != '\\'))
                {
                    if (!inQuote)
                    {
                        inQuote = true;
                        quoteChar = c;
                    }
                    else if (quoteChar == c)
                    {
                        inQuote = false;
                    }
                }

                if (!inQuote && c == '/' && line[i + 1] == '/')
                {
                    return line.Substring(0, i);
                }
            }

            return line;
        }
    }
}

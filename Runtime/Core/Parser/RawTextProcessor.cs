using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ktory.Core.Ast;
using Ktory.Core.Common;
using Ktory.Core.Desugar;

namespace Ktory.Core.Parser
{
    /// <summary>
    /// Protects literal spans before comments, indentation and text syntax are processed.
    /// Tokens exist only during parsing; their prefix is chosen absent from the source.
    /// </summary>
    internal sealed class RawTextProcessor
    {
        private readonly string _prefix;
        private readonly List<string> _literals = new List<string>();

        internal RawTextProcessor(string source)
        {
            // TextDesugarer reserves E000–E3E7 for escaped characters.
            _prefix = "\uF000raw";
            while (source.IndexOf(_prefix, StringComparison.Ordinal) >= 0) _prefix += "x";
        }

        internal string ProtectLine(string line, StringReader reader, ref int physicalLine)
        {
            var output = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length;)
            {
                char c = line[i];
                if (c == '\\' && i + 1 < line.Length)
                {
                    output.Append(line, i, 2);
                    i += 2;
                    continue;
                }
                if (c == '"') quoted = !quoted;
                if (!quoted && c == '/' && i + 1 < line.Length && line[i + 1] == '/')
                {
                    if (!LexicalScanner.IsUriScheme(line, i)) break;
                    output.Append("//");
                    i += 2;
                    continue;
                }

                // A genuine trailing decorator sequence owns its quoted argument strings.
                if (!quoted && c == '.' && (i == 0 || char.IsWhiteSpace(line[i - 1])) &&
                    TagParser.TryParseTagSequence(LexicalScanner.StripComment(line), i, out _, out _))
                {
                    output.Append(LexicalScanner.StripComment(line).Substring(i));
                    break;
                }

                int length = c == '\'' ? QuoteRun(line, i) : 0;
                if (length < 3)
                {
                    output.Append(c);
                    i++;
                    continue;
                }

                int openingLine = physicalLine;
                int openingColumn = i + 1;
                i += length;
                var literal = new StringBuilder();
                while (true)
                {
                    if (i == line.Length)
                    {
                        line = reader.ReadLine() ?? throw new KtoryException(
                            $"Unterminated raw text fence; expected {length} ASCII single quotes.", openingLine, openingColumn);
                        physicalLine++;
                        literal.Append('\n');
                        i = 0;
                        continue;
                    }
                    int run = QuoteRun(line, i);
                    if (run == length)
                    {
                        i += run;
                        break;
                    }
                    int count = Math.Max(1, run);
                    literal.Append(line, i, count);
                    i += count;
                }
                output.Append(_prefix).Append(_literals.Count).Append('\uF001');
                _literals.Add(NormalizeBlock(literal.ToString()));
            }
            return output.ToString();
        }

        private static int QuoteRun(string text, int index)
        {
            int end = index;
            while (end < text.Length && text[end] == '\'') end++;
            return end - index;
        }

        private static string NormalizeBlock(string text)
        {
            int firstBreak = text.IndexOf('\n');
            if (firstBreak < 0 || text.Substring(0, firstBreak).Trim(' ', '\t').Length != 0) return text;

            // Opening/closing layout lines are not display text. Blank body lines are.
            text = text.Substring(firstBreak + 1);
            int lastBreak = text.LastIndexOf('\n');
            if (lastBreak >= 0 && text.Substring(lastBreak + 1).Trim(' ', '\t').Length == 0)
                text = text.Substring(0, lastBreak);
            else if (text.Trim(' ', '\t').Length == 0) return string.Empty;

            var lines = text.Split('\n');
            int common = int.MaxValue;
            foreach (var line in lines)
            {
                if (line.Trim(' ', '\t').Length == 0) continue;
                int indent = 0;
                foreach (char c in line)
                {
                    if (c == ' ') indent++;
                    else if (c == '\t') indent += 2;
                    else break;
                }
                common = Math.Min(common, indent);
            }
            if (common == int.MaxValue) common = 0;
            for (int n = 0; n < lines.Length; n++)
            {
                int i = 0, removed = 0;
                while (i < lines[n].Length && removed < common && (lines[n][i] == ' ' || lines[n][i] == '\t'))
                    removed += lines[n][i++] == '\t' ? 2 : 1;
                lines[n] = new string(' ', Math.Max(0, removed - common)) + lines[n].Substring(i);
            }
            return string.Join("\n", lines);
        }

        internal string Desugar(string text, int lineNumber, out IReadOnlyList<TextSegment>? segments)
        {
            text = TextDesugarer.Desugar(text, lineNumber);
            var pieces = new List<TextSegment>();
            var content = new StringBuilder();
            int cursor = 0;
            while (true)
            {
                int start = text.IndexOf(_prefix, cursor, StringComparison.Ordinal);
                if (start < 0) break;
                int end = text.IndexOf('\uF001', start);
                int id = int.Parse(text.Substring(start + _prefix.Length, end - start - _prefix.Length));
                if (start > cursor) pieces.Add(new TextSegment { Content = text.Substring(cursor, start - cursor) });
                pieces.Add(new TextSegment { Content = _literals[id], IsLiteral = true });
                cursor = end + 1;
            }
            if (pieces.Count == 0) { segments = null; return text; }
            if (cursor < text.Length) pieces.Add(new TextSegment { Content = text.Substring(cursor) });
            foreach (var piece in pieces) content.Append(piece.Content);
            segments = pieces.ToArray();
            return content.ToString();
        }
    }
}

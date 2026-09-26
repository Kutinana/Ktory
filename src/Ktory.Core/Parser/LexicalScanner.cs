using System;
using System.Collections.Generic;
using Ktory.Core.Ast;

namespace Ktory.Core.Parser
{
    /// <summary>
    /// Lexical scanner for Ktory scripts.
    /// Handles comment stripping, URI scheme discrimination, string/quote tracking, and trailing tag boundaries.
    /// </summary>
    public static class LexicalScanner
    {
        /// <summary>
        /// Strips line comments (//) from a source line while preserving:
        /// 1. // within single or double quotes
        /// 2. // in URI schemes (e.g. https://, http://, file://)
        /// 3. Escaped slashes (\//)
        /// </summary>
        public static string StripComment(string line)
        {
            if (string.IsNullOrEmpty(line)) return string.Empty;

            bool inDoubleQuote = false;
            bool inSingleQuote = false;

            for (int i = 0; i < line.Length - 1; i++)
            {
                char c = line[i];

                // Escape character: skip next character
                if (c == '\\' && i + 1 < line.Length)
                {
                    i++;
                    continue;
                }

                // Quote tracking
                if (c == '"' && !inSingleQuote)
                {
                    inDoubleQuote = !inDoubleQuote;
                    continue;
                }
                if (c == '\'' && !inDoubleQuote)
                {
                    inSingleQuote = !inSingleQuote;
                    continue;
                }

                if (inDoubleQuote || inSingleQuote)
                {
                    continue;
                }

                // Check for // comment
                if (c == '/' && line[i + 1] == '/')
                {
                    // Check if this // is part of a URI scheme (e.g. https://, http://)
                    if (IsUriScheme(line, i))
                    {
                        i++; // skip the second '/'
                        continue;
                    }

                    // Legitimate comment: strip everything from here to end of line
                    return line.Substring(0, i);
                }
            }

            return line;
        }

        /// <summary>
        /// Checks if a double-slash at slashIndex is part of a URI scheme, e.g. "https://", "http://", "file://".
        /// </summary>
        public static bool IsUriScheme(string text, int slashIndex)
        {
            if (slashIndex <= 1) return false;
            if (text[slashIndex - 1] != ':') return false;

            // Scan backwards from slashIndex - 2 to find scheme name [a-zA-Z][a-zA-Z0-9+.-]*
            int p = slashIndex - 2;
            while (p >= 0 && (char.IsLetterOrDigit(text[p]) || text[p] == '+' || text[p] == '-' || text[p] == '.'))
            {
                p--;
            }

            int schemeStart = p + 1;
            if (schemeStart < slashIndex - 1 && char.IsLetter(text[schemeStart]))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Safely extracts trailing decorator tags (e.g. ".wait(2) .sfx(boom)") from a dialogue/narration line.
        /// Preserves normal text such as ": 请打开 .ktr 文件。" without incorrectly stripping words following a dot.
        /// </summary>
        public static bool TryExtractTrailingTags(string content, out string cleanContent, out List<TagData> tags)
        {
            cleanContent = content;
            tags = new List<TagData>();

            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            bool inDoubleQuote = false;
            bool inSingleQuote = false;
            int bracketDepth = 0;
            int braceDepth = 0;

            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];

                if (c == '\\' && i + 1 < content.Length)
                {
                    i++;
                    continue;
                }

                if (c == '"' && !inSingleQuote)
                {
                    inDoubleQuote = !inDoubleQuote;
                    continue;
                }
                if (c == '\'' && !inDoubleQuote)
                {
                    inSingleQuote = !inSingleQuote;
                    continue;
                }

                if (inDoubleQuote || inSingleQuote)
                {
                    continue;
                }

                if (c == '[') { bracketDepth++; continue; }
                if (c == ']') { if (bracketDepth > 0) bracketDepth--; continue; }
                if (c == '{') { braceDepth++; continue; }
                if (c == '}') { if (braceDepth > 0) braceDepth--; continue; }

                if (bracketDepth > 0 || braceDepth > 0)
                {
                    continue;
                }

                // Check candidate start of tag sequence: '.' preceded by whitespace (or at index 0)
                if (c == '.' && (i == 0 || char.IsWhiteSpace(content[i - 1])))
                {
                    if (TagParser.TryParseTagSequence(content, i, out var candidateTags, out int endIndex))
                    {
                        // Candidate sequence must extend strictly to the end of the content
                        if (endIndex >= content.Length || string.IsNullOrWhiteSpace(content.Substring(endIndex)))
                        {
                            cleanContent = content.Substring(0, i).TrimEnd();
                            tags = candidateTags;
                            return true;
                        }
                    }
                }
            }

            return false;
        }
    }
}

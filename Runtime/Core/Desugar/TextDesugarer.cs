using System;
using System.Text;
using System.Text.RegularExpressions;
using Ktory.Core.Common;

namespace Ktory.Core.Desugar
{
    public static class TextDesugarer
    {
        // Regex patterns for inline formatting
        // Matches [base]{ruby}
        private static readonly Regex RubyRegex = new Regex(@"\[([^\[\]\r\n]+)\]\{([^{}\r\n]+)\}", RegexOptions.Compiled);

        // Matches **bold** or __bold__
        private static readonly Regex BoldAsteriskRegex = new Regex(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
        private static readonly Regex BoldUnderscoreRegex = new Regex(@"__(.+?)__", RegexOptions.Compiled);

        // Matches *italic* or _italic_
        private static readonly Regex ItalicAsteriskRegex = new Regex(@"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", RegexOptions.Compiled);
        private static readonly Regex ItalicUnderscoreRegex = new Regex(@"(?<!_)_(?!_)(.+?)(?<!_)_(?!_)", RegexOptions.Compiled);

        // Matches ~~strikethrough~~
        private static readonly Regex StrikethroughRegex = new Regex(@"~~(.+?)~~", RegexOptions.Compiled);

        public static string Desugar(string input, int lineNumber = 0)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            // Check forbidden block markdown
            ValidateNoForbiddenBlockMarkdown(input, lineNumber);

            // Temporarily protect escaped characters (e.g. \*, \_, \~, \[, \{, \\)
            var protectedText = new StringBuilder();
            var placeholders = new System.Collections.Generic.Dictionary<string, string>();
            int placeholderIdx = 0;

            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] == '\\' && i + 1 < input.Length)
                {
                    char next = input[i + 1];
                    if (next is '*' or '_' or '~' or '[' or ']' or '{' or '}' or '\\')
                    {
                        char tokenChar = (char)(0xE000 + (placeholderIdx++ % 1000));
                        string token = tokenChar.ToString();
                        placeholders[token] = next.ToString();
                        protectedText.Append(token);
                        i++; // skip escaped char
                        continue;
                    }
                }
                protectedText.Append(input[i]);
            }

            var text = protectedText.ToString();

            // 1. Ruby tags: [base]{ruby} -> <ruby="ruby">base</ruby>
            text = RubyRegex.Replace(text, match =>
            {
                var baseText = match.Groups[1].Value;
                var rubyText = match.Groups[2].Value;
                return $"<ruby=\"{rubyText}\">{baseText}</ruby>";
            });

            // 2. Bold: **text** and __text__ -> <b>text</b>
            text = BoldAsteriskRegex.Replace(text, "<b>$1</b>");
            text = BoldUnderscoreRegex.Replace(text, "<b>$1</b>");

            // 3. Strikethrough: ~~text~~ -> <s>text</s>
            text = StrikethroughRegex.Replace(text, "<s>$1</s>");

            // 4. Italic: *text* and _text_ -> <i>text</i>
            text = ItalicAsteriskRegex.Replace(text, "<i>$1</i>");
            text = ItalicUnderscoreRegex.Replace(text, "<i>$1</i>");

            // 5. Restore escaped characters
            foreach (var kvp in placeholders)
            {
                text = text.Replace(kvp.Key, kvp.Value);
            }

            return text;
        }

        private static void ValidateNoForbiddenBlockMarkdown(string input, int lineNumber)
        {
            var trimmed = input.TrimStart();
            if (trimmed.StartsWith("# ") || trimmed.StartsWith("## ") || trimmed.StartsWith("### "))
            {
                throw new KtoryException("Forbidden block Markdown: Header '#' is not allowed in text lines.", lineNumber, 1);
            }
            if (trimmed.StartsWith("> ") || trimmed.StartsWith(">"))
            {
                throw new KtoryException("Forbidden block Markdown: Block quote '>' is not allowed in text lines.", lineNumber, 1);
            }
            if (trimmed.StartsWith("```"))
            {
                throw new KtoryException("Forbidden block Markdown: Multi-line code block '```' is not allowed.", lineNumber, 1);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using Ktory.Core.Ast;

namespace Ktory.Core.Parser
{
    public static class TagParser
    {
        /// <summary>
        /// Parses one or more chained decorators, e.g. ".loop.timeout(0)" or ".camera_shake(0.5) .sfx(\"hit\")"
        /// </summary>
        public static List<TagData> ParseTags(string input)
        {
            var result = new List<TagData>();
            if (string.IsNullOrWhiteSpace(input)) return result;

            int i = 0;
            while (i < input.Length)
            {
                // Skip whitespace
                while (i < input.Length && char.IsWhiteSpace(input[i])) i++;
                if (i >= input.Length) break;

                if (input[i] != '.')
                {
                    i++;
                    continue;
                }

                // Skip '.'
                i++;
                if (i >= input.Length) break;

                // Read tag name
                var nameSb = new StringBuilder();
                while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] == '_'))
                {
                    nameSb.Append(input[i]);
                    i++;
                }

                var tagName = nameSb.ToString();
                if (string.IsNullOrEmpty(tagName)) continue;

                var tag = new TagData(tagName);

                // Check for arguments in parentheses
                if (i < input.Length && input[i] == '(')
                {
                    i++; // skip '('
                    ParseArguments(input, ref i, tag);
                }

                result.Add(tag);
            }

            return result;
        }

        /// <summary>
        /// Attempts to parse a strict, continuous sequence of tags starting at startIndex until the end of input.
        /// If any non-tag content is encountered (e.g. normal text like " 文件。"), returns false.
        /// </summary>
        public static bool TryParseTagSequence(string input, int startIndex, out List<TagData> tags, out int endIndex)
        {
            tags = new List<TagData>();
            endIndex = startIndex;
            if (string.IsNullOrWhiteSpace(input) || startIndex >= input.Length) return false;

            int i = startIndex;
            while (i < input.Length)
            {
                while (i < input.Length && char.IsWhiteSpace(input[i])) i++;
                if (i >= input.Length) break;

                if (input[i] != '.')
                {
                    // Non-tag character encountered! Not a valid tag sequence.
                    return false;
                }

                i++; // skip '.'
                if (i >= input.Length) return false;

                // Read tag identifier [a-zA-Z_][a-zA-Z0-9_]*
                int nameStart = i;
                if (!char.IsLetter(input[i]) && input[i] != '_')
                {
                    return false;
                }
                while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] == '_'))
                {
                    i++;
                }

                string tagName = input.Substring(nameStart, i - nameStart);
                var tag = new TagData(tagName);

                // Optional arguments (...)
                if (i < input.Length && input[i] == '(')
                {
                    i++; // skip '('
                    if (!TryParseArguments(input, ref i, tag))
                    {
                        return false;
                    }
                }

                tags.Add(tag);
            }

            endIndex = i;
            return tags.Count > 0;
        }

        public static bool TryParseArguments(string input, ref int i, TagData tag)
        {
            while (i < input.Length && input[i] != ')')
            {
                // Skip whitespace and commas
                while (i < input.Length && (char.IsWhiteSpace(input[i]) || input[i] == ',')) i++;
                if (i >= input.Length || input[i] == ')') break;

                // Parse single argument (positional or named: key = value)
                string? key = null;
                object? val = null;

                // Check if it's a string literal
                if (input[i] == '"' || input[i] == '\'')
                {
                    if (!TryReadStringLiteral(input, ref i, out string lit))
                    {
                        return false;
                    }
                    val = lit;
                }
                else
                {
                    // Read identifier or expression until '=', ',', or ')'
                    var tokenSb = new StringBuilder();
                    while (i < input.Length && input[i] != '=' && input[i] != ',' && input[i] != ')')
                    {
                        tokenSb.Append(input[i]);
                        i++;
                    }

                    var token = tokenSb.ToString().Trim();

                    // If followed by '=', this token was a key
                    if (i < input.Length && input[i] == '=')
                    {
                        key = token;
                        i++; // skip '='
                        while (i < input.Length && char.IsWhiteSpace(input[i])) i++;

                        // Read value
                        if (i < input.Length && (input[i] == '"' || input[i] == '\''))
                        {
                            if (!TryReadStringLiteral(input, ref i, out string lit))
                            {
                                return false;
                            }
                            val = lit;
                        }
                        else
                        {
                            var valSb = new StringBuilder();
                            while (i < input.Length && input[i] != ',' && input[i] != ')')
                            {
                                valSb.Append(input[i]);
                                i++;
                            }
                            val = ParseScalar(valSb.ToString().Trim());
                        }
                    }
                    else
                    {
                        val = ParseScalar(token);
                    }
                }

                if (!string.IsNullOrEmpty(key))
                {
                    tag.NamedArgs[key!] = val ?? string.Empty;
                }
                else if (val != null)
                {
                    tag.PositionalArgs.Add(val);
                }

                while (i < input.Length && (char.IsWhiteSpace(input[i]) || input[i] == ',')) i++;
            }

            if (i < input.Length && input[i] == ')')
            {
                i++; // skip ')'
                return true;
            }

            return false; // missing closing parenthesis
        }

        private static bool TryReadStringLiteral(string input, ref int i, out string result)
        {
            result = string.Empty;
            if (i >= input.Length) return false;
            char quote = input[i++];
            var sb = new StringBuilder();
            while (i < input.Length)
            {
                if (input[i] == '\\' && i + 1 < input.Length)
                {
                    sb.Append(input[i + 1]);
                    i += 2;
                    continue;
                }
                if (input[i] == quote)
                {
                    i++; // skip closing quote
                    result = sb.ToString();
                    return true;
                }
                sb.Append(input[i]);
                i++;
            }
            return false;
        }

        private static void ParseArguments(string input, ref int i, TagData tag)
        {
            while (i < input.Length && input[i] != ')')
            {
                // Skip whitespace and commas
                while (i < input.Length && (char.IsWhiteSpace(input[i]) || input[i] == ',')) i++;
                if (i >= input.Length || input[i] == ')') break;

                // Parse single argument (positional or named: key = value)
                int start = i;
                string? key = null;
                object? val = null;

                // Check if it's a string literal
                if (input[i] == '"' || input[i] == '\'')
                {
                    val = ReadStringLiteral(input, ref i);
                }
                else
                {
                    // Read identifier or expression until '=', ',', or ')'
                    var tokenSb = new StringBuilder();
                    while (i < input.Length && input[i] != '=' && input[i] != ',' && input[i] != ')')
                    {
                        tokenSb.Append(input[i]);
                        i++;
                    }

                    var token = tokenSb.ToString().Trim();

                    // If followed by '=', this token was a key
                    if (i < input.Length && input[i] == '=')
                    {
                        key = token;
                        i++; // skip '='
                        while (i < input.Length && char.IsWhiteSpace(input[i])) i++;

                        // Read value
                        if (i < input.Length && (input[i] == '"' || input[i] == '\''))
                        {
                            val = ReadStringLiteral(input, ref i);
                        }
                        else
                        {
                            var valSb = new StringBuilder();
                            while (i < input.Length && input[i] != ',' && input[i] != ')')
                            {
                                valSb.Append(input[i]);
                                i++;
                            }
                            val = ParseScalar(valSb.ToString().Trim());
                        }
                    }
                    else
                    {
                        val = ParseScalar(token);
                    }
                }

                if (!string.IsNullOrEmpty(key))
                {
                    tag.NamedArgs[key!] = val ?? string.Empty;
                }
                else if (val != null)
                {
                    tag.PositionalArgs.Add(val);
                }

                while (i < input.Length && (char.IsWhiteSpace(input[i]) || input[i] == ',')) i++;
            }

            if (i < input.Length && input[i] == ')')
            {
                i++; // skip ')'
            }
        }

        private static string ReadStringLiteral(string input, ref int i)
        {
            char quote = input[i++];
            var sb = new StringBuilder();
            while (i < input.Length)
            {
                if (input[i] == '\\' && i + 1 < input.Length)
                {
                    sb.Append(input[i + 1]);
                    i += 2;
                    continue;
                }
                if (input[i] == quote)
                {
                    i++; // skip closing quote
                    break;
                }
                sb.Append(input[i]);
                i++;
            }
            return sb.ToString();
        }

        private static object ParseScalar(string raw)
        {
            if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(raw, "null", StringComparison.OrdinalIgnoreCase)) return "null";

            if (long.TryParse(raw, out var l)) return l;
            if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;

            return raw;
        }
    }
}

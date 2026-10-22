using System;
using System.Globalization;
using Ktory.Core.Common;

namespace Ktory.Core.Ast
{
    internal static class BuiltinArguments
    {
        internal static void Check(TagData tag, int line, params string[] allowed)
        {
            if (tag.PositionalArgs.Count != 0)
                throw Error(tag, line, "uses named arguments; migrate positional arguments to their explicit parameter names");
            if (tag.DuplicateNamedArgs.Count > 0)
                throw Error(tag, line, $"repeats argument '{tag.DuplicateNamedArgs[0]}'");
            foreach (var name in tag.NamedArgs.Keys)
            {
                bool found = false;
                foreach (var candidate in allowed)
                    if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
                if (!found) throw Error(tag, line, $"does not accept argument '{name}'");
            }
        }

        internal static string? String(TagData tag, string name, int line, bool required = false, bool allowEmpty = false)
        {
            if (!tag.NamedArgs.TryGetValue(name, out var value))
            {
                if (required) throw Error(tag, line, $"requires '{name}'");
                return null;
            }
            if (!(value is string text) || (!allowEmpty && string.IsNullOrWhiteSpace(text)))
                throw Error(tag, line, $"requires '{name}' to be a {(allowEmpty ? "" : "non-empty ")}string");
            return text;
        }

        internal static double? Number(TagData tag, string name, int line, bool required = false, bool nonnegative = false)
        {
            if (!tag.NamedArgs.TryGetValue(name, out var value))
            {
                if (required) throw Error(tag, line, $"requires '{name}'");
                return null;
            }
            double number;
            if (value is string || value is bool || value == null)
                throw Error(tag, line, $"requires '{name}' to be a finite number");
            try { number = Convert.ToDouble(value, CultureInfo.InvariantCulture); }
            catch { throw Error(tag, line, $"requires '{name}' to be a finite number"); }
            if (double.IsNaN(number) || double.IsInfinity(number) || (nonnegative && number < 0))
                throw Error(tag, line, $"requires '{name}' to be a finite {(nonnegative ? "non-negative " : "")}number");
            return number;
        }

        internal static KtoryException Error(TagData tag, int line, string message) =>
            new KtoryException($".{tag.Name} {message}.", line, 1);
    }
}

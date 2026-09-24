using System;
using System.Collections.Generic;

namespace Ktory.Core.Ast
{
    public class TagData
    {
        public string Name { get; set; } = string.Empty;
        public List<object> PositionalArgs { get; set; } = new List<object>();
        public Dictionary<string, object> NamedArgs { get; set; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public TagData() { }

        public TagData(string name)
        {
            Name = name;
        }

        public TagData(string name, params object[] positionalArgs)
        {
            Name = name;
            PositionalArgs = new List<object>(positionalArgs);
        }

        public bool HasArg(string key) => NamedArgs.ContainsKey(key);

        public T? GetNamed<T>(string key, T? defaultValue = default)
        {
            if (NamedArgs.TryGetValue(key, out var val))
            {
                return ConvertValue<T>(val);
            }
            return defaultValue;
        }

        public T? GetPositional<T>(int index, T? defaultValue = default)
        {
            if (index >= 0 && index < PositionalArgs.Count)
            {
                return ConvertValue<T>(PositionalArgs[index]);
            }
            return defaultValue;
        }

        private static T? ConvertValue<T>(object? val)
        {
            if (val == null) return default;
            if (val is T typed) return typed;
            try
            {
                return (T)Convert.ChangeType(val, typeof(T));
            }
            catch
            {
                return default;
            }
        }

        public override string ToString()
        {
            var parts = new List<string>();
            foreach (var pos in PositionalArgs)
            {
                parts.Add(pos is string s ? $"\"{s}\"" : pos?.ToString() ?? "null");
            }
            foreach (var kvp in NamedArgs)
            {
                var v = kvp.Value is string s ? $"\"{s}\"" : kvp.Value?.ToString() ?? "null";
                parts.Add($"{kvp.Key}={v}");
            }
            return parts.Count > 0 ? $".{Name}({string.Join(", ", parts)})" : $".{Name}";
        }
    }
}

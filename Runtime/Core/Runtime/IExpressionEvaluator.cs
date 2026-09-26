using System;
using System.Collections.Generic;

namespace Ktory.Core.Runtime
{
    public interface IExpressionEvaluator
    {
        bool EvaluateCondition(string expression);
    }

    /// <summary>
    /// Default expression evaluator. In standalone runner mode, unknown external conditions
    /// are treated as true so authors can preview all choices.
    /// </summary>
    public class DefaultExpressionEvaluator : IExpressionEvaluator
    {
        private readonly Dictionary<string, bool> _variables = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        public bool IgnoreUnknownConditions { get; set; } = true;

        public void SetVariable(string name, bool value)
        {
            _variables[name] = value;
        }

        public bool EvaluateCondition(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression)) return true;

            var trimmed = expression.Trim();
            if (string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase) || trimmed == "1") return true;
            if (string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase) || trimmed == "0") return false;

            // Check if variable is defined
            if (_variables.TryGetValue(trimmed, out var val))
            {
                return val;
            }

            // Check inverted "not expr"
            if (trimmed.StartsWith("not ", StringComparison.OrdinalIgnoreCase))
            {
                var sub = trimmed.Substring(4).Trim();
                if (_variables.TryGetValue(sub, out var subVal))
                {
                    return !subVal;
                }
                if (IgnoreUnknownConditions) return true; // in runner, show option
            }

            // By default in preview mode, unknown external conditions evaluate to true to display all choices
            return IgnoreUnknownConditions;
        }
    }
}

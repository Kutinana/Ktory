using System;
using System.Collections.Generic;

namespace Ktory.Core.Runtime
{
    public interface IExpressionEvaluator
    {
        bool EvaluateCondition(string expression);
    }

    /// <summary>
    /// Basic known-boolean evaluator. Games reject unknown conditions by default;
    /// standalone preview hosts explicitly opt into showing unknown conditions.
    /// </summary>
    public class DefaultExpressionEvaluator : IExpressionEvaluator
    {
        private readonly Dictionary<string, bool> _variables = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        public bool IgnoreUnknownConditions { get; set; }

        public void SetVariable(string name, bool value)
        {
            _variables[name] = value;
        }

        public bool EvaluateCondition(string expression)
            => TryEvaluateCondition(expression, out var result) ? result : IgnoreUnknownConditions;

        /// <summary>Returns false when the expression cannot be resolved, distinct from a known false result.</summary>
        public bool TryEvaluateCondition(string expression, out bool result)
        {
            result = true;
            if (string.IsNullOrWhiteSpace(expression)) return true;

            var trimmed = expression.Trim();
            if (string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase) || trimmed == "1") return true;
            if (string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase) || trimmed == "0")
            {
                result = false;
                return true;
            }

            // Check if variable is defined
            if (_variables.TryGetValue(trimmed, out var val))
            {
                result = val;
                return true;
            }

            // Check inverted "not expr"
            if (trimmed.StartsWith("not ", StringComparison.OrdinalIgnoreCase))
            {
                var sub = trimmed.Substring(4).Trim();
                if (_variables.TryGetValue(sub, out var subVal))
                {
                    result = !subVal;
                    return true;
                }
            }

            result = false;
            return false;
        }
    }
}

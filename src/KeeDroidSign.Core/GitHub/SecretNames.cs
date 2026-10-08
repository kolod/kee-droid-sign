using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace KeeDroidSign.Core.GitHub
{
    /// <summary>GitHub Actions secret naming rules (FR-024).</summary>
    public static class SecretNames
    {
        private static readonly Regex NamePattern = new Regex("^[A-Za-z_][A-Za-z0-9_]*$");

        public static void Validate(SecretMapping mapping)
        {
            if (mapping == null) throw new ArgumentNullException(nameof(mapping));

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in mapping.AllNames())
            {
                Validate(name);
                if (!seen.Add(name))
                    throw new ArgumentException($"The secret name '{name}' is used more than once.");
            }
        }

        public static void Validate(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("A secret name is required.");
            if (!NamePattern.IsMatch(name))
                throw new ArgumentException(
                    $"The secret name '{name}' is invalid: use Latin letters, digits and '_', and do not start with a digit.");
            if (name.StartsWith("GITHUB_", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"The secret name '{name}' must not start with 'GITHUB_'.");
        }
    }
}

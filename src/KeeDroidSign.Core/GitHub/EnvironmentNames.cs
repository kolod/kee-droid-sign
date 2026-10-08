using System;
using System.Linq;

namespace KeeDroidSign.Core.GitHub
{
    /// <summary>
    /// Rules for GitHub deployment environment names accepted by the plugin. GitHub itself also allows
    /// '/', but .NET Framework may unescape "%2F" in request paths, so such names are rejected.
    /// </summary>
    public static class EnvironmentNames
    {
        public const int MaxLength = 255;

        public static bool IsValid(string name) => Problem(name) == null;

        /// <summary>Throws <see cref="ArgumentException"/> describing why the name is not accepted.</summary>
        public static void Validate(string name)
        {
            string problem = Problem(name);
            if (problem != null)
                throw new ArgumentException(problem);
        }

        private static string Problem(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "The environment name must not be empty.";
            if (name.Length > MaxLength)
                return $"The environment name must not be longer than {MaxLength} characters.";
            if (name.Trim() != name)
                return "The environment name must not start or end with spaces.";
            if (name.Contains('/'))
                return "The environment name must not contain '/'.";
            if (name.Any(char.IsControl))
                return "The environment name must not contain control characters.";
            return null;
        }
    }
}

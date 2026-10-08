using System;
using System.Text.RegularExpressions;

namespace KeeDroidSign.Core.GitHub
{
    /// <summary>A GitHub repository identified by owner and name.</summary>
    public sealed class RepositoryTarget
    {
        private static readonly Regex OwnerPattern = new Regex("^[A-Za-z0-9-]{1,39}$");
        private static readonly Regex NamePattern = new Regex("^[A-Za-z0-9._-]{1,100}$");
        private const string GitHubPrefix = "https://github.com/";

        public RepositoryTarget(string owner, string name)
        {
            if (owner == null || !OwnerPattern.IsMatch(owner))
                throw new ArgumentException("The repository owner must be 1-39 characters: Latin letters, digits or '-'.");
            if (name == null || !NamePattern.IsMatch(name) || name == "." || name == "..")
                throw new ArgumentException("The repository name must be 1-100 characters: Latin letters, digits, '.', '_' or '-'.");

            Owner = owner;
            Name = name;
        }

        public string Owner { get; }
        public string Name { get; }

        /// <summary>Accepts <c>owner/name</c> or <c>https://github.com/owner/name(.git)</c>.</summary>
        public static RepositoryTarget Parse(string value)
        {
            if (value == null)
                throw new ArgumentException("The repository is required.");

            string text = value.Trim();
            if (text.StartsWith(GitHubPrefix, StringComparison.OrdinalIgnoreCase))
                text = text.Substring(GitHubPrefix.Length);
            else if (text.Contains("://"))
                throw new ArgumentException("Only github.com repositories are supported.");

            text = text.TrimEnd('/');
            if (text.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(0, text.Length - 4);

            string[] parts = text.Split('/');
            if (parts.Length != 2)
                throw new ArgumentException("The repository must be given as 'owner/name'.");

            return new RepositoryTarget(parts[0], parts[1]);
        }

        public static bool TryParse(string value, out RepositoryTarget target)
        {
            try
            {
                target = Parse(value);
                return true;
            }
            catch (ArgumentException)
            {
                target = null;
                return false;
            }
        }

        public override string ToString() => Owner + "/" + Name;
    }
}

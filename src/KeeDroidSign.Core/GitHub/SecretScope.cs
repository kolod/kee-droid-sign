using System;

namespace KeeDroidSign.Core.GitHub
{
    /// <summary>
    /// Where Actions secrets are read and written: the repository itself (readable by every workflow)
    /// or one of its deployment environments (readable only by jobs that use the environment).
    /// </summary>
    public sealed class SecretScope
    {
        public SecretScope(RepositoryTarget repository, string environment = null)
        {
            Repository = repository ?? throw new ArgumentNullException(nameof(repository));
            if (environment != null)
                EnvironmentNames.Validate(environment);
            Environment = environment;
        }

        public RepositoryTarget Repository { get; }

        /// <summary>The environment name, or null for repository-level secrets.</summary>
        public string Environment { get; }

        public bool IsEnvironment => Environment != null;

        public static SecretScope ForRepository(RepositoryTarget repository) => new SecretScope(repository);

        /// <summary>Relative API path of the secrets collection of this scope.</summary>
        internal string SecretsPath => IsEnvironment
            ? EnvironmentPath(Repository, Environment) + "/secrets"
            : GitHubClient.RepoPath(Repository) + "/actions/secrets";

        internal static string EnvironmentPath(RepositoryTarget repository, string environment) =>
            GitHubClient.RepoPath(repository) + "/environments/" + Uri.EscapeDataString(environment);

        public override string ToString() =>
            IsEnvironment ? $"{Repository}, environment {Environment}" : Repository.ToString();
    }
}

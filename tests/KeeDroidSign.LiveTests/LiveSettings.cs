using System;
using KeeDroidSign.Core.GitHub;

namespace KeeDroidSign.LiveTests
{
    /// <summary>Live test configuration from environment variables (contracts/live-test.md).</summary>
    internal sealed class LiveSettings
    {
        public const string TokenVariable = "KDS_LIVE_GITHUB_TOKEN";

        /// <summary>Value of <c>KDS_LIVE_ENVIRONMENT</c> that selects repository-level secrets.</summary>
        public const string RepositoryLevel = "-";

        private LiveSettings(string token, RepositoryTarget repository, string environment, string gitRef, TimeSpan timeout)
        {
            Token = token;
            Repository = repository;
            Scope = new SecretScope(repository, environment);
            Ref = gitRef;
            Timeout = timeout;
        }

        public string Token { get; }
        public RepositoryTarget Repository { get; }

        /// <summary>Where the secrets are exported: environment "release" by default.</summary>
        public SecretScope Scope { get; }

        public string Ref { get; }
        public TimeSpan Timeout { get; }

        public static bool IsEnabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TokenVariable));

        public static LiveSettings Load()
        {
            string token = Environment.GetEnvironmentVariable(TokenVariable);
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException($"{TokenVariable} is not set.");

            string repository = Environment.GetEnvironmentVariable("KDS_LIVE_REPOSITORY");
            string environment = Environment.GetEnvironmentVariable("KDS_LIVE_ENVIRONMENT");
            environment = string.IsNullOrWhiteSpace(environment) ? "release" : environment.Trim();
            string gitRef = Environment.GetEnvironmentVariable("KDS_LIVE_REF");
            string minutes = Environment.GetEnvironmentVariable("KDS_LIVE_TIMEOUT_MINUTES");

            return new LiveSettings(
                token.Trim(),
                RepositoryTarget.Parse(string.IsNullOrWhiteSpace(repository) ? "kolod/kee-droid-sign" : repository),
                environment == RepositoryLevel ? null : environment,
                string.IsNullOrWhiteSpace(gitRef) ? "main" : gitRef.Trim(),
                TimeSpan.FromMinutes(int.TryParse(minutes, out int m) && m > 0 ? m : 20));
        }

        public override string ToString() =>
            $"LiveSettings(Scope={Scope}, Ref={Ref}, Timeout={Timeout.TotalMinutes} min, Token=***)";
    }
}

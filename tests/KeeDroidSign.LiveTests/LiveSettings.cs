using System;
using KeeDroidSign.Core.GitHub;

namespace KeeDroidSign.LiveTests
{
    /// <summary>Live test configuration from environment variables (contracts/live-test.md).</summary>
    internal sealed class LiveSettings
    {
        public const string TokenVariable = "KDS_LIVE_GITHUB_TOKEN";

        private LiveSettings(string token, RepositoryTarget repository, string gitRef, TimeSpan timeout)
        {
            Token = token;
            Repository = repository;
            Ref = gitRef;
            Timeout = timeout;
        }

        public string Token { get; }
        public RepositoryTarget Repository { get; }
        public string Ref { get; }
        public TimeSpan Timeout { get; }

        public static bool IsEnabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TokenVariable));

        public static LiveSettings Load()
        {
            string token = Environment.GetEnvironmentVariable(TokenVariable);
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException($"{TokenVariable} is not set.");

            string repository = Environment.GetEnvironmentVariable("KDS_LIVE_REPOSITORY");
            string gitRef = Environment.GetEnvironmentVariable("KDS_LIVE_REF");
            string minutes = Environment.GetEnvironmentVariable("KDS_LIVE_TIMEOUT_MINUTES");

            return new LiveSettings(
                token.Trim(),
                RepositoryTarget.Parse(string.IsNullOrWhiteSpace(repository) ? "kolod/kee-droid-sign" : repository),
                string.IsNullOrWhiteSpace(gitRef) ? "main" : gitRef.Trim(),
                TimeSpan.FromMinutes(int.TryParse(minutes, out int m) && m > 0 ? m : 20));
        }

        public override string ToString() =>
            $"LiveSettings(Repository={Repository}, Ref={Ref}, Timeout={Timeout.TotalMinutes} min, Token=***)";
    }
}

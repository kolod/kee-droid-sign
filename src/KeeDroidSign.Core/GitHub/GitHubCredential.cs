using System;

namespace KeeDroidSign.Core.GitHub
{
    /// <summary>
    /// A GitHub personal access token. It is stored by the caller in the KeePass database only and
    /// is never logged or persisted by the core (FR-019).
    /// </summary>
    public sealed class GitHubCredential
    {
        public GitHubCredential(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("The GitHub personal access token is required.", nameof(token));
            Token = token.Trim();
        }

        public string Token { get; }

        public override string ToString() => $"GitHubCredential(Token={Redaction.Placeholder})";
    }

    /// <summary>The account a token belongs to.</summary>
    public sealed class GitHubIdentity
    {
        public GitHubIdentity(string login, string[] tokenScopes)
        {
            Login = login;
            TokenScopes = tokenScopes ?? new string[0];
        }

        public string Login { get; }

        /// <summary>Scopes of a classic token (<c>X-OAuth-Scopes</c>); empty for fine-grained tokens.</summary>
        public string[] TokenScopes { get; }

        public override string ToString() => $"GitHubIdentity(Login={Login})";
    }
}

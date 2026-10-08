using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KeeDroidSign.Core.GitHub;
using KeeDroidSign.Core.Keystore;
using KeeDroidSign.Settings;
using KeeDroidSign.Storage;
using KeePassLib;
using KeePassLib.Utility;

namespace KeeDroidSign.Services
{
    /// <summary>
    /// Exports one key's secrets to the repository in its keystore entry, or to an environment of
    /// it (the app's own target, else the default from the settings). The token is read from the
    /// KeePass entry selected in the settings at the moment of use (spec FR-013).
    /// </summary>
    public sealed class ExportService
    {
        private readonly Func<PluginSettings> _settings;
        private readonly HttpMessageHandler _handler;

        public ExportService(Func<PluginSettings> settings)
            : this(settings, null)
        {
        }

        /// <param name="settings">Reads the current settings at the moment of use.</param>
        /// <param name="handler">Optional HTTP handler (tests inject a fake).</param>
        public ExportService(Func<PluginSettings> settings, HttpMessageHandler handler)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            _settings = settings;
            _handler = handler;
        }

        /// <summary>The token from the configured entry of <paramref name="database"/>, or null.</summary>
        public GitHubCredential ResolveToken(PwDatabase database)
        {
            string hex = _settings().TokenEntryUuid;
            if (database == null || database.RootGroup == null || string.IsNullOrEmpty(hex)) return null;

            PwUuid uuid;
            try
            {
                uuid = new PwUuid(MemUtil.HexStringToByteArray(hex));
            }
            catch (ArgumentException)
            {
                return null;
            }

            PwEntry entry = database.RootGroup.FindEntry(uuid, true);
            string token = entry == null ? null : entry.Strings.ReadSafe(PwDefs.PasswordField);
            return string.IsNullOrWhiteSpace(token) ? null : new GitHubCredential(token);
        }

        /// <summary>Where this key's secrets go.</summary>
        public ExportTarget ResolveTarget(KeyContext key)
        {
            if (key == null) throw new ArgumentNullException("key");
            return ExportTarget.Resolve(key.App, _settings());
        }

        public async Task<ExportPlan> PlanAsync(KeyContext key, GitHubCredential token, CancellationToken ct)
        {
            EnsureExportable(key);
            SecretScope scope = ResolveTarget(key).ToScope();
            using (GitHubClient client = CreateClient(token))
                return await new SecretExporter(client).PlanAsync(scope, _settings().ToSecretMapping(), ct)
                    .ConfigureAwait(false);
        }

        public async Task<ExportResult> ExportAsync(KeyContext key, GitHubCredential token, bool overwrite, CancellationToken ct)
        {
            EnsureExportable(key);
            SigningKeystore keystore = key.ToSigningKeystore();
            SecretScope scope = ResolveTarget(key).ToScope();
            using (GitHubClient client = CreateClient(token))
                return await new SecretExporter(client)
                    .ExportAsync(scope, keystore, _settings().ToSecretMapping(), overwrite, ct)
                    .ConfigureAwait(false);
        }

        /// <summary>
        /// Repository-level secrets named like this key's secrets, which stay readable by every
        /// workflow. Always empty when the target is the repository itself.
        /// </summary>
        public async Task<RepositoryCopies> FindRepositoryCopiesAsync(KeyContext key, GitHubCredential token, CancellationToken ct)
        {
            EnsureExportable(key);
            ExportTarget target = ResolveTarget(key);
            if (!target.IsEnvironment) return new RepositoryCopies(new string[0], true);

            using (GitHubClient client = CreateClient(token))
                return await new SecretExporter(client)
                    .FindRepositoryCopiesAsync(target.Repository, _settings().ToSecretMapping(), ct)
                    .ConfigureAwait(false);
        }

        public async Task<ExportResult> DeleteRepositoryCopiesAsync(KeyContext key, GitHubCredential token,
            IEnumerable<string> names, CancellationToken ct)
        {
            EnsureExportable(key);
            using (GitHubClient client = CreateClient(token))
                return await new SecretExporter(client).DeleteRepositorySecretsAsync(key.App.Repository, names, ct)
                    .ConfigureAwait(false);
        }

        /// <summary>Read-only inspection of the target repository/environment for the export wizard.</summary>
        public async Task<RepositoryReport> InspectAsync(KeyContext key, GitHubCredential token, CancellationToken ct)
        {
            EnsureExportable(key);
            SecretScope scope = ResolveTarget(key).ToScope();
            using (GitHubClient client = CreateClient(token))
                return await new RepositoryInspector(client).InspectAsync(scope, _settings().ToSecretMapping(), ct)
                    .ConfigureAwait(false);
        }

        /// <summary>
        /// Creates or restricts this key's target environment, adding <paramref name="patterns"/>
        /// (needs the Administration permission).
        /// </summary>
        public async Task<ProtectionResult> ProtectEnvironmentAsync(KeyContext key, GitHubCredential token,
            IReadOnlyList<DeploymentPattern> patterns, CancellationToken ct)
        {
            EnsureExportable(key);
            ExportTarget target = ResolveTarget(key);
            if (!target.IsEnvironment)
                throw new InvalidOperationException("This key is exported at repository level; there is no environment to protect.");

            using (GitHubClient client = CreateClient(token))
                return await new EnvironmentProtector(client).ProtectAsync(target.Repository, target.Environment, patterns, ct)
                    .ConfigureAwait(false);
        }

        /// <summary>Adds a tag ruleset so only repository admins can create, move or delete v* tags.</summary>
        public async Task<ProtectionResult> RestrictReleaseTagsAsync(KeyContext key, GitHubCredential token, CancellationToken ct)
        {
            EnsureExportable(key);
            using (GitHubClient client = CreateClient(token))
                return await new RepositoryHardening(client).RestrictReleaseTagsAsync(key.App.Repository, ct).ConfigureAwait(false);
        }

        /// <summary>Adds a branch ruleset requiring pull requests for the default branch.</summary>
        public async Task<ProtectionResult> ProtectDefaultBranchAsync(KeyContext key, GitHubCredential token, string branch,
            CancellationToken ct)
        {
            EnsureExportable(key);
            using (GitHubClient client = CreateClient(token))
                return await new RepositoryHardening(client).ProtectDefaultBranchAsync(key.App.Repository, branch, ct).ConfigureAwait(false);
        }

        /// <summary>Removes the v* tag pattern from this key's target environment.</summary>
        public async Task<ProtectionResult> RemoveTagPatternAsync(KeyContext key, GitHubCredential token, CancellationToken ct)
        {
            EnsureExportable(key);
            ExportTarget target = ResolveTarget(key);
            if (!target.IsEnvironment)
                throw new InvalidOperationException("This key is exported at repository level; there is no environment to change.");
            using (GitHubClient client = CreateClient(token))
                return await new RepositoryHardening(client).RemoveTagPatternAsync(target.Repository, target.Environment, ct)
                    .ConfigureAwait(false);
        }

        private GitHubClient CreateClient(GitHubCredential token)
        {
            if (token == null) throw new ArgumentNullException("token");
            return new GitHubClient(token, _handler);
        }

        private static void EnsureExportable(KeyContext key)
        {
            if (key == null) throw new ArgumentNullException("key");
            if (key.Problems.Count > 0)
                throw new InvalidOperationException("This key cannot be exported: " + string.Join(", ", key.Problems) + ".");
        }
    }
}

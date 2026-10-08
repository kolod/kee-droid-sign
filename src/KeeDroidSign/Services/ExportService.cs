using System;
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
    /// Exports one key's secrets to the repository in its keystore entry. The token is read from
    /// the KeePass entry selected in the settings at the moment of use (spec FR-013).
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

        public async Task<ExportPlan> PlanAsync(KeyContext key, GitHubCredential token, CancellationToken ct)
        {
            EnsureExportable(key);
            using (GitHubClient client = CreateClient(token))
                return await new SecretExporter(client).PlanAsync(key.App.Repository, _settings().ToSecretMapping(), ct)
                    .ConfigureAwait(false);
        }

        public async Task<ExportResult> ExportAsync(KeyContext key, GitHubCredential token, bool overwrite, CancellationToken ct)
        {
            EnsureExportable(key);
            SigningKeystore keystore = key.ToSigningKeystore();
            using (GitHubClient client = CreateClient(token))
                return await new SecretExporter(client)
                    .ExportAsync(key.App.Repository, keystore, _settings().ToSecretMapping(), overwrite, ct)
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

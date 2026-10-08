using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KeeDroidSign.Core.Keystore;

namespace KeeDroidSign.Core.GitHub
{
    /// <summary>Per-secret outcome of an export.</summary>
    public enum SecretOutcomeStatus
    {
        Created,
        Updated,
        Failed,

        /// <summary>A repository-level copy was deleted after an environment export.</summary>
        Deleted,

        /// <summary>Not written because existing secrets would be overwritten without confirmation.</summary>
        SkippedConflict,
    }

    /// <summary>Which secrets an export would create and which it would overwrite.</summary>
    public sealed class ExportPlan
    {
        public ExportPlan(IReadOnlyList<string> toCreate, IReadOnlyList<string> toOverwrite)
            : this(toCreate, toOverwrite, null, null)
        {
        }

        public ExportPlan(IReadOnlyList<string> toCreate, IReadOnlyList<string> toOverwrite, SecretScope scope,
            EnvironmentStatus environment)
        {
            ToCreate = toCreate;
            ToOverwrite = toOverwrite;
            Scope = scope;
            Environment = environment;
        }

        public IReadOnlyList<string> ToCreate { get; }
        public IReadOnlyList<string> ToOverwrite { get; }

        /// <summary>Where the secrets will be written; null in plans built by callers for display only.</summary>
        public SecretScope Scope { get; }

        /// <summary>Status of the target environment; null for repository-level exports.</summary>
        public EnvironmentStatus Environment { get; }
    }

    /// <summary>Repository-level secrets that share a name with the exported environment secrets.</summary>
    public sealed class RepositoryCopies
    {
        public RepositoryCopies(IReadOnlyList<string> names, bool @checked)
        {
            Names = names;
            Checked = @checked;
        }

        public IReadOnlyList<string> Names { get; }

        /// <summary>False when the token may not list repository secrets, so copies could not be checked.</summary>
        public bool Checked { get; }
    }

    /// <summary>What happened to one secret during an export.</summary>
    public sealed class SecretOutcome
    {
        public SecretOutcome(string name, SecretOutcomeStatus status, string reason = null)
        {
            Name = name;
            Status = status;
            Reason = reason;
        }

        public string Name { get; }
        public SecretOutcomeStatus Status { get; }

        /// <summary>Fixed English reason for failures and skips; never contains secret values.</summary>
        public string Reason { get; }

        public override string ToString() => Reason == null ? $"{Name}: {Status}" : $"{Name}: {Status} ({Reason})";
    }

    /// <summary>Per-secret outcomes of an export.</summary>
    public sealed class ExportResult
    {
        public ExportResult(IReadOnlyList<SecretOutcome> outcomes)
        {
            Outcomes = outcomes;
        }

        public IReadOnlyList<SecretOutcome> Outcomes { get; }

        /// <summary>True when every secret was created or updated (or, for a cleanup, deleted).</summary>
        public bool Succeeded =>
            Outcomes.All(o => o.Status == SecretOutcomeStatus.Created || o.Status == SecretOutcomeStatus.Updated ||
                o.Status == SecretOutcomeStatus.Deleted);

        public override string ToString() => string.Join("; ", Outcomes);
    }

    /// <summary>
    /// Exports a signing keystore and its credentials as Actions secrets of a repository or of one of
    /// its environments. Existing secrets are overwritten only when the caller confirms (FR-025).
    /// </summary>
    public sealed class SecretExporter
    {
        /// <summary>GitHub's maximum secret size.</summary>
        public const int MaxSecretBytes = 48 * 1024;

        private readonly GitHubClient _client;

        public SecretExporter(GitHubClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public Task<ExportPlan> PlanAsync(RepositoryTarget repo, SecretMapping mapping, CancellationToken ct) =>
            PlanAsync(SecretScope.ForRepository(repo), mapping, ct);

        /// <summary>
        /// Which secrets would be created or overwritten. For an environment, first checks that it
        /// exists (throws <see cref="EnvironmentNotFoundException"/> otherwise) and reads its branch policy.
        /// </summary>
        public async Task<ExportPlan> PlanAsync(SecretScope scope, SecretMapping mapping, CancellationToken ct)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            SecretNames.Validate(mapping);

            EnvironmentStatus environment = null;
            if (scope.IsEnvironment)
            {
                // The public key needs the same permission as the export and fails clearly if the
                // environment is missing; the policy read needs Actions: read and must not block.
                await _client.GetPublicKeyAsync(scope, ct).ConfigureAwait(false);
                EnvironmentInfo info = await _client.GetEnvironmentAsync(scope.Repository, scope.Environment, ct).ConfigureAwait(false);
                environment = new EnvironmentStatus(EnvironmentExistence.Found,
                    info.Existence == EnvironmentExistence.Found ? info.Policy : BranchPolicyKind.Unknown);
            }

            var existing = new HashSet<string>(
                await _client.ListSecretNamesAsync(scope, ct).ConfigureAwait(false), StringComparer.OrdinalIgnoreCase);

            string[] names = mapping.AllNames();
            return new ExportPlan(
                names.Where(n => !existing.Contains(n)).ToList(),
                names.Where(n => existing.Contains(n)).ToList(),
                scope,
                environment);
        }

        public Task<ExportResult> ExportAsync(RepositoryTarget repo, SigningKeystore keystore, SecretMapping mapping,
            bool overwrite, CancellationToken ct) =>
            ExportAsync(SecretScope.ForRepository(repo), keystore, mapping, overwrite, ct);

        public async Task<ExportResult> ExportAsync(SecretScope scope, SigningKeystore keystore, SecretMapping mapping,
            bool overwrite, CancellationToken ct)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (keystore == null) throw new ArgumentNullException(nameof(keystore));
            SecretNames.Validate(mapping);

            var values = new[]
            {
                (Name: mapping.KeystoreBase64Name, Value: keystore.ToBase64()),
                (Name: mapping.StorePasswordName, Value: keystore.StorePassword),
                (Name: mapping.KeyAliasName, Value: keystore.Alias),
                (Name: mapping.KeyPasswordName, Value: keystore.KeyPassword),
            };
            foreach (var secret in values)
            {
                if (string.IsNullOrEmpty(secret.Value))
                    throw new ArgumentException($"The value for secret '{secret.Name}' is empty.");
                if (Encoding.UTF8.GetByteCount(secret.Value) > MaxSecretBytes)
                    throw new ArgumentException($"The value for secret '{secret.Name}' exceeds GitHub's 48 KB limit.");
            }
            ct.ThrowIfCancellationRequested();

            ExportPlan plan = await PlanAsync(scope, mapping, ct).ConfigureAwait(false);
            if (plan.ToOverwrite.Count > 0 && !overwrite)
            {
                return new ExportResult(values.Select(v => new SecretOutcome(v.Name, SecretOutcomeStatus.SkippedConflict,
                    "Not written: existing secrets would be overwritten without confirmation.")).ToList());
            }

            RepositoryPublicKey publicKey = await _client.GetPublicKeyAsync(scope, ct).ConfigureAwait(false);

            var outcomes = new List<SecretOutcome>();
            foreach (var secret in values)
            {
                ct.ThrowIfCancellationRequested();

                string encrypted = Convert.ToBase64String(SealedBox.Seal(Encoding.UTF8.GetBytes(secret.Value), publicKey.Key));
                SecretWriteResult write = await _client.PutSecretAsync(scope, secret.Name, encrypted, publicKey.KeyId, ct)
                    .ConfigureAwait(false);

                outcomes.Add(new SecretOutcome(secret.Name, Map(write.Status), write.Reason));
            }
            return new ExportResult(outcomes);
        }

        /// <summary>
        /// Repository-level secrets named like the mapping's secrets. A token that may not list
        /// repository secrets yields <c>Checked = false</c> instead of an error.
        /// </summary>
        public async Task<RepositoryCopies> FindRepositoryCopiesAsync(RepositoryTarget repo, SecretMapping mapping, CancellationToken ct)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            SecretNames.Validate(mapping);

            IReadOnlyCollection<string> existing;
            try
            {
                existing = await _client.ListSecretNamesAsync(repo, ct).ConfigureAwait(false);
            }
            catch (GitHubApiException)
            {
                return new RepositoryCopies(new string[0], false);
            }

            var set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
            return new RepositoryCopies(mapping.AllNames().Where(set.Contains).ToList(), true);
        }

        /// <summary>Deletes the given repository-level secrets; one outcome (Deleted or Failed) per name.</summary>
        public async Task<ExportResult> DeleteRepositorySecretsAsync(RepositoryTarget repo, IEnumerable<string> names, CancellationToken ct)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            if (names == null) throw new ArgumentNullException(nameof(names));

            var outcomes = new List<SecretOutcome>();
            foreach (string name in names)
            {
                ct.ThrowIfCancellationRequested();
                SecretDeleteResult delete = await _client.DeleteSecretAsync(repo, name, ct).ConfigureAwait(false);
                outcomes.Add(delete.Deleted
                    ? new SecretOutcome(name, SecretOutcomeStatus.Deleted)
                    : new SecretOutcome(name, SecretOutcomeStatus.Failed, delete.Reason));
            }
            return new ExportResult(outcomes);
        }

        private static SecretOutcomeStatus Map(SecretWriteStatus status)
        {
            switch (status)
            {
                case SecretWriteStatus.Created: return SecretOutcomeStatus.Created;
                case SecretWriteStatus.Updated: return SecretOutcomeStatus.Updated;
                default: return SecretOutcomeStatus.Failed;
            }
        }
    }
}

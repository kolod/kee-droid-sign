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

        /// <summary>Not written because existing secrets would be overwritten without confirmation.</summary>
        SkippedConflict,
    }

    /// <summary>Which secrets an export would create and which it would overwrite.</summary>
    public sealed class ExportPlan
    {
        public ExportPlan(IReadOnlyList<string> toCreate, IReadOnlyList<string> toOverwrite)
        {
            ToCreate = toCreate;
            ToOverwrite = toOverwrite;
        }

        public IReadOnlyList<string> ToCreate { get; }
        public IReadOnlyList<string> ToOverwrite { get; }
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

        /// <summary>True when every secret was created or updated.</summary>
        public bool Succeeded =>
            Outcomes.All(o => o.Status == SecretOutcomeStatus.Created || o.Status == SecretOutcomeStatus.Updated);

        public override string ToString() => string.Join("; ", Outcomes);
    }

    /// <summary>
    /// Exports a signing keystore and its credentials as repository Actions secrets (User Story 5).
    /// Existing secrets are overwritten only when the caller confirms (FR-025).
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

        public async Task<ExportPlan> PlanAsync(RepositoryTarget repo, SecretMapping mapping, CancellationToken ct)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            SecretNames.Validate(mapping);

            var existing = new HashSet<string>(
                await _client.ListSecretNamesAsync(repo, ct).ConfigureAwait(false), StringComparer.OrdinalIgnoreCase);

            string[] names = mapping.AllNames();
            return new ExportPlan(
                names.Where(n => !existing.Contains(n)).ToList(),
                names.Where(n => existing.Contains(n)).ToList());
        }

        public async Task<ExportResult> ExportAsync(RepositoryTarget repo, SigningKeystore keystore, SecretMapping mapping,
            bool overwrite, CancellationToken ct)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
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

            ExportPlan plan = await PlanAsync(repo, mapping, ct).ConfigureAwait(false);
            if (plan.ToOverwrite.Count > 0 && !overwrite)
            {
                return new ExportResult(values.Select(v => new SecretOutcome(v.Name, SecretOutcomeStatus.SkippedConflict,
                    "Not written: existing secrets would be overwritten without confirmation.")).ToList());
            }

            RepositoryPublicKey publicKey = await _client.GetPublicKeyAsync(repo, ct).ConfigureAwait(false);

            var outcomes = new List<SecretOutcome>();
            foreach (var secret in values)
            {
                ct.ThrowIfCancellationRequested();

                string encrypted = Convert.ToBase64String(SealedBox.Seal(Encoding.UTF8.GetBytes(secret.Value), publicKey.Key));
                SecretWriteResult write = await _client.PutSecretAsync(repo, secret.Name, encrypted, publicKey.KeyId, ct)
                    .ConfigureAwait(false);

                outcomes.Add(new SecretOutcome(secret.Name, Map(write.Status), write.Reason));
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

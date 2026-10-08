# Contract: KeeDroidSign.Core public API

The plugin UI layer (future features) consumes only these types. Signatures are normative;
bodies are implementation detail. All async methods accept a `CancellationToken` (FR-027) and
throw `OperationCanceledException` on cancellation.

## Passwords

```csharp
namespace KeeDroidSign.Core.Passwords
{
    public sealed class PasswordPolicy { /* see data-model.md */ public void Validate(); }

    public interface IPasswordGenerator
    {
        string Generate(PasswordPolicy policy);           // throws ArgumentException if unsatisfiable
    }

    public sealed class PasswordGenerator : IPasswordGenerator
    {
        public PasswordGenerator();                       // CSPRNG-backed
        public const string SafeSymbols = "!#%+,-./:=?@^_~";
    }
}
```

## Keystore

```csharp
namespace KeeDroidSign.Core.Keystore
{
    public sealed class DistinguishedName { public string ToRfc4514(); } // display only
    public sealed class KeyRequest { /* see data-model.md */ public void Validate(); }

    public interface IKeystoreGenerator
    {
        Task<SigningKeystore> GenerateAsync(KeyRequest request, CancellationToken ct);
    }

    public sealed class KeystoreGenerator : IKeystoreGenerator
    {
        public KeystoreGenerator();                       // BouncyCastle, fully in-process (FR-011)
        // Overload writes a file only when explicitly asked (FR-010); refuses to overwrite an
        // existing file (KeystoreExistsException).
        public Task<SigningKeystore> GenerateAsync(KeyRequest request, string outputPath,
                                                   CancellationToken ct);
    }

    public static class KeystoreReader
    {
        // Throws InvalidKeystorePasswordException / AliasNotFoundException / InvalidKeyPasswordException
        public static SigningKeystore Open(byte[] content, string alias,
                                           string storePassword, string keyPassword);
        public static byte[] GetCertificate(byte[] content, string alias, string storePassword);
    }
}
```

## Fingerprints

```csharp
namespace KeeDroidSign.Core.Fingerprints
{
    public enum FingerprintAlgorithm { Sha256, Sha1 }

    public static class CertificateFingerprint
    {
        public static string Compute(byte[] derCertificate,
                                     FingerprintAlgorithm algorithm = FingerprintAlgorithm.Sha256);
        public static string Format(byte[] digest);       // "AA:BB:..."
    }
}
```

## GitHub

```csharp
namespace KeeDroidSign.Core.GitHub
{
    public sealed class GitHubClient : IDisposable
    {
        public GitHubClient(GitHubCredential credential, HttpMessageHandler handler = null);

        Task<TokenValidationResult> ValidateTokenAsync(CancellationToken ct);
        Task<RepositoryAccess> CheckRepositoryAccessAsync(RepositoryTarget repo, CancellationToken ct);
        Task<IReadOnlyCollection<string>> ListSecretNamesAsync(RepositoryTarget repo, CancellationToken ct); // GitHubApiException on failure
        Task<RepositoryPublicKey> GetPublicKeyAsync(RepositoryTarget repo, CancellationToken ct);           // GitHubApiException on failure
        Task<SecretWriteResult> PutSecretAsync(RepositoryTarget repo, string name,
                                               string encryptedValue, string keyId, CancellationToken ct);
    }

    public static class SealedBox
    {
        public static byte[] Seal(byte[] message, byte[] recipientPublicKey);   // crypto_box_seal
    }

    public static class SecretNames { public static void Validate(SecretMapping mapping); }

    public sealed class SecretExporter
    {
        public SecretExporter(GitHubClient client);

        Task<ExportPlan> PlanAsync(RepositoryTarget repo, SecretMapping mapping, CancellationToken ct);

        // overwrite=false and conflicts exist → returns all SkippedConflict, writes nothing (FR-025)
        Task<ExportResult> ExportAsync(RepositoryTarget repo, SigningKeystore keystore,
                                       SecretMapping mapping, bool overwrite, CancellationToken ct);
    }
}
```

## Exceptions

All derive from `KeeDroidSignException`; messages are fixed English text plus non-secret context
(alias, output path, repository, secret name). Messages never include passwords, tokens or key bytes.

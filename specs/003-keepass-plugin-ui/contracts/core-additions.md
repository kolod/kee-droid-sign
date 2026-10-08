# Contract: Core and plugin service APIs

## Core additions (`KeeDroidSign.Core`)

```csharp
namespace KeeDroidSign.Core.Keystore
{
    public static class KeystoreEditor
    {
        // Adds a new private key under request.Alias to an existing JKS keystore.
        // request.StorePassword must equal the keystore password; throws
        // InvalidKeystorePasswordException, ArgumentException("alias exists").
        public static Task<SigningKeystore> AddKeyAsync(byte[] content, KeyRequest request, CancellationToken ct);
    }

    public static partial class KeystoreReader
    {
        public static IReadOnlyList<string> ListKeyAliases(byte[] content, string storePassword);
    }
}
```

## Plugin services (`KeeDroidSign`, no WinForms dependencies)

```csharp
namespace KeeDroidSign.Storage
{
    public sealed class DroidSignStore
    {
        public DroidSignStore(PwDatabase database, PluginSettings settings);

        IReadOnlyList<AppKeystore> ListApps();
        AppKeystore FindApp(string packageId);                     // null if absent
        AppKeystore CreateApp(NewAppRequest request, SigningKeystore keystore); // keystore entry + key "1"
        KeyEntryInfo AddKey(AppKeystore app, SigningKeystore updated, int number); // backup + attachment + key entry
        static bool IsKeyEntry(PwEntry entry);
        KeyContext ResolveKey(PwEntry keyEntry);                   // keystore bytes, store pw, alias, key pw, repo
    }
}

namespace KeeDroidSign.Services
{
    public sealed class KeyService      // orchestrates core + store
    {
        Task<KeyEntryInfo> CreateAppAsync(NewAppRequest request, CancellationToken ct);
        Task<KeyEntryInfo> AddKeyAsync(AppKeystore app, DistinguishedName subject, CancellationToken ct);
        KeyDetails Describe(KeyContext key);
    }

    public sealed class ExportService
    {
        GitHubCredential ResolveToken(PwDatabase database);       // null if not configured/found
        Task<ExportPlan> PlanAsync(KeyContext key, GitHubCredential token, CancellationToken ct);
        Task<ExportResult> ExportAsync(KeyContext key, GitHubCredential token, bool overwrite, CancellationToken ct);
    }
}

namespace KeeDroidSign.Settings
{
    public interface ISettingsStore { string Get(string key); void Set(string key, string value); }
    public sealed class PluginSettings
    {
        static PluginSettings Load(ISettingsStore store);
        void Validate();                 // ArgumentException with field name
        void Save(ISettingsStore store);
    }
}
```

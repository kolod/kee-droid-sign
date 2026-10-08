using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using KeeDroidSign.Core.Keystore;
using KeeDroidSign.Core.Passwords;
using KeeDroidSign.Settings;
using KeeDroidSign.Storage;
using KeePassLib;

namespace KeeDroidSign.Services
{
    /// <summary>
    /// Orchestrates key generation (core) and the database layout (store). Settings are read at the
    /// start of each operation, so changes apply immediately. The database changes only after
    /// generation succeeded.
    /// </summary>
    public sealed partial class KeyService
    {
        private readonly PwDatabase _database;
        private readonly Func<PluginSettings> _settings;
        private readonly IPasswordGenerator _passwords;

        public KeyService(PwDatabase database, Func<PluginSettings> settings)
            : this(database, settings, new PasswordGenerator())
        {
        }

        public KeyService(PwDatabase database, Func<PluginSettings> settings, IPasswordGenerator passwords)
        {
            if (database == null) throw new ArgumentNullException("database");
            if (settings == null) throw new ArgumentNullException("settings");
            if (passwords == null) throw new ArgumentNullException("passwords");
            _database = database;
            _settings = settings;
            _passwords = passwords;
        }

        /// <summary>The database the keys are stored in.</summary>
        public PwDatabase Database { get { return _database; } }

        /// <summary>Resolves a key entry with its app (problems are reported, not thrown).</summary>
        public KeyContext ResolveKey(PwEntry keyEntry)
        {
            return new DroidSignStore(_database, _settings()).ResolveKey(keyEntry);
        }

        public async Task<KeyEntryInfo> CreateAppAsync(NewAppRequest request, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException("request");

            PluginSettings settings = _settings();
            var store = new DroidSignStore(_database, settings);
            DroidSignStore.ValidateNewApp(request);
            if (store.FindApp(request.PackageId) != null)
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                    "A signing key for '{0}' already exists.", request.PackageId));
            ct.ThrowIfCancellationRequested();

            var policy = new PasswordPolicy { Length = settings.PasswordLength };
            var keyRequest = new KeyRequest
            {
                Alias = "1",
                Subject = request.Subject,
                KeySize = request.KeySize,
                ValidityYears = request.ValidityYears,
                StorePassword = _passwords.Generate(policy),
                KeyPassword = _passwords.Generate(policy),
            };

            SigningKeystore keystore = await new KeystoreGenerator().GenerateAsync(keyRequest, ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();

            return store.CreateApp(request, keystore);
        }
    }
}

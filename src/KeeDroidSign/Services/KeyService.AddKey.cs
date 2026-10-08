using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KeeDroidSign.Core.Keystore;
using KeeDroidSign.Core.Passwords;
using KeeDroidSign.Settings;
using KeeDroidSign.Storage;

namespace KeeDroidSign.Services
{
    public sealed partial class KeyService
    {
        /// <summary>The number the next key of <paramref name="app"/> will get (highest number + 1).</summary>
        public static int NextKeyNumber(AppKeystore app, byte[] keystore)
        {
            int highest = app.Keys.Count == 0 ? 0 : app.Keys.Max(k => k.Number);
            if (keystore != null)
            {
                foreach (string alias in KeystoreReader.ListKeyAliases(keystore, app.StorePassword))
                {
                    int n;
                    if (int.TryParse(alias, NumberStyles.None, CultureInfo.InvariantCulture, out n))
                        highest = Math.Max(highest, n);
                }
            }
            return highest + 1;
        }

        /// <summary>
        /// Adds a key to the app's keystore (User Story 3). The database changes only after the new
        /// keystore was produced; on any failure it stays untouched.
        /// </summary>
        public async Task<KeyEntryInfo> AddKeyAsync(AppKeystore app, DistinguishedName subject, CancellationToken ct)
        {
            if (app == null) throw new ArgumentNullException("app");
            if (subject == null) throw new ArgumentNullException("subject");

            byte[] keystore = app.ReadKeystore();
            if (app.KeystoreEntry == null || keystore == null)
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                    "The app '{0}' has no keystore file.", app.PackageId));

            PluginSettings settings = _settings();
            int number = NextKeyNumber(app, keystore);
            var request = new KeyRequest
            {
                Alias = number.ToString(CultureInfo.InvariantCulture),
                Subject = subject,
                KeySize = settings.KeySize,
                ValidityYears = settings.ValidityYears,
                StorePassword = app.StorePassword,
                KeyPassword = _passwords.Generate(new PasswordPolicy { Length = settings.PasswordLength }),
            };

            SigningKeystore updated = await KeystoreEditor.AddKeyAsync(keystore, request, ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();

            return new DroidSignStore(_database, settings).AddKey(app, updated, number);
        }
    }
}

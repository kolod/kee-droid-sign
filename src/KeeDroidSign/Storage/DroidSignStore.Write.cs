using System;
using System.Linq;
using System.Globalization;
using KeeDroidSign.Core.GitHub;
using KeeDroidSign.Core.Keystore;
using KeePassLib;
using KeePassLib.Security;

namespace KeeDroidSign.Storage
{
    public sealed partial class DroidSignStore
    {
        /// <summary>
        /// Stores the app's own export target on its keystore entry (Default removes it) and marks
        /// the database modified. The database file is not saved here.
        /// </summary>
        public void SetExportTarget(AppKeystore app, ExportTargetOverride value)
        {
            if (app == null) throw new ArgumentNullException("app");
            if (value == null) throw new ArgumentNullException("value");
            if (app.KeystoreEntry == null) throw new InvalidOperationException("The app has no keystore entry.");

            PwEntry entry = app.KeystoreEntry;
            string stored = value.ToFieldValue();
            if (stored == null)
            {
                if (!entry.Strings.Remove(EntryFields.ExportTarget)) return;
            }
            else
            {
                if (entry.Strings.ReadSafe(EntryFields.ExportTarget) == stored) return;
                SetText(entry, EntryFields.ExportTarget, stored);
            }
            entry.Touch(true, false);
            _database.Modified = true;
        }

        /// <summary>
        /// Creates <c>&lt;root&gt;/&lt;package id&gt;/</c> with the keystore entry and key entry "1".
        /// Validates everything before touching the database.
        /// </summary>
        public KeyEntryInfo CreateApp(NewAppRequest request, SigningKeystore keystore)
        {
            if (request == null) throw new ArgumentNullException("request");
            if (keystore == null) throw new ArgumentNullException("keystore");

            RepositoryTarget repository = ValidateNewApp(request);
            if (FindApp(request.PackageId) != null)
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                    "A signing key for '{0}' already exists.", request.PackageId));

            PwGroup root = _database.RootGroup.FindCreateGroup(_settings.RootGroup, true);
            PwGroup group = root.FindCreateGroup(request.PackageId, true);

            var keystoreEntry = new PwEntry(true, true);
            SetText(keystoreEntry, PwDefs.TitleField, request.DisplayName.Trim());
            SetSecret(keystoreEntry, PwDefs.PasswordField, keystore.StorePassword);
            SetText(keystoreEntry, PwDefs.UrlField, "https://github.com/" + repository);
            SetText(keystoreEntry, EntryFields.Role, EntryFields.RoleKeystore);
            keystoreEntry.Binaries.Set(EntryFields.KeystoreFileName(request.PackageId),
                new ProtectedBinary(true, keystore.Content));
            group.AddEntry(keystoreEntry, true);

            PwEntry keyEntry = CreateKeyEntry(group, 1, keystore.KeyPassword);

            _database.Modified = true;
            return new KeyEntryInfo(keyEntry, 1);
        }

        /// <summary>
        /// Stores a keystore that gained key <paramref name="number"/>: the previous keystore entry
        /// state is kept in its history, the attachment is replaced and key entry
        /// <paramref name="number"/> is created.
        /// </summary>
        public KeyEntryInfo AddKey(AppKeystore app, SigningKeystore updated, int number)
        {
            if (app == null || app.KeystoreEntry == null) throw new ArgumentException("The app has no keystore entry.", "app");
            if (updated == null) throw new ArgumentNullException("updated");
            if (app.Keys.Any(k => k.Number == number))
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                    "Key number {0} already exists.", number));

            string fileName = app.KeystoreFileName ?? EntryFields.KeystoreFileName(app.PackageId);
            PwEntry keystoreEntry = app.KeystoreEntry;
            keystoreEntry.CreateBackup(_database);
            keystoreEntry.Binaries.Set(fileName, new ProtectedBinary(true, updated.Content));
            keystoreEntry.Touch(true, false);

            PwEntry keyEntry = CreateKeyEntry(app.Group, number, updated.KeyPassword);

            _database.Modified = true;
            return new KeyEntryInfo(keyEntry, number);
        }

        /// <summary>Throws <see cref="ArgumentException"/> naming the invalid field.</summary>
        public static RepositoryTarget ValidateNewApp(NewAppRequest request)
        {
            RepositoryTarget repository = ValidateAppFields(request);
            if (request.Subject == null)
                throw new ArgumentException("The certificate owner is required.");
            request.Subject.Validate();
            return repository;
        }

        /// <summary>Validates package ID, display name and repository only (first step of the window).</summary>
        public static RepositoryTarget ValidateAppFields(NewAppRequest request)
        {
            if (request == null) throw new ArgumentNullException("request");
            if (!IsValidPackageId(request.PackageId))
                throw new ArgumentException("The package ID must be a valid Android application ID, e.g. com.example.app.");
            if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length > 100)
                throw new ArgumentException("The display name is required and must not be longer than 100 characters.");
            RepositoryTarget repository;
            if (!RepositoryTarget.TryParse(request.Repository, out repository))
                throw new ArgumentException("The repository must be 'owner/name' or a https://github.com/owner/name URL.");
            return repository;
        }

        private static PwEntry CreateKeyEntry(PwGroup group, int number, string keyPassword)
        {
            string text = number.ToString(CultureInfo.InvariantCulture);
            var entry = new PwEntry(true, true);
            SetText(entry, PwDefs.TitleField, text);
            SetSecret(entry, PwDefs.PasswordField, keyPassword);
            SetText(entry, EntryFields.Role, EntryFields.RoleKey);
            group.AddEntry(entry, true);
            return entry;
        }

        private static void SetText(PwEntry entry, string field, string value)
        {
            entry.Strings.Set(field, new ProtectedString(false, value ?? string.Empty));
        }

        private static void SetSecret(PwEntry entry, string field, string value)
        {
            entry.Strings.Set(field, new ProtectedString(true, value ?? string.Empty));
        }
    }
}

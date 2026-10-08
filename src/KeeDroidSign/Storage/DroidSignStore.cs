using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using KeeDroidSign.Core.GitHub;
using KeeDroidSign.Settings;
using KeePassLib;

namespace KeeDroidSign.Storage
{
    /// <summary>
    /// Reads and writes the plugin's layout in a KeePass database:
    /// <c>&lt;root&gt;/&lt;package id&gt;/</c> with one keystore entry and numbered key entries.
    /// No WinForms dependencies; the database file is never saved here.
    /// </summary>
    public sealed partial class DroidSignStore
    {
        private static readonly Regex PackageIdPattern =
            new Regex("^[a-zA-Z][a-zA-Z0-9_]*(\\.[a-zA-Z][a-zA-Z0-9_]*)+$");

        private readonly PwDatabase _database;
        private readonly PluginSettings _settings;

        public DroidSignStore(PwDatabase database, PluginSettings settings)
        {
            if (database == null) throw new ArgumentNullException("database");
            if (settings == null) throw new ArgumentNullException("settings");
            _database = database;
            _settings = settings;
        }

        public static bool IsValidPackageId(string packageId)
        {
            return !string.IsNullOrEmpty(packageId) && packageId.Length <= 255 && PackageIdPattern.IsMatch(packageId);
        }

        public static bool IsKeyEntry(PwEntry entry)
        {
            return entry != null && entry.Strings.ReadSafe(EntryFields.Role) == EntryFields.RoleKey;
        }

        public static bool IsKeystoreEntry(PwEntry entry)
        {
            return entry != null && entry.Strings.ReadSafe(EntryFields.Role) == EntryFields.RoleKeystore;
        }

        /// <summary>All app groups (those containing a keystore entry) under the root group.</summary>
        public IReadOnlyList<AppKeystore> ListApps()
        {
            PwGroup root = FindRoot();
            if (root == null) return new AppKeystore[0];

            return root.Groups
                .Select(BuildApp)
                .Where(app => app.KeystoreEntry != null)
                .ToList();
        }

        /// <summary>The app group for the package ID, or null.</summary>
        public AppKeystore FindApp(string packageId)
        {
            PwGroup root = FindRoot();
            PwGroup group = root == null ? null : root.FindCreateGroup(packageId, false);
            if (group == null) return null;

            AppKeystore app = BuildApp(group);
            return app.KeystoreEntry == null ? null : app;
        }

        /// <summary>Resolves a key entry with its app; problems are reported, not thrown.</summary>
        public KeyContext ResolveKey(PwEntry keyEntry)
        {
            if (!IsKeyEntry(keyEntry)) throw new ArgumentException("The entry is not a DroidSign key entry.", "keyEntry");

            AppKeystore app = BuildApp(keyEntry.ParentGroup);
            KeyEntryInfo key = app.Keys.FirstOrDefault(k => ReferenceEquals(k.Entry, keyEntry));

            var problems = new List<KeyProblem>();
            if (key == null) problems.Add(KeyProblem.MissingKeyNumber);
            if (app.KeystoreEntry == null)
            {
                problems.Add(KeyProblem.MissingKeystoreEntry);
            }
            else
            {
                if (app.KeystoreFileName == null) problems.Add(KeyProblem.MissingKeystoreFile);
                if (string.IsNullOrWhiteSpace(app.RepositoryUrl)) problems.Add(KeyProblem.MissingRepository);
                else if (app.Repository == null) problems.Add(KeyProblem.InvalidRepository);
            }

            return new KeyContext(app, key, problems);
        }

        private PwGroup FindRoot()
        {
            return _database.RootGroup == null ? null : _database.RootGroup.FindCreateGroup(_settings.RootGroup, false);
        }

        private static AppKeystore BuildApp(PwGroup group)
        {
            var warnings = new List<string>();
            List<PwEntry> keystoreEntries = group.Entries.Where(IsKeystoreEntry).ToList();
            if (keystoreEntries.Count > 1)
                warnings.Add(string.Format(CultureInfo.InvariantCulture,
                    "The group '{0}' contains {1} keystore entries; the first one is used.", group.Name, keystoreEntries.Count));

            var keys = new List<KeyEntryInfo>();
            foreach (PwEntry entry in group.Entries.Where(IsKeyEntry))
            {
                int number;
                // The title is the key number, which is also the alias inside the keystore.
                string title = entry.Strings.ReadSafe(PwDefs.TitleField).Trim();
                if (!int.TryParse(title, NumberStyles.None, CultureInfo.InvariantCulture, out number) || number <= 0)
                {
                    warnings.Add(string.Format(CultureInfo.InvariantCulture,
                        "The key entry '{0}' is ignored: its title must be the key number (1, 2, ...).", title));
                    continue;
                }
                keys.Add(new KeyEntryInfo(entry, number));
            }

            PwEntry keystoreEntry = keystoreEntries.FirstOrDefault();
            string url = keystoreEntry == null ? null : keystoreEntry.Strings.ReadSafe(PwDefs.UrlField);
            RepositoryTarget repository;
            RepositoryTarget.TryParse(url, out repository);

            return new AppKeystore(group, keystoreEntry, keys.OrderBy(k => k.Number).ToList(), repository, warnings);
        }
    }
}

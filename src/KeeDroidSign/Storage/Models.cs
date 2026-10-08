using System;
using System.Collections.Generic;
using System.Globalization;
using KeeDroidSign.Core.GitHub;
using KeeDroidSign.Core.Keystore;
using KeePassLib;
using KeePassLib.Security;

namespace KeeDroidSign.Storage
{
    /// <summary>Input of the "New signing key" window.</summary>
    public sealed class NewAppRequest
    {
        public NewAppRequest()
        {
            KeySize = 4096;
            ValidityYears = 30;
        }

        public string PackageId { get; set; }
        public string DisplayName { get; set; }
        public string Repository { get; set; }
        public DistinguishedName Subject { get; set; }
        public int KeySize { get; set; }
        public int ValidityYears { get; set; }
    }

    /// <summary>Problems that prevent using a key; shown on the entry tab instead of throwing.</summary>
    public enum KeyProblem
    {
        MissingKeystoreEntry,
        MissingKeystoreFile,
        MissingRepository,
        InvalidRepository,
        MissingKeyNumber,
    }

    /// <summary>One key entry of an app group.</summary>
    public sealed class KeyEntryInfo
    {
        private readonly PwEntry _entry;
        private readonly int _number;
        private readonly bool _titleMismatch;

        public KeyEntryInfo(PwEntry entry, int number, bool titleMismatch)
        {
            _entry = entry;
            _number = number;
            _titleMismatch = titleMismatch;
        }

        public PwEntry Entry { get { return _entry; } }

        /// <summary>Key number; also the alias inside the keystore.</summary>
        public int Number { get { return _number; } }

        public string Alias { get { return _number.ToString(CultureInfo.InvariantCulture); } }

        /// <summary>True when the user renamed the entry so its title no longer equals the number.</summary>
        public bool TitleMismatch { get { return _titleMismatch; } }
    }

    /// <summary>An app group with its keystore entry and key entries.</summary>
    public sealed class AppKeystore
    {
        private readonly PwGroup _group;
        private readonly PwEntry _keystoreEntry;
        private readonly IReadOnlyList<KeyEntryInfo> _keys;
        private readonly RepositoryTarget _repository;
        private readonly IReadOnlyList<string> _warnings;

        public AppKeystore(PwGroup group, PwEntry keystoreEntry, IReadOnlyList<KeyEntryInfo> keys,
            RepositoryTarget repository, IReadOnlyList<string> warnings)
        {
            _group = group;
            _keystoreEntry = keystoreEntry;
            _keys = keys;
            _repository = repository;
            _warnings = warnings;
        }

        public PwGroup Group { get { return _group; } }
        public string PackageId { get { return _group.Name; } }

        /// <summary>Null when the group has no keystore entry.</summary>
        public PwEntry KeystoreEntry { get { return _keystoreEntry; } }

        public string DisplayName { get { return ReadField(PwDefs.TitleField) ?? string.Empty; } }
        public string RepositoryUrl { get { return ReadField(PwDefs.UrlField) ?? string.Empty; } }
        public string StorePassword { get { return ReadField(PwDefs.PasswordField); } }

        /// <summary>Null when the URL is missing or not a GitHub repository.</summary>
        public RepositoryTarget Repository { get { return _repository; } }

        public IReadOnlyList<KeyEntryInfo> Keys { get { return _keys; } }
        public IReadOnlyList<string> Warnings { get { return _warnings; } }

        /// <summary>Name of the attached .jks file, or null.</summary>
        public string KeystoreFileName
        {
            get
            {
                if (_keystoreEntry == null) return null;
                string preferred = EntryFields.KeystoreFileName(PackageId);
                if (_keystoreEntry.Binaries.Get(preferred) != null) return preferred;
                foreach (KeyValuePair<string, ProtectedBinary> pair in _keystoreEntry.Binaries)
                {
                    if (pair.Key.EndsWith(EntryFields.KeystoreExtension, StringComparison.OrdinalIgnoreCase))
                        return pair.Key;
                }
                return null;
            }
        }

        /// <summary>The attached keystore bytes, or null.</summary>
        public byte[] ReadKeystore()
        {
            string name = KeystoreFileName;
            return name == null ? null : _keystoreEntry.Binaries.Get(name).ReadData();
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "AppKeystore({0}, {1} key(s))", PackageId, _keys.Count);
        }

        private string ReadField(string name)
        {
            return _keystoreEntry == null ? null : _keystoreEntry.Strings.ReadSafe(name);
        }
    }

    /// <summary>Everything needed to describe or export one key. Holds secrets.</summary>
    public sealed class KeyContext
    {
        private readonly AppKeystore _app;
        private readonly KeyEntryInfo _key;
        private readonly IReadOnlyList<KeyProblem> _problems;

        public KeyContext(AppKeystore app, KeyEntryInfo key, IReadOnlyList<KeyProblem> problems)
        {
            _app = app;
            _key = key;
            _problems = problems;
        }

        public AppKeystore App { get { return _app; } }
        public KeyEntryInfo Key { get { return _key; } }
        public IReadOnlyList<KeyProblem> Problems { get { return _problems; } }

        public string Alias { get { return _key == null ? null : _key.Alias; } }
        public byte[] KeystoreContent { get { return _app == null ? null : _app.ReadKeystore(); } }
        public string StorePassword { get { return _app == null ? null : _app.StorePassword; } }
        public string KeyPassword { get { return _key == null ? null : _key.Entry.Strings.ReadSafe(PwDefs.PasswordField); } }

        public SigningKeystore ToSigningKeystore()
        {
            return KeystoreReader.Open(KeystoreContent, Alias, StorePassword, KeyPassword);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "KeyContext(App={0}, Alias={1}, Problems={2}, Passwords=***)",
                _app == null ? null : _app.PackageId, Alias, _problems.Count);
        }
    }

    /// <summary>Public certificate details shown on the entry tab.</summary>
    public sealed class KeyDetails
    {
        public string Alias { get; set; }
        public string Subject { get; set; }
        public DateTime NotBefore { get; set; }
        public DateTime NotAfter { get; set; }
        public string Sha256 { get; set; }
    }
}

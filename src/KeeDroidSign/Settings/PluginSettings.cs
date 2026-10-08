using System;
using System.Globalization;
using System.Text.RegularExpressions;
using KeeDroidSign.Core.GitHub;
using KeeDroidSign.Core.Keystore;

namespace KeeDroidSign.Settings
{
    /// <summary>Key/value storage for settings; keys are unprefixed (e.g. "RootGroup").</summary>
    public interface ISettingsStore
    {
        string Get(string key);
        void Set(string key, string value);
    }

    /// <summary>
    /// Global plugin settings. Holds names, numbers and the token entry's UUID only; never a
    /// secret (Constitution Principle III).
    /// </summary>
    public sealed class PluginSettings
    {
        private static readonly Regex UuidPattern = new Regex("^[0-9A-Fa-f]{32}$");
        private static readonly Regex GitHubOwnerPattern = new Regex("^[A-Za-z0-9-]{1,39}$");

        public PluginSettings()
        {
            RootGroup = "DroidSign";
            TokenEntryUuid = string.Empty;
            KeySize = 4096;
            ValidityYears = 30;
            PasswordLength = 32;
            SaveAfterKeyChange = true;
            SecretKeystoreBase64 = "ANDROID_KEYSTORE_BASE64";
            SecretStorePassword = "ANDROID_KEYSTORE_PASSWORD";
            SecretKeyAlias = "ANDROID_KEY_ALIAS";
            SecretKeyPassword = "ANDROID_KEY_PASSWORD";
            DefaultCommonName = string.Empty;
            DefaultOrganizationalUnit = string.Empty;
            DefaultOrganization = string.Empty;
            DefaultLocality = string.Empty;
            DefaultState = string.Empty;
            DefaultCountry = string.Empty;
            DefaultGitHubOwner = string.Empty;
            DefaultEnvironment = string.Empty;
        }

        /// <summary>Default environment of a new installation; upgrades from 1.0.x keep repository level.</summary>
        public const string NewInstallEnvironment = "release";

        public string RootGroup { get; set; }

        /// <summary>Hex UUID of the KeePass entry whose password is the GitHub token; empty if unset.</summary>
        public string TokenEntryUuid { get; set; }

        public int KeySize { get; set; }
        public int ValidityYears { get; set; }
        public int PasswordLength { get; set; }

        /// <summary>Save the database (through KeePass) after a key was created or added. On by default.</summary>
        public bool SaveAfterKeyChange { get; set; }

        public string SecretKeystoreBase64 { get; set; }
        public string SecretStorePassword { get; set; }
        public string SecretKeyAlias { get; set; }
        public string SecretKeyPassword { get; set; }

        /// <summary>Certificate owner name (CN) for new keys; empty means "use the app's display name".</summary>
        public string DefaultCommonName { get; set; }
        public string DefaultOrganizationalUnit { get; set; }
        public string DefaultOrganization { get; set; }
        public string DefaultLocality { get; set; }
        public string DefaultState { get; set; }

        /// <summary>Two-letter country code for new keys, or empty.</summary>
        public string DefaultCountry { get; set; }

        /// <summary>GitHub user or organization pre-filled as the repository owner, or empty.</summary>
        public string DefaultGitHubOwner { get; set; }

        /// <summary>
        /// GitHub environment the secrets are exported into unless an app overrides it; empty means
        /// repository-level secrets (readable by every workflow of the repository).
        /// </summary>
        public string DefaultEnvironment { get; set; }

        /// <summary>
        /// Initial text of the repository field: "https://github.com/", plus "owner/" when a default
        /// owner is set, so only the repository name remains to be typed.
        /// </summary>
        public string RepositoryPrefill
        {
            get
            {
                const string GitHub = "https://github.com/";
                return string.IsNullOrEmpty(DefaultGitHubOwner) ? GitHub : GitHub + DefaultGitHubOwner + "/";
            }
        }

        /// <summary>
        /// Initial text of the package ID field: "io.github.&lt;owner&gt;." (GitHub Pages style), with the
        /// owner lower-cased and '-' replaced by '_' to form a valid Java package segment; only
        /// "io.github." when the owner starts with a digit; empty when no owner is set.
        /// </summary>
        public string PackageIdPrefill
        {
            get
            {
                if (string.IsNullOrEmpty(DefaultGitHubOwner)) return string.Empty;
                string segment = DefaultGitHubOwner.ToLowerInvariant().Replace('-', '_');
                return char.IsLetter(segment[0]) ? "io.github." + segment + "." : "io.github.";
            }
        }

        /// <summary>Certificate owner for a new key; the CN falls back to <paramref name="displayName"/>.</summary>
        public DistinguishedName DefaultSubject(string displayName)
        {
            return new DistinguishedName
            {
                CommonName = string.IsNullOrEmpty(DefaultCommonName) ? displayName : DefaultCommonName,
                OrganizationalUnit = DefaultOrganizationalUnit,
                Organization = DefaultOrganization,
                Locality = DefaultLocality,
                State = DefaultState,
                Country = DefaultCountry,
            };
        }

        public static PluginSettings Load(ISettingsStore store)
        {
            if (store == null) throw new ArgumentNullException("store");

            var s = new PluginSettings();
            s.RootGroup = store.Get("RootGroup") ?? s.RootGroup;
            s.TokenEntryUuid = store.Get("TokenEntryUuid") ?? s.TokenEntryUuid;
            s.KeySize = ReadInt(store, "KeySize", s.KeySize);
            s.ValidityYears = ReadInt(store, "ValidityYears", s.ValidityYears);
            s.PasswordLength = ReadInt(store, "PasswordLength", s.PasswordLength);
            string save = store.Get("SaveAfterKeyChange");
            s.SaveAfterKeyChange = save == null ? s.SaveAfterKeyChange : save == "true";
            s.SecretKeystoreBase64 = store.Get("Secret.KeystoreBase64") ?? s.SecretKeystoreBase64;
            s.SecretStorePassword = store.Get("Secret.StorePassword") ?? s.SecretStorePassword;
            s.SecretKeyAlias = store.Get("Secret.KeyAlias") ?? s.SecretKeyAlias;
            s.SecretKeyPassword = store.Get("Secret.KeyPassword") ?? s.SecretKeyPassword;
            s.DefaultCommonName = store.Get("Default.CommonName") ?? s.DefaultCommonName;
            s.DefaultOrganizationalUnit = store.Get("Default.OrganizationalUnit") ?? s.DefaultOrganizationalUnit;
            s.DefaultOrganization = store.Get("Default.Organization") ?? s.DefaultOrganization;
            s.DefaultLocality = store.Get("Default.Locality") ?? s.DefaultLocality;
            s.DefaultState = store.Get("Default.State") ?? s.DefaultState;
            s.DefaultCountry = store.Get("Default.Country") ?? s.DefaultCountry;
            s.DefaultGitHubOwner = store.Get("Default.GitHubOwner") ?? s.DefaultGitHubOwner;
            // Not saved yet: a new installation gets "release"; settings saved by 1.0.x (which always
            // include RootGroup) keep exporting at repository level until the user changes it.
            string environment = store.Get("GitHubEnvironment");
            if (environment != null)
                s.DefaultEnvironment = environment;
            else if (store.Get("RootGroup") == null)
                s.DefaultEnvironment = NewInstallEnvironment;
            return s;
        }

        public void Save(ISettingsStore store)
        {
            if (store == null) throw new ArgumentNullException("store");

            store.Set("RootGroup", RootGroup);
            store.Set("TokenEntryUuid", TokenEntryUuid ?? string.Empty);
            store.Set("KeySize", KeySize.ToString(CultureInfo.InvariantCulture));
            store.Set("ValidityYears", ValidityYears.ToString(CultureInfo.InvariantCulture));
            store.Set("PasswordLength", PasswordLength.ToString(CultureInfo.InvariantCulture));
            store.Set("SaveAfterKeyChange", SaveAfterKeyChange ? "true" : "false");
            store.Set("Secret.KeystoreBase64", SecretKeystoreBase64);
            store.Set("Secret.StorePassword", SecretStorePassword);
            store.Set("Secret.KeyAlias", SecretKeyAlias);
            store.Set("Secret.KeyPassword", SecretKeyPassword);
            store.Set("Default.CommonName", DefaultCommonName ?? string.Empty);
            store.Set("Default.OrganizationalUnit", DefaultOrganizationalUnit ?? string.Empty);
            store.Set("Default.Organization", DefaultOrganization ?? string.Empty);
            store.Set("Default.Locality", DefaultLocality ?? string.Empty);
            store.Set("Default.State", DefaultState ?? string.Empty);
            store.Set("Default.Country", DefaultCountry ?? string.Empty);
            store.Set("Default.GitHubOwner", DefaultGitHubOwner ?? string.Empty);
            store.Set("GitHubEnvironment", DefaultEnvironment ?? string.Empty);
        }

        /// <summary>Throws <see cref="ArgumentException"/> naming the invalid field.</summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(RootGroup) || RootGroup.Contains("/"))
                throw new ArgumentException("The root group name must not be empty or contain '/'.");
            if (!string.IsNullOrEmpty(TokenEntryUuid) && !UuidPattern.IsMatch(TokenEntryUuid))
                throw new ArgumentException("The token entry reference is not a valid entry UUID.");
            if (KeySize != 2048 && KeySize != 3072 && KeySize != 4096)
                throw new ArgumentException("The key size must be 2048, 3072 or 4096 bits.");
            if (ValidityYears < 25 || ValidityYears > 100)
                throw new ArgumentException("The validity must be between 25 and 100 years.");
            if (PasswordLength < 8 || PasswordLength > 128)
                throw new ArgumentException("The password length must be between 8 and 128.");

            try
            {
                SecretNames.Validate(ToSecretMapping());
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException("Invalid GitHub secret name: " + ex.Message, ex);
            }

            if (!string.IsNullOrEmpty(DefaultGitHubOwner) && !GitHubOwnerPattern.IsMatch(DefaultGitHubOwner))
                throw new ArgumentException("The default GitHub owner must be a user or organization name (letters, digits, '-').");
            if (!string.IsNullOrEmpty(DefaultEnvironment) && !EnvironmentNames.IsValid(DefaultEnvironment))
            {
                try
                {
                    EnvironmentNames.Validate(DefaultEnvironment);
                }
                catch (ArgumentException ex)
                {
                    throw new ArgumentException("Invalid GitHub environment: " + ex.Message, ex);
                }
            }

            // Owner fields follow the certificate rules; the CN placeholder only satisfies "CN required".
            DistinguishedName subject = DefaultSubject("placeholder");
            subject.Validate();
        }

        public SecretMapping ToSecretMapping()
        {
            return new SecretMapping
            {
                KeystoreBase64Name = SecretKeystoreBase64,
                StorePasswordName = SecretStorePassword,
                KeyAliasName = SecretKeyAlias,
                KeyPasswordName = SecretKeyPassword,
            };
        }

        public PluginSettings Clone()
        {
            return (PluginSettings)MemberwiseClone();
        }

        private static int ReadInt(ISettingsStore store, string key, int fallback)
        {
            int value;
            return int.TryParse(store.Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }
    }
}

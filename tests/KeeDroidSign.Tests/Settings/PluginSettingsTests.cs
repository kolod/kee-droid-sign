using System;
using System.Collections.Generic;
using System.Linq;
using KeeDroidSign.Settings;
using Xunit;

namespace KeeDroidSign.Tests.Settings
{
    public class PluginSettingsTests
    {
        internal sealed class DictionaryStore : ISettingsStore
        {
            public Dictionary<string, string> Values { get; } = new Dictionary<string, string>();
            public string Get(string key) => Values.TryGetValue(key, out string v) ? v : null;
            public void Set(string key, string value) => Values[key] = value;
        }

        [Fact]
        public void Load_EmptyStore_ReturnsDefaults()
        {
            PluginSettings s = PluginSettings.Load(new DictionaryStore());

            Assert.Equal("DroidSign", s.RootGroup);
            Assert.Equal(string.Empty, s.TokenEntryUuid);
            Assert.Equal(4096, s.KeySize);
            Assert.Equal(30, s.ValidityYears);
            Assert.Equal(32, s.PasswordLength);
            Assert.Equal("ANDROID_KEYSTORE_BASE64", s.SecretKeystoreBase64);
            Assert.Equal("ANDROID_KEYSTORE_PASSWORD", s.SecretStorePassword);
            Assert.Equal("ANDROID_KEY_ALIAS", s.SecretKeyAlias);
            Assert.Equal("ANDROID_KEY_PASSWORD", s.SecretKeyPassword);
            s.Validate();
        }

        [Fact]
        public void SaveAndLoad_RoundTrips()
        {
            var store = new DictionaryStore();
            var s = PluginSettings.Load(store);
            s.RootGroup = "Android Keys";
            s.TokenEntryUuid = "0123456789ABCDEF0123456789ABCDEF";
            s.KeySize = 3072;
            s.ValidityYears = 40;
            s.PasswordLength = 48;
            s.SecretKeyAlias = "RELEASE_ALIAS";

            s.Save(store);
            PluginSettings loaded = PluginSettings.Load(store);

            Assert.Equal("Android Keys", loaded.RootGroup);
            Assert.Equal("0123456789ABCDEF0123456789ABCDEF", loaded.TokenEntryUuid);
            Assert.Equal(3072, loaded.KeySize);
            Assert.Equal(40, loaded.ValidityYears);
            Assert.Equal(48, loaded.PasswordLength);
            Assert.Equal("RELEASE_ALIAS", loaded.SecretKeyAlias);
        }

        [Fact]
        public void Load_GarbageNumbers_FallBackToDefaults()
        {
            var store = new DictionaryStore();
            store.Set("KeySize", "abc");
            store.Set("ValidityYears", "");

            PluginSettings s = PluginSettings.Load(store);

            Assert.Equal(4096, s.KeySize);
            Assert.Equal(30, s.ValidityYears);
        }

        public static IEnumerable<object[]> InvalidSettings()
        {
            yield return new object[] { (Action<PluginSettings>)(s => s.RootGroup = " "), "root group" };
            yield return new object[] { (Action<PluginSettings>)(s => s.RootGroup = "a/b"), "root group" };
            yield return new object[] { (Action<PluginSettings>)(s => s.KeySize = 1024), "key size" };
            yield return new object[] { (Action<PluginSettings>)(s => s.ValidityYears = 24), "validity" };
            yield return new object[] { (Action<PluginSettings>)(s => s.PasswordLength = 7), "password length" };
            yield return new object[] { (Action<PluginSettings>)(s => s.SecretKeyAlias = "GITHUB_X"), "secret" };
            yield return new object[] { (Action<PluginSettings>)(s => s.SecretKeyAlias = "ANDROID_KEY_PASSWORD"), "secret" };
            yield return new object[] { (Action<PluginSettings>)(s => s.TokenEntryUuid = "xyz"), "token entry" };
        }

        [Theory]
        [MemberData(nameof(InvalidSettings))]
        public void Validate_RejectsInvalidValues(Action<PluginSettings> breakIt, string fieldHint)
        {
            var s = PluginSettings.Load(new DictionaryStore());
            breakIt(s);

            var ex = Assert.Throws<ArgumentException>(() => s.Validate());
            Assert.Contains(fieldHint, ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Save_StoresOnlyNamesNumbersAndUuid()
        {
            var store = new DictionaryStore();
            PluginSettings.Load(store).Save(store);

            Assert.Equal(
                new[] { "Default.CommonName", "Default.Country", "Default.GitHubOwner", "Default.Locality",
                        "Default.Organization", "Default.OrganizationalUnit", "Default.State",
                        "KeySize", "PasswordLength", "RootGroup", "SaveAfterKeyChange", "Secret.KeyAlias", "Secret.KeyPassword",
                        "Secret.KeystoreBase64", "Secret.StorePassword", "TokenEntryUuid", "ValidityYears" },
                store.Values.Keys.OrderBy(k => k, StringComparer.Ordinal));
        }

        [Fact]
        public void ToSecretMapping_UsesConfiguredNames()
        {
            var s = PluginSettings.Load(new DictionaryStore());
            s.SecretStorePassword = "STORE_PW";

            var mapping = s.ToSecretMapping();

            Assert.Equal("STORE_PW", mapping.StorePasswordName);
            Assert.Equal("ANDROID_KEYSTORE_BASE64", mapping.KeystoreBase64Name);
        }
    }
}

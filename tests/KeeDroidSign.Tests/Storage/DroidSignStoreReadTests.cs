using System.Linq;
using KeeDroidSign.Settings;
using KeeDroidSign.Storage;
using KeeDroidSign.Tests.Settings;
using KeeDroidSign.Tests.Support;
using KeePassLib;
using KeePassLib.Security;
using Xunit;

namespace KeeDroidSign.Tests.Storage
{
    public class DroidSignStoreReadTests
    {
        private static PluginSettings Settings(string root = "DroidSign")
        {
            var s = PluginSettings.Load(new PluginSettingsTests.DictionaryStore());
            s.RootGroup = root;
            return s;
        }

        [Fact]
        public void ListApps_FindsAppGroupsUnderConfiguredRootOnly()
        {
            var pd = TestDatabase.Create();
            var ks = TestDatabase.Keystore();
            TestDatabase.AddApp(pd, "DroidSign", "com.example.one", ks);
            TestDatabase.AddApp(pd, "DroidSign", "com.example.two", ks);
            TestDatabase.AddApp(pd, "Other", "com.example.three", ks);
            pd.RootGroup.FindCreateGroup("DroidSign", false).FindCreateGroup("not-an-app", true);

            var apps = new DroidSignStore(pd, Settings()).ListApps();

            Assert.Equal(new[] { "com.example.one", "com.example.two" }, apps.Select(a => a.PackageId).OrderBy(x => x));
        }

        [Fact]
        public void Reads_DoNotCreateGroups()
        {
            var pd = TestDatabase.Create();
            var store = new DroidSignStore(pd, Settings());

            Assert.Empty(store.ListApps());
            Assert.Null(store.FindApp("com.example.app"));
            Assert.Empty(pd.RootGroup.Groups);
            Assert.False(pd.Modified);
        }

        [Fact]
        public void FindApp_ReturnsAppWithKeysAndRepository()
        {
            var pd = TestDatabase.Create();
            var ks = TestDatabase.Keystore();
            TestDatabase.AddApp(pd, "DroidSign", "com.example.app", ks, "https://github.com/octo/app", 1, 3);

            AppKeystore app = new DroidSignStore(pd, Settings()).FindApp("com.example.app");

            Assert.NotNull(app);
            Assert.Equal("Test App", app.DisplayName);
            Assert.Equal("octo/app", app.Repository.ToString());
            Assert.Equal(new[] { 1, 3 }, app.Keys.Select(k => k.Number));
            Assert.Equal(ks.Content, app.ReadKeystore());
            Assert.Empty(app.Warnings);
        }

        [Theory]
        [InlineData("https://github.com/octo/app", "https://github.com/octo/app")]
        [InlineData("octo/app", "https://github.com/octo/app")]
        [InlineData("https://github.com/octo/app.git", "https://github.com/octo/app")]
        [InlineData("https://evil.example.com/octo/app", null)]
        [InlineData("javascript:alert(1)", null)]
        public void RepositoryWebUrl_IsBuiltFromParsedRepositoryOnly(string url, string expected)
        {
            var pd = TestDatabase.Create();
            TestDatabase.AddApp(pd, "DroidSign", "com.example.app", TestDatabase.Keystore(), url);

            AppKeystore app = new DroidSignStore(pd, Settings()).FindApp("com.example.app");

            Assert.Equal(expected, app.RepositoryWebUrl);
        }

        [Fact]
        public void TwoKeystoreEntries_FirstIsUsedWithWarning()
        {
            var pd = TestDatabase.Create();
            PwGroup group = TestDatabase.AddApp(pd, "DroidSign", "com.example.app", TestDatabase.Keystore());
            var extra = new PwEntry(true, true);
            extra.Strings.Set(EntryFields.Role, new ProtectedString(false, EntryFields.RoleKeystore));
            extra.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "Duplicate"));
            group.AddEntry(extra, true);

            AppKeystore app = new DroidSignStore(pd, Settings()).FindApp("com.example.app");

            Assert.Equal("Test App", app.DisplayName);
            Assert.NotEmpty(app.Warnings);
        }

        [Fact]
        public void IsKeyEntry_OnlyForKeyRole()
        {
            var pd = TestDatabase.Create();
            PwGroup group = TestDatabase.AddApp(pd, "DroidSign", "com.example.app", TestDatabase.Keystore());
            PwEntry keystoreEntry = group.Entries.First(e => e.Strings.ReadSafe(EntryFields.Role) == EntryFields.RoleKeystore);
            PwEntry keyEntry = group.Entries.First(e => e.Strings.ReadSafe(EntryFields.Role) == EntryFields.RoleKey);

            Assert.True(DroidSignStore.IsKeyEntry(keyEntry));
            Assert.False(DroidSignStore.IsKeyEntry(keystoreEntry));
            Assert.False(DroidSignStore.IsKeyEntry(new PwEntry(true, true)));
            Assert.False(DroidSignStore.IsKeyEntry(null));
        }

        [Fact]
        public void ResolveKey_ReturnsEverythingNeededForExport()
        {
            var pd = TestDatabase.Create();
            var ks = TestDatabase.Keystore();
            PwGroup group = TestDatabase.AddApp(pd, "DroidSign", "com.example.app", ks);
            PwEntry keyEntry = group.Entries.First(DroidSignStore.IsKeyEntry);

            KeyContext key = new DroidSignStore(pd, Settings()).ResolveKey(keyEntry);

            Assert.Empty(key.Problems);
            Assert.Equal("1", key.Alias);
            Assert.Equal(TestDatabase.StorePassword, key.StorePassword);
            Assert.Equal(TestDatabase.KeyPassword, key.KeyPassword);
            Assert.Equal(ks.Content, key.KeystoreContent);
            Assert.Equal("octo/app", key.App.Repository.ToString());
            Assert.DoesNotContain(TestDatabase.StorePassword, key.ToString());
            Assert.DoesNotContain(TestDatabase.KeyPassword, key.ToString());
        }

        [Fact]
        public void ResolveKey_NonNumericTitle_IsReportedAsMissingKeyNumber()
        {
            var pd = TestDatabase.Create();
            PwGroup group = TestDatabase.AddApp(pd, "DroidSign", "com.example.app", TestDatabase.Keystore());
            PwEntry renamed = group.Entries.First(DroidSignStore.IsKeyEntry);
            renamed.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "upload key"));

            KeyContext key = new DroidSignStore(pd, Settings()).ResolveKey(renamed);

            Assert.Null(key.Key);
            Assert.Contains(KeyProblem.MissingKeyNumber, key.Problems);
        }

        [Fact]
        public void ResolveKey_AliasComesFromTitle_LegacyKeyNumberFieldIsIgnored()
        {
            var pd = TestDatabase.Create();
            PwGroup group = TestDatabase.AddApp(pd, "DroidSign", "com.example.app", TestDatabase.Keystore(), keyNumbers: 2);
            PwEntry entry = group.Entries.First(DroidSignStore.IsKeyEntry);
            entry.Strings.Set("DroidSign.KeyNumber", new ProtectedString(false, "1")); // written by plugin 1.0.0

            KeyContext key = new DroidSignStore(pd, Settings()).ResolveKey(entry);

            Assert.Equal("2", key.Alias);
        }

        [Fact]
        public void ResolveKey_MissingAttachmentAndUrl_AreReportedAsProblems()
        {
            var pd = TestDatabase.Create();
            PwGroup group = TestDatabase.AddApp(pd, "DroidSign", "com.example.app", null, repoUrl: null);
            PwEntry keyEntry = group.Entries.First(DroidSignStore.IsKeyEntry);

            KeyContext key = new DroidSignStore(pd, Settings()).ResolveKey(keyEntry);

            Assert.Contains(KeyProblem.MissingKeystoreFile, key.Problems);
            Assert.Contains(KeyProblem.MissingRepository, key.Problems);
        }

        [Fact]
        public void ResolveKey_NoKeystoreEntryInGroup_IsReported()
        {
            var pd = TestDatabase.Create();
            PwGroup group = pd.RootGroup.FindCreateGroup("DroidSign", true).FindCreateGroup("com.example.app", true);
            PwEntry keyEntry = TestDatabase.AddKeyEntry(group, 1);

            KeyContext key = new DroidSignStore(pd, Settings()).ResolveKey(keyEntry);

            Assert.Contains(KeyProblem.MissingKeystoreEntry, key.Problems);
        }

        [Theory]
        [InlineData("com.example.app", true)]
        [InlineData("io.github.kolod.whisker_grid", true)]
        [InlineData("a.b", true)]
        [InlineData("com", false)]
        [InlineData("1abc.def", false)]
        [InlineData("a..b", false)]
        [InlineData("with space.x", false)]
        [InlineData("com.example.", false)]
        [InlineData("com.1example", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsValidPackageId(string packageId, bool expected)
        {
            Assert.Equal(expected, DroidSignStore.IsValidPackageId(packageId));
        }
    }
}

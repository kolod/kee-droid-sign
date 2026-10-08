using System;
using System.Linq;
using KeeDroidSign.Core.Keystore;
using KeeDroidSign.Settings;
using KeeDroidSign.Storage;
using KeeDroidSign.Tests.Settings;
using KeeDroidSign.Tests.Support;
using KeePassLib;
using Xunit;

namespace KeeDroidSign.Tests.Storage
{
    public class DroidSignStoreCreateTests
    {
        private static PluginSettings Settings() => PluginSettings.Load(new PluginSettingsTests.DictionaryStore());

        private static NewAppRequest Request(string packageId = "com.example.app") => new NewAppRequest
        {
            PackageId = packageId,
            DisplayName = "Example App",
            Repository = "octo/app",
            Subject = new DistinguishedName { CommonName = "Example App" },
        };

        [Fact]
        public void CreateApp_CreatesDocumentedLayout()
        {
            var pd = TestDatabase.Create();
            SigningKeystore ks = TestDatabase.Keystore();

            KeyEntryInfo key = new DroidSignStore(pd, Settings()).CreateApp(Request(), ks);

            PwGroup app = pd.RootGroup.FindCreateGroup("DroidSign", false).FindCreateGroup("com.example.app", false);
            Assert.NotNull(app);
            Assert.Equal(2u, app.Entries.UCount);

            PwEntry keystoreEntry = app.Entries.Single(DroidSignStore.IsKeystoreEntry);
            Assert.Equal("Example App", keystoreEntry.Strings.ReadSafe(PwDefs.TitleField));
            Assert.Equal(TestDatabase.StorePassword, keystoreEntry.Strings.ReadSafe(PwDefs.PasswordField));
            Assert.True(keystoreEntry.Strings.Get(PwDefs.PasswordField).IsProtected);
            Assert.Equal("https://github.com/octo/app", keystoreEntry.Strings.ReadSafe(PwDefs.UrlField));
            var attachment = keystoreEntry.Binaries.Get("com.example.app.jks");
            Assert.NotNull(attachment);
            Assert.True(attachment.IsProtected);
            Assert.Equal(ks.Content, attachment.ReadData());

            PwEntry keyEntry = app.Entries.Single(DroidSignStore.IsKeyEntry);
            Assert.Same(keyEntry, key.Entry);
            Assert.Equal("1", keyEntry.Strings.ReadSafe(PwDefs.TitleField));
            Assert.Equal("1", keyEntry.Strings.ReadSafe(EntryFields.KeyNumber));
            Assert.Equal(TestDatabase.KeyPassword, keyEntry.Strings.ReadSafe(PwDefs.PasswordField));
            Assert.True(keyEntry.Strings.Get(PwDefs.PasswordField).IsProtected);

            Assert.True(pd.Modified);
        }

        [Fact]
        public void CreateApp_UsesConfiguredRootGroup()
        {
            var pd = TestDatabase.Create();
            var settings = Settings();
            settings.RootGroup = "Android Keys";

            new DroidSignStore(pd, settings).CreateApp(Request(), TestDatabase.Keystore());

            Assert.NotNull(pd.RootGroup.FindCreateGroup("Android Keys", false)?.FindCreateGroup("com.example.app", false));
            Assert.Null(pd.RootGroup.FindCreateGroup("DroidSign", false));
        }

        [Fact]
        public void CreateApp_ExistingApp_IsRefused()
        {
            var pd = TestDatabase.Create();
            var store = new DroidSignStore(pd, Settings());
            store.CreateApp(Request(), TestDatabase.Keystore());

            Assert.Throws<InvalidOperationException>(() => store.CreateApp(Request(), TestDatabase.Keystore()));
            Assert.Single(store.ListApps());
        }

        [Theory]
        [InlineData("com")]
        [InlineData("1abc.def")]
        [InlineData("a..b")]
        [InlineData("with space.x")]
        public void CreateApp_InvalidPackageId_IsRejected(string packageId)
        {
            var pd = TestDatabase.Create();

            Assert.Throws<ArgumentException>(() => new DroidSignStore(pd, Settings()).CreateApp(Request(packageId), TestDatabase.Keystore()));
            Assert.Empty(pd.RootGroup.Groups);
            Assert.False(pd.Modified);
        }

        [Fact]
        public void CreateApp_InvalidRepositoryOrName_IsRejected()
        {
            var pd = TestDatabase.Create();
            var store = new DroidSignStore(pd, Settings());
            var noName = Request();
            noName.DisplayName = " ";
            var badRepo = Request();
            badRepo.Repository = "https://gitlab.com/a/b";

            Assert.Throws<ArgumentException>(() => store.CreateApp(noName, TestDatabase.Keystore()));
            Assert.Throws<ArgumentException>(() => store.CreateApp(badRepo, TestDatabase.Keystore()));
            Assert.Empty(pd.RootGroup.Groups);
        }
    }
}

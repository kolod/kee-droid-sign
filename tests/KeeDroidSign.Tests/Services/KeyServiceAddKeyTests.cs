using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KeeDroidSign.Core;
using KeeDroidSign.Core.Keystore;
using KeeDroidSign.Services;
using KeeDroidSign.Storage;
using KeeDroidSign.Tests.Support;
using KeePassLib;
using KeePassLib.Security;
using Xunit;

namespace KeeDroidSign.Tests.Services
{
    public class KeyServiceAddKeyTests
    {
        private static readonly DistinguishedName Subject = new DistinguishedName { CommonName = "Example App" };

        private static async Task<(PwDatabase Db, KeyService Service, DroidSignStore Store)> CreateAppAsync()
        {
            var pd = TestDatabase.Create();
            var service = new KeyService(pd, KeyServiceCreateTests.FastSettings);
            await service.CreateAppAsync(KeyServiceCreateTests.Request(), CancellationToken.None);
            pd.Modified = false;
            return (pd, service, new DroidSignStore(pd, KeyServiceCreateTests.FastSettings()));
        }

        [Fact]
        public async Task AddKey_CreatesNextNumberAndKeepsAllKeys()
        {
            var (pd, service, store) = await CreateAppAsync();
            AppKeystore app = store.FindApp("com.example.app");
            byte[] before = app.ReadKeystore();

            KeyEntryInfo added = await service.AddKeyAsync(app, Subject, CancellationToken.None);

            Assert.Equal(2, added.Number);
            Assert.Equal("2", added.Entry.Strings.ReadSafe(PwDefs.TitleField));
            app = store.FindApp("com.example.app");
            Assert.Equal(new[] { 1, 2 }, app.Keys.Select(k => k.Number));
            Assert.NotEqual(before, app.ReadKeystore());
            foreach (KeyEntryInfo key in app.Keys)
                KeystoreReader.Open(app.ReadKeystore(), key.Alias, app.StorePassword, key.Entry.Strings.ReadSafe(PwDefs.PasswordField));
            Assert.True(pd.Modified);
        }

        [Fact]
        public async Task AddKey_KeepsPreviousKeystoreInEntryHistory()
        {
            var (_, service, store) = await CreateAppAsync();
            AppKeystore app = store.FindApp("com.example.app");
            byte[] before = app.ReadKeystore();
            uint historyBefore = app.KeystoreEntry.History.UCount;

            await service.AddKeyAsync(app, Subject, CancellationToken.None);

            Assert.Equal(historyBefore + 1, app.KeystoreEntry.History.UCount);
            PwEntry backup = app.KeystoreEntry.History.GetAt(app.KeystoreEntry.History.UCount - 1);
            Assert.Equal(before, backup.Binaries.Get("com.example.app.jks").ReadData());
        }

        [Fact]
        public async Task AddKey_WithGap_UsesHighestPlusOne()
        {
            var (_, service, store) = await CreateAppAsync();
            AppKeystore app = store.FindApp("com.example.app");
            TestDatabase.AddKeyEntry(app.Group, 3);
            app = store.FindApp("com.example.app");

            KeyEntryInfo added = await service.AddKeyAsync(app, Subject, CancellationToken.None);

            Assert.Equal(4, added.Number);
        }

        [Fact]
        public async Task AddKey_WrongStorePassword_LeavesDatabaseUntouched()
        {
            var (pd, service, store) = await CreateAppAsync();
            AppKeystore app = store.FindApp("com.example.app");
            app.KeystoreEntry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "wrong-store-pass"));
            pd.Modified = false;
            uint history = app.KeystoreEntry.History.UCount;

            await Assert.ThrowsAsync<InvalidKeystorePasswordException>(() => service.AddKeyAsync(app, Subject, CancellationToken.None));

            app = store.FindApp("com.example.app");
            Assert.Single(app.Keys);
            Assert.Equal(history, app.KeystoreEntry.History.UCount);
            Assert.False(pd.Modified);
        }

        [Fact]
        public async Task AddKey_MissingKeystoreFile_IsRejected()
        {
            var (_, service, store) = await CreateAppAsync();
            AppKeystore app = store.FindApp("com.example.app");
            app.KeystoreEntry.Binaries.Remove("com.example.app.jks");

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddKeyAsync(app, Subject, CancellationToken.None));
        }
    }
}

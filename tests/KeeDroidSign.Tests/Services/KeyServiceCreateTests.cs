using System;
using System.Threading;
using System.Threading.Tasks;
using KeeDroidSign.Core.Keystore;
using KeeDroidSign.Services;
using KeeDroidSign.Settings;
using KeeDroidSign.Storage;
using KeeDroidSign.Tests.Settings;
using KeeDroidSign.Tests.Support;
using KeePassLib;
using Xunit;

namespace KeeDroidSign.Tests.Services
{
    public class KeyServiceCreateTests
    {
        internal static PluginSettings FastSettings()
        {
            var s = PluginSettings.Load(new PluginSettingsTests.DictionaryStore());
            s.KeySize = 2048;
            s.PasswordLength = 40;
            return s;
        }

        internal static NewAppRequest Request() => new NewAppRequest
        {
            PackageId = "com.example.app",
            DisplayName = "Example App",
            Repository = "https://github.com/octo/app",
            Subject = new DistinguishedName { CommonName = "Example App", Country = "UA" },
            KeySize = 2048,
            ValidityYears = 30,
        };

        [Fact]
        public async Task CreateApp_StoresUsableKeystoreAndKey()
        {
            var pd = TestDatabase.Create();
            var service = new KeyService(pd, FastSettings);

            KeyEntryInfo key = await service.CreateAppAsync(Request(), CancellationToken.None);

            var store = new DroidSignStore(pd, FastSettings());
            AppKeystore app = store.FindApp("com.example.app");
            string storePassword = app.StorePassword;
            string keyPassword = key.Entry.Strings.ReadSafe(PwDefs.PasswordField);

            Assert.Equal(40, storePassword.Length);
            Assert.Equal(40, keyPassword.Length);
            Assert.NotEqual(storePassword, keyPassword);

            SigningKeystore opened = KeystoreReader.Open(app.ReadKeystore(), "1", storePassword, keyPassword);
            Assert.Equal("1", opened.Alias);
            Assert.Equal(1, key.Number);
        }

        [Fact]
        public async Task CreateApp_UsesSettingsAtOperationTime()
        {
            var pd = TestDatabase.Create();
            var settings = FastSettings();
            var service = new KeyService(pd, () => settings);
            settings.RootGroup = "Changed Root";

            await service.CreateAppAsync(Request(), CancellationToken.None);

            Assert.NotNull(pd.RootGroup.FindCreateGroup("Changed Root", false));
        }

        [Fact]
        public async Task CreateApp_Cancelled_LeavesDatabaseUnchanged()
        {
            var pd = TestDatabase.Create();
            var service = new KeyService(pd, FastSettings);
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateAppAsync(Request(), cts.Token));
            }

            Assert.Empty(pd.RootGroup.Groups);
            Assert.False(pd.Modified);
        }

        [Fact]
        public async Task CreateApp_ExistingApp_FailsBeforeGenerating()
        {
            var pd = TestDatabase.Create();
            var service = new KeyService(pd, FastSettings);
            await service.CreateAppAsync(Request(), CancellationToken.None);
            pd.Modified = false;

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAppAsync(Request(), CancellationToken.None));
            Assert.False(pd.Modified);
        }

        [Fact]
        public async Task CreateApp_InvalidRequest_NamesTheFieldAndWritesNothing()
        {
            var pd = TestDatabase.Create();
            var service = new KeyService(pd, FastSettings);
            var request = Request();
            request.PackageId = "not valid";

            var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAppAsync(request, CancellationToken.None));

            Assert.Contains("package", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(pd.RootGroup.Groups);
            Assert.False(pd.Modified);
        }
    }
}

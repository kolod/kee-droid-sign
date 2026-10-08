using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KeeDroidSign.Core.GitHub;
using KeeDroidSign.Core.Keystore;
using KeeDroidSign.Core.Tests.Support;
using KeeDroidSign.Services;
using KeeDroidSign.Settings;
using KeeDroidSign.Storage;
using KeeDroidSign.Tests.Support;
using KeePassLib;
using KeePassLib.Security;
using Sodium;
using Xunit;

namespace KeeDroidSign.Tests.Services
{
    public class ExportServiceTests
    {
        private const string Token = "github_pat_PLUGINTEST_1234567890";
        private const string SecretsPath = "/repos/octo/app/actions/secrets";

        private readonly KeyPair _repoKey = PublicKeyBox.GenerateKeyPair();

        private sealed class Fixture
        {
            public PwDatabase Database;
            public PluginSettings Settings;
            public KeyContext Key;
            public SigningKeystore Keystore;
        }

        private static Fixture Setup()
        {
            var pd = TestDatabase.Create();
            SigningKeystore ks = TestDatabase.Keystore();
            PwGroup group = TestDatabase.AddApp(pd, "DroidSign", "com.example.app", ks);

            var tokenEntry = new PwEntry(true, true);
            tokenEntry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "GitHub"));
            tokenEntry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, Token));
            pd.RootGroup.AddEntry(tokenEntry, true);

            PluginSettings settings = KeyServiceCreateTests.FastSettings();
            settings.TokenEntryUuid = tokenEntry.Uuid.ToHexString();

            KeyContext key = new DroidSignStore(pd, settings).ResolveKey(group.Entries.First(DroidSignStore.IsKeyEntry));
            return new Fixture { Database = pd, Settings = settings, Key = key, Keystore = ks };
        }

        private FakeGitHubHandler Repository(params string[] existing)
        {
            string list = "{\"total_count\":" + existing.Length + ",\"secrets\":[" +
                string.Join(",", existing.Select(n => "{\"name\":\"" + n + "\"}")) + "]}";
            var handler = new FakeGitHubHandler()
                .On("GET", SecretsPath + "?per_page=100&page=1", HttpStatusCode.OK, list)
                .On("GET", SecretsPath + "/public-key", HttpStatusCode.OK,
                    "{\"key_id\":\"k1\",\"key\":\"" + Convert.ToBase64String(_repoKey.PublicKey) + "\"}");
            foreach (string name in new[] { "ANDROID_KEYSTORE_BASE64", "ANDROID_KEYSTORE_PASSWORD", "ANDROID_KEY_ALIAS",
                "ANDROID_KEY_PASSWORD", "STORE_PW" })
                handler.On("PUT", SecretsPath + "/" + name, existing.Contains(name) ? HttpStatusCode.NoContent : HttpStatusCode.Created);
            return handler;
        }

        private string Decrypt(RecordedRequest put)
        {
            var dto = KeeDroidSign.Core.GitHub.Json.Deserialize<PutSecretDto>(put.Body);
            return Encoding.UTF8.GetString(SealedPublicKeyBox.Open(
                Convert.FromBase64String(dto.EncryptedValue), _repoKey.PrivateKey, _repoKey.PublicKey));
        }

        [Fact]
        public void ResolveToken_ReadsPasswordOfConfiguredEntry()
        {
            Fixture f = Setup();
            var service = new ExportService(() => f.Settings);

            GitHubCredential credential = service.ResolveToken(f.Database);

            Assert.Equal(Token, credential.Token);
        }

        [Theory]
        [InlineData("")]
        [InlineData("0123456789ABCDEF0123456789ABCDEF")]
        public void ResolveToken_NotConfiguredOrMissing_ReturnsNull(string uuid)
        {
            Fixture f = Setup();
            f.Settings.TokenEntryUuid = uuid;

            Assert.Null(new ExportService(() => f.Settings).ResolveToken(f.Database));
        }

        [Fact]
        public async Task Export_SendsFourSecretsOfThisKey()
        {
            Fixture f = Setup();
            var handler = Repository();
            var service = new ExportService(() => f.Settings, handler);

            ExportResult result = await service.ExportAsync(f.Key, new GitHubCredential(Token), false, CancellationToken.None);

            Assert.True(result.Succeeded);
            var values = handler.Requests.Where(r => r.Method == "PUT")
                .ToDictionary(r => r.Uri.AbsolutePath.Split('/').Last(), Decrypt);
            Assert.Equal(f.Keystore.ToBase64(), values["ANDROID_KEYSTORE_BASE64"]);
            Assert.Equal(TestDatabase.StorePassword, values["ANDROID_KEYSTORE_PASSWORD"]);
            Assert.Equal("1", values["ANDROID_KEY_ALIAS"]);
            Assert.Equal(TestDatabase.KeyPassword, values["ANDROID_KEY_PASSWORD"]);
            Assert.All(handler.Requests, r => Assert.Equal("Bearer " + Token, r.Headers["Authorization"]));
        }

        [Fact]
        public async Task Export_UsesSecretNamesFromSettings()
        {
            Fixture f = Setup();
            f.Settings.SecretStorePassword = "STORE_PW";
            var handler = Repository();

            await new ExportService(() => f.Settings, handler).ExportAsync(f.Key, new GitHubCredential(Token), false, CancellationToken.None);

            Assert.Contains(handler.Requests, r => r.Method == "PUT" && r.Uri.AbsolutePath.EndsWith("/STORE_PW"));
            Assert.DoesNotContain(handler.Requests, r => r.Method == "PUT" && r.Uri.AbsolutePath.EndsWith("/ANDROID_KEYSTORE_PASSWORD"));
        }

        [Fact]
        public async Task Plan_ListsConflictsAndExportWithoutOverwriteWritesNothing()
        {
            Fixture f = Setup();
            var handler = Repository("ANDROID_KEY_ALIAS");
            var service = new ExportService(() => f.Settings, handler);
            var token = new GitHubCredential(Token);

            ExportPlan plan = await service.PlanAsync(f.Key, token, CancellationToken.None);
            ExportResult result = await service.ExportAsync(f.Key, token, false, CancellationToken.None);

            Assert.Equal(new[] { "ANDROID_KEY_ALIAS" }, plan.ToOverwrite);
            Assert.False(result.Succeeded);
            Assert.DoesNotContain(handler.Requests, r => r.Method == "PUT");
        }

        [Fact]
        public async Task Export_KeyWithProblems_IsRefused()
        {
            Fixture f = Setup();
            f.Key.App.KeystoreEntry.Strings.Remove(PwDefs.UrlField);
            KeyContext broken = new DroidSignStore(f.Database, f.Settings).ResolveKey(f.Key.Key.Entry);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ExportService(() => f.Settings, Repository()).ExportAsync(broken, new GitHubCredential(Token), false, CancellationToken.None));
        }

        [Fact]
        public async Task Export_Failure_DoesNotLeakSecrets()
        {
            Fixture f = Setup();
            var handler = new FakeGitHubHandler()
                .On("GET", SecretsPath + "?per_page=100&page=1", HttpStatusCode.Forbidden, "{\"message\":\"" + Token + "\"}");

            var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
                new ExportService(() => f.Settings, handler).ExportAsync(f.Key, new GitHubCredential(Token), false, CancellationToken.None));

            string text = ex.ToString();
            foreach (string secret in new[] { Token, TestDatabase.StorePassword, TestDatabase.KeyPassword, f.Keystore.ToBase64() })
                Assert.DoesNotContain(secret, text);
        }
    }
}

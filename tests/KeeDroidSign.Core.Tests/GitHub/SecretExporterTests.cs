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
using Sodium;
using Xunit;

namespace KeeDroidSign.Core.Tests.GitHub
{
    public class SecretExporterTests
    {
        private static readonly RepositoryTarget Repo = RepositoryTarget.Parse("octo/app");
        private const string SecretsPath = "/repos/octo/app/actions/secrets";
        private const string KeyId = "568250167242549743";

        private static readonly string[] DefaultNames =
            { "ANDROID_KEYSTORE_BASE64", "ANDROID_KEYSTORE_PASSWORD", "ANDROID_KEY_ALIAS", "ANDROID_KEY_PASSWORD" };

        private readonly KeyPair _repoKey = PublicKeyBox.GenerateKeyPair();

        private static SigningKeystore Keystore(int contentSize = 2000) =>
            new SigningKeystore(Enumerable.Range(0, contentSize).Select(i => (byte)i).ToArray(), "upload",
                "store-secret-123", "key-secret-456", new byte[] { 1 }, DateTime.UtcNow, DateTime.UtcNow.AddYears(30));

        private FakeGitHubHandler Repository(IEnumerable<string> existing, Func<string, HttpStatusCode> putStatus = null)
        {
            string list = "{\"total_count\":" + existing.Count() + ",\"secrets\":[" +
                string.Join(",", existing.Select(n => "{\"name\":\"" + n + "\"}")) + "]}";
            var handler = new FakeGitHubHandler()
                .On("GET", SecretsPath + "?per_page=100&page=1", HttpStatusCode.OK, list)
                .On("GET", SecretsPath + "/public-key", HttpStatusCode.OK,
                    "{\"key_id\":\"" + KeyId + "\",\"key\":\"" + Convert.ToBase64String(_repoKey.PublicKey) + "\"}");

            var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
            foreach (string name in DefaultNames.Concat(new[] { "CUSTOM_STORE", "CUSTOM_ALIAS" }))
            {
                string captured = name;
                handler.On("PUT", SecretsPath + "/" + name, _ => FakeGitHubHandler.Response(
                    putStatus?.Invoke(captured) ?? (existingSet.Contains(captured) ? HttpStatusCode.NoContent : HttpStatusCode.Created)));
            }
            return handler;
        }

        private static SecretExporter Exporter(FakeGitHubHandler handler) =>
            new SecretExporter(new GitHubClient(new GitHubCredential(GitHubClientAuthTests.Token), handler));

        private string Decrypt(RecordedRequest put)
        {
            var dto = Json.Deserialize<PutSecretDto>(put.Body);
            Assert.Equal(KeyId, dto.KeyId);
            byte[] plain = SealedPublicKeyBox.Open(Convert.FromBase64String(dto.EncryptedValue), _repoKey.PrivateKey, _repoKey.PublicKey);
            return Encoding.UTF8.GetString(plain);
        }

        private static string SecretName(RecordedRequest put) => put.Uri.AbsolutePath.Split('/').Last();

        [Fact]
        public async Task Export_EmptyRepository_CreatesAllSecretsWithCorrectValues()
        {
            var handler = Repository(new string[0]);
            SigningKeystore keystore = Keystore();

            ExportResult result = await Exporter(handler).ExportAsync(Repo, keystore, new SecretMapping(), false, CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(DefaultNames.OrderBy(n => n), result.Outcomes.Select(o => o.Name).OrderBy(n => n));
            Assert.All(result.Outcomes, o => Assert.Equal(SecretOutcomeStatus.Created, o.Status));

            var puts = handler.Requests.Where(r => r.Method == "PUT").ToDictionary(SecretName, Decrypt);
            Assert.Equal(keystore.ToBase64(), puts["ANDROID_KEYSTORE_BASE64"]);
            Assert.Equal("store-secret-123", puts["ANDROID_KEYSTORE_PASSWORD"]);
            Assert.Equal("upload", puts["ANDROID_KEY_ALIAS"]);
            Assert.Equal("key-secret-456", puts["ANDROID_KEY_PASSWORD"]);
        }

        [Fact]
        public async Task Export_ConflictsWithoutOverwrite_WritesNothing()
        {
            var handler = Repository(new[] { "android_key_alias", "OTHER" });

            ExportResult result = await Exporter(handler).ExportAsync(Repo, Keystore(), new SecretMapping(), false, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.All(result.Outcomes, o => Assert.Equal(SecretOutcomeStatus.SkippedConflict, o.Status));
            Assert.DoesNotContain(handler.Requests, r => r.Method == "PUT");
        }

        [Fact]
        public async Task Export_ConflictsWithOverwrite_UpdatesExisting()
        {
            var handler = Repository(new[] { "ANDROID_KEY_ALIAS", "ANDROID_KEY_PASSWORD" });

            ExportResult result = await Exporter(handler).ExportAsync(Repo, Keystore(), new SecretMapping(), true, CancellationToken.None);

            Assert.True(result.Succeeded);
            var byName = result.Outcomes.ToDictionary(o => o.Name, o => o.Status);
            Assert.Equal(SecretOutcomeStatus.Updated, byName["ANDROID_KEY_ALIAS"]);
            Assert.Equal(SecretOutcomeStatus.Updated, byName["ANDROID_KEY_PASSWORD"]);
            Assert.Equal(SecretOutcomeStatus.Created, byName["ANDROID_KEYSTORE_BASE64"]);
        }

        [Fact]
        public async Task Export_CustomNames_AreUsed()
        {
            var handler = Repository(new string[0]);
            var mapping = new SecretMapping { StorePasswordName = "CUSTOM_STORE", KeyAliasName = "CUSTOM_ALIAS" };

            ExportResult result = await Exporter(handler).ExportAsync(Repo, Keystore(), mapping, false, CancellationToken.None);

            Assert.True(result.Succeeded);
            var puts = handler.Requests.Where(r => r.Method == "PUT").ToDictionary(SecretName, Decrypt);
            Assert.Equal("store-secret-123", puts["CUSTOM_STORE"]);
            Assert.Equal("upload", puts["CUSTOM_ALIAS"]);
            Assert.False(puts.ContainsKey("ANDROID_KEY_ALIAS"));
        }

        [Fact]
        public async Task Export_OneFailure_OthersStillAttempted()
        {
            var handler = Repository(new string[0], name => name == "ANDROID_KEY_ALIAS" ? HttpStatusCode.Forbidden : HttpStatusCode.Created);

            ExportResult result = await Exporter(handler).ExportAsync(Repo, Keystore(), new SecretMapping(), false, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(4, handler.Requests.Count(r => r.Method == "PUT"));
            SecretOutcome failed = result.Outcomes.Single(o => o.Status == SecretOutcomeStatus.Failed);
            Assert.Equal("ANDROID_KEY_ALIAS", failed.Name);
            Assert.False(string.IsNullOrEmpty(failed.Reason));
        }

        [Fact]
        public async Task Plan_ReportsCreateAndOverwrite()
        {
            var handler = Repository(new[] { "android_keystore_password" });

            ExportPlan plan = await Exporter(handler).PlanAsync(Repo, new SecretMapping(), CancellationToken.None);

            Assert.Equal(new[] { "ANDROID_KEYSTORE_PASSWORD" }, plan.ToOverwrite);
            Assert.Equal(3, plan.ToCreate.Count);
            Assert.DoesNotContain("ANDROID_KEYSTORE_PASSWORD", plan.ToCreate);
        }

        [Fact]
        public async Task Export_ValueOver48KB_RejectedBeforeAnyRequest()
        {
            var handler = Repository(new string[0]);

            await Assert.ThrowsAsync<ArgumentException>(
                () => Exporter(handler).ExportAsync(Repo, Keystore(40000), new SecretMapping(), false, CancellationToken.None));
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task Export_InvalidSecretName_RejectedBeforeAnyRequest()
        {
            var handler = Repository(new string[0]);

            await Assert.ThrowsAsync<ArgumentException>(() => Exporter(handler).ExportAsync(
                Repo, Keystore(), new SecretMapping { KeyAliasName = "GITHUB_ALIAS" }, false, CancellationToken.None));
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task Export_Cancelled_Throws()
        {
            var handler = Repository(new string[0]);
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => Exporter(handler).ExportAsync(Repo, Keystore(), new SecretMapping(), false, cts.Token));
            }
        }

        [Fact]
        public async Task Export_NoPlaintextSecretInRequestsOrResults()
        {
            var handler = Repository(new[] { "ANDROID_KEY_ALIAS" }, name => name == "ANDROID_KEY_PASSWORD" ? (HttpStatusCode)422 : HttpStatusCode.Created);
            SigningKeystore keystore = Keystore();

            ExportResult result = await Exporter(handler).ExportAsync(Repo, keystore, new SecretMapping(), true, CancellationToken.None);

            var secrets = new[] { keystore.StorePassword, keystore.KeyPassword, keystore.ToBase64(), GitHubClientAuthTests.Token };
            SecretLeakScanner.AssertNoLeak(secrets, handler.Requests.Select(r => r.Body).ToArray());
            SecretLeakScanner.AssertNoLeak(secrets, result.ToString());
            SecretLeakScanner.AssertNoLeak(secrets, result.Outcomes.Select(o => o.ToString() + o.Reason).ToArray());
        }
    }
}

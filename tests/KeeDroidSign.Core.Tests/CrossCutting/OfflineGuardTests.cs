using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using KeeDroidSign.Core.GitHub;
using KeeDroidSign.Core.Keystore;
using KeeDroidSign.Core.Tests.Keystore;
using KeeDroidSign.Core.Tests.Support;
using Sodium;
using Xunit;

namespace KeeDroidSign.Core.Tests.CrossCutting
{
    /// <summary>
    /// Runs the complete flow (token check, access check, export) against the fake GitHub and
    /// verifies every HTTP call went through the injected handler, i.e. nothing reached the network.
    /// </summary>
    public class OfflineGuardTests
    {
        [Fact]
        public async Task FullFlow_AllRequestsGoThroughInjectedHandler()
        {
            KeyPair repoKey = PublicKeyBox.GenerateKeyPair();
            var handler = new FakeGitHubHandler()
                .On("GET", "/user", HttpStatusCode.OK, "{\"login\":\"octocat\"}")
                .On("GET", "/repos/octo/app", HttpStatusCode.OK, "{\"archived\":false}")
                .On("GET", "/repos/octo/app/actions/secrets?per_page=1", HttpStatusCode.OK, "{\"total_count\":0,\"secrets\":[]}")
                .On("GET", "/repos/octo/app/actions/secrets?per_page=100&page=1", HttpStatusCode.OK, "{\"total_count\":0,\"secrets\":[]}")
                .On("GET", "/repos/octo/app/actions/secrets/public-key", HttpStatusCode.OK,
                    "{\"key_id\":\"1\",\"key\":\"" + System.Convert.ToBase64String(repoKey.PublicKey) + "\"}");
            foreach (string name in new[] { "ANDROID_KEYSTORE_BASE64", "ANDROID_KEYSTORE_PASSWORD", "ANDROID_KEY_ALIAS", "ANDROID_KEY_PASSWORD" })
                handler.On("PUT", "/repos/octo/app/actions/secrets/" + name, HttpStatusCode.Created);

            SigningKeystore keystore = await new KeystoreGenerator()
                .GenerateAsync(KeystoreGeneratorTests.FastRequest(), CancellationToken.None);
            var repo = RepositoryTarget.Parse("octo/app");

            using (var client = new GitHubClient(new GitHubCredential("ghp_offline"), handler))
            {
                Assert.Equal(TokenStatus.Valid, (await client.ValidateTokenAsync(CancellationToken.None)).Status);
                Assert.Equal(RepositoryAccess.CanManageSecrets, await client.CheckRepositoryAccessAsync(repo, CancellationToken.None));
                ExportResult result = await new SecretExporter(client)
                    .ExportAsync(repo, keystore, new SecretMapping(), false, CancellationToken.None);
                Assert.True(result.Succeeded);
            }

            // 1 user + 2 access + 1 list + 1 public key + 4 PUT = 9 calls, all recorded, all to api.github.com.
            Assert.Equal(9, handler.Requests.Count);
            Assert.All(handler.Requests, r => Assert.Equal("https://api.github.com", r.Uri.GetLeftPart(System.UriPartial.Authority)));
            Assert.DoesNotContain(handler.Requests, r => r.Uri.PathAndQuery == "/" || r.Uri.Host != "api.github.com");
            Assert.Equal(4, handler.Requests.Count(r => r.Method == "PUT"));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KeeDroidSign.Core.GitHub;
using KeeDroidSign.Core.Tests.Support;
using Xunit;

namespace KeeDroidSign.Core.Tests.GitHub
{
    public class GitHubClientSecretsTests
    {
        private static readonly RepositoryTarget Repo = RepositoryTarget.Parse("octo/app");
        private const string SecretsPath = "/repos/octo/app/actions/secrets";

        private static GitHubClient Client(FakeGitHubHandler handler) =>
            new GitHubClient(new GitHubCredential(GitHubClientAuthTests.Token), handler);

        [Fact]
        public async Task ListSecretNames_PaginatesUntilTotalCount()
        {
            string page1 = "{\"total_count\":102,\"secrets\":[" +
                string.Join(",", Enumerable.Range(0, 100).Select(i => "{\"name\":\"S" + i + "\"}")) + "]}";
            string page2 = "{\"total_count\":102,\"secrets\":[{\"name\":\"ANDROID_KEY_ALIAS\"},{\"name\":\"OTHER\"}]}";
            var handler = new FakeGitHubHandler()
                .On("GET", SecretsPath + "?per_page=100&page=1", HttpStatusCode.OK, page1)
                .On("GET", SecretsPath + "?per_page=100&page=2", HttpStatusCode.OK, page2);

            using (var client = Client(handler))
            {
                IReadOnlyCollection<string> names = await client.ListSecretNamesAsync(Repo, CancellationToken.None);

                Assert.Equal(102, names.Count);
                Assert.Contains("ANDROID_KEY_ALIAS", names);
                Assert.Equal(2, handler.Requests.Count);
            }
        }

        [Fact]
        public async Task ListSecretNames_StopsOnEmptyPage()
        {
            var handler = new FakeGitHubHandler()
                .On("GET", SecretsPath + "?per_page=100&page=1", HttpStatusCode.OK, "{\"total_count\":5,\"secrets\":[{\"name\":\"A\"}]}")
                .On("GET", SecretsPath + "?per_page=100&page=2", HttpStatusCode.OK, "{\"total_count\":5,\"secrets\":[]}");

            using (var client = Client(handler))
                Assert.Single(await client.ListSecretNamesAsync(Repo, CancellationToken.None));
        }

        [Fact]
        public async Task ListSecretNames_Forbidden_ThrowsWithoutBody()
        {
            var handler = new FakeGitHubHandler()
                .On("GET", SecretsPath + "?per_page=100&page=1", HttpStatusCode.Forbidden, "{\"message\":\"SECRET-ECHO\"}");

            using (var client = Client(handler))
            {
                var ex = await Assert.ThrowsAsync<GitHubApiException>(() => client.ListSecretNamesAsync(Repo, CancellationToken.None));
                Assert.DoesNotContain("SECRET-ECHO", ex.Message);
            }
        }

        [Fact]
        public async Task GetPublicKey_ReturnsDecodedKey()
        {
            var key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
            var handler = new FakeGitHubHandler().On("GET", SecretsPath + "/public-key", HttpStatusCode.OK,
                "{\"key_id\":\"012345678912345678\",\"key\":\"" + Convert.ToBase64String(key) + "\"}");

            using (var client = Client(handler))
            {
                RepositoryPublicKey publicKey = await client.GetPublicKeyAsync(Repo, CancellationToken.None);

                Assert.Equal("012345678912345678", publicKey.KeyId);
                Assert.Equal(key, publicKey.Key);
            }
        }

        [Theory]
        [InlineData("AAAA")]
        [InlineData("not-base64!")]
        public async Task GetPublicKey_RejectsInvalidKey(string key)
        {
            var handler = new FakeGitHubHandler().On("GET", SecretsPath + "/public-key", HttpStatusCode.OK,
                "{\"key_id\":\"1\",\"key\":\"" + key + "\"}");

            using (var client = Client(handler))
                await Assert.ThrowsAsync<GitHubApiException>(() => client.GetPublicKeyAsync(Repo, CancellationToken.None));
        }

        [Fact]
        public async Task PutSecret_SendsJsonBody()
        {
            var handler = new FakeGitHubHandler().On("PUT", SecretsPath + "/ANDROID_KEY_ALIAS", HttpStatusCode.Created);

            using (var client = Client(handler))
            {
                SecretWriteResult result = await client.PutSecretAsync(Repo, "ANDROID_KEY_ALIAS", "ZW5j", "key-1", CancellationToken.None);

                Assert.Equal(SecretWriteStatus.Created, result.Status);
            }

            RecordedRequest request = handler.Requests.Single();
            Assert.Equal("PUT", request.Method);
            Assert.StartsWith("application/json", request.Headers["Content-Type"]);
            var dto = Json.Deserialize<PutSecretDto>(request.Body);
            Assert.Equal("ZW5j", dto.EncryptedValue);
            Assert.Equal("key-1", dto.KeyId);
        }

        [Theory]
        [InlineData(HttpStatusCode.Created, SecretWriteStatus.Created)]
        [InlineData(HttpStatusCode.NoContent, SecretWriteStatus.Updated)]
        [InlineData(HttpStatusCode.Forbidden, SecretWriteStatus.Failed)]
        [InlineData(HttpStatusCode.NotFound, SecretWriteStatus.Failed)]
        [InlineData((HttpStatusCode)422, SecretWriteStatus.Failed)]
        [InlineData((HttpStatusCode)429, SecretWriteStatus.Failed)]
        [InlineData(HttpStatusCode.BadGateway, SecretWriteStatus.Failed)]
        public async Task PutSecret_MapsStatus(HttpStatusCode status, SecretWriteStatus expected)
        {
            var handler = new FakeGitHubHandler().On("PUT", SecretsPath + "/X", status, "{\"message\":\"BODY-ECHO\"}");

            using (var client = Client(handler))
            {
                SecretWriteResult result = await client.PutSecretAsync(Repo, "X", "ZW5j", "key-1", CancellationToken.None);

                Assert.Equal(expected, result.Status);
                if (expected == SecretWriteStatus.Failed)
                {
                    Assert.False(string.IsNullOrEmpty(result.Reason));
                    Assert.DoesNotContain("BODY-ECHO", result.Reason);
                }
            }
        }

        [Fact]
        public async Task PutSecret_NetworkFailure_ReportsFailed()
        {
            var handler = new FakeGitHubHandler().Throw("PUT", SecretsPath + "/X", new HttpRequestException("reset"));

            using (var client = Client(handler))
            {
                SecretWriteResult result = await client.PutSecretAsync(Repo, "X", "ZW5j", "key-1", CancellationToken.None);

                Assert.Equal(SecretWriteStatus.Failed, result.Status);
                Assert.Contains("network", result.Reason, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}

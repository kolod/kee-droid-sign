using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using KeeDroidSign.Core.GitHub;
using KeeDroidSign.Core.Tests.Support;
using Sodium;
using Xunit;

namespace KeeDroidSign.Core.Tests.GitHub
{
    public class GitHubClientEnvironmentTests
    {
        private static readonly RepositoryTarget Repo = RepositoryTarget.Parse("octo/app");
        private static readonly SecretScope Release = new SecretScope(Repo, "release");
        private const string EnvPath = "/repos/octo/app/environments/release";

        private static GitHubClient Client(FakeGitHubHandler handler) =>
            new GitHubClient(new GitHubCredential(GitHubClientAuthTests.Token), handler);

        [Fact]
        public async Task PublicKey_EnvironmentScope_ReadsEnvironmentKey()
        {
            KeyPair key = PublicKeyBox.GenerateKeyPair();
            var handler = new FakeGitHubHandler().SecretStore(EnvPath + "/secrets", new string[0], key.PublicKey, "42");

            using (GitHubClient client = Client(handler))
            {
                RepositoryPublicKey result = await client.GetPublicKeyAsync(Release, CancellationToken.None);

                Assert.Equal("42", result.KeyId);
                Assert.Equal(key.PublicKey, result.Key);
            }
            Assert.Equal(EnvPath + "/secrets/public-key", handler.Requests.Single().Uri.AbsolutePath);
        }

        [Fact]
        public async Task PublicKey_MissingEnvironment_ThrowsEnvironmentNotFound()
        {
            using (GitHubClient client = Client(new FakeGitHubHandler()))
            {
                var ex = await Assert.ThrowsAsync<EnvironmentNotFoundException>(
                    () => client.GetPublicKeyAsync(Release, CancellationToken.None));

                Assert.Equal("release", ex.Environment);
                Assert.Contains("octo/app", ex.Message);
                Assert.DoesNotContain(GitHubClientAuthTests.Token, ex.Message);
            }
        }

        [Fact]
        public async Task PublicKey_Forbidden_NamesEnvironmentsPermission()
        {
            var handler = new FakeGitHubHandler().On("GET", EnvPath + "/secrets/public-key", HttpStatusCode.Forbidden, "{}");

            using (GitHubClient client = Client(handler))
            {
                var ex = await Assert.ThrowsAsync<GitHubApiException>(() => client.GetPublicKeyAsync(Release, CancellationToken.None));

                Assert.IsNotType<EnvironmentNotFoundException>(ex);
                Assert.Contains("Environments permission", ex.Message);
            }
        }

        [Fact]
        public async Task PutSecret_EnvironmentScope_WritesToEnvironment()
        {
            var handler = new FakeGitHubHandler().SecretStore(EnvPath + "/secrets", new string[0], new byte[32], "1");

            using (GitHubClient client = Client(handler))
            {
                SecretWriteResult result = await client.PutSecretAsync(Release, "ANDROID_KEY_ALIAS", "ZW5j", "1", CancellationToken.None);

                Assert.Equal(SecretWriteStatus.Created, result.Status);
            }
            Assert.Equal(EnvPath + "/secrets/ANDROID_KEY_ALIAS", handler.Requests.Single().Uri.AbsolutePath);
        }

        [Fact]
        public async Task PutSecret_EnvironmentForbidden_ReportsEnvironmentPermission()
        {
            var handler = new FakeGitHubHandler().On("PUT", EnvPath + "/secrets/ANDROID_KEY_ALIAS", HttpStatusCode.Forbidden);

            using (GitHubClient client = Client(handler))
            {
                SecretWriteResult result = await client.PutSecretAsync(Release, "ANDROID_KEY_ALIAS", "ZW5j", "1", CancellationToken.None);

                Assert.Equal(SecretWriteStatus.Failed, result.Status);
                Assert.Contains("environment secrets", result.Reason);
            }
        }

        [Theory]
        [InlineData("null", BranchPolicyKind.AllBranches)]
        [InlineData("{\"protected_branches\":true,\"custom_branch_policies\":false}", BranchPolicyKind.ProtectedBranches)]
        [InlineData("{\"protected_branches\":false,\"custom_branch_policies\":true}", BranchPolicyKind.CustomPolicies)]
        public async Task GetEnvironment_MapsBranchPolicy(string policyJson, BranchPolicyKind expected)
        {
            var handler = new FakeGitHubHandler().On("GET", EnvPath, HttpStatusCode.OK,
                "{\"name\":\"release\",\"deployment_branch_policy\":" + policyJson + ",\"protection_rules\":[]}");

            using (GitHubClient client = Client(handler))
            {
                EnvironmentInfo info = await client.GetEnvironmentAsync(Repo, "release", CancellationToken.None);

                Assert.Equal(EnvironmentExistence.Found, info.Existence);
                Assert.Equal(expected, info.Policy);
            }
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound, EnvironmentExistence.NotFound)]
        [InlineData(HttpStatusCode.Forbidden, EnvironmentExistence.NoPermission)]
        public async Task GetEnvironment_MapsFailures(HttpStatusCode status, EnvironmentExistence expected)
        {
            var handler = new FakeGitHubHandler().On("GET", EnvPath, status, "{}");

            using (GitHubClient client = Client(handler))
            {
                EnvironmentInfo info = await client.GetEnvironmentAsync(Repo, "release", CancellationToken.None);

                Assert.Equal(expected, info.Existence);
                Assert.Equal(BranchPolicyKind.Unknown, info.Policy);
            }
        }

        [Theory]
        [InlineData(HttpStatusCode.NoContent, true)]
        [InlineData(HttpStatusCode.NotFound, true)]
        [InlineData(HttpStatusCode.Forbidden, false)]
        public async Task DeleteSecret_MapsStatus(HttpStatusCode status, bool deleted)
        {
            var handler = new FakeGitHubHandler().On("DELETE", "/repos/octo/app/actions/secrets/ANDROID_KEY_ALIAS", status);

            using (GitHubClient client = Client(handler))
            {
                SecretDeleteResult result = await client.DeleteSecretAsync(Repo, "ANDROID_KEY_ALIAS", CancellationToken.None);

                Assert.Equal(deleted, result.Deleted);
                Assert.Equal(deleted, result.Reason == null);
            }
        }
    }
}

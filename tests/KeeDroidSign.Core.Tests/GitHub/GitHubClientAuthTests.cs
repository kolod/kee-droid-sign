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
    public class GitHubClientAuthTests
    {
        internal const string Token = "ghp_testTokenValue1234567890";
        private static readonly RepositoryTarget Repo = RepositoryTarget.Parse("octo/app");

        private static GitHubClient Client(FakeGitHubHandler handler) =>
            new GitHubClient(new GitHubCredential(Token), handler);

        [Fact]
        public async Task ValidateToken_Success_ReturnsLoginAndScopes()
        {
            var handler = new FakeGitHubHandler().On("GET", "/user", HttpStatusCode.OK,
                "{\"login\":\"octocat\",\"id\":1}", new Dictionary<string, string> { ["X-OAuth-Scopes"] = "repo, workflow" });

            using (var client = Client(handler))
            {
                TokenValidationResult result = await client.ValidateTokenAsync(CancellationToken.None);

                Assert.Equal(TokenStatus.Valid, result.Status);
                Assert.Equal("octocat", result.Identity.Login);
                Assert.Equal(new[] { "repo", "workflow" }, result.Identity.TokenScopes);
            }
        }

        [Fact]
        public async Task ValidateToken_FineGrainedToken_HasNoScopes()
        {
            var handler = new FakeGitHubHandler().On("GET", "/user", HttpStatusCode.OK, "{\"login\":\"octocat\"}");

            using (var client = Client(handler))
            {
                TokenValidationResult result = await client.ValidateTokenAsync(CancellationToken.None);

                Assert.Empty(result.Identity.TokenScopes);
            }
        }

        [Fact]
        public async Task EveryRequest_SendsRequiredHeadersOverHttps()
        {
            var handler = new FakeGitHubHandler().On("GET", "/user", HttpStatusCode.OK, "{\"login\":\"octocat\"}");

            using (var client = Client(handler))
            {
                await client.ValidateTokenAsync(CancellationToken.None);
                await client.CheckRepositoryAccessAsync(Repo, CancellationToken.None);
            }

            Assert.NotEmpty(handler.Requests);
            Assert.All(handler.Requests, r =>
            {
                Assert.Equal("https", r.Uri.Scheme);
                Assert.Equal("api.github.com", r.Uri.Host);
                Assert.Equal("Bearer " + Token, r.Headers["Authorization"]);
                Assert.Equal("application/vnd.github+json", r.Headers["Accept"]);
                Assert.Equal("2022-11-28", r.Headers["X-GitHub-Api-Version"]);
                Assert.StartsWith("KeeDroidSign/", r.Headers["User-Agent"]);
            });
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized, null, TokenStatus.InvalidOrExpired)]
        [InlineData(HttpStatusCode.Forbidden, "0", TokenStatus.RateLimited)]
        [InlineData((HttpStatusCode)429, null, TokenStatus.RateLimited)]
        [InlineData(HttpStatusCode.Forbidden, null, TokenStatus.InsufficientPermissions)]
        [InlineData(HttpStatusCode.InternalServerError, null, TokenStatus.NetworkError)]
        public async Task ValidateToken_MapsErrorStatuses(HttpStatusCode status, string remaining, TokenStatus expected)
        {
            var headers = remaining == null ? null : new Dictionary<string, string> { ["x-ratelimit-remaining"] = remaining };
            var handler = new FakeGitHubHandler().On("GET", "/user", status, "{\"message\":\"" + Token + "\"}", headers);

            using (var client = Client(handler))
            {
                TokenValidationResult result = await client.ValidateTokenAsync(CancellationToken.None);

                Assert.Equal(expected, result.Status);
                Assert.Null(result.Identity);
                SecretLeakScanner.AssertNoLeak(new[] { Token }, result.ToString(), result.Message);
            }
        }

        [Fact]
        public async Task ValidateToken_NetworkFailure_ReturnsNetworkError()
        {
            var handler = new FakeGitHubHandler().Throw("GET", "/user", new HttpRequestException("connection refused"));

            using (var client = Client(handler))
            {
                TokenValidationResult result = await client.ValidateTokenAsync(CancellationToken.None);

                Assert.Equal(TokenStatus.NetworkError, result.Status);
            }
        }

        [Fact]
        public async Task ValidateToken_UserCancellation_Throws()
        {
            var handler = new FakeGitHubHandler().On("GET", "/user", HttpStatusCode.OK, "{\"login\":\"octocat\"}");

            using (var client = Client(handler))
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ValidateTokenAsync(cts.Token));
            }
        }

        [Fact]
        public async Task CheckRepositoryAccess_CanManageSecrets()
        {
            var handler = new FakeGitHubHandler()
                .On("GET", "/repos/octo/app", HttpStatusCode.OK, "{\"archived\":false}")
                .On("GET", "/repos/octo/app/actions/secrets?per_page=1", HttpStatusCode.OK, "{\"total_count\":0,\"secrets\":[]}");

            using (var client = Client(handler))
                Assert.Equal(RepositoryAccess.CanManageSecrets, await client.CheckRepositoryAccessAsync(Repo, CancellationToken.None));
        }

        [Fact]
        public async Task CheckRepositoryAccess_NotFound()
        {
            using (var client = Client(new FakeGitHubHandler()))
                Assert.Equal(RepositoryAccess.NotFoundOrNoAccess, await client.CheckRepositoryAccessAsync(Repo, CancellationToken.None));
        }

        [Fact]
        public async Task CheckRepositoryAccess_Archived()
        {
            var handler = new FakeGitHubHandler().On("GET", "/repos/octo/app", HttpStatusCode.OK, "{\"archived\":true}");

            using (var client = Client(handler))
                Assert.Equal(RepositoryAccess.Archived, await client.CheckRepositoryAccessAsync(Repo, CancellationToken.None));
        }

        [Theory]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.NotFound)]
        public async Task CheckRepositoryAccess_SecretsDenied(HttpStatusCode status)
        {
            var handler = new FakeGitHubHandler()
                .On("GET", "/repos/octo/app", HttpStatusCode.OK, "{\"archived\":false}")
                .On("GET", "/repos/octo/app/actions/secrets?per_page=1", status, "{}");

            using (var client = Client(handler))
                Assert.Equal(RepositoryAccess.InsufficientPermissions, await client.CheckRepositoryAccessAsync(Repo, CancellationToken.None));
        }

        [Fact]
        public async Task CheckRepositoryAccess_RateLimitedAndNetworkError()
        {
            var limited = new FakeGitHubHandler().On("GET", "/repos/octo/app", (HttpStatusCode)429);
            var broken = new FakeGitHubHandler().Throw("GET", "/repos/octo/app", new HttpRequestException("dns"));

            using (var client = Client(limited))
                Assert.Equal(RepositoryAccess.RateLimited, await client.CheckRepositoryAccessAsync(Repo, CancellationToken.None));
            using (var client = Client(broken))
                Assert.Equal(RepositoryAccess.NetworkError, await client.CheckRepositoryAccessAsync(Repo, CancellationToken.None));
        }

        [Fact]
        public void Credential_RedactsTokenAndRejectsEmpty()
        {
            var credential = new GitHubCredential(Token);

            SecretLeakScanner.AssertNoLeak(new[] { Token }, credential.ToString());
            Assert.Throws<ArgumentException>(() => new GitHubCredential(" "));
            Assert.Throws<ArgumentException>(() => new GitHubCredential(null));
        }

        [Fact]
        public void Client_RequiresCredential()
        {
            Assert.Throws<ArgumentNullException>(() => new GitHubClient(null));
        }
    }
}

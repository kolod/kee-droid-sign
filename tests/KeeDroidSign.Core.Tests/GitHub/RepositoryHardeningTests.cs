using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using KeeDroidSign.Core.GitHub;
using KeeDroidSign.Core.Tests.Support;
using Xunit;

namespace KeeDroidSign.Core.Tests.GitHub
{
    /// <summary>Optional repository changes offered for unprotected default branches and v* tags.</summary>
    public class RepositoryHardeningTests
    {
        private static readonly RepositoryTarget Repo = RepositoryTarget.Parse("octo/app");
        private const string RulesetsPath = "/repos/octo/app/rulesets";
        private const string PoliciesPath = "/repos/octo/app/environments/release/deployment-branch-policies";

        private static RepositoryHardening Hardening(FakeGitHubHandler handler) =>
            new RepositoryHardening(new GitHubClient(new GitHubCredential(GitHubClientAuthTests.Token), handler));

        [Fact]
        public async Task RestrictReleaseTags_CreatesTagRulesetWithAdminBypass()
        {
            var handler = new FakeGitHubHandler().On("POST", RulesetsPath, HttpStatusCode.Created, "{}");

            ProtectionResult result = await Hardening(handler).RestrictReleaseTagsAsync(Repo, CancellationToken.None);

            Assert.True(result.Succeeded);
            string body = handler.RequestsTo("POST", RulesetsPath).Single().Body;
            Assert.Contains("\"target\":\"tag\"", body);
            Assert.Contains("\"enforcement\":\"active\"", body);
            Assert.Contains("\"include\":[\"refs\\/tags\\/v*\"]", body);
            Assert.Contains("{\"type\":\"creation\"}", body);
            Assert.Contains("{\"type\":\"update\"}", body);
            Assert.Contains("{\"type\":\"deletion\"}", body);
            Assert.Contains("\"actor_id\":5,\"actor_type\":\"RepositoryRole\",\"bypass_mode\":\"always\"", body);
        }

        [Fact]
        public async Task ProtectDefaultBranch_RequiresPullRequestsWithoutApprovals()
        {
            var handler = new FakeGitHubHandler().On("POST", RulesetsPath, HttpStatusCode.Created, "{}");

            ProtectionResult result = await Hardening(handler).ProtectDefaultBranchAsync(Repo, "master", CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Contains("master", result.Message);
            string body = handler.RequestsTo("POST", RulesetsPath).Single().Body;
            Assert.Contains("\"target\":\"branch\"", body);
            Assert.Contains("\"include\":[\"~DEFAULT_BRANCH\"]", body);
            Assert.Contains("\"type\":\"pull_request\"", body);
            Assert.Contains("\"required_approving_review_count\":0", body);
            Assert.Contains("{\"type\":\"non_fast_forward\"}", body);
            Assert.Contains("\"bypass_mode\":\"pull_request\"", body);
        }

        [Theory]
        [InlineData(HttpStatusCode.Forbidden, ProtectionStatus.NeedsAdministration)]
        [InlineData(HttpStatusCode.NotFound, ProtectionStatus.NeedsAdministration)]
        [InlineData((HttpStatusCode)422, ProtectionStatus.Failed)]
        public async Task CreateRuleset_Failures(HttpStatusCode status, ProtectionStatus expected)
        {
            var handler = new FakeGitHubHandler().On("POST", RulesetsPath, status, "{}");

            ProtectionResult result = await Hardening(handler).RestrictReleaseTagsAsync(Repo, CancellationToken.None);

            Assert.Equal(expected, result.Status);
            Assert.False(result.Succeeded);
        }

        [Fact]
        public async Task RemoveTagPattern_DeletesOnlyTheTagPolicy()
        {
            var handler = new FakeGitHubHandler()
                .On("GET", PoliciesPath + "?per_page=100&page=1", HttpStatusCode.OK,
                    "{\"total_count\":2,\"branch_policies\":[{\"id\":11,\"name\":\"main\",\"type\":\"branch\"},{\"id\":12,\"name\":\"v*\",\"type\":\"tag\"}]}")
                .On("DELETE", PoliciesPath + "/12", HttpStatusCode.NoContent);

            ProtectionResult result = await Hardening(handler).RemoveTagPatternAsync(Repo, "release", CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(PoliciesPath + "/12", handler.RequestsTo("DELETE", PoliciesPath).Single().Uri.AbsolutePath);
        }

        [Fact]
        public async Task RemoveTagPattern_NoTagPolicy_ChangesNothing()
        {
            var handler = new FakeGitHubHandler()
                .On("GET", PoliciesPath + "?per_page=100&page=1", HttpStatusCode.OK,
                    "{\"total_count\":1,\"branch_policies\":[{\"id\":11,\"name\":\"main\",\"type\":\"branch\"}]}");

            ProtectionResult result = await Hardening(handler).RemoveTagPatternAsync(Repo, "release", CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Empty(handler.RequestsTo("DELETE", PoliciesPath));
        }
    }
}

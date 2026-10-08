using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using KeeDroidSign.Core.GitHub;
using KeeDroidSign.Core.Tests.Support;
using Xunit;

namespace KeeDroidSign.Core.Tests.GitHub
{
    public class EnvironmentProtectorTests
    {
        private static readonly RepositoryTarget Repo = RepositoryTarget.Parse("octo/app");
        private const string EnvPath = "/repos/octo/app/environments/release";
        private const string PoliciesPath = EnvPath + "/deployment-branch-policies";

        private static readonly DeploymentPattern[] Patterns =
            { new DeploymentPattern("main", DeploymentPattern.Branch), new DeploymentPattern("v*", DeploymentPattern.Tag) };

        private static EnvironmentProtector Protector(FakeGitHubHandler handler) =>
            new EnvironmentProtector(new GitHubClient(new GitHubCredential(GitHubClientAuthTests.Token), handler));

        private static string Environment(string policyJson, string rulesJson = "[]") =>
            "{\"name\":\"release\",\"deployment_branch_policy\":" + policyJson + ",\"protection_rules\":" + rulesJson + "}";

        private static string Policies(params string[] items) =>
            "{\"total_count\":" + items.Length + ",\"branch_policies\":[" + string.Join(",", items) + "]}";

        private static FakeGitHubHandler Writable(FakeGitHubHandler handler) =>
            handler.On("PUT", EnvPath, HttpStatusCode.OK, "{}").On("POST", PoliciesPath, HttpStatusCode.OK, "{}");

        [Fact]
        public async Task MissingEnvironment_IsCreatedWithMainAndTagPolicies()
        {
            var handler = Writable(new FakeGitHubHandler());

            ProtectionResult result = await Protector(handler).ProtectAsync(Repo, "release", Patterns, CancellationToken.None);

            Assert.Equal(ProtectionStatus.Protected, result.Status);
            Assert.True(result.Created);
            string put = handler.RequestsTo("PUT", EnvPath).Single().Body;
            Assert.Contains("\"custom_branch_policies\":true", put);
            Assert.Contains("\"protected_branches\":false", put);
            Assert.DoesNotContain("reviewers", put);
            var posts = handler.RequestsTo("POST", PoliciesPath).Select(r => r.Body).ToList();
            Assert.Equal(2, posts.Count);
            Assert.Contains(posts, b => b.Contains("\"name\":\"main\"") && b.Contains("\"type\":\"branch\""));
            Assert.Contains(posts, b => b.Contains("\"name\":\"v*\"") && b.Contains("\"type\":\"tag\""));
        }

        [Fact]
        public async Task AllBranches_KeepsReviewersAndWaitTimer()
        {
            string rules = "[{\"type\":\"wait_timer\",\"wait_timer\":15}," +
                "{\"type\":\"required_reviewers\",\"prevent_self_review\":true,\"reviewers\":[{\"type\":\"User\",\"reviewer\":{\"id\":77,\"login\":\"kolod\"}}]}," +
                "{\"type\":\"branch_policy\"}]";
            var handler = Writable(new FakeGitHubHandler()
                .On("GET", EnvPath, HttpStatusCode.OK, Environment("null", rules))
                .On("GET", PoliciesPath + "?per_page=100&page=1", HttpStatusCode.OK, Policies()));

            ProtectionResult result = await Protector(handler).ProtectAsync(Repo, "release", Patterns, CancellationToken.None);

            Assert.Equal(ProtectionStatus.Protected, result.Status);
            Assert.False(result.Created);
            string put = handler.RequestsTo("PUT", EnvPath).Single().Body;
            Assert.Contains("\"wait_timer\":15", put);
            Assert.Contains("\"prevent_self_review\":true", put);
            Assert.Contains("\"reviewers\":[{\"type\":\"User\",\"id\":77}]", put);
            Assert.Contains("\"custom_branch_policies\":true", put);
        }

        [Fact]
        public async Task ProtectedBranches_IsLeftUnchanged()
        {
            var handler = Writable(new FakeGitHubHandler()
                .On("GET", EnvPath, HttpStatusCode.OK, Environment("{\"protected_branches\":true,\"custom_branch_policies\":false}")));

            ProtectionResult result = await Protector(handler).ProtectAsync(Repo, "release", Patterns, CancellationToken.None);

            Assert.Equal(ProtectionStatus.AlreadyRestricted, result.Status);
            Assert.True(result.Succeeded);
            Assert.DoesNotContain(handler.Requests, r => r.Method != "GET");
        }

        [Fact]
        public async Task CustomPolicies_AddsOnlyMissingPatterns()
        {
            var handler = Writable(new FakeGitHubHandler()
                .On("GET", EnvPath, HttpStatusCode.OK, Environment("{\"protected_branches\":false,\"custom_branch_policies\":true}"))
                .On("GET", PoliciesPath + "?per_page=100&page=1", HttpStatusCode.OK,
                    Policies("{\"id\":1,\"name\":\"main\",\"type\":\"branch\"}", "{\"id\":2,\"name\":\"develop\",\"type\":\"branch\"}")));

            ProtectionResult result = await Protector(handler).ProtectAsync(Repo, "release", Patterns, CancellationToken.None);

            Assert.Equal(ProtectionStatus.Protected, result.Status);
            Assert.Empty(handler.RequestsTo("PUT", EnvPath));
            Assert.Contains("\"name\":\"v*\"", handler.RequestsTo("POST", PoliciesPath).Single().Body);
            Assert.Equal(new[] { "v* (tag)" }, result.AddedPolicies);
        }

        [Fact]
        public async Task ExistingPattern_SeeOther_CountsAsPresent()
        {
            var handler = new FakeGitHubHandler()
                .On("GET", EnvPath, HttpStatusCode.OK, Environment("{\"protected_branches\":false,\"custom_branch_policies\":true}"))
                .On("GET", PoliciesPath + "?per_page=100&page=1", HttpStatusCode.OK, Policies())
                .On("POST", PoliciesPath, HttpStatusCode.SeeOther);

            ProtectionResult result = await Protector(handler).ProtectAsync(Repo, "release", Patterns, CancellationToken.None);

            Assert.Equal(ProtectionStatus.Protected, result.Status);
            Assert.Empty(result.AddedPolicies);
        }

        [Theory]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.NotFound)]
        public async Task NoAdministration_OnPut_StopsWithoutFurtherCalls(HttpStatusCode status)
        {
            var handler = new FakeGitHubHandler().On("PUT", EnvPath, status, "{}");

            ProtectionResult result = await Protector(handler).ProtectAsync(Repo, "release", Patterns, CancellationToken.None);

            Assert.Equal(ProtectionStatus.NeedsAdministration, result.Status);
            Assert.Contains("Administration", result.Message);
            Assert.Empty(handler.RequestsTo("POST", PoliciesPath));
        }

        [Fact]
        public async Task NoAdministration_OnPost_IsReported()
        {
            var handler = new FakeGitHubHandler()
                .On("GET", EnvPath, HttpStatusCode.OK, Environment("{\"protected_branches\":false,\"custom_branch_policies\":true}"))
                .On("GET", PoliciesPath + "?per_page=100&page=1", HttpStatusCode.OK, Policies())
                .On("POST", PoliciesPath, HttpStatusCode.Forbidden, "{}");

            ProtectionResult result = await Protector(handler).ProtectAsync(Repo, "release", Patterns, CancellationToken.None);

            Assert.Equal(ProtectionStatus.NeedsAdministration, result.Status);
            Assert.Single(handler.RequestsTo("POST", PoliciesPath));
        }

        [Fact]
        public async Task EnvironmentUnreadable_NeedsActionsRead()
        {
            var handler = new FakeGitHubHandler().On("GET", EnvPath, HttpStatusCode.Forbidden, "{}");

            ProtectionResult result = await Protector(handler).ProtectAsync(Repo, "release", Patterns, CancellationToken.None);

            Assert.Equal(ProtectionStatus.NeedsActionsRead, result.Status);
            Assert.DoesNotContain(handler.Requests, r => r.Method != "GET");
        }
    }
}

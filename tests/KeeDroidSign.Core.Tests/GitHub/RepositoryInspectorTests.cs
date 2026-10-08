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
    /// <summary>The wizard's read-only inspection: default branch, protection, environment and proposals.</summary>
    public class RepositoryInspectorTests
    {
        private static readonly RepositoryTarget Repo = RepositoryTarget.Parse("octo/app");
        private static readonly SecretScope Release = new SecretScope(Repo, "release");
        private const string RepoPath = "/repos/octo/app";
        private const string EnvPath = RepoPath + "/environments/release";

        private readonly KeyPair _key = PublicKeyBox.GenerateKeyPair();

        /// <summary>Repository with default branch <paramref name="branch"/>, no rules, no rulesets, unprotected.</summary>
        private FakeGitHubHandler Repository(string branch = "master")
        {
            return new FakeGitHubHandler()
                .On("GET", RepoPath, HttpStatusCode.OK, "{\"default_branch\":\"" + branch + "\",\"archived\":false}")
                .On("GET", RepoPath + "/rules/branches/" + branch + "?per_page=100", HttpStatusCode.OK, "[]")
                .On("GET", RepoPath + "/branches/" + branch, HttpStatusCode.OK, "{\"name\":\"" + branch + "\",\"protected\":false}")
                .On("GET", RepoPath + "/rulesets?targets=tag&includes_parents=true&per_page=100", HttpStatusCode.OK, "[]")
                .SecretStore(RepoPath + "/actions/secrets", new string[0], _key.PublicKey, "repo");
        }

        private FakeGitHubHandler WithEnvironment(FakeGitHubHandler handler, string policyJson, params string[] policies)
        {
            handler.SecretStore(EnvPath + "/secrets", new[] { "ANDROID_KEY_ALIAS" }, _key.PublicKey, "env")
                .On("GET", EnvPath, HttpStatusCode.OK, "{\"name\":\"release\",\"deployment_branch_policy\":" + policyJson + "}")
                .On("GET", EnvPath + "/deployment-branch-policies?per_page=100&page=1", HttpStatusCode.OK,
                    "{\"total_count\":" + policies.Length + ",\"branch_policies\":[" + string.Join(",", policies) + "]}");
            return handler;
        }

        private static Task<RepositoryReport> Inspect(FakeGitHubHandler handler, SecretScope scope = null) =>
            new RepositoryInspector(new GitHubClient(new GitHubCredential(GitHubClientAuthTests.Token), handler))
                .InspectAsync(scope ?? Release, new SecretMapping(), CancellationToken.None);

        [Fact]
        public async Task MissingEnvironment_ProposesDefaultBranchByRealNameAndTag()
        {
            RepositoryReport report = await Inspect(Repository("master"));

            Assert.True(report.EnvironmentMissing);
            Assert.True(report.ProtectionProposed);
            Assert.Equal("master", report.DefaultBranch);
            Assert.Equal(new[] { "master (branch)", "v* (tag)" }, report.ProposedPatterns.Select(p => p.ToString()));
            Assert.DoesNotContain(report.ProposedPatterns, p => p.Name == "main");
            Assert.Equal(4, report.Plan.ToCreate.Count);
        }

        [Fact]
        public async Task UnprotectedBranchAndTags_AreReported()
        {
            RepositoryReport report = await Inspect(Repository());

            Assert.Equal(RefProtection.Unprotected, report.DefaultBranchProtection);
            Assert.Equal(RefProtection.Unprotected, report.TagCreation);
        }

        [Theory]
        [InlineData("[{\"type\":\"pull_request\"}]", "{\"protected\":false}", RefProtection.Protected)]
        [InlineData("[{\"type\":\"deletion\"}]", "{\"protected\":true}", RefProtection.Protected)]
        [InlineData("[{\"type\":\"deletion\"}]", "{\"protected\":false}", RefProtection.Unprotected)]
        public async Task BranchProtection_FromRulesetsOrClassic(string rules, string branch, RefProtection expected)
        {
            var handler = Repository("main")
                .On("GET", RepoPath + "/rules/branches/main?per_page=100", HttpStatusCode.OK, rules)
                .On("GET", RepoPath + "/branches/main", HttpStatusCode.OK, branch);

            Assert.Equal(expected, (await Inspect(handler)).DefaultBranchProtection);
        }

        [Fact]
        public async Task BranchProtection_ClassicForbiddenWithoutRules_IsUnknown()
        {
            var handler = Repository("main").On("GET", RepoPath + "/branches/main", HttpStatusCode.Forbidden, "{}");

            Assert.Equal(RefProtection.Unknown, (await Inspect(handler)).DefaultBranchProtection);
        }

        [Theory]
        [InlineData("active", "[\"refs/tags/v*\"]", "[]", "creation", RefProtection.Protected)]
        [InlineData("active", "[\"~ALL\"]", "[]", "creation", RefProtection.Protected)]
        [InlineData("active", "[\"refs/tags/v*\"]", "[]", "deletion", RefProtection.Unprotected)]
        [InlineData("disabled", "[\"refs/tags/v*\"]", "[]", "creation", RefProtection.Unprotected)]
        [InlineData("active", "[\"refs/tags/release-*\"]", "[]", "creation", RefProtection.Unprotected)]
        [InlineData("active", "[\"~ALL\"]", "[\"refs/tags/v*\"]", "creation", RefProtection.Unprotected)]
        public async Task TagCreation_FromTagRulesets(string enforcement, string include, string exclude, string rule, RefProtection expected)
        {
            var handler = Repository()
                .On("GET", RepoPath + "/rulesets?targets=tag&includes_parents=true&per_page=100", HttpStatusCode.OK,
                    "[{\"id\":7,\"name\":\"tags\",\"target\":\"tag\",\"enforcement\":\"" + enforcement + "\"}]")
                .On("GET", RepoPath + "/rulesets/7", HttpStatusCode.OK,
                    "{\"id\":7,\"name\":\"tags\",\"target\":\"tag\",\"enforcement\":\"" + enforcement + "\"," +
                    "\"conditions\":{\"ref_name\":{\"include\":" + include + ",\"exclude\":" + exclude + "}}," +
                    "\"rules\":[{\"type\":\"" + rule + "\"}]}");

            Assert.Equal(expected, (await Inspect(handler)).TagCreation);
        }

        [Fact]
        public async Task TagRulesetsUnreadable_IsUnknown()
        {
            var handler = Repository().On("GET", RepoPath + "/rulesets?targets=tag&includes_parents=true&per_page=100",
                HttpStatusCode.Forbidden, "{}");

            Assert.Equal(RefProtection.Unknown, (await Inspect(handler)).TagCreation);
        }

        [Fact]
        public async Task AllBranchesEnvironment_ProposesPatternsAndPlansSecrets()
        {
            RepositoryReport report = await Inspect(WithEnvironment(Repository("main"), "null"));

            Assert.False(report.EnvironmentMissing);
            Assert.Equal(BranchPolicyKind.AllBranches, report.Policy);
            Assert.Equal(new[] { "main (branch)", "v* (tag)" }, report.ProposedPatterns.Select(p => p.ToString()));
            Assert.Equal(new[] { "ANDROID_KEY_ALIAS" }, report.Plan.ToOverwrite);
        }

        [Fact]
        public async Task CustomPolicies_DoNotProposeExistingPatterns()
        {
            var handler = WithEnvironment(Repository("main"), "{\"protected_branches\":false,\"custom_branch_policies\":true}",
                "{\"id\":1,\"name\":\"main\",\"type\":\"branch\"}", "{\"id\":2,\"name\":\"v*\",\"type\":\"tag\"}");

            RepositoryReport report = await Inspect(handler);

            Assert.Empty(report.ProposedPatterns);
            Assert.False(report.ProtectionProposed);
            Assert.Equal(new[] { "main (branch)", "v* (tag)" }, report.ExistingPatterns.Select(p => p.ToString()));
            Assert.Equal(RefProtection.Unprotected, report.TagCreation);
        }

        [Fact]
        public async Task CustomPoliciesUnreadable_AreUnknownAndProposeNothing()
        {
            var handler = WithEnvironment(Repository("main"), "{\"protected_branches\":false,\"custom_branch_policies\":true}")
                .On("GET", EnvPath + "/deployment-branch-policies?per_page=100&page=1", HttpStatusCode.Forbidden, "{}");

            RepositoryReport report = await Inspect(handler);

            Assert.Equal(BranchPolicyKind.Unknown, report.Policy);
            Assert.Empty(report.ProposedPatterns);
        }

        [Fact]
        public async Task ProtectedBranchesOnly_ProposesNothing()
        {
            RepositoryReport report = await Inspect(WithEnvironment(Repository(),
                "{\"protected_branches\":true,\"custom_branch_policies\":false}"));

            Assert.Equal(BranchPolicyKind.ProtectedBranches, report.Policy);
            Assert.Empty(report.ProposedPatterns);
            Assert.Null(report.TagCreation);
        }

        [Fact]
        public async Task RepositoryCopies_AreReported()
        {
            var handler = Repository().SecretStore(RepoPath + "/actions/secrets", new[] { "ANDROID_KEY_PASSWORD" }, _key.PublicKey, "repo");

            RepositoryReport report = await Inspect(handler);

            Assert.Equal(new[] { "ANDROID_KEY_PASSWORD" }, report.Copies.Names);
        }

        [Fact]
        public async Task RepositoryTarget_HasNoEnvironmentData()
        {
            RepositoryReport report = await Inspect(Repository(), SecretScope.ForRepository(Repo));

            Assert.Null(report.Environment);
            Assert.Null(report.Copies);
            Assert.False(report.ProtectionProposed);
            Assert.Equal(4, report.Plan.ToCreate.Count);
        }

        [Theory]
        [InlineData("refs/tags/v*", "refs/tags/v1.0.0", true)]
        [InlineData("refs/tags/*", "refs/tags/v1.0.0", true)]
        [InlineData("refs/*", "refs/tags/v1.0.0", false)]
        [InlineData("refs/**", "refs/tags/v1.0.0", true)]
        [InlineData("~ALL", "refs/tags/v1.0.0", true)]
        [InlineData("~DEFAULT_BRANCH", "refs/tags/v1.0.0", false)]
        public void RefPatternMatches(string pattern, string refName, bool expected)
        {
            Assert.Equal(expected, RepositoryInspector.RefPatternMatches(pattern, refName));
        }
    }
}

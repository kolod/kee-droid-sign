using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using KeeDroidSign.Core.GitHub;
using KeeDroidSign.Core.Tests.Support;
using KeeDroidSign.Services;
using KeeDroidSign.Storage;
using Sodium;
using Xunit;

namespace KeeDroidSign.Tests.Services
{
    /// <summary>Review texts of the export wizard: target, findings, actions and manual steps.</summary>
    public class ExportWizardTextTests
    {
        private static readonly RepositoryTarget Repo = RepositoryTarget.Parse("octo/app");
        private static readonly ExportTarget Release = new ExportTarget(Repo, "release", false);
        private static readonly ExportTarget RepositoryLevel = new ExportTarget(Repo, null, false);
        private const string RepoPath = "/repos/octo/app";

        private readonly KeyPair _key = PublicKeyBox.GenerateKeyPair();

        /// <summary>A real report from the inspector against a simulated repository.</summary>
        private Task<RepositoryReport> Report(SecretScope scope, string branch = "master", string environmentJson = null,
            string[] repositoryCopies = null, bool branchProtected = false)
        {
            var handler = new FakeGitHubHandler()
                .On("GET", RepoPath, HttpStatusCode.OK, "{\"default_branch\":\"" + branch + "\"}")
                .On("GET", RepoPath + "/rules/branches/" + branch + "?per_page=100", HttpStatusCode.OK,
                    branchProtected ? "[{\"type\":\"pull_request\"}]" : "[]")
                .On("GET", RepoPath + "/branches/" + branch, HttpStatusCode.OK, "{\"protected\":false}")
                .On("GET", RepoPath + "/rulesets?targets=tag&includes_parents=true&per_page=100", HttpStatusCode.OK, "[]")
                .SecretStore(RepoPath + "/actions/secrets", repositoryCopies ?? new string[0], _key.PublicKey, "repo");
            if (environmentJson != null)
                handler.SecretStore(RepoPath + "/environments/release/secrets", new string[0], _key.PublicKey, "env")
                    .On("GET", RepoPath + "/environments/release", HttpStatusCode.OK, environmentJson);

            return new RepositoryInspector(new GitHubClient(new GitHubCredential("ghp_wizardTextTest_123"), handler))
                .InspectAsync(scope, new SecretMapping(), CancellationToken.None);
        }

        [Fact]
        public void Target_NamesRepositoryAndEnvironment()
        {
            Assert.Equal("octo/app, environment release", ExportWizardText.Target(Release));
            Assert.Equal("octo/app, repository secrets", ExportWizardText.Target(RepositoryLevel));
        }

        [Theory]
        [InlineData("release", "Use the default from the settings (environment release)")]
        [InlineData("", "Use the default from the settings (repository secrets)")]
        public void DefaultChoice(string defaultEnvironment, string expected)
        {
            Assert.Equal(expected, ExportWizardText.DefaultChoice(defaultEnvironment));
        }

        [Fact]
        public async Task MissingEnvironment_IsBlockingAndManualStepsUseRealDefaultBranch()
        {
            RepositoryReport report = await Report(new SecretScope(Repo, "release"), branch: "master");

            var findings = ExportWizardText.Findings(Release, report);
            Assert.Contains(findings, f => f.Level == FindingLevel.Blocking && f.Text.Contains("release does not exist"));
            Assert.Contains(findings, f => f.Level == FindingLevel.Warning && f.Text.Contains("master is not protected"));
            Assert.Contains(findings, f => f.Level == FindingLevel.Warning && f.Text.Contains("create a v* tag"));

            Assert.True(ExportWizardText.NeedsManualSteps(Release, report));
            string steps = ExportWizardText.ManualSteps(Release, report);
            Assert.Contains("\"master\" (branch)", steps);
            Assert.DoesNotContain("\"main\"", steps);
            Assert.StartsWith("Create environment release", ExportWizardText.ProtectAction(Release, report));
            Assert.Contains("only after environment release exists", ExportWizardText.ExportAction(Release, report));
        }

        [Fact]
        public async Task AllBranchesEnvironment_WarnsAndProposesRestriction()
        {
            RepositoryReport report = await Report(new SecretScope(Repo, "release"), branch: "main",
                environmentJson: "{\"name\":\"release\",\"deployment_branch_policy\":null}", branchProtected: true);

            var findings = ExportWizardText.Findings(Release, report);
            Assert.Contains(findings, f => f.Level == FindingLevel.Warning && f.Text.Contains("allows every branch"));
            Assert.Contains(findings, f => f.Level == FindingLevel.Ok && f.Text.Contains("main is protected"));
            Assert.StartsWith("Restrict environment release", ExportWizardText.ProtectAction(Release, report));
            Assert.Equal("Export the 4 signing secrets", ExportWizardText.ExportAction(Release, report));
        }

        [Fact]
        public async Task RepositoryCopies_AreListedAsWarning()
        {
            RepositoryReport report = await Report(new SecretScope(Repo, "release"),
                environmentJson: "{\"name\":\"release\",\"deployment_branch_policy\":{\"protected_branches\":true,\"custom_branch_policies\":false}}",
                repositoryCopies: new[] { "ANDROID_KEY_ALIAS" });

            var findings = ExportWizardText.Findings(Release, report);
            Assert.Contains(findings, f => f.Level == FindingLevel.Warning && f.Text.Contains("ANDROID_KEY_ALIAS")
                && f.Text.Contains("every workflow"));
            Assert.Contains(findings, f => f.Level == FindingLevel.Ok && f.Text.Contains("only by protected branches"));
            Assert.False(ExportWizardText.NeedsManualSteps(Release, report));
        }

        [Fact]
        public async Task RepositoryTarget_WarnsThatEveryWorkflowCanRead()
        {
            RepositoryReport report = await Report(SecretScope.ForRepository(Repo));

            var findings = ExportWizardText.Findings(RepositoryLevel, report);
            Assert.Contains(findings, f => f.Level == FindingLevel.Warning && f.Text.Contains("every workflow on every branch of octo/app"));
            Assert.Contains(findings, f => f.Text.Contains("New secrets: ANDROID_KEYSTORE_BASE64"));
            Assert.False(ExportWizardText.NeedsManualSteps(RepositoryLevel, report));
        }

        [Fact]
        public async Task Protection_NeedsAdministration_IncludesManualSteps()
        {
            RepositoryReport report = await Report(new SecretScope(Repo, "release"), branch: "develop");
            var result = new ProtectionResult(ProtectionStatus.NeedsAdministration, false, null, "Needs Administration.");

            string text = ExportWizardText.DescribeProtection(result, Release, report);

            Assert.Contains("Needs Administration.", text);
            Assert.Contains("\"develop\" (branch)", text);
        }

        [Fact]
        public void FindingSymbols_AreDistinct()
        {
            var symbols = new[] { FindingLevel.Ok, FindingLevel.Info, FindingLevel.Warning, FindingLevel.Blocking, FindingLevel.Unknown }
                .Select(WizardFinding.Symbol).ToList();
            Assert.Equal(symbols.Count, symbols.Distinct().Count());
        }
    }
}

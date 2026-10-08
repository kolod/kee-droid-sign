using System;
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
    /// <summary>Export into a deployment environment and cleanup of repository-level copies.</summary>
    public class SecretExporterEnvironmentTests
    {
        private static readonly RepositoryTarget Repo = RepositoryTarget.Parse("octo/app");
        private static readonly SecretScope Release = new SecretScope(Repo, "release");
        private const string RepoSecrets = "/repos/octo/app/actions/secrets";
        private const string EnvPath = "/repos/octo/app/environments/release";
        private const string EnvSecrets = EnvPath + "/secrets";

        private static readonly string[] DefaultNames =
            { "ANDROID_KEYSTORE_BASE64", "ANDROID_KEYSTORE_PASSWORD", "ANDROID_KEY_ALIAS", "ANDROID_KEY_PASSWORD" };

        private readonly KeyPair _envKey = PublicKeyBox.GenerateKeyPair();
        private readonly KeyPair _repoKey = PublicKeyBox.GenerateKeyPair();

        private static SigningKeystore Keystore() =>
            new SigningKeystore(Enumerable.Range(0, 500).Select(i => (byte)i).ToArray(), "upload",
                "store-secret-123", "key-secret-456", new byte[] { 1 }, DateTime.UtcNow, DateTime.UtcNow.AddYears(30));

        private FakeGitHubHandler Environment(string policyJson = "{\"protected_branches\":false,\"custom_branch_policies\":true}",
            params string[] existing)
        {
            return new FakeGitHubHandler()
                .SecretStore(EnvSecrets, existing, _envKey.PublicKey, "env-key")
                .SecretStore(RepoSecrets, DefaultNames, _repoKey.PublicKey, "repo-key")
                .On("GET", EnvPath, HttpStatusCode.OK,
                    "{\"name\":\"release\",\"deployment_branch_policy\":" + policyJson + ",\"protection_rules\":[]}");
        }

        private static SecretExporter Exporter(FakeGitHubHandler handler) =>
            new SecretExporter(new GitHubClient(new GitHubCredential(GitHubClientAuthTests.Token), handler));

        [Fact]
        public async Task Export_Environment_WritesOnlyEnvironmentSecretsWithEnvironmentKey()
        {
            var handler = Environment();
            SigningKeystore keystore = Keystore();

            ExportResult result = await Exporter(handler).ExportAsync(Release, keystore, new SecretMapping(), false, CancellationToken.None);

            Assert.True(result.Succeeded, result.ToString());
            Assert.Empty(handler.RequestsTo("PUT", RepoSecrets));
            var puts = handler.RequestsTo("PUT", EnvSecrets);
            Assert.Equal(DefaultNames.OrderBy(n => n), puts.Select(p => p.Uri.AbsolutePath.Split('/').Last()).OrderBy(n => n));

            var values = puts.ToDictionary(p => p.Uri.AbsolutePath.Split('/').Last(), p =>
            {
                var dto = Json.Deserialize<PutSecretDto>(p.Body);
                Assert.Equal("env-key", dto.KeyId);
                return Encoding.UTF8.GetString(SealedPublicKeyBox.Open(
                    Convert.FromBase64String(dto.EncryptedValue), _envKey.PrivateKey, _envKey.PublicKey));
            });
            Assert.Equal(keystore.ToBase64(), values["ANDROID_KEYSTORE_BASE64"]);
            Assert.Equal("key-secret-456", values["ANDROID_KEY_PASSWORD"]);
        }

        [Fact]
        public async Task Export_MissingEnvironment_ThrowsAndWritesNothing()
        {
            var handler = new FakeGitHubHandler().SecretStore(RepoSecrets, new string[0], _repoKey.PublicKey, "repo-key");

            await Assert.ThrowsAsync<EnvironmentNotFoundException>(() =>
                Exporter(handler).ExportAsync(Release, Keystore(), new SecretMapping(), true, CancellationToken.None));

            Assert.DoesNotContain(handler.Requests, r => r.Method == "PUT");
        }

        [Fact]
        public async Task Export_EnvironmentForbidden_ThrowsAndWritesNothing()
        {
            var handler = new FakeGitHubHandler().On("GET", EnvSecrets + "/public-key", HttpStatusCode.Forbidden, "{}");

            var ex = await Assert.ThrowsAsync<GitHubApiException>(() =>
                Exporter(handler).ExportAsync(Release, Keystore(), new SecretMapping(), true, CancellationToken.None));

            Assert.Contains("Environments permission", ex.Message);
            Assert.DoesNotContain(handler.Requests, r => r.Method == "PUT");
        }

        [Theory]
        [InlineData("null", BranchPolicyKind.AllBranches)]
        [InlineData("{\"protected_branches\":true,\"custom_branch_policies\":false}", BranchPolicyKind.ProtectedBranches)]
        public async Task Plan_Environment_ReportsPolicyAndConflicts(string policyJson, BranchPolicyKind expected)
        {
            var handler = Environment(policyJson, "ANDROID_KEY_ALIAS");

            ExportPlan plan = await Exporter(handler).PlanAsync(Release, new SecretMapping(), CancellationToken.None);

            Assert.Same(Release, plan.Scope);
            Assert.Equal(EnvironmentExistence.Found, plan.Environment.Existence);
            Assert.Equal(expected, plan.Environment.Policy);
            Assert.Equal(new[] { "ANDROID_KEY_ALIAS" }, plan.ToOverwrite);
            Assert.Equal(3, plan.ToCreate.Count);
        }

        [Fact]
        public async Task Plan_PolicyUnreadable_IsUnknownButNotAnError()
        {
            var handler = Environment().On("GET", EnvPath, HttpStatusCode.Forbidden, "{}");

            ExportPlan plan = await Exporter(handler).PlanAsync(Release, new SecretMapping(), CancellationToken.None);

            Assert.Equal(BranchPolicyKind.Unknown, plan.Environment.Policy);
        }

        [Fact]
        public async Task Plan_Repository_HasNoEnvironmentStatus()
        {
            var handler = new FakeGitHubHandler().SecretStore(RepoSecrets, new string[0], _repoKey.PublicKey, "repo-key");

            ExportPlan plan = await Exporter(handler).PlanAsync(Repo, new SecretMapping(), CancellationToken.None);

            Assert.Null(plan.Environment);
            Assert.False(plan.Scope.IsEnvironment);
        }

        [Fact]
        public async Task FindRepositoryCopies_ReturnsConfiguredNamesPresentAtRepositoryLevel()
        {
            var handler = new FakeGitHubHandler().SecretStore(RepoSecrets,
                new[] { "ANDROID_KEY_ALIAS", "ANDROID_KEY_PASSWORD", "UNRELATED" }, _repoKey.PublicKey, "repo-key");

            RepositoryCopies copies = await Exporter(handler).FindRepositoryCopiesAsync(Repo, new SecretMapping(), CancellationToken.None);

            Assert.True(copies.Checked);
            Assert.Equal(new[] { "ANDROID_KEY_ALIAS", "ANDROID_KEY_PASSWORD" }, copies.Names.OrderBy(n => n));
        }

        [Fact]
        public async Task FindRepositoryCopies_ListingForbidden_IsUnchecked()
        {
            var handler = new FakeGitHubHandler().On("GET", RepoSecrets + "?per_page=100&page=1", HttpStatusCode.Forbidden, "{}");

            RepositoryCopies copies = await Exporter(handler).FindRepositoryCopiesAsync(Repo, new SecretMapping(), CancellationToken.None);

            Assert.False(copies.Checked);
            Assert.Empty(copies.Names);
        }

        [Fact]
        public async Task DeleteRepositorySecrets_ReportsEachName()
        {
            var handler = new FakeGitHubHandler()
                .On("DELETE", RepoSecrets + "/ANDROID_KEY_ALIAS", HttpStatusCode.NoContent)
                .On("DELETE", RepoSecrets + "/ANDROID_KEY_PASSWORD", HttpStatusCode.NotFound)
                .On("DELETE", RepoSecrets + "/ANDROID_KEYSTORE_PASSWORD", HttpStatusCode.Forbidden);

            ExportResult result = await Exporter(handler).DeleteRepositorySecretsAsync(Repo,
                new[] { "ANDROID_KEY_ALIAS", "ANDROID_KEY_PASSWORD", "ANDROID_KEYSTORE_PASSWORD" }, CancellationToken.None);

            var byName = result.Outcomes.ToDictionary(o => o.Name, o => o.Status);
            Assert.Equal(SecretOutcomeStatus.Deleted, byName["ANDROID_KEY_ALIAS"]);
            Assert.Equal(SecretOutcomeStatus.Deleted, byName["ANDROID_KEY_PASSWORD"]);
            Assert.Equal(SecretOutcomeStatus.Failed, byName["ANDROID_KEYSTORE_PASSWORD"]);
            Assert.False(result.Succeeded);
        }

        [Fact]
        public async Task Export_Environment_NeverDeletesAnything()
        {
            var handler = Environment(putStatusFailing: true);

            ExportResult result = await Exporter(handler).ExportAsync(Release, Keystore(), new SecretMapping(), true, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.DoesNotContain(handler.Requests, r => r.Method == "DELETE");
        }

        private FakeGitHubHandler Environment(bool putStatusFailing)
        {
            return new FakeGitHubHandler()
                .SecretStore(EnvSecrets, new string[0], _envKey.PublicKey, "env-key",
                    name => putStatusFailing && name == "ANDROID_KEY_ALIAS" ? HttpStatusCode.Forbidden : HttpStatusCode.Created)
                .SecretStore(RepoSecrets, DefaultNames, _repoKey.PublicKey, "repo-key")
                .On("GET", EnvPath, HttpStatusCode.OK, "{\"name\":\"release\",\"deployment_branch_policy\":null}");
        }
    }
}

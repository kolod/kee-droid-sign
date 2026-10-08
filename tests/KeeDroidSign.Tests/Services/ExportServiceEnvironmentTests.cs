using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using KeeDroidSign.Core.GitHub;
using KeeDroidSign.Core.Tests.Support;
using KeeDroidSign.Services;
using KeeDroidSign.Settings;
using KeeDroidSign.Storage;
using KeeDroidSign.Tests.Support;
using KeePassLib;
using KeePassLib.Security;
using Sodium;
using Xunit;

namespace KeeDroidSign.Tests.Services
{
    /// <summary>Environment target resolution, cleanup of repository copies and protection through the service.</summary>
    public class ExportServiceEnvironmentTests
    {
        private const string Token = "github_pat_PLUGINTEST_ENV_1234567890";
        private const string RepoSecrets = "/repos/octo/app/actions/secrets";
        private const string EnvPath = "/repos/octo/app/environments/";

        private static readonly string[] DefaultNames =
            { "ANDROID_KEYSTORE_BASE64", "ANDROID_KEYSTORE_PASSWORD", "ANDROID_KEY_ALIAS", "ANDROID_KEY_PASSWORD" };

        private readonly KeyPair _key = PublicKeyBox.GenerateKeyPair();

        private static (PwDatabase Database, PluginSettings Settings, Func<KeyContext> Key) Setup(string defaultEnvironment,
            string appTarget = null)
        {
            PwDatabase pd = TestDatabase.Create();
            PwGroup group = TestDatabase.AddApp(pd, "DroidSign", "com.example.app", TestDatabase.Keystore());
            if (appTarget != null)
                group.Entries.First(DroidSignStore.IsKeystoreEntry).Strings.Set(EntryFields.ExportTarget,
                    new ProtectedString(false, appTarget));

            var tokenEntry = new PwEntry(true, true);
            tokenEntry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, Token));
            pd.RootGroup.AddEntry(tokenEntry, true);

            PluginSettings settings = KeyServiceCreateTests.FastSettings();
            settings.TokenEntryUuid = tokenEntry.Uuid.ToHexString();
            settings.DefaultEnvironment = defaultEnvironment;

            return (pd, settings, () => new DroidSignStore(pd, settings).ResolveKey(group.Entries.First(DroidSignStore.IsKeyEntry)));
        }

        private FakeGitHubHandler GitHub(string environment, params string[] repositoryCopies)
        {
            var handler = new FakeGitHubHandler().SecretStore(RepoSecrets, repositoryCopies, _key.PublicKey, "repo");
            if (environment != null)
                handler.SecretStore(EnvPath + environment + "/secrets", new string[0], _key.PublicKey, "env")
                    .On("GET", EnvPath + environment, HttpStatusCode.OK,
                        "{\"name\":\"" + environment + "\",\"deployment_branch_policy\":{\"protected_branches\":false,\"custom_branch_policies\":true}}");
                handler.On("GET", EnvPath + environment + "/deployment-branch-policies?per_page=100&page=1", HttpStatusCode.OK,
                    "{\"total_count\":0,\"branch_policies\":[]}");
            return handler.OnPrefix("DELETE", RepoSecrets + "/", _ => FakeGitHubHandler.Response(HttpStatusCode.NoContent));
        }

        private static async Task<ExportResult> Export(ExportService service, KeyContext key) =>
            await service.ExportAsync(key, new GitHubCredential(Token), true, CancellationToken.None);

        [Fact]
        public async Task DefaultEnvironment_ExportsIntoEnvironmentOnly()
        {
            var s = Setup("release");
            var handler = GitHub("release");
            var service = new ExportService(() => s.Settings, handler);

            ExportResult result = await Export(service, s.Key());

            Assert.True(result.Succeeded, result.ToString());
            Assert.Equal(4, handler.RequestsTo("PUT", EnvPath + "release/secrets/").Count);
            Assert.Empty(handler.RequestsTo("PUT", RepoSecrets));
        }

        [Fact]
        public async Task AppOverrideRepository_ExportsAtRepositoryLevel()
        {
            var s = Setup("release", "repository");
            var handler = GitHub(null);
            var service = new ExportService(() => s.Settings, handler);

            ExportResult result = await Export(service, s.Key());

            Assert.True(result.Succeeded, result.ToString());
            Assert.Equal(4, handler.RequestsTo("PUT", RepoSecrets + "/").Count);
            Assert.Empty(handler.RequestsTo("PUT", EnvPath));
        }

        [Fact]
        public async Task AppOverrideEnvironment_WinsOverDefault()
        {
            var s = Setup("release", "environment:staging");
            var handler = GitHub("staging");
            var service = new ExportService(() => s.Settings, handler);

            Assert.Equal("staging", service.ResolveTarget(s.Key()).Environment);
            ExportResult result = await Export(service, s.Key());

            Assert.True(result.Succeeded, result.ToString());
            Assert.Equal(4, handler.RequestsTo("PUT", EnvPath + "staging/secrets/").Count);
        }

        [Fact]
        public async Task MissingEnvironment_ThrowsEnvironmentNotFound()
        {
            var s = Setup("release");
            var service = new ExportService(() => s.Settings, GitHub(null));

            await Assert.ThrowsAsync<EnvironmentNotFoundException>(() =>
                service.PlanAsync(s.Key(), new GitHubCredential(Token), CancellationToken.None));
        }

        [Fact]
        public async Task RepositoryCopies_FoundAndDeleted()
        {
            var s = Setup("release");
            var handler = GitHub("release", "ANDROID_KEY_ALIAS", "OTHER");
            var service = new ExportService(() => s.Settings, handler);
            var token = new GitHubCredential(Token);

            RepositoryCopies copies = await service.FindRepositoryCopiesAsync(s.Key(), token, CancellationToken.None);
            ExportResult deleted = await service.DeleteRepositoryCopiesAsync(s.Key(), token, copies.Names, CancellationToken.None);

            Assert.Equal(new[] { "ANDROID_KEY_ALIAS" }, copies.Names);
            Assert.True(deleted.Succeeded);
            Assert.Equal(RepoSecrets + "/ANDROID_KEY_ALIAS", handler.RequestsTo("DELETE", RepoSecrets).Single().Uri.AbsolutePath);
        }

        [Fact]
        public async Task RepositoryCopies_RepositoryTarget_AlwaysEmpty()
        {
            var s = Setup(string.Empty);
            var handler = GitHub(null, DefaultNames);
            var service = new ExportService(() => s.Settings, handler);

            RepositoryCopies copies = await service.FindRepositoryCopiesAsync(s.Key(), new GitHubCredential(Token), CancellationToken.None);

            Assert.Empty(copies.Names);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task Inspect_UsesResolvedTarget()
        {
            var s = Setup("release", "environment:staging");
            var handler = GitHub("staging")
                .On("GET", "/repos/octo/app", HttpStatusCode.OK, "{\"default_branch\":\"trunk\"}");
            var service = new ExportService(() => s.Settings, handler);

            RepositoryReport report = await service.InspectAsync(s.Key(), new GitHubCredential(Token), CancellationToken.None);

            Assert.Equal("staging", report.Scope.Environment);
            Assert.Equal("trunk", report.DefaultBranch);
            Assert.Contains(report.ProposedPatterns, p => p.Name == "trunk" && p.Type == DeploymentPattern.Branch);
        }

        [Fact]
        public async Task RestrictReleaseTags_CreatesRulesetInAppRepository()
        {
            var s = Setup("release");
            var handler = new FakeGitHubHandler().On("POST", "/repos/octo/app/rulesets", HttpStatusCode.Created, "{}");
            var service = new ExportService(() => s.Settings, handler);

            ProtectionResult result = await service.RestrictReleaseTagsAsync(s.Key(), new GitHubCredential(Token), CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Single(handler.RequestsTo("POST", "/repos/octo/app/rulesets"));
        }

        [Fact]
        public async Task RemoveTagPattern_RepositoryTarget_Throws()
        {
            var s = Setup(string.Empty);
            var service = new ExportService(() => s.Settings, GitHub(null));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RemoveTagPatternAsync(s.Key(), new GitHubCredential(Token), CancellationToken.None));
        }

        [Fact]
        public async Task Protect_RepositoryTarget_Throws()
        {
            var s = Setup(string.Empty);
            var service = new ExportService(() => s.Settings, GitHub(null));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ProtectEnvironmentAsync(s.Key(), new GitHubCredential(Token), new DeploymentPattern[0], CancellationToken.None));
        }

        [Fact]
        public async Task Protect_EnvironmentTarget_UsesProtector()
        {
            var s = Setup("release");
            var handler = new FakeGitHubHandler()
                .On("PUT", EnvPath + "release", HttpStatusCode.OK, "{}")
                .On("POST", EnvPath + "release/deployment-branch-policies", HttpStatusCode.OK, "{}");
            var service = new ExportService(() => s.Settings, handler);

            ProtectionResult result = await service.ProtectEnvironmentAsync(s.Key(), new GitHubCredential(Token),
                new[] { new DeploymentPattern("main", DeploymentPattern.Branch) }, CancellationToken.None);

            Assert.Equal(ProtectionStatus.Protected, result.Status);
            Assert.True(result.Created);
        }
    }
}

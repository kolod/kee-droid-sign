using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KeeDroidSign.Core.Fingerprints;
using KeeDroidSign.Core.GitHub;
using KeeDroidSign.Core.Keystore;
using KeeDroidSign.Core.Passwords;
using Xunit;
using Xunit.Abstractions;

namespace KeeDroidSign.LiveTests
{
    /// <summary>
    /// End-to-end: generate a key -> export it to the repository secrets -> run the signing workflow ->
    /// check the APK is signed with exactly that key. Overwrites the repository's signing secrets
    /// with a throwaway key on every run.
    /// </summary>
    public class SigningEndToEndTests
    {
        private const string WorkflowFile = "android-sign.yml";

        private readonly ITestOutputHelper _output;

        public SigningEndToEndTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [LiveFact]
        public async Task ExportedSecrets_SignTheSampleApk()
        {
            LiveSettings settings = LiveSettings.Load();
            var passwords = new PasswordGenerator();
            var request = new KeyRequest
            {
                Alias = "e2e",
                Subject = new DistinguishedName { CommonName = "KeeDroidSign E2E", Organization = "KeeDroidSign", Country = "UA" },
                KeySize = 2048,
                StorePassword = passwords.Generate(new PasswordPolicy()),
                KeyPassword = passwords.Generate(new PasswordPolicy()),
            };

            SigningKeystore keystore = await new KeystoreGenerator().GenerateAsync(request, CancellationToken.None);
            string[] secrets = { settings.Token, keystore.StorePassword, keystore.KeyPassword, keystore.ToBase64() };

            try
            {
                await RunAsync(settings, keystore);
            }
            catch (Exception ex)
            {
                // Never surface a secret through a failure message (FR-015).
                string text = ex.ToString();
                if (secrets.Any(s => !string.IsNullOrEmpty(s) && text.Contains(s)))
                    throw new Exception("The live test failed; details were withheld because they contained secret data.");
                throw;
            }
        }

        private async Task RunAsync(LiveSettings settings, SigningKeystore keystore)
        {
            string expected = CertificateFingerprint.Compute(keystore.CertificateDer);
            string requestId = Guid.NewGuid().ToString("N");
            _output.WriteLine("Repository: " + settings.Repository + ", ref: " + settings.Ref);
            _output.WriteLine("Request ID: " + requestId);
            _output.WriteLine("Expected SHA-256: " + expected);

            using (var client = new GitHubClient(new GitHubCredential(settings.Token)))
            {
                ExportResult export = await new SecretExporter(client)
                    .ExportAsync(settings.Repository, keystore, new SecretMapping(), true, CancellationToken.None);
                Assert.True(export.Succeeded, "Secret export failed: " + export);
                _output.WriteLine("Secrets exported: " + export);
            }

            using (var actions = new GitHubActionsHelper(settings.Token))
            {
                DispatchedRun dispatched = await actions.DispatchAsync(
                    settings.Repository, WorkflowFile, settings.Ref, requestId, CancellationToken.None);
                _output.WriteLine("Workflow run: " + dispatched.HtmlUrl);

                RunDto run = await actions.WaitForCompletionAsync(
                    settings.Repository, dispatched.RunId, settings.Timeout, CancellationToken.None);
                Assert.True(run.Conclusion == "success",
                    $"Workflow run concluded '{run.Conclusion}': {dispatched.HtmlUrl}");
                Assert.Contains(requestId, run.DisplayTitle);

                SigningReportDto report = await actions.DownloadSigningReportAsync(
                    settings.Repository, dispatched.RunId, CancellationToken.None);
                _output.WriteLine("Actual SHA-256:   " + report.Sha256);

                Assert.Equal(requestId, report.RequestId);
                Assert.Equal(1, report.SignerCount);
                Assert.Equal(expected, report.Sha256);
            }
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KeeDroidSign.Core.Fingerprints;
using KeeDroidSign.Core.Keystore;
using KeeDroidSign.Core.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace KeeDroidSign.Core.Tests.Keystore
{
    /// <summary>Optional check that the JDK keytool accepts keystores produced in-process (FR-012).</summary>
    public class KeytoolInteropTests
    {
        private readonly ITestOutputHelper _output;

        public KeytoolInteropTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [KeytoolFact]
        public async Task Keytool_OpensGeneratedKeystoreWithBothPasswords()
        {
            SigningKeystore keystore = await new KeystoreGenerator()
                .GenerateAsync(KeystoreGeneratorTests.FastRequest(), CancellationToken.None);

            string dir = Path.Combine(Path.GetTempPath(), "kds-interop-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string file = Path.Combine(dir, "sample.jks");
                File.WriteAllBytes(file, keystore.Content);

                (int listExit, string listOutput) = RunKeytool(
                    "-list -v -keystore \"" + file + "\" -storepass:env KDS_SP");
                Assert.Equal(0, listExit);
                Assert.Contains("upload", listOutput);
                Assert.Contains("PrivateKeyEntry", listOutput);

                string fingerprint = CertificateFingerprint.Compute(keystore.CertificateDer);
                Assert.Contains("SHA256: " + fingerprint, listOutput);

                // -certreq must decrypt the private key, which proves the separate key password works.
                (int reqExit, string reqOutput) = RunKeytool(
                    "-certreq -alias upload -keystore \"" + file + "\" -storepass:env KDS_SP -keypass:env KDS_KP");
                Assert.True(reqExit == 0, "keytool -certreq failed: exit code " + reqExit);
                Assert.Contains("BEGIN NEW CERTIFICATE REQUEST", reqOutput);

                if (Environment.GetEnvironmentVariable("KDS_KEEP_TEST_OUTPUT") == "1")
                {
                    string outDir = Path.Combine(RepositoryRoot(), "artifacts", "test-output");
                    Directory.CreateDirectory(outDir);
                    File.Copy(file, Path.Combine(outDir, "sample.jks"), true);
                    _output.WriteLine("Kept keystore: " + Path.Combine(outDir, "sample.jks"));
                    _output.WriteLine("SHA-256 fingerprint: " + fingerprint);
                }
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private static string RepositoryRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "KeeDroidSign.sln")))
                    return dir.FullName;
            }
            return Directory.GetCurrentDirectory();
        }

        private static (int ExitCode, string Output) RunKeytool(string arguments)
        {
            var info = new ProcessStartInfo(KeytoolFactAttribute.KeytoolPath, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            // Test-only passwords, passed through the child environment rather than the command line.
            info.EnvironmentVariables["KDS_SP"] = "test-store-pass";
            info.EnvironmentVariables["KDS_KP"] = "test-key-pass";
            // Force English output so assertions do not depend on the machine locale.
            info.EnvironmentVariables["JAVA_TOOL_OPTIONS"] = "-Duser.language=en -Duser.country=US";

            using (Process process = Process.Start(info))
            {
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(60000))
                {
                    process.Kill();
                    throw new TimeoutException("keytool did not finish within 60 seconds.");
                }
                return (process.ExitCode, stdout.Result + stderr.Result);
            }
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KeeDroidSign.Core.Keystore;
using KeeDroidSign.Core.Tests.Support;
using Xunit;

namespace KeeDroidSign.Core.Tests.Keystore
{
    public class KeystoreEditorTests
    {
        private static KeyRequest Request(string alias, string keyPassword) => new KeyRequest
        {
            Alias = alias,
            Subject = new DistinguishedName { CommonName = "Editor Test", Country = "UA" },
            KeySize = 2048,
            StorePassword = "test-store-pass",
            KeyPassword = keyPassword,
        };

        private static Task<SigningKeystore> Initial() =>
            new KeystoreGenerator().GenerateAsync(Request("1", "key-one-pass"), CancellationToken.None);

        [Fact]
        public async Task AddKey_AddsNewAliasAndKeepsExistingKey()
        {
            SigningKeystore initial = await Initial();

            SigningKeystore updated = await KeystoreEditor.AddKeyAsync(initial.Content, Request("2", "key-two-pass"), CancellationToken.None);

            Assert.Equal("2", updated.Alias);
            Assert.Equal(new[] { "1", "2" }, KeystoreReader.ListKeyAliases(updated.Content, "test-store-pass"));

            SigningKeystore one = KeystoreReader.Open(updated.Content, "1", "test-store-pass", "key-one-pass");
            SigningKeystore two = KeystoreReader.Open(updated.Content, "2", "test-store-pass", "key-two-pass");
            Assert.Equal(initial.CertificateDer, one.CertificateDer);
            Assert.Equal(updated.CertificateDer, two.CertificateDer);
            Assert.NotEqual(one.CertificateDer, two.CertificateDer);
            Assert.Throws<InvalidKeyPasswordException>(() => KeystoreReader.Open(updated.Content, "1", "test-store-pass", "key-two-pass"));
        }

        [Fact]
        public async Task AddKey_ExistingAlias_IsRejected()
        {
            SigningKeystore initial = await Initial();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                KeystoreEditor.AddKeyAsync(initial.Content, Request("1", "another-pass"), CancellationToken.None));
        }

        [Fact]
        public async Task AddKey_WrongStorePassword_IsRejected()
        {
            SigningKeystore initial = await Initial();
            KeyRequest request = Request("2", "key-two-pass");
            request.StorePassword = "wrong-store-pass";

            await Assert.ThrowsAsync<InvalidKeystorePasswordException>(() =>
                KeystoreEditor.AddKeyAsync(initial.Content, request, CancellationToken.None));
        }

        [Fact]
        public async Task AddKey_Cancelled_Throws()
        {
            SigningKeystore initial = await Initial();
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    KeystoreEditor.AddKeyAsync(initial.Content, Request("2", "key-two-pass"), cts.Token));
            }
        }

        [Fact]
        public async Task ListKeyAliases_WrongPassword_Throws()
        {
            SigningKeystore initial = await Initial();

            Assert.Throws<InvalidKeystorePasswordException>(() => KeystoreReader.ListKeyAliases(initial.Content, "nope-nope"));
        }

        [KeytoolFact]
        public async Task Keytool_ListsBothPrivateKeys()
        {
            SigningKeystore initial = await Initial();
            SigningKeystore updated = await KeystoreEditor.AddKeyAsync(initial.Content, Request("2", "key-two-pass"), CancellationToken.None);
            string file = Path.Combine(Path.GetTempPath(), "kds-editor-" + Guid.NewGuid().ToString("N") + ".jks");
            File.WriteAllBytes(file, updated.Content);
            try
            {
                var info = new ProcessStartInfo(KeytoolFactAttribute.KeytoolPath, "-list -keystore \"" + file + "\" -storepass:env KDS_SP")
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                };
                info.EnvironmentVariables["KDS_SP"] = "test-store-pass";
                info.EnvironmentVariables["JAVA_TOOL_OPTIONS"] = "-Duser.language=en -Duser.country=US";
                using (Process p = Process.Start(info))
                {
                    string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit(60000);
                    Assert.Equal(0, p.ExitCode);
                    Assert.Equal(2, output.Split('\n').Count(l => l.Contains("PrivateKeyEntry")));
                }
            }
            finally
            {
                File.Delete(file);
            }
        }
    }
}

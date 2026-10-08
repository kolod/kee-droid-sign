using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KeeDroidSign.Core.Keystore;
using KeeDroidSign.Core.Tests.Support;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Xunit;

namespace KeeDroidSign.Core.Tests.Keystore
{
    public class KeystoreGeneratorTests
    {
        private readonly KeystoreGenerator _generator = new KeystoreGenerator();

        internal static KeyRequest FastRequest()
        {
            var request = KeyRequestTests.Valid();
            request.KeySize = 2048;
            return request;
        }

        [Fact]
        public async Task Generate_ProducesKeystoreThatOpensWithBothPasswords()
        {
            SigningKeystore keystore = await _generator.GenerateAsync(FastRequest(), CancellationToken.None);
            SigningKeystore reopened = KeystoreReader.Open(keystore.Content, "upload", "test-store-pass", "test-key-pass");

            Assert.Equal("upload", reopened.Alias);
            Assert.Equal(keystore.CertificateDer, reopened.CertificateDer);
            Assert.Equal("test-store-pass", keystore.StorePassword);
            Assert.Equal("test-key-pass", keystore.KeyPassword);
        }

        [Fact]
        public async Task Generate_ContainsExactlyOneKeyEntryWithRequestedSize()
        {
            SigningKeystore keystore = await _generator.GenerateAsync(FastRequest(), CancellationToken.None);

            var store = new JksStore();
            using (var stream = new MemoryStream(keystore.Content))
                store.Load(stream, "test-store-pass".ToCharArray());

            Assert.Equal(new[] { "upload" }, store.Aliases.ToArray());
            Assert.True(store.IsKeyEntry("upload"));
            var key = (RsaPrivateCrtKeyParameters)store.GetKey("upload", "test-key-pass".ToCharArray());
            Assert.Equal(2048, key.Modulus.BitLength);
            Assert.Equal(65537, key.PublicExponent.IntValue);
        }

        [Fact]
        public async Task Generate_DefaultRequest_Uses4096BitKey()
        {
            SigningKeystore keystore = await _generator.GenerateAsync(KeyRequestTests.Valid(), CancellationToken.None);

            var cert = new X509Certificate(keystore.CertificateDer);
            var publicKey = (RsaKeyParameters)cert.GetPublicKey();
            Assert.Equal(4096, publicKey.Modulus.BitLength);
        }

        [Fact]
        public async Task Generate_CertificateMatchesRequest()
        {
            var request = FastRequest();
            DateTime before = DateTime.UtcNow;

            SigningKeystore keystore = await _generator.GenerateAsync(request, CancellationToken.None);
            var cert = new X509Certificate(keystore.CertificateDer);

            Assert.Equal("Jane Doe", cert.SubjectDN.GetValueList(X509Name.CN).Single());
            Assert.Equal("Acme", cert.SubjectDN.GetValueList(X509Name.O).Single());
            Assert.Equal("UA", cert.SubjectDN.GetValueList(X509Name.C).Single());
            Assert.True(cert.SubjectDN.Equivalent(cert.IssuerDN), "Certificate must be self-signed");
            Assert.True(cert.NotAfter >= before.AddYears(30).AddDays(-1), "Validity is shorter than requested");
            Assert.True(cert.NotBefore <= before, "Certificate must already be valid");
            Assert.Equal("1.2.840.113549.1.1.11", cert.SigAlgOid); // sha256WithRSAEncryption
            Assert.True(cert.SerialNumber.SignValue > 0);
            cert.Verify(cert.GetPublicKey());
            Assert.Equal(cert.NotBefore, keystore.NotBefore);
            Assert.Equal(cert.NotAfter, keystore.NotAfter);
        }

        [Fact]
        public async Task Open_WithWrongPasswordsOrAlias_ThrowsSpecificErrors()
        {
            SigningKeystore keystore = await _generator.GenerateAsync(FastRequest(), CancellationToken.None);

            Assert.Throws<InvalidKeystorePasswordException>(
                () => KeystoreReader.Open(keystore.Content, "upload", "wrong-store-pass", "test-key-pass"));
            Assert.Throws<InvalidKeyPasswordException>(
                () => KeystoreReader.Open(keystore.Content, "upload", "test-store-pass", "wrong-key-pass"));
            Assert.Throws<AliasNotFoundException>(
                () => KeystoreReader.Open(keystore.Content, "other", "test-store-pass", "test-key-pass"));
        }

        [Fact]
        public void Open_GarbageContent_ThrowsInvalidKeystorePassword()
        {
            Assert.Throws<InvalidKeystorePasswordException>(
                () => KeystoreReader.Open(new byte[] { 1, 2, 3 }, "upload", "test-store-pass", "test-key-pass"));
        }

        [Fact]
        public async Task Generate_UpperCaseAlias_IsStoredLowerCase()
        {
            var request = FastRequest();
            request.Alias = "Upload-Key";

            SigningKeystore keystore = await _generator.GenerateAsync(request, CancellationToken.None);

            Assert.Equal("upload-key", keystore.Alias);
            KeystoreReader.Open(keystore.Content, "Upload-Key", "test-store-pass", "test-key-pass");
        }

        [Fact]
        public async Task Generate_WithoutOutputPath_WritesNoKeystoreFiles()
        {
            string[] dirs = { Path.GetTempPath(), Directory.GetCurrentDirectory() };
            var before = new HashSet<string>(dirs.SelectMany(Directory.EnumerateFiles));

            await _generator.GenerateAsync(FastRequest(), CancellationToken.None);

            var created = dirs.SelectMany(Directory.EnumerateFiles).Where(f => !before.Contains(f)).ToList();
            Assert.DoesNotContain(created, IsJksFile);
        }

        [Fact]
        public async Task Generate_WithOutputPath_WritesFileAndRefusesOverwrite()
        {
            string dir = Path.Combine(Path.GetTempPath(), "kds-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "release.jks");

                SigningKeystore keystore = await _generator.GenerateAsync(FastRequest(), path, CancellationToken.None);

                Assert.Equal(keystore.Content, File.ReadAllBytes(path));
                await Assert.ThrowsAsync<KeystoreExistsException>(
                    () => _generator.GenerateAsync(FastRequest(), path, CancellationToken.None));
                Assert.Equal(keystore.Content, File.ReadAllBytes(path));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public async Task Generate_ExistingOutputPath_FailsBeforeGenerating()
        {
            string path = Path.GetTempFileName();
            try
            {
                await Assert.ThrowsAsync<KeystoreExistsException>(
                    () => _generator.GenerateAsync(FastRequest(), path, CancellationToken.None));
                Assert.Equal(0, new FileInfo(path).Length);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task Generate_CancelledToken_Throws()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => _generator.GenerateAsync(FastRequest(), cts.Token));
            }
        }

        [Fact]
        public async Task Generate_InvalidRequest_ThrowsArgumentException()
        {
            var request = FastRequest();
            request.StorePassword = "123";

            await Assert.ThrowsAsync<ArgumentException>(() => _generator.GenerateAsync(request, CancellationToken.None));
        }

        [Fact]
        public async Task ToBase64_IsSingleLineAndRoundTrips()
        {
            SigningKeystore keystore = await _generator.GenerateAsync(FastRequest(), CancellationToken.None);

            string base64 = keystore.ToBase64();

            Assert.DoesNotContain("\n", base64);
            Assert.DoesNotContain("\r", base64);
            Assert.Equal(keystore.Content, Convert.FromBase64String(base64));
        }

        [Fact]
        public async Task ToString_DoesNotLeakSecrets()
        {
            SigningKeystore keystore = await _generator.GenerateAsync(FastRequest(), CancellationToken.None);

            SecretLeakScanner.AssertNoLeak(
                new[] { "test-store-pass", "test-key-pass", keystore.ToBase64() },
                keystore.ToString());
        }

        [Fact]
        public async Task GetCertificate_ReturnsCertificateWithoutKeyPassword()
        {
            SigningKeystore keystore = await _generator.GenerateAsync(FastRequest(), CancellationToken.None);

            byte[] der = KeystoreReader.GetCertificate(keystore.Content, "upload", "test-store-pass");

            Assert.Equal(keystore.CertificateDer, der);
            Assert.Throws<AliasNotFoundException>(() => KeystoreReader.GetCertificate(keystore.Content, "x", "test-store-pass"));
            Assert.Throws<InvalidKeystorePasswordException>(() => KeystoreReader.GetCertificate(keystore.Content, "upload", "nope-nope"));
        }

        private static bool IsJksFile(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var magic = new byte[4];
                    return stream.Read(magic, 0, 4) == 4
                        && magic[0] == 0xFE && magic[1] == 0xED && magic[2] == 0xFE && magic[3] == 0xED;
                }
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}

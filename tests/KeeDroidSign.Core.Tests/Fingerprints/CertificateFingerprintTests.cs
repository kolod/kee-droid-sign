using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KeeDroidSign.Core.Fingerprints;
using KeeDroidSign.Core.Keystore;
using KeeDroidSign.Core.Tests.Keystore;
using Xunit;

namespace KeeDroidSign.Core.Tests.Fingerprints
{
    public class CertificateFingerprintTests
    {
        private static readonly Regex FormatPattern = new Regex("^([0-9A-F]{2}:)*[0-9A-F]{2}$");

        [Fact]
        public void Sha256_OfFixture_EqualsReference()
        {
            string fingerprint = CertificateFingerprint.Compute(FixtureCertificate());

            Assert.Equal(ReferenceValue("SHA256"), fingerprint);
            Assert.Equal(32, fingerprint.Split(':').Length);
            Assert.Matches(FormatPattern, fingerprint);
        }

        [Fact]
        public void Sha1_OfFixture_EqualsReference()
        {
            string fingerprint = CertificateFingerprint.Compute(FixtureCertificate(), FingerprintAlgorithm.Sha1);

            Assert.Equal(ReferenceValue("SHA1"), fingerprint);
            Assert.Equal(20, fingerprint.Split(':').Length);
        }

        [Fact]
        public void Format_ProducesUpperCaseColonSeparatedPairs()
        {
            Assert.Equal("0A:FF", CertificateFingerprint.Format(new byte[] { 0x0A, 0xFF }));
            Assert.Equal("00", CertificateFingerprint.Format(new byte[] { 0x00 }));
            Assert.Equal(string.Empty, CertificateFingerprint.Format(new byte[0]));
        }

        [Fact]
        public void Compute_NullCertificate_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => CertificateFingerprint.Compute(null));
        }

        [Fact]
        public async Task EndToEnd_GeneratedKeystoreFingerprintMatchesCertificateHash()
        {
            SigningKeystore keystore = await new KeystoreGenerator()
                .GenerateAsync(KeystoreGeneratorTests.FastRequest(), CancellationToken.None);

            byte[] der = KeystoreReader.GetCertificate(keystore.Content, "upload", "test-store-pass");
            string fingerprint = CertificateFingerprint.Compute(der);

            byte[] expected;
            using (var sha = SHA256.Create())
                expected = sha.ComputeHash(keystore.CertificateDer);
            Assert.Equal(string.Join(":", expected.Select(b => b.ToString("X2"))), fingerprint);

            Assert.Throws<InvalidKeystorePasswordException>(
                () => KeystoreReader.GetCertificate(keystore.Content, "upload", "wrong-pass"));
            Assert.Throws<AliasNotFoundException>(
                () => KeystoreReader.GetCertificate(keystore.Content, "missing", "test-store-pass"));
        }

        private static byte[] FixtureCertificate() =>
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "test-cert.der"));

        private static string ReferenceValue(string algorithm) =>
            File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "test-cert.fingerprints.txt"))
                .Select(line => line.Split(new[] { '=' }, 2))
                .Single(parts => parts[0] == algorithm)[1]
                .Trim();
    }
}

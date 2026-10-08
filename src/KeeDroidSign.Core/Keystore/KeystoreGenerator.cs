using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace KeeDroidSign.Core.Keystore
{
    /// <summary>
    /// Generates an RSA key, a self-signed SHA-256 certificate and a JKS keystore entirely in
    /// memory (FR-010, FR-011). No Java or other external tool is required.
    /// </summary>
    public sealed class KeystoreGenerator : IKeystoreGenerator
    {
        private const int RsaCertainty = 100;

        /// <summary>Generates the keystore in memory; nothing is written to disk.</summary>
        public Task<SigningKeystore> GenerateAsync(KeyRequest request, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            request.Validate();
            ct.ThrowIfCancellationRequested();

            return Task.Run(() => Generate(request, ct), ct);
        }

        /// <summary>
        /// Generates the keystore and also writes it to <paramref name="outputPath"/>. An existing
        /// file is never overwritten (<see cref="KeystoreExistsException"/>).
        /// </summary>
        public async Task<SigningKeystore> GenerateAsync(KeyRequest request, string outputPath, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(outputPath)) throw new ArgumentException("The output path is required.", nameof(outputPath));
            if (File.Exists(outputPath))
                throw new KeystoreExistsException(outputPath);

            SigningKeystore keystore = await GenerateAsync(request, ct).ConfigureAwait(false);

            try
            {
                using (var file = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    file.Write(keystore.Content, 0, keystore.Content.Length);
            }
            catch (IOException) when (File.Exists(outputPath))
            {
                throw new KeystoreExistsException(outputPath);
            }

            return keystore;
        }

        private static SigningKeystore Generate(KeyRequest request, CancellationToken ct)
        {
            var random = new SecureRandom();

            var keyGenerator = new RsaKeyPairGenerator();
            keyGenerator.Init(new RsaKeyGenerationParameters(
                BigInteger.ValueOf(65537), random, request.KeySize, RsaCertainty));
            AsymmetricCipherKeyPair keyPair = keyGenerator.GenerateKeyPair();
            ct.ThrowIfCancellationRequested();

            DateTime notBefore = DateTime.UtcNow.Date.AddDays(-1);
            DateTime notAfter = notBefore.AddYears(request.ValidityYears).AddDays(1);
            var subject = request.Subject.ToX509Name();

            var certGenerator = new X509V3CertificateGenerator();
            certGenerator.SetSerialNumber(new BigInteger(128, random).SetBit(127));
            certGenerator.SetIssuerDN(subject);
            certGenerator.SetSubjectDN(subject);
            certGenerator.SetNotBefore(notBefore);
            certGenerator.SetNotAfter(notAfter);
            certGenerator.SetPublicKey(keyPair.Public);
            X509Certificate certificate = certGenerator.Generate(
                new Asn1SignatureFactory("SHA256WITHRSA", keyPair.Private, random));

            string alias = request.Alias.ToLowerInvariant();
            var store = new JksStore();
            store.SetKeyEntry(alias, keyPair.Private, request.KeyPassword.ToCharArray(), new[] { certificate });

            byte[] content;
            using (var stream = new MemoryStream())
            {
                store.Save(stream, request.StorePassword.ToCharArray());
                content = stream.ToArray();
            }
            ct.ThrowIfCancellationRequested();

            return new SigningKeystore(content, alias, request.StorePassword, request.KeyPassword,
                certificate.GetEncoded(), certificate.NotBefore, certificate.NotAfter);
        }
    }
}

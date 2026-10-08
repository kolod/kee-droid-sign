using System;
using System.IO;
using System.Threading;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace KeeDroidSign.Core.Keystore
{
    /// <summary>Shared key and certificate generation for new and existing keystores.</summary>
    internal static class KeyMaterial
    {
        private const int RsaCertainty = 100;

        /// <summary>Adds a new RSA key with a self-signed certificate to <paramref name="store"/> and saves it.</summary>
        public static SigningKeystore AddKey(JksStore store, KeyRequest request, CancellationToken ct)
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

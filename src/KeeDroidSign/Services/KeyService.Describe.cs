using System;
using System.Linq;
using KeeDroidSign.Core.Fingerprints;
using KeeDroidSign.Core.Keystore;
using KeeDroidSign.Storage;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.X509;

namespace KeeDroidSign.Services
{
    public sealed partial class KeyService
    {
        /// <summary>
        /// Public certificate details of a key for the entry tab. Needs only the keystore password;
        /// throws the core's keystore exceptions or <see cref="InvalidOperationException"/> when the
        /// key has layout problems.
        /// </summary>
        public static KeyDetails Describe(KeyContext key)
        {
            if (key == null) throw new ArgumentNullException("key");
            if (key.Problems.Contains(KeyProblem.MissingKeystoreEntry) ||
                key.Problems.Contains(KeyProblem.MissingKeystoreFile) ||
                key.Problems.Contains(KeyProblem.MissingKeyNumber))
                throw new InvalidOperationException("The keystore or the key number for this entry is missing.");

            byte[] der = KeystoreReader.GetCertificate(key.KeystoreContent, key.Alias, key.StorePassword);
            var certificate = new X509Certificate(der);

            return new KeyDetails
            {
                Alias = key.Alias,
                Subject = FormatSubject(certificate.SubjectDN),
                NotBefore = certificate.NotBefore,
                NotAfter = certificate.NotAfter,
                Sha256 = CertificateFingerprint.Compute(der),
            };
        }

        /// <summary>Most specific attribute first, e.g. "CN=Jane, O=Acme, C=UA" (as keytool shows it).</summary>
        private static string FormatSubject(X509Name name)
        {
            return name.ToString(true, X509Name.DefaultSymbols).Replace(",", ", ");
        }
    }
}

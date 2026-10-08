using System;
using System.Security.Cryptography;
using System.Text;

namespace KeeDroidSign.Core.Fingerprints
{
    /// <summary>Digest used for a certificate fingerprint.</summary>
    public enum FingerprintAlgorithm
    {
        Sha256,
        Sha1,
    }

    /// <summary>
    /// Certificate fingerprints in the colon-separated hex format requested by the Android
    /// Developer Console, e.g. <c>B2:71:2B:...</c> (FR-013 - FR-015).
    /// </summary>
    public static class CertificateFingerprint
    {
        /// <summary>Hashes the DER-encoded certificate and formats the digest as <c>AA:BB:...</c>.</summary>
        public static string Compute(byte[] derCertificate, FingerprintAlgorithm algorithm = FingerprintAlgorithm.Sha256)
        {
            if (derCertificate == null) throw new ArgumentNullException(nameof(derCertificate));

            using (HashAlgorithm hash = algorithm == FingerprintAlgorithm.Sha1 ? (HashAlgorithm)SHA1.Create() : SHA256.Create())
                return Format(hash.ComputeHash(derCertificate));
        }

        /// <summary>Formats bytes as upper-case two-digit hex values joined by <c>:</c>.</summary>
        public static string Format(byte[] digest)
        {
            if (digest == null) throw new ArgumentNullException(nameof(digest));

            var sb = new StringBuilder(digest.Length * 3);
            for (int i = 0; i < digest.Length; i++)
            {
                if (i > 0) sb.Append(':');
                sb.Append(digest[i].ToString("X2"));
            }
            return sb.ToString();
        }
    }
}

using System;

namespace KeeDroidSign.Core.Keystore
{
    /// <summary>
    /// A JKS keystore with its credentials: the unit stored in the KeePass database and exported to
    /// GitHub.
    /// </summary>
    public sealed class SigningKeystore
    {
        public SigningKeystore(byte[] content, string alias, string storePassword, string keyPassword,
            byte[] certificateDer, DateTime notBefore, DateTime notAfter)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
            Alias = alias ?? throw new ArgumentNullException(nameof(alias));
            StorePassword = storePassword;
            KeyPassword = keyPassword;
            CertificateDer = certificateDer ?? throw new ArgumentNullException(nameof(certificateDer));
            NotBefore = notBefore;
            NotAfter = notAfter;
        }

        /// <summary>Raw JKS bytes.</summary>
        public byte[] Content { get; }

        /// <summary>Key alias, lower-case as stored by JKS.</summary>
        public string Alias { get; }

        public string StorePassword { get; }
        public string KeyPassword { get; }

        /// <summary>DER-encoded public certificate.</summary>
        public byte[] CertificateDer { get; }

        public DateTime NotBefore { get; }
        public DateTime NotAfter { get; }

        /// <summary>Standard single-line Base64 of <see cref="Content"/>, decodable with <c>base64 -d</c>.</summary>
        public string ToBase64() => Convert.ToBase64String(Content);

        public override string ToString() =>
            $"SigningKeystore(Alias={Alias}, Size={Content.Length} bytes, NotAfter={NotAfter:yyyy-MM-dd}, " +
            $"StorePassword={Redaction.Placeholder}, KeyPassword={Redaction.Placeholder})";
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace KeeDroidSign.Core.Keystore
{
    /// <summary>Opens JKS keystores in memory and verifies their passwords.</summary>
    public static class KeystoreReader
    {
        /// <summary>
        /// Opens the keystore and checks both passwords. Throws
        /// <see cref="InvalidKeystorePasswordException"/>, <see cref="AliasNotFoundException"/> or
        /// <see cref="InvalidKeyPasswordException"/>.
        /// </summary>
        public static SigningKeystore Open(byte[] content, string alias, string storePassword, string keyPassword)
        {
            if (keyPassword == null) throw new ArgumentNullException(nameof(keyPassword));

            JksStore store = Load(content, storePassword);
            string normalized = NormalizeAlias(alias);
            if (!store.IsKeyEntry(normalized))
                throw new AliasNotFoundException(alias);

            AsymmetricKeyParameter key;
            try
            {
                key = store.GetKey(normalized, keyPassword.ToCharArray());
            }
            catch (IOException)
            {
                throw new InvalidKeyPasswordException(alias);
            }
            if (key == null)
                throw new InvalidKeyPasswordException(alias);

            X509Certificate cert = store.GetCertificate(normalized);
            return new SigningKeystore(content, normalized, storePassword, keyPassword,
                cert.GetEncoded(), cert.NotBefore, cert.NotAfter);
        }

        /// <summary>Returns the DER certificate of the alias; only the store password is needed.</summary>
        public static byte[] GetCertificate(byte[] content, string alias, string storePassword)
        {
            JksStore store = Load(content, storePassword);
            X509Certificate cert = store.GetCertificate(NormalizeAlias(alias));
            if (cert == null)
                throw new AliasNotFoundException(alias);
            return cert.GetEncoded();
        }

        internal static JksStore Load(byte[] content, string storePassword)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (storePassword == null) throw new ArgumentNullException(nameof(storePassword));

            var store = new JksStore();
            try
            {
                using (var stream = new MemoryStream(content, false))
                    store.Load(stream, storePassword.ToCharArray());
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is InvalidCastException)
            {
                // Wrong password, tampered data, or truncated / non-JKS content: BouncyCastle reports
                // these as IOException, EndOfStreamException or ArgumentOutOfRangeException.
                throw new InvalidKeystorePasswordException();
            }
            return store;
        }

        /// <summary>Aliases of all private-key entries, sorted ordinally; only the store password is needed.</summary>
        public static IReadOnlyList<string> ListKeyAliases(byte[] content, string storePassword)
        {
            JksStore store = Load(content, storePassword);
            return store.Aliases.Where(store.IsKeyEntry).OrderBy(a => a, StringComparer.Ordinal).ToList();
        }

        internal static string NormalizeAlias(string alias)
        {
            if (string.IsNullOrEmpty(alias)) throw new ArgumentException("The alias is required.", nameof(alias));
            return alias.ToLowerInvariant();
        }
    }
}

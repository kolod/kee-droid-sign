using System;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Security;

namespace KeeDroidSign.Core.Keystore
{
    /// <summary>Changes existing JKS keystores in memory (User Story 3 of feature 003: key rotation).</summary>
    public static class KeystoreEditor
    {
        /// <summary>
        /// Adds a new private key under <c>request.Alias</c>. <c>request.StorePassword</c> must be the
        /// keystore's password. Existing keys stay in their encrypted form and need no passwords.
        /// Throws <see cref="InvalidKeystorePasswordException"/> or <see cref="ArgumentException"/>
        /// when the alias already exists.
        /// </summary>
        public static Task<SigningKeystore> AddKeyAsync(byte[] content, KeyRequest request, CancellationToken ct)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (request == null) throw new ArgumentNullException(nameof(request));
            request.Validate();
            ct.ThrowIfCancellationRequested();

            JksStore store = KeystoreReader.Load(content, request.StorePassword);
            if (store.ContainsAlias(KeystoreReader.NormalizeAlias(request.Alias)))
                throw new ArgumentException($"The keystore already contains the alias '{request.Alias}'.");

            return Task.Run(() => KeyMaterial.AddKey(store, request, ct), ct);
        }
    }
}

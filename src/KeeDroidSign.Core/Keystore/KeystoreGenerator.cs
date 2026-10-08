using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Security;

namespace KeeDroidSign.Core.Keystore
{
    /// <summary>
    /// Generates an RSA key, a self-signed SHA-256 certificate and a JKS keystore entirely in
    /// memory (FR-010, FR-011). No Java or other external tool is required.
    /// </summary>
    public sealed class KeystoreGenerator : IKeystoreGenerator
    {
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

        private static SigningKeystore Generate(KeyRequest request, CancellationToken ct) =>
            KeyMaterial.AddKey(new JksStore(), request, ct);
    }
}

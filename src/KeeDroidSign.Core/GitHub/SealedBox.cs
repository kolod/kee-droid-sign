using System;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace KeeDroidSign.Core.GitHub
{
    /// <summary>
    /// Managed implementation of libsodium <c>crypto_box_seal</c>, the encryption GitHub requires
    /// for Actions secrets. Output: ephemeral public key (32) || Poly1305 tag (16) || ciphertext.
    /// </summary>
    public static class SealedBox
    {
        public const int PublicKeyLength = 32;
        public const int Overhead = 48;

        private const int MacKeyLength = 32;
        private const int NonceLength = 24;

        /// <summary>Encrypts <paramref name="message"/> for the holder of the X25519 private key.</summary>
        public static byte[] Seal(byte[] message, byte[] recipientPublicKey)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (recipientPublicKey == null) throw new ArgumentNullException(nameof(recipientPublicKey));
            if (recipientPublicKey.Length != PublicKeyLength)
                throw new ArgumentException("The recipient public key must be 32 bytes.", nameof(recipientPublicKey));

            var random = new SecureRandom();
            var generator = new X25519KeyPairGenerator();
            generator.Init(new X25519KeyGenerationParameters(random));
            AsymmetricCipherKeyPair ephemeral = generator.GenerateKeyPair();
            byte[] ephemeralPublic = ((X25519PublicKeyParameters)ephemeral.Public).GetEncoded();

            // nonce = BLAKE2b-192(epk || pk)
            var nonce = new byte[NonceLength];
            var blake = new Blake2bDigest(NonceLength * 8);
            blake.BlockUpdate(ephemeralPublic, 0, ephemeralPublic.Length);
            blake.BlockUpdate(recipientPublicKey, 0, recipientPublicKey.Length);
            blake.DoFinal(nonce, 0);

            // k = HSalsa20(X25519(esk, pk), 0^16)  (crypto_box_beforenm)
            var shared = new byte[32];
            var agreement = new X25519Agreement();
            agreement.Init(ephemeral.Private);
            agreement.CalculateAgreement(new X25519PublicKeyParameters(recipientPublicKey, 0), shared, 0);
            byte[] boxKey = HSalsa20.Derive(shared, new byte[16]);

            // crypto_secretbox_xsalsa20poly1305: first 32 keystream bytes key Poly1305, the rest encrypt.
            var result = new byte[Overhead + message.Length];
            var macKey = new byte[MacKeyLength];
            try
            {
                var cipher = new XSalsa20Engine();
                cipher.Init(true, new ParametersWithIV(new KeyParameter(boxKey), nonce));
                cipher.ProcessBytes(new byte[MacKeyLength], 0, MacKeyLength, macKey, 0);
                if (message.Length > 0)
                    cipher.ProcessBytes(message, 0, message.Length, result, Overhead);

                var mac = new Poly1305();
                mac.Init(new KeyParameter(macKey));
                mac.BlockUpdate(result, Overhead, message.Length);
                mac.DoFinal(result, PublicKeyLength);

                Buffer.BlockCopy(ephemeralPublic, 0, result, 0, PublicKeyLength);
                return result;
            }
            finally
            {
                Array.Clear(shared, 0, shared.Length);
                Array.Clear(boxKey, 0, boxKey.Length);
                Array.Clear(macKey, 0, macKey.Length);
            }
        }
    }
}

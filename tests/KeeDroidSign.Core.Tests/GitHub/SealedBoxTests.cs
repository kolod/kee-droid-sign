using System;
using System.Security.Cryptography;
using KeeDroidSign.Core.GitHub;
using Sodium;
using Sodium.Exceptions;
using Xunit;

namespace KeeDroidSign.Core.Tests.GitHub
{
    /// <summary>Cross-checks the managed crypto_box_seal against native libsodium (test-only).</summary>
    public class SealedBoxTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(32)]
        [InlineData(40000)]
        public void Seal_OpensWithLibsodium(int length)
        {
            KeyPair recipient = PublicKeyBox.GenerateKeyPair();
            byte[] message = RandomBytes(length);

            byte[] sealedBox = SealedBox.Seal(message, recipient.PublicKey);

            Assert.Equal(length + 48, sealedBox.Length);
            byte[] opened = SealedPublicKeyBox.Open(sealedBox, recipient.PrivateKey, recipient.PublicKey);
            Assert.Equal(message, opened);
        }

        [Fact]
        public void Seal_IsRandomized()
        {
            KeyPair recipient = PublicKeyBox.GenerateKeyPair();
            byte[] message = RandomBytes(16);

            Assert.NotEqual(SealedBox.Seal(message, recipient.PublicKey), SealedBox.Seal(message, recipient.PublicKey));
        }

        [Fact]
        public void Seal_WrongRecipientCannotOpen()
        {
            KeyPair recipient = PublicKeyBox.GenerateKeyPair();
            KeyPair other = PublicKeyBox.GenerateKeyPair();

            byte[] sealedBox = SealedBox.Seal(RandomBytes(16), recipient.PublicKey);

            Assert.ThrowsAny<Exception>(() => SealedPublicKeyBox.Open(sealedBox, other.PrivateKey, other.PublicKey));
        }

        [Fact]
        public void Seal_TamperedCiphertextIsRejected()
        {
            KeyPair recipient = PublicKeyBox.GenerateKeyPair();
            byte[] sealedBox = SealedBox.Seal(RandomBytes(16), recipient.PublicKey);
            sealedBox[sealedBox.Length - 1] ^= 1;

            Assert.ThrowsAny<CryptographicException>(() => SealedPublicKeyBox.Open(sealedBox, recipient.PrivateKey, recipient.PublicKey));
        }

        [Fact]
        public void Seal_RejectsInvalidArguments()
        {
            Assert.Throws<ArgumentException>(() => SealedBox.Seal(new byte[1], new byte[31]));
            Assert.Throws<ArgumentNullException>(() => SealedBox.Seal(null, new byte[32]));
            Assert.Throws<ArgumentNullException>(() => SealedBox.Seal(new byte[1], null));
        }

        private static byte[] RandomBytes(int length)
        {
            var bytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return bytes;
        }
    }
}
